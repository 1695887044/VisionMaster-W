using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Core.Interfaces;
using VisionMaster;
using VisionMaster.Lifetime;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// [E17] 引擎全面审查「第一批修复」的断言：
    ///  · ImageHub 的精确收回（被拒请求不许把自己的帧留给下一次触发）与重建/切方案清槽；
    ///  · 退出链必须能对"卡住的退出任务"超时跳过（原先同步 action 让超时形同虚设）；
    ///  · 绑定写回必须走会通知的 API（直改字典 = 版本不涨 = 已编译会话跑旧连线）；
    ///  · C# 脚本的引用集必须拒绝原生 DLL（宿主目录里的 onnxruntime 会让脚本必然编译失败）；
    ///  · 运行中"改名/删除流程"的守卫与旧会话回收。
    ///
    /// 说明：UI 层（流程栏命令）没法在无 GUI 宿主里真点，那几条用**源码纪律断言**兜住
    /// （仓库既有做法：MotionZMotionChecks 等也这样钉界面层的"必须这么写"）。
    /// </summary>
    internal static class FlowEngineFixChecks
    {
        internal static void Run()
        {
            HubPreciseRemove();
            SessionReplacementAndSolutionSwitchClearHub();
            ExitChainCanTimeOut();
            StartupChainTimeoutIsNotFatal();
            CommunicationCheckRunsAfterShell();
            HelpCatalogCheckRunsAfterShell();
            BindingWritesGoThroughNotifyingApi();
            CSharpScriptRefusesNativeDlls();
            RunGuardsAreInPlace();
            ExcelExportManualDispatchIsGuarded();
            StopIsObservableAndReachable();
            StartupSolutionFailureIsObservable();
            FlowBatchSelectionChecks.Run();
        }

        private static HubImageItem NewHubItem(string requestId, string flowName) => new()
        {
            RequestId = requestId,
            FlowName = flowName,
            PixelData = new byte[1],
            Width = 1,
            Height = 1,
            Channels = 1,
            SourceName = requestId,
        };

        /// <summary>从输出目录往上找仓库根，再拼相对路径（定位不到返回 null，由调用方跳过）</summary>
        private static string ResolveRepoFile(string relative)
        {
            var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate)) return candidate;
            }
            return null;
        }

        // ==================================================================
        //  [E17] ① ImageHub：按请求精确收回（不误伤别人的帧、保序）
        // ==================================================================
        private static void HubPreciseRemove()
        {
            Section("[E17] ImageHub：按请求精确收回");

            ImageHub.ClearAll();
            ImageHub.Push(NewHubItem("r-A", "请求流"));
            ImageHub.Push(NewHubItem("r-B", "请求流"));
            ImageHub.Push(NewHubItem("r-C", "请求流"));
            Check("起始 3 帧", ImageHub.Count("请求流") == 3, $"Count={ImageHub.Count("请求流")}");

            bool removed = ImageHub.TryRemove("r-B");
            Check("★按 RequestId 精确移除指定那一帧（3 → 2）",
                removed && ImageHub.Count("请求流") == 2, $"removed={removed} Count={ImageHub.Count("请求流")}");

            // TryPop 的语义是"排空到最后一帧、取最新"（见其注释），所以只能验"最新那帧是 C"
            // ——A 没被误删由上面 Count==2 佐证；"删的确实是 B 而不是别的"用下面 A/B 两帧的用例正面证明
            bool poppedLatest = ImageHub.TryPop("请求流", out var latest);
            Check("删中间帧后，最新一帧仍是 C（保序未被破坏）",
                poppedLatest && latest?.RequestId == "r-C", $"latest={latest?.RequestId}");

            ImageHub.Clear("请求流");
            ImageHub.Push(NewHubItem("r-A", "请求流"));
            ImageHub.Push(NewHubItem("r-B", "请求流"));
            ImageHub.TryRemove("r-A");
            bool afterRemove = ImageHub.TryPop("请求流", out var survivor);
            Check("★删的确实是点名那一帧（删 A 后取到的是 B）",
                afterRemove && survivor?.RequestId == "r-B", $"survivor={survivor?.RequestId}");
            Check("已取走/不存在的请求号 → false（幂等）", !ImageHub.TryRemove("r-B"), "");
        }

        // ==================================================================
        //  [E17] ② 会话重建 / 切方案 → 清收图槽（旧帧不许留给下一轮）
        // ==================================================================
        private static void SessionReplacementAndSolutionSwitchClearHub()
        {
            Section("[E17] 收图槽：会话重建与切方案清槽");

            ImageHub.ClearAll();
            ImageHub.Push(NewHubItem("r-X", "重建流"));

            var runtime = new RuntimeManager();
            var session = new FlowSession { FlowName = "重建流" };
            runtime.RegisterSession(session);
            Check("★RegisterSession（= 会话重建）清掉该流程的陈旧帧",
                ImageHub.Count("重建流") == 0, $"Count={ImageHub.Count("重建流")}");
            runtime.UnregisterSession(session.SessionID);

            ImageHub.Push(NewHubItem("r-Y", "切换流"));
            var workspace = new WorkspaceContext();
            workspace.SwitchSolution(new SolutionModel());
            Check("★切方案清空所有收图槽（跨方案的帧没有归属）",
                ImageHub.Count("切换流") == 0, $"Count={ImageHub.Count("切换流")}");
        }

        // ==================================================================
        //  [E17] ③ 退出链：卡住的任务必须被超时跳过（真跑一遍）
        // ==================================================================
        private static void ExitChainCanTimeOut()
        {
            Section("[E17] 退出链：卡住的退出任务可超时跳过");

            var log = new StubLog();
            var lifetime = new AppLifetimeService(log);
            using var release = new ManualResetEventSlim(false);

            // 故意卡住 30s 的任务 + 一个正常任务；超时给 300ms
            lifetime.RegisterExitTask(ExitTask.Of("故意卡住的任务",
                () => release.Wait(TimeSpan.FromSeconds(30)), timeoutMs: 300));
            lifetime.RegisterExitTask(ExitTask.Of("正常任务", () => { }));

            var sw = Stopwatch.StartNew();
            lifetime.ExecuteExitChainAsync("断言").GetAwaiter().GetResult();
            sw.Stop();

            Check("★卡住的退出任务被超时跳过（链没有挂死）",
                sw.Elapsed < TimeSpan.FromSeconds(5), $"链耗时={sw.ElapsedMilliseconds}ms");
            Check("超时被如实记录（日志里有「超时…跳过」）",
                log.Infos.Concat(log.Warns).Any(m => m.Contains("超时") && m.Contains("跳过")),
                string.Join(" | ", log.Infos.Concat(log.Warns).TakeLast(4)));
            Check("卡住任务之后的项照常执行（逆序：正常任务先跑，卡住的后跑）",
                log.Infos.Any(m => m.Contains("正常任务")), string.Join(" | ", log.Infos.TakeLast(4)));

            release.Set();
        }

        // ==================================================================
        //  [E17] ③b 启动链：慢自检项超时不得把进程打死
        // ==================================================================
        private static void StartupChainTimeoutIsNotFatal()
        {
            Section("[E17] 启动链：慢项超时按声明级别处置（插件扫描 = Warning 放行）");

            // 2026-10-15 真机事故：插件扫描冷读 Modules\（十几 MB DLL）实测 1.1s ~ 28.8s，
            // 30 秒默认超时正好落在波动带中间，超时被判 Error → Environment.Exit(2)
            // → 用户看到"第一次启动自己退了、第二次才成功"（第二次磁盘缓存已热）。
            // 这条断言钉住两个口径：① 超时级别由该项自己声明；② 默认级别仍是 Error（不许被顺手改成全放行）。
            var log = new StubLog();
            var lifetime = new AppLifetimeService(log);

            // 故意卡住的自检项，超时 200ms；声明超时按 Warning 处置
            lifetime.RegisterCheck(new SlowWarningCheck());
            // 紧跟一个正常项：Warning 放行后，启动链必须继续往下走
            lifetime.RegisterCheck(new InstantCheck());

            var sw = Stopwatch.StartNew();
            var result = lifetime.RunStartupChecksAsync().GetAwaiter().GetResult();
            sw.Stop();

            Check("★慢项超时不再阻断启动（Warning 放行，链继续跑完）",
                result.Success,
                $"Success={result.Success}，BlockingReason={result.BlockingReason ?? "(无)"}");
            Check("★超时被记为警告（Splash/日志能看见，不是静默吞掉）",
                result.Warnings.Any(w => w.Contains("超时")),
                result.Warnings.Count == 0 ? "(无警告)" : string.Join(" | ", result.Warnings));
            Check("超时项之后的检查照常执行（放行不等于终止链）",
                log.Infos.Any(m => m.Contains("正常项")),
                string.Join(" | ", log.Infos.TakeLast(3)));
            Check("慢项按自己的 200ms 超时处置，而不是等满默认 30 秒",
                sw.Elapsed < TimeSpan.FromSeconds(5), $"链耗时={sw.ElapsedMilliseconds}ms");

            // 反向：默认级别必须仍是 Error —— 否则"任何自检超时都放行"会把单实例/配置校验也放过去
            var strict = new AppLifetimeService(new StubLog());
            strict.RegisterCheck(new SlowDefaultCheck());
            var strictResult = strict.RunStartupChecksAsync().GetAwaiter().GetResult();
            Check("★未声明放行的项，超时仍按 Error 阻断启动（默认口径没被改掉）",
                !strictResult.Success && strictResult.BlockingReason != null && strictResult.BlockingReason.Contains("超时"),
                $"Success={strictResult.Success}，BlockingReason={strictResult.BlockingReason ?? "(无)"}");
        }

        // ==================================================================
        //  [E18] 通讯连通性：不站在启动关键路径上，改为主窗口显示后后台跑
        // ==================================================================
        /// <summary>
        /// 2026-10-16 用户问"启动自检串行太慢，能不能并行"。查下来最大的收益不是并行，而是
        /// 把"只报不拦"的通讯连通性检测挪出启动链：它全部失败出口都是 Warning，代价却是
        /// 3 秒追赶窗口 + 每条最多 3 秒（总预算 12 秒）。
        /// 这套约定全是静默失效型的——有人把 RegisterCheck 加回去、或删掉后台那一次调用，
        /// 编译都照过，只是启动又慢回去（或者通讯再也不被检测）。所以五条断言逐个钉住：
        /// ①链上不许有它 ②OnInitialized 必须真的跑一次 ③OnExit 必须叫停 ④它仍全 Warning
        /// （前提一旦变成 Error 就该回链上）⑤其余五项不许被顺手一起删。
        /// ⑥（2026-10-19 补）遍历连接前必须取持锁快照（GetAllConnections）——
        /// 后台自检与方案装配并发，直接遍历活动集合会抛「Collection was modified」。
        /// </summary>
        private static void CommunicationCheckRunsAfterShell()
        {
            Section("[E18] 通讯连通性：移出启动链 + 主窗口显示后后台跑一次");

            var repoRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\.."));
            var appPath = Path.Combine(repoRoot, @"VisionMaster\App.xaml.cs");
            var checkPath = Path.Combine(repoRoot, @"VisionMaster\Lifetime\Checks\CommunicationCheck.cs");
            var app = File.Exists(appPath) ? File.ReadAllText(appPath) : "";
            var check = File.Exists(checkPath) ? File.ReadAllText(checkPath) : "";

            Check("★启动自检链里没有通讯连通性（RegisterCheck 不许加回来）",
                app.Length > 0 && !app.Contains("RegisterCheck(new CommunicationCheck"),
                app.Length == 0 ? "App.xaml.cs 没找到" : "链上注册行存在=启动又慢回去");

            var onInitialized = MethodBody(app, "protected override void OnInitialized()");
            Check("主窗口显示后确实跑一次（OnInitialized 里调用后台入口）",
                onInitialized.Contains("RunCommunicationCheckInBackground()"),
                $"OnInitialized 体长={onInitialized.Length}");

            var onExit = MethodBody(app, "protected override void OnExit(ExitEventArgs e)");
            Check("退出时叫停后台检测（不许在关闭过程中继续碰连接管理器）",
                onExit.Contains("_postStartupCts.Cancel()"),
                $"OnExit 体长={onExit.Length}");

            // 反向前提：这个挪动成立的全部理由是"它只出 Warning"。哪天它开始拦启动，就必须回链上。
            int errorInCheck = check.Split(new[] { "CheckLevel.Error" }, StringSplitOptions.None).Length - 1;
            Check("★它的所有失败出口仍全是 Warning（变成 Error 就该回启动链，这条会红提醒你）",
                errorInCheck == 0 && check.Contains("CheckLevel.Warning"),
                $"CheckLevel.Error 出现 {errorInCheck} 次 / Warning 存在={check.Contains("CheckLevel.Warning")}");

            // 反向：只挪走这一项，其余四项（单实例/配置/HALCON/插件扫描）必须还在链上。
            // 帮助手册合并 2026-10-16 也已挪到主窗口显示后后台跑（同一条先例，见 [E19]），
            // 所以链上现在是 4 项——有人把 RegisterCheck 加回去或顺手删掉别的，这里都会红。
            int registered = app.Split(new[] { "RegisterCheck(" }, StringSplitOptions.None).Length - 1;
            Check("其余启动自检项没被顺手删掉（链上仍有 4 项注册）",
                registered == 4, $"RegisterCheck( 出现 {registered} 次（含 _singleInstance 那一次）");

            // ⑥ 挪到后台带来的竞态：方案装配会在同一时段增删连接，
            //    直接遍历活动集合必现「Collection was modified; enumeration operation may not execute」
            //    （2026-10-19 真机日志实测命中）。
            //    必须用 GetAllConnections()——它在 _connectionsListLock 内 ToList；
            //    旧写法只是绕开枚举器版本检查，没持锁，仍会读到撕裂快照。
            //    判据先去掉注释：说明文字里会提到被禁的旧写法，不去注释会误判成"仍残留"。
            string checkCode = StripComments(check);
            Check("后台自检遍历前取持锁快照（GetAllConnections），不遍历活动集合",
                checkCode.Contains("GetAllConnections()") && !checkCode.Contains("ConnectionsList.ToList()"),
                !checkCode.Contains("GetAllConnections()")
                    ? "未使用 GetAllConnections：并发增删时会读到撕裂快照"
                    : (checkCode.Contains("ConnectionsList.ToList()") ? "仍残留 ConnectionsList.ToList()" : "已取持锁快照"));
        }

        // ==================================================================
        //  [E19] 帮助手册合并：不站在启动关键路径上，主窗口显示后后台跑（同 E18 先例）
        // ==================================================================
        /// <summary>
        /// 2026-10-16 启动提速层 2c：帮助手册合并实测约 1.2 秒，却只给帮助弹窗供数、失败全是 Warning——
        /// 与通讯连通性同一形状（"只报不拦却守在关键路径上"），按同一先例挪出启动链。
        /// 兜底是现成的：HelpViewModel.OnDialogOpened 有懒构建（!IsBuilt → Build），
        /// 后台没跑完时用户打开帮助会当场构建，行为不降级。
        /// 三条断言钉住：①链上不许有它 ②OnInitialized 必须真的跑一次 ③懒构建兜底必须还在
        ///（兜底没了，"后台没跑完 + 用户秒开帮助"就是空白目录）。
        /// </summary>
        private static void HelpCatalogCheckRunsAfterShell()
        {
            Section("[E19] 帮助手册合并：移出启动链 + 主窗口显示后后台跑一次（兜底懒构建在位）");

            var repoRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\.."));
            var appPath = Path.Combine(repoRoot, @"VisionMaster\App.xaml.cs");
            var vmPath = Path.Combine(repoRoot, @"VisionMaster\ViewModels\DialogViewModels\HelpViewModel.cs");
            var app = File.Exists(appPath) ? File.ReadAllText(appPath) : "";
            var vm = File.Exists(vmPath) ? File.ReadAllText(vmPath) : "";

            Check("★启动自检链里没有帮助手册合并（RegisterCheck 不许加回来）",
                app.Length > 0 && !app.Contains("RegisterCheck(new HelpCatalogCheck"),
                app.Length == 0 ? "App.xaml.cs 没找到" : "链上注册行存在=启动又慢回去");

            var onInitialized = MethodBody(app, "protected override void OnInitialized()");
            Check("主窗口显示后确实跑一次（OnInitialized 里调用后台入口）",
                onInitialized.Contains("RunHelpCatalogCheckInBackground()"),
                $"OnInitialized 体长={onInitialized.Length}");

            // 兜底前提：挪出去能成立的全部理由是"懒构建兜底在位"。哪天它被删了，这里红着提醒。
            var onDialogOpened = MethodBody(vm, "public void OnDialogOpened");
            Check("★懒构建兜底仍在（后台没跑完时打开帮助会当场 Build；删了它就该回链上）",
                onDialogOpened.Contains("!_catalog.IsBuilt") && onDialogOpened.Contains("_catalog.Build()"),
                $"OnDialogOpened 体长={onDialogOpened.Length}");
        }


        /// <summary>取某个方法的源码体（从签名后的第一个 { 起按花括号配对截）；找不到返回空串</summary>
        private static string MethodBody(string source, string signature)
        {
            int at = source.IndexOf(signature, StringComparison.Ordinal);
            if (at < 0) return "";
            int open = source.IndexOf('{', at);
            if (open < 0) return "";
            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}' && --depth == 0) return source.Substring(open, i - open + 1);
            }
            return source.Substring(open);
        }

        /// <summary>
        /// 去掉 C# 注释。
        /// <para>静态断言扫的是"代码里还残留什么写法"，而说明性注释里往往**必须**提到被禁的旧写法
        /// （否则后来者不知道为什么不许用）。不去注释就会把"解释为什么禁止"误判成"仍在违规"。</para>
        /// </summary>
        private static string StripComments(string source)
        {
            if (string.IsNullOrEmpty(source)) return source ?? "";

            var noBlock = System.Text.RegularExpressions.Regex.Replace(
                source, @"/\*.*?\*/", string.Empty,
                System.Text.RegularExpressions.RegexOptions.Singleline);

            return System.Text.RegularExpressions.Regex.Replace(
                noBlock, @"//[^\r\n]*", string.Empty,
                System.Text.RegularExpressions.RegexOptions.Multiline);
        }

        // ==================================================================
        //  [EE4] Excel报表手动导出：Task.Run 调度本身失败也要落到终态
        // ==================================================================
        /// <summary>
        /// 2026-10-20：`ExportNow()` 原先只有 Task.Run **内部**的 try/catch/finally。
        /// 线程池拒单、或 lambda 还没进去就被拆掉时，那层 catch 根本没机会执行 ——
        /// `LastResult` 永远停在「正在导出…」、`IsExporting` 一直是 true、按钮从此永久置灰
        /// （比"导出失败"更难查：界面上看不出任何错误）。
        ///
        /// 判据落在**结构**上而不是行号/空白（改缩进、加注释不误伤），四条各自对应一个真实故障形状：
        ///  ① Task.Run 必须落在外层 try 里（否则调度异常直接抛给按钮事件 → UI 线程崩）；
        ///  ② 外层 catch 必须写 LastResult（否则界面停在「正在导出…」）；
        ///  ③ 外层 catch 必须复位 IsExporting（否则按钮永久置灰，且下次连点都被 `if (IsExporting) return` 吞掉）；
        ///  ④ 内层 finally 仍在（正常路径的复位不许被"搬去外层"顺手删掉）。
        /// 变异反证：删掉外层 catch → ② ③ ④ 准确变红（`IsExporting = false` 只剩 1 处）。
        /// </summary>
        private static void ExcelExportManualDispatchIsGuarded()
        {
            Section("[EE4] Excel报表：手动导出的调度失败兜底");

            var file = ResolveRepoFile(@"Plugins\Plugin.ExcelExport\ExcelExportPlugin.cs");
            string source = file != null ? File.ReadAllText(file) : "";
            if (source.Length == 0)
            {
                Check("ExcelExportPlugin.cs 定位（EE4 判据前提）", false, "定位失败");
                return;
            }

            string code = StripComments(source);
            string body = MethodBody(code, "public void ExportNow()");
            Check("ExportNow 方法体可取（判据前提）", body.Length > 0, $"体长={body.Length}");
            if (body.Length == 0) return;

            int taskAt = body.IndexOf("Task.Run(", StringComparison.Ordinal);
            int outerTryAt = body.IndexOf("try", StringComparison.Ordinal);
            Check("★① Task.Run 落在外层 try 内（调度异常不许抛给按钮事件）",
                taskAt > 0 && outerTryAt >= 0 && outerTryAt < taskAt,
                $"try@{outerTryAt} Task.Run@{taskAt}");

            // 外层 catch：从外层 try 的 { 起配对找到块尾，紧随其后的 catch 就是外层那一支
            string outerCatch = "";
            if (outerTryAt >= 0)
            {
                int open = body.IndexOf('{', outerTryAt);
                int close = open < 0 ? -1 : FindMatchingBrace(body, open);
                if (close > 0)
                {
                    int catchAt = body.IndexOf("catch", close, StringComparison.Ordinal);
                    if (catchAt >= 0)
                    {
                        int catchOpen = body.IndexOf('{', catchAt);
                        int catchClose = catchOpen < 0 ? -1 : FindMatchingBrace(body, catchOpen);
                        if (catchClose > catchOpen)
                            outerCatch = body.Substring(catchAt, catchClose - catchAt + 1);
                    }
                }
            }

            Check("★② 外层 catch 写 LastResult（界面不会停在「正在导出…」）",
                outerCatch.Contains("LastResult") && outerCatch.Contains("导出异常"),
                outerCatch.Length == 0 ? "外层 catch 未找到" : $"体长={outerCatch.Length}");

            Check("★③ 外层 catch 复位 IsExporting（按钮不会永久置灰）",
                outerCatch.Contains("IsExporting = false"),
                outerCatch.Length == 0 ? "外层 catch 未找到" : outerCatch);

            Check("异常文案带类型名（现场能分清是哪种异常）",
                outerCatch.Contains("ex.GetType().Name"),
                outerCatch.Length == 0 ? "外层 catch 未找到" : "");

            int resetCount = CountOccurrences(body, "IsExporting = false");
            Check("★④ 内层 finally 仍在（复位点必须两处：内层 finally + 外层 catch）",
                body.Contains("finally") && resetCount == 2,
                $"IsExporting = false 出现 {resetCount} 次（应为 2）");
        }

        /// <summary>从 openIndex 处的 '{' 起按花括号配对找块尾，返回 '}' 的下标；找不到返回 -1</summary>
        private static int FindMatchingBrace(string source, int openIndex)
        {
            int depth = 0;
            for (int i = openIndex; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}' && --depth == 0) return i;
            }
            return -1;
        }

        private static int CountOccurrences(string source, string needle)
            => needle.Length == 0 ? 0 : source.Split(new[] { needle }, StringSplitOptions.None).Length - 1;

        /// <summary>超时按 Warning 放行的慢自检项（模拟插件扫描）</summary>
        private sealed class SlowWarningCheck : IStartupCheck
        {
            public string Name => "慢项（声明 Warning）";
            public int TimeoutMs => 200;
            public CheckLevel TimeoutLevel => CheckLevel.Warning;
            public async Task<CheckResult> ExecuteAsync(CancellationToken ct)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                return CheckResult.Ok("不该跑到这里");
            }
        }

        /// <summary>未声明放行的慢自检项：超时应按默认 Error 阻断</summary>
        private sealed class SlowDefaultCheck : IStartupCheck
        {
            public string Name => "慢项（默认口径）";
            public int TimeoutMs => 200;
            public async Task<CheckResult> ExecuteAsync(CancellationToken ct)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                return CheckResult.Ok("不该跑到这里");
            }
        }

        /// <summary>瞬时通过的自检项</summary>
        private sealed class InstantCheck : IStartupCheck
        {
            public string Name => "正常项";
            public Task<CheckResult> ExecuteAsync(CancellationToken ct)
                => Task.FromResult(CheckResult.Ok("正常项照常执行"));
        }

        // ==================================================================
        //  [E17] ④ 绑定写回：走会通知的 API + 弹窗源码纪律
        // ==================================================================
        private static void BindingWritesGoThroughNotifyingApi()
        {
            Section("[E17] 绑定写回：必须走会通知的 API");

            var flow = new FlowModel { FlowName = "绑定纪律" };
            var step = new ActionStep("T", "测试算子", "BuiltIn_Test", "绑定步骤");
            flow.Steps.Add(step);

            int v = flow.Version;
            step.SetLink("Image", new LinkReference(LinkKind.Constant, Guid.Empty, "1", "常量:1"));
            Check("SetLink 递增 Version（绑定写回走它，图纸才「看得见」）",
                flow.Version == v + 1, $"Version {v} → {flow.Version}");
            v = flow.Version;

            step.RemoveLink("Image");
            Check("RemoveLink 递增 Version（解绑同理）", flow.Version == v + 1, $"Version {v} → {flow.Version}");

            var vmFile = ResolveRepoFile(@"VisionMaster\ViewModels\DialogViewModels\VariableBindingViewModel.cs");
            var text = vmFile != null ? File.ReadAllText(vmFile) : string.Empty;
            Check("★绑定弹窗不再直改字典（源码里没有 LinkedSources[…] 写入/直删）",
                text.Contains("SetLink(") && text.Contains("RemoveLink(")
                && !text.Contains("LinkedSources[bindKey] =")
                && !text.Contains("LinkedSources.Remove("),
                vmFile == null ? "定位不到 VariableBindingViewModel.cs" : "");
        }

        // ==================================================================
        //  [E17] ⑤ C# 脚本：原生 DLL 不得进引用集（判据 + 源码 + 红线③）
        // ==================================================================
        private static void CSharpScriptRefusesNativeDlls()
        {
            Section("[E17] C# 脚本：原生 DLL 过滤");

            // 判据实测：拿真的"肇事物"（Yolo 部署到宿主目录的 onnxruntime 原生 DLL）验一刀
            var repoRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\.."));
            var candidates = new[]
            {
                Path.Combine(repoRoot, @"VisionMaster\bin\Debug\net9.0-windows\onnxruntime_providers_shared.dll"),
                Path.Combine(repoRoot, @"VisionMaster\bin\Release\net9.0-windows\onnxruntime_providers_shared.dll"),
            };
            var nativeDll = candidates.FirstOrDefault(File.Exists);

            if (nativeDll == null)
            {
                Check("宿主输出目录含原生 DLL 这一前提（本机未构建过 Yolo 部署则跳过判据实测）", true,
                    "未找到 onnxruntime_providers_shared.dll");
            }
            else
            {
                bool isManaged = true;
                try { AssemblyName.GetAssemblyName(nativeDll); }
                catch (Exception) { isManaged = false; }

                Check("★过滤判据成立：该原生 DLL 会被 IsManagedAssembly 判否（GetAssemblyName 抛 BadImageFormat）",
                    !isManaged, nativeDll);
            }

            var srcFile = ResolveRepoFile(@"Plugins\Plugin.CSharpScript\CSharpScriptEngine.cs");
            var srcText = srcFile != null ? File.ReadAllText(srcFile) : string.Empty;
            Check("★脚本引擎在加入引用前先判托管（源码含 IsManagedAssembly 调用）",
                srcText.Contains("if (!IsManagedAssembly(path)) return;"),
                srcFile == null ? "定位不到 CSharpScriptEngine.cs" : "");

            // 红线③：改了插件必须重建并投递（DLL 不得早于源码）
            var dllFile = ResolveRepoFile(@"Modules\Plugin.CSharpScript.dll");
            if (srcFile != null && dllFile != null)
            {
                var srcWrite = File.GetLastWriteTimeUtc(srcFile);
                var dllWrite = File.GetLastWriteTimeUtc(dllFile);
                Check("★改插件后已重建并投递到 Modules（DLL 不早于源码）", dllWrite >= srcWrite,
                    $"源码={srcWrite:MM-dd HH:mm:ss} DLL={dllWrite:MM-dd HH:mm:ss}");
            }
            else
            {
                Check("插件源码/DLL 定位（红线③核对前提）", false, "定位失败");
            }
        }

        // ==================================================================
        //  [E17] ⑥ 运行中守卫：改名/删除被拦 + 旧会话被回收（界面层源码纪律）
        // ==================================================================
        private static void RunGuardsAreInPlace()
        {
            Section("[E17] 运行中守卫：改名/删除流程");

            var vmFile = ResolveRepoFile(@"VisionMaster\ViewModels\FlowListViewModel.cs");
            var text = vmFile != null ? File.ReadAllText(vmFile) : string.Empty;

            Check("★运行中禁止删除流程（守卫在）", text.Contains("正在运行，禁止删除"),
                vmFile == null ? "定位不到 FlowListViewModel.cs" : "");
            Check("★运行中禁止改名流程（守卫在）", text.Contains("正在运行，禁止改名"),
                vmFile == null ? "定位不到 FlowListViewModel.cs" : "");
            Check("★改名后回收旧名会话（生产代码里真的调用 UnregisterSession）",
                text.Contains("UnregisterSession("), "");
        }

        // ==================================================================
        //  [P1A-S6] 停止可观测：CanExecute / 日志 / 条数
        // ==================================================================
        /// <summary>
        /// 2026-10-22 S6。三件事各自对应一个"现场说不清"的形状：
        ///  ① Stop 的 CanExecute 只看 MainRunState —— 单流程运行 / HTTP 触发都不写它，
        ///     于是"流程明明在跑、停止按钮是灰的"（与方案互锁 CanExecuteSolution 是同一口径问题）；
        ///  ② 正常停止零日志 —— 用户说"点了停止没反应"，日志里查不到任何东西；
        ///  ③ StopAll 不回报条数 —— 提示只能写死"已停止所有运行中的流程"，0 条时也是这句假话。
        /// ①②③ 里只有 ③ 能纯运行时验（引擎侧）；①② 是界面层，用源码纪律断言兜住
        /// （仓库既有做法：FlowListViewModel 的守卫、ExcelExport 的调度兜底都这么钉）。
        /// </summary>
        private static void StopIsObservableAndReachable()
        {
            Section("[P1A-S6] 停止可观测：CanExecute / 日志 / 条数");

            // ---- ① 引擎侧真跑一遍：StopSession 留 Info、StopAll 返回条数 ----
            GatePlugin.Reset();
            var gate = new ActionStep(
                "S6G", "闸门", typeof(GatePlugin).AssemblyQualifiedName!, "停止闸门");
            var run = ExecHarness.Prepare(new StepModel[] { gate });
            Check("S6 停止用例图纸编译通过", run.Compiled, run.Errors);
            if (run.Compiled)
            {
                var log = new StubLog();
                var manager = new RuntimeManager();
                var engine = new FlowEngineService(
                    manager, log, run.Workspace!, null,
                    new ResourceLockService(), NullCameraProvider.Instance);
                var session = run.Session!;
                // 必须登记进 RuntimeManager：StopAll 走的是它的活动会话快照
                // （没登记 = 快照里没有它 = 永远返回 0，断言会误判成"引擎不回报条数"）。
                manager.RegisterSession(session);

                var task = engine.RunSessionAsync(session);
                bool arrived = GatePlugin.Reached.Wait(TimeSpan.FromSeconds(5));
                Check("S6 执行线程已抵达闸门（构造出运行中的窗口）",
                    arrived, arrived ? "" : "5 秒内没进到算子");

                int stopped = engine.StopAll();
                GatePlugin.Proceed.Set();
                try { task.Wait(TimeSpan.FromSeconds(5)); } catch { /* 收尾异常由别的断言负责 */ }

                Check("★S6-① StopAll 返回真实停止条数（1 条）",
                    stopped == 1, $"stopped={stopped}（登记了 1 个运行中的会话）");
                Check("★S6-② StopSession 在取消前写 Info（流程名 + 取消前状态）",
                    log.Infos.Any(m => m.Contains("[停止]", StringComparison.Ordinal)
                        && m.Contains(ExecHarness.FlowName, StringComparison.Ordinal)
                        && m.Contains("Running", StringComparison.Ordinal)),
                    log.Infos.Count == 0 ? "(无 Info 日志)" : string.Join(" | ", log.Infos));
                Check("★S6-③ StopAll 写 Info（条数 + 流程名，文案不暗示只停当前流程）",
                    log.Infos.Any(m => m.Contains("急停", StringComparison.Ordinal)
                        && m.Contains("1 条", StringComparison.Ordinal)
                        && m.Contains(ExecHarness.FlowName, StringComparison.Ordinal)),
                    log.Infos.Count == 0 ? "(无 Info 日志)" : string.Join(" | ", log.Infos));

                // 跑完之后再停一次：0 条也必须留痕（"点了没反应"时日志要能回答"当时确实没有在跑的"）
                int none = engine.StopAll();
                Check("S6-④ 没有流程在跑时 StopAll 返回 0", none == 0, $"stopped={none}");
                Check("S6-⑤ 0 条也留一行 Info（不是静默空跑）",
                    log.Infos.Any(m => m.Contains("当前没有运行中的流程", StringComparison.Ordinal)),
                    log.Infos.Count == 0 ? "(无 Info 日志)" : string.Join(" | ", log.Infos.TakeLast(2)));
            }

            // ---- ② 界面层：Stop 的 CanExecute 必须补"是否真有流程在跑" ----
            var shellFile = ResolveRepoFile(@"VisionMaster\ShellViewModel.cs");
            string shell = shellFile != null ? StripComments(File.ReadAllText(shellFile)) : "";
            string canExec = MethodBody(shell, "private bool CanExecuteExecution");
            Check("★S6-⑥ 停止按钮判据含 AnyFlowRunning（单流程 / HTTP 运行时可点）",
                canExec.Contains("case ExecutionAction.Stop", StringComparison.Ordinal)
                && canExec.Contains("AnyFlowRunning()", StringComparison.Ordinal),
                canExec.Length == 0 ? "CanExecuteExecution 没找到" : "只认 MainRunState 会让停止按钮灰着");

            // ---- ③ 界面层：提示按返回条数，不写死"所有运行中的流程" ----
            string stopAllBody = MethodBody(shell, "private void StopAllRunning");
            Check("★S6-⑦ 停止提示按 StopAll 返回条数（0 条走 Info，不再写死成功文案）",
                stopAllBody.Contains("_flowEngineService.StopAll()", StringComparison.Ordinal)
                && stopAllBody.Contains("stopped", StringComparison.Ordinal)
                && stopAllBody.Contains("Notifier.ShowInfo", StringComparison.Ordinal),
                stopAllBody.Length == 0 ? "StopAllRunning 没找到" : stopAllBody);

            // ---- ④ 契约纪律：Core\IFlowEngine 的 StopAll 仍是 void（没被顺手改成 int）----
            var contractFile = ResolveRepoFile(@"Core\IFlowEngine.cs");
            var engineFile = ResolveRepoFile(@"Engine\FlowEngineService.cs");
            string contract = contractFile != null ? StripComments(File.ReadAllText(contractFile)) : "";
            string engineSrc = engineFile != null ? StripComments(File.ReadAllText(engineFile)) : "";
            Check("★S6-⑧ 公共契约未改：IFlowEngine.StopAll 仍是 void，实现用显式接口保住契约",
                contract.Contains("void StopAll();", StringComparison.Ordinal)
                && engineSrc.Contains("void IFlowEngine.StopAll()", StringComparison.Ordinal)
                && engineSrc.Contains("public int StopAll()", StringComparison.Ordinal),
                contract.Length == 0 || engineSrc.Length == 0 ? "源码定位失败"
                    : "契约应保持 void，条数只在具体类型上暴露（Shell 已注入 FlowEngineService）");
        }

        // ==================================================================
        //  [P1A-N3] 启动方案加载失败：日志 + 非模态提示，不许静默 return
        // ==================================================================
        /// <summary>
        /// 2026-10-22 N3。旧实现两个 `return;` 都是静默的：文件不存在、解析失败，
        /// 用户看到的只是"方案没加载"，既不知道为什么、也不知道去哪儿改。
        /// 判据落在**分支归属**上（源码里的先后顺序就是分支结构），而不是"文件里出现过 Warn"：
        ///  · File.Exists 之后、LoadAsync 之前必须有 Warn + ShowWarning（缺文件分支）；
        ///  · LoadAsync 之后必须有 Error + ShowError，且文案带 loadResult.Message（解析失败分支）；
        ///  · 全程不许写 StartupSolutionPath（不自动清配置：盘没挂上/文件暂时移走都可恢复）。
        /// 另有一条反向前提：`log?.Warn(` / `log?.Error(` 必须在，否则"有提示"只是右下角弹一下，
        /// 事后查日志什么都看不到（启动期弹泡 3 秒就消，R78 的同一教训）。
        /// </summary>
        private static void StartupSolutionFailureIsObservable()
        {
            Section("[P1A-N3] 启动方案加载失败：日志 + 非模态提示");

            var shellFile = ResolveRepoFile(@"VisionMaster\ShellViewModel.cs");
            string shell = shellFile != null ? StripComments(File.ReadAllText(shellFile)) : "";
            string body = MethodBody(shell, "public async Task AutoLoadStartupSolutionAsync");
            Check("N3 判据前提：AutoLoadStartupSolutionAsync 方法体可取",
                body.Length > 0, $"体长={body.Length}");
            if (body.Length == 0) return;

            int existsAt = body.IndexOf("File.Exists(startupPath)", StringComparison.Ordinal);
            int warnAt = body.IndexOf("ShowWarning", StringComparison.Ordinal);
            int loadAt = body.IndexOf("LoadAsync(startupPath)", StringComparison.Ordinal);
            int errAt = body.IndexOf("ShowError", StringComparison.Ordinal);

            Check("★N3-① 文件不存在分支：判存在 → Warn 日志 + 非模态提示 → 才轮到 LoadAsync",
                existsAt >= 0 && warnAt > existsAt && loadAt > warnAt,
                $"exists@{existsAt} warn@{warnAt} load@{loadAt}（顺序即分支归属）");

            Check("★N3-② 解析失败分支：LoadAsync 失败后 Error 日志 + 提示（文案含 loadResult.Message）",
                loadAt > 0 && errAt > loadAt && body.Contains("loadResult.Message", StringComparison.Ordinal),
                $"load@{loadAt} err@{errAt}");

            Check("★N3-③ 两个分支都落 ILogService（Warn / Error 各一处，事后可查）",
                body.Contains("log?.Warn(", StringComparison.Ordinal)
                && body.Contains("log?.Error(", StringComparison.Ordinal),
                "只弹泡不写日志 = 启动期弹泡 3 秒就消，事后无从排查");

            Check("★N3-④ 不自动清 StartupSolutionPath（配置保留：盘没挂上 / 文件暂时移走都可恢复）",
                !body.Contains("StartupSolutionPath =", StringComparison.Ordinal),
                "替用户删配置 = 下次插上盘也不会再自动加载");
        }
    }
}
