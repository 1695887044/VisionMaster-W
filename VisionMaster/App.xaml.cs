using Core.Interfaces;
using Prism.Ioc;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using VisionMaster.Communications;
using VisionMaster.Core;
using VisionMaster.Lifetime;
using VisionMaster.Lifetime.Checks;
using VisionMaster.Services;
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

        protected override Window CreateShell()
        {
            var thinFont = this.Resources["FA.Light"];
            this.Resources["Icon"] = thinFont;

            // ===== 生命周期宿主：异常分级接线 =====
            _lifetime = Container.Resolve<AppLifetimeService>();
            _lifetime.RegisterGlobalExceptionHandlers();

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

        /// <summary>主窗口已显示后关闭 Splash（base.OnInitialized 内部会 Show 主窗口）</summary>
        protected override void OnInitialized()
        {
            base.OnInitialized();
            _splash?.Close();
            _splash = null;
        }

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            MemoryManager.Instance.Start(300, 30);
            containerRegistry.RegisterSingleton<SolutionService>();
            containerRegistry.RegisterSingleton<FlowCompiler>();
            containerRegistry.RegisterSingleton<IPluginProvider, PluginProvider>();
            containerRegistry.RegisterSingleton<IFlowEngine, FlowEngineService>();
            containerRegistry.RegisterSingleton<IRuntimeManager, RuntimeManager>();
            containerRegistry.RegisterSingleton<ICommunicationManager, AdvancedCommunicationManager>();
            containerRegistry.RegisterSingleton<IExecutionContext, Services.ExecutionContext>();
            containerRegistry.RegisterSingleton<WorkspaceContext>();
            containerRegistry.Register<IReadOnlyWorkspaceContext>(c => c.Resolve<WorkspaceContext>());
            containerRegistry.Register<IWorkspaceManager>(c => c.Resolve<WorkspaceContext>());
            containerRegistry.RegisterSingleton<ILogService, LogService>();
            containerRegistry.RegisterSingleton<IPerformanceMonitor, PerformanceMonitor>();
            containerRegistry.RegisterSingleton<AppSettingsService>();
            containerRegistry.RegisterSingleton<AppLifetimeService>();
            containerRegistry.RegisterForNavigation<LogView,LogViewModel>();
            containerRegistry.RegisterForNavigation<ProcessView, ProcessViewModel>();
            containerRegistry.RegisterForNavigation<GlobalDataView, GlobalDataViewModel>();
            containerRegistry.RegisterForNavigation<ToolView, ToolViewModel>();
            containerRegistry.RegisterForNavigation<ModuleOutputView, ModuleOutputViewModel>();
            containerRegistry.RegisterDialog<VariableBindingView, VariableBindingViewModel>("DataBindView");
            containerRegistry.RegisterDialog<GlobalVariableView, GlobalVariableManagerViewModel>("GlobalVariable");
            containerRegistry.RegisterDialog<ConditionEditorView, ConditionEditorViewModel>("ConditionEditor");
            containerRegistry.RegisterDialog<FlowManagerView, FlowManagerViewModel>("FlowManagerView");
            containerRegistry.RegisterDialog<CommunicationSettingsView, CommunicationSettingsViewModel>("CommunicationSettingsView");
            containerRegistry.RegisterDialog<PluginConfigShellView, PluginConfigShellViewModel>("PluginConfigShell");
            containerRegistry.RegisterDialog<SolutionListView, SolutionListViewModel>("SolutionListView");
            containerRegistry.RegisterForNavigation<Shell, ShellViewModel>();

            // ===== 退出资源释放链 =====
            // 执行时按注册相反顺序：停止引擎 → 释放会话 → 断通讯 → 存配置 → 停内存
            var lifetime = containerRegistry.GetContainer().Resolve<AppLifetimeService>();
            lifetime.RegisterExitTask(ExitTask.Of("停止内存管理", () => MemoryManager.Instance.Stop()));
            lifetime.RegisterExitTask(ExitTask.Of("持久化软件配置", () => Container.Resolve<AppSettingsService>().Save()));
            lifetime.RegisterExitTask(ExitTask.Of("断开通讯连接", () =>
            {
                var comm = Container.Resolve<AdvancedCommunicationManager>();
                comm.StopAll();
                comm.DisconnectAll();
            }));
            lifetime.RegisterExitTask(ExitTask.Of("释放运行会话", () =>
            {
                Container.Resolve<IRuntimeManager>().ClearAll();
            }));
            lifetime.RegisterExitTask(ExitTask.Of("停止流程引擎", () => Container.Resolve<IFlowEngine>().StopAll()));
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { _lifetime?.ExecuteExitChainBlocking("应用退出"); }
            catch { /* 退出链内部已逐项捕获，此处兜底 */ }
            _singleInstance?.Dispose();
            base.OnExit(e);
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
