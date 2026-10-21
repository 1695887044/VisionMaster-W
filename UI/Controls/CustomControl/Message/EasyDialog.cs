using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace UI.CustomControl
{
    /// <summary>
    /// 🌟 工业级全局异步/同步弹窗引擎 (纯 C# 零 XAML)
    /// 具备顶级防爆机制，彻底解决 WPF 调度器挂起和跨线程死锁问题。
    /// </summary>
    public static class EasyDialog
    {
        // 保证全局同一时刻只有一个弹窗
        private static readonly SemaphoreSlim _dialogLock = new(1, 1);
        private static TaskCompletionSource<MessageResult>? _tcs;

        #region ====== 核心引擎 (解决跨线程、并发与 WPF 调度器挂起) ======

        /// <summary>
        /// 核心异步调度引擎：动态生成透明遮罩窗体。
        ///
        /// 返回值从 bool 扩成 <see cref="MessageResult"/>（Primary / Secondary / Close）：
        /// 旧的消息调用仍按「Primary = true」映射回 bool，调用点零改动；
        /// 新的 <see cref="MessageDialog"/> 直接拿到三态结果。
        /// </summary>
        private static async Task<MessageResult> InternalExecuteAsync(
            string title,
            string message,
            FrameworkElement? customContent,
            bool isModal,
            MessageDialogOptions? messageOptions = null)
        {
            // 没有 WPF 应用上下文（设计器 / 单元测试 / 已关机）就没有可承载的窗口：
            // 这里返回 Close，而不是让下面的 Dispatcher 访问抛 NRE。
            if (Application.Current == null) return MessageResult.Close;

            await _dialogLock.WaitAsync();
            Window? overlayWindow = null;
            Window? owner = null;
            EventHandler? generalHandler = null;
            SizeChangedEventHandler? sizeHandler = null;

            // ★ tcs 同时以局部变量和静态字段存在：
            //   局部变量给本窗口的按钮/关闭事件用（闭包捕获，永不串台）；
            //   静态字段只为了让 EasyDialog.SetResult（OverlayHost 等外部承载控件）也能收口到"当前"这一个。
            var tcs = new TaskCompletionSource<MessageResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                _tcs = tcs;

                // 🚨 终极防爆 1：DispatcherPriority.Background 降维打击！
                // 强行把弹窗的创建和渲染排到 WPF 消息队列的最末尾。
                // 等当前所有引发弹窗的 UI 事件（如 ListBox 选中、按钮高亮动画）彻底死透、释放锁之后，再来弹窗！
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // Owner 取当前激活窗口：从对话框（如变量管理）触发时弹窗跟随该窗口，
                    // 保证 Z 序在其之上且遮罩覆盖其范围；无激活窗口时回退主窗口。
                    // （原来固定绑 MainWindow，导致对话框内弹窗被对话框自身遮挡）
                    // MessageDialogOptions.Owner 显式给了就以它为准。
                    owner = messageOptions?.Owner
                            ?? Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                            ?? Application.Current.MainWindow;

                    overlayWindow = new Window
                    {
                        WindowStyle = WindowStyle.None,
                        AllowsTransparency = true,
                        // 设计基线：rgba(15,23,42,.44) → #700F172A（112/255 ≈ .439）
                        Background = new SolidColorBrush(Color.FromArgb(112, 15, 23, 42)),
                        ShowInTaskbar = false,
                        Owner = owner,
                        WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.Manual
                    };

                    Action syncPosition = () =>
                    {
                        if (owner == null || overlayWindow == null) return;

                        if (TryGetOwnerScreenRect(owner, out var screenRect))
                        {
                            overlayWindow.Left = screenRect.Left;
                            overlayWindow.Top = screenRect.Top;
                            overlayWindow.Width = screenRect.Width;
                            overlayWindow.Height = screenRect.Height;
                            return;
                        }

                        // 退化路径：窗口还没连上 PresentationSource 时沿用旧口径。
                        // 最大化状态下这条会偏移（见 TryGetOwnerScreenRect 的注释），
                        // 但"遮罩位置略歪"远好过"遮罩尺寸非法导致弹窗打不开"。
                        overlayWindow.Left = owner.Left;
                        overlayWindow.Top = owner.Top;
                        overlayWindow.Width = owner.ActualWidth;
                        overlayWindow.Height = owner.ActualHeight;
                    };

                    generalHandler = (s, e) => syncPosition();
                    sizeHandler = (s, e) => syncPosition();

                    if (owner != null)
                    {
                        syncPosition(); // 初始化时对齐一次
                        owner.LocationChanged += generalHandler;
                        owner.StateChanged += generalHandler;
                        owner.SizeChanged += sizeHandler;
                    }

                    // ★ 窗口被"非按钮方式"关掉时必须收口成 Close：
                    //   Owner 关闭 / 系统菜单 / 任何外部 Close 都会走到这里。
                    //   没有它，_tcs 永远不完成 → await 永久挂起 → finally 不执行 →
                    //   _dialogLock 不释放 → 之后**所有**弹窗永久失效（整个 EasyDialog 一次性死掉）。
                    overlayWindow.Closed += (s, e) => tcs.TrySetResult(MessageResult.Close);

                    // 消息卡片的键盘契约在宿主窗口这一层收口：
                    //   Esc → 次操作；Y → 默认按钮；N → 次操作；Enter → 默认按钮。
                    //   Y 不能无条件走主操作：危险弹窗的默认焦点在取消，Y 也必须取消。
                    // 只有卡片是 MessageDialogView 时才转发；自定义内容（属性表格 / 文本输入）
                    // 保持旧行为（只接 Esc），否则会抢走输入框里的 Y/N/Enter。
                    MessageDialogView? messageView = null;
                    overlayWindow.PreviewKeyDown += (s, e) =>
                    {
                        if (e.Key == System.Windows.Input.Key.Escape)
                        {
                            if (messageView != null) messageView.RequestSecondary();
                            else tcs.TrySetResult(MessageResult.Secondary);
                            e.Handled = true;
                            return;
                        }

                        if (messageView == null) return;

                        if (e.Key == System.Windows.Input.Key.Y)
                        {
                            // Y 跟随默认按钮，不直接触发主操作：危险弹窗的默认按钮是「取消」，
                            // 无条件 RequestPrimary 会让操作员按 Y 就把停止/删除执行掉。
                            messageView.RequestDefault();
                            e.Handled = true;
                        }
                        else if (e.Key == System.Windows.Input.Key.N)
                        {
                            messageView.RequestSecondary();
                            e.Handled = true;
                        }
                        else if (e.Key == System.Windows.Input.Key.Enter)
                        {
                            // 焦点已经在某颗按钮上时，让这颗按钮自己收 Enter：
                            // 否则危险场景 Tab 到「删除分组」后按 Enter 会被默认焦点（取消）抢走，
                            // 键盘用户永远触发不了红色主按钮。
                            if (System.Windows.Input.Keyboard.FocusedElement is Button)
                                return;

                            // 默认按钮由视图解析（危险场景 = 取消），Enter 不直接执行危险动作
                            messageView.RequestDefault();
                            e.Handled = true;
                        }
                    };

                    var content = CreateDialogContent(title, message, customContent, messageOptions, tcs);
                    messageView = content as MessageDialogView;
                    overlayWindow.Content = content;
                    overlayWindow.Show();

                }, DispatcherPriority.Background); // 👈 救命的优先级降级

                // 异步等待用户点击“确定”或“取消”（或 Esc / 窗口被关闭）
                return await tcs.Task;
            }
            finally
            {
                // 清理战场：这里**任何一步抛异常都会导致锁不释放**（弹窗系统整体卡死），
                // 所以整段包 try/catch —— 应用正在关闭时 Dispatcher 回调本身就可能抛。
                try
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (owner != null)
                        {
                            if (generalHandler != null)
                            {
                                owner.LocationChanged -= generalHandler;
                                owner.StateChanged -= generalHandler;
                            }
                            if (sizeHandler != null)
                            {
                                owner.SizeChanged -= sizeHandler;
                            }
                        }
                        overlayWindow?.Close();
                    });
                }
                catch
                {
                    // 清理失败也要把锁放掉，不能让一次异常锁死后续所有弹窗
                }

                _tcs = null;
                _dialogLock.Release();
            }
        }

        /// <summary>
        /// 外部承载控件（OverlayHost 等）收口结果：true = Primary，false = Secondary。
        /// 旧语义「确定 / 取消」保持不变。
        /// </summary>
        internal static void SetResult(bool result)
            => _tcs?.TrySetResult(result ? MessageResult.Primary : MessageResult.Secondary);

        /// <summary>
        /// 取主题资源；宿主没合并 Fluent 主题（或根本不在 WPF 应用里）时返回 null，
        /// 由调用方回退到旧值 —— 纯 C# 构造的 UI 没有 XAML 的"资源缺失即异常"保护，
        /// 直接 FindResource 会在没主题的宿主里当场抛。
        /// </summary>
        private static T? TryResource<T>(string key)
        {
            try
            {
                var value = Application.Current?.TryFindResource(key);
                return value is T typed ? typed : default;
            }
            catch
            {
                return default;
            }
        }

        /// <summary>
        /// 取 owner 在屏幕上的**真实**矩形（DIP），供遮罩窗口对齐。
        ///
        /// ★ 不能再用 <c>owner.Left/Top</c>：窗口最大化时那两个属性返回的是**还原位置**
        ///   （最大化之前的位置），而 <c>ActualWidth/Height</c> 返回的是**最大化后的尺寸**。
        ///   两者不同源 → 遮罩整体右下偏移：2026-10-21 真机截图逐像素实测偏移 (304,304) px
        ///   （左栏与顶栏没被压暗、卡片不居中、遮罩右下溢出屏幕）；更糟的是没被遮罩盖住的
        ///   区域仍然能点，假模态被穿透 —— 弹窗开着还能触发主窗体命令。
        ///
        /// ★ 正解：PointToScreen 取客户区左上/右下（设备像素），再按**该窗口自己的**
        ///   HwndSource 变换折回 DIP。最大化 / 普通 / 拖动 / 改尺寸四种状态同一个口径，
        ///   而且不依赖 WindowState 分支（分支写法在"最大化过程中 SizeChanged 还没到"时仍会抖）。
        ///
        /// 返回 false 表示"现在量不出来"（没连上 PresentationSource / 还没布局 / 窗口正在销毁），
        /// 由调用方退回旧口径 —— 负宽高的窗口会让 WPF 在 Show() 时抛 ArgumentException，
        /// 那等于"弹窗打不开"，比位置歪严重得多。
        /// </summary>
        private static bool TryGetOwnerScreenRect(Window owner, out Rect rect)
        {
            rect = Rect.Empty;

            try
            {
                var source = PresentationSource.FromVisual(owner);
                if (source?.CompositionTarget == null) return false;

                // 设备像素 → DIP：跨 DPI 缩放（125%/150%）下直接拿 PointToScreen 的像素值
                // 赋给 Left/Top 会再被放大一次，偏移量随缩放比例变化。
                Matrix fromDevice = source.CompositionTarget.TransformFromDevice;

                Point topLeft = fromDevice.Transform(owner.PointToScreen(new Point(0, 0)));
                Point bottomRight = fromDevice.Transform(
                    owner.PointToScreen(new Point(owner.ActualWidth, owner.ActualHeight)));

                double width = bottomRight.X - topLeft.X;
                double height = bottomRight.Y - topLeft.Y;

                if (!IsFinite(topLeft.X) || !IsFinite(topLeft.Y) || !IsFinite(width) || !IsFinite(height))
                    return false;
                if (width <= 0 || height <= 0) return false;

                rect = new Rect(topLeft.X, topLeft.Y, width, height);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        #endregion

        #region ====== UI 动态构建引擎 (纯 C# 零 XAML，使用主题 Style) ======

        /// <summary>
        /// 生成弹窗内容：
        ///   · 自定义内容（属性表格 / 文本输入）→ 旧卡片，行为不变；
        ///   · 消息弹窗（含旧 ShowAsync(title,message)）→ MessageDialogView。
        /// 视图创建失败（宿主没合并 Dialog.Message.xaml / 资源断链）时退回旧卡片，
        /// 保证"弹窗打不开"不会成为新的失败面。
        /// </summary>
        private static FrameworkElement CreateDialogContent(
            string title,
            string message,
            FrameworkElement? customContent,
            MessageDialogOptions? messageOptions,
            TaskCompletionSource<MessageResult> tcs)
        {
            if (customContent != null)
                return BuildDialogUI(title, message, customContent, tcs);

            var options = messageOptions ?? BuildLegacyOptions(title, message);
            try
            {
                var view = new MessageDialogView { Options = options };
                view.ResultRequested += (_, result) => tcs.TrySetResult(result);
                return view;
            }
            catch (Exception ex)
            {
                // 资源断链 / XAML 解析失败时静默退回旧卡片会让现场只看到"弹窗变旧了"，
                // 无法定位是哪本字典断了；至少留一条调试输出（EasyDialog 不引日志服务）。
                System.Diagnostics.Debug.WriteLine(
                    "[EasyDialog] MessageDialogView 创建失败，回退旧卡片：" + ex);
                return BuildDialogUI(options.Title, options.Message, null, tcs);
            }
        }

        /// <summary>
        /// 旧签名 ShowAsync(title, message) 的默认文案：保留原来的「确定 / 取消」，
        /// 调用点不用改文案就能得到新卡片。
        /// </summary>
        private static MessageDialogOptions BuildLegacyOptions(string title, string message) => new()
        {
            Severity = MessageSeverity.Info,
            Title = title ?? string.Empty,
            Message = message ?? string.Empty,
            PrimaryText = "确定",
            SecondaryText = "取消",
        };

        private static Border BuildDialogUI(
            string title, string message, FrameworkElement? customContent, TaskCompletionSource<MessageResult> tcs)
        {
            // ★ 视觉一律取 Fluent 令牌（UI 库的 Themes/Fluent/FluentTokens.xaml），
            //   不再自己硬编码白底 / 8px 圆角 / 25 模糊的阴影：
            //   硬编码的一份会让"弹窗"成为唯一不跟随设计系统的角落
            //   （改了令牌，全项目都变了，只有弹窗没变 —— 这类不一致最难被发现）。
            //   取不到令牌时回退到旧值：本控件库也可能被没有合并 Fluent 主题的宿主使用。
            var cardBackground = TryResource<Brush>("FluentCardBackgroundBrush") ?? Brushes.White;
            var cardRadius = TryResource<CornerRadius>("FluentRadiusLarge");
            var cardShadow = TryResource<Effect>("FluentShadowOverlay")
                             ?? new DropShadowEffect
                             {
                                 BlurRadius = 25,
                                 ShadowDepth = 6,
                                 Opacity = 0.15,
                                 Direction = 270,
                                 Color = Colors.Black
                             };
            var divider = TryResource<Brush>("FluentDividerBrush")
                          ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EBEEF5"));
            // 字色也走令牌：原来标题/正文是硬编码 #303133 / #606266，
            // 导致"改了令牌全项目都变、只有弹窗不变"——正是上面那段注释要避免的不一致
            var titleBrush = TryResource<Brush>("FluentTextPrimaryBrush")
                             ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#303133"));
            var messageBrush = TryResource<Brush>("FluentTextSecondaryBrush")
                               ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#606266"));

            // 主卡片背景 (带弥散阴影)
            var card = new Border
            {
                Background = cardBackground,
                CornerRadius = cardRadius == default ? new CornerRadius(8) : cardRadius,
                Padding = new Thickness(24, 20, 24, 20),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 350,
                MaxWidth = 700,
                Effect = cardShadow,
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 标题
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 分割线
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 内容区
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 按钮区

            // 标题
            var txtTitle = new TextBlock
            {
                Text = title,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = titleBrush,
                Margin = new Thickness(0, 0, 0, 12),
            };
            Grid.SetRow(txtTitle, 0);
            grid.Children.Add(txtTitle);

            // 分割线
            var line = new Rectangle
            {
                Height = 1,
                Fill = divider,
                Margin = new Thickness(0, 0, 0, 16),
            };
            Grid.SetRow(line, 1);
            grid.Children.Add(line);

            // 内容区
            FrameworkElement contentElement;
            if (customContent != null)
            {
                contentElement = customContent;
                contentElement.Margin = new Thickness(0, 0, 0, 24);
            }
            else
            {
                contentElement = new TextBlock
                {
                    Text = message,
                    FontSize = 14,
                    Foreground = messageBrush,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 24),
                };
            }
            Grid.SetRow(contentElement, 2);
            grid.Children.Add(contentElement);

            // 按钮区
            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            Grid.SetRow(btnPanel, 3);

            // 🌟 1. “取消”按钮：寻找定义的扁平次要 Style
            var btnCancel = new Button();
            // ★ 一律走 TryResource（带异常兜底）：直接 TryFindResource 时，
            //   目标样式若因依赖缺失而无法创建（BasedOn 解析失败），异常会一路抛出
            //   把整个弹窗带崩 —— 而"少一个按钮样式"远没有"弹窗打不开"严重。
            var cancelStyle = TryResource<Style>("FlatButtonStyle");
            if (cancelStyle != null) btnCancel.Style = cancelStyle;

            var cancelContent = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            cancelContent.Children.Add(new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12 19 6.41z"), // 专业的“取消”图标
                // ★ 这一处原来直接 FindResource（同文件其它 8 处都走 TryResource）：
                //   键一旦不在（换主题 / 宿主没合并那本字典）就是 KeyNotFoundException，
                //   整个弹窗引擎当场崩 —— "少一个图标色"远没有"弹窗打不开"严重。
                Fill = TryResource<Brush>("TextRegular") ?? Brushes.DimGray,
                Width = 12,
                Height = 12,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 0, 8, 0)
            });
            cancelContent.Children.Add(new TextBlock { Text = "取 消" });
            btnCancel.Content = cancelContent;
            btnCancel.Click += (s, e) => tcs.TrySetResult(MessageResult.Secondary);
            btnPanel.Children.Add(btnCancel);


            // 🌟 2. “确定”按钮：寻找定义的高亮蓝 Style
            var btnConfirm = new Button();
            var confirmStyle = TryResource<Style>("FlatButtonVariantStyle");
            if (confirmStyle != null) btnConfirm.Style = confirmStyle;

            var confirmContent = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            confirmContent.Children.Add(new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M9 16.2L4.8 12l-1.4 1.4L9 19 21 7l-1.4-1.4L9 16.2z"), // 专业的“确定”图标
                Fill = Brushes.White,
                Width = 14,
                Height = 14,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 0, 8, 0)
            });
            confirmContent.Children.Add(new TextBlock { Text = "确 定" });
            btnConfirm.Content = confirmContent;
            btnConfirm.Click += (s, e) => tcs.TrySetResult(MessageResult.Primary);
            btnPanel.Children.Add(btnConfirm);

            // 默认焦点给「确定」：键盘用户打开即可回车，不必先 Tab 过去
            // （只在没有可输入控件时才抢焦点，否则会打断输入框的光标）
            btnConfirm.Loaded += (s, e) =>
            {
                if (customContent == null)
                    btnConfirm.Focus();
            };

            grid.Children.Add(btnPanel);

            card.Child = grid;
            return card;
        }

        #endregion

        #region ====== 同步转换器 (黑科技：安全阻塞 UI) ======

        private static T RunSync<T>(Func<Task<T>> asyncMethod)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && dispatcher.CheckAccess())
            {
                var frame = new DispatcherFrame();
                T result = default!;
                Exception? failure = null;

                _ = asyncMethod().ContinueWith(t =>
                {
                    // ★ frame.Continue 必须放在 finally 里放行。
                    //   原来写成 `result = t.Result; frame.Continue = false;`：一旦任务失败，
                    //   t.Result 会在这里抛出，后面那行永远执行不到 —— UI 线程就永久卡死在
                    //   下面的 PushFrame 里，整个弹窗系统死锁（连"取消失败"都弹不出来）。
                    try
                    {
                        result = t.Result;
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }
                    finally
                    {
                        frame.Continue = false;
                    }
                }, TaskScheduler.Default);

                Dispatcher.PushFrame(frame);

                // 保持原来的 AggregateException 语义：失败照常往外抛，只是不再把 UI 卡住
                if (failure != null)
                    throw failure;

                return result;
            }
            return asyncMethod().GetAwaiter().GetResult();
        }

        #endregion

        #region ====== 1. 标准文本提示框 ======

        /// <summary>
        /// 旧签名：返回 bool（Primary = true）。调用点零改动，
        /// 内部已换成 MessageDialogView 的新卡片。
        /// </summary>
        public static async Task<bool> ShowAsync(string title, string message, bool isModal = true)
            => await InternalExecuteAsync(title, message, null, isModal) == MessageResult.Primary;

        public static bool ShowSync(string title, string message, bool isModal = true) =>
            RunSync(() => ShowAsync(title, message, isModal));

        /// <summary>公共消息弹窗（三态结果）：Primary / Secondary / Close。</summary>
        public static Task<MessageResult> ShowMessageAsync(MessageDialogOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            return InternalExecuteAsync(options.Title, options.Message, null, true, options);
        }

        public static MessageResult ShowMessageSync(MessageDialogOptions options)
            => RunSync(() => ShowMessageAsync(options));

        #endregion

        #region ====== 2. 自定义控件弹窗 ======

        /// <summary>
        /// 自定义内容（属性表格 / 文本输入）仍走旧卡片，行为不变；
        /// 只有消息弹窗换成 MessageDialogView。
        /// </summary>
        public static async Task<bool> ShowCustomAsync(string title, FrameworkElement customContent, bool isModal = true)
            => await InternalExecuteAsync(title, string.Empty, customContent, isModal) == MessageResult.Primary;

        public static bool ShowSync(string title, FrameworkElement customContent, bool isModal = true) =>
            RunSync(() => ShowCustomAsync(title, customContent, isModal));

        #endregion

        #region ====== 3. 文本输入弹窗 ======

        public static async Task<(bool IsConfirmed, string Value)> ShowTextInputAsync(string title, string defaultValue = "")
        {
            var textBox = await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var tb = new TextBox
                {
                    Text = defaultValue,
                    FontSize = 14,
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 5, 0, 5),
                    MinWidth = 300,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    BorderBrush = TryResource<Brush>("FluentBorderStrongBrush")
                                  ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DCDFE6")),
                };
                tb.Loaded += (s, e) => { tb.SelectAll(); tb.Focus(); };
                return tb;
            }, DispatcherPriority.Background);

            bool isConfirmed = await ShowCustomAsync(title, textBox, true);
            string finalValue = await Application.Current.Dispatcher.InvokeAsync(() => textBox.Text);
            return (isConfirmed, finalValue);
        }

        public static (bool IsConfirmed, string Value) ShowTextInputSync(string title, string defaultValue = "") =>
            RunSync(() => ShowTextInputAsync(title, defaultValue));

        #endregion

        #region ====== 4. 属性表格弹窗 (极限防爆版) ======

        /// <summary>
        /// 异步呼出 FlatPropertyGrid 弹窗 (完美避开 WPF 路由死锁)
        /// </summary>
        public static async Task<bool> ShowPropertyGridAsync(string title, object targetObject)
        {

            var propertyGrid = await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                return new FlatPropertyGrid
                {
                    BindingObject = targetObject,
                    MinWidth = 450,
                    MaxHeight =600
                };
            }, DispatcherPriority.Background);

            // 3. 呼出弹窗
            return await ShowCustomAsync(title, propertyGrid, true);
        }

        /// <summary>
        /// 同步呼出 PropertyGrid。
        /// ⚠️ 警告：极不推荐在 ListView.SelectionChanged 等敏感路由事件中使用！如果必须使用，请改用 Async 方案。
        /// </summary>
        public static bool ShowPropertyGridSync(string title, object targetObject) =>
            RunSync(() => ShowPropertyGridAsync(title, targetObject));

        #endregion
    }
}