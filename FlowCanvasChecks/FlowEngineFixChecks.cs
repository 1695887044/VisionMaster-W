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
    }
}
