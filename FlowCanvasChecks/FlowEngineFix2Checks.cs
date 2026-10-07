using System;
using System.IO;
using System.Threading;
using Core.Interfaces;
using VisionMaster;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// [E18] 引擎全面审查「第二批」的断言：会话准备收口（FlowSessionFactory）+ 三件语义修复 + 锁定门禁。
    ///  · 条件变量未绑定 → 编译期硬错误（不再静默取默认值走错分支）；
    ///  · 新鲜度判据 = 流程身份(FlowID) + 版本（同名不同源不许复用编译产物）；
    ///  · 锁定门禁收口到引擎（任何触发源都拦）——真跑一次断言"没跑"；
    ///  · 数组下标写进 TargetPortName / 切方案清理会话（界面层用源码纪律断言兜住）。
    /// </summary>
    internal static class FlowEngineFix2Checks
    {
        internal static void Run()
        {
            UnlinkedConditionVariableIsCompileError();
            StalenessUsesFlowIdentity();
            LockedFlowIsRefusedAtEngine();
            SourceDiscipline();
        }

        private static string Aqn<T>() => typeof(T).AssemblyQualifiedName!;

        private static (WorkspaceContext workspace, SolutionModel solution) NewWorkspace(FlowModel flow)
        {
            var workspace = new WorkspaceContext();
            var solution = new SolutionModel();
            solution.Flows.Clear();
            solution.Flows.Add(flow);
            workspace.SwitchSolution(solution);
            return (workspace, solution);
        }

        // ==================================================================
        //  [E18] ① 条件变量未绑定 → 编译错误（不是静默取默认值）
        // ==================================================================
        private static void UnlinkedConditionVariableIsCompileError()
        {
            Section("[E18] 条件变量未绑定：编译期拦截");

            var workspace = new WorkspaceContext();
            var compiler = new FlowCompiler(workspace);

            var cond = new ConditionStep("C", "条件判断", "BuiltIn_If", "未绑定条件的If");
            cond.Children[0].Expression = "Score > 80";
            cond.LocalVariables.Add(new LocalVariableItem { Name = "Score", DataTypeName = "System.Double" });

            var failed = compiler.Compile(new StepModel[] { cond }, "未绑定条件");
            Check("★声明了条件变量却没绑数据源 → 编译失败（此前静默取 0 → 恒走 Else）",
                !failed.Success && failed.Errors.Any(e => e.Message.Contains("条件变量未绑定")),
                failed.Success ? "编译竟然通过了" : string.Join("；", failed.Errors.Select(e => e.Message)));

            // 绑上数据源（键 = 变量 Id 字符串，条件编辑器写回的口径）→ 编译通过
            var cond2 = new ConditionStep("C", "条件判断", "BuiltIn_If", "已绑定条件的If");
            cond2.Children[0].Expression = "Score > 80";
            var score = new LocalVariableItem { Name = "Score", DataTypeName = "System.Double" };
            cond2.LocalVariables.Add(score);
            cond2.SetLink(score.Id.ToString(), new LinkReference(LinkKind.Constant, Guid.Empty, "0", "常量:0"));

            var ok = compiler.Compile(new StepModel[] { cond2 }, "已绑定条件");
            Check("按变量 Id 绑定后正常编译（合法路径未被误伤）", ok.Success,
                ok.Success ? "" : string.Join("；", ok.Errors.Select(e => e.Message)));
        }

        // ==================================================================
        //  [E18] ② 新鲜度判据：流程身份（FlowID）+ 版本
        // ==================================================================
        private static void StalenessUsesFlowIdentity()
        {
            Section("[E18] 会话新鲜度：流程身份 + 版本");

            var flowA = new FlowModel { FlowName = "同名流程", Version = 3 };
            var flowB = new FlowModel { FlowName = "同名流程", Version = 3 };   // 另一份方案里的同名流程

            var session = new FlowSession { FlowName = "同名流程", CompiledVersion = 3, CompiledFlowId = flowA.FlowID };
            Check("同身份 + 同版本 → 新鲜（复用会话）", !FlowSessionFactory.IsStale(session, flowA), "");
            Check("★同名但**换了一份方案**（FlowID 不同）→ 判定过期（不许复用别的方案的编译产物）",
                FlowSessionFactory.IsStale(session, flowB), $"A={flowA.FlowID} B={flowB.FlowID}");

            var bumped = new FlowModel { FlowName = "同名流程", Version = 4 };
            bumped.FlowID = flowA.FlowID;
            Check("同身份 + 版本更高 → 过期（图纸改过要重编译）",
                FlowSessionFactory.IsStale(session, bumped), "");

            var legacy = new FlowSession { FlowName = "同名流程", CompiledVersion = 3, CompiledFlowId = "" };
            Check("老会话未填身份（空串）→ 退化为只比版本（兼容既有行为）",
                !FlowSessionFactory.IsStale(legacy, flowB), "");
        }

        // ==================================================================
        //  [E18] ③ 锁定门禁：引擎层拦所有触发源（真跑一次，断言"没跑"）
        // ==================================================================
        private static void LockedFlowIsRefusedAtEngine()
        {
            Section("[E18] 锁定（禁止运行）：引擎层统一门禁");

            CountA.Reset();
            var flow = new FlowModel
            {
                FlowName = "锁定流程",
                IsEnabled = true,
                StepsEncrypted = true,   // 锁定
            };
            flow.Steps.Add(new ActionStep("T", "计数A", Aqn<CountA>(), "锁定步骤"));

            var (workspace, _) = NewWorkspace(flow);
            var log = new StubLog();
            var runtime = new RuntimeManager();
            var engine = new FlowEngineService(runtime, log, workspace, null, new ResourceLockService(), NullCameraProvider.Instance);

            var session = new FlowSession
            {
                FlowName = flow.FlowName,
                ExecutionEngine = new FlowCompiler(workspace).Compile(flow.Steps, flow.FlowName).Data,
                CompiledVersion = flow.Version,
                CompiledFlowId = flow.FlowID,
            };
            session.AddBlueprintsDeep(flow.Steps);

            var started = engine.TryRunSessionOnceAsync(session).GetAwaiter().GetResult();
            Check("★锁定流程：单次执行被引擎拒绝（返回 false）", !started, $"started={started}");
            Check("被拒的锁定流程一个节点都没跑（计数 0）", CountA.Runs == 0, $"Runs={CountA.Runs}");
            Check("拒绝留有 Warn（现场能查到为什么没跑）",
                log.Warns.Any(w => w.Contains("已被锁定")), string.Join(" | ", log.Warns));

            // 解锁后同一会话可以正常跑（门禁不是把流程锁死）
            flow.StepsEncrypted = false;
            var startedAfterUnlock = engine.TryRunSessionOnceAsync(session).GetAwaiter().GetResult();
            Check("解锁后同一会话可正常运行", startedAfterUnlock && CountA.Runs == 1,
                $"started={startedAfterUnlock} Runs={CountA.Runs}");
        }

        // ==================================================================
        //  [E18] ④ 界面层两处纪律（源码断言）：数组下标 / 切方案清会话
        // ==================================================================
        private static void SourceDiscipline()
        {
            Section("[E18] 界面层纪律：数组下标与切方案");

            var vmFile = ResolveRepoFile(@"VisionMaster\ViewModels\DialogViewModels\VariableBindingViewModel.cs");
            var vmText = vmFile != null ? File.ReadAllText(vmFile) : string.Empty;
            Check("★数组元素绑定把下标写进 TargetPortName（编译器只从那里解析 ArrayIndexProxyPort）",
                vmText.Contains("index >= 0 ? $\"{port.Name}[{index}]\" : port.Name"),
                vmFile == null ? "定位不到 VariableBindingViewModel.cs" : "");

            var shellFile = ResolveRepoFile(@"VisionMaster\ShellViewModel.cs");
            var shellText = shellFile != null ? File.ReadAllText(shellFile) : string.Empty;
            Check("★切换方案后清理旧会话（不再让上一份方案的会话按名串车）",
                shellText.Contains("Workspace.SwitchSolution(loadResult.Data);")
                && shellText.Contains("_runtimeManager.ClearAll();"),
                shellFile == null ? "定位不到 ShellViewModel.cs" : "");

            var httpFile = ResolveRepoFile(@"VisionMaster\Services\HttpImageServer.cs");
            var httpText = httpFile != null ? File.ReadAllText(httpFile) : string.Empty;
            Check("★HTTP 的会话准备已收口到单点（不再保留第二份 TryBuildSession 实现）",
                httpText.Contains("FlowSessionFactory.TryEnsureSession(") && !httpText.Contains("private bool TryBuildSession("),
                httpFile == null ? "定位不到 HttpImageServer.cs" : "");
        }

        /// <summary>从输出目录往上找仓库根，再拼相对路径（定位不到返回 null）</summary>
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
    }
}
