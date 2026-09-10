using Core.Interfaces;
using Prism.Ioc;
using VisionMaster.Lifetime;
using VisionMaster.Services;

namespace VisionMaster.Engine
{
    /// <summary>流程引擎模块：服务注册 + 退出任务自声明</summary>
    public class FlowEngineModule : IAppModule
    {
        public string Name => "流程引擎模块";

        public void Register(IContainerRegistry registry)
        {
            registry.RegisterSingleton<FlowCompiler>();
            registry.RegisterSingleton<PluginProvider>();
            registry.RegisterSingleton<IPluginProvider, PluginProvider>();
            registry.RegisterSingleton<IFlowEngine, FlowEngineService>();
            registry.RegisterSingleton<IRuntimeManager, RuntimeManager>();
            registry.RegisterSingleton<IExecutionContext, VisionMaster.Services.ExecutionContext>();
            registry.RegisterSingleton<IResourceLockService, ResourceLockService>();
        }

        public void Initialize(IContainerProvider container, AppLifetimeService lifetime)
        {
            var engine = container.Resolve<IFlowEngine>();
            var runtime = container.Resolve<IRuntimeManager>();

            // 退出链按注册逆序执行：先注册的靠后释放，引擎任务先于通讯执行
            lifetime.RegisterExitTask(ExitTask.Of("停止流程引擎", () => engine.StopAll()));
            lifetime.RegisterExitTask(ExitTask.Of("释放运行会话", () => runtime.ClearAll()));
        }
    }
}
