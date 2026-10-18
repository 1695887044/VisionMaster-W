using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Interfaces;
using Newtonsoft.Json;
using VisionMaster;
using VisionMaster.Models;
using VisionMaster.Services;
// 直接复用断言宿主的 Section/Check 计数口径，不再另造一套
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 等待节点（BuiltIn_Wait / WaitStep / CompiledWaitNode）的执行层断言。
    ///
    /// 分工：`WaitNodeChecks` 之前的执行层断言（ExecutionChecks E1~E12）验的是
    /// "容器与序列执行器"；本段只验等待节点自己的四条语义，且每条都必须能反证：
    ///   ① 编译期硬错（空条件 / 轮询过小 / 超时为负）——配置错误不许编译成"能跑的图"；
    ///   ② 条件已成立时**不额外睡一轮**——先求值后等待的顺序，写反了这条必红；
    ///   ③ 轮询期间条件转真 / 停止请求 / 超时为 0 三种退出路径——超时判定用墙钟，停止用令牌；
    ///   ④ 超时后的两种处置（终止 / 继续）与两个输出端口（TimedOut / ElapsedMs），
    ///      含"下游 If 靠 TimedOut 走异常分支"的端到端路径（同时验生产方端口解析 + 依赖表）。
    ///
    /// 全局变量连线（LinkKind.GlobalVariable）是条件变量的数据源：它在编译期被解析成
    /// 变量对象本身（IVariable : IOutputPort），运行期每次求值都读 `Value`，
    /// 所以后台任务改 `flag.Value = true` 能被轮询看到——"连线被丢"的后果是条件恒 false、
    /// 一直等到超时，③ 的两条用例因此是连线是否真的生效的反证。
    /// </summary>
    internal static class WaitNodeChecks
    {
        private const string BoolType = "System.Boolean";

        /// <summary>计数桩的插件类型名（与 ExecutionChecks 同一口径：AssemblyQualifiedName 才能被反射实例化）</summary>
        private static string CountingType => typeof(CountingPlugin).AssemblyQualifiedName!;

        /// <summary>造一个等待节点；linkGlobalFlag=true 时把条件变量 Flag 连到全局 WaitFlag</summary>
        private static WaitStep NewWait(
            string expression,
            int poll = 20,
            int timeout = 2000,
            WaitTimeoutAction action = WaitTimeoutAction.TerminateFlow,
            string? alarm = null,
            bool linkGlobalFlag = false)
        {
            var wait = new WaitStep("\uE700", "等待", "BuiltIn_Wait", "等待_0")
            {
                ConditionExpression = expression,
                PollIntervalMs = poll,
                TimeoutMs = timeout,
                TimeoutAction = action,
                AlarmVariableName = alarm ?? string.Empty,
            };

            if (linkGlobalFlag)
            {
                var varId = Guid.NewGuid();
                wait.LocalVariables.Add(
                    new LocalVariableItem { Id = varId, Name = "Flag", DataTypeName = BoolType });
                wait.LinkedSources[varId.ToString()] =
                    new LinkReference(LinkKind.GlobalVariable, Guid.Empty, "WaitFlag", "Global.WaitFlag");
            }

            return wait;
        }

        /// <summary>全局 bool 变量（种子在编译之前种进工作区，见 ExecHarness.Prepare 的 seed 参数）</summary>
        private static LocalVariableModel NewGlobalBool(string name, bool value = false)
            => new() { Name = name, DataTypeString = BoolType, Value = value };

        private static CompiledWaitNode NodeOf(ExecRun run, WaitStep wait)
            => (CompiledWaitNode)run.Engine!.NodeLookup[wait.StepID];

        public static void Run()
        {
            Section("[E24] 等待节点（BuiltIn_Wait）：编译门禁 / 轮询 / 超时处置 / 输出端口");

            CompileGuards();
            AlreadyTrueDoesNotSleep();
            PollUntilTrue();
            ConditionEvaluationFailure();
            TimeoutTerminates();
            TimeoutContinues();
            TimeoutShorterThanPollInterval();
            TimedOutDrivesIfBranch();
            ElapsedMsProducerLink();
            WaitInsideParallelBranchResolvesLinks();
            StopRequestReturnsImmediately();
            PortsResetBetweenRuns();
            ZeroTimeoutNeverTimesOut();
            AlarmVariable();
            VmsRoundTrip();
        }

        // ==================================================================
        //  [E24-1] 编译期硬错：三条独立用例，各自只违反一条规则
        // ==================================================================
        private static void CompileGuards()
        {
            var runEmpty = ExecHarness.Prepare(new StepModel[] { NewWait("", poll: 20, timeout: 100) });
            Check("等待节点：条件为空 → 编译失败（[编译错误] 等待条件为空）",
                !runEmpty.Compiled && runEmpty.Errors.Contains("等待条件为空"),
                runEmpty.Errors);

            var runPoll = ExecHarness.Prepare(new StepModel[] { NewWait("false", poll: 5, timeout: 100) });
            Check("等待节点：轮询间隔 5ms → 编译失败（… 轮询间隔 … 过小）",
                !runPoll.Compiled && runPoll.Errors.Contains("轮询间隔") && runPoll.Errors.Contains("过小"),
                runPoll.Errors);

            var runTimeout = ExecHarness.Prepare(new StepModel[] { NewWait("false", poll: 20, timeout: -1) });
            Check("等待节点：超时 -1ms → 编译失败（… 超时时间不能为负）",
                !runTimeout.Compiled && runTimeout.Errors.Contains("超时时间不能为负"),
                runTimeout.Errors);
        }

        // ==================================================================
        //  [E24-2] 条件已成立：先求值后等待（写反了就会先睡满 poll=200ms）
        // ==================================================================
        private static void AlreadyTrueDoesNotSleep()
        {
            var wait = NewWait("1 == 1", poll: 200, timeout: 5000);
            var run = ExecHarness.Prepare(new StepModel[] { wait });
            Check("等待节点：条件已成立用例编译通过", run.Compiled, run.Errors);
            if (!run.Compiled) return;

            var node = NodeOf(run, wait);
            var sw = Stopwatch.StartNew();
            run.Engine!.Run(run.NewContext(new StubLog()));
            sw.Stop();

            Check("等待节点：条件已成立 → 立即继续，不额外等一轮（总耗时 < 150ms，poll=200ms）",
                wait.State == StepState.Success && sw.ElapsedMilliseconds < 150,
                $"State={wait.State} 总耗时={sw.ElapsedMilliseconds}ms");
            Check("等待节点：条件成立 → TimedOut=false（不是超时路径）",
                !node.TimedOutPort.TypedValue, $"TimedOut={node.TimedOutPort.TypedValue}");
        }

        // ==================================================================
        //  [E24-3] 轮询期间条件转真：全局变量连线必须真的生效
        //          （连线被丢 → bool 恒 false → 一直等到 timeout=2000 → 抛 TimeoutException）
        // ==================================================================
        private static void PollUntilTrue()
        {
            var flag = NewGlobalBool("WaitFlag");
            var wait = NewWait("Flag", poll: 20, timeout: 2000, linkGlobalFlag: true);
            var run = ExecHarness.Prepare(new StepModel[] { wait }, ws => ws.GlobalVariables.Add(flag));
            Check("等待节点：全局变量连线用例编译通过", run.Compiled, run.Errors);
            if (!run.Compiled) return;

            var node = NodeOf(run, wait);
            var sw = Stopwatch.StartNew();
            Exception? boom = null;
            try
            {
                // 150ms 后置真：若连线没生效（条件恒 false），这里会等到 2000ms 超时并抛异常
                Task.Run(async () =>
                {
                    await Task.Delay(150);
                    flag.Value = true;
                });
                run.Engine!.Run(run.NewContext(new StubLog()));
            }
            catch (Exception ex)
            {
                boom = ex;
            }
            sw.Stop();

            Check("等待节点：条件 150ms 后成立 → 继续（不抛；全局变量连线生效）",
                boom == null && wait.State == StepState.Success,
                boom == null ? $"State={wait.State}" : $"抛了 {boom.GetType().Name}: {boom.Message}");
            Check("等待节点：轮询命中耗时落在 120~1200ms（证明是轮询到真，不是睡到超时）",
                node.ElapsedMsPort.TypedValue >= 120 && node.ElapsedMsPort.TypedValue <= 1200,
                $"ElapsedMs={node.ElapsedMsPort.TypedValue} 总耗时={sw.ElapsedMilliseconds}ms");
        }

        // ==================================================================
        //  [E24-3b] 条件求值异常：必须停机（不许静默当成 false 一直等到超时）
        // ==================================================================
        private static void ConditionEvaluationFailure()
        {
            var wait = new WaitStep("\uE700", "等待", "BuiltIn_Wait", "等待_0")
            {
                ConditionExpression = "X > 0",
                PollIntervalMs = 20,
                TimeoutMs = 1000,
            };
            // 声明成 int，运行期塞非数字字符串 → 类型归一会抛（与 If 的 P0-1 同一病灶）
            wait.RuntimeVariableRefs.Add(
                new LocalVariableItem { Name = "X", DataTypeName = "System.Int32" });

            var run = ExecHarness.Prepare(new StepModel[] { wait });
            Check("等待节点：求值异常用例编译通过（类型校验不拦运行期脏值）", run.Compiled, run.Errors);
            if (!run.Compiled) return;

            var log = new StubLog();
            var ctx = run.NewContext(log);
            ctx.LocalVariables["X"] = "abc";

            Exception? boom = null;
            try { run.Engine!.Run(ctx); }
            catch (Exception ex) { boom = ex; }

            Check("等待节点：条件求值异常 → 上抛（不静默当成 false 等到超时）",
                boom != null, boom == null ? "没抛：求值异常被吞掉了" : boom.GetType().Name);
            Check("等待节点：条件求值异常 → 步骤落 Failed",
                wait.State == StepState.Failed, $"State={wait.State}");
            Check("等待节点：日志含“条件求值失败”",
                log.Errors.Any(e => e.Contains("条件求值失败")),
                string.Join(" | ", log.Errors));
        }

        // ==================================================================
        //  [E24-4] 超时 + TerminateFlow：抛 TimeoutException，状态 Failed，端口与日志齐备
        // ==================================================================
        private static void TimeoutTerminates()
        {
            var wait = NewWait("false", poll: 20, timeout: 150);
            var run = ExecHarness.Prepare(new StepModel[] { wait });
            Check("等待节点：超时终止用例编译通过", run.Compiled, run.Errors);
            if (!run.Compiled) return;

            var node = NodeOf(run, wait);
            var log = new StubLog();
            Exception? boom = null;
            try { run.Engine!.Run(run.NewContext(log)); }
            catch (Exception ex) { boom = ex; }

            Check("等待节点：超时 + TerminateFlow → 抛 TimeoutException",
                boom is TimeoutException,
                boom == null ? "没抛（超时被静默吞掉）" : boom.GetType().Name);
            Check("等待节点：超时后步骤落 Failed（不是绿着继续）",
                wait.State == StepState.Failed, $"State={wait.State}");
            Check("等待节点：超时日志含 [等待超时] 与条件表达式文本",
                log.Errors.Any(e => e.Contains("[等待超时]") && e.Contains("[false]")),
                string.Join(" | ", log.Errors));
            Check("等待节点：超时后 TimedOut=true / ElapsedMs>=140",
                node.TimedOutPort.TypedValue && node.ElapsedMsPort.TypedValue >= 140,
                $"TimedOut={node.TimedOutPort.TypedValue} ElapsedMs={node.ElapsedMsPort.TypedValue}");
        }

        // ==================================================================
        //  [E24-5] 超时 + Continue：不抛、状态仍 Failed、后续步骤照常执行
        // ==================================================================
        private static void TimeoutContinues()
        {
            CountingPlugin.Reset();
            var wait = NewWait("false", poll: 20, timeout: 150, action: WaitTimeoutAction.Continue);
            var after = new ActionStep("A", "计数", CountingType, "超时后计数");
            var run = ExecHarness.Prepare(new StepModel[] { wait, after });
            Check("等待节点：超时继续用例编译通过", run.Compiled, run.Errors);
            if (!run.Compiled) return;

            var node = NodeOf(run, wait);
            Exception? boom = null;
            try { run.Engine!.Run(run.NewContext(new StubLog())); }
            catch (Exception ex) { boom = ex; }

            Check("等待节点：超时 + Continue → 不抛（流程继续）",
                boom == null, boom == null ? "" : boom.GetType().Name + ": " + boom.Message);
            Check("等待节点：Continue 下步骤仍落 Failed（超时是事实）",
                wait.State == StepState.Failed, $"State={wait.State}");
            Check("等待节点：Continue 下 TimedOut=true",
                node.TimedOutPort.TypedValue, $"TimedOut={node.TimedOutPort.TypedValue}");
            Check("等待节点：超时后后续算子照常执行（Runs==1）",
                CountingPlugin.Runs == 1, $"Runs={CountingPlugin.Runs}");
        }

        // ==================================================================
        //  [E24-5b] 超时 < 轮询间隔：运行期按剩余时间夹取，不许等满一个轮询周期
        // ==================================================================
        private static void TimeoutShorterThanPollInterval()
        {
            var wait = NewWait("false", poll: 500, timeout: 50, action: WaitTimeoutAction.Continue);
            var run = ExecHarness.Prepare(new StepModel[] { wait });
            Check("等待节点：timeout<poll 用例编译通过", run.Compiled, run.Errors);
            if (!run.Compiled) return;

            var node = NodeOf(run, wait);
            var sw = Stopwatch.StartNew();
            run.Engine!.Run(run.NewContext(new StubLog()));
            sw.Stop();

            Check("等待节点：timeout(50) < poll(500) → 按剩余时间夹取（< 300ms 就判超时）",
                node.TimedOutPort.TypedValue && sw.ElapsedMilliseconds < 300,
                $"TimedOut={node.TimedOutPort.TypedValue} 总耗时={sw.ElapsedMilliseconds}ms");
        }

        // ==================================================================
        //  [E24-6] 端到端异常分支：Wait.TimedOut → If 的条件变量 → 分支内计数
        //          同时验生产方端口解析（LinkPorts 的 CompiledWaitNode 分支）与 AddDependency
        // ==================================================================
        private static void TimedOutDrivesIfBranch()
        {
            (StepModel[] Steps, WaitStep Wait, ConditionStep If) BuildDiagram(string waitExpression)
            {
                var wait = NewWait(
                    waitExpression, poll: 20, timeout: 100, action: WaitTimeoutAction.Continue);

                var ifStep = new ConditionStep("I", "条件", "SomeIfOperator", "超时判断");
                var toId = Guid.NewGuid();
                ifStep.LocalVariables.Add(
                    new LocalVariableItem { Id = toId, Name = "TO", DataTypeName = BoolType });
                ifStep.LinkedSources[toId.ToString()] =
                    new LinkReference(LinkKind.StepPort, wait.StepID, "TimedOut", "等待_0.TimedOut");
                ifStep.Children[0].Expression = "TO";
                ifStep.Children[0].Steps.Add(
                    new ActionStep("A", "计数", CountingType, "异常分支计数"));

                return (new StepModel[] { wait, ifStep }, wait, ifStep);
            }

            // ---- 超时路径：TimedOut=true → If 命中 → 分支内算子跑 1 次 ----
            CountingPlugin.Reset();
            var (stepsTimeout, waitTimeout, ifTimeout) = BuildDiagram("false");
            var runTimeout = ExecHarness.Prepare(stepsTimeout);
            Check("等待节点：TimedOut→If 图纸编译通过（生产方端口解析成功）",
                runTimeout.Compiled, runTimeout.Errors);
            if (runTimeout.Compiled)
            {
                runTimeout.Engine!.Run(runTimeout.NewContext(new StubLog()));
                Check("等待节点：超时 → TimedOut 驱动 If 走异常分支（Runs==1）",
                    CountingPlugin.Runs == 1 && ifTimeout.State == StepState.Success,
                    $"Runs={CountingPlugin.Runs} IfState={ifTimeout.State}");
            }

            // ---- 正常路径：同一张图把等待条件改成恒真重编译 → TimedOut=false → If 不走 ----
            CountingPlugin.Reset();
            var (stepsOk, waitOk, ifOk) = BuildDiagram("1 == 1");
            var runOk = ExecHarness.Prepare(stepsOk);
            Check("等待节点：条件恒真重编译通过", runOk.Compiled, runOk.Errors);
            if (runOk.Compiled)
            {
                runOk.Engine!.Run(runOk.NewContext(new StubLog()));
                Check("等待节点：条件成立 → TimedOut=false，If 不走异常分支（Runs==0）",
                    CountingPlugin.Runs == 0 && waitOk.State == StepState.Success,
                    $"Runs={CountingPlugin.Runs} WaitState={waitOk.State} IfState={ifOk.State}");
            }
        }

        // ==================================================================
        //  [E24-6b] ElapsedMs 生产方连线：下游 If 按耗时走分支
        // ==================================================================
        private static void ElapsedMsProducerLink()
        {
            var wait = NewWait("false", poll: 20, timeout: 100, action: WaitTimeoutAction.Continue);

            var ifStep = new ConditionStep("I", "条件", "SomeIfOperator", "耗时判断");
            var elapsedId = Guid.NewGuid();
            ifStep.LocalVariables.Add(
                new LocalVariableItem { Id = elapsedId, Name = "E", DataTypeName = "System.Int32" });
            ifStep.LinkedSources[elapsedId.ToString()] =
                new LinkReference(LinkKind.StepPort, wait.StepID, "ElapsedMs", "等待_0.ElapsedMs");
            ifStep.Children[0].Expression = "E >= 90";
            ifStep.Children[0].Steps.Add(
                new ActionStep("A", "计数", CountingType, "耗时分支计数"));

            CountingPlugin.Reset();
            var run = ExecHarness.Prepare(new StepModel[] { wait, ifStep });
            Check("等待节点：ElapsedMs→If 图纸编译通过", run.Compiled, run.Errors);
            if (!run.Compiled) return;

            run.Engine!.Run(run.NewContext(new StubLog()));
            Check("等待节点：ElapsedMs 可被下游 If 消费（超时 ~100ms → E>=90 成立 → Runs==1）",
                CountingPlugin.Runs == 1,
                $"Runs={CountingPlugin.Runs} WaitState={wait.State} IfState={ifStep.State}");
        }

        // ==================================================================
        //  [E24-6c] H1 回归：并行分支里的连线必须被递归编译进来
        //          修复前 LinkPorts 不递归 ParallelStep → 等待节点的 UpstreamLinks 为空
        //          → 条件恒取默认 false → 一直等到超时（State=Failed）
        // ==================================================================
        private static void WaitInsideParallelBranchResolvesLinks()
        {
            var flag = NewGlobalBool("WaitFlag");
            var wait = NewWait(
                "Flag", poll: 20, timeout: 1000, action: WaitTimeoutAction.Continue, linkGlobalFlag: true);

            var parallel = new ParallelStep("\uE700", "并行分组", "BuiltIn_Parallel", "并行_0")
            {
                // 全限定名：System.Linq 也有个 ParallelExecutionMode（隐式 using），不限定则 CS0104
                ExecutionMode = VisionMaster.Models.ParallelExecutionMode.Parallel,
            };
            parallel.Children[0].Steps.Add(wait);

            var run = ExecHarness.Prepare(
                new StepModel[] { parallel }, ws => ws.GlobalVariables.Add(flag));
            Check("等待节点：并行分支内图纸编译通过（容器连线递归）", run.Compiled, run.Errors);
            if (!run.Compiled) return;

            var node = NodeOf(run, wait);
            Exception? boom = null;
            try
            {
                Task.Run(async () =>
                {
                    await Task.Delay(100);
                    flag.Value = true;
                });
                run.Engine!.Run(run.NewContext(new StubLog()));
            }
            catch (Exception ex)
            {
                boom = ex;
            }

            Check("等待节点：并行分支内的全局变量连线生效（100ms 后成立 → Success，不是等到超时 Failed）",
                boom == null && wait.State == StepState.Success && !node.TimedOutPort.TypedValue,
                boom == null
                    ? $"State={wait.State} TimedOut={node.TimedOutPort.TypedValue} ElapsedMs={node.ElapsedMsPort.TypedValue}"
                    : $"抛了 {boom.GetType().Name}: {boom.Message}");
        }

        // ==================================================================
        //  [E24-7] 停止请求：立即返回、状态 Skipped、不写 TimedOut（取消不是超时）
        // ==================================================================
        private static void StopRequestReturnsImmediately()
        {
            var wait = NewWait("false", poll: 20, timeout: 5000);
            var run = ExecHarness.Prepare(new StepModel[] { wait });
            Check("等待节点：停止请求用例编译通过", run.Compiled, run.Errors);
            if (!run.Compiled) return;

            var node = NodeOf(run, wait);
            using var cts = new CancellationTokenSource();
            var log = new StubLog();

            Exception? boom = null;
            var sw = Stopwatch.StartNew();
            try
            {
                Task.Run(async () =>
                {
                    await Task.Delay(100);
                    cts.Cancel();
                });
                run.Engine!.Run(run.NewContext(log, cts.Token));
            }
            catch (Exception ex)
            {
                boom = ex;
            }
            sw.Stop();

            Check("等待节点：停止请求 → 不抛（取消不是失败）",
                boom == null, boom == null ? "" : boom.GetType().Name + ": " + boom.Message);
            Check("等待节点：停止请求 → 状态 Skipped",
                wait.State == StepState.Skipped, $"State={wait.State}");
            Check("等待节点：停止请求立即返回（< 1500ms，timeout=5000ms 不等满）",
                sw.ElapsedMilliseconds < 1500, $"总耗时={sw.ElapsedMilliseconds}ms");
            Check("等待节点：取消不写 TimedOut（TimedOut 只表示超时）",
                !node.TimedOutPort.TypedValue, $"TimedOut={node.TimedOutPort.TypedValue}");
            // M5：取消分支也要 EndTiming（UpdateStepRuntimeState(Skipped) 只释放焦点，不结束计时）
            Check("等待节点：停止请求也会冻结耗时（LastRunTimeMs > 0，不是停在 0）",
                wait.LastRunTimeMs > 0, $"LastRunTimeMs={wait.LastRunTimeMs:0.0}ms");
        }

        // ==================================================================
        //  [E24-7b] 跨轮端口清零：同一个编译节点被连续运行复用时，上一轮的
        //           TimedOut / ElapsedMs 不许残留到本轮（L1）
        // ==================================================================
        private static void PortsResetBetweenRuns()
        {
            var wait = NewWait("false", poll: 20, timeout: 200, action: WaitTimeoutAction.Continue);
            var run = ExecHarness.Prepare(new StepModel[] { wait });
            Check("等待节点：跨轮端口用例编译通过", run.Compiled, run.Errors);
            if (!run.Compiled) return;

            var node = NodeOf(run, wait);

            // 第一轮：超时（Continue 不抛）→ TimedOut=true / ElapsedMs≈200
            run.Engine!.Run(run.NewContext(new StubLog()));
            Check("等待节点：第一轮超时 → TimedOut=true（为第二轮残留检查立前提）",
                node.TimedOutPort.TypedValue && node.ElapsedMsPort.TypedValue >= 150,
                $"TimedOut={node.TimedOutPort.TypedValue} ElapsedMs={node.ElapsedMsPort.TypedValue}");

            // 第二轮：同一个节点立刻被停止（40ms 后取消，远早于 200ms 超时）。
            // 入口清零若缺失，TimedOut 会留着第一轮的 true —— 下游会误以为本轮也超时了。
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(40);
            run.Engine.Run(run.NewContext(new StubLog(), cts.Token));

            Check("等待节点：第二轮入口清零（TimedOut=false / ElapsedMs=0，不残留上一轮）",
                !node.TimedOutPort.TypedValue && node.ElapsedMsPort.TypedValue == 0,
                $"TimedOut={node.TimedOutPort.TypedValue} ElapsedMs={node.ElapsedMsPort.TypedValue} State={wait.State}");
        }

        // ==================================================================
        //  [E24-8] TimeoutMs=0：不限（只响应取消/条件成立），200ms 后成立即继续
        // ==================================================================
        private static void ZeroTimeoutNeverTimesOut()
        {
            var flag = NewGlobalBool("WaitFlag");
            var wait = NewWait("Flag", poll: 20, timeout: 0, linkGlobalFlag: true);
            var run = ExecHarness.Prepare(new StepModel[] { wait }, ws => ws.GlobalVariables.Add(flag));
            Check("等待节点：TimeoutMs=0 用例编译通过", run.Compiled, run.Errors);
            if (!run.Compiled) return;

            var node = NodeOf(run, wait);

            // 兜底取消：TimeoutMs=0 是"永不超时"，万一实现退化成死等，这条令牌 2 秒后把
            // 断言宿主救回来（否则整套 gate 会挂死，而不是红一条）。
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(2000);

            Exception? boom = null;
            try
            {
                Task.Run(async () =>
                {
                    await Task.Delay(200);
                    flag.Value = true;
                });
                run.Engine!.Run(run.NewContext(new StubLog(), cts.Token));
            }
            catch (Exception ex)
            {
                boom = ex;
            }

            Check("等待节点：TimeoutMs=0 不触发超时（200ms 后条件成立 → Success，且未用到兜底取消）",
                boom == null && wait.State == StepState.Success
                    && !node.TimedOutPort.TypedValue && !cts.IsCancellationRequested,
                boom == null
                    ? $"State={wait.State} TimedOut={node.TimedOutPort.TypedValue} ElapsedMs={node.ElapsedMsPort.TypedValue} 兜底取消={cts.IsCancellationRequested}"
                    : $"抛了 {boom.GetType().Name}: {boom.Message}");
        }

        // ==================================================================
        //  [E24-9] 报警变量：存在则写 true；不存在只 Warn、超时处置不变
        // ==================================================================
        private static void AlarmVariable()
        {
            // ---- ① 变量存在：写 true ----
            var alarm = NewGlobalBool("AlarmFlag");
            var wait = NewWait(
                "false", poll: 20, timeout: 100, action: WaitTimeoutAction.Continue, alarm: "AlarmFlag");
            var run = ExecHarness.Prepare(new StepModel[] { wait }, ws => ws.GlobalVariables.Add(alarm));
            Check("等待节点：报警变量用例编译通过", run.Compiled, run.Errors);
            if (run.Compiled)
            {
                Exception? boom = null;
                try { run.Engine!.Run(run.NewContext(new StubLog())); }
                catch (Exception ex) { boom = ex; }

                Check("等待节点：超时 → 报警全局变量被写 true（Continue 下不抛）",
                    boom == null && Equals(alarm.Value, true),
                    boom == null ? $"AlarmFlag.Value={alarm.Value}" : boom.GetType().Name + ": " + boom.Message);
            }

            // ---- ② 变量不存在：只 Warn，不抛，Continue 仍继续 ----
            var waitMissing = NewWait(
                "false", poll: 20, timeout: 100, action: WaitTimeoutAction.Continue, alarm: "NoSuchAlarmFlag");
            var after = new ActionStep("A", "计数", CountingType, "报警写失败后计数");
            CountingPlugin.Reset();
            var runMissing = ExecHarness.Prepare(new StepModel[] { waitMissing, after });
            Check("等待节点：报警变量不存在用例编译通过", runMissing.Compiled, runMissing.Errors);
            if (runMissing.Compiled)
            {
                var log = new StubLog();
                Exception? boom = null;
                try { runMissing.Engine!.Run(runMissing.NewContext(log)); }
                catch (Exception ex) { boom = ex; }

                Check("等待节点：报警变量不存在 → 只 Warn（含“报警变量”），不抛",
                    boom == null && log.Warns.Any(w => w.Contains("报警变量")),
                    boom == null
                        ? string.Join(" | ", log.Warns)
                        : boom.GetType().Name + ": " + boom.Message);
                Check("等待节点：报警写失败不改变超时处置（Continue 仍继续，Runs==1）",
                    CountingPlugin.Runs == 1, $"Runs={CountingPlugin.Runs}");
            }
        }

        // ==================================================================
        //  [E24-10] .vms 往返：等待节点的六个语义属性与变量/连线表必须原样存回
        //          与 .vms 落盘、画布复制粘贴共用同一份序列化设置（SolutionService.SerializationSettings）
        // ==================================================================
        private static void VmsRoundTrip()
        {
            var wait = new WaitStep("\uE700", "等待", "BuiltIn_Wait", "等待往返")
            {
                ConditionExpression = "Flag",
                PollIntervalMs = 33,
                TimeoutMs = 4321,
                TimeoutAction = WaitTimeoutAction.Continue,
                TimeoutMessage = "料未到位",
                AlarmVariableName = "AlarmFlag",
            };
            var varId = Guid.NewGuid();
            wait.LocalVariables.Add(
                new LocalVariableItem { Id = varId, Name = "Flag", DataTypeName = BoolType });
            wait.LinkedSources[varId.ToString()] =
                new LinkReference(LinkKind.GlobalVariable, Guid.Empty, "WaitFlag", "Global.WaitFlag");

            var json = JsonConvert.SerializeObject(
                wait, typeof(StepModel), SolutionService.SerializationSettings);
            var back = JsonConvert.DeserializeObject<StepModel>(
                json, SolutionService.SerializationSettings) as WaitStep;

            Check("等待节点：.vms 往返保留六个语义属性",
                back != null
                    && back.ConditionExpression == "Flag"
                    && back.PollIntervalMs == 33
                    && back.TimeoutMs == 4321
                    && back.TimeoutAction == WaitTimeoutAction.Continue
                    && back.TimeoutMessage == "料未到位"
                    && back.AlarmVariableName == "AlarmFlag",
                back == null
                    ? "反序列化得到 null（$type 白名单没放行？）"
                    : $"expr='{back.ConditionExpression}' poll={back.PollIntervalMs} timeout={back.TimeoutMs} "
                        + $"action={back.TimeoutAction} msg='{back.TimeoutMessage}' alarm='{back.AlarmVariableName}'");

            Check("等待节点：.vms 往返保留变量表与连线表（Id 不变、连线键仍能命中）",
                back != null
                    && back.LocalVariables.Count == 1
                    && back.LocalVariables[0].Id == varId
                    && back.LocalVariables[0].Name == "Flag"
                    && back.LinkedSources.Count == 1
                    && back.LinkedSources.ContainsKey(varId.ToString()),
                back == null
                    ? "反序列化得到 null"
                    : $"vars={back.LocalVariables.Count} links={back.LinkedSources.Count}");
        }
    }
}
