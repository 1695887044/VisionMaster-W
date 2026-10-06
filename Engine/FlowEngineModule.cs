using Core.Interfaces;
using Prism.Ioc;
using System;
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

            // 「调用方式」里 Timer / Variable / Subroutine 三位的运行侧：
            //  · FlowInvoker：把一条流程当子程序跑（能力经执行上下文递给「调用流程」步骤）；
            //  · FlowTimerScheduler：勾了「定时」的流程按间隔自动跑；
            //  · FlowVariableTriggerService：勾了「变量」的流程在指定变量上升沿跑。
            // 具体类型与接口同实例（理由同上：接口另建一份会拿到"假的"那份）
            registry.RegisterSingleton<FlowInvoker>();
            registry.RegisterSingleton<IFlowInvoker>(c => c.Resolve<FlowInvoker>());
            registry.RegisterSingleton<FlowTimerScheduler>();
            registry.RegisterSingleton<FlowVariableTriggerService>();
        }

        public void Initialize(IContainerProvider container, AppLifetimeService lifetime)
        {
            var engine = container.Resolve<IFlowEngine>();
            var runtime = container.Resolve<IRuntimeManager>();

            // 「调用流程」步骤的能力注入：引擎建执行上下文时把调用器递给插件。
            // 属性注入而不是构造参数：调用器自己要用引擎跑子流程，两者互为引用（环）没法在构造期解
            if (engine is FlowEngineService concreteEngine)
                concreteEngine.FlowInvoker = container.Resolve<FlowInvoker>();

            var timerScheduler = container.Resolve<FlowTimerScheduler>();
            var variableTrigger = container.Resolve<FlowVariableTriggerService>();

            // 自动触发起不来只让对应链路不可用，不阻断开机（与 HTTP 收图模块同一口径）
            try
            {
                timerScheduler.Start();
                variableTrigger.Start();
            }
            catch (Exception ex)
            {
                container.Resolve<ILogService>().Error($"[FlowAutomation] 自动触发服务启动失败，本次按无自动触发运行：{ex.Message}");
            }

            // 退出链按注册逆序执行：先注册的靠后释放，引擎任务先于通讯执行
            lifetime.RegisterExitTask(ExitTask.Of("停止流程引擎", () => engine.StopAll()));
            lifetime.RegisterExitTask(ExitTask.Of("释放运行会话", () => runtime.ClearAll()));
            // 后注册的先执行：自动触发必须先停，否则引擎都停了还有人在往里塞单
            lifetime.RegisterExitTask(ExitTask.Of("停止定时调度", () => timerScheduler.Dispose()));
            lifetime.RegisterExitTask(ExitTask.Of("停止变量触发", () => variableTrigger.Dispose()));
        }
    }
}
