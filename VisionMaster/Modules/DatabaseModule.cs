using Core.Database;
using Core.Interfaces;
using Core.Interfaces.Database;
using Prism.Ioc;
using VisionMaster.Lifetime;

namespace VisionMaster
{
    /// <summary>
    /// 数据库模块：把"宿主数据库服务"自装配进宿主。
    ///
    /// 三条纪律（与相机/运动/通讯模块同口径）：
    /// 1) <b>具体类型与接口指向同一个单例</b>：硬件管理数据库页用具体类型做 CRUD，
    ///    流程步骤只认 IDatabaseAccess；两份实例会表现为"页面改了、流程还在用旧配置"。
    /// 2) <b>启动只读盘、不连库</b>：databases.json 读失败只记 Warn 并按空库继续，
    ///    不能让一个坏配置文件把整台设备卡在启动阶段。
    /// 3) <b>退出清连接池</b>：SQLite 连接池持有文件句柄，不清会让"关软件后文件被占用"。
    /// </summary>
    public class DatabaseModule : IAppModule
    {
        public string Name => "数据库模块";

        public void Register(IContainerRegistry registry)
        {
            registry.RegisterSingleton<DatabaseService>(c => new DatabaseService(c.Resolve<ILogService>()));
            registry.RegisterSingleton<IDatabaseAccess>(c => c.Resolve<DatabaseService>());
        }

        public void Initialize(IContainerProvider container, AppLifetimeService lifetime)
        {
            var service = container.Resolve<DatabaseService>();
            var log = container.Resolve<ILogService>();

            // 启动日志：读不到/读坏了要留一行 Warn（与 N3 启动方案日志同一口径）。
            // 这里不连库、不测试连接——数据库离线不应该影响开机。
            if (!string.IsNullOrWhiteSpace(service.LastLoadError))
                log.Warn("[数据库] " + service.LastLoadError);
            else
                log.Info($"[数据库] 已加载 {service.ListProfiles().Count} 个数据库配置（{service.ConfigPath}）");

            lifetime.RegisterExitTask(ExitTask.Of("释放数据库连接池", DatabaseService.ClearPools));
        }
    }
}
