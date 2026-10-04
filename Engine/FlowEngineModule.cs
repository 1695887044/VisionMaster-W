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
            // 具体类型也注册成单例，并把接口映射到同一个实例：
            // 图像集采集要订阅 FlowEngineService.FlowRunCompleted（该事件不在 IFlowEngine 契约上），
            // 若仍用 RegisterSingleton<IFlowEngine, FlowEngineService>()，容器会为具体类型另建一份，
            // 于是"订阅事件的那份"与"真正在跑流程的那份"不是同一个对象，事件永远收不到。
            registry.RegisterSingleton<FlowEngineService>();
            registry.RegisterSingleton<IFlowEngine>(c => c.Resolve<FlowEngineService>());
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
