using Prism.Ioc;

namespace VisionMaster.Lifetime
{
    /// <summary>
    /// 应用模块契约：通讯/引擎等子系统以模块形式装配回主程序。
    /// 模块自注册服务与生命周期任务，主程序零改动接入新模块。
    /// </summary>
    public interface IAppModule
    {
        /// <summary>模块名（日志用）</summary>
        string Name { get; }

        /// <summary>注册模块服务（在容器构建期调用）</summary>
        void Register(IContainerRegistry registry);

        /// <summary>
        /// 模块初始化（容器就绪后、启动自检前调用）：
        /// 解析自身服务、向 AppLifetimeService 注册自检项/退出任务。
        /// </summary>
        void Initialize(IContainerProvider container, AppLifetimeService lifetime);
    }
}
