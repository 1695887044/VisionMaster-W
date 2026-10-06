using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Core.Interfaces;
using Newtonsoft.Json;
using VisionMaster;
using VisionMaster.Models;
using VisionMaster.Services;
// 直接复用断言宿主的 Section/Check 计数口径，与 ExecutionChecks 同款
using static FlowCanvasChecks.Program;

namespace FlowCanvasChecks
{
    /// <summary>
    /// DWV 第 1 期（断点 / 单步 / 暂停继续）断言：调试门的行为契约。
    ///
    /// 断言面与手段：
    ///  · 调试门是全引擎唯一插桩点（CompiledNode.RunSequence），所以这里全部走"真图纸 → 真编译 →
    ///    真引擎（FlowEngineService）"；执行点在后台线程，用轮询等待（超时即失败，不挂死）。
    ///  · 豁免是硬约束：HTTP / 试运行 / 非调试单次执行不得被断点卡住 —— 用"跑得完"来钉。
///  · 调试态归引擎置位：调用方以 debugSession 参数请求，引擎在抢到会话锁后赋值（收尾清 false）；
///    调用方预置的 true 不生效（P2：界面预置 + HTTP 抢锁 = 调试态泄漏给非界面运行）。
    ///  · 断点不落盘、不递增 Version：用序列化文本与 FlowModel.Version 来钉（[E5] 的延伸）。
    ///  · 停止打断：停在断点上 StopSession 必须让任务干净退出（Canceled 或 RanToCompletion，
    ///    只有 Faulted 算炸），线程不残留、锁回放行态。
    /// </summary>
    internal static class DebugChecks
    {
        internal static void Run()
        {
            BreakpointHitsAndResumes();
            StopWhilePausedCleansUp();
            NonDebugSingleRunIsExempt();
            StepOnceExecutesExactlyOneNode();
            LoopBreakpointHitsEveryIteration();
            ContinuousDebugRunHitsAgainOnNextRound();
            ToggleBreakpointDoesNotBumpVersion();
            SerializationKeepsDebugFlagsOut();
            TestRunIsExempt();
            PauseAndResumeDuringDebugSingleRun();
            EngineOwnsDebugFlag();
        }

        // ==================================================================
        //  公共夹具
        // ==================================================================

        /// <summary>把图纸放进容器的某个分支里（与 ExecutionChecks 同款）</summary>
        private static void Put(StepCollection branch, params StepModel[] steps)
        {
            foreach (var s in steps) branch.Steps.Add(s);
        }

        private static string Aqn<T>() => typeof(T).AssemblyQualifiedName!;

        /// <summary>轮询等待条件成立（引擎跑在后台线程；超时返回 false，防断言程序挂死）</summary>
        private static bool WaitFor(Func<bool> condition, int timeoutMs = 5000)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (condition()) return true;
                Thread.Sleep(5);
            }
            return condition();
        }

        /// <summary>
        /// 停止是个合法出口：停在暂停点时取消令牌会让调试门抛 OperationCanceledException、
        /// 任务状态变 Canceled；RanToCompletion 也正常，只有 Faulted 才是炸（口径同 [E11]）。
        /// </summary>
        private static bool EndedCleanly(Task t)
        {
            try
            {
                t.Wait(TimeSpan.FromSeconds(5));
                return true;
            }
            catch (AggregateException ae) when (t.IsCanceled && ae.InnerExceptions.All(x => x is TaskCanceledException))
            {
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static FlowEngineService NewEngine(ExecRun run, StubLog log)
            => new(new RuntimeManager(), log, run.Workspace!, null, new ResourceLockService(), Core.Interfaces.NullCameraProvider.Instance);

        /// <summary>三节点计数流程：A → B(可挂断点) → C，各步用独立计数桩分账</summary>
        private static ExecRun PrepareAbc(bool breakpointOnB, out ActionStep a, out ActionStep b, out ActionStep c)
        {
            CountA.Reset(); CountB.Reset(); CountC.Reset();
            a = new ActionStep("A", "计数A", Aqn<CountA>(), "断点前序A");
            b = new ActionStep("B", "计数B", Aqn<CountB>(), "断点命中B");
            c = new ActionStep("C", "计数C", Aqn<CountC>(), "断点后序C");
            b.IsBreakpoint = breakpointOnB;
            return ExecHarness.Prepare(new StepModel[] { a, b, c });
        }

        // ==================================================================
        //  [E13] ① 断点命中与继续（调试单次执行可命中；前序已执行 / 命中未执行）
        // ==================================================================
        private static void BreakpointHitsAndResumes()
        {
            Section("[E13] 断点命中 / 继续（调试单次执行）");

            var run = PrepareAbc(breakpointOnB: true, out _, out var b, out _);
            Check("断点图纸编译通过", run.Compiled, run.Errors);
            if (!run.Compiled) return;

            var engine = NewEngine(run, new StubLog());
            var session = run.Session!;

            string? pauseMessage = null;
            engine.SessionStateChanged += (_, e) => { if (e.NewState == SessionState.Paused) pauseMessage = e.Message; };

            // Shell 两处运行入口的同款接线：调试态由引擎按 debugSession 参数、在抢到会话锁后置位
            var task = engine.RunSessionOnceAsync(session, debugSession: true);

            bool paused = WaitFor(() => session.State == SessionState.Paused);
            Check("命中断点后停在 Paused", paused, $"State={session.State}（超时说明断点没拦住）");
            Check("暂停原因 = Breakpoint", session.PauseReason == SessionPauseReason.Breakpoint, $"PauseReason={session.PauseReason}");
            Check("前序节点已执行（A=1）", CountA.Runs == 1, $"A={CountA.Runs}");
            Check("命中节点停在执行前（B=0）", CountB.Runs == 0, $"B={CountB.Runs}");
            Check("后序节点未执行（C=0）", CountC.Runs == 0, $"C={CountC.Runs}");
            Check("命中节点被标记 IsDebugStopped（UI 停点高亮）", b.IsDebugStopped, $"IsDebugStopped={b.IsDebugStopped}");
            Check("会话仍活着等待放行（IsRunning=true）", session.IsRunning, $"IsRunning={session.IsRunning}");
            bool msgSeen = WaitFor(() => pauseMessage != null, 2000);
            Check("暂停消息承载原因（Message 首次启用）", msgSeen && pauseMessage?.Contains("断点") == true, $"Message={pauseMessage ?? "(null)"}");

            engine.ResumeSession(session);
            Check("继续后回到 Running", session.State == SessionState.Running, $"State={session.State}");
            Check("继续后暂停原因清空", session.PauseReason == SessionPauseReason.None, $"PauseReason={session.PauseReason}");

            Check("继续后正常跑完（任务干净结束）", EndedCleanly(task),
                task.IsFaulted ? $"任务 Faulted：{task.Exception?.GetBaseException().Message}" : "");
            Check("继续后全流程走完（A=B=C=1）", CountA.Runs == 1 && CountB.Runs == 1 && CountC.Runs == 1,
                $"A={CountA.Runs} B={CountB.Runs} C={CountC.Runs}");
            Check("收尾后 State=Stopped 且 IsRunning=false",
                !session.IsRunning && session.State == SessionState.Stopped, $"IsRunning={session.IsRunning} State={session.State}");
        }

        // ==================================================================
        //  [E13] ② 停在断点时 StopSession：立即打断、干净收尾、无残留
        // ==================================================================
        private static void StopWhilePausedCleansUp()
        {
            Section("[E13] 停在断点时 StopSession 干净收尾");

            var run = PrepareAbc(breakpointOnB: true, out _, out var b, out _);
            Check("停止打断图纸编译通过", run.Compiled, run.Errors);
            if (!run.Compiled) return;

            var engine = NewEngine(run, new StubLog());
            var session = run.Session!;
            var task = engine.RunSessionOnceAsync(session, debugSession: true);
            bool paused = WaitFor(() => session.State == SessionState.Paused);
            Check("已停在断点上（构造出打断窗口）", paused && CountB.Runs == 0, $"paused={paused} B={CountB.Runs}");

            engine.StopSession(session); // 立即打断：令牌取消必须唤醒 PauseLock.Wait(token)

            Check("停点上的停止立即生效：任务干净收尾（非 Faulted）", EndedCleanly(task),
                task.IsFaulted ? $"任务 Faulted：{task.Exception?.GetBaseException().Message}" : "（超时说明 PauseLock.Wait(token) 没被取消唤醒）");
            Check("收尾后 State=Stopped 且 IsRunning=false", !session.IsRunning && session.State == SessionState.Stopped,
                $"State={session.State} IsRunning={session.IsRunning}");
            Check("命中节点始终未执行（停在执行前）", CountB.Runs == 0, $"B={CountB.Runs}");
            Check("停点高亮随收尾清除（IsDebugStopped=false）", !b.IsDebugStopped, $"IsDebugStopped={b.IsDebugStopped}");
            Check("暂停锁回到放行态（收尾 Set 兜底）", session.PauseLock.IsSet, "锁留在合上态：后续等待会被莫名按住");
            Check("收尾清除运行焦点（无残留高亮）", session.FocusedStep == null, $"FocusedStep={session.FocusedStep?.StepName ?? "(null)"}");
        }

        // ==================================================================
        //  [E13] ③ 非调试单次执行豁免（HTTP 触发同款路径：DebugEnabled=false 不被断点卡住）
        // ==================================================================
        private static void NonDebugSingleRunIsExempt()
        {
            Section("[E13] 非调试单次执行豁免（HTTP 触发同款路径）");

            var run = PrepareAbc(breakpointOnB: true, out _, out _, out _);
            Check("豁免图纸编译通过", run.Compiled, run.Errors);
            if (!run.Compiled) return;

            var engine = NewEngine(run, new StubLog());
            var session = run.Session!;
            Check("DebugEnabled 默认 false", !session.DebugEnabled, $"DebugEnabled={session.DebugEnabled}");

            int pausedSeen = 0;
            engine.SessionStateChanged += (_, e) => { if (e.NewState == SessionState.Paused) Interlocked.Increment(ref pausedSeen); };

            var task = engine.RunSessionOnceAsync(session);
            Check("挂着断点也不被卡住：一次跑完", EndedCleanly(task),
                task.IsFaulted ? $"任务 Faulted：{task.Exception?.GetBaseException().Message}" : "（超时说明断点把非调试运行卡住了）");
            Check("全流程执行（A=B=C=1）", CountA.Runs == 1 && CountB.Runs == 1 && CountC.Runs == 1,
                $"A={CountA.Runs} B={CountB.Runs} C={CountC.Runs}");
            Check("全程从未进入 Paused", pausedSeen == 0, $"Paused 次数={pausedSeen}");
            Check("收尾后 State=Stopped 且 IsRunning=false", !session.IsRunning && session.State == SessionState.Stopped,
                $"State={session.State} IsRunning={session.IsRunning}");
        }

        // ==================================================================
        //  [E13] ④ 单步：从断点停点放行，恰好执行 1 个节点后再次停住（连步两次验证）
        // ==================================================================
        private static void StepOnceExecutesExactlyOneNode()
        {
            Section("[E13] 单步：恰好多执行 1 个节点");

            CountA.Reset(); CountB.Reset(); CountC.Reset(); CountD.Reset();
            var a = new ActionStep("A", "计数A", Aqn<CountA>(), "步进A");
            var b = new ActionStep("B", "计数B", Aqn<CountB>(), "断点B");
            var c = new ActionStep("C", "计数C", Aqn<CountC>(), "步进C");
            var d = new ActionStep("D", "计数D", Aqn<CountD>(), "步进D");
            b.IsBreakpoint = true;
            var run = ExecHarness.Prepare(new StepModel[] { a, b, c, d });
            Check("单步图纸编译通过", run.Compiled, run.Errors);
            if (!run.Compiled) return;

            var engine = NewEngine(run, new StubLog());
            var session = run.Session!;
            var task = engine.RunSessionOnceAsync(session, debugSession: true);
            bool stop1 = WaitFor(() => session.State == SessionState.Paused);
            Check("先命中 B 的断点（A=1 / B=0）", stop1 && CountA.Runs == 1 && CountB.Runs == 0,
                $"paused={stop1} A={CountA.Runs} B={CountB.Runs}");

            // ---- 第一次单步：执行 B，停到 C 前（恰好 +1 个节点） ----
            engine.StepSession(session);
            bool stop2 = WaitFor(() => session.State == SessionState.Paused && CountB.Runs == 1 && CountC.Runs == 0);
            Check("单步后停在下一个节点前（B 已执行 / C 未执行 —— 恰好 +1）", stop2,
                $"State={session.State} B={CountB.Runs} C={CountC.Runs}");
            Check("单步停点原因 = Step", session.PauseReason == SessionPauseReason.Step, $"PauseReason={session.PauseReason}");

            // ---- 第二次单步：执行 C，停到 D 前（再恰好 +1 个节点） ----
            engine.StepSession(session);
            bool stop3 = WaitFor(() => session.State == SessionState.Paused && CountC.Runs == 1 && CountD.Runs == 0);
            Check("再次单步仍恰好 +1（C 已执行 / D 未执行）", stop3,
                $"State={session.State} C={CountC.Runs} D={CountD.Runs}");

            engine.ResumeSession(session);
            Check("从单步停点继续后跑完（D 也执行）", EndedCleanly(task) && CountD.Runs == 1,
                $"D={CountD.Runs} " + (task.IsFaulted ? "任务 Faulted" : ""));
        }

        // ==================================================================
        //  [E13] ⑤ 循环体断点：每圈命中都断（循环语义与断点共存）
        // ==================================================================
        private static void LoopBreakpointHitsEveryIteration()
        {
            Section("[E13] 循环体断点：每圈命中都断");

            CountA.Reset();
            var body = new ActionStep("A", "循环体", Aqn<CountA>(), "圈内断点");
            body.IsBreakpoint = true;
            var forStep = new ForStep("F", "计次循环", Aqn<CountA>(), "两圈For");
            forStep.DefaultLoopCount = 2;
            Put(forStep.Children[0], body);

            var run = ExecHarness.Prepare(new StepModel[] { forStep });
            Check("循环断点图纸编译通过", run.Compiled, run.Errors);
            if (!run.Compiled) return;

            var engine = NewEngine(run, new StubLog());
            var session = run.Session!;
            var task = engine.RunSessionOnceAsync(session, debugSession: true);

            bool hit1 = WaitFor(() => session.State == SessionState.Paused);
            Check("第 1 圈断开住（body=0，停在执行前）", hit1 && CountA.Runs == 0, $"paused={hit1} body={CountA.Runs}");

            engine.ResumeSession(session);
            bool hit2 = WaitFor(() => session.State == SessionState.Paused && CountA.Runs == 1);
            Check("第 2 圈同一断点再次断开住（body=1）", hit2, $"State={session.State} body={CountA.Runs}");

            engine.ResumeSession(session);
            Check("继续后两圈跑完并正常收尾", EndedCleanly(task) && CountA.Runs == 2, $"body={CountA.Runs}");
        }

        // ==================================================================
        //  [E13] ⑥ 连续调试运行：第二圈再次命中；停在断点上停止仍干净收尾
        // ==================================================================
        private static void ContinuousDebugRunHitsAgainOnNextRound()
        {
            Section("[E13] 连续调试运行：第二圈再次命中 + 停点停止");

            var run = PrepareAbc(breakpointOnB: true, out _, out _, out _);
            Check("连续调试图纸编译通过", run.Compiled, run.Errors);
            if (!run.Compiled) return;

            var engine = NewEngine(run, new StubLog());
            var session = run.Session!;

            int pausedSeen = 0;
            engine.SessionStateChanged += (_, e) => { if (e.NewState == SessionState.Paused) Interlocked.Increment(ref pausedSeen); };

            var task = engine.RunSessionAsync(session, debugSession: true);

            bool stop1 = WaitFor(() => session.State == SessionState.Paused);
            Check("第 1 圈断点停住（A=1 / B=0）", stop1 && CountA.Runs == 1 && CountB.Runs == 0,
                $"paused={stop1} A={CountA.Runs} B={CountB.Runs}");

            engine.ResumeSession(session);
            bool stop2 = WaitFor(() => session.State == SessionState.Paused && CountA.Runs == 2);
            Check("第 2 圈同一断点再次停住（A=2 / B=1 / C=1）", stop2 && CountB.Runs == 1 && CountC.Runs == 1,
                $"State={session.State} A={CountA.Runs} B={CountB.Runs} C={CountC.Runs}");
            bool twoPaused = WaitFor(() => Volatile.Read(ref pausedSeen) == 2, 2000);
            Check("两次命中恰好两次 Paused 事件", twoPaused, $"Paused 次数={Volatile.Read(ref pausedSeen)}");

            engine.StopSession(session);
            Check("停在断点时 StopSession 干净收尾（线程不残留）", EndedCleanly(task),
                task.IsFaulted ? $"任务 Faulted：{task.Exception?.GetBaseException().Message}" : "（超时说明停止打不断断点等待）");
            Check("停止后 State=Stopped 且 IsRunning=false", !session.IsRunning && session.State == SessionState.Stopped,
                $"State={session.State} IsRunning={session.IsRunning}");
        }

        // ==================================================================
        //  [E13] ⑦ 断点标记不递增 Version（[E5] 的延伸：调试设施不得惊动语义闸门）
        // ==================================================================
        private static void ToggleBreakpointDoesNotBumpVersion()
        {
            Section("[E13] 断点标记：不递增 Version");

            var step = new ActionStep("A", "计数A", Aqn<CountA>(), "版本断言步骤");
            var flow = new FlowModel { FlowName = "断点版本断言" };
            flow.Steps.Add(step);

            var stepNotices = new List<string>();
            step.PropertyChanged += (_, e) => stepNotices.Add(e.PropertyName ?? "(null)");
            var flowNotices = new List<string>();
            flow.PropertyChanged += (_, e) => flowNotices.Add(e.PropertyName ?? "(null)");

            int versionBefore = flow.Version;
            step.IsBreakpoint = true;
            step.IsBreakpoint = false;
            step.IsDebugStopped = true;
            step.IsDebugStopped = false;

            Check("切换断点/停点标记不递增 Version", flow.Version == versionBefore,
                $"Version {versionBefore} → {flow.Version}");
            Check("标记变更确实发了通知（UI 红点 / 停点高亮会刷新）",
                stepNotices.Contains(nameof(StepModel.IsBreakpoint)) && stepNotices.Contains(nameof(StepModel.IsDebugStopped)),
                string.Join(",", stepNotices));
            Check("流程一路零通知（不惊动语义变更闸门）", flowNotices.Count == 0, string.Join(",", flowNotices));
        }

        // ==================================================================
        //  [E13] ⑧ 断点不落盘：序列化文本不含调试标记
        // ==================================================================
        private static void SerializationKeepsDebugFlagsOut()
        {
            Section("[E13] 断点不落盘：序列化文本不含调试标记");

            var step = new ActionStep("A", "计数A", Aqn<CountA>(), "序列化步骤");
            step.IsBreakpoint = true;
            step.IsDebugStopped = true;

            var json = JsonConvert.SerializeObject(step, Formatting.Indented);
            Check("序列化文本不含 IsBreakpoint", !json.Contains("IsBreakpoint"),
                "断点跟着方案进产线是安全隐患（评审结论 4）");
            Check("序列化文本不含 IsDebugStopped", !json.Contains("IsDebugStopped"), "");
            Check("同类运行期标记同样不落盘（IsRunningFocus 对照）", !json.Contains("IsRunningFocus"), "");
            Check("常规字段仍被序列化（防'空文本假通过'）",
                json.Contains("StepName") && json.Contains("PluginName"), "");
        }

        // ==================================================================
        //  [E13] ⑨ 插件试运行豁免：带断点的步骤试运行不得被卡住
        // ==================================================================
        private static void TestRunIsExempt()
        {
            Section("[E13] 插件试运行豁免：带断点也不能卡住");

            CountA.Reset();
            var step = new ActionStep("A", "计数A", Aqn<CountA>(), "试运行断点步骤");
            step.IsBreakpoint = true; // 故意挂着断点：试运行属于"非调试豁免"路径

            var flow = new FlowModel { FlowName = "试运行豁免流程" };
            flow.Steps.Add(step);

            // PluginTestRunner 要求工作区有 CurrentFlow（它按当前流程编译）——组装最小方案外壳
            var workspace = new WorkspaceContext();
            var solution = new SolutionModel();
            solution.Flows.Add(flow);
            workspace.SwitchSolution(solution);
            workspace.SwitchFlow(flow);

            var log = new StubLog();
            var result = PluginTestRunner.Run(
                new CountA(),
                step,
                workspace,
                log,
                new FlowCompiler(workspace),
                CancellationToken.None,
                out var trialSession);

            Check("带断点的步骤试运行正常完成（未被断点卡住）", result.Success,
                result.Success ? result.Message : result.ErrorMessage);
            Check("试运行确实执行了目标算子", CountA.Runs == 1, $"Runs={CountA.Runs}");
            Check("试运行会话从未进入调试态（DebugEnabled=false）",
                trialSession != null && !trialSession.DebugEnabled,
                $"trial={(trialSession != null)} DebugEnabled={trialSession?.DebugEnabled}");
        }

        // ==================================================================
        //  [E13] ⑩ 调试单次执行：暂停 / 继续生效（用户决策推翻评审稿 §3 的验收点）
        // ==================================================================
        private static void PauseAndResumeDuringDebugSingleRun()
        {
            Section("[E13] 调试单次执行：暂停 / 继续生效");

            GatePlugin.Reset();
            CountA.Reset(); CountC.Reset();
            var a = new ActionStep("A", "计数A", Aqn<CountA>(), "暂停前A");
            var b = new ActionStep("B", "闸门", Aqn<GatePlugin>(), "暂停闸门");
            var c = new ActionStep("C", "计数C", Aqn<CountC>(), "继续后C");
            var run = ExecHarness.Prepare(new StepModel[] { a, b, c });
            Check("暂停继续图纸编译通过", run.Compiled, run.Errors);
            if (!run.Compiled) return;

            var engine = NewEngine(run, new StubLog());
            var session = run.Session!;
            var task = engine.RunSessionOnceAsync(session, debugSession: true);
            bool inGate = GatePlugin.Reached.Wait(TimeSpan.FromSeconds(5));
            Check("执行中已进入闸门算子（构造出运行窗口）", inGate, "5 秒内没进到算子，后面的暂停断言无意义");

            engine.PauseSession(session);
            Check("调试单次执行期间 PauseSession 生效（State=Paused）", session.State == SessionState.Paused,
                $"State={session.State}（非调试单次执行时这里必须仍是 Running）");
            Check("暂停原因 = User", session.PauseReason == SessionPauseReason.User, $"PauseReason={session.PauseReason}");

            // 放行闸门算子：本节点跑完后，调试门应在下一个节点执行前把流程按住
            GatePlugin.Proceed.Set();
            bool bDone = WaitFor(() => b.State == StepState.Success, 2000);
            Thread.Sleep(100); // 让调试门有时间把下一节点按住（没按住则 C 会瞬间跑过，下一条必要红）
            Check("闸门后的下一节点被调试门按住（C 未执行）", bDone && CountC.Runs == 0 && session.State == SessionState.Paused,
                $"bDone={bDone} State={session.State} C={CountC.Runs}");

            engine.ResumeSession(session);
            Check("继续后跑完并正常收尾", EndedCleanly(task) && CountA.Runs == 1 && CountC.Runs == 1,
                $"A={CountA.Runs} C={CountC.Runs} " + (task.IsFaulted ? "任务 Faulted" : ""));
        }

        // ==================================================================
        //  [E13] ⑪ 调试态由引擎独占置位：调用方预置的 true 不生效（P2 竞态收口）
        // ==================================================================
        private static void EngineOwnsDebugFlag()
        {
            Section("[E13] 调试态归引擎置位（预置 true 不生效）");

            // 竞态原形：界面在调用前置 true，随后 HTTP 触发抢先拿到会话锁 → 界面这次调用被拒，
            // true 却残留在会话上，那次 HTTP 运行就会被断点卡住（违反"HTTP 豁免"硬约束）。
            // 现在置位权在引擎侧：抢到锁后按调用显式参数**赋值**（不是只在 true 时置位），
            // 于是非调试调用顺带把残留清干净 —— 用"预置 true + 带断点图纸必须一次跑完"来钉。
            var run = PrepareAbc(breakpointOnB: true, out _, out _, out _);
            Check("竞态图纸编译通过", run.Compiled, run.Errors);
            if (!run.Compiled) return;

            var engine = NewEngine(run, new StubLog());
            var session = run.Session!;
            session.DebugEnabled = true; // 模拟残留：调用方在调用前擅自置位

            int pausedSeen = 0;
            engine.SessionStateChanged += (_, e) => { if (e.NewState == SessionState.Paused) Interlocked.Increment(ref pausedSeen); };

            var task = engine.RunSessionOnceAsync(session); // 非调试调用（默认参数）：引擎应把残留清掉
            Check("预置的调试态被引擎清掉：带断点也不停", EndedCleanly(task) && pausedSeen == 0,
                task.IsFaulted
                    ? $"任务 Faulted：{task.Exception?.GetBaseException().Message}"
                    : $"Paused 次数={pausedSeen}（>0 说明残留的 true 把断点放行了）");
            Check("非调试调用收尾后 DebugEnabled=false", !session.DebugEnabled, $"DebugEnabled={session.DebugEnabled}");
            Check("全流程照常执行（A=B=C=1）", CountA.Runs == 1 && CountB.Runs == 1 && CountC.Runs == 1,
                $"A={CountA.Runs} B={CountB.Runs} C={CountC.Runs}");
        }
    }

    /// <summary>断点断言用计数桩：各自独立静态计数——共用一个计数就分不清"到底哪个节点跑了"</summary>
    internal sealed class CountA : VisionPluginBase
    {
        public static int Runs;
        public static void Reset() => Runs = 0;
        public override void RunAlgorithm(IExecutionContext context) => Interlocked.Increment(ref Runs);
    }

    internal sealed class CountB : VisionPluginBase
    {
        public static int Runs;
        public static void Reset() => Runs = 0;
        public override void RunAlgorithm(IExecutionContext context) => Interlocked.Increment(ref Runs);
    }

    internal sealed class CountC : VisionPluginBase
    {
        public static int Runs;
        public static void Reset() => Runs = 0;
        public override void RunAlgorithm(IExecutionContext context) => Interlocked.Increment(ref Runs);
    }

    internal sealed class CountD : VisionPluginBase
    {
        public static int Runs;
        public static void Reset() => Runs = 0;
        public override void RunAlgorithm(IExecutionContext context) => Interlocked.Increment(ref Runs);
    }
}
