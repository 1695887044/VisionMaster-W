using Core.Interfaces;
using Prism.Ioc;
using System;
using System.Collections.Generic;
using System.Linq;
using VisionMaster.Binding;
using VisionMaster.Lifetime;
using VisionMaster.Scada;
using VisionMaster.Services;
using VisionMaster.Services.Motion;

namespace VisionMaster
{
    /// <summary>
    /// 运动卡模块：把"方案级运动卡资源"这条链路自装配进宿主。
    ///
    /// 与 <see cref="CameraModule"/> 同范式（谁的能力谁负责注册、启动、收尾），两处关键点也一样：
    ///
    /// 1) <b>MotionProvider 既要注册具体类型也要注册接口</b>
    ///    —— 宿主内部（运动卡设置界面、ShellViewModel）拿具体类型用 MarkConfigDirty / SyncFromSolution
    ///       等能力，而流程步骤只认 IMotionProvider（通过 <c>IExecutionContext.Motions</c>）；
    ///       两者必须是**同一个实例**，否则会出现"界面加了卡、流程侧那张表没变"。
    ///
    /// 2) <b>启动顺序放在 CameraModule 之后</b>
    ///    —— 虽然运动与相机没有直接依赖，但运动常常是"视觉定位 → 运动执行"的后半段，
    ///       让相机先就绪更贴合实际使用顺序；更重要的是：运动卡的自动连接会**上电**，
    ///       万一有问题应该排在外围服务（收图/相机）之后，便于在日志里定位。
    ///
    /// 注意本模块**不注册任何视图**：运动卡设置界面的注册在 App.RegisterTypes 里（与相机设置同处），
    /// 这样"模块管能力、App 管入口"的界限与其它模块一致。
    /// </summary>
    public class MotionModule : IAppModule
    {
        public string Name => "运动卡模块";

        public void Register(IContainerRegistry registry)
        {
            registry.RegisterSingleton<MotionProvider>();
            // 接口与具体类型指向同一单例（与相机/通讯模块同口径）
            registry.RegisterSingleton<IMotionProvider>(c => c.Resolve<MotionProvider>());

            // 轴注册表：全局唯一轴名 →（卡 Id + 轴号）的唯一权威。
            // 必须是单例 —— 两份实例各自维护一份索引，表现就是"界面改了轴名，流程解析到的还是旧的"。
            // 它不认识 IMotionDevice：正视这可能让 UI 线程与流程线程同时读写，内部自带锁与自刷新。
            registry.RegisterSingleton<MotionAxisRegistry>(c => new MotionAxisRegistry(
                cardsProvider: () => c.Resolve<IWorkspaceManager>().CurrentSolution?.MotionCards,
                camProvider: () => c.Resolve<IWorkspaceManager>().CurrentSolution?.CamTables));

            // 轴定位器：把注册表与 IMotionProvider 缝起来，给出插件唯一需要的那次调用
            //（按轴名直取"设备 + 轴号"）。注册表保持纯数据、可被无设备测试直接断言，
            // "按卡取设备"这一步只在这里出现一次。
            registry.RegisterSingleton<MotionAxisLocator>(c => new MotionAxisLocator(
                c.Resolve<MotionAxisRegistry>(), c.Resolve<IMotionProvider>()));

            // 轴状态发布器：把运动卡的轮询快照投影成 SCADA 的只读数据源。
            // 具体类型与接口指向同一个单例——宿主内部（生命周期）拿具体类型 Stop/Dispose，
            // 轴面板只认 IScadaAxisSource（Scada.Controls 不引用运动模块）。
            //
            // 系统变量目录：轴状态的第二投影（Motion.X.Position 这类只读变量）。
            // 走工厂而不是类型映射：两个参数都要显式点名——
            //   · workspace：目录把投影写进它的 SystemVariables（注册表挂的正是那一份）；
            //   · registry：轴改名后由它去修**名字索引**（系统变量名是派生量，
            //     改名时集合不增不减、一个 CollectionChanged 都不发，光靠增量维护永远修不到）。
            // 第二个参数是可选参数，交给容器去猜的结果是编译能过、运行起来改名后新名永远查不到。
            registry.RegisterSingleton<SystemVariableCatalog>(c =>
            {
                var catalog = new SystemVariableCatalog(
                    c.Resolve<IWorkspaceManager>(),
                    c.Resolve<IWorkspaceManager>().VariableRegistry);
                // 接到统一宿主（若已注册）：此后运动/IO/未来来源的变量由宿主合并写集合，
                // 任何一方重建都不会误删别人的变量。
                catalog.AttachToHost(c.Resolve<SystemVariableHost>());
                return catalog;
            });

            // 发布器必须显式传系统变量目录：构造函数上的那个参数是**可选参数**，
            // 交给容器去猜的结果是编译能过、运行起来系统变量永远是空的（而且不报错）。
            registry.RegisterSingleton<MotionStatePublisher>(c => new MotionStatePublisher(
                c.Resolve<ILogService>(),
                c.Resolve<IMotionProvider>(),
                c.Resolve<MotionAxisRegistry>(),
                c.Resolve<SystemVariableCatalog>()));
            registry.RegisterSingleton<IScadaAxisSource>(c => c.Resolve<MotionStatePublisher>());

            // 组态动作表的"运动命令族"执行器：持有"哪根轴在点动"的状态与看门狗，必须单例。
            registry.RegisterSingleton<MotionActionExecutor>(c => new MotionActionExecutor(
                c.Resolve<MotionAxisRegistry>(),
                c.Resolve<IMotionProvider>(),
                c.Resolve<ILogService>()));

            // 轴面板上的按钮走同一个执行器（门禁/看门狗只有一份），不另写一条命令链。
            registry.RegisterSingleton<IScadaAxisCommandSink>(c => c.Resolve<MotionActionExecutor>());
        }

        public void Initialize(IContainerProvider container, AppLifetimeService lifetime)
        {
            var provider = container.Resolve<MotionProvider>();

            // 把注册表与设备仓库递给插件侧的取轴入口（静态注入，与 StepConfigOptionSource 同一手法）。
            // 插件是运行期加载进来的独立程序集，拿不到 DI 容器；有了它，
            // 插件里就只剩"给轴名 → 拿设备+轴号"这一次调用，不再出现"选哪张卡"。
            MotionAxisResolution.Attach(
                container.Resolve<MotionAxisRegistry>(), container.Resolve<IMotionProvider>());

            // 启动体检：把历史方案里的轴名重复 / 轴号冲突报到日志。
            // 这件事必须在启动阶段做一次 —— 这类问题不会报错，只会表现为
            // "某根轴怎么都动不了"，而现场没人会往"轴名撞了"上想。
            ReportAxisRegistryProblems(container);

            // 步骤参数候选项来源（轴名 / 卡名）：属性面板据此把这两类参数渲染成下拉，
            // 用户不必再手打逻辑名（打错要到运行期才报"没有名为 X 的轴"）。
            // 放在 Initialize 而不是 Register：候选项要读**当前方案**，
            // 而 Register 阶段容器还没建好实例、方案也还没加载。
            RegisterStepConfigOptions(container);

            // 自动连接：只对勾了"自动连接"的卡生效，失败只记日志不阻断启动。
            // 一张卡连不上（网线没插、IP 冲突、别的程序占用）绝不该让整台设备开不了机 ——
            // 与相机、通讯模块同一口径。
            try
            {
                provider.EnsureSynced();
                provider.ConnectAutoStart();
            }
            catch (Exception ex)
            {
                container.Resolve<ILogService>().Warn(
                    $"[MotionModule] 运动卡自动连接阶段异常（已忽略，可到「运动卡设置」手动连接）：{ex.Message}");
            }

            // 轴状态发布器：自动连接之后再启动，第一拍就能读到已连上的卡；
            // 没有配置任何卡时它只是发布一张空表，不影响启动。
            container.Resolve<MotionStatePublisher>().Start();

            // 退出收尾：先停发布器（它读的是设备缓存），再断开所有卡。
            // Disconnect 会先**安全停止所有轴**——少了它，关软件时正在运动的轴会按最后一条命令继续走完。
            // 运动执行器一并释放：它的看门狗表里若还有点动中的轴，Dispose 会补发停止。
            lifetime.RegisterExitTask(ExitTask.Of("停止轴状态发布并断开全部运动卡", () =>
            {
                container.Resolve<MotionActionExecutor>().Dispose();
                container.Resolve<MotionStatePublisher>().Dispose();
                provider.Dispose();
            }));
        }

        /// <summary>
        /// 把轴注册表发现的历史问题写进日志。
        ///
        /// 只记录、不自动修正：自动改用户的配置等于悄悄动了他没让动的东西
        /// （"谁把我的轴名改了"比"这根轴不动"更难解释）。日志给出**具体是谁和谁冲突**，
        /// 让人自己去「运动卡设置」改，改完即时生效。
        /// </summary>
        private static void ReportAxisRegistryProblems(IContainerProvider container)
        {
            try
            {
                var problems = container.Resolve<MotionAxisRegistry>().Reload();
                if (problems.Count == 0) return;

                var log = container.Resolve<ILogService>();
                log.Warn($"[MotionAxisRegistry] 当前方案的轴映射有 {problems.Count} 处问题"
                         + "（重名的轴只有前者能被解析，未修前请不要依赖同名轴）：");

                foreach (var problem in problems) log.Warn($"[MotionAxisRegistry] · {problem.Message}");

                // 卡名唯一性：IO 这类卡级步骤现在**按卡名**寻址，
                // 两张卡同名时"选到谁"取决于遍历顺序 —— 与轴名重名是同一类静默错误，
                // 必须在启动阶段就点名，而不是等现场发现"IO 写到了另一张卡上"。
                var solution = container.Resolve<IWorkspaceManager>().CurrentSolution;
                var duplicateCardNames = solution?.MotionCards
                    .Where(c => !string.IsNullOrWhiteSpace(c.DisplayName))
                    .GroupBy(c => c.DisplayName.Trim(), StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .ToList();

                if (duplicateCardNames is { Count: > 0 })
                {
                    foreach (var group in duplicateCardNames)
                    {
                        log.Warn($"[MotionAxisRegistry] 卡名「{group.Key}」重复（{group.Count()} 张卡同名）："
                                 + "IO 等卡级步骤按卡名寻址会只命中其中一张，请到「运动卡设置」改名");
                    }
                }
            }
            catch (Exception ex)
            {
                // 体检失败不该挡住启动：它只是诊断，不是运行前提
                container.Resolve<ILogService>().Warn($"[MotionAxisRegistry] 轴映射体检未完成：{ex.Message}");
            }
        }

        /// <summary>
        /// 告诉属性面板"轴名 / 卡地址"的候选从哪里来。
        ///
        /// 候选是**每次渲染属性面板时现取**的（lambda 里解析），所以用户改了轴映射表、
        /// 新加了一张卡，下次打开就是新的 —— 不需要任何"刷新通知"机制。
        ///
        /// 只列**已启用**的轴：未启用的轴在流程里用不了（驱动的命令门禁会拒），
        /// 把它列进候选等于诱导用户选一个必然失败的名字。
        /// </summary>
        private static void RegisterStepConfigOptions(IContainerProvider container)
        {
            // 提供器在契约层：属性面板用它渲染常量下拉，
            // 而**插件的输入端口**（Axis / Card 端口）也用它填 PresetOptions —— 两者共用一份。
            // 用 RegisterAdditional（不是 Register）：通讯模块也会追加"通讯变量名"来源，
            // 两者顺序无关、互不覆盖。
            StepConfigOptionSource.RegisterAdditional(kind =>
            {
                var solution = container.Resolve<IWorkspaceManager>().CurrentSolution;

                return kind switch
                {
                    // 卡名：**值就是卡名**（解析按卡名找设备，与 IP 无关）。
                    //
                    // ★ 同名卡只列一项并置空显示后缀：两张卡同名时"选到谁"取决于遍历顺序，
                    //   与其给用户两个看起来一样、行为却随机的选项，不如列一个 +
                    //   让启动体检把"卡名重复"当成问题报出来（见 ReportAxisRegistryProblems）。
                    StepConfigOptionKind.MotionCardName => solution?.MotionCards
                        .Where(c => !string.IsNullOrWhiteSpace(c.DisplayName))
                        .GroupBy(c => c.DisplayName.Trim(), StringComparer.OrdinalIgnoreCase)
                        .Select(g => new StepConfigOption(g.Key.Trim()))
                        .OrderBy(o => o.Value, StringComparer.OrdinalIgnoreCase)
                        .ToList() ?? new List<StepConfigOption>(),

                    // 轴名候选来自**全局轴注册表**，而不是各卡映射表的并集。
                    // 差别有两处，都是真机上会出事的地方：
                    //   ① 旧写法是 SelectMany + Distinct —— 跨卡重名会坍缩成一个下拉项，
                    //      选中后解析到谁全看遍历顺序，表现是"同一条流程换个方案就动错轴"；
                    //   ② 注册表里**解析不到的名字一律不列**（重名时被影子化的那些）。
                    //      把它列出来等于递给用户一个"选了必然失败"的选项 ——
                    //      他会以为配置里明明有这根轴，却永远动不了，这类问题极难定位。
                    //
                    // 值 = 轴名本体（注册表按它解析），刻意不挂"卡名/轴名"做显示文本：
                    // 一旦显示文本被回填成值，解析就断了（卡地址那条敢带显示名，
                    // 是因为它的值本身就是给人读的地址）。
                    StepConfigOptionKind.MotionAxisName => container.Resolve<MotionAxisRegistry>()
                        .Snapshot()
                        .Where(b => b.Enabled)
                        .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(b => new StepConfigOption(b.Name))
                        .ToList(),

                    _ => new List<StepConfigOption>(),
                };
            });
        }
    }
}
