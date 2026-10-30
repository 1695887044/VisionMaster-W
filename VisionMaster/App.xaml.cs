using Core.Database;
using Core.Interfaces;
using Core.Interfaces.Io;
using Core.Interfaces.Operations;
using Prism.Dialogs;
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
using VisionMaster.Models;
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
        private SolutionSessionCoordinator? _sessionCoordinator;
        private HalconCheck? _halconCheck;
        private CommunicationCheck? _communicationCheck;
        private HelpCatalogCheck? _helpCatalogCheck;
        private readonly CancellationTokenSource _postStartupCts = new();
        private Lifetime.SplashScreen? _splash;
        private readonly List<IAppModule> _modules = new();

        /// <summary>
        /// RegisterTypes 阶段产生的"模块注册/加载"诊断（含可选模块加载器的结果）。
        ///
        /// 为什么先缓冲、不直接写日志
        /// ---------
        /// RegisterTypes 里容器尚未构建，<see cref="ILogService"/> 还解析不出来（那一步 Resolve 会抛）。
        /// 而这些诊断又必须在 Release 下可见（<c>Debug.WriteLine</c> 在发布版是黑洞）——
        /// 所以统一缓冲到 <see cref="CreateShell"/>，那时日志服务已就绪，逐条按 Warn 落盘（A03 口径）。
        /// </summary>
        private readonly List<string> _pendingModuleDiagnostics = new();

        /// <summary>
        /// UI 线程未处理异常的**最后一道兜底**（必须在任何窗口/启动代码之前挂上）。
        ///
        /// 【与 Core 的 <c>AppLifetimeService.RegisterGlobalExceptionHandlers</c> 的关系】
        /// 那套分级接线（Core\Lifetime\AppLifetimeService.cs）同样订阅了
        /// <see cref="Application.DispatcherUnhandledException"/>，但它走的是
        /// "Warning 级 → 限流日志 + 状态栏" 的处置链，且只在 CreateShell 里才挂上。
        /// 本处理器更早、更"笨"：**无条件**把完整 <see cref="Exception.ToString"/>（含堆栈）
        /// 写进日志、给一条非模态提示、置 Handled。两者同时订阅并不冲突——WPF 会依次调用
        /// 全部订阅者（Handled 只决定"事件之后是否终止进程"，不会跳过后续订阅者）。
        ///
        /// 【边界（写清楚，免得被当成万能兜底）】
        ///  · 只兜 **UI 线程**的未处理异常；后台线程仍走 AppDomain.UnhandledException
        ///    （Core 的分级接线已订阅，没有重复加）。
        ///  · <see cref="StackOverflowException"/> / <see cref="OutOfMemoryException"/> 这类
        ///    "进程状态已不可信"的异常**不主动 FailFast**：按本仓库既有口径只记录 + 提示，
        ///    由用户决定是否重启（主动杀进程会让"现场还能不能救"变成零可能）。
        ///  · 提示/日志服务自身可能正是故障源（容器未就绪、日志线程已死），两处都各自 try/catch，
        ///    兜底层绝不允许再抛一次。
        /// </summary>
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            var ex = e.Exception;

            SoftwareOperationLogHost.SafeRecord(entry =>
            {
                entry.Category = "Lifecycle";
                entry.Action = "未处理异常";
                entry.Outcome = "失败";
                entry.Subject = ex?.GetType().Name ?? "unknown";
                entry.Detail = ex?.Message ?? string.Empty;
            });

            try
            {
                TryResolveService<ILogService>()?.Error(
                    "[全局异常] UI 线程未处理异常（已拦截，界面继续运行）：" + Environment.NewLine + ex);
            }
            catch
            {
                // 日志服务故障时忽略：兜底层不能因为记录失败再抛
            }

            try
            {
                TryResolveService<IUserNotifier>()?.ShowError(
                    $"界面操作出现异常：{ex.GetType().Name}：{ex.Message}（详情见日志）");
            }
            catch
            {
                // 提示服务故障时忽略
            }

            // 标记已处理：界面继续运行（与 Core 分级接线的 Dispatcher 分支口径一致）
            e.Handled = true;
        }

        /// <summary>按需解析宿主服务；容器尚未初始化时返回 null（启动极早期可能发生）。</summary>
        private static T? TryResolveService<T>() where T : class
        {
            try
            {
                return ContainerLocator.Container?.Resolve<T>();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 在 Prism 的启动流程之前挂上 UI 线程兜底：
        /// RegisterTypes / CreateShell / Splash 期间的未处理异常也要被这一层接住。
        /// </summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;

            // 软件级操作记录：**在一切之前**装好，登录/方案切换/参数保存都靠它。
            // fail-open：库建不起来就退化成 no-op（见 SqliteSoftwareOperationLog），不影响启动。
            var operationLog = new SqliteSoftwareOperationLog(
                SqliteSoftwareOperationLog.DefaultPath,
                retentionDays: () =>
                {
                    // 容器在 base.OnStartup（RegisterTypes / CreateShell）之后才可用；
                    // 而本回调在**首条记录建库**时就会被调一次（下方 SafeRecord「启动」条目），
                    // 那一刻容器还是 null。
                    // ⚠ 不能直接写 Container.Resolve<T>()：Prism 的 Resolve<T>() 是扩展方法，
                    //   provider 为 null 时它自己先抛 NullReferenceException，再被 catch 吃掉——
                    //   结果虽然正确（回落 90 天），但调试器会按 first-chance 停在那一行
                    //   （2026-10-30 实测）。走 TryResolveService：先判空，正常路径不产生异常。
                    return TryResolveService<AppSettingsService>()?.Current.AuditRetentionDays ?? 90;
                },
                warn: message => LogServiceSafe(message));
            SoftwareOperationLogHost.Current = operationLog;
            SoftwareOperationLogHost.SafeRecord(entry =>
            {
                entry.Category = "Lifecycle";
                entry.Action = "启动";
                entry.Subject = Environment.MachineName;
                entry.Detail = "版本 " + (typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown");
            });

            base.OnStartup(e);
        }

        /// <summary>启动最早期的日志出口：容器还没起来，只能落到控制台/调试输出。</summary>
        private static void LogServiceSafe(string message)
        {
            try { Console.WriteLine(message); } catch { }
            System.Diagnostics.Debug.WriteLine(message);
        }

        protected override Window CreateShell()
        {
            var thinFont = this.Resources["FA.Light"];
            this.Resources["Icon"] = thinFont;

            // ===== 生命周期宿主：异常分级接线 + 模块初始化 =====
            _lifetime = Container.Resolve<AppLifetimeService>();
            _lifetime.RegisterGlobalExceptionHandlers();
            _lifetime.CrashSceneProvider = DescribeCrashScene;

            // 方案级资产历史：`<方案目录>\Logs\operations.db`（跟方案走，不跟机器走）。
            // 根路径用惰性 provider：切方案时 SolutionIdentityHolder 会更新，这里不必重新装配。
            AssetVersionSinkHost.Current = new SqliteAssetHistoryStore(
                () =>
                {
                    try { return Container.Resolve<SolutionIdentityHolder>().SolutionRoot; }
                    catch { return null; }
                },
                message => LogServiceSafe(message));

            // ===== 退出链收尾：日志落盘 =====
            // 退出链逆序执行：此处最先注册 → 最后运行，保证其他退出任务的日志都完整落盘
            _lifetime.RegisterExitTask(ExitTask.Of("日志落盘收尾", () =>
            {
                if (Container.Resolve<ILogService>() is IDisposable d)
                    d.Dispose();
            }));

            // ===== 退出链：记一条「退出」（注册在日志收尾之后 → 逆序先跑，此时各服务都还活着） =====
            _lifetime.RegisterExitTask(ExitTask.Of("软件操作记录·退出", () =>
            {
                SoftwareOperationLogHost.SafeRecord(entry =>
                {
                    entry.Category = "Lifecycle";
                    entry.Action = "退出";
                    entry.Subject = Environment.MachineName;
                });
            }));

            // ===== 退出链收尾：未保存草稿转存 =====
            // 注册在"日志落盘收尾"之后 → 逆序执行时先跑，此刻日志服务还活着，转存结果能记进日志。
            // 正常关闭与严重异常退出都会走退出链，所以两条路都受这一项保护。
            _lifetime.RegisterExitTask(ExitTask.Of("未保存草稿转存", () =>
            {
                var workspace = Container.Resolve<WorkspaceContext>();
                var solution = workspace.CurrentSolution;
                // v2：草稿只序列化 SolutionModel；变量值在 VariableSnapshots 里，
                // 不先 Capture 就会把「配方刚写过的变量」存成上一次保存的旧值。
                if (solution != null)
                    VariablePersistenceService.Capture(solution, workspace);

                var result = Container.Resolve<SolutionDraftService>().Capture(solution);

                if (result.Status == DraftCaptureStatus.Captured)
                    Container.Resolve<ILogService>().Warn($"[退出链] 方案有未保存改动，已转存草稿：{result.Path}");
                else if (result.Status == DraftCaptureStatus.Failed)
                    Container.Resolve<ILogService>().Warn($"[退出链] 草稿转存失败：{result.Detail}");
            }, 8000));

            // 通信层日志接进 UI 日志窗口：manager 内部日志原本只写 Console（WPF 无控制台=黑洞），
            // 变量注册/轮询/读取失败等诊断必须可见。
            // 必须在模块初始化之前挂接：通讯模块的启动装配（加载配置 + 自动连接）日志也在其中
            AdvancedCommunicationManager.LogSink = Container.Resolve<ILogService>();

            // RegisterTypes 阶段缓冲的模块诊断（含可选 Web 模块"启用但缺件"）在这里落地：
            // 日志服务此刻已可用，Release 版也能在 Logs\ 当天日志里查到（A03）。
            if (_pendingModuleDiagnostics.Count > 0)
            {
                var startupLog = Container.Resolve<ILogService>();
                foreach (var diagnostic in _pendingModuleDiagnostics)
                    startupLog.Warn(diagnostic);
                _pendingModuleDiagnostics.Clear();
            }

            // 模块初始化逐项隔离：一个模块起不来只让它的功能不可用，不许把主界面拦在启动链外
            // （Initialize 阶段容器已就绪，异常可以直接写日志——与 RegisterTypes 阶段的缓冲口径不同）。
            // 注册阶段失败的模块已在 RegisterTypes 里从 _modules 摘除，所以这里天然只跑"注册成功"的那些；
            // 万一将来有人在注册失败后重新加回，会在这里再炸一次——那是新加的装配路径该修的 bug。
            foreach (var module in _modules)
            {
                try
                {
                    module.Initialize(Container, _lifetime); // 模块注册自检项/退出任务，须在自检前完成
                }
                catch (Exception ex)
                {
                    var message = $"[模块] {module.Name} 初始化失败，本次按该功能不可用运行：{ex.GetType().Name}：{ex.Message}";
                    Debug.WriteLine(message);
                    // 日志服务本身取不到时只留 Debug 痕迹：一个模块起不来绝不许把启动带走
                    try { Container.Resolve<ILogService>().Error(message); } catch { }
                }
            }

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

            // 方案级日志：同一行同时写全局日志与当前方案的 <方案根>\Logs
            // （没有方案 / 方案尚未保存时只写全局）。
            //
            // 用"现读"提供口而不是在新建/打开/列表/启动自动加载四个入口逐个推送：
            // 漏推一处，这份方案的日志就会一直写进上一份方案的目录，而且要到翻日志时才发现。
            // 容器还没建好或已释放（退出链）时一律按"没有方案"处理——日志服务不能反过来把进程带走。
            WorkspaceContext? logScopeWorkspace = null;
            LogService.SolutionRootProvider = () =>
            {
                try
                {
                    // 先判空再解析：这个提供口在**容器建好之前**就会被调（启动早期第一条日志），
                    // 直接 ContainerLocator.Container.Resolve(...) 会在 null 上抛 NRE，
                    // 被下面 catch 吞掉后虽然行为正确，但调试器会 first-chance 中断。
                    if (ContainerLocator.Container == null) return null;
                    logScopeWorkspace ??= ContainerLocator.Container.Resolve<WorkspaceContext>();
                    return SolutionPaths.GetSolutionRoot(logScopeWorkspace.CurrentSolution?.SolutionFilePath);
                }
                catch
                {
                    return null;
                }
            };

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

            // 步骤图标补全钩子：Icon 是"建步骤时"从工具箱快照进 .vms 的，老方案（或插件补
            // ShortName 之前建的步骤）会是空串。插件表只有容器知道，所以在这里注入。
            // 与上面同一注意点：lambda 在**方案加载时**执行，那时容器已就绪，可以 Resolve。
            SolutionService.StepIconBackfillHook = solution =>
            {
                try
                {
                    StepIconBackfill.Apply(solution, Container.Resolve<IPluginProvider>());
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[方案加载] 步骤图标补全失败（已忽略）：{ex.Message}");
                }
            };
            // 画布重建耗时日志：只对 >=80 步的流程打一行，量的是"点完流程到界面重新响应"。
            // 走 ILogService（不进弹泡）：真机卡顿排查靠的是日志文件，不是 3 秒就消的气泡。
            // lambda 在重建时执行，那时容器已就绪；故障时静默（日志本身不能影响画布）。
            FlowCanvasViewModel.PerfLog = message =>
            {
                try
                {
                    Container.Resolve<ILogService>().Info(message);
                }
                catch
                {
                    // 容器未就绪/日志服务缺失：丢掉这一行，不影响画布
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
            // 系统变量模块：把通讯设备与相机接进统一宿主（运动轴由 MotionModule 自己接）。
            // 必须排在 CameraModule / MotionModule 之后：它初始化时要解析 ICameraProvider 与
            // 已挂好运动目录的 SystemVariableHost，早于两者会拿到空设备表。
            _modules.Add(new SystemVariableModule());
            // 数据库模块：只做配置存储与 SQLite 执行，不参与设备连接；排在设备模块之后即可
            _modules.Add(new DatabaseModule());
            // 注：IO 接入层已下线——设备/点位配置改由"通讯连接 + 网络变量"承载
            // （旧 IO 配置在方案加载时由 IoMigrationHook 一次性折算，见下方注册）。

            // ===== 可选模块：SCADA 运行态 Web 客户端 =====
            // 追加在**最后**，语义有两条：
            //   ① Initialize 最后执行——Web 只读运行态，必须在流程引擎/相机/运动都就绪之后才开始看；
            //   ② 退出任务最先执行——AppLifetimeService 按注册逆序跑退出链，
            //      最后注册的退出任务最先跑，Web 的监听/会话先停，再去停流程引擎与通讯。
            // 加载失败（未启用 / 缺 DLL / Web\ 里出现契约副本）一律**不拦启动**：
            // 未启用时 diagnostic 为 null（正常状态，不写 Warn），其余情况缓冲到 CreateShell 再 Warn。
            var webModule = ScadaWebModuleLoader.TryLoad(AppContext.BaseDirectory, out var webDiagnostic);
            if (webModule != null)
                _modules.Add(webModule);
            else if (!string.IsNullOrWhiteSpace(webDiagnostic))
                _pendingModuleDiagnostics.Add(webDiagnostic);

            // 模块注册逐项隔离：单个模块的注册异常不许把整条启动链带走
            // （RegisterTypes 阶段不能 Resolve 日志服务，异常信息同样先缓冲，CreateShell 里 Warn）。
            // 注册失败必须**从 _modules 摘除**：半注册的模块（部分服务已登记、部分没登记）若留在表里，
            // CreateShell 的 Initialize 会继续调它，等于把"注册失败"变成"初始化时再炸一次"，
            // 而且那时炸出来的是缺依赖的二次故障，真实原因（注册阶段那条）反而被盖住。
            // 用 ToArray() 快照遍历：边遍历边删会让 foreach 抛 InvalidOperationException。
            foreach (var module in _modules.ToArray())
            {
                try
                {
                    module.Register(containerRegistry);
                }
                catch (Exception ex)
                {
                    _modules.Remove(module);
                    var message = $"[模块] {module.Name} 注册失败，已跳过（本次按该功能不可用运行）："
                        + $"{ex.GetType().Name}：{ex.Message}";
                    _pendingModuleDiagnostics.Add(message);
                    Debug.WriteLine(message);
                }
            }

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

            // 流程判定解析器（L2）：引擎只依赖 IFlowVerdictResolver，宿主实现读结果输出步骤/约定变量。
            // 必须早于 FlowEngineModule.Initialize 解析引擎——本方法（RegisterTypes）先于模块初始化执行。
            containerRegistry.RegisterSingleton<VisionMaster.Services.FlowVerdictResolver>(c =>
                new VisionMaster.Services.FlowVerdictResolver(
                    c.Resolve<IWorkspaceManager>(),
                    c.Resolve<IWorkspaceManager>().VariableRegistry));
            containerRegistry.RegisterSingleton<VisionMaster.Models.IFlowVerdictResolver>(c =>
                c.Resolve<VisionMaster.Services.FlowVerdictResolver>());
            // 系统变量统一宿主：**必须显式注册**，且集合只能是 workspace.SystemVariables。
            // 漏了这条不报任何错——Unity 会自己构造一个 SystemVariableHost，连它要的集合也新造一份空的；
            // 运动目录据此进入"托管模式"，投影全写进没人读的孤儿集合，界面「系统变量」恒为 0
            // （2026-10-23 实测：方案里 3 根启用轴，36 条变量一条都没上屏）。
            containerRegistry.RegisterSingleton<SystemVariableHost>(c =>
                new SystemVariableHost(c.Resolve<IWorkspaceManager>().SystemVariables));
            containerRegistry.RegisterForNavigation<LogView,LogViewModel>();
            containerRegistry.RegisterForNavigation<ProcessView, ProcessViewModel>();
            containerRegistry.RegisterForNavigation<MonitorView, MonitorViewModel>();
            containerRegistry.RegisterForNavigation<ToolView, ToolViewModel>();
            // 底部控制台抽屉条（收起态 32px）要和日志页/监控栏显示**同一份**计数与"最后一条"：
            // LogViewModel 订阅的是具体类 LogService.OnLogReceived，每个实例各收一份，
            // 抽屉自己再造一个会在 LogPanelMaxRows 上限处与日志页的「全部 N」分叉。
            // 与 ScadaEditorViewModel 同理：singleton 覆盖 RegisterForNavigation 的默认单次构造，
            // 视图 AutoWire 与抽屉解析到的是同一个实例。
            containerRegistry.RegisterSingleton<LogViewModel>();
            containerRegistry.RegisterSingleton<MonitorViewModel>();
            containerRegistry.RegisterSingleton<ConsoleDrawerViewModel>(c =>
                new ConsoleDrawerViewModel(c.Resolve<LogViewModel>(), c.Resolve<MonitorViewModel>()));
            // 组态编辑器的视图模型必须是单例：画布宿主与属性面板要靠"同一个 SelectedElement"联动，
            // 各自一份就得引入事件总线才能对齐选中态。RegisterForNavigation 造视图时经由容器
            // 解析视图模型，singleton 覆盖默认的单次构造，所以两处拿到的是同一个实例。
            containerRegistry.RegisterSingleton<ScadaEditorViewModel>(c => new ScadaEditorViewModel(
                c.Resolve<IWorkspaceManager>(),
                c.Resolve<IScadaRuntimeHost>(),
                // 报警配置入口（方案级）从组态工具栏打开；断言工程构造时可以不传。
                c.Resolve<IDialogService>()));
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
            containerRegistry.RegisterSingleton<SolutionIdentityHolder>();
            containerRegistry.RegisterSingleton<IScadaSolutionContext>(c => c.Resolve<SolutionIdentityHolder>());
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
                retentionProvider: () => c.Resolve<AppSettingsService>().Current.AuditRetentionDays,
                solutionTagProvider: () => c.Resolve<SolutionIdentityHolder>().AuditTag));
            // 审计的跨程序集出口（可选模块用）：接口在 VM.Scada，实现是宿主这一份 writer。
            // 映射到**同一个单例**是硬要求——审计是"谁在什么时候按了哪个按钮"的凭证，
            // 两份实例各自往同一个文件追加写，出来的会是交错的行（与上面"全局一份"同一条理由）。
            containerRegistry.RegisterSingleton<IScadaAuditSink>(c => c.Resolve<ScadaAuditWriter>());
            // 运行态宿主必须是单例：它管的就是"同一时刻只允许一个运行窗口"这条策略，
            // 每个调用方解析出一份新实例，等于这条策略根本不存在（两份会话各自写同一批变量）。
            // 三个映射指向**同一个实例**（具体类型 / 桌面宿主接口 / 运行态事实接口）：
            //   · IScadaRuntimeHost 住在宿主工程，桌面侧（Shell、运行窗口）用；
            //   · IScadaRunHub 住在 VM.Scada，可选模块（Web 运行态）只引用 VM.Scada、看不到前者，
            //     靠它拿"这一轮是否在跑 / 会话 / 文档 / generation"与启停事件。
            // 走工厂而不是类型映射（与 FlowEngineModule 同一手法）：接口另建一份会让
            // "订阅事件的实例"与"真正在跑运行的实例"不是同一个对象——事件永远收不到，
            // 而症状是"Web 上看不到运行态"，很难往注册处想。
            containerRegistry.RegisterSingleton<ScadaRuntimeHost>(c => new ScadaRuntimeHost(
                c.Resolve<ILogService>(),
                c.Resolve<IUserNotifier>(),
                c.Resolve<IWorkspaceManager>(),
                c.Resolve<IScadaActionDispatcher>(),
                c.Resolve<IScadaValueSource>(),
                c.Resolve<IScadaValueWriter>(),
                c.Resolve<ScadaNavigator>(),
                c.Resolve<IScadaAccessPolicy>(),
                c.Resolve<ScadaAuditWriter>(),
                c.Resolve<AppSettingsService>(),
                ResolveOptional<IScadaAxisSource>(c),
                ResolveOptional<IScadaAxisCommandSink>(c),
                c.Resolve<IScadaConfirmationService>(),
                // 方案级监控必须与宿主共用同一个实例；显式传，不靠容器猜可选参数。
                c.Resolve<ScadaMonitoringHost>(),
                // 运行态未授权点击：弹登录 → 登录成功后按最新权限重判 → 继续原操作。
                required => ShowRuntimeAccessLoginPrompt(c, required),
                // v6 语言服务：运行窗口的 @键 靠它解析（不传 = 所有文本按字面量显示）。
                languages: c.Resolve<IScadaLanguageService>(),
                // v7：静音出口 + 配方面板命令出口（配方面板靠它们从只读升级为可选/下载/上传/保存）。
                alarmMute: c.Resolve<IScadaAlarmMute>(),
                recipeCommands: c.Resolve<IScadaRecipeCommands>()));
            // 方案级监控：报警 + 变量事件 + 节拍，生命周期跟方案走（不再跟 SCADA 运行窗口）。
            containerRegistry.RegisterSingleton<ScadaMonitoringHost>(c => new ScadaMonitoringHost(
                c.Resolve<ILogService>(),
                c.Resolve<IWorkspaceManager>(),
                c.Resolve<IScadaValueSource>(),
                c.Resolve<IScadaValueWriter>(),
                c.Resolve<IScadaAccessPolicy>(),
                ResolveOptional<IScadaAxisSource>(c),
                ResolveOptional<IScadaAxisCommandSink>(c),
                c.Resolve<IScadaConfirmationService>(),
                c.Resolve<IScadaActionDispatcher>(),
                c.Resolve<AppSettingsService>(),
                c.Resolve<IScadaLanguageService>()));
            containerRegistry.RegisterSingleton<IScadaRuntimeHost>(c => c.Resolve<ScadaRuntimeHost>());
            containerRegistry.RegisterSingleton<IScadaRunHub>(c => c.Resolve<ScadaRuntimeHost>());
            // DataGrid 行数据的只读出口：与运行宿主同一实例（它才拿得到运行窗口画布）。
            containerRegistry.RegisterSingleton<IScadaRuntimeDataProvider>(c => c.Resolve<ScadaRuntimeHost>());
            // 报警命令出口（M2）：Web 命令层「确认报警 / 全部确认」只认 VM.Scada 里的接口，
            // 引擎实例由本宿主按"当前运行的那一轮"现取（见 ScadaRuntimeHost.CurrentAlarmEngine），
            // 所以这里也必须映射到**同一个**单例——另建一份实例会拿不到运行中的引擎。
            containerRegistry.RegisterSingleton<IScadaAlarmCommands>(c => c.Resolve<ScadaRuntimeHost>());
            // 报警只读快照（M2b AckButton 的列表/未确认计数）：与确认命令同一实例、同一轮引擎。
            containerRegistry.RegisterSingleton<IScadaAlarmFeed>(c => c.Resolve<ScadaRuntimeHost>());
            // 渲染目录（M1a）：把图元注册表投影成无 WPF 的只读 DTO，供可选模块（Web 渲染器）读
            // "类型键 / 默认尺寸 / 属性默认值 / 事件名"。**必须懒构建**：趋势图（Hmi.ChartTrend）在
            // 另一个程序集里，要等 Initialize 里的 ScadaChartElements.Register() 跑完才在注册表里，
            // 所以这里只登记映射、不解析实例（第一次 Resolve 必须晚于那次注册）。
            containerRegistry.RegisterSingleton<IScadaRenderCatalog, ScadaRenderCatalog>();

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

            // A 范围的安全闸：Web 写变量前判"是不是流程触发器"（变量上升沿会启动流程，
            // 流程里可以含轴运动）。实现住在宿主（能看见 IWorkspaceManager），
            // Web 模块只认 VM.Scada 里的接口；解析不到时 Web 命令层 fail closed。
            containerRegistry.RegisterSingleton<IScadaFlowTriggerQuery>(
                c => new ScadaFlowTriggerQuery(c.Resolve<IWorkspaceManager>()));
            // 动作分发器同样是单例：它无状态（只往日志/变量/导航出口递话），而每次运行都 new 一份
            // 只会让人误以为"动作的执行状态存在某处"——将来接 S6 写变量时这里换成带状态的实现也不动调用方。
            // 走工厂而不是类型映射：audit 在构造函数上是**可选参数**（默认 null），
            // 让容器去猜"这个可选参数要不要注入"，结果多半是编译能过、运行起来审计文件一直空着
            // （而且不报错）。这里显式传进去，"接上审计"这件事在注册处一眼可见。
            // 执行前二次确认（P0）：全局一份。窗口路径允许弹框，headless 监控会把它临时置为不可交互。
            containerRegistry.RegisterSingleton<IScadaConfirmationService>(c => new ScadaConfirmationService());
            // 动作表「播放声音」的声音出口：与报警声音服务分开（报警声音跟严重度/静音策略走）。
            // 单例：SystemSoundPlayer 内部只有一个 SoundPlayer，重复实例会让两次播放互相打断。
            containerRegistry.RegisterSingleton<IScadaSoundPlayer>(c => new SystemSoundPlayer());
            containerRegistry.RegisterSingleton<IScadaActionDispatcher>(c => new ScadaActionDispatcher(
                c.Resolve<ILogService>(),
                c.Resolve<IScadaValueSource>(),
                c.Resolve<IScadaNavigator>(),
                c.Resolve<ScadaAuditWriter>(),
                c.Resolve<IScadaFlowRunner>(),
                // 运动命令族执行器：同样是可选参数（断言工程里 16 处 4~5 参构造不该为它解析运动依赖）。
                // 这里显式传进去——交给容器去猜的结果是编译能过、运行起来点「轴定位」只记一行
                // "未接入运动模块"（而且不报错）。它持有"哪根轴在点动"的状态与看门狗，所以是单例。
                c.Resolve<MotionActionExecutor>(),
                // 执行前二次确认（P0）：显式接上，没接线时按取消处理（fail-safe）。
                c.Resolve<IScadaConfirmationService>(),
                // 配方应用出口：必须显式接上 RecipeService，否则 SCADA 的「应用配方」会永远
                // 记一行“未接入配方服务”（而且不报错）。配方服务在下方注册；工厂是惰性解析，
                // 首次用到分发器时它已经就绪。
                c.Resolve<RecipeService>(),
                // 播放声音出口：显式接上，否则动作表里配了「播放声音」只会记“未接入”。
                c.Resolve<IScadaSoundPlayer>(),
                // v6「登录」：无条件弹登录窗（换人登录）。
                // 不能复用 ShowRuntimeAccessLoginPrompt：它开头有"已有权限就直接返回 true"的短路，
                // 那是给"点不动 → 补权限"用的；动作「登录」的语义是"我要换个账号"，
                // 走那条短路会出现"点了登录什么都没发生"。
                _ => ShowRuntimeLoginPromptAlways(c),
                // v6「注销」：会话状态只有 ScadaAccessPolicy 一份，这里加一层薄适配
                //（未登录归一成 null、void Logout 包成 bool + 原因）。
                new ScadaUserSessionAdapter(c.Resolve<ScadaAccessPolicy>()),
                // v6「切换语言」：按方案当前文档读词表，换方案后现取（抓实例会在换方案后继续读旧文档）。
                c.Resolve<IScadaLanguageService>(),
                // v7「确认报警」：与 Web 命令层共用同一个报警命令出口（映射到运行宿主单例）。
                // 【不能在这里直接 Resolve】——ScadaRuntimeHost 的构造又需要 IScadaActionDispatcher
                //（:550），而 IScadaAlarmCommands 映射回 ScadaRuntimeHost（:589），三者构成
                // ScadaRuntimeHost → IScudaActionDispatcher → IScadaAlarmCommands → ScadaRuntimeHost
                // 的**构造期环**：DryIoc 的工厂委托重入没有环检测，首次解析即 StackOverflow
                //（2026-10-30 实测启动即崩）。用惰性适配器断环：适配器构造零依赖，
                // 首次派发报警命令时宿主单例早已就绪，行为与直接注入宿主完全一致。
                new LazyAlarmCommands(() => c.Resolve<ScadaRuntimeHost>()),
                // v7「静音」：声音服务是每轮运行新建的，适配器每次调用现取（见适配器注释）。
                // 【同样不能直接 Resolve】——ScadaAlarmMuteAdapter 要 ScadaMonitoringHost（:570），
                // 而监控宿主的构造又需要 IScudaActionDispatcher（:579）——与上面同族的第二个
                // 构造期环（实测拆掉 AlarmCommands 环之后仍 StackOverflow 的根因）。
                // 同样用惰性适配器断环：首次静音操作时三个单例早已就绪。
                new LazyAlarmMute(() => new ScadaAlarmMuteAdapter(c.Resolve<ScadaMonitoringHost>()))));
            // 运行态语言出口（v6）：词表 + 当前语言 + 变化通知。全局一份——
            // 动作「切换语言」、运行窗口、设计期画布必须看到同一个"当前语言"，
            // 两份实例的表现是"切了语言，画布变了但运行窗口没变"（而且不报错）。
            // 走工厂 + Func：方案是会被换掉的，抓住 ScadaDocument 实例会在换方案后继续读旧文档，
            // 症状是"换了方案，@键 全部不解析"。每次现取才不受换方案影响。
            containerRegistry.RegisterSingleton<IScadaLanguageService>(c =>
            {
                var workspace = c.Resolve<IWorkspaceManager>();
                return new ScadaLanguageService(() => workspace.CurrentSolution?.Scada);
            });
            // v7 报警静音出口：适配 ScadaMonitoringHost 持有的声音服务（每轮运行新建，故适配器现取）。
            containerRegistry.RegisterSingleton<IScadaAlarmMute>(c =>
                new ScadaAlarmMuteAdapter(c.Resolve<ScadaMonitoringHost>()));
            // v7 配方面板命令出口：下载/上传走 RecipeService，保存走与主界面同一条 RecipeSaveCoordinator。
            containerRegistry.RegisterSingleton<IScadaRecipeCommands>(c =>
                new ScadaRecipeCommands(
                    c.Resolve<IWorkspaceManager>(),
                    c.Resolve<RecipeService>(),
                    ResolveOptional<RecipeSaveCoordinator>(c)));
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
            // 会话凭据仓（切方案"同账号密码保持登录"的唯一材料）：
            // 只在内存里存一份可擦除的 char[]；任何登出（手动/空闲超时/切方案/强制）立刻清零，
            // 绝不落盘、不进日志与审计。构造时订阅 ScadaAccessPolicy.LoggedOut，所以必须晚于它注册。
            containerRegistry.RegisterSingleton<ScadaSessionCredentialVault>(c =>
                new ScadaSessionCredentialVault(c.Resolve<ScadaAccessPolicy>()));
            containerRegistry.RegisterSingleton<ScadaPermissionGate>(
                c => new ScadaPermissionGate(c.Resolve<ScadaAccessPolicy>()));
            // 调试模式（开发期免权限 / 上线后仅「用户管理」可开关；开关落盘且不按时间失效）。
            // 走工厂而不是类型映射：四个口全是委托，显式写出来才能一眼看清
            // "权限判的是哪个 Key、写盘走哪条路径、审计拿的是谁"。
            // settingsProvider 每次现读配置节——AppSettingsService.Load 在配置损坏时会整体替换 Current，
            // 抓引用会让开关写到一份已经没人读的旧对象上。
            containerRegistry.RegisterSingleton<DebugModeService>(c =>
            {
                var settingsService = c.Resolve<AppSettingsService>();
                var policy = c.Resolve<ScadaAccessPolicy>();
                return new DebugModeService(
                    settings: () => settingsService.Current.DebugMode,
                    persist: (enabled, actor, at) => settingsService.SyncDebugModeSettings(enabled, actor, at),
                    permissionProbe: key =>
                    {
                        var allowed = policy.Can(key, out var reason);
                        return new DebugPermissionResult(allowed, reason);
                    },
                    actorProvider: () => policy.CurrentUserName,
                    audit: c.Resolve<ScadaAuditWriter>());
            });
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
                    actorProvider: () => policy.CurrentUserName,
                    // 密码最小长度是软件级配置（系统设置 → 安全与留档 → 账号）：
                    // 每次校验现读，改完不需要重启软件。
                    minPasswordLengthProvider: () => c.Resolve<AppSettingsService>().Current.MinPasswordLength,
                    // 账号本属于方案：启动时先不绑机器级文件，由 SolutionSessionCoordinator 在切方案时绑定。
                    startUnbound: true,
                    // 删除权限前扫描全方案页面/图元的 RequiredPermission；null 会让删除一律拒绝（fail-closed）。
                    permissionReferenceCounter: key => CountPermissionReferences(c.Resolve<IWorkspaceManager>(), key));
            });
            // 账号校验的跨程序集出口（可选模块用）：接口在 VM.Scada，实现是宿主这一份 ScadaUserStore。
            // 同一单例——"谁能登录、登录后是几档"只有一份数据；两份实例的表现是
            // "Web 上刚建的账号登录时说不存在"。
            containerRegistry.RegisterSingleton<IScadaUserAuthenticator>(c => c.Resolve<ScadaUserStore>());
            // 账号变更通知出口（M2a P0-A）：改密/重置/改用户组/删号后，Web 模块据此立刻撤销旧会话。
            // 必须是同一个 ScadaUserStore 实例——另建一份就收不到事件。
            containerRegistry.RegisterSingleton<IScadaUserChangeNotifier>(c => c.Resolve<ScadaUserStore>());
            // 属性面板「所需权限」下拉的目录出口：与用户管理同一份目录（15 内置 + 自定义）。
            // 同一单例——用户在用户管理里新建的权限，回到组态面板必须立刻能选到。
            containerRegistry.RegisterSingleton<IScadaPermissionCatalog>(c => c.Resolve<ScadaUserStore>());
            // Web 模块（可选）把在线会话管理口挂到桥；未加载时解析抛异常，协调器按不可用降级。
            containerRegistry.RegisterSingleton<IScadaWebSessionAdmin>(c =>
                ScadaWebSessionAdminBridge.Current
                ?? throw new InvalidOperationException("Web 运行态未加载：在线会话管理不可用"));
            // 方案切换 = 会话边界：登出旧身份、重绑账号本、更新审计标签；组/用户变更时重判或登出。
            containerRegistry.RegisterSingleton<SolutionSessionCoordinator>(c => new SolutionSessionCoordinator(
                c.Resolve<IReadOnlyWorkspaceContext>(),
                c.Resolve<ScadaUserStore>(),
                c.Resolve<ScadaAccessPolicy>(),
                c.Resolve<SolutionIdentityHolder>(),
                c.Resolve<ScadaAuditWriter>(),
                () => ResolveOptional<IScadaWebSessionAdmin>(c),
                message => c.Resolve<ILogService>().Warn(message),
                c.Resolve<ScadaSessionCredentialVault>(),
                // "调试构建默认管理员"只在调试模式开启时生效：面板开关关着 = 开发者不想被自动登录。
                // 取的是每次现读（配置可能刚被面板改过），不是构造时抓一份。
                () => c.Resolve<DebugModeService>().IsEnabled));
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
            // 组态「选配方」入口：清单来自当前方案的配方表（RecipeService），身份是 Guid 型 RecipeId。
            // 与上三个同一手法：运行态「应用配方」按 Id 寻址，所以"选得到就一定应用得到"；
            // 配方名只作展示与旧数据兜底。
            containerRegistry.RegisterSingleton<IScadaRecipePicker, ScadaRecipePicker>();
            containerRegistry.RegisterDialog<ScadaRecipePickerView, ScadaRecipePickerViewModel>(
                ScadaRecipePicker.DialogName);
            // 「运行统计 · 结果源」：流程栏右键入口。只读态走 IFlowConfigAccess（同一「编辑流程结构」Key），
            // 弹窗本身只写 FlowModel 的结果源字段，不改流程图、下一轮生效。
            containerRegistry.RegisterSingleton<IFlowConfigAccess, FlowConfigAccess>();
            containerRegistry.RegisterDialog<FlowResultSourceDialogView, FlowResultSourceDialogViewModel>(
                FlowResultSourceDialogViewModel.DialogName);
            containerRegistry.RegisterDialog<VariableBindingView, VariableBindingViewModel>("DataBindView");
            containerRegistry.RegisterDialog<GlobalVariableView, GlobalVariableManagerViewModel>("GlobalVariable");
            // 单设备变量表批量导入/导出（xlsx）：入口在变量管理卡片头，仅网络来源可见。
            // 导入弹窗自己完成"差异计划 → 事务应用 → 回滚"，关闭后由变量管理重建左栏与明细。
            containerRegistry.RegisterDialog<DeviceVariableExportView, DeviceVariableExportViewModel>(
                DeviceVariableExportViewModel.DialogName);
            containerRegistry.RegisterDialog<DeviceVariableImportView, DeviceVariableImportViewModel>(
                DeviceVariableImportViewModel.DialogName);
            // 数组编辑弹窗（变量管理 → 数组根「编辑数组…」）：装填当前值，默认只写当前值，
            // 勾选才写初始值；校验在弹窗内完成（设计基线 global-variables v2）。
            containerRegistry.RegisterDialog<ArrayEditorView, ArrayEditorViewModel>(ArrayEditorViewModel.DialogName);
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
            // 方案级配方（v1：步骤参数）。服务是单例：生效指针、偏离计算与审计口径只有一份；
            // 保存协调器复用主窗口同一套 Capture（布局/变量/通讯），避免“步骤新值、变量旧值”。
            containerRegistry.RegisterSingleton<RecipeService>(c => new RecipeService(
                c.Resolve<IWorkspaceManager>(),
                c.Resolve<IRuntimeManager>(),
                c.Resolve<IFlowRunState>(),
                c.Resolve<ScadaAccessPolicy>(),
                c.Resolve<ScadaAuditWriter>(),
                c.Resolve<ILogService>()));
            containerRegistry.RegisterSingleton<RecipeSaveCoordinator>();
            containerRegistry.RegisterSingleton<RecipeFileService>(c => new RecipeFileService(
                c.Resolve<RecipeService>(), c.Resolve<ScadaAuditWriter>()));
            containerRegistry.RegisterSingleton<RecipeViewModel>();
            containerRegistry.RegisterDialog<RecipeManagerView, RecipeManagerViewModel>("RecipeManagerView");
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
                    () => Container.Resolve<ScadaAccessPolicy>().CurrentUserName,
                    monitorOptionsProvider: null,
                    // 日志：目录要重开写入器、保留期要立刻清一次。
                    // 用具体类型 LogService（RequestReopen / CleanupNow 不在 ILogService 上——
                    // 那两个是"改完生效"的内部收尾动作，不该污染日志接口）。
                    applyLogSettings: () =>
                    {
                        if (Container.Resolve<ILogService>() is LogService log)
                        {
                            log.RequestReopen();
                            log.CleanupNow();
                        }

                        // 「日志面板保留条数」也是日志设置的一项：LogView 绑的是
                        // LogViewModel.PanelMaxRows（= 配置现值）。这是普通属性，不通知就永远
                        // 停在首次读到的值上——改完设置会表现为"面板还是留 5000 条"。
                        // 解析失败（未注册 / 退出期）时忽略：面板上限不生效不该让保存流程失败。
                        try { TryResolveService<LogViewModel>()?.RefreshPanelMaxRows(); } catch { }
                    },
                    // 两个收图服务：Host/Port 是绑定期决定的，只能停掉再起。
                    // 两个各起各的：一个端口被占用不该让另一个也跟着不重开。
                    restartInboundServers: () =>
                    {
                        var errors = new List<string>();

                        try { Container.Resolve<HttpImageServer>().Restart(); }
                        catch (Exception ex) { errors.Add($"HTTP 收图服务：{ex.Message}"); }

                        try { Container.Resolve<NetworkCameraServer>().Restart(); }
                        catch (Exception ex) { errors.Add($"网络相机服务：{ex.Message}"); }

                        if (errors.Count > 0)
                            throw new InvalidOperationException(string.Join("；", errors));
                    },
                    // 「打开日志目录」：与 LogService 的目录口径**逐字相同**（空串 = 程序目录\Logs），
                    // 否则按钮打开的是一个和真正落盘位置不同的目录。
                    openLogFolder: () =>
                    {
                        var configured = Container.Resolve<AppSettingsService>().Current.LogDirectory;
                        var target = string.IsNullOrWhiteSpace(configured)
                            ? Path.Combine(AppContext.BaseDirectory, "Logs")
                            : configured.Trim();

                        try
                        {
                            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target}\"")
                            {
                                UseShellExecute = true
                            });
                        }
                        catch
                        {
                            // 打不开资源管理器只影响这一个按钮，不该让调用点崩
                        }
                    }));
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
                    Container.Resolve<IScadaAxisPicker>(),
                    // 第五个 picker（配方）：「应用配方」动作行的「选择配方」用。同一口径。
                    Container.Resolve<IScadaRecipePicker>()));
            // 报警配置弹窗同一手法。它同样"每一次编辑都当场写回模型"，没有统一的提交点，
            // 所以 audit / actorProvider 两个可选参数漏接的话，一条线索都不会留下。
            // picker 也必须显式传：它是"为报警选监视变量"那个按钮用的，
            // 交给容器去猜的结果是编译能过、运行起来点「选择变量」没反应。
            Prism.Mvvm.ViewModelLocationProvider.Register<ScadaAlarmConfigDialogView>(() =>
                new ScadaAlarmConfigDialogViewModel(
                    Container.Resolve<IWorkspaceManager>(),
                    Container.Resolve<IScadaVariablePicker>(),
                    Container.Resolve<ScadaAuditWriter>(),
                    () => Container.Resolve<ScadaAccessPolicy>().CurrentUserName,
                    // 运行态信号：标题栏「运行中」胶囊与底栏琥珀提示都靠它。
                    // 用 IFlowRunState.AnyFlowRunning，不用 ScadaMonitoringHost.IsActive——
                    // 后者是方案级监控开关，不是"流程真的在跑"。
                    Container.Resolve<IFlowRunState>()));
            // 权限体系的两个弹窗（S12）。注册名与调用点逐字一致：
            // "ScadaLoginView" ← ShellViewModel.OnSystemAction(SystemAction.UserLogin) + 状态栏点击
            // "ScadaUserManagerView" ← ScadaLoginViewModel.OnOpenUserManager（登录弹窗里的「用户管理」）
            // 两个 VM 都由容器装配（AutoWireViewModel），所以构造注入的 ScadaUserStore /
            // ScadaAccessPolicy / IDialogService 必须已经在上面注册好——这也是它们排在这里的原因。
            containerRegistry.RegisterDialog<ScadaLoginView, ScadaLoginViewModel>("ScadaLoginView");
            containerRegistry.RegisterDialog<UserManagementView, UserManagementViewModel>("UserManagementView");
            // 软件级操作记录：VM 手工装配（要把当前进程那份 SQLite 日志实例传进去，
            // 让界面读的和写的是同一个库；交给容器去 new 会解析出一份指向默认路径的新实例）。
            ViewModelLocationProvider.Register<OperationsLogView>(() => new OperationsLogViewModel(
                Container.Resolve<ScadaAccessPolicy>(),
                SoftwareOperationLogHost.Current as SqliteSoftwareOperationLog));
            containerRegistry.RegisterDialog<OperationsLogView, OperationsLogViewModel>("OperationsLogView");
            // 项目资产：写入口只走 ProjectAssetService（权限 + 版本 + 审计），VM 从工厂注入。
            // 资产 service 必须是**当前方案**的写入口，不能构造时定死一份方案；
            // 每次解析时按工作区当前方案新建（资产写是低频操作，不值得缓存）。
            Func<ProjectAssetService> resolveAssetService = () => new ProjectAssetService(
                new SolutionAssetWriter(
                    Container.Resolve<IReadOnlyWorkspaceContext>().CurrentSolution ?? new SolutionModel()),
                Container.Resolve<IScadaAccessPolicy>(),
                Container.Resolve<ScadaAuditWriter>(),
                () => Container.Resolve<SolutionIdentityHolder>().AuditTag);
            Prism.Mvvm.ViewModelLocationProvider.Register<ProjectAssetsView>(() =>
                new ProjectAssetsViewModel(
                    Container.Resolve<IReadOnlyWorkspaceContext>(),
                    Container.Resolve<IReadOnlyWorkspaceContext>().CurrentSolution == null
                        ? null
                        : resolveAssetService()));
            containerRegistry.RegisterDialog<ProjectAssetsView, ProjectAssetsViewModel>("ProjectAssetsView");
            containerRegistry.RegisterDialog<ScadaGroupEditorView, ScadaGroupEditorViewModel>("ScadaGroupEditorView");
            containerRegistry.RegisterDialog<ScadaPermissionCatalogView, ScadaPermissionCatalogViewModel>("ScadaPermissionCatalogView");
            containerRegistry.RegisterDialog<PluginConfigShellView, PluginConfigShellViewModel>("PluginConfigShell");
            // 帮助手册：目录合并已从启动自检链挪到主窗口显示后后台跑（OnInitialized →
            // RunHelpCatalogCheckInBackground），这里注册服务与弹窗两件事。服务走工厂显式传依赖，
            // 与上面几处"可选参数显式传"同一手法——交给容器去猜的结果是编译能过、运行起来目录永远空着。
            // 懒构建兜底在 HelpViewModel.OnDialogOpened（后台没跑完时当场 Build）。
            containerRegistry.RegisterSingleton<HelpCatalogService>(c => new HelpCatalogService(
                c.Resolve<IPluginProvider>(),
                c.Resolve<ILogService>()));
            containerRegistry.RegisterDialog<HelpView, HelpViewModel>("HelpView");
            // 调试面板：只读诊断 + 调试模式开关。能不能打开由 ShellViewModel 经 DebugModeService 判
            // （未上线免权限 / 上线后需「用户管理」），弹窗自己不判权限——判据只留一份。
            containerRegistry.RegisterDialog<DebugPanelView, DebugPanelViewModel>("DebugPanelView");
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
            try { _sessionCoordinator?.Stop(); } catch { /* 退出链不应被协调器异常打断 */ }
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

            // 方案切换 = 会话边界：先挂订阅，再把"启动时已经打开的方案"补一次同步
            // （Shell 构造期可能早于 OnInitialized 完成首次 SwitchSolution）。
            _sessionCoordinator = Container.Resolve<SolutionSessionCoordinator>();
            _sessionCoordinator.Start();
            _sessionCoordinator.HandleSolutionSwitched(
                Container.Resolve<IReadOnlyWorkspaceContext>().CurrentSolution);

            // 上次异常退出/有未保存草稿的提示：延到界面空闲再弹，别卡在启动链上
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(ShowRecoveryNotice));

            // HALCON 不可用的提示：同样延到界面空闲 —— 自检那一刻 Splash 转瞬即逝，
            // 详细说明必须在主界面出来之后才看得见
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(ShowHalconNotice));

            // 通讯连通性：主窗口已显示，自动连接此刻已爬过一会儿，检测放后台跑、不占启动时间
            RunCommunicationCheckInBackground();

            // 帮助手册合并：与通讯连通性同一先例——只报不拦、不站在启动关键路径上（见 OnInitialized 的注释）
            RunHelpCatalogCheckInBackground();

            // 语音播报自检：与上面两项同一先例。没有中文语音包只影响"念不念得出声"，
            // 报警记录/上屏/提示音全都不受影响，所以只落一行 Warn、绝不站在启动链上（R57）。
            RunSpeechCheckInBackground();
        }

        /// <summary>
        /// 后台检查本机有没有可用的语音包（TTS）。没有就落一行 Warn，告诉用户去哪装。
        /// 只报不拦：没有语音包时报警照常记录与上屏，播报退化为提示音。
        /// </summary>
        private void RunSpeechCheckInBackground()
        {
            _ = Task.Run(() =>
            {
                try
                {
                    using var engine = new Services.SapiSpeechEngine();
                    var voices = engine.Voices;
                    var log = TryResolveService<ILogService>();
                    if (voices.Count == 0)
                    {
                        log?.Warn("[语音] 本机没有可用的语音包，报警/组态播报已退化为提示音"
                                  + "（控制面板 → 时间和语言 → 语音 → 添加语音，选中文）");
                    }
                    else
                    {
                        log?.Info($"[语音] 可用语音包 {voices.Count} 个：{string.Join("、", voices)}");
                    }
                }
                catch (Exception ex)
                {
                    // 语音组件本身缺失（精简系统/Server Core）也只落一行，不阻断任何东西
                    TryResolveService<ILogService>()?.Warn($"[语音] 语音组件自检失败：{ex.Message}；播报将退化为提示音");
                }
            });
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
        /// <summary>
        /// 解析可选模块依赖：没注册（纯视觉/离线场景）就返回 null，不让容器报错。
        /// 只用于“有就用、没有也能跑”的轴接口；核心依赖仍走 <c>Resolve</c>，缺了必须启动失败。
        /// </summary>
        private static T? ResolveOptional<T>(IContainerProvider container) where T : class
        {
            try { return container.Resolve<T>(); }
            catch { return null; }
        }

        /// <summary>
        /// 运行态未授权点击的登录回调（B3）：弹标准登录窗，等它关闭后按最新权限重判。
        ///
        /// <b>为什么要 PushFrame</b>：Prism 的 <c>ShowDialog</c> 是"显示后立即返回、关闭时回调"，
        /// 而权限判定是同步的——不在这里等，调用方会拿着"还没登录"的会话立刻重判失败。
        /// 若某天实现改成同步阻塞，回调会先跑完，<see cref="DispatcherFrame.Continue"/> 已为 false，
        /// PushFrame 立即返回，两种实现都成立。
        /// </summary>
        private static bool ShowRuntimeAccessLoginPrompt(IContainerProvider container, string requiredPermission)
        {
            if (string.IsNullOrWhiteSpace(requiredPermission))
                return true;

            var policy = container.Resolve<IScadaAccessPolicy>();
            if (policy.Can(requiredPermission, out _))
                return true;

            try
            {
                var dialogService = container.Resolve<IDialogService>();
                bool ok = false;
                var frame = new DispatcherFrame();

                dialogService.ShowDialog(
                    "ScadaLoginView",
                    new DialogParameters
                    {
                        { "RequiredPermission", requiredPermission },
                        { "RequiredPermissionName", ScadaPermissionKeys.DisplayName(requiredPermission) },
                    },
                    result =>
                    {
                        ok = result.Result == ButtonResult.OK;
                        frame.Continue = false;
                    });

                Dispatcher.PushFrame(frame);

                return ok && policy.Can(requiredPermission, out _);
            }
            catch (Exception ex)
            {
                // 弹窗起不来不能把运行态点击链带崩：按"没有权限"处理，调用方会给回执。
                try { container.Resolve<ILogService>().Warn($"[权限] 运行态登录弹窗打开失败：{ex.GetType().Name}：{ex.Message}"); }
                catch { }
                return false;
            }
        }

        /// <summary>
        /// 动作「登录」专用：<b>无条件</b>弹登录窗（换人登录）。
        ///
        /// 与 <see cref="ShowRuntimeAccessLoginPrompt"/> 的唯一区别：不做"已有权限就直接返回 true"的短路。
        /// 那条短路是给"点不动 → 补权限"用的；动作「登录」的语义是"我要换个账号"，
        /// 走短路会出现"点了登录什么都没发生"（日志还记一条"已登录"）。
        ///
        /// 不传 RequiredPermission：登录窗的 OnDialogOpened 只清输入、不读参数，
        /// 传了也不会显示成"需要某权限"的提示，反而会误导操作员。
        /// </summary>
        private static bool ShowRuntimeLoginPromptAlways(IContainerProvider container)
        {
            try
            {
                var dialogService = container.Resolve<IDialogService>();
                bool ok = false;
                var frame = new DispatcherFrame();

                dialogService.ShowDialog("ScadaLoginView", new DialogParameters(), result =>
                {
                    ok = result.Result == ButtonResult.OK;
                    frame.Continue = false;
                });

                Dispatcher.PushFrame(frame);

                return ok;
            }
            catch (Exception ex)
            {
                // 弹窗起不来不能把事件动作链带崩：按"没登录"处理，分发器会记一条跳过。
                try { container.Resolve<ILogService>().Warn($"[登录动作] 登录弹窗打开失败：{ex.GetType().Name}：{ex.Message}"); }
                catch { }
                return false;
            }
        }

        /// <summary>删除权限守卫：统计全方案页面/图元里引用该 Key 的按钮数。</summary>
        private static int CountPermissionReferences(IWorkspaceManager workspace, string key)
        {
            var solution = workspace?.CurrentSolution;
            var document = solution?.Scada;
            if (document == null || string.IsNullOrWhiteSpace(key)) return 0;

            var count = 0;
            foreach (var page in document.Pages.Concat(document.BasePages))
            {
                if (page == null) continue;
                if (string.Equals(page.RequiredPermission, key, StringComparison.Ordinal))
                    count++;
                else if (string.IsNullOrWhiteSpace(page.RequiredPermission)
                         && page.RequiredRole is { } pageRole
                         && string.Equals(ScadaPermissionKeys.LegacyRolePermission(pageRole), key, StringComparison.Ordinal))
                    count++;

                foreach (var element in page.Elements)
                {
                    if (element == null) continue;
                    if (string.Equals(element.RequiredPermission, key, StringComparison.Ordinal))
                        count++;
                    else if (string.IsNullOrWhiteSpace(element.RequiredPermission)
                             && element.RequiredRole is { } elementRole
                             && string.Equals(ScadaPermissionKeys.LegacyRolePermission(elementRole), key, StringComparison.Ordinal))
                        count++;
                }
            }
            return count;
        }

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

        /// <summary>
        /// 断开 ScadaRuntimeHost → IScudaActionDispatcher → IScadaAlarmCommands → ScadaRuntimeHost
        /// 的构造期 DI 环（2026-10-30，启动即 StackOverflow）：适配器构造零依赖（只捕获解析委托），
        /// 首次派发报警命令时才解析运行宿主单例——那时三个单例都已就绪，
        /// 行为与直接注入宿主完全一致（同一个实例、同一批成员）。
        /// </summary>
        private sealed class LazyAlarmCommands : IScadaAlarmCommands
        {
            private readonly Func<IScadaAlarmCommands> _factory;
            public LazyAlarmCommands(Func<IScadaAlarmCommands> factory) => _factory = factory;

            private IScadaAlarmCommands Real => _factory();

            public bool TryAcknowledge(Guid alarmId, out string? error)
                => Real.TryAcknowledge(alarmId, out error);

            public int AcknowledgeAll(out string? error)
                => Real.AcknowledgeAll(out error);
        }

        /// <summary>
        /// 断开 ScadaActionDispatcher → IScadaAlarmMute → ScadaMonitoringHost → IScudaActionDispatcher
        /// 的第二个构造期环（与 <see cref="LazyAlarmCommands"/> 同族、同修法）：构造零依赖，
        /// 成员转发到首次调用时的适配器实例——适配器"每次现取声音服务"的语义不变。
        /// </summary>
        private sealed class LazyAlarmMute : IScadaAlarmMute
        {
            private readonly Func<IScadaAlarmMute> _factory;
            public LazyAlarmMute(Func<IScadaAlarmMute> factory) => _factory = factory;

            private IScadaAlarmMute Real => _factory();

            public bool IsMuted => Real.IsMuted;

            public int MuteMinutes => Real.MuteMinutes;

            public bool TryMute(int minutes, out string? error) => Real.TryMute(minutes, out error);

            public bool TryUnmute(out string? error) => Real.TryUnmute(out error);
        }
    }
}
