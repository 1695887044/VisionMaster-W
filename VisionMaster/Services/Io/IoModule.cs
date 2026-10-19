using System;
using System.ComponentModel;
using Core.Interfaces;
using Core.Interfaces.Io;
using Prism.Ioc;
using VisionMaster.Binding;
using VisionMaster.Lifetime;
using VisionMaster.Communications;
using VisionMaster.Services;

namespace VisionMaster.Services.Io
{
    /// <summary>IO 子系统装配：注册服务、按方案重建设备、退出时释放。</summary>
    public sealed class IoModule : IAppModule
    {
        public string Name => "IO 模块";

        public void Register(IContainerRegistry registry)
        {
            registry.RegisterSingleton<IoDriverRegistry>();
            registry.RegisterSingleton<IoProvider>();
            registry.RegisterSingleton<IIoProvider>(c => c.Resolve<IoProvider>());

            // 输出写租约：严格单写者，手动写用独立 owner；流程/分支持有期间拒绝外部写。
            registry.RegisterSingleton<IoOutputLeaseRegistry>();

            // 系统变量统一宿主：运动目录与 IO 目录都只产快照，由宿主合并写集合。
            // 不注册成类型映射——它需要 workspace.SystemVariables 这个具体集合实例。
            registry.RegisterSingleton<SystemVariableHost>(c =>
                new SystemVariableHost(c.Resolve<IWorkspaceManager>().SystemVariables));
            registry.RegisterSingleton<IoVariableCatalog>(c => new IoVariableCatalog(
                c.Resolve<IWorkspaceManager>(),
                c.Resolve<IIoProvider>(),
                c.Resolve<ILogService>(),
                c.Resolve<IoOutputLeaseRegistry>()));

            // 健康快照：界面/诊断按 1s 拉一次，不订阅高频事件。
            registry.RegisterSingleton<IoHealthMonitor>(c => new IoHealthMonitor(
                c.Resolve<IoProvider>(), c.Resolve<IoVariableCatalog>()));

            // 通讯配置的方案级装配：打开/切换方案时整体替换连接集合。
            registry.RegisterSingleton<CommunicationSolutionBinder>();

            // 租约注册表到核心层写入作用域的挂接放在 Initialize——Register 阶段容器尚未构建，不能 Resolve。
        }

        public void Initialize(IContainerProvider container, AppLifetimeService lifetime)
        {
            IoWriteScope.AttachLeaseRegistry(container.Resolve<IoOutputLeaseRegistry>());
            var provider = container.Resolve<IoProvider>();
            var workspace = container.Resolve<IWorkspaceManager>();
            var log = container.Resolve<ILogService>();
            var variables = container.Resolve<IoVariableCatalog>();
            var host = container.Resolve<SystemVariableHost>();

            // 先把 IO 目录接到统一宿主，再按方案装配：之后运动目录重建不会删 IO 变量。
            variables.AttachToHost(host);

            // 状态变量每拍刷新（Online/State/LastError）；50ms 足够，状态不是高频量。
            var statusTimer = new System.Threading.Timer(_ =>
            {
                try { variables.PublishStatusValues(); }
                catch { /* 诊断链路不许把应用带下去 */ }
            }, null, 200, 200);

            void ApplyFromWorkspace()
            {
                try
                {
                    var solution = workspace.CurrentSolution;
                    provider.Apply(solution?.IoBuses, solution?.IoDevices, autoStart: true);
                    variables.Rebuild();
                    variables.PublishStatusValues();
                    host.Refresh();
                }
                catch (Exception ex)
                {
                    log.Error($"[IO] 按方案装配失败：{ex.Message}");
                }
            }

            ApplyFromWorkspace();

            if (workspace is INotifyPropertyChanged inpc)
            {
                inpc.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(IWorkspaceManager.CurrentSolution))
                        ApplyFromWorkspace();
                };
            }

            lifetime.RegisterExitTask(ExitTask.Of("释放 IO 设备", () =>
            {
                try { statusTimer.Dispose(); } catch { }
                try { provider.StopAll(); } catch { }
                try { provider.ReleaseAll(); } catch { }
            }));
        }
    }
}
