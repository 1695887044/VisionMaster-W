using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using UI.CustomControl.PropertyGrid;

namespace UI.CustomControl
{
    public static class EasyDialog
    {
        // 保证全局同一时刻只有一个弹窗
        private static readonly SemaphoreSlim _dialogLock = new(1, 1);
        private static TaskCompletionSource<bool>? _tcs;

        #region ====== 核心引擎 (解决跨线程与并发) ======

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

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    owner = Application.Current.MainWindow;

                    overlayWindow = new Window
                    {
                        WindowStyle = WindowStyle.None,
                        AllowsTransparency = true,
                        Background = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0)),
                        ShowInTaskbar = false,
                        Owner = owner
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
                });

                return await _tcs.Task;
            }
            finally
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
                _tcs = null;
                _dialogLock.Release();
            }
        }

        internal static void SetResult(bool result)
        {
            _tcs?.TrySetResult(result);
        }

        #endregion

        #region ====== UI 动态构建引擎 (纯 C# 绘制，零 XAML) ======

        private static Border BuildDialogUI(
            string title,
            string message,
            FrameworkElement? customContent,
            bool isModal
        )
        {
            // 主卡片背景
            var card = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth = 350,
                MaxWidth = 600,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 20,
                    ShadowDepth = 5,
                    Opacity = 0.2,
                    Direction = 270,
                },
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 标题
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 分割线
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 内容区
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 按钮区

            // 标题
            var txtTitle = new TextBlock
            {
                Text = title,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#303133")
                ),
                Margin = new Thickness(0, 0, 0, 10),
            };
            Grid.SetRow(txtTitle, 0);
            grid.Children.Add(txtTitle);

            // 分割线
            var line = new Rectangle
            {
                Height = 1,
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EBEEF5")),
                Margin = new Thickness(0, 0, 0, 15),
            };
            Grid.SetRow(line, 1);
            grid.Children.Add(line);

            // 内容区 (文本 或 自定义控件)
            FrameworkElement contentElement;
            if (customContent != null)
            {
                contentElement = customContent;
                contentElement.Margin = new Thickness(0, 0, 0, 20);
            }
            else
            {
                contentElement = new TextBlock
                {
                    Text = message,
                    FontSize = 14,
                    Foreground = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString("#606266")
                    ),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 25),
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

            // 取消按钮 (如果是模态且允许取消)
            var btnCancel = new Button
            {
                Content = "取消",
                Padding = new Thickness(20, 8, 20, 8),
                Margin = new Thickness(0, 0, 10, 0),
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#DCDFE6")
                ),
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            // 纯代码设置圆角样式
            btnCancel.Resources.Add(
                typeof(Border),
                new Style(typeof(Border))
                {
                    Setters = { new Setter(Border.CornerRadiusProperty, new CornerRadius(4)) },
                }
            );
            btnCancel.Click += (s, e) => SetResult(false);

            // 确定按钮
            var btnConfirm = new Button
            {
                Content = "确定",
                Padding = new Thickness(20, 8, 20, 8),
                Background = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#409EFF")
                ),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.SemiBold,
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            btnConfirm.Resources.Add(
                typeof(Border),
                new Style(typeof(Border))
                {
                    Setters = { new Setter(Border.CornerRadiusProperty, new CornerRadius(4)) },
                }
            );
            btnConfirm.Click += (s, e) => SetResult(true);

            btnPanel.Children.Add(btnCancel);
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

                _ = asyncMethod()
                    .ContinueWith(
                        t =>
                        {
                            result = t.Result;
                            frame.Continue = false;
                        },
                        TaskScheduler.Default
                    );

                System.Windows.Threading.Dispatcher.PushFrame(frame);
                return result;
            }
            else
            {
                return asyncMethod().GetAwaiter().GetResult();
            }
        }

        #endregion

        #region ====== 1. 标准文本提示框 ======

        public static Task<bool> ShowAsync(string title, string message, bool isModal = false) =>
            InternalExecuteAsync(title, message, null, isModal);

        public static bool ShowSync(string title, string message, bool isModal = false) =>
            RunSync(() => ShowAsync(title, message, isModal));

        #endregion

        #region ====== 2. 自定义控件弹窗 ======

        public static Task<bool> ShowCustomAsync(
            string title,
            FrameworkElement customContent,
            bool isModal = false
        ) => InternalExecuteAsync(title, string.Empty, customContent, isModal);

        public static bool ShowSync(
            string title,
            FrameworkElement customContent,
            bool isModal = false
        ) => RunSync(() => ShowCustomAsync(title, customContent, isModal));

        #endregion

        #region ====== 3. 文本输入弹窗 ======

        public static async Task<(bool IsConfirmed, string Value)> ShowTextInputAsync(
            string title,
            string defaultValue = ""
        )
        {
            var textBox = await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var tb = new TextBox
                {
                    Text = defaultValue,
                    FontSize = 14,
                    Padding = new Thickness(8),
                    Margin = new Thickness(5),
                    MinWidth = 300,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    BorderBrush = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString("#DCDFE6")
                    ),
                };
                tb.Loaded += (s, e) =>
                {
                    tb.SelectAll();
                    tb.Focus();
                };
                return tb;
            });

            bool isConfirmed = await ShowCustomAsync(title, textBox, true);
            string finalValue = await Application.Current.Dispatcher.InvokeAsync(() =>
                textBox.Text
            );
            return (isConfirmed, finalValue);
        }

        public static (bool IsConfirmed, string Value) ShowTextInputSync(
            string title,
            string defaultValue = ""
        ) => RunSync(() => ShowTextInputAsync(title, defaultValue));

        #endregion
        public static Task<bool> ShowPropertyGridAsync(string title, object targetObject)
        {

            return Application.Current.Dispatcher.Invoke(() =>
            {
                var propertyGrid = new FlatPropertyGrid
                {
                    BindingObject = targetObject,

                    MinWidth = 450,
                };

                return ShowCustomAsync(title, propertyGrid, true);
            });
        }
        public static bool ShowPropertyGridSync(string title, object targetObject) =>
            RunSync(() => ShowPropertyGridAsync(title, targetObject));
    }
}
