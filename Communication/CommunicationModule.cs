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
            //
            // ⚠ 方案级改造后的口径：连接配置的**唯一事实源是方案**（SolutionModel.CommunicationConfigs），
            //    由 CommunicationSolutionBinder 在"打开/新建/自动加载方案"时整体替换。
            //    这里仍读机器级文件，是为了两条老场景不退化：
            //      · 没有配置任何启动方案（纯壳子启动）时，通讯自检仍有一份配置可测；
            //      · 打开方案时 Apply 会先 StopAll 再逐条移除，机器级那份不会残留成"幽灵连接"。
            //    一旦方案被加载，机器级配置即被整体替换——不再出现"两份配置各说各话"。
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
