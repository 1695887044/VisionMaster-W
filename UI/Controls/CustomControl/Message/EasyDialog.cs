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
        private static TaskCompletionSource<bool>? _tcs;

        #region ====== 核心引擎 (解决跨线程、并发与 WPF 调度器挂起) ======

        /// <summary>
        /// 核心异步调度引擎：动态生成透明遮罩窗体
        /// </summary>
        private static async Task<bool> InternalExecuteAsync(string title, string message, FrameworkElement? customContent, bool isModal)
        {
            // 没有 WPF 应用上下文（设计器 / 单元测试 / 已关机）就没有可承载的窗口：
            // 这里返回 false（= 用户取消），而不是让下面的 Dispatcher 访问抛 NRE。
            if (Application.Current == null) return false;

            await _dialogLock.WaitAsync();
            Window? overlayWindow = null;
            Window? owner = null;
            EventHandler? generalHandler = null;
            SizeChangedEventHandler? sizeHandler = null;

            // ★ tcs 同时以局部变量和静态字段存在：
            //   局部变量给本窗口的按钮/关闭事件用（闭包捕获，永不串台）；
            //   静态字段只为了让 EasyDialog.SetResult（OverlayHost 等外部承载控件）也能收口到"当前"这一个。
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

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
                    owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                            ?? Application.Current.MainWindow;

                    overlayWindow = new Window
                    {
                        WindowStyle = WindowStyle.None,
                        AllowsTransparency = true,
                        Background = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0)),
                        ShowInTaskbar = false,
                        Owner = owner,
                        WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.Manual
                    };

                    Action syncPosition = () =>
                    {
                        if (owner != null && overlayWindow != null)
                        {
                            overlayWindow.Left = owner.Left;
                            overlayWindow.Top = owner.Top;
                            overlayWindow.Width = owner.ActualWidth;
                            overlayWindow.Height = owner.ActualHeight;
                        }
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

                    // ★ 窗口被"非按钮方式"关掉时必须收口成取消：
                    //   Owner 关闭 / 系统菜单 / 任何外部 Close 都会走到这里。
                    //   没有它，_tcs 永远不完成 → await 永久挂起 → finally 不执行 →
                    //   _dialogLock 不释放 → 之后**所有**弹窗永久失效（整个 EasyDialog 一次性死掉）。
                    overlayWindow.Closed += (s, e) => tcs.TrySetResult(false);

                    // Esc = 取消（PreviewKeyDown：先于内部控件处理，属性框里的键盘操作不受影响）
                    overlayWindow.PreviewKeyDown += (s, e) =>
                    {
                        if (e.Key == System.Windows.Input.Key.Escape)
                        {
                            tcs.TrySetResult(false);
                            e.Handled = true;
                        }
                    };

                    overlayWindow.Content = BuildDialogUI(title, message, customContent, isModal, tcs);
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

        internal static void SetResult(bool result)
        {
            _tcs?.TrySetResult(result);
        }

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

        #endregion

        #region ====== UI 动态构建引擎 (纯 C# 零 XAML，使用主题 Style) ======

        private static Border BuildDialogUI(
            string title, string message, FrameworkElement? customContent, bool isModal, TaskCompletionSource<bool> tcs)
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
            btnCancel.Click += (s, e) => tcs.TrySetResult(false);
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
            btnConfirm.Click += (s, e) => tcs.TrySetResult(true);
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

        public static Task<bool> ShowAsync(string title, string message, bool isModal = true) =>
            InternalExecuteAsync(title, message, null, isModal);

        public static bool ShowSync(string title, string message, bool isModal = true) =>
            RunSync(() => ShowAsync(title, message, isModal));

        #endregion

        #region ====== 2. 自定义控件弹窗 ======

        public static Task<bool> ShowCustomAsync(string title, FrameworkElement customContent, bool isModal = true) =>
            InternalExecuteAsync(title, string.Empty, customContent, isModal);

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