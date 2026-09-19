using Core.Interfaces;
using Prism.Ioc;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using VisionMaster.Binding;
using VisionMaster.Communications;
using VisionMaster.Core;
using VisionMaster.Engine;
using VisionMaster.Lifetime;
using VisionMaster.Lifetime.Checks;
using VisionMaster.Services;
using VisionMaster.Scada;
using VisionMaster.ViewModels;
using VisionMaster.ViewModels.DialogViewModels;
using VisionMaster.Views;
using VisionMaster.Views.DialogViews;

namespace VisionMaster
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : PrismApplication
    {
        private AppLifetimeService? _lifetime;
        private SingleInstanceCheck? _singleInstance;
        private VisionMaster.Lifetime.SplashScreen? _splash;
        private readonly List<IAppModule> _modules = new();

        protected override Window CreateShell()
        {
            var thinFont = this.Resources["FA.Light"];
            this.Resources["Icon"] = thinFont;

            // ===== 生命周期宿主：异常分级接线 + 模块初始化 =====
            _lifetime = Container.Resolve<AppLifetimeService>();
            _lifetime.RegisterGlobalExceptionHandlers();

            // ===== 退出链收尾：日志落盘 =====
            // 退出链逆序执行：此处最先注册 → 最后运行，保证其他退出任务的日志都完整落盘
            _lifetime.RegisterExitTask(ExitTask.Of("日志落盘收尾", () =>
            {
                if (Container.Resolve<ILogService>() is IDisposable d)
                    d.Dispose();
            }));

            // 通信层日志接进 UI 日志窗口：manager 内部日志原本只写 Console（WPF 无控制台=黑洞），
            // 变量注册/轮询/读取失败等诊断必须可见。
            // 必须在模块初始化之前挂接：通讯模块的启动装配（加载配置 + 自动连接）日志也在其中
            AdvancedCommunicationManager.LogSink = Container.Resolve<ILogService>();

            foreach (var module in _modules)
                module.Initialize(Container, _lifetime); // 模块注册自检项/退出任务，须在自检前完成

            // ===== 启动自检链（按执行顺序注册）=====
            _singleInstance = new SingleInstanceCheck();
            _lifetime.RegisterCheck(_singleInstance);
            _lifetime.RegisterCheck(new ConfigCheck(Container.Resolve<AppSettingsService>(), Container.Resolve<ILogService>()));
            _lifetime.RegisterCheck(new PluginScanCheck(
                () => Container.Resolve<PluginService>(),
                () => Container.Resolve<IPluginProvider>(),
                Container.Resolve<ILogService>()));
            _lifetime.RegisterCheck(new CommunicationCheck(
                () => Container.Resolve<AdvancedCommunicationManager>(),
                () => Container.Resolve<AppSettingsService>().Current,
                Container.Resolve<ILogService>()));

            // ===== Splash 进度窗 + 自检执行（Dispatcher 帧泵保持 Splash 响应）=====
            // 注意：Splash 在主窗口显示后才关闭（OnInitialized），避免出现“零窗口”触发 Shutdown
            _splash = new VisionMaster.Lifetime.SplashScreen();
            _splash.InitChecks(_lifetime.Checks);
            _lifetime.CheckCompleted += e => _splash.UpdateCheck(e.Name, e.Passed, e.Result, e.Done, e.Total);
            _splash.Show();

            var result = RunWithPump(() => _lifetime.RunStartupChecksAsync());
            if (!result.Success)
            {
                // Error 级失败：展示原因后终止，不进入主界面
                _splash.ShowBlockingFailure(result.BlockingReason ?? "未知原因");
                Thread.Sleep(3000);
                _splash.Close();
                Environment.Exit(2);
            }
            _splash.SetFinished(result.Warnings);
            RunWithPump(async () => { await Task.Delay(900); return true; }); // 停留片刻展示“启动完成/警告”
            return Container.Resolve<Shell>();
        }

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            // 定时深度清理已禁用：Aggressive GC + LOH 压缩 + 工作集裁剪每 30s 冻结主线程 2~3s，
            // 且 WPF+Halcon 宿主常态内存 >300MB 阈值，表现为"空闲双击也卡"。
            // .NET 自身 GC 对本应用足够高效；如将来确有内存增长问题，再评估温和化巡检。
            // MemoryManager.Instance.Start(300, 30);

            // ===== 基础服务最先注册：Unity 注册工厂时可能提前解析依赖，必须保证已就位 =====
            containerRegistry.RegisterSingleton<ILogService, LogService>();
            containerRegistry.RegisterSingleton<IPerformanceMonitor, PerformanceMonitor>();
            containerRegistry.RegisterSingleton<AppSettingsService>();
            containerRegistry.RegisterSingleton<AppLifetimeService>();
            containerRegistry.RegisterSingleton<IUserNotifier, UserNotifier>();

            // ===== 模块注册：通讯/引擎子系统自装配 =====
            _modules.Add(new CommunicationModule());
            _modules.Add(new FlowEngineModule());
            foreach (var module in _modules)
                module.Register(containerRegistry);

            containerRegistry.RegisterSingleton<SolutionService>();
            containerRegistry.RegisterSingleton<WorkspaceContext>();
            containerRegistry.RegisterSingleton<NetworkVariableBridge>();
            containerRegistry.Register<IReadOnlyWorkspaceContext>(c => c.Resolve<WorkspaceContext>());
            containerRegistry.Register<IWorkspaceManager>(c => c.Resolve<WorkspaceContext>());
            containerRegistry.RegisterForNavigation<LogView,LogViewModel>();
            containerRegistry.RegisterForNavigation<ProcessView, ProcessViewModel>();
            containerRegistry.RegisterForNavigation<MonitorView, MonitorViewModel>();
            containerRegistry.RegisterForNavigation<ToolView, ToolViewModel>();
            // 组态编辑器的视图模型必须是单例：画布宿主与属性面板要靠"同一个 SelectedElement"联动，
            // 各自一份就得引入事件总线才能对齐选中态。RegisterForNavigation 造视图时经由容器
            // 解析视图模型，singleton 覆盖默认的单次构造，所以两处拿到的是同一个实例。
            containerRegistry.RegisterSingleton<ScadaEditorViewModel>();
            containerRegistry.RegisterForNavigation<ScadaEditorView, ScadaEditorViewModel>();
            containerRegistry.RegisterForNavigation<ScadaToolboxView, ScadaToolboxViewModel>();
            containerRegistry.RegisterForNavigation<ScadaPropertyView, ScadaPropertyViewModel>();
            // 运行态宿主必须是单例：它管的就是"同一时刻只允许一个运行窗口"这条策略，
            // 每个调用方解析出一份新实例，等于这条策略根本不存在（两份会话各自写同一批变量）。
            containerRegistry.RegisterSingleton<IScadaRuntimeHost, ScadaRuntimeHost>();
            // 运行态的数据通道：把工作区的变量注册表接成领域层认的"值源"（见 IScadaValueSource）。
            // 走工厂而不是类型映射，是因为注册表挂在 WorkspaceContext 上、不单独注册——
            // 这样"变量只有一个索引"这条约束不会被绕开（多一份索引就会分裂）。
            containerRegistry.RegisterSingleton<IScadaValueSource>(
                c => new RegistryScadaValueSource(c.Resolve<IWorkspaceManager>().VariableRegistry));
            // 动作分发器同样是单例：它无状态（只往日志/变量/导航出口递话），而每次运行都 new 一份
            // 只会让人误以为"动作的执行状态存在某处"——将来接 S6 写变量时这里换成带状态的实现也不动调用方。
            containerRegistry.RegisterSingleton<IScadaActionDispatcher, ScadaActionDispatcher>();
            // 组态「选变量」入口：属性面板只说"给我一个变量"，弹窗怎么弹、弹哪个由这一层决定。
            // 面板与事件行因此能在无 WPF 宿主下被直接构造、直接断言（ScadaChecks 就是这么测的）。
            containerRegistry.RegisterSingleton<IScadaVariablePicker, ScadaVariablePicker>();
            containerRegistry.RegisterDialog<ScadaVariablePickerView, ScadaVariablePickerViewModel>(
                ScadaVariablePicker.DialogName);
            containerRegistry.RegisterDialog<VariableBindingView, VariableBindingViewModel>("DataBindView");
            containerRegistry.RegisterDialog<GlobalVariableView, GlobalVariableManagerViewModel>("GlobalVariable");
            containerRegistry.RegisterDialog<ConditionEditorView, ConditionEditorViewModel>("ConditionEditor");
            containerRegistry.RegisterDialog<FlowManagerView, FlowManagerViewModel>("FlowManagerView");
            containerRegistry.RegisterDialog<CommunicationSettingsView, CommunicationSettingsViewModel>("CommunicationSettingsView");
            // 运行窗口形态设置（依附主窗口 / 独立窗口）：软件级偏好，落 AppConfig.json
            containerRegistry.RegisterDialog<ScadaRunWindowSettingsView, ScadaRunWindowSettingsViewModel>("ScadaRunWindowSettingsView");
            containerRegistry.RegisterDialog<PluginConfigShellView, PluginConfigShellViewModel>("PluginConfigShell");
            containerRegistry.RegisterDialog<SolutionListView, SolutionListViewModel>("SolutionListView");
            containerRegistry.RegisterForNavigation<Shell, ShellViewModel>();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { _lifetime?.ExecuteExitChainBlocking("应用退出"); }
            catch { /* 退出链内部已逐项捕获，此处兜底 */ }
            _singleInstance?.Dispose();
            base.OnExit(e);
        }

        /// <summary>主窗口已显示后关闭 Splash（base.OnInitialized 内部会 Show 主窗口）</summary>
        protected override void OnInitialized()
        {
            base.OnInitialized();
            _splash?.Close();
            _splash = null;
        }

        /// <summary>
        /// 在当前 Dispatcher 上泵消息帧直至异步工作完成：
        /// CreateShell 必须同步返回窗口，但自检链是异步的——
        /// 帧泵让 Splash 的渲染/输入在等待期间照常处理（解决启动 UI 假死）。
        /// </summary>
        private static T RunWithPump<T>(Func<Task<T>> work)
        {
            var frame = new DispatcherFrame();
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = work().ContinueWith(t =>
            {
                if (t.IsFaulted) tcs.TrySetException(t.Exception!.GetBaseException());
                else tcs.TrySetResult(t.Result);
                frame.Continue = false;
            }, TaskScheduler.FromCurrentSynchronizationContext());
            Dispatcher.PushFrame(frame);
            return tcs.Task.GetAwaiter().GetResult();
        }
    }
}
