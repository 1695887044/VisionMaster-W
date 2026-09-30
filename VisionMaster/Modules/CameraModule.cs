using Core.Interfaces;
using Prism.Ioc;
using System;
using VisionMaster.Lifetime;
using VisionMaster.Services;

namespace VisionMaster
{
    /// <summary>
    /// 相机模块：把"方案级相机资源"这条链路自装配进宿主。
    ///
    /// 与 HttpImageServerModule 同范式（谁的能力谁负责注册、启动、收尾），但有两处必须说清的差别：
    ///
    /// 1) <b>CameraProvider 既要注册具体类型也要注册接口</b>
    ///    —— 宿主内部（相机设置界面）拿具体类型用 MarkConfigDirty 等能力，
    ///       而驱动插件只认 ICameraProvider（通过 IExecutionContext.Cameras），
    ///       两者必须是同一个实例，否则会出现"界面改了配置，流程侧那张表没变"。
    ///
    /// 2) <b>初始化顺序必须在 HttpImageServer 之后</b>
    ///    —— 本模块在 Initialize 里尝试自动连接相机，而"连上之后就开始收帧"需要收图服务已经能路由。
    ///       顺序反了不会崩，但现场会看到"相机显示已连接，客户端推图却 404"这种自相矛盾的状态。
    /// </summary>
    public class CameraModule : IAppModule
    {
        public string Name => "相机模块";

        public void Register(IContainerRegistry registry)
        {
            registry.RegisterSingleton<CameraProvider>();
            // 接口与具体类型指向同一单例（与通讯模块同口径）
            registry.RegisterSingleton<ICameraProvider>(c => c.Resolve<CameraProvider>());

            registry.RegisterSingleton<NetworkCameraServer>();
        }

        public void Initialize(IContainerProvider container, AppLifetimeService lifetime)
        {
            var provider = container.Resolve<CameraProvider>();

            // 自动连接：只对勾了"自动连接"的相机生效，且失败只记日志不阻断启动。
            // 一台相机连不上（网线没插、被别的程序占用）绝不该让整台设备开不了机——
            // 与通讯模块、收图服务同一口径。
            try
            {
                provider.EnsureSynced();
                provider.ConnectAutoStart();
            }
            catch (Exception ex)
            {
                container.Resolve<ILogService>().Warn($"[CameraModule] 相机自动连接阶段异常（已忽略，可到「系统 → 相机设置」手动连接）：{ex.Message}");
            }

            // 网络相机收图服务：起不来（端口被占等）只让"网络相机"这一类不可用
            var server = container.Resolve<NetworkCameraServer>();
            try
            {
                server.Start();
            }
            catch (Exception ex)
            {
                container.Resolve<ILogService>().Error($"[NetworkCameraServer] 启动失败，本次按无网络相机服务运行：{ex.Message}");
            }

            lifetime.RegisterExitTask(ExitTask.Of("停止网络相机收图服务", () => server.Dispose()));
            lifetime.RegisterExitTask(ExitTask.Of("断开全部相机", () => provider.Dispose()));
        }
    }
}
