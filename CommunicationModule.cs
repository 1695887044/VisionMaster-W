using System;
using Core.Interfaces;
using Prism.Ioc;
using VisionMaster.Lifetime;

namespace VisionMaster.Communications
{
    /// <summary>通讯子系统模块：服务注册 + 启动装配（恢复配置/自动连接）+ 退出任务自声明</summary>
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

            // ===== 启动装配：必须在"自检链执行"与"Shell 构造"之前完成 =====
            // 1) 恢复上次保存的连接配置（communications.json）：通讯自检要按配置逐条测连通，
            //    配置没加载它只能看到空列表；变量绑定与流程运行也都依赖连接已就位。
            // 2) 按 AutoStart 发起自动连接（非阻塞，建连由连接专属线程完成）。
            try
            {
                manager.LoadConfig();
                manager.StartAll();
            }
            catch (Exception ex)
            {
                // 配置文件损坏不应阻断软件启动：记录后按"无通讯配置"继续，用户可在通讯设置里重建
                container.Resolve<ILogService>().Error($"[Comm] 启动装配失败，本次按无通讯配置运行：{ex.Message}");
            }

            lifetime.RegisterExitTask(ExitTask.Of("断开通讯连接", () =>
            {
                manager.StopAll();
                manager.DisconnectAll();
            }));
        }
    }
}
