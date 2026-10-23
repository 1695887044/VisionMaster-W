using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;
using VisionMaster.Services;

namespace VisionMaster.Views
{
    /// <summary>
    /// ScadaRuntimeWindow.xaml 的交互逻辑（组态运行态窗口壳子）
    ///
    /// 这个类里只允许两类代码：一是"把会话里的这一页摆正"（取景），
    /// 二是"把只属于窗口自己的显示数字写回状态条"（视口尺寸、适配倍率、加载耗时）。
    /// 运行策略一条都不写在这里——单实例、什么时候该停、日志记什么，全在
    /// <see cref="Services.IScadaRuntimeHost"/>。窗口被 Show 出来就说明该显示了，
    /// 它不需要、也不该知道"我是被谁叫起来的"。
    /// </summary>
    public partial class ScadaRuntimeWindow : Window
    {
        /// <summary>构造后 DataContext 就是这个会话；本窗口只读 <c>CurrentPage</c> 摆画布</summary>
        private readonly ScadaRuntime _session;

        /// <summary>
        /// 当前打开的页面菜单。必须由窗口持有并在关闭时收掉：
        /// ContextMenu 在独立 Popup 视觉树里，不随窗口关闭自动消失；它的 Click 处理器
        /// 又指向本窗口实例——留着就是"菜单反向钉住已关闭的窗口/会话/Document"。
        /// </summary>
        private ContextMenu? _pageMenu;

        public ScadaRuntimeWindow(ScadaRuntime session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            InitializeComponent();

            // DataContext 由宿主交来的会话充当：绑定路径只有一条 CurrentPage.*，
            // 不再套一层视图模型去转发——转一次就得同步一次，多一处漏改的机会。
            DataContext = _session;
            _session.PropertyChanged += OnSessionPropertyChanged;
            AttachPopupPlacement();

            // 组态事件的接线在宿主（ScadaRuntimeHost）的窗口根上：AddHandler/RemoveHandler
            // 成对收尾，全程序只有一处订阅。这里不再挂第二座桥——
            // 路由事件按委托逐个回调，两座桥会让同一钩子的动作被执行两遍。
            Loaded += OnWindowLoaded;

            // 关闭时摘掉报警面板对引擎的订阅。本处理器是在构造函数里挂的，而宿主的收尾处理器
            // 挂在 Start()（更晚），所以这里的执行顺序<b>早于</b> TeardownAlarms——
            // 正好符合"订阅方先退场，再拆被订阅的东西"这条顺序。
            Closed += OnWindowClosed;

            // 软键盘：输入控件进入/离开编辑态时冒泡到这里，窗口一处决定弹不弹。
            AddHandler(ScadaElementBase.EditStateChangedEvent, new RoutedEventHandler(OnEditStateChanged));
        }

        /// <summary>
        /// 画面画布。运行态数据泵要按它反查"图元 → 控件"才能建表，宿主从这里取。
        ///
        /// 为什么不把画布做成 XAML 里的公开字段就算了：<c>x:Name</c> 生成的是 internal 字段，
        /// 宿主虽然同程序集能用，但那是"恰好能用"——把取画布这件事写成一个具名成员，
        /// 读的人一眼看得出窗口与宿主的契约就是"我给你画布，其余别管"。
        /// 窗口自己不建表、不订阅变量：那属于运行策略，归宿主（见 ScadaRuntimeHost）。
        /// </summary>
        public ScadaCanvas CanvasHost => Canvas;

        /// <summary>弹窗画布（宿主为弹窗建第二张数据泵时取它；没有弹窗时也在可视树里，只是不可见）。</summary>
        public ScadaCanvas PopupCanvasHost => PopupCanvas;

        /// <summary>
        /// 按软件配置（AppConfig.json 的 <c>RunWindowMode</c>）定窗口形态。**必须在 <see cref="Window.Show"/> 之前调用**。
        ///
        /// 两种形态各自解决一个真实场景
        /// ---------
        /// · <see cref="ScadaRunWindowMode.AttachedToMainWindow"/>（默认）：无边框 + 最大化，盖住整个主界面。
        ///   现场触摸屏/一体机上跑生产时的形态——运行画面就是唯一界面，不该看到编辑器的任何东西。
        /// · <see cref="ScadaRunWindowMode.IndependentWindow"/>：带系统标题栏、可缩放、独立占一个任务栏项，
        ///   与主界面（含视觉图像面板）并排显示。开发调试时的形态——一边看画面跑，一边看图像与日志。
        ///
        /// 独立窗口为什么不"居中弹出"
        /// ---------
        /// 主界面通常是最大化的，居中弹出的 1280×800 会正好压在界面中央——多半就是图像区，
        /// 等于换个方式复现了本来要躲的那个问题。所以独立形态直接落到**目标屏工作区右下角**，
        /// 并按工作区比例收一收尺寸：第一眼就能看到两边，之后拖到哪、拉多大由用户自己决定。
        /// 用工作区而不是主窗口的 Left/Top/ActualWidth：
        /// 主界面最大化时这几个值互相对不上（Left/Top 是还原位置的坐标，ActualWidth 是最大化后的尺寸），
        /// 拿它们算偏移会算出窗口跑到屏幕外面去。
        ///
        /// 目标屏从哪儿来
        /// ---------
        /// 由宿主按软件配置（AppConfig.json 的 <c>RunWindowMonitor</c>）解好之后传进来。
        /// 窗口自己不读配置、不认识设备名——"落在哪块屏"是策略，"摆成什么样"才是窗口的事，
        /// 与 Owner 同一条分工（见下）。尺寸与落点两个数由 <see cref="ScadaMonitors.PlaceIndependent"/>
        /// 一处算，纯函数，断言可以直接钉住它。
        ///
        /// 这里不碰 <see cref="Window.Owner"/>
        /// ---------
        /// 挂上 Owner 会让运行窗口永远压在主界面之上，还会跟着主界面一起最小化、一起被关，
        /// "并排显示"就名存实亡了。挂不挂由宿主决定（见 ScadaRuntimeHost.Start）——
        /// 窗口只负责"我长什么样"，"我归谁管"是宿主的策略。
        /// </summary>
        /// <param name="monitor">
        /// 目标显示器；传 <c>null</c> 表示"按历史路径走"（见下）。
        /// </param>
        public void ApplyRunWindowMode(ScadaRunWindowMode mode, ScadaMonitor? monitor)
        {
            // 目标屏工作区（DIP）。monitor 为 null 只可能来自 EnumDisplayMonitors 枚举不出东西的
            // 受限桌面——那时退回 SystemParameters.WorkArea，也就是 S13-e 之前那条路径，行为逐字不变。
            // 留这条退路不是"防御性编程"：它是默认配置（跟随主屏）之外的唯一兜底，
            // 没有它，"枚举失败"会变成"运行窗口不显示"。
            var area = monitor != null ? monitor.WorkDip(ScadaMonitors.DpiScale()) : SystemParameters.WorkArea;

            if (mode == ScadaRunWindowMode.IndependentWindow)
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                ResizeMode = ResizeMode.CanResize;
                WindowState = WindowState.Normal;
                ShowInTaskbar = true;

                var place = ScadaMonitors.PlaceIndependent(area, Width, Height);
                Width = place.Width;
                Height = place.Height;
                // 必须先切 Manual，否则 WPF 仍按 XAML 的 CenterScreen 摆放，Left/Top 白设。
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = place.Left;
                Top = place.Top;
                return;
            }

            // 依附主窗口：现场运行形态，铺满整块屏（连任务栏一起盖），退出只有"停止运行"一条路。
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;

            // 指定了非主屏时先把窗口挪到那块屏，再最大化——WPF 的"最大化"落在窗口当前所在的屏上。
            // 跟随主屏时一个字节都不设（Left/Top 保持 NaN）：那时 WPF 按 CenterScreen 定位到主屏再最大化，
            // 正是现场已经在跑的行为，升级不该动它。
            if (monitor != null && !monitor.IsPrimary)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = area.Left;
                Top = area.Top;
            }

            WindowState = WindowState.Maximized;
            // 不占任务栏：它是主界面的从属窗口，两个任务栏图标在现场只会让人点错。
            ShowInTaskbar = false;
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            // 首帧之前先适配一次：FitToScreen 依赖画布已量出的尺寸，
            // 没量到也没关系——随后的 SizeChanged 会再兜一遍（见 OnCanvasSizeChanged）
            FitCanvas();
            UpdateStatusBar();
            ApplyPageBackgrounds();

            // 方案图片解析器：画布把它注入给每个图元（Hmi.Picture 用），
            // 与背景图同一条“方案目录 + 相对路径”口径。
            Canvas.ImageAssetResolver = ResolveImageAsset;
            PopupCanvas.ImageAssetResolver = ResolveImageAsset;

            // 报警面板的运行态上下文来自画布：宿主在 StartAlarms 里把它装到画布上，
            // 而 StartAlarms 排在 Show() 之前（见 ScadaRuntimeHost.Start），所以走到 Loaded
            // 这一步时它已经就绪。
            //
            // 面板不自己去上溯找画布（Window.GetWindow(this) 那套）：那等于把"面板恰好住在一个
            // 有画布的窗口里"写成硬约束，换个宿主就断。由窗口显式转交，契约才立得住。
            AlarmHistory.Attach(CanvasHost.RuntimeContext);

            // 抽屉的开关状态只有一个真相（面板自己的 Visibility）：× 关掉抽屉时按钮也得跟着弹起来。
            // 用框架自带的 IsVisibleChanged 而不是让面板再开一个事件——那是现成的、且一定会发。
            AlarmHistory.IsVisibleChanged += OnAlarmHistoryVisibleChanged;
            AlarmHistoryToggle.IsChecked = AlarmHistory.IsOpen;
        }

        private void OnWindowClosed(object sender, EventArgs e)
        {
            // 页面菜单在独立 Popup 里，不随窗口关闭自动消失；先收掉再让宿主拆会话。
            if (_pageMenu != null)
            {
                _pageMenu.IsOpen = false;
                _pageMenu = null;
            }

            _session.PropertyChanged -= OnSessionPropertyChanged;
            RemoveHandler(ScadaElementBase.EditStateChangedEvent, new RoutedEventHandler(OnEditStateChanged));
            CloseSoftKeyboard();

            if (_placementPopup != null)
            {
                _placementPopup.PropertyChanged -= OnPopupPropertyChanged;
                _placementPopup = null;
            }

            // 面板先摘订阅（引擎的三个事件 + 节拍），随后宿主的 TeardownAlarms 才去停节拍、拆引擎。
            AlarmHistory.Attach(null);
        }

        // ---- 弹窗层（特殊画面） ----

        private ScadaPage? _placementPopup;

        private IScadaSoftKeyboardTarget? _keyboardTarget;

        /// <summary>摆放期间会改键盘 MaxWidth/Height 并触发 SizeChanged；用它挡住重入。</summary>
        private bool _placingKeyboard;

        private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ScadaRuntime.CurrentPopup) or nameof(ScadaRuntime.IsPopupOpen))
                AttachPopupPlacement();

            // 切页/开关弹窗时，正在编辑的字段已经离开表面：先收键盘，避免它继续对着旧控件输入。
            if (e.PropertyName is nameof(ScadaRuntime.CurrentPage) or nameof(ScadaRuntime.CurrentPopup))
            {
                CloseSoftKeyboard();
                ApplyPageBackgrounds();
            }
        }

        /// <summary>
        /// 方案目录（背景图相对路径的解析根）。由宿主在创建窗口后、Show 之前赋值。
        /// 为空时背景图一律回退背景色——方案还没保存过就没有 Image 目录。
        /// </summary>
        public string? SolutionRoot { get; set; }

        /// <summary>方案图片解析器（相对路径 → 位图）；失败返回 null，图元显示占位。</summary>
        private ImageSource? ResolveImageAsset(string relativePath)
            => SolutionAssetService.TryLoad(SolutionRoot, relativePath, out var image, out _) ? image : null;

        /// <summary>把当前页/弹窗页的背景图铺到两张画布上（相对路径 → 位图由资产服务解析）。</summary>
        private void ApplyPageBackgrounds()
        {
            ApplyPageBackground(Canvas, _session.CurrentPage);
            ApplyPageBackground(PopupCanvas, _session.CurrentPopup);
        }

        private void ApplyPageBackground(ScadaCanvas canvas, ScadaPage? page)
        {
            if (canvas == null)
                return;

            if (page == null || string.IsNullOrWhiteSpace(page.BackgroundImage))
            {
                canvas.PageBackgroundImage = null;
                return;
            }

            if (!SolutionAssetService.TryLoad(SolutionRoot, page.BackgroundImage, out var image, out _))
            {
                // 缺失/解码失败：回退背景色，运行态不弹窗（设计基线 §页面背景）。
                canvas.PageBackgroundImage = null;
                return;
            }

            // 先写填充参数再写图：每次赋值都会重建 ImageBrush，最后写图那一次用的是最终参数。
            canvas.PageBackgroundStretch = ScadaBackgroundStretch.Parse(page.BackgroundStretch);
            canvas.PageBackgroundOpacity = ScadaBackgroundStretch.ToOpacity(page.BackgroundOpacity);
            canvas.PageBackgroundTile = ScadaBackgroundStretch.IsTile(page.BackgroundStretch);
            canvas.PageBackgroundImage = image;
        }

        /// <summary>换弹窗时重挂锚点订阅；弹窗页自己的锚点/偏移变了也要重新摆放。</summary>
        private void AttachPopupPlacement()
        {
            if (_placementPopup != null)
                _placementPopup.PropertyChanged -= OnPopupPropertyChanged;

            _placementPopup = _session.CurrentPopup;

            if (_placementPopup != null)
                _placementPopup.PropertyChanged += OnPopupPropertyChanged;

            ApplyPopupPlacement();
        }

        private void OnPopupPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ScadaPage.PopupAnchor)
                or nameof(ScadaPage.PopupOffsetX)
                or nameof(ScadaPage.PopupOffsetY))
            {
                ApplyPopupPlacement();
            }
        }

        /// <summary>
        /// 九宫格锚点 → WPF 对齐方式；偏移用 RenderTransform 而不是 Margin。
        /// 用 Margin 会把"居中 + 偏移"变成"可用空间里重新居中"，偏一点就跳一格；
        /// TranslateTransform 是相对最终位置的纯平移，九种锚点的行为一致。
        /// </summary>
        private void ApplyPopupPlacement()
        {
            var popup = _session.CurrentPopup;
            if (popup == null)
                return;

            PopupFrame.HorizontalAlignment = popup.PopupAnchor switch
            {
                ScadaPopupAnchor.TopLeft or ScadaPopupAnchor.Left or ScadaPopupAnchor.BottomLeft
                    => HorizontalAlignment.Left,
                ScadaPopupAnchor.TopRight or ScadaPopupAnchor.Right or ScadaPopupAnchor.BottomRight
                    => HorizontalAlignment.Right,
                _ => HorizontalAlignment.Center,
            };

            PopupFrame.VerticalAlignment = popup.PopupAnchor switch
            {
                ScadaPopupAnchor.TopLeft or ScadaPopupAnchor.Top or ScadaPopupAnchor.TopRight
                    => VerticalAlignment.Top,
                ScadaPopupAnchor.BottomLeft or ScadaPopupAnchor.Bottom or ScadaPopupAnchor.BottomRight
                    => VerticalAlignment.Bottom,
                _ => VerticalAlignment.Center,
            };

            // 偏移夹回层内：大偏移不能把标题栏/关闭按钮推出窗口（Esc 仍可用，但按钮看不见
            // 就是"弹窗关不掉"的观感）。可用空间按当前对齐方式算，居中时左右各一半。
            var availableX = PopupLayer.ActualWidth - PopupFrame.ActualWidth;
            var availableY = PopupLayer.ActualHeight - PopupFrame.ActualHeight;

            PopupFrame.RenderTransform = new TranslateTransform(
                ClampOffset(popup.PopupOffsetX, availableX, PopupFrame.HorizontalAlignment),
                ClampOffset(popup.PopupOffsetY, availableY, PopupFrame.VerticalAlignment));
        }

        private static double ClampOffset(double value, double available, HorizontalAlignment alignment)
        {
            var span = Math.Max(0, available);

            return alignment switch
            {
                HorizontalAlignment.Left => Math.Clamp(value, 0, span),
                HorizontalAlignment.Right => Math.Clamp(value, -span, 0),
                _ => Math.Clamp(value, -span / 2, span / 2),
            };
        }

        private static double ClampOffset(double value, double available, VerticalAlignment alignment)
        {
            var span = Math.Max(0, available);

            return alignment switch
            {
                VerticalAlignment.Top => Math.Clamp(value, 0, span),
                VerticalAlignment.Bottom => Math.Clamp(value, -span, 0),
                _ => Math.Clamp(value, -span / 2, span / 2),
            };
        }

        private void OnPopupLayerSizeChanged(object sender, SizeChangedEventArgs e) => ApplyPopupPlacement();

        private void OnPopupCloseClick(object sender, RoutedEventArgs e) => _session.ClosePopup();

        private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 键盘打开时 Esc/Enter 先归键盘：Esc = 放弃本次编辑，Enter = 确定。
            // 其它键不拦——它们要照常进到输入控件自己的 TextBox 里。
            if (SoftKeyboard.IsOpen)
            {
                if (e.Key == Key.Escape)
                {
                    CloseSoftKeyboard();
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Enter)
                {
                    _keyboardTarget?.CommitEdit();
                    SoftKeyboard.Refresh();

                    if (_keyboardTarget?.IsEditing != true)
                        CloseSoftKeyboard();

                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.Escape && _session.IsPopupOpen)
            {
                _session.ClosePopup();
                e.Handled = true;
            }
        }

        // ---- 软键盘（数字 / 字符） ----

        private void OnEditStateChanged(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is not IScadaSoftKeyboardTarget target)
                return;

            if (target.IsEditing)
                OpenSoftKeyboard(target);
            else if (ReferenceEquals(_keyboardTarget, target))
                CloseSoftKeyboard();
        }

        private void OpenSoftKeyboard(IScadaSoftKeyboardTarget target)
        {
            _keyboardTarget = target;
            SoftKeyboard.Open(target);
            KeyboardLayer.Visibility = Visibility.Visible;
            KeyboardLayer.UpdateLayout();
            PlaceSoftKeyboard(target);
        }

        /// <summary>
        /// 收键盘。先摘目标再 CancelEdit：CancelEdit 会把 IsEditing 翻成 false 并再发一次
        /// EditStateChanged，目标已摘掉就不会递归回到这里。
        /// </summary>
        private void CloseSoftKeyboard()
        {
            var target = _keyboardTarget;
            _keyboardTarget = null;

            target?.CancelEdit();

            SoftKeyboard.Close();
            KeyboardLayer.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 九宫格摆放：默认放在字段下方；下方放不下翻到上方；左右按字段中心对齐并夹回画面区。
        /// 目标在缩放画布上时用 TranslatePoint 换算到窗口坐标，不拿模型 X/Y 直接算。
        /// </summary>
        private void PlaceSoftKeyboard(IScadaSoftKeyboardTarget target)
        {
            if (target is not FrameworkElement element || _placingKeyboard)
                return;

            var areaWidth = KeyboardLayer.ActualWidth;
            var areaHeight = KeyboardLayer.ActualHeight;

            if (areaWidth <= 0 || areaHeight <= 0)
                return;

            const double gap = 8;

            _placingKeyboard = true;

            try
            {
                // 先限尺寸再测量：长文本/长错误不能把键盘撑出画面区；超出部分由 Viewbox 等比缩小。
                SoftKeyboard.MaxWidth = Math.Max(0, areaWidth - gap * 2);
                SoftKeyboard.MaxHeight = Math.Max(0, areaHeight - gap * 2);

                if (SoftKeyboard.MaxWidth <= 0 || SoftKeyboard.MaxHeight <= 0)
                    return;

                SoftKeyboard.Measure(new Size(SoftKeyboard.MaxWidth, SoftKeyboard.MaxHeight));
                var size = SoftKeyboard.DesiredSize;

                if (size.Width <= 0 || size.Height <= 0)
                    return;

                Point p0, p1, p2, p3;

                try
                {
                    // 用四个角换算到窗口坐标再取包围盒：画布有 ScaleTransform、弹窗还套了 Viewbox，
                    // 直接拿 element.ActualWidth 当屏幕宽高会把键盘摆偏（旋转元素也一起兜住）。
                    p0 = element.TranslatePoint(new Point(0, 0), KeyboardLayer);
                    p1 = element.TranslatePoint(new Point(element.ActualWidth, 0), KeyboardLayer);
                    p2 = element.TranslatePoint(new Point(0, element.ActualHeight), KeyboardLayer);
                    p3 = element.TranslatePoint(
                        new Point(element.ActualWidth, element.ActualHeight), KeyboardLayer);
                }
                catch (InvalidOperationException)
                {
                    // 控件已经不在可视树上（切页/关窗的收尾）：不摆，窗口那一层会很快收起键盘。
                    return;
                }

                var field = new Rect(
                    Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X)),
                    Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y)),
                    Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X))
                        - Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X)),
                    Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y))
                        - Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y)));

                var placement = ScadaSoftKeyboard.ComputePlacement(
                    field, size, new Size(areaWidth, areaHeight), gap);

                System.Windows.Controls.Canvas.SetLeft(SoftKeyboard, placement.X);
                System.Windows.Controls.Canvas.SetTop(SoftKeyboard, placement.Y);
            }
            finally
            {
                _placingKeyboard = false;
            }
        }

        private void OnKeyboardLayerSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (SoftKeyboard.IsOpen && _keyboardTarget is { } target)
                PlaceSoftKeyboard(target);
        }

        private void OnSoftKeyboardSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 提交失败后错误行出现/消失会改变键盘高度：重新摆一次，别让“确定”被挤到画面外。
            if (SoftKeyboard.IsOpen && _keyboardTarget is { } target)
                PlaceSoftKeyboard(target);
        }

        private void OnKeyboardBackdropClick(object sender, MouseButtonEventArgs e)
        {
            // 点键盘外 = 取消本次编辑（与 Esc 同义），不提交半截输入。
            CloseSoftKeyboard();
            e.Handled = true;
        }

        // ---- 报警历史抽屉 ----

        private void OnAlarmHistoryClick(object sender, RoutedEventArgs e) => AlarmHistory.Toggle();

        /// <summary>抽屉被谁关掉都要让顶条按钮跟着弹起来（× 按钮关的，不是按钮自己关的）</summary>
        private void OnAlarmHistoryVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
            => AlarmHistoryToggle.IsChecked = AlarmHistory.IsOpen;

        // ---- 取景 ----

        private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 尺寸还没量出来（0）时不要算适配倍率：那会算出 0 倍并被夹到下限，
            // 表现为"画面缩成一个点"，而且下一次 SizeChanged 未必会来。
            if (Canvas.ActualWidth <= 0 || Canvas.ActualHeight <= 0) return;

            FitCanvas();
            UpdateStatusBar();
        }

        private void FitCanvas()
        {
            if (Canvas.ActualWidth <= 0 || Canvas.ActualHeight <= 0) return;
            Canvas.FitToScreen();
        }

        private void UpdateStatusBar()
        {
            ViewportText.Text = $"视口 {ActualWidth:0} × {ActualHeight:0}";
            FitText.Text = $"适配 {Canvas.Zoom:P0}";
        }

        /// <summary>
        /// 宿主在首帧渲染完成后回填加载耗时。
        /// 之所以由外部塞进来：这一刻只有窗口知道（渲染完成是 WPF 的事件），
        /// 而领域层拿不到那一帧，所以耗时不可能是 <see cref="ScadaRuntime"/> 的属性。
        /// </summary>
        public void ReportLoadElapsed(double milliseconds)
        {
            ElapsedText.Text = $"加载耗时 {milliseconds:0} ms";
        }

        // ---- 页面菜单 ----

        /// <summary>
        /// 顶栏页面菜单：按运行态文档里的可导航画面生成 ContextMenu。
        /// 当前页打勾；点一项就走 <see cref="ScadaRuntime.Navigate(ScadaPage?)"/>——
        /// 与动作表、NavigateBack 同一个出口，不另开一套切页路径。
        /// 基座页由 <see cref="ScadaDocument.IsNavigable"/> 排除（它是公共层，不是终点）。
        /// </summary>
        private void OnPageMenuClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ScadaRuntime runtime)
                return;

            // 先收掉上一次的菜单：连点两下不该留下两个 Popup。
            if (_pageMenu != null)
            {
                _pageMenu.IsOpen = false;
                _pageMenu = null;
            }

            var menu = new ContextMenu
            {
                PlacementTarget = PageMenuButton,
                Placement = PlacementMode.Bottom,
            };

            if (FindResource("PageMenuContextStyle") is Style contextStyle)
                menu.Style = contextStyle;

            var itemStyle = FindResource("PageMenuItemStyle") as Style;

            foreach (var page in runtime.Document.Pages.Where(
                         p => runtime.Document.IsNavigable(p) && runtime.CanAccess(p, out _)))
            {
                var item = new MenuItem
                {
                    // 用 TextBlock 而不是裸字符串：默认 MenuItem 模板会把 "_" 当访问键吞掉，
                    // 页名「01_总览」会显示成「01总览」。
                    Header = new TextBlock { Text = page.Name },
                    Tag = page,
                    IsCheckable = true,
                    IsChecked = ReferenceEquals(page, runtime.CurrentPage),
                    FontWeight = ReferenceEquals(page, runtime.CurrentPage)
                        ? FontWeights.SemiBold
                        : FontWeights.Normal,
                };

                if (itemStyle != null)
                    item.Style = itemStyle;

                item.Click += OnPageMenuItemClick;
                menu.Items.Add(item);
            }

            if (menu.Items.Count == 0)
            {
                var empty = new MenuItem
                {
                    Header = new TextBlock { Text = "（没有可切换的画面）" },
                    IsEnabled = false,
                };

                if (itemStyle != null)
                    empty.Style = itemStyle;

                menu.Items.Add(empty);
            }

            menu.Closed += (_, _) =>
            {
                if (ReferenceEquals(_pageMenu, menu))
                    _pageMenu = null;
            };

            _pageMenu = menu;
            menu.IsOpen = true;
        }

        private void OnPageMenuItemClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ScadaRuntime runtime
                && sender is MenuItem { Tag: ScadaPage page })
            {
                runtime.Navigate(page);
            }
        }

        /// <summary>
        /// 返回上一页：与 NavigateBack 动作同一个出口（<see cref="ScadaRuntime.GoBack()"/>）。
        /// 栈空/回退目标已删时静默不动——按钮的 IsEnabled 已按 CanGoBack 置灰，
        /// 这里再判一次只是防"置灰还没刷新时点了一下"。
        /// </summary>
        private void OnPageBackClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ScadaRuntime runtime)
                runtime.GoBack();
        }

        // ---- 退出 ----

        /// <summary>
        /// 停止运行 = 关掉本窗口。会话的收尾（置为未运行、发不发 Unloaded）由宿主
        /// 挂在 <c>Closed</c> 上做，这里不直接去碰会话，免得两条路都在改同一个状态。
        /// </summary>
        private void OnStopClick(object sender, RoutedEventArgs e) => Close();
    }
}
