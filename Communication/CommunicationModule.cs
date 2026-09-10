using Prism.Ioc;
using VisionMaster.Lifetime;

namespace VisionMaster.Communications
{
    /// <summary>通讯子系统模块：服务注册 + 退出任务自声明</summary>
    public class CommunicationModule : IAppModule
    {
        public string Name => "通讯模块";

        public void Register(IContainerRegistry registry)
        {
            // 接口与具体类型都注册为单例，保证两种解析拿到同一实例
            registry.RegisterSingleton<AdvancedCommunicationManager>();
            registry.RegisterSingleton<ICommunicationManager, AdvancedCommunicationManager>();
        }

        public void Initialize(IContainerProvider container, AppLifetimeService lifetime)
        {
            var manager = container.Resolve<AdvancedCommunicationManager>();
            lifetime.RegisterExitTask(ExitTask.Of("断开通讯连接", () =>
            {
                manager.StopAll();
                manager.DisconnectAll();
            }));
        }
    }
}
