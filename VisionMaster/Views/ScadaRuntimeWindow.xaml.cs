using System;
using System.Windows;
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

        public ScadaRuntimeWindow(ScadaRuntime session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            InitializeComponent();

            // DataContext 由宿主交来的会话充当：绑定路径只有一条 CurrentPage.*，
            // 不再套一层视图模型去转发——转一次就得同步一次，多一处漏改的机会。
            DataContext = _session;

            // 组态事件的接线在宿主（ScadaRuntimeHost）的窗口根上：AddHandler/RemoveHandler
            // 成对收尾，全程序只有一处订阅。这里不再挂第二座桥——
            // 路由事件按委托逐个回调，两座桥会让同一钩子的动作被执行两遍。
            Loaded += OnWindowLoaded;

            // 关闭时摘掉报警面板对引擎的订阅。本处理器是在构造函数里挂的，而宿主的收尾处理器
            // 挂在 Start()（更晚），所以这里的执行顺序<b>早于</b> TeardownAlarms——
            // 正好符合"订阅方先退场，再拆被订阅的东西"这条顺序。
            Closed += OnWindowClosed;
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
            // 面板先摘订阅（引擎的三个事件 + 节拍），随后宿主的 TeardownAlarms 才去停节拍、拆引擎。
            AlarmHistory.Attach(null);
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

        // ---- 退出 ----

        /// <summary>
        /// 停止运行 = 关掉本窗口。会话的收尾（置为未运行、发不发 Unloaded）由宿主
        /// 挂在 <c>Closed</c> 上做，这里不直接去碰会话，免得两条路都在改同一个状态。
        /// </summary>
        private void OnStopClick(object sender, RoutedEventArgs e) => Close();
    }
}
