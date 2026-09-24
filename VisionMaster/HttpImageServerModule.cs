using Core.Interfaces;
using Prism.Ioc;
using System;
using VisionMaster.Lifetime;
using VisionMaster.Services;

namespace VisionMaster
{
    /// <summary>
    /// HTTP 收图模块：把"外部推图 → 触发流程 → 回结果"这条链路自装配进宿主。
    ///
    /// 为什么收图服务要做成模块而不是在 App.xaml.cs 里直接 new
    /// ---------
    /// 与通讯模块、流程引擎模块同范式：谁的能力谁负责注册、启动、收尾。
    /// App 只需要知道"有这么个模块"，不必知道它依赖谁、什么时候起监听、退出时怎么停。
    /// 依赖顺序上，它必须在流程引擎模块之后初始化——收图服务的每一次请求都要
    /// 用引擎跑流程、用运行管理器找会话，这两个东西得先活着。
    /// </summary>
    public class HttpImageServerModule : IAppModule
    {
        public string Name => "HTTP 收图模块";

        public void Register(IContainerRegistry registry)
        {
            registry.RegisterSingleton<FlowOutputCollector>();
            registry.RegisterSingleton<HttpImageServer>();
        }

        public void Initialize(IContainerProvider container, AppLifetimeService lifetime)
        {
            var server = container.Resolve<HttpImageServer>();

            // 监听失败不阻断启动：与通讯模块同口径——收图服务起不来（端口被占等）
            // 只该让这条链路不可用，不该让整台设备开不了机
            try
            {
                server.Start();
            }
            catch (Exception ex)
            {
                container.Resolve<ILogService>().Error($"[HttpImageServer] 启动失败，本次按无收图服务运行：{ex.Message}");
            }

            lifetime.RegisterExitTask(ExitTask.Of("停止 HTTP 收图服务", () => server.Dispose()));
        }
    }
}
