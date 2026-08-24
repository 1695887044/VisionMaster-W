using System;
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
            await _dialogLock.WaitAsync();
            Window? overlayWindow = null;
            Window? owner = null;
            EventHandler? generalHandler = null;
            SizeChangedEventHandler? sizeHandler = null;

            try
            {
                _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                // 🚨 终极防爆 1：DispatcherPriority.Background 降维打击！
                // 强行把弹窗的创建和渲染排到 WPF 消息队列的最末尾。
                // 等当前所有引发弹窗的 UI 事件（如 ListBox 选中、按钮高亮动画）彻底死透、释放锁之后，再来弹窗！
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    owner = Application.Current.MainWindow;

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

                    overlayWindow.Content = BuildDialogUI(title, message, customContent, isModal);
                    overlayWindow.Show();

                }, DispatcherPriority.Background); // 👈 救命的优先级降级

                // 异步等待用户点击“确定”或“取消”
                return await _tcs.Task;
            }
            finally
            {
                // 清理战场
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
                _tcs = null;
                _dialogLock.Release();
            }
        }

        internal static void SetResult(bool result)
        {
            _tcs?.TrySetResult(result);
        }

        #endregion

        #region ====== UI 动态构建引擎 (纯 C# 零 XAML，使用主题 Style) ======

        private static Border BuildDialogUI(string title, string message, FrameworkElement? customContent, bool isModal)
        {
            // 主卡片背景 (带弥散阴影)
            var card = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(24, 20, 24, 20),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 350,
                MaxWidth = 700,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 25,
                    ShadowDepth = 6,
                    Opacity = 0.15,
                    Direction = 270,
                    Color = Colors.Black
                },
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
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#303133")),
                Margin = new Thickness(0, 0, 0, 12),
            };
            Grid.SetRow(txtTitle, 0);
            grid.Children.Add(txtTitle);

            // 分割线
            var line = new Rectangle
            {
                Height = 1,
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EBEEF5")),
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
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#606266")),
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
            var cancelStyle = Application.Current.TryFindResource("FlatButtonStyle") as Style;
            if (cancelStyle != null) btnCancel.Style = cancelStyle;

            var cancelContent = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            cancelContent.Children.Add(new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12 19 6.41z"), // 专业的“取消”图标
                Fill = (SolidColorBrush)Application.Current.FindResource("TextRegular"),
                Width = 12,
                Height = 12,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 0, 8, 0)
            });
            cancelContent.Children.Add(new TextBlock { Text = "取 消" });
            btnCancel.Content = cancelContent;
            btnCancel.Click += (s, e) => SetResult(false);
            btnPanel.Children.Add(btnCancel);


            // 🌟 2. “确定”按钮：寻找定义的高亮蓝 Style
            var btnConfirm = new Button();
            var confirmStyle = Application.Current.TryFindResource("FlatButtonVariantStyle") as Style;
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
            btnConfirm.Click += (s, e) => SetResult(true);
            btnPanel.Children.Add(btnConfirm);

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

                _ = asyncMethod().ContinueWith(t =>
                {
                    result = t.Result;
                    frame.Continue = false;
                }, TaskScheduler.Default);

                Dispatcher.PushFrame(frame);
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
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DCDFE6")),
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