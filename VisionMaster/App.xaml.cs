using Core.Interfaces;
using Core.Interfaces.Io;
using Prism.Ioc;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using UI.CustomControl;
using VisionMaster.Binding;
using VisionMaster.Communications;
using VisionMaster.Core;
using VisionMaster.Engine;
using VisionMaster.Lifetime;
using VisionMaster.Lifetime.Checks;
using VisionMaster.Services;
using VisionMaster.Services.Hardware;
using VisionMaster.Services.Help;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;
using VisionMaster.Services.Motion;
using VisionMaster.ViewModels;
using VisionMaster.ViewModels.DialogViewModels;
using VisionMaster.Views;
using VisionMaster.Views.DialogViews;

namespace VisionMaster
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : PrismApplication
    {
        private AppLifetimeService? _lifetime;
        private SingleInstanceCheck? _singleInstance;
        private HalconCheck? _halconCheck;
        private CommunicationCheck? _communicationCheck;
        private HelpCatalogCheck? _helpCatalogCheck;
        private readonly CancellationTokenSource _postStartupCts = new();
        private Lifetime.SplashScreen? _splash;
        private readonly List<IAppModule> _modules = new();

        protected override Window CreateShell()
        {
            var thinFont = this.Resources["FA.Light"];
            this.Resources["Icon"] = thinFont;

            // ===== 生命周期宿主：异常分级接线 + 模块初始化 =====
            _lifetime = Container.Resolve<AppLifetimeService>();
            _lifetime.RegisterGlobalExceptionHandlers();
            _lifetime.CrashSceneProvider = DescribeCrashScene;

            // ===== 退出链收尾：日志落盘 =====
            // 退出链逆序执行：此处最先注册 → 最后运行，保证其他退出任务的日志都完整落盘
            _lifetime.RegisterExitTask(ExitTask.Of("日志落盘收尾", () =>
            {
                if (Container.Resolve<ILogService>() is IDisposable d)
                    d.Dispose();
            }));

            // ===== 退出链收尾：未保存草稿转存 =====
            // 注册在"日志落盘收尾"之后 → 逆序执行时先跑，此刻日志服务还活着，转存结果能记进日志。
            // 正常关闭与严重异常退出都会走退出链，所以两条路都受这一项保护。
            _lifetime.RegisterExitTask(ExitTask.Of("未保存草稿转存", () =>
            {
                var result = Container.Resolve<SolutionDraftService>()
                    .Capture(Container.Resolve<WorkspaceContext>().CurrentSolution);

                if (result.Status == DraftCaptureStatus.Captured)
                    Container.Resolve<ILogService>().Warn($"[退出链] 方案有未保存改动，已转存草稿：{result.Path}");
                else if (result.Status == DraftCaptureStatus.Failed)
                    Container.Resolve<ILogService>().Warn($"[退出链] 草稿转存失败：{result.Detail}");
            }, 8000));

            // 通信层日志接进 UI 日志窗口：manager 内部日志原本只写 Console（WPF 无控制台=黑洞），
            // 变量注册/轮询/读取失败等诊断必须可见。
            // 必须在模块初始化之前挂接：通讯模块的启动装配（加载配置 + 自动连接）日志也在其中
            AdvancedCommunicationManager.LogSink = Container.Resolve<ILogService>();

            foreach (var module in _modules)
                module.Initialize(Container, _lifetime); // 模块注册自检项/退出任务，须在自检前完成

            // 图像集采集服务：在这里强制构造一次，让它的订阅（FlowRunCompleted / 实时预览）
            // 先于任何一次流程运行挂上。否则"第一次运行没进图集、第二次才有"这种时序 bug
            // 会一直等到有人点开图像页时才消失，很难查。
            Container.Resolve<ImageCollectionService>();

            // ===== 曲线图元注册（必须显式调，见 ScadaChartElements 的类注释）=====
            // 曲线图元在另一个程序集里，跨程序集的类型静态构造不会因为"引用了 DLL"就跑，
            // 所以不像内置图元那样自动注册。这里调一次；结果是"工具箱里有没有曲线"，
            // 忘了调不报任何错——由 ScadaChecks 的断言兜住（那边也会调一次）。
            VisionMaster.Scada.Controls.Charts.ScadaChartElements.Register();
            foreach (var chartError in VisionMaster.Scada.Controls.Charts.ScadaChartElements.Errors)
                Container.Resolve<ILogService>().Warn($"曲线图元注册失败：{chartError}");

            // ===== 启动自检链（按执行顺序注册）=====
            _singleInstance = new SingleInstanceCheck();
            _lifetime.RegisterCheck(_singleInstance);
            _lifetime.RegisterCheck(new ConfigCheck(Container.Resolve<AppSettingsService>(), Container.Resolve<ILogService>()));
            _halconCheck = new HalconCheck(Container.Resolve<ILogService>());
            _lifetime.RegisterCheck(_halconCheck);
            _lifetime.RegisterCheck(new PluginScanCheck(
                () => Container.Resolve<PluginService>(),
                () => Container.Resolve<IPluginProvider>(),
                Container.Resolve<ILogService>()));
            // 帮助手册合并**不进启动自检链**（2026-10-16，与通讯连通性同一先例 R57）：
            // 它只给帮助弹窗供数、失败全是 Warning，实测却要约 1.2 秒——让"只报不拦"的项守在
            // 进主界面的关键路径上不值得。改由 OnInitialized 在主窗口显示后后台跑；
            // 且 HelpViewModel.OnDialogOpened 已有懒构建兜底（!IsBuilt → Build），
            // 后台没跑完时用户打开帮助会当场构建，行为不降级。
            _helpCatalogCheck = new HelpCatalogCheck(
                () => Container.Resolve<HelpCatalogService>(),
                Container.Resolve<ILogService>());
            // 通讯连通性**不进启动自检链**（2026-10-16）：它只出 Warning、不拦启动，却要等
            // 3 秒追赶窗口 + 每条最多 3 秒（总预算 12 秒）——让"只报不拦"的诊断项站在进主界面的
            // 关键路径上不值得。改由 OnInitialized 在主窗口显示后后台跑，结果写日志（口径不变）。
            _communicationCheck = new CommunicationCheck(
                () => Container.Resolve<AdvancedCommunicationManager>(),
                () => Container.Resolve<AppSettingsService>().Current,
                Container.Resolve<ILogService>());

            // ===== Splash 进度窗 + 自检执行（Dispatcher 帧泵保持 Splash 响应）=====
            // 注意：Splash 在主窗口显示后才关闭（OnInitialized），避免出现“零窗口”触发 Shutdown
            _splash = new Lifetime.SplashScreen();
            _splash.InitChecks(_lifetime.Checks);
            _lifetime.CheckCompleted += e => _splash.UpdateCheck(e.Name, e.Passed, e.Result, e.Done, e.Total);
            _splash.Show();

            var result = RunWithPump(() => _lifetime.RunStartupChecksAsync());
            if (!result.Success)
            {
                // Error 级失败：展示原因后终止，不进入主界面
                _splash.ShowBlockingFailure(result.BlockingReason ?? "未知原因");
                Thread.Sleep(3000);
                _splash.Close();
                Environment.Exit(2);
            }
            _splash.SetFinished(result.Warnings);
            RunWithPump(async () => { await Task.Delay(900); return true; }); // 停留片刻展示“启动完成/警告”
            return Container.Resolve<Shell>();
        }

        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            // 且 WPF+Halcon 宿主常态内存 >300MB 阈值，表现为"空闲双击也卡"。
            // .NET 自身 GC 对本应用足够高效；如将来确有内存增长问题，再评估温和化巡检。
            // MemoryManager.Instance.Start(300, 30);

            // ===== 基础服务最先注册：Unity 注册工厂时可能提前解析依赖，必须保证已就位 =====
            containerRegistry.RegisterSingleton<ILogService, LogService>();
            containerRegistry.RegisterSingleton<IPerformanceMonitor, PerformanceMonitor>();
            containerRegistry.RegisterSingleton<AppSettingsService>();
            containerRegistry.RegisterSingleton<AppLifetimeService>();
            containerRegistry.RegisterSingleton<IUserNotifier, UserNotifier>();

            // ===== 模块注册：通讯/引擎子系统自装配 =====
            _modules.Add(new CommunicationModule());
            _modules.Add(new FlowEngineModule());

            // IO → 通讯 折算钩子：加载方案时把旧 IO 配置折成"连接 + 网络变量"。
            // 为什么用钩子：Core 引用不到 Communication 的配置/地址类型（依赖方向不允许反过来），
            // 所以真正的翻译动作由本层注入。未挂接时跳过折算，旧 IO 字段照常可用。
            // 注意：RegisterTypes 阶段容器尚未构建，这里**不能** Resolve，只写 Debug 痕迹。
            SolutionService.IoMigrationHook = solution =>
            {
                var (connections, variables) = VisionMaster.Communications.IoMigrationApplier.Apply(solution);
                if (connections > 0 || variables > 0)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[IO→通讯] 已折算 {connections} 条连接 / {variables} 个变量");
                }
            };
            // 收图服务放在最后：它初始化时要解析 IRuntimeManager / IFlowEngine / FlowCompiler，
            // 这几个都由上面的引擎模块注册，顺序错了 Initialize 会解析不到
            _modules.Add(new HttpImageServerModule());
            // 相机模块排在收图服务之后：它初始化时会尝试自动连接相机，
            // 而"连上即开始收帧"这条链路需要收图服务已经就绪
            _modules.Add(new CameraModule());
            // 运动卡模块排在相机之后：运动常常是"视觉定位 → 运动执行"的后半段，
            // 让相机先就绪更贴合实际使用顺序；而且运动卡自动连接会**给伺服上电**，
            // 这类会动的东西排在收图/相机等外围服务之后，出问题时日志里更好定位。
            _modules.Add(new MotionModule());
            // 注：IO 接入层已下线——设备/点位配置改由"通讯连接 + 网络变量"承载
            // （旧 IO 配置在方案加载时由 IoMigrationHook 一次性折算，见下方注册）。
            foreach (var module in _modules)
                module.Register(containerRegistry);

            containerRegistry.RegisterSingleton<SolutionService>();
            containerRegistry.RegisterSingleton<SolutionDraftService>();
            containerRegistry.RegisterSingleton<WorkspaceContext>();
            // 写租约：与具体协议无关（键是 "{连接名}.{变量名}"），网络变量的显式写走它仲裁，
            // 并行分支抢同一个输出时后者被明确拒绝，而不是"最后写入者赢"。
            containerRegistry.RegisterSingleton<VisionMaster.Services.WriteLeaseRegistry>();
            containerRegistry.RegisterSingleton<NetworkVariableBridge>();
            // 图像集：把流程输出的图像按流程收成一个集合，供视觉图像页"图集模式"显示
            containerRegistry.RegisterSingleton<ImageCollectionService>();
            containerRegistry.Register<IReadOnlyWorkspaceContext>(c => c.Resolve<WorkspaceContext>());
            containerRegistry.Register<IWorkspaceManager>(c => c.Resolve<WorkspaceContext>());
            containerRegistry.RegisterForNavigation<LogView,LogViewModel>();
            containerRegistry.RegisterForNavigation<ProcessView, ProcessViewModel>();
            containerRegistry.RegisterForNavigation<MonitorView, MonitorViewModel>();
            containerRegistry.RegisterForNavigation<ToolView, ToolViewModel>();
            // 组态编辑器的视图模型必须是单例：画布宿主与属性面板要靠"同一个 SelectedElement"联动，
            // 各自一份就得引入事件总线才能对齐选中态。RegisterForNavigation 造视图时经由容器
            // 解析视图模型，singleton 覆盖默认的单次构造，所以两处拿到的是同一个实例。
            containerRegistry.RegisterSingleton<ScadaEditorViewModel>();
            containerRegistry.RegisterForNavigation<ScadaEditorView, ScadaEditorViewModel>();
            containerRegistry.RegisterForNavigation<ScadaToolboxView, ScadaToolboxViewModel>();
            containerRegistry.RegisterForNavigation<ScadaPropertyView, ScadaPropertyViewModel>();
            // 图层面板：与属性面板共用同一个 ScadaEditorViewModel 单例，靠它的 SelectedPage 取当前画面。
            // 不是单例——面板跟着视图入树/离树 Activate/Deactivate，一份实例只服务一个视图实例最省心。
            containerRegistry.RegisterForNavigation<ScadaLayerView, ScadaLayerViewModel>();
            // 操作审计落盘端（S12）。**全局一份**：审计是"谁在什么时候按了哪个按钮"的凭证，
            // 两份实例各自往同一个文件追加写，出来的会是交错的行（连表头都会被写两遍）。
            // 目录固定在程序目录下的 Audit——与 Logs（可随时清）、Alarms（报警资产）各占一个：
            // 混在一起早晚会被"清理日志"一起删掉，而凭证恰恰是不能删的那一份。
            // 诊断口与报警历史/数据泵同口径（写不成必须能在日志里看见）：它不影响操作能否成功，
            // 但影响事后追责可不可信——"审计里没有这一条"和"审计没记成"是两件事，
            // 前者是没发生，后者是记丢了，只靠一个空文件分不出来。
            // retentionProvider 同样是**显式传**（S13-d）：保留期现在是软件级配置项
            // （「系统 → 系统参数设置」可改），每次清理时现读——改完不必重启，也不必等下一次跨天。
            // 若不传，构造参数会回落成"构造时定死"，那正是这次要拆掉的写死行为。
            containerRegistry.RegisterSingleton<ScadaAuditWriter>(c => new ScadaAuditWriter(
                ScadaAuditWriter.DefaultDirectory,
                (level, message) =>
                {
                    var log = c.Resolve<ILogService>();
                    if (level >= ScadaDiagnosticLevel.Error)
                        log.Error(message);
                    else
                        log.Warn(message);
                },
                retentionProvider: () => c.Resolve<AppSettingsService>().Current.AuditRetentionDays));
            // 运行态宿主必须是单例：它管的就是"同一时刻只允许一个运行窗口"这条策略，
            // 每个调用方解析出一份新实例，等于这条策略根本不存在（两份会话各自写同一批变量）。
            containerRegistry.RegisterSingleton<IScadaRuntimeHost, ScadaRuntimeHost>();
            // 运行态的数据通道：把工作区的变量注册表接成领域层认的"值源"（见 IScadaValueSource）。
            // 走工厂而不是类型映射，是因为注册表挂在 WorkspaceContext 上、不单独注册——
            // 这样"变量只有一个索引"这条约束不会被绕开（多一份索引就会分裂）。
            containerRegistry.RegisterSingleton<IScadaValueSource>(
                c => new RegistryScadaValueSource(c.Resolve<IWorkspaceManager>().VariableRegistry));
            // 运行态的回写通道：图元输入框（I/O 域）把操作员敲的字送回变量。与值源是一对，
            // 所以紧挨着注册——一读一写两条方向摆在相邻两行，"写变量这条路是怎么接上的"一眼看得完。
            // 同样走工厂而不是类型映射：转换规则必须只认 VariableValueConverter 一份
            //（由 ScadaValueWriter 自己保证），而这里显式点名实现类，将来换成
            // "要审计每一次手动改值"或"要二次确认弹窗"时，改动点就在这一行上。
            containerRegistry.RegisterSingleton<IScadaValueWriter>(
                c => new ScadaValueWriter(c.Resolve<ILogService>(), c.Resolve<IScadaValueSource>()));
            // 动作分发器同样是单例：它无状态（只往日志/变量/导航出口递话），而每次运行都 new 一份
            // 只会让人误以为"动作的执行状态存在某处"——将来接 S6 写变量时这里换成带状态的实现也不动调用方。
            // 走工厂而不是类型映射：audit 在构造函数上是**可选参数**（默认 null），
            // 让容器去猜"这个可选参数要不要注入"，结果多半是编译能过、运行起来审计文件一直空着
            // （而且不报错）。这里显式传进去，"接上审计"这件事在注册处一眼可见。
            containerRegistry.RegisterSingleton<IScadaActionDispatcher>(c => new ScadaActionDispatcher(
                c.Resolve<ILogService>(),
                c.Resolve<IScadaValueSource>(),
                c.Resolve<IScadaNavigator>(),
                c.Resolve<ScadaAuditWriter>(),
                c.Resolve<IScadaFlowRunner>(),
                // 运动命令族执行器：同样是可选参数（断言工程里 16 处 4~5 参构造不该为它解析运动依赖）。
                // 这里显式传进去——交给容器去猜的结果是编译能过、运行起来点「轴定位」只记一行
                // "未接入运动模块"（而且不报错）。它持有"哪根轴在点动"的状态与看门狗，所以是单例。
                c.Resolve<MotionActionExecutor>()));
            // 运行态的切页出口：分发器是长命单例，运行态会话是每次点"运行"现建、关窗口即废，
            // 中间得有人记住"此刻活着的是哪一个会话"。宿主 Start 时 Attach、关窗口收尾时摘掉。
            // 也必须是单例：两份实例各记一个会话，动作落到哪一份上就变成了随机。
            containerRegistry.RegisterSingleton<ScadaNavigator>();
            containerRegistry.RegisterSingleton<IScadaNavigator>(c => c.Resolve<ScadaNavigator>());
            // 流程启停出口：SCADA 事件动作的「启动流程 / 停止流程 / 执行一次」经它落到流程引擎上。
            // 单例：引擎、运行管理器、编译器都是单例，本类只是它们面前的一层薄翻译，
            // 两份实例不会有第二份状态（无字段可分裂），但仍按仓库惯例映射同一实例。
            // 走工厂而不是类型映射：它的五个依赖全部已注册为单例，显式点名实现类是为了
            // "这条链上用的就是这一份"在注册处一眼可见（与 IScadaValueSource 同一手法）。
            containerRegistry.RegisterSingleton<ScadaFlowRunner>();
            containerRegistry.RegisterSingleton<IScadaFlowRunner>(c => c.Resolve<ScadaFlowRunner>());
            // 运行态权限出口（S12）：**"此刻是谁登录着"全局只有一份**，所以必须是单例——
            // 两份实例的表现是"状态栏显示已登录，而权限判定用的是另一份（未登录）"，
            // 现场看起来就是"登录了还是按不动"，而且这种 bug 极难往"注册了两份"上想。
            // 宿主按接口取它（只问"够不够格"），登录界面按具体类型取它（要能 Login/Logout/Touch——
            // 那三个方法刻意不在接口上，见 IScadaAccessPolicy 的契约③）。
            // 走工厂而不是类型映射（S13-d）：构造函数的 idleTimeoutProvider 是**可选参数**，
            // 交给容器去猜的结果是编译能过、运行起来永远按兜底的 10 分钟踢人——而「系统 → 系统参数设置」
            // 里那个改完不生效的输入框，没人会往"注册处没注入"上想。
            // 注入的是"每次现读配置"的口：改完立刻生效是结构上保证的，不靠谁记得把新值推过来。
            containerRegistry.RegisterSingleton<ScadaAccessPolicy>(c =>
                new ScadaAccessPolicy(() => c.Resolve<AppSettingsService>().Current.IdleTimeout));
            containerRegistry.RegisterSingleton<IScadaAccessPolicy>(c => c.Resolve<ScadaAccessPolicy>());
            // 账号存储（"谁能登录、登录后是几档"的唯一一份数据）也必须是单例：
            // 登录弹窗验身份、用户管理弹窗增删改，读写的必须是同一份清单，
            // 两份实例的表现是"刚建的账号登录时说不存在"。
            // 走工厂而不是类型映射：构造函数的 storePath 是可选参数（null = 程序目录下
            // ScadaUsers.json），显式传 null 比让容器去猜"这个 string 从哪来"更清楚。
            // 另两个参数同理**必须显式传**（S13-c）：audit 与 actorProvider 都是可选参数（默认 null），
            // 交给容器去猜的结果是编译能过、运行起来"改密码"一条审计都不落，而且不报任何错——
            // 这正是审计最怕的失效方式（漏记而无人知道）。显式写在这里，"账号操作已接审计"一眼可见。
            // actorProvider 取的是"此刻登录着谁"：账号存储本身不认识会话，所以由宿主把
            // ScadaAccessPolicy.CurrentUserName 递进去；取不到时 ScadaAuditEntry 自己回落"未登录"，
            // 那本身也是一条线索（"有人未登录就改了账号"）。注意这里必须 Resolve 具体类型
            // ScadaAccessPolicy 而不是接口——Login/Logout/Touch 刻意不在 IScadaAccessPolicy 上，
            // 但 CurrentUserName 在，两者都行；用具体类型是为了与登录弹窗取到**同一个实例**。
            containerRegistry.RegisterSingleton<ScadaUserStore>(c =>
            {
                var policy = c.Resolve<ScadaAccessPolicy>();
                return new ScadaUserStore(
                    storePath: null,
                    audit: c.Resolve<ScadaAuditWriter>(),
                    actorProvider: () => policy.CurrentUserName);
            });
            // 组态「选变量」入口：属性面板只说"给我一个变量"，弹窗怎么弹、弹哪个由这一层决定。
            // 面板与事件行因此能在无 WPF 宿主下被直接构造、直接断言（ScadaChecks 就是这么测的）。
            containerRegistry.RegisterSingleton<IScadaVariablePicker, ScadaVariablePicker>();
            containerRegistry.RegisterDialog<ScadaVariablePickerView, ScadaVariablePickerViewModel>(
                ScadaVariablePicker.DialogName);
            // 组态「选轴」入口：轴面板的设计期候选清单。候选来自 MotionAxisRegistry（只列已启用轴），
            // 与变量选择器同一手法——面板不依赖 Prism 弹窗，也不认识运动模块。
            containerRegistry.RegisterSingleton<IScadaAxisPicker, ScadaAxisPicker>();
            containerRegistry.RegisterDialog<ScadaAxisPickerView, ScadaAxisPickerViewModel>(
                ScadaAxisPicker.DialogName);
            // 组态「选画面」入口：与「选变量」同一手法——清单来源不同（画面来自当前方案，
            // 变量来自工程变量表），所以是两个入口而不是一个带枚举的入口：
            // 运行态两边的"选得到就一定生效"承诺各自成立，混成一个反而要在这层判类型。
            containerRegistry.RegisterSingleton<IScadaPagePicker, ScadaPagePicker>();
            containerRegistry.RegisterDialog<ScadaPagePickerView, ScadaPagePickerViewModel>(
                ScadaPagePicker.DialogName);
            // 组态「选流程」入口：与上两个同一手法——清单来源不同（流程来自当前方案的流程集合），
            // 目标身份是字符串型的 FlowID（不是 Guid），所以另立一个入口而不是给万能选择器加档位：
            // 运行态"选得到就一定跑得到"的承诺与另两个各自成立。
            containerRegistry.RegisterSingleton<IScadaFlowPicker, ScadaFlowPicker>();
            containerRegistry.RegisterDialog<ScadaFlowPickerView, ScadaFlowPickerViewModel>(
                ScadaFlowPicker.DialogName);
            containerRegistry.RegisterDialog<VariableBindingView, VariableBindingViewModel>("DataBindView");
            containerRegistry.RegisterDialog<GlobalVariableView, GlobalVariableManagerViewModel>("GlobalVariable");
            containerRegistry.RegisterDialog<ConditionEditorView, ConditionEditorViewModel>("ConditionEditor");
            // 并行分组参数已收编为标准属性面板（EasyDialog.ShowPropertyGridSync + FlatPropertyGrid，
            // 2026-10-09 用户裁决"没必要新建一个视图"）：不再注册专属弹窗 ParallelGroupConfigView，
            // 分派点在 StepParameterDialog（命中窗的「打开模块参数」与流程栏共用同一处）。
            containerRegistry.RegisterDialog<FlowManagerView, FlowManagerViewModel>("FlowManagerView");
            containerRegistry.RegisterDialog<CommunicationSettingsView, CommunicationSettingsViewModel>("CommunicationSettingsView");

            // 系统健康：三域只读诊断（通讯/相机/运动）。
            // IO 域已随 IO 接入层下线——IO 点位现在就是网络变量，其采集健康度看通讯域。
            // 三个采集器用显式工厂注册：构造函数里带可选委托（时钟等），
            // 让 Unity 自己挑构造函数会去解析无法注册的 Func<long>。
            containerRegistry.RegisterSingleton<Services.Health.CommunicationHealthMonitor>(c =>
                new Services.Health.CommunicationHealthMonitor(
                    c.Resolve<AdvancedCommunicationManager>()));
            containerRegistry.RegisterSingleton<Services.Health.CameraHealthMonitor>(c =>
                new Services.Health.CameraHealthMonitor(c.Resolve<ICameraProvider>()));
            containerRegistry.RegisterSingleton<Services.Health.MotionHealthMonitor>(c =>
                new Services.Health.MotionHealthMonitor(c.Resolve<IMotionProvider>()));
            containerRegistry.RegisterDialog<SystemHealthView, SystemHealthViewModel>("SystemHealthView");
            // 相机设置：方案级相机资源（增删相机 / 连接 / 采流 / 参数 / 溢出计数 / 预览）。
            // 与通讯设置同一范式，但相机配置不走 Manager——它直接就是 SolutionModel.CameraConfigs，
            // 弹窗从"当前方案"现取，故 ShellViewModel 侧不传任何参数（见 ShowCameraSettings）。
            containerRegistry.RegisterDialog<CameraSettingsView, CameraSettingsViewModel>("CameraSettingsView");
            // 运动板卡：共享左栏选卡 + 四页签（卡设置 / 手动调试 / 点位列表 / 电子凸轮）。
            // 旧的"运动设置 / 手动调试 / 轴点位表"三个独立弹窗已退役 —— UI 无入口（SystemAction 只发
            // MotionBoard），功能由板卡对应页签承担；点动看门狗的安全判断已收敛进 MotionJogGuard。
            containerRegistry.RegisterDialog<MotionBoardView, MotionBoardViewModel>("MotionBoardView");
            // 硬件管理（批次 A/B）：统一入口 + 只读概览；批次 C 起嵌入三个域页面。
            // 门禁与运行态查询在宿主层注册一次，三个嵌入页共用同一份判定。
            containerRegistry.RegisterSingleton<IFlowRunState, WorkspaceFlowRunState>();
            containerRegistry.RegisterSingleton<IHardwareEditGate, HardwareEditGate>();
            containerRegistry.RegisterDialog<HardwareManagerView, HardwareManagerViewModel>("HardwareManagerView");
            // 扫描组编辑器：从连接设置的设备表格操作列进入，编辑的是**连接级**的组表
            // （一条连接一个组表；变量只存组名引用，见 ScanGroupEditorViewModel 注释）
            containerRegistry.RegisterDialog<ScanGroupEditorView, ScanGroupEditorViewModel>(
                ScanGroupEditorViewModel.DialogName);
            // 系统设置（合并窗口）：运行窗口形态 / 落屏 + 安全与会话 + 历史留档 + 画布（图集）。
            // 原先的「运行窗口设置」「系统参数设置」两个弹窗写的是同一份东西（程序目录 AppConfig.json），
            // 口径也逐条相同，现已合并成一个弹窗、由 CardPropertyGrid 按草稿类反射生成正文。
            // 旧的两个 SystemAction 枚举值保留（整数值可能已落进 Layout.xml / 旧配置），
            // 由 ShellViewModel 转发到本弹窗的对应页签。
            containerRegistry.RegisterDialog<ScadaSystemSettingsView, ScadaSystemSettingsViewModel>(
                ScadaSystemSettingsViewModel.DialogName);
            // 变量事件（按变量配置值驱动的规则：更改数值 / 值为真 / 值为假 / 上限 / 下限）。
            // 与上面两个弹窗相反，它改的是**方案内容**（落 .vms、进撤销栈），所以清单来自
            // 当前方案的 ScadaDocument.VariableEvents + 工程变量表（IWorkspaceManager.GlobalVariables），
            // 弹窗打开时现读——两次打开之间用户可能去变量管理里增删过变量，缓存下来就是幽灵条目。
            containerRegistry.RegisterDialog<ScadaVariableEventDialogView, ScadaVariableEventDialogViewModel>("ScadaVariableEventDialogView");
            // 报警配置（方案级：一张平铺表，增删报警定义、就地改阈值 / 严重度 / 报警组）。
            // 它同样改的是**方案内容**（落 .vms、进撤销栈），清单来自当前方案的
            // ScadaDocument.Alarms，弹窗打开时现读；入口挂在「文件」菜单的 SolutionAction.AlarmConfig 上，
            // 于是自动继承"运行中禁用"的互锁（见 ShellViewModel.CanExecuteSolution）。
            containerRegistry.RegisterDialog<ScadaAlarmConfigDialogView, ScadaAlarmConfigDialogViewModel>("ScadaAlarmConfigDialogView");
            // ===== 配置类弹窗的审计注入（S13-f）=====
            // 下面三个弹窗的 VM 构造函数上都有一对**可选参数**（audit / actorProvider，默认 null）。
            // 交给容器去猜的结果与 ScadaUserStore / ScadaActionDispatcher 逐字相同：
            // 编译能过、运行起来"改了参数一条审计都没有"而且**不报任何错**——
            // 这正是审计最怕的失效方式（漏记而无人知道），所以这里显式接上，"改配置已留凭证"一眼可见。
            //
            // 为什么走 ViewModelLocationProvider 的工厂，而不是往容器里再注册一遍 VM 类型：
            // ① RegisterTypes 这一刻手上只有 IContainerRegistry，拿不到可用的 IContainerProvider
            //    （本仓 Prism 包里没有 GetContainer / PrismIocExtensions 那条路，已验证）；
            //    工厂闭包里的 Resolve 发生在**视图被造出来时**，那时容器早已就绪。
            // ② 它优先于 AutoWireViewModel 的容器解析，所以"这三个 VM 注入了什么"在这几行里看得完，
            //    不必再去追容器把哪个可选参数填成了什么。
            // actorProvider 递的是"此刻登录着谁"（与 ScadaUserStore 同源）：VM 本身不认识会话；
            // 取不到时 ScadaAuditEntry 自己回落"未登录"——那本身也是一条线索（有人没登录就改了配置）。
            Prism.Mvvm.ViewModelLocationProvider.Register<ScadaSystemSettingsView>(() =>
                new ScadaSystemSettingsViewModel(
                    Container.Resolve<AppSettingsService>(),
                    Container.Resolve<ScadaAuditWriter>(),
                    () => Container.Resolve<ScadaAccessPolicy>().CurrentUserName));
            // 变量事件弹窗同一手法（它的 audit / actorProvider 同样是可选参数，理由见上）。
            // 它比上面几个更依赖这一行：本弹窗的每一次勾选、每一次改阈值都当场写回模型，
            // 没有"确定"这个统一的提交点——所以漏接审计的话，一条线索都不会留下。
            // 三个 picker 也必须显式传：它们是 5 个事件编辑器构造时用的，
            // 交给容器去猜的结果是编译能过、运行起来点「选择变量」没反应。
            Prism.Mvvm.ViewModelLocationProvider.Register<ScadaVariableEventDialogView>(() =>
                new ScadaVariableEventDialogViewModel(
                    Container.Resolve<IWorkspaceManager>(),
                    Container.Resolve<IScadaVariablePicker>(),
                    Container.Resolve<IScadaPagePicker>(),
                    Container.Resolve<IScadaFlowPicker>(),
                    Container.Resolve<ScadaAuditWriter>(),
                    () => Container.Resolve<ScadaAccessPolicy>().CurrentUserName,
                    // 第四个 picker（轴）：运动命令动作行的「选轴」用。同样是可选参数，
                    // 漏传的表现是"按钮灰着、点了没反应"，而不是编译错误。
                    Container.Resolve<IScadaAxisPicker>()));
            // 报警配置弹窗同一手法。它同样"每一次编辑都当场写回模型"，没有统一的提交点，
            // 所以 audit / actorProvider 两个可选参数漏接的话，一条线索都不会留下。
            // picker 也必须显式传：它是"为报警选监视变量"那个按钮用的，
            // 交给容器去猜的结果是编译能过、运行起来点「选择变量」没反应。
            Prism.Mvvm.ViewModelLocationProvider.Register<ScadaAlarmConfigDialogView>(() =>
                new ScadaAlarmConfigDialogViewModel(
                    Container.Resolve<IWorkspaceManager>(),
                    Container.Resolve<IScadaVariablePicker>(),
                    Container.Resolve<ScadaAuditWriter>(),
                    () => Container.Resolve<ScadaAccessPolicy>().CurrentUserName));
            // 权限体系的两个弹窗（S12）。注册名与调用点逐字一致：
            // "ScadaLoginView" ← ShellViewModel.OnSystemAction(SystemAction.UserLogin) + 状态栏点击
            // "ScadaUserManagerView" ← ScadaLoginViewModel.OnOpenUserManager（登录弹窗里的「用户管理」）
            // 两个 VM 都由容器装配（AutoWireViewModel），所以构造注入的 ScadaUserStore /
            // ScadaAccessPolicy / IDialogService 必须已经在上面注册好——这也是它们排在这里的原因。
            containerRegistry.RegisterDialog<ScadaLoginView, ScadaLoginViewModel>("ScadaLoginView");
            containerRegistry.RegisterDialog<ScadaUserManagerView, ScadaUserManagerViewModel>("ScadaUserManagerView");
            containerRegistry.RegisterDialog<PluginConfigShellView, PluginConfigShellViewModel>("PluginConfigShell");
            // 帮助手册：目录合并已从启动自检链挪到主窗口显示后后台跑（OnInitialized →
            // RunHelpCatalogCheckInBackground），这里注册服务与弹窗两件事。服务走工厂显式传依赖，
            // 与上面几处"可选参数显式传"同一手法——交给容器去猜的结果是编译能过、运行起来目录永远空着。
            // 懒构建兜底在 HelpViewModel.OnDialogOpened（后台没跑完时当场 Build）。
            containerRegistry.RegisterSingleton<HelpCatalogService>(c => new HelpCatalogService(
                c.Resolve<IPluginProvider>(),
                c.Resolve<ILogService>()));
            containerRegistry.RegisterDialog<HelpView, HelpViewModel>("HelpView");
            containerRegistry.RegisterDialog<SolutionListView, SolutionListViewModel>("SolutionListView");
            // 方案清单弹窗的审计注入：与上面两个弹窗同一手法（理由见「配置类弹窗的审计注入」段）。
            // 这一项改的是"开机自动打开哪份方案"，改错了要到第二天开机才发现，更需要留凭证。
            Prism.Mvvm.ViewModelLocationProvider.Register<SolutionListView>(() =>
                new SolutionListViewModel(
                    Container.Resolve<SolutionService>(),
                    Container.Resolve<IWorkspaceManager>(),
                    Container.Resolve<AppSettingsService>(),
                    Container.Resolve<ScadaAuditWriter>(),
                    () => Container.Resolve<ScadaAccessPolicy>().CurrentUserName));
            containerRegistry.RegisterForNavigation<Shell, ShellViewModel>();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 先叫停后台诊断：通讯连通性检测跑在退出链之外，不取消就会在关闭过程中继续碰连接管理器
            _postStartupCts.Cancel();
            try { _lifetime?.ExecuteExitChainBlocking("应用退出"); }
            catch { /* 退出链内部已逐项捕获，此处兜底 */ }
            _postStartupCts.Dispose();
            _singleInstance?.Dispose();
            base.OnExit(e);
        }

        /// <summary>主窗口已显示后关闭 Splash（base.OnInitialized 内部会 Show 主窗口）</summary>
        protected override void OnInitialized()
        {
            base.OnInitialized();
            _splash?.Close();
            _splash = null;

            HookGlobalActivity();

            // 上次异常退出/有未保存草稿的提示：延到界面空闲再弹，别卡在启动链上
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(ShowRecoveryNotice));

            // HALCON 不可用的提示：同样延到界面空闲 —— 自检那一刻 Splash 转瞬即逝，
            // 详细说明必须在主界面出来之后才看得见
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(ShowHalconNotice));

            // 通讯连通性：主窗口已显示，自动连接此刻已爬过一会儿，检测放后台跑、不占启动时间
            RunCommunicationCheckInBackground();

            // 帮助手册合并：与通讯连通性同一先例——只报不拦、不站在启动关键路径上（见 OnInitialized 的注释）
            RunHelpCatalogCheckInBackground();
        }

        /// <summary>
        /// 后台跑一次帮助手册合并，结果只写日志——不弹窗、不拦启动。
        ///
        /// 为什么挪出启动自检链
        /// ---------
        /// 它只给帮助弹窗供数：合并出来的目录是给「帮助」窗口看的，不是主界面可用的前提。
        /// 失败出口全是 Warning（读不出来只影响"查手册"），而实测成本约 1.2 秒（253 页内嵌资源）。
        /// 放在链上等于让一个"只报不拦"的项决定用户多等多久。
        ///
        /// 时序不变式
        /// ---------
        /// 它仍然排在插件扫描**之后**：合并要按插件的注册元数据分组，而主窗口只能在
        /// CreateShell 返回后出现——那一刻插件扫描早已跑完（它在自检链上），所以挪到后台
        /// 不改变"插件程序集已在内存里"这个前提，读内嵌手册依旧不必二次 LoadFrom。
        ///
        /// 兜底
        /// ----
        /// 后台没跑完时用户就打开帮助窗口？HelpViewModel.OnDialogOpened 已有懒构建
        ///（!IsBuilt → Build），窗口会当场构建目录，行为不降级——两条路径共用同一份 Build。
        /// </summary>
        private void RunHelpCatalogCheckInBackground()
        {
            var check = _helpCatalogCheck;
            if (check == null) return;
            var log = Container.Resolve<ILogService>();
            var ct = _postStartupCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await check.ExecuteAsync(ct);
                    if (result.Passed)
                        log.Info($"[启动自检] {check.Name}: {result.Message}");
                    else if (result.Level == CheckLevel.Error)
                        log.Error($"[启动自检] {check.Name} 失败：{result.Message}");
                    else
                        log.Warn($"[启动自检] {check.Name} 警告：{result.Message}");
                }
                catch (OperationCanceledException)
                {
                    // 应用正在退出，目录没人要了
                }
                catch (Exception ex)
                {
                    // 后台合并绝不能把应用带下去：这里只剩一行日志
                    try { log.Warn($"[启动自检] {check.Name} 异常：{ex.Message}"); } catch { }
                }
            }, ct);
        }

        /// <summary>
        /// 后台跑一次通讯连通性检测，结果只写日志——不弹窗、不拦启动。
        ///
        /// 为什么不进启动自检链
        /// ---------
        /// 它的全部失败出口都是 Warning（`CommunicationCheck` 的 105/112 行）：进得了主界面，只是某条
        /// 连接不可达。而它的代价是 3 秒追赶窗口 + 每条最多 3 秒（总预算 12 秒）。放在链上，就等于让
        /// 一个"只报不拦"的诊断项决定用户多等多久；挪到主窗口显示之后，Splash 一关界面就出来了。
        /// 连接配置与自动连接仍由 CommunicationModule 在模块初始化阶段完成，时序没有变——检测只是晚
        /// 一点跑，反而更接近"连接已稳定"，误报"不可达"的机会更少。
        ///
        /// 口径不变：结果仍按 `[启动自检] 通讯连通性` 写日志，现场排查时的检索词还是同一个。
        /// </summary>
        private void RunCommunicationCheckInBackground()
        {
            var check = _communicationCheck;
            if (check == null) return;
            var log = Container.Resolve<ILogService>();
            var ct = _postStartupCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await check.ExecuteAsync(ct);
                    if (result.Passed)
                        log.Info($"[启动自检] {check.Name}: {result.Message}");
                    else if (result.Level == CheckLevel.Error)
                        log.Error($"[启动自检] {check.Name} 失败：{result.Message}");
                    else
                        log.Warn($"[启动自检] {check.Name} 警告：{result.Message}");
                }
                catch (OperationCanceledException)
                {
                    // 应用正在退出，检测结果没人要了
                }
                catch (Exception ex)
                {
                    // 后台诊断绝不能把应用带下去：这里只剩一行日志
                    try { log.Warn($"[启动自检] {check.Name} 异常：{ex.Message}"); } catch { }
                }
            }, ct);
        }

        /// <summary>
        /// HALCON 视觉引擎不可用时弹一次中文提示。
        ///
        /// 为什么不在自检链里直接弹
        /// ---------
        /// 自检链跑在 Splash 上，而 Splash 在主窗口显示后立刻关闭 —— 提示写得再清楚也是一闪而过。
        /// 所以自检只负责"判"，详细说明延到主界面出来后再弹，用户一定看得到。
        ///
        /// 为什么不禁用视觉入口
        /// ---------
        /// 缺 HALCON 只影响能不能跑图像算法，不影响用户看方案、配流程、调通讯。
        /// 把入口灰掉反而挡住了"先把流程编好，等 HALCON 装好再跑"这条正常路径。
        ///
        /// 只弹一次：引擎可用时不弹；不可用时每次启动弹一次（既不重复骚扰，也不常驻遮挡界面）。
        /// </summary>
        private void ShowHalconNotice()
        {
            if (_halconCheck?.FailureDetail == null) return;   // 引擎可用 → 什么都不做

            try
            {
                var owner = Current.MainWindow;
                string text = _halconCheck.FailureDetail;
                if (owner == null)
                    MessageBox.Show(text, "视觉引擎不可用", MessageBoxButton.OK, MessageBoxImage.Warning);
                else
                    MessageBox.Show(owner, text, "视觉引擎不可用", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                // 提示本身失败绝不能影响主界面：记一行日志就够了（与 ShowRecoveryNotice 同口径）
                try { Container.Resolve<ILogService>().Warn($"[启动自检] HALCON 提示显示失败：{ex.Message}"); } catch { }
            }
        }

        /// <summary>
        /// 崩溃现场摘要：本方法在"已经出事"的路径上被调用，只做最廉价的读取，
        /// 不序列化方案、不访问可能已死的服务，出错由调用方兜底。
        /// </summary>
        private string? DescribeCrashScene()
        {
            var solution = Container.Resolve<WorkspaceContext>().CurrentSolution;
            if (solution == null) return "当前没有打开的方案";

            var path = string.IsNullOrWhiteSpace(solution.SolutionFilePath)
                ? "(尚未保存到磁盘)"
                : solution.SolutionFilePath;

            return $"方案    : {solution.SolutionName}\r\n文件    : {path}\r\n流程数  : {solution.Flows?.Count ?? 0}";
        }

        /// <summary>
        /// 启动后提示"上次是异常退出"与"有未保存草稿"。
        ///
        /// 两件事分开说、合在一处问：崩溃现场（crash_reports）是给开发者/售后看的证据，
        /// 草稿（Autosave\recovered）是用户能立刻救回来的成果——用户真正在意的是后者，
        /// 所以文案先讲草稿、再讲现场，并且只给一个"打开目录"的动作。
        ///
        /// 只提示一次：两类文件都在被取走时即标记/归档（见 CrashReportWriter.TakeLatestUnacknowledged
        /// 与 SolutionDraftService.TakePendingDrafts），下一次启动不再重复打扰。
        /// </summary>
        private void ShowRecoveryNotice()
        {
            try
            {
                var crashReport = CrashReportWriter.TakeLatestUnacknowledged();
                var drafts = Container.Resolve<SolutionDraftService>().TakePendingDrafts();
                if (crashReport == null && drafts.Count == 0) return;

                // 设计基线 message-dialog v1：把「一段话 + 是/否」换成
                // 语义图标 + 结构化详情（草稿 / 崩溃现场各一行，可复制）+ 动作化按钮。
                var options = new MessageDialogOptions
                {
                    Severity = MessageSeverity.Warning,
                    Title = "上次运行的现场",
                    Message = drafts.Count > 0 && crashReport != null
                        ? $"检测到 {drafts.Count} 份未保存的方案草稿，以及上次异常退出的现场，已归档到下面的位置。"
                        : drafts.Count > 0
                            ? $"检测到 {drafts.Count} 份未保存的方案草稿，已归档到下面的位置。"
                            : "上次运行发生过严重异常退出，现场已保存到下面的位置。",
                    Hint = drafts.Count > 0 ? "可用「打开方案」载入草稿继续编辑。" : null,
                    Question = "现在打开所在目录吗？",
                    PrimaryText = "打开目录",
                    SecondaryText = "稍后再说",
                    Owner = Current.MainWindow,
                };

                if (drafts.Count > 0)
                {
                    options.Details.Add(new MessageDetail(
                        "方案草稿",
                        $"{drafts.Count} 份",
                        Path.Combine(SolutionDraftService.DirectoryName, SolutionDraftService.RecoveredDirectoryName),
                        MessageChipTone.Accent));
                }
                if (crashReport != null)
                {
                    options.Details.Add(new MessageDetail(
                        "崩溃现场",
                        "1 个",
                        Path.Combine(CrashReportWriter.DirectoryName, Path.GetFileName(crashReport)),
                        MessageChipTone.Neutral));
                }

                var answer = MessageDialog.Show(options);
                if (answer == MessageResult.Primary)
                {
                    // 优先打开草稿目录（用户的成果），没有草稿才开崩溃目录
                    var dir = drafts.Count > 0
                        ? Path.Combine(AppContext.BaseDirectory, SolutionDraftService.DirectoryName, SolutionDraftService.RecoveredDirectoryName)
                        : Path.Combine(AppContext.BaseDirectory, CrashReportWriter.DirectoryName);
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                // 提示本身失败绝不能影响主界面：记一行日志就够了
                try { Container.Resolve<ILogService>().Warn($"[现场恢复] 提示失败：{ex.Message}"); } catch { }
            }
        }

        /// <summary>
        /// 把"有人在动"接到会话上（空闲自动登出的唯一输入源）。
        ///
        /// 为什么挂在 <see cref="InputManager.PreProcessInput"/> 上，而不是逐个窗口挂事件
        /// ---------
        /// 这是 WPF 在"输入分发给任何元素之前"的唯一一道总口：一次订阅覆盖主窗口、
        /// 运行窗口和每一个弹窗。反过来，若让每个窗口各自上报，等于每加一个窗口就要记得加一次，
        /// 而漏一个的表现是"在那个窗口里操作也会被踢下线"——这种 bug 只在超时那一刻复现，
        /// 极难往"少挂了一个事件"上想。
        ///
        /// 只认四类"真人在动"的输入。<b>鼠标移动也算</b>：那是空闲检测的行业惯例
        /// （人坐在机器前动鼠标就是在场，不必非得点一下）。
        ///
        /// 性能：本钩子对每一次输入都执行，但 <see cref="ScadaAccessPolicy.Touch"/> 只是写一个
        /// DateTime 字段，且未登录时调用无害（见其注释）——这里刻意不做"登录了才上报"的分支，
        /// 那会引入"登录前动鼠标不算活动"这类只在特定时序下出现的偏差。
        ///
        /// 时序：<c>base.OnInitialized()</c> 之后容器已就绪（Prism 的 OnStartup → Initialize → OnInitialized）。
        /// </summary>
        private void HookGlobalActivity()
        {
            var accessPolicy = Container.Resolve<ScadaAccessPolicy>();

            InputManager.Current.PreProcessInput += (_, args) =>
            {
                if (args.StagingItem.Input is MouseEventArgs or KeyboardEventArgs
                    or TouchEventArgs or StylusEventArgs)
                {
                    accessPolicy.Touch();
                }
            };
        }

        /// <summary>
        /// 在当前 Dispatcher 上泵消息帧直至异步工作完成：
        /// CreateShell 必须同步返回窗口，但自检链是异步的——
        /// 帧泵让 Splash 的渲染/输入在等待期间照常处理（解决启动 UI 假死）。
        /// </summary>
        private static T RunWithPump<T>(Func<Task<T>> work)
        {
            var frame = new DispatcherFrame();
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = work().ContinueWith(t =>
            {
                if (t.IsFaulted) tcs.TrySetException(t.Exception!.GetBaseException());
                else tcs.TrySetResult(t.Result);
                frame.Continue = false;
            }, TaskScheduler.FromCurrentSynchronizationContext());
            Dispatcher.PushFrame(frame);
            return tcs.Task.GetAwaiter().GetResult();
        }
    }
}
