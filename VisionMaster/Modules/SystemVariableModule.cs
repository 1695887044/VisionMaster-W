using Core.Interfaces;
using Prism.Ioc;
using VisionMaster.Binding;
using VisionMaster.Communications;
using VisionMaster.Lifetime;
using VisionMaster.Services;
using VisionMaster.Services.SystemVariables;

namespace VisionMaster
{
    /// <summary>
    /// 系统变量模块：把通讯 / 相机两类设备接进统一宿主。
    ///
    /// 为什么单独成模块而不是塞进 CommunicationModule / CameraModule：
    /// ① 两个子系统分属不同工程（Communication 程序集 / VisionMaster），
    ///    "系统变量"是宿主的横切能力，不该让它们互相引用；
    /// ② 初始化顺序要**排在相机模块与运动模块之后**（它俩要先建好设备与发布器），
    ///    单独一个模块才能把这条顺序写在一个地方。
    ///
    /// 运动轴的系统变量仍由 <see cref="MotionModule"/> 注册（与轴状态发布器同生共死）；
    /// 本模块只补"通讯设备"和"相机"两个提供方，三者由 <see cref="SystemVariableHost"/> 合并。
    /// </summary>
    public class SystemVariableModule : IAppModule
    {
        public string Name => "系统变量模块";

        public void Register(IContainerRegistry registry)
        {
            // 通讯：目录只认窄接口，适配器负责读管理器（理由见 CommunicationDeviceStateSource 的注释）
            registry.RegisterSingleton<CommSystemVariableCatalog>(c =>
                new CommSystemVariableCatalog(
                    new CommunicationDeviceStateSource(c.Resolve<ICommunicationManager>())));

            // 相机：ICameraProvider 本身就是窄接口（清单 + 按 Id 取设备）
            registry.RegisterSingleton<CameraSystemVariableCatalog>(c =>
                new CameraSystemVariableCatalog(c.Resolve<ICameraProvider>()));

            // 配方：当前生效配方的名称/编码/版本/偏离/参数摘要，供 SCADA 文本图元只读绑定。
            // 适配器在宿主层（读 RecipeService），目录在 Core（投影成 SystemVariableModel）。
            registry.RegisterSingleton<RecipeVariableSource>(c =>
                new RecipeVariableSource(
                    c.Resolve<RecipeService>(),
                    c.Resolve<IWorkspaceManager>()));
            registry.RegisterSingleton<RecipeVariableCatalog>(c =>
                new RecipeVariableCatalog(c.Resolve<RecipeVariableSource>()));

            // 流程运行统计：订阅引擎每轮事件，投影成 System.Flow.<流程名>.*。
            // 来源在宿主层（要读引擎/工作区/变量注册表），目录在 Core（要调 UpdateMirrorValue）。
            registry.RegisterSingleton<FlowRuntimeVariableSource>(c =>
                new FlowRuntimeVariableSource(
                    c.Resolve<FlowEngineService>(),
                    c.Resolve<IWorkspaceManager>(),
                    c.Resolve<ILogService>()));
            registry.RegisterSingleton<FlowRuntimeVariableCatalog>(c =>
                new FlowRuntimeVariableCatalog(
                    c.Resolve<FlowRuntimeVariableSource>(),
                    c.Resolve<IWorkspaceManager>().VariableRegistry));
        }

        public void Initialize(IContainerProvider container, AppLifetimeService lifetime)
        {
            var host = container.Resolve<SystemVariableHost>();
            var recipe = container.Resolve<RecipeVariableCatalog>();
            var comm = container.Resolve<CommSystemVariableCatalog>();
            var camera = container.Resolve<CameraSystemVariableCatalog>();
            var flow = container.Resolve<FlowRuntimeVariableCatalog>();
            var flowSource = container.Resolve<FlowRuntimeVariableSource>();

            // 注册顺序 = 合并顺序：运动（已在 MotionModule 注册）→ 配方 → 通讯 → 相机 → 流程。
            // 名字/Id 冲突时先注册者优先，这条顺序保证"设备变量"不会遮住运动轴与配方。
            host.Register(recipe);
            host.Register(comm);
            host.Register(camera);
            host.Register(flow);

            // 先同步刷新一拍：不刷的话，启动后 0~500ms 内「变量管理」里看不到通讯/相机变量，
            // 用户第一眼会以为"设备加了但变量没出来"（这类"慢半拍"最容易被当成功能缺失）。
            recipe.Publish();
            comm.Refresh();
            camera.Refresh();
            flow.Publish();

            comm.Start();
            camera.Start();

            var log = container.Resolve<ILogService>();

            // 设备增删只发一次（值变化不发）——方案加载/连接增删时打一行，排查
            // "设备加了但变量没出来" 时有据可查。回调在目录的定时器线程上，ILogService 线程安全。
            comm.Changed += (_, _) =>
                log.Info($"[系统变量] 通讯设备拓扑变化：当前 {comm.Snapshot().Count} 条");
            camera.Changed += (_, _) =>
                log.Info($"[系统变量] 相机拓扑变化：当前 {camera.Snapshot().Count} 条");

            // 启动插桩落 ILogService（R78：弹泡 3 秒就消，日志才留得住）。
            // 计数 = 父变量 + 字段（一台设备 1 + N 条），排查"变量怎么没出来"时先看这一行。
            log.Info($"[系统变量] 配方 {recipe.Snapshot().Count} 条 / 通讯 {comm.Snapshot().Count} 条 / 相机 {camera.Snapshot().Count} 条 / 流程 {flow.Snapshot().Count} 条已接入统一宿主");

            lifetime.RegisterExitTask(ExitTask.Of("停止系统变量刷新（通讯/相机）", () =>
            {
                comm.Dispose();
                camera.Dispose();
            }));

            lifetime.RegisterExitTask(ExitTask.Of("停止流程运行统计", () => flowSource.Dispose()));
        }
    }
}
