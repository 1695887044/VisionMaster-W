using System;
using System.Linq;
using System.Threading;
using Core.Interfaces;
using VisionMaster;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// [E19] 逻辑分支容器四项修复的断言：
    ///  · For 迭代上限（超限拒绝执行 + 报错，不再变成不可中断的长循环）；
    ///  · 容器状态上浮（子步骤失败 → 容器如实标 Failed，不改控制流）；
    ///  · Break/Continue/Return 上报运行状态（画布要亮、要有耗时）；
    ///  · 循环体分支数校验（多余分支报错，不再静默丢弃）。
    /// </summary>
    internal static class FlowContainerChecks
    {
        internal static void Run()
        {
            ForIterationCap();
            ContainerFailureEscalates();
            BreakReportsState();
            LoopBodyBranchCountValidated();
        }

        private static string Aqn<T>() => typeof(T).AssemblyQualifiedName!;

        private static (WorkspaceContext workspace, FlowEngineService engine, RuntimeManager runtime, StubLog log) NewEngine(FlowModel flow)
        {
            var workspace = new WorkspaceContext();
            var solution = new SolutionModel();
            solution.Flows.Clear();
            solution.Flows.Add(flow);
            workspace.SwitchSolution(solution);

            var log = new StubLog();
            var runtime = new RuntimeManager();
            var engine = new FlowEngineService(runtime, log, workspace, null, new ResourceLockService(), NullCameraProvider.Instance);
            return (workspace, engine, runtime, log);
        }

        private static bool WaitFor(Func<bool> cond, int timeoutMs = 5000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (cond()) return true;
                Thread.Sleep(10);
            }
            return cond();
        }

        // ==================================================================
        //  [E19] ① For 迭代上限
        // ==================================================================
        private static void ForIterationCap()
        {
            Section("[E19] For 迭代上限（对齐 While 的 9999）");

            CountA.Reset();
            var forStep = new ForStep("F", "计次循环", "BuiltIn_For", "超限循环");
            forStep.DefaultLoopCount = CompiledForNodeMaxIterationsProbe + 1;   // 上限 +1
            forStep.Children[0].Steps.Add(new ActionStep("A", "计数A", Aqn<CountA>(), "循环体步骤"));

            var flow = new FlowModel { FlowName = "超限循环流程" };
            flow.Steps.Add(forStep);

            var (workspace, engine, _, log) = NewEngine(flow);
            var compiled = new FlowCompiler(workspace).Compile(flow.Steps, flow.FlowName);
            Check("超限图纸编译通过（上限是运行期闸门，不是编译期）", compiled.Success,
                compiled.Success ? "" : string.Join("；", compiled.Errors.Select(e => e.Message)));
            if (!compiled.Success) return;

            var session = new FlowSession { FlowName = flow.FlowName, ExecutionEngine = compiled.Data, CompiledVersion = flow.Version };
            session.AddBlueprintsDeep(flow.Steps);
            engine.TryRunSessionOnceAsync(session).GetAwaiter().GetResult();

            Check("★超限：循环体一圈都没跑（拒绝执行，不夹取）", CountA.Runs == 0, $"Runs={CountA.Runs}");
            Check("★超限：循环容器标 Failed（有据可查，不是静默跳过）",
                forStep.State == StepState.Failed, $"State={forStep.State}");
            Check("超限留有 Error 日志（含实际次数与上限）",
                log.Errors.Any(e => e.Contains("超过上限")), string.Join(" | ", log.Errors.TakeLast(2)));
        }

        /// <summary>与 CompiledForNode.MaxIterations 对应（编译期常量，用反射取，避免复制数字）</summary>
        private static int CompiledForNodeMaxIterationsProbe
            => (int)typeof(CompiledForNode).GetField("MaxIterations")!.GetRawConstantValue()!;

        // ==================================================================
        //  [E19] ② 容器状态上浮（子步骤失败 → 容器 Failed；不改控制流）
        // ==================================================================
        private static void ContainerFailureEscalates()
        {
            Section("[E19] 容器状态上浮：子步骤失败 → 容器 Failed");

            // 用「调用流程」插件造一个**运行期必失败**的步骤（流程名留空 → 插件自报失败）
            Type pluginType = null;
            try
            {
                var modulesDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\Modules"));
                pluginType = System.Reflection.Assembly.LoadFrom(
                    System.IO.Path.Combine(modulesDir, "Plugin.RunFlow.dll"))
                    .GetType("VisionMaster.Plugins.RunFlow.RunFlowPlugin", throwOnError: false);
            }
            catch { /* 下面按 null 处理 */ }

            if (pluginType == null)
            {
                Check("（前提）Plugin.RunFlow.dll 可装载", false, "定位失败，跳过本组");
                return;
            }

            var ifStep = new ConditionStep("C", "条件判断", "BuiltIn_If", "含失败子步骤的If");
            ifStep.Children[0].Expression = "true";
            var failing = new ActionStep("R", "调用流程", pluginType.AssemblyQualifiedName, "必失败的子步骤");
            // 必填端口要配齐（否则编译期就报 [参数缺失]）；流程名故意指向不存在的流程 →
            // 插件在运行期自报失败（Success=false → 框架标该步 Failed），正是本组要的"失败现场"
            failing.SetInputValue("FlowName", "本流程不存在的目标XYZ");
            failing.SetInputValue("TimeoutMs", 1000);
            ifStep.Children[0].Steps.Add(failing);
            ifStep.Children[1].Steps.Add(new ActionStep("A", "计数A", Aqn<CountA>(), "Else里的步骤"));

            var flow = new FlowModel { FlowName = "容器失败上浮流程" };
            flow.Steps.Add(ifStep);

            CountA.Reset();
            var (workspace, engine, _, _) = NewEngine(flow);
            var compiled = new FlowCompiler(workspace).Compile(flow.Steps, flow.FlowName);
            Check("容器上浮图纸编译通过", compiled.Success,
                compiled.Success ? "" : string.Join("；", compiled.Errors.Select(e => e.Message)));
            if (!compiled.Success) return;

            var session = new FlowSession { FlowName = flow.FlowName, ExecutionEngine = compiled.Data, CompiledVersion = flow.Version };
            session.AddBlueprintsDeep(flow.Steps);
            engine.TryRunSessionOnceAsync(session).GetAwaiter().GetResult();

            var child = ifStep.Children[0].Steps[0];
            Check("子步骤确实失败（构造出失败现场）", child.State == StepState.Failed, $"State={child.State}");
            Check("★容器如实上浮为 Failed（子步骤绿了才是问题）",
                ifStep.State == StepState.Failed, $"If.State={ifStep.State}");
            Check("控制流未被改变（走的是 If 分支，Else 里的步骤没跑）", CountA.Runs == 0, $"Runs={CountA.Runs}");
        }

        // ==================================================================
        //  [E19] ③ Break/Continue 上报运行状态
        // ==================================================================
        private static void BreakReportsState()
        {
            Section("[E19] Break/Continue 上报状态");

            CountA.Reset();
            var forStep = new ForStep("F", "计次循环", "BuiltIn_For", "含Break的循环");
            forStep.DefaultLoopCount = 3;
            var breakStep = new BreakStep("B", "跳出", "BuiltIn_Break", "跳出循环");
            forStep.Children[0].Steps.Add(new ActionStep("A", "计数A", Aqn<CountA>(), "Break前一步"));
            forStep.Children[0].Steps.Add(breakStep);

            var flow = new FlowModel { FlowName = "Break状态流程" };
            flow.Steps.Add(forStep);

            var (workspace, engine, _, _) = NewEngine(flow);
            var compiled = new FlowCompiler(workspace).Compile(flow.Steps, flow.FlowName);
            Check("Break 图纸编译通过", compiled.Success,
                compiled.Success ? "" : string.Join("；", compiled.Errors.Select(e => e.Message)));
            if (!compiled.Success) return;

            var session = new FlowSession { FlowName = flow.FlowName, ExecutionEngine = compiled.Data, CompiledVersion = flow.Version };
            session.AddBlueprintsDeep(flow.Steps);
            engine.TryRunSessionOnceAsync(session).GetAwaiter().GetResult();

            Check("Break 生效：循环体只跑了 1 圈（第 1 圈就跳出）", CountA.Runs == 1, $"Runs={CountA.Runs}");
            Check("★Break 步骤上报了状态与耗时（此前永远 Idle、画布不亮）",
                breakStep.State == StepState.Success, $"State={breakStep.State}");
        }

        // ==================================================================
        //  [E19] ④ 循环体分支数校验（多余分支报错，不再静默丢弃）
        // ==================================================================
        private static void LoopBodyBranchCountValidated()
        {
            Section("[E19] 循环体分支数校验");

            var forStep = new ForStep("F", "计次循环", "BuiltIn_For", "多分支循环");
            forStep.DefaultLoopCount = 1;
            // 手改数据才会出现的场景：再塞一个分支（构造器只会建一个）
            forStep.Children.Add(new StepCollection { BranchType = BranchType.Default, StepName = "多余分支" });
            forStep.Children[1].Steps.Add(new ActionStep("A", "计数A", Aqn<CountA>(), "多余分支里的步骤"));

            var flow = new FlowModel { FlowName = "多分支流程" };
            flow.Steps.Add(forStep);

            var workspace = new WorkspaceContext();
            var compiled = new FlowCompiler(workspace).Compile(flow.Steps, flow.FlowName);

            Check("★循环体多于一个分支 → 编译报错（不再静默丢弃多余分支）",
                !compiled.Success && compiled.Errors.Any(e => e.Message.Contains("容器分支数错误")),
                compiled.Success ? "编译竟然通过了" : string.Join("；", compiled.Errors.Select(e => e.Message)));
        }
    }
}
