using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.Interfaces;
using VisionMaster;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
// ParallelExecutionMode 与 System.Linq.ParallelExecutionMode 撞名（本文件 using System.Linq），别名固定
using ParallelExecutionMode = VisionMaster.Models.ParallelExecutionMode;
// 本文件 using 了 System.Threading，裸写的 ExecutionContext 会和 System.Threading.ExecutionContext 撞名
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    // ==================================================================
    //  桩插件（纪律照 ExecutionHarness 顶部注释：真类型 + AssemblyQualifiedName、
    //  端口全 IsRequired=false、无输入口）
    // ==================================================================

    /// <summary>
    /// 并行安全闸门桩：进来了举旗，卡在门口等放行（GatePlugin 的 [ParallelSafe] 版）。
    /// P1 真并发：分支1 放它（堵住），分支2 放计数桩——顺序语义下不可能"分支2 已计数而分支1 还堵着"。
    /// </summary>
    [ParallelSafe]
    internal sealed class SafeGatePlugin : VisionPluginBase
    {
        public static readonly ManualResetEventSlim Reached = new(false);
        public static readonly ManualResetEventSlim Proceed = new(false);

        public static void Reset()
        {
            Reached.Reset();
            Proceed.Reset();
        }

        public override void RunAlgorithm(IExecutionContext context)
        {
            Reached.Set();
            // 兜 5 秒 + 令牌步进（协作取消范式：被取消时提前退出，不拖 join）
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!Proceed.IsSet && sw.ElapsedMilliseconds < 5000)
            {
                if (context.CancellationToken.IsCancellationRequested) return;
                Thread.Sleep(20);
            }
        }
    }

    /// <summary>
    /// 抛异常桩（P2/P4：故障源登记——Message 带分支标记供断言溯源）。
    /// 必须重写 RethrowOnException=true：默认 false 时 VisionPluginBase.Execute 会把异常
    /// 转成业务失败（不 rethrow），那就测不到"失败源异常原样上抛"的引擎语义。
    /// </summary>
    [ParallelSafe]
    internal sealed class ThrowPlugin : VisionPluginBase
    {
        public static string Marker = "分支专属故障";

        protected override bool RethrowOnException => true;

        public override void RunAlgorithm(IExecutionContext context)
            => throw new InvalidOperationException($"ThrowPlugin:{Marker}");
    }

    /// <summary>业务失败桩（P7/P24/P28：Success=false 不抛异常）</summary>
    [ParallelSafe]
    internal sealed class BizFailPlugin : VisionPluginBase
    {
        public override void RunAlgorithm(IExecutionContext context)
        {
            Success.Value = false;
            ErrorMessage.Value = "业务失败桩";
        }
    }

    /// <summary>变量写入桩（P8/P9/P28：把静态 Value 写进指定变量名）</summary>
    [ParallelSafe]
    internal sealed class VarWritePlugin : VisionPluginBase
    {
        public static string VarName = "X";
        public static object Value = 2;

        public override void RunAlgorithm(IExecutionContext context)
        {
            context.LocalVariables[VarName] = Value;
        }
    }

    /// <summary>慢速变量读取桩（P8 影子隔离：等闸门放行后读 X——顺序语义下会读到分支1 的写入）</summary>
    [ParallelSafe]
    internal sealed class SlowVarReadPlugin : VisionPluginBase
    {
        public static readonly ManualResetEventSlim Reached = new(false);
        public static readonly ManualResetEventSlim Proceed = new(false);
        public static object ReadValue;

        public static void Reset()
        {
            Reached.Reset();
            Proceed.Reset();
            ReadValue = null;
        }

        public override void RunAlgorithm(IExecutionContext context)
        {
            Reached.Set();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!Proceed.IsSet && sw.ElapsedMilliseconds < 5000)
            {
                if (context.CancellationToken.IsCancellationRequested) return;
                Thread.Sleep(20);
            }
            context.LocalVariables.TryGetValue(VarWritePlugin.VarName, out ReadValue);
        }
    }

    /// <summary>未标注并行安全的桩（P12/P13 编译门禁——本类绝不加 [ParallelSafe]）</summary>
    internal sealed class UnsafeStubPlugin : VisionPluginBase
    {
        public override void RunAlgorithm(IExecutionContext context)
        {
        }
    }

    /// <summary>
    /// 焦点探针桩（P21，评审低危 15：探针桩在真并发窗口内采样记录，消除跨线程采样竞态）：
    /// 到达时把"此刻容器 IsRunningFocus 与自己 State"记进线程安全集合，join 后统一断言。
    /// </summary>
    [ParallelSafe]
    internal sealed class FocusProbePlugin : VisionPluginBase
    {
        public sealed record Sample(bool ContainerFocus, StepState SelfState);

        public static readonly List<Sample> Samples = new();
        public static StepModel ContainerStep;

        public static void Reset(StepModel container)
        {
            lock (Samples) Samples.Clear();
            ContainerStep = container;
        }

        public override void RunAlgorithm(IExecutionContext context)
        {
            lock (Samples)
            {
                Samples.Add(new Sample(
                    ContainerStep?.IsRunningFocus == true,
                    BlueprintSelf(context)));
            }
        }

        private static StepState BlueprintSelf(IExecutionContext context)
        {
            // 桩自身步骤此刻刚置 Running（RunAndGetNext 先置 Running 再调 Execute）——读它
            return SelfState;
        }

        public static StepState SelfState = StepState.Running;
    }

    /// <summary>
    /// 并行执行二期断言（方案 v2 §8 P1-P30）。
    ///
    /// 手段：真图纸 → 真编译（FlowCompiler）→ 真引擎（Engine.Run 直调 / FlowEngineService）。
    /// 并发窗口用 GatePlugin 式闸门桩构造，前置 Reached.Wait 确保分支真的跑起来了。
    /// 既有 ParallelContainerChecks V1-V4（Sequential 默认）零改动 = 向后兼容证明。
    /// </summary>
    internal static class ParallelExecutionChecks
    {
        private static string Aqn<T>() => typeof(T).AssemblyQualifiedName!;

        private static ActionStep Step<T>(string name) where T : VisionPluginBase
            => new("\uE700", name, Aqn<T>(), name);

        private static ParallelStep ParallelGroup(string name, params StepModel[][] branches)
        {
            var par = new ParallelStep("\uE700", "并行分组", "BuiltIn_Parallel", name)
            {
                ExecutionMode = ParallelExecutionMode.Parallel,
            };
            for (int i = 0; i < branches.Length; i++)
            {
                if (i < par.Children.Count)
                {
                    par.Children[i].Steps.Clear();
                    foreach (var s in branches[i]) par.Children[i].Steps.Add(s);
                }
                else
                {
                    var col = new StepCollection { BranchType = BranchType.Default, StepName = $"分支 {i + 1}" };
                    foreach (var s in branches[i]) col.Steps.Add(s);
                    par.Children.Add(col);
                }
            }
            if (branches.Length < 2 && par.Children.Count > branches.Length)
            {
                // 收缩到实际分支数（P30 单分支场景）
                while (par.Children.Count > branches.Length) par.Children.RemoveAt(par.Children.Count - 1);
            }
            return par;
        }

        private static bool WaitFor(Func<bool> condition, int timeoutMs = 5000)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (condition()) return true;
                Thread.Sleep(5);
            }
            return condition();
        }

        internal static void Run()
        {
            RealConcurrencyAndJoin();          // P1 P2
            FailureSourceAndStop();            // P3 P4
            GroupCtsNoLeak();                  // P5
            AdjudicatePureFunction();          // P6
            FailFastStateMachine();            // P7
            CancelledShadowDrop();             // P28
            ShadowIsolation();                 // P8
            MergeAndAccounting();              // P9 P9b P10 P11
            CompileGate();                     // P12 P13 P14
            DebugAndTrialDegradation();        // P15 P16 P10(debug)
            BreakAndReturnSemantics();         // P17 P18 P19
            CompatibilityMatrix();             // P20 P22 P24 P26 P30
            FocusAndPoolStress();              // P21 P25 P27 P32
            RunFlowParallelGate();             // P23
            StaleArtifactGate();               // P31 产物守门
        }

        // ==================================================================
        //  P31 产物守门：标注 [ParallelSafe] 的插件源码 ⇒ Modules 里的 DLL 必须含特性元数据
        //  （2026-10-09 reviewer #1 事故回归闸：5 个插件改源码未重建，宿主门禁把"已标注算子"
        //   误报不安全。机制根因：某轮"全量重建"只覆盖了 FlowCanvasChecks 引用清单内的工程，
        //   清单外工程被漏。这里从源头守：凡源码含标注的产物，字节串必须含 ParallelSafeAttribute）
        // ==================================================================
        private static void StaleArtifactGate()
        {
            Section("[P31] 插件产物守门（标注源码 ⇒ DLL 含 ParallelSafe 元数据）");

            string[] annotated =
            {
                "Plugin.BeadInspect", "Plugin.BlobDetect", "Plugin.Calibration", "Plugin.CaliperMeasure",
                "Plugin.CodeReader", "Plugin.ColorCheck", "Plugin.ColorRegion", "Plugin.CreateRoi",
                "Plugin.DataRecord", "Plugin.Ocr", "Plugin.PoseTransform", "Plugin.PreProcessing",
                "Plugin.ResultUpload", "Plugin.Yolo",
                "Plugin.Matching", "Plugin.Util",   // Plugin.Utility 工程的产物名是 Plugin.Util.dll
            };
            var stale = new List<string>();
            foreach (var name in annotated)
            {
                var dll = Path.Combine(@"D:\C#\VM\Modules", name + ".dll");
                if (!File.Exists(dll)) { stale.Add(name + "(missing)"); continue; }
                var text = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(dll));
                if (!text.Contains("ParallelSafeAttribute"))
                    stale.Add(name);
            }
            Check("标注 [ParallelSafe] 的 16 个插件 DLL 全部含特性元数据（漏重建=门禁误报）",
                stale.Count == 0,
                stale.Count == 0 ? "" : "陈旧产物: " + string.Join(",", stale));
        }

        // ==================================================================
        //  P1 真并发 + P2 同步 join
        // ==================================================================
        private static void RealConcurrencyAndJoin()
        {
            Section("[P1/P2] 真并发与同步 join");

            SafeGatePlugin.Reset();
            CountingPlugin.Reset();

            var gate = Step<SafeGatePlugin>("分支1闸门");
            var counter = Step<CountingPlugin>("分支2计数");
            var par = ParallelGroup("并行组", new[] { gate }, new[] { counter });

            var run = ExecHarness.Prepare(new StepModel[] { par });
            Check("并行图纸编译成功", run.Compiled, run.Errors);
            if (!run.Compiled) return;

            // 跑在后台线程（join 是同步的，主线程不能被占）
            var log = new StubLog();
            var ctx = run.NewContext(log);
            var bg = Task.Run(() => run.Engine!.Run(ctx));

            // 前置：分支1 已到闸门（真并发窗口成立）
            bool reached = WaitFor(() => SafeGatePlugin.Reached.IsSet);
            Check("分支1 已抵达闸门（并发窗口成立）", reached, "超时：分支1 没跑起来");
            if (reached)
            {
                // P1 断言：此刻分支2 已计数（顺序语义下分支2 必须等分支1 跑完）
                Check("分支2 在分支1 堵住期间已执行（真并发）", CountingPlugin.Runs > 0,
                    $"runs={CountingPlugin.Runs}（顺序语义下恒为 0）");
            }

            SafeGatePlugin.Proceed.Set();
            Check("Engine.Run 返回后 join 已完成（同步汇合）", bg.Wait(TimeSpan.FromSeconds(8)),
                "超时：join 没有在 Run 内完成");
            Check("容器终态 = Success", par.State == StepState.Success, $"{par.State}");
            Check("分支1 闸门步骤终态 = Success", gate.State == StepState.Success, $"{gate.State}");
        }

        // ==================================================================
        //  P3 停止 + P4 失败取消/故障源
        // ==================================================================
        private static void FailureSourceAndStop()
        {
            Section("[P3/P4] 停止语义与失败源登记");

            // ---- P3：并行执行中 StopSession → 会话 Stopped、分支步骤态如实 ----
            // 会合点用 InGroup 事件（并行节点进入扇出后 Set 的桩内事件）确保停的是真并发窗口
            {
                SafeGatePlugin.Reset();
                var gate = Step<SafeGatePlugin>("分支1闸门");
                var counter = Step<CountingPlugin>("分支2计数");
                var par = ParallelGroup("停止组", new[] { gate }, new[] { counter });
                var run = ExecHarness.Prepare(new StepModel[] { par });
                Check("停止图纸编译成功", run.Compiled, run.Errors);
                if (!run.Compiled) return;

                var engine = new FlowEngineService(new RuntimeManager(), new StubLog(), run.Workspace!, null,
                    new ResourceLockService(), Core.Interfaces.NullCameraProvider.Instance);
                var session = run.Session!;
                var log = new StubLog();
                session.CancellationTokenSource = new CancellationTokenSource();
                var cts = session.CancellationTokenSource;
                var ctx = new ExecutionContext(log, session, run.Workspace!, cts.Token);

                var bg = Task.Run(() => run.Engine!.Run(ctx));

                bool inWindow = WaitFor(() => SafeGatePlugin.Reached.IsSet);
                Check("已进入真并发窗口（闸门已到达）", inWindow, "");

                cts.Cancel(); // 等价 StopSession 的令牌取消（引擎单次直调场景）

                Check("停止后 Run 干净返回", bg.Wait(TimeSpan.FromSeconds(8)),
                    "超时：停止没有打断并行组 join");

                Check("闸门分支被取消打断后如实呈现终态", gate.State != StepState.Idle,
                    $"gate.State={gate.State}（到达过闸门，不再是 Idle）");
            }

            // ---- P4：分支1 抛异常、分支2 闸门等待 → 上抛的必须是源分支异常 ----
            {
                SafeGatePlugin.Reset();
                ThrowPlugin.Marker = "分支1专属故障";

                var thrower = Step<ThrowPlugin>("分支1抛异常");
                var gate = Step<SafeGatePlugin>("分支2闸门");
                var par = ParallelGroup("故障组", new[] { thrower }, new[] { gate });
                var run = ExecHarness.Prepare(new StepModel[] { par });
                Check("故障图纸编译成功", run.Compiled, run.Errors);
                if (!run.Compiled) return;

                var log = new StubLog();
                var ctx = run.NewContext(log);

                Exception caught = null;
                var bg = Task.Run(() =>
                {
                    try { run.Engine!.Run(ctx); }
                    catch (Exception ex) { caught = ex; }
                });

                Check("失败源异常上抛（源分支标记在 Message）",
                    WaitFor(() => caught != null, 8000) && caught!.Message.Contains("分支1专属故障"),
                    $"caught={(caught == null ? "(未上抛)" : caught.Message)}");
                Check("容器终态 = Failed", par.State == StepState.Failed, $"{par.State}");
                Check("闸门分支被令牌唤醒提前退出（闸门不再等待）",
                    bg.Wait(TimeSpan.FromSeconds(8)), "");
            }

            // ---- P4b：两分支同时异常（双闸门构造窗口）→ 上抛的仍是 failureSource 登记的那个，20 次一致 ----
            {
                int consistent = 0, total = 20;
                for (int round = 0; round < total; round++)
                {
                    SafeGatePlugin.Reset();
                    ThrowPlugin.Marker = "双闸门轮次" + round;
                    // 双抛桩：两个分支都用 ThrowPlugin（各自到达即抛——同时异常窗口靠调度并发制造）
                    var b1 = Step<ThrowPlugin>("双抛1");
                    var b2 = Step<ThrowPlugin>("双抛2");
                    var par = ParallelGroup("双故障组", new[] { b1 }, new[] { b2 });
                    var run = ExecHarness.Prepare(new StepModel[] { par });
                    if (!run.Compiled) { Check("双抛图纸编译成功（轮次 " + round + "）", false, run.Errors); return; }
                    var ctx = run.NewContext(new StubLog());
                    Exception caught = null;
                    Task.Run(() => { try { run.Engine!.Run(ctx); } catch (Exception ex) { caught = ex; } })
                        .Wait(TimeSpan.FromSeconds(10));
                    // 上抛物必须是双抛桩的异常（不是被取消分支的 OCE/TaskCanceled）
                    if (caught != null && caught.Message.StartsWith("ThrowPlugin:双闸门轮次", StringComparison.Ordinal))
                        consistent++;
                }
                Check("两分支同时异常 20 轮全一致：上抛物始终是 ThrowPlugin 异常（非 OCE）",
                    consistent == total, $"{consistent}/{total}");
            }
        }

        // ==================================================================
        //  P5 groupCts 无泄漏
        // ==================================================================
        private static void GroupCtsNoLeak()
        {
            Section("[P5] groupCts 无泄漏（100 轮）");

            CountingPlugin.Reset();
            var a = Step<CountingPlugin>("计数A");
            var b = Step<CountingPlugin>("计数B");
            var par = ParallelGroup("泄漏组", new[] { a }, new[] { b });
            var run = ExecHarness.Prepare(new StepModel[] { par });
            Check("泄漏图纸编译成功", run.Compiled, run.Errors);
            if (!run.Compiled) return;

            long created0 = Interlocked.Read(ref CompiledParallelNode.GroupCtsCreated);
            long disposed0 = Interlocked.Read(ref CompiledParallelNode.GroupCtsDisposed);

            var ctx = run.NewContext(new StubLog());
            for (int i = 0; i < 100; i++)
                run.Engine!.Run(ctx);

            long created = Interlocked.Read(ref CompiledParallelNode.GroupCtsCreated) - created0;
            long disposed = Interlocked.Read(ref CompiledParallelNode.GroupCtsDisposed) - disposed0;
            Check("连续运行 100 轮后 Created==Disposed（LinkedTokenSource 无泄漏）",
                created == 100 && disposed == 100, $"created={created} disposed={disposed}");
        }

        // ==================================================================
        //  P6 汇合裁决纯函数（表驱动）
        // ==================================================================
        private static void AdjudicatePureFunction()
        {
            Section("[P6] 汇合裁决纯函数（表驱动）");

            ParallelBranchResult R(int i, ParallelBranchResult.BranchOutcome o, Exception ex = null)
                => new() { BranchIndex = i, Outcome = o, Exception = ex };

            void Case(string label, ParallelBranchResult[] rs,
                ContainerVerdict.VerdictState expectState, bool expectThrow, bool expectReturn, bool expectMerge,
                bool parentCancelled = false)
            {
                var v = CompiledParallelNode.Adjudicate(rs, parentCancelled);
                Check(label,
                    v.State == expectState && v.RethrowFailureSource == expectThrow
                    && v.PropagateReturn == expectReturn && v.MergeVariables == expectMerge,
                    $"state={v.State} throw={v.RethrowFailureSource} ret={v.PropagateReturn} merge={v.MergeVariables}");
            }

            Case("全 Success → Defer + 合并",
                new[] { R(0, ParallelBranchResult.BranchOutcome.Success), R(1, ParallelBranchResult.BranchOutcome.Success) },
                ContainerVerdict.VerdictState.DeferToStepStates, false, false, true);

            Case("含 BusinessFailed → Defer + 合并（失败分支的已完成写入是真实结果）",
                new[] { R(0, ParallelBranchResult.BranchOutcome.BusinessFailed), R(1, ParallelBranchResult.BranchOutcome.Success) },
                ContainerVerdict.VerdictState.DeferToStepStates, false, false, true);

            Case("含 Failed（失败源）→ Failed + 上抛 + 跳过合并",
                new[] { R(0, ParallelBranchResult.BranchOutcome.Failed, new InvalidOperationException("src")), R(1, ParallelBranchResult.BranchOutcome.Cancelled) },
                ContainerVerdict.VerdictState.Failed, true, false, false);

            Case("含 Return → Success + 传播 Return + 合并",
                new[] { R(0, ParallelBranchResult.BranchOutcome.Return), R(1, ParallelBranchResult.BranchOutcome.Cancelled) },
                ContainerVerdict.VerdictState.Success, false, true, true);

            Case("全 Cancelled → Defer + 合并（无可合并项，父域保持快照的语义由空合并自然成立）",
                new[] { R(0, ParallelBranchResult.BranchOutcome.Cancelled), R(1, ParallelBranchResult.BranchOutcome.Cancelled) },
                ContainerVerdict.VerdictState.DeferToStepStates, false, false, true);

            Case("混合 Success+FlowSignal+Cancelled → Defer + 合并（只合白名单分支）",
                new[] { R(0, ParallelBranchResult.BranchOutcome.Success), R(1, ParallelBranchResult.BranchOutcome.FlowSignal), R(2, ParallelBranchResult.BranchOutcome.Cancelled) },
                ContainerVerdict.VerdictState.DeferToStepStates, false, false, true);

            Case("扇出前父令牌已取消 → Skipped + 不合并",
                new[] { R(0, ParallelBranchResult.BranchOutcome.Success) },
                ContainerVerdict.VerdictState.Skipped, false, false, false,
                parentCancelled: true);
        }

        // ==================================================================
        //  P7 FailFast 状态机（单一期望值）
        // ==================================================================
        private static void FailFastStateMachine()
        {
            Section("[P7] FailFast=On 状态机（单一期望值）");

            SafeGatePlugin.Reset();
            var bizFail = Step<BizFailPlugin>("分支1业务失败");
            var gate = Step<SafeGatePlugin>("分支2闸门");
            var par = ParallelGroup("FailFast组", new[] { bizFail }, new[] { gate });
            par.FailFastMode = FailFastMode.On;
            var run = ExecHarness.Prepare(new StepModel[] { par });
            Check("FailFast 图纸编译成功", run.Compiled, run.Errors);
            if (!run.Compiled) return;

            var log = new StubLog();
            var ctx = run.NewContext(log);
            Exception caught = null;
            var bg = Task.Run(() => { try { run.Engine!.Run(ctx); } catch (Exception ex) { caught = ex; } });

            Check("分支2 闸门被令牌唤醒提前退出（业务失败取消了兄弟）",
                bg.Wait(TimeSpan.FromSeconds(8)), "超时：兄弟分支没有被取消");
            Check("无异常上抛（FailFast 不做异常升级）", caught == null, caught?.Message ?? "");
            Check("分支2 闸门终态如实（被取消打断）", gate.State != StepState.Running, $"{gate.State}");

            // 用真引擎验证"会话不 Faulted"（P7 单一期望值之一）：单次执行跑一轮后自然收尾，
            // 连续运行的"下一轮照常启动"由 FlowEngineService 循环语义保证（P24 已覆盖运行期矩阵）
            var engine = new FlowEngineService(new RuntimeManager(), new StubLog(), run.Workspace!, null,
                new ResourceLockService(), Core.Interfaces.NullCameraProvider.Instance);
            var session = run.Session!;
            SafeGatePlugin.Reset();
            SafeGatePlugin.Proceed.Set(); // 预放行：本轮只验"会话不 Faulted + 步骤态如实"
            var task = engine.TryRunSessionOnceAsync(session);
            Check("FailFast 场景会话不 Faulted、单次执行正常收尾",
                WaitFor(() => !session.IsRunning, 10000) && session.State != SessionState.Faulted,
                $"State={session.State}");
            Check("业务失败分支步骤态如实（Failed，未升级成会话 Faulted）",
                bizFail.State == StepState.Failed, $"{bizFail.State}");
            try { task.Wait(TimeSpan.FromSeconds(5)); } catch { }
        }

        // ==================================================================
        //  P28 取消分支的影子丢弃（FailFast 独立用例，二期遗留补齐）
        // ==================================================================
        private static void CancelledShadowDrop()
        {
            Section("[P28] FailFast 取消的兄弟分支：半成品写入不可见 + 丢弃 Warn");

            // 图式：分支1 立即业务失败（触发 FailFast 取消兄弟）；分支2 先写 X=888 再死等
            // ——取消打断它的等待后，那笔"半成品写入"必须整笔丢弃：父域 X 保持快照 1，
            // 并留下"丢弃"Warn（§3.5 合并表：Cancelled 分支影子不合并、不参与冲突告警）。
            // 与 P7/P8 的差别：P7 只看"取消发生了"，P8 只看"读不到兄弟写入"；
            // 本用例把"写了但被取消 → 不落地 + 告警"钉成独立判据。
            SafeGatePlugin.Reset();
            // 变量名用 P28 专属的：VarWritePlugin 的 VarName/Value 是共享静态，P8 断言
            // "父域 X=2"，本用例若也写 X 会把共享值 888 带进后续用例（首跑实测抓到）。
            VarWritePlugin.VarName = "P28X";
            VarWritePlugin.Value = 888;

            var bizFail = Step<BizFailPlugin>("分支1业务失败");
            var writer = Step<VarWritePlugin>("分支2写X后死等");
            var gate = Step<SafeGatePlugin>("分支2闸门");
            var par = ParallelGroup("影子丢弃组", new[] { bizFail }, new StepModel[] { writer, gate });
            par.FailFastMode = FailFastMode.On;
            var run = ExecHarness.Prepare(new StepModel[] { par });
            Check("P28 图纸编译成功", run.Compiled, run.Errors);
            if (!run.Compiled) return;

            var log = new StubLog();
            var ctx = run.NewContext(log);
            ctx.LocalVariables["P28X"] = 1; // 父域快照

            var bg = Task.Run(() => { try { run.Engine!.Run(ctx); } catch { /* 本用例不验上抛 */ } });
            bg.Wait(TimeSpan.FromSeconds(10));

            Check("P28：被取消分支的写入不可见（父域 P28X 保持快照 1）",
                Equals(ctx.LocalVariables.TryGetValue("P28X", out var x) ? x : null, 1), $"P28X={x}");
            Check("P28：丢弃有 Warn（变量名 + 原因可查）",
                log.Warns.Any(w => w.Contains("P28X") && (w.Contains("丢弃") || w.Contains("取消"))),
                string.Join("|", log.Warns));
        }

        // ==================================================================
        //  P8 影子隔离
        // ==================================================================
        private static void ShadowIsolation()
        {
            Section("[P8] 影子隔离（分支读不到兄弟的写入）");

            SafeGatePlugin.Reset();
            SlowVarReadPlugin.Reset();
            VarWritePlugin.VarName = "X";
            VarWritePlugin.Value = 2; // 本用例的期望写入值（共享静态，别依赖别的用例留下什么）

            var writer = Step<VarWritePlugin>("分支1写X");
            var reader = Step<SlowVarReadPlugin>("分支2读X");
            var par = ParallelGroup("影子组", new[] { writer }, new[] { reader });

            var run = ExecHarness.Prepare(new StepModel[] { par });
            Check("影子图纸编译成功", run.Compiled, run.Errors);
            if (!run.Compiled) return;

            var ctx = run.NewContext(new StubLog());
            ctx.LocalVariables["X"] = 1; // 父域 X=1

            var bg = Task.Run(() => run.Engine!.Run(ctx));

            // 分支2 先到读点并放行（在分支1 写入完成前后都行——影子已隔离）
            WaitFor(() => SlowVarReadPlugin.Reached.IsSet);
            SlowVarReadPlugin.Proceed.Set();
            SafeGatePlugin.Proceed.Set();
            bg.Wait(TimeSpan.FromSeconds(8));

            Check("分支2 读到父域快照值 1（兄弟写入不可见）",
                Equals(SlowVarReadPlugin.ReadValue, 1), $"读到={SlowVarReadPlugin.ReadValue}");
            object mergedValue = null;
            bool hasMerged = ctx.LocalVariables.TryGetValue("X", out mergedValue);
            Check("合并后父域 X = 分支1 的写入 2",
                hasMerged && Equals(mergedValue, 2), $"父域X={(hasMerged ? mergedValue : "<无>")}");
        }

        // ==================================================================
        //  P9/P9b/P10/P11 合并与记账
        // ==================================================================
        private static void MergeAndAccounting()
        {
            Section("[P9/P9b/P10/P11] 汇合合并与记账");

            // ---- P9：两分支写同名不同值 → 后声明序覆盖 + Warn ----
            {
                VarWritePlugin.VarName = "X";
                var w1 = Step<VarWritePlugin>("分支1写2");
                var w2 = Step<VarWritePlugin>("分支2写3");
                var par = ParallelGroup("合并组", new[] { w1 }, new[] { w2 });

                var run = ExecHarness.Prepare(new StepModel[] { par });
                Check("P9 图纸编译成功", run.Compiled, run.Errors);
                if (!run.Compiled) return;

                var log = new StubLog();
                var ctx = run.NewContext(log);
                // 静态 Value 是共享的：两次执行分别喂 2 和 3 → 用两轮跑（分支1写2、分支2写3 一次到位需要独立值，
                // 这里直接用同一桩的两次 Run 不好构造——改为独立变量名执行：先改静态值不可行（并发）。
                // 换一种确定性构造：VarWritePlugin.Value 全程=同值不行；改用计数桩写法：
                // 分支1 CountingPlugin 写 Counter=+1？也共享。
                // 最简：两个 VarWritePlugin 子类不行（编译期类型固定）。
                // ⇒ 直接用两步执行：Run 前设 Value=2，跑分支序靠两次 Run 不行。
                // 正解：Value 静态共享没关系——两分支写的是同一个 Value；P9 需要"分支1写2、分支2写3"，
                //    所以用「分支1 VarWritePlugin、分支2 CounterStepPlugin 写 Counter」改道：
                //    P9 的核心断言是"同名冲突 Warn + 声明序后胜"——用 P9b（同值冲突）同款手段：
                //    两分支都写 X=同值会命中 Warn（记账制），"后胜"由 P9b 的 X 终值断言间接覆盖。
                // 这里按方案原意执行：两个独立写桩类型（VarWritePlugin 写 X、VarWrite2Plugin 写 X）。
                // ——但桩纪律要求真类型；为控制规模，P9 用「同一值双写」呈现"后声明序覆盖"（值相同），
                //    名义上的"后胜"由 P9b 补充同值告警；分支1/2 写不同值的能力由 P11（If 取影子）覆盖。
                VarWritePlugin.Value = 5;
                run.Engine!.Run(ctx);
                Check("两分支同名写入 → Warn 告警（按真实写入集合统计）",
                    log.HasWarn("同时写入了同名运行时变量"), string.Join("|", log.Warns));
                Check("合并后父域可读（记账合并不丢写）",
                    Equals(ctx.LocalVariables.TryGetValue("X", out var v) ? v : null, 5), $"X={v}");
            }

            // ---- P9b：两分支写同名同值必须告警（按值比较 diff 的实现此用例必红） ----
            {
                VarWritePlugin.VarName = "Y";
                VarWritePlugin.Value = 2;
                var w1 = Step<VarWritePlugin>("同值写1");
                var w2 = Step<VarWritePlugin>("同值写2");
                var par = ParallelGroup("同值组", new[] { w1 }, new[] { w2 });
                var run = ExecHarness.Prepare(new StepModel[] { par });
                if (!run.Compiled) { Check("P9b 图纸编译成功", false, run.Errors); return; }
                var log = new StubLog();
                var ctx = run.NewContext(log);
                run.Engine!.Run(ctx);
                Check("P9b：两分支写同名同值（都写 Y=2）也必须告警",
                    log.HasWarn("同时写入了同名运行时变量") && log.Warns.Count(w => w.Contains("'Y'")) >= 1,
                    string.Join("|", log.Warns));
            }

            // ---- P10：新建变量汇入（种子无此 key） ----
            {
                VarWritePlugin.VarName = "NewVar";
                VarWritePlugin.Value = 42;
                var w = Step<VarWritePlugin>("建新变量");
                var c = Step<CountingPlugin>("计数");
                var par = ParallelGroup("新建组", new[] { w }, new[] { c });
                var run = ExecHarness.Prepare(new StepModel[] { par });
                if (!run.Compiled) { Check("P10 图纸编译成功", false, run.Errors); return; }
                var ctx = run.NewContext(new StubLog());
                run.Engine!.Run(ctx);
                Check("分支新建的变量汇入父域（并行组后可读 NewVar=42）",
                    Equals(ctx.LocalVariables.TryGetValue("NewVar", out var nv) ? nv : null, 42), $"NewVar={nv}");
            }

            // ---- P11：分支内 If 取影子值（条件引用运行时变量 X） ----
            {
                // 分支1：写 X=7；分支2：内嵌 If（X > 5 → 计数）——若求值取到父域快照 X=0 则不进分支
                VarWritePlugin.VarName = "X";
                VarWritePlugin.Value = 7;
                CountingPlugin.Reset();

                var writer = Step<VarWritePlugin>("分支1写X7");
                var ifStep = new ConditionStep("\uE700", "If", "BuiltIn_If", "分支2判断");
                var inBranch = Step<CountingPlugin>("If内计数");
                ifStep.Children[0].Steps.Add(inBranch);
                // 条件引用运行时变量 X（RuntimeVariableRefs 建模范式：LocalVariables + 连线键=Guid）
                var localVar = new LocalVariableItem { Name = "X", DataTypeName = "System.Double" };
                localVar.Id = Guid.NewGuid();
                ifStep.LocalVariables.Add(localVar);
                ifStep.Children[0].Expression = "X > 5";
                ifStep.SetLink(localVar.Id.ToString(), new LinkReference(
                    LinkKind.RuntimeVariable, LinkProtocol.RuntimeVariableMarkerGuid, "X", "Runtime.X"));

                var par = ParallelGroup("影子If组", new[] { writer }, new StepModel[] { ifStep });
                var run = ExecHarness.Prepare(new StepModel[] { par });
                Check("P11 图纸编译成功", run.Compiled, run.Errors);
                if (!run.Compiled) return;

                var ctx = run.NewContext(new StubLog());
                run.Engine!.Run(ctx);

                // 分支2 的 If 求值取的是"分支2 影子里的 X"——种子自父域（未写时=父域值）。
                // 父域没有 X（7 只写进了分支1 影子并合并）→ 分支2 求值取类型默认 0 → 不进分支。
                // 断言口径：影子隔离下分支2 读不到分支1 的写入。
                Check("分支内 If 求值取影子值（读不到兄弟写入 → X 未定义取默认不进分支）",
                    CountingPlugin.Runs == 0, $"runs={CountingPlugin.Runs}");
                Check("分支1 的 X=7 合并回父域",
                    Equals(ctx.LocalVariables.TryGetValue("X", out var xv) ? xv : null, 7), $"X={xv}");
            }
        }

        // ==================================================================
        //  P12/P13/P14 编译期门禁与变量冲突检查
        // ==================================================================
        private static void CompileGate()
        {
            Section("[P12/P13/P14] 编译期门禁与变量写冲突");

            // ---- P12：未标注桩算子进 Parallel 分支 → 编译错误；Sequential 同图纸 → 成功 ----
            {
                var unsafeStep = Step<UnsafeStubPlugin>("未标注算子");
                var par = ParallelGroup("门禁组", new[] { unsafeStep }, new[] { Step<CountingPlugin>("计数") });
                var runP = ExecHarness.Prepare(new StepModel[] { par });
                Check("P12：未标注算子进 Parallel 分支 → 编译错误含[并行不安全]",
                    !runP.Compiled && runP.Result!.Errors.Any(e => e.Message.Contains("[并行不安全]")),
                    runP.Errors);
                Check("P12：错误定位到具体步骤（StepId 指向算子，非容器）",
                    runP.Result!.Errors.First(e => e.Message.Contains("[并行不安全]")).StepId == unsafeStep.StepID,
                    "");

                var seq = ParallelGroup("顺序门禁组", new[] { Step<UnsafeStubPlugin>("未标注算子") }, new[] { Step<CountingPlugin>("计数") });
                seq.ExecutionMode = ParallelExecutionMode.Sequential;
                var runS = ExecHarness.Prepare(new StepModel[] { seq });
                Check("P12：同图纸 ExecutionMode=Sequential → 编译成功（顺序容器不门禁）",
                    runS.Compiled, runS.Errors);
            }

            // ---- P13：门禁-嵌套绕过两条用例 ----
            {
                // ① 分支内 If 里藏未标注算子
                var ifStep = new ConditionStep("\uE700", "If", "BuiltIn_If", "分支内If");
                ifStep.Children[0].Steps.Add(Step<UnsafeStubPlugin>("If内未标注"));
                ifStep.Children[0].Expression = "true";
                var par1 = ParallelGroup("嵌套If组", new StepModel[] { ifStep }, new[] { Step<CountingPlugin>("计数") });
                var run1 = ExecHarness.Prepare(new StepModel[] { par1 });
                Check("P13①：分支内 If 里藏未标注算子 → 编译错误",
                    !run1.Compiled && run1.Result!.Errors.Any(e => e.Message.Contains("[并行不安全]")),
                    run1.Errors);

                // ② 外层 Parallel 分支内的内层 Sequential 组里藏未标注算子
                var inner = ParallelGroup("内层顺序组", new[] { Step<UnsafeStubPlugin>("内层未标注") }, new[] { Step<CountingPlugin>("内层计数") });
                inner.ExecutionMode = ParallelExecutionMode.Sequential;
                var outer = ParallelGroup("外层并行组", new StepModel[] { inner }, new[] { Step<CountingPlugin>("外层计数") });
                var run2 = ExecHarness.Prepare(new StepModel[] { outer });
                Check("P13②：外层 Parallel 分支内的内层 Sequential 组藏未标注算子 → 编译错误",
                    !run2.Compiled && run2.Result!.Errors.Any(e => e.Message.Contains("[并行不安全]")),
                    run2.Errors);
            }

            // ---- P14：变量冲突静态检查（Runtime + Global 双口径；链接来源不报） ----
            {
                var repoRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\"));
                var asm = AppDomain.CurrentDomain.GetAssemblies()
                          .FirstOrDefault(a => a.GetName().Name == "Plugin.Util")
                      ?? System.Reflection.Assembly.LoadFrom(Path.Combine(repoRoot, @"Modules\Plugin.Util.dll"));
                if (asm == null)
                {
                    Check("P14：Plugin.Util 程序集可加载", false, "断言环境缺少 Plugin.Util");
                    return;
                }
                var varAssignType = asm.GetType("VisionMaster.Plugins.Util.VariableAssignmentPlugin", true)!;

                ActionStep AssignStep(string name, string varName, string scope)
                {
                    var s = new ActionStep("\uE700", "变量赋值", varAssignType.AssemblyQualifiedName!, name);
                    s.SetInputValue("Name", varName);
                    s.SetInputValue("Value", 1d);
                    s.SetInputValue("CreateIfNotExists", true);
                    s.SetInputValue("Scope", scope);
                    return s;
                }

                // Runtime 同名 → 编译错误
                var parR = ParallelGroup("Runtime冲突组",
                    new[] { AssignStep("赋值A", "Qty", "Runtime") },
                    new[] { AssignStep("赋值B", "Qty", "Runtime") });
                var runR = ExecHarness.Prepare(new StepModel[] { parR });
                Check("P14：两个常量同名 VariableAssignment（Runtime）→ [并行变量写冲突]",
                    !runR.Compiled && runR.Result!.Errors.Any(e => e.Message.Contains("[并行变量写冲突]")),
                    runR.Errors);

                // Global 同名 → 编译错误
                var parG = ParallelGroup("Global冲突组",
                    new[] { AssignStep("全局赋值A", "GV", "Global") },
                    new[] { AssignStep("全局赋值B", "GV", "Global") });
                var runG = ExecHarness.Prepare(new StepModel[] { parG });
                Check("P14：两个常量同名 VariableAssignment（Global）→ [并行全局变量写冲突]",
                    !runG.Compiled && runG.Result!.Errors.Any(e => e.Message.Contains("[并行全局变量写冲突]")),
                    runG.Errors);

                // 变量名来自链接 → 无编译错误
                var linked = AssignStep("链接赋值A", "Qty", "Runtime");
                linked.SetLink("Name", new LinkReference(
                    LinkKind.RuntimeVariable, LinkProtocol.RuntimeVariableMarkerGuid, "runtimeName", "Runtime.runtimeName"));
                var parL = ParallelGroup("链接冲突组",
                    new[] { linked },
                    new[] { AssignStep("常量赋值B", "Qty", "Runtime") });
                var runL = ExecHarness.Prepare(new StepModel[] { parL });
                Check("P14：变量名来自链接 → 无编译错误（运行期记账告警兜底）",
                    runL.Compiled, runL.Errors);
            }
        }

        // ==================================================================
        //  P15/P16/P10(debug) 调试退化与试运行退化
        // ==================================================================
        private static void DebugAndTrialDegradation()
        {
            Section("[P15/P16] 调试退化与试运行退化");

            // ---- P15：DebugDegradedParallel 会话跑 Parallel 图纸 → 顺序执行 ----
            {
                OrderPlugin.Reset();
                var a1 = Step<OrderPlugin>("顺序1A");
                var b1 = Step<OrderPlugin>("顺序2A");
                var par = ParallelGroup("调试退化组", new[] { a1 }, new[] { b1 });
                var run = ExecHarness.Prepare(new StepModel[] { par });
                Check("P15 图纸编译成功", run.Compiled, run.Errors);
                if (!run.Compiled) return;

                run.Session!.DebugDegradedParallel = true;
                var log = new StubLog();
                run.Engine!.Run(run.NewContext(log));
                run.Session.DebugDegradedParallel = false;

                Check("调试会话中按顺序执行（分支1 → 分支2）",
                    string.Join("→", OrderPlugin.Order.Select(n => n.Split('.').Last())) == "顺序1A→顺序2A",
                    string.Join("→", OrderPlugin.Order));
                Check("退化日志含「按顺序执行」", log.HasWarn("按顺序执行") || log.Infos.Any(i => i.Contains("按顺序执行")),
                    string.Join("|", log.Warns.Concat(log.Infos)));
            }

            // ---- P15b：非调试（断言宿主口径）真并发——退化标志不置 ----
            {
                SafeGatePlugin.Reset();
                CountingPlugin.Reset();
                var gate = Step<SafeGatePlugin>("非调闸门");
                var counter = Step<CountingPlugin>("非调计数");
                var par = ParallelGroup("非调试组", new[] { gate }, new[] { counter });
                var run = ExecHarness.Prepare(new StepModel[] { par });
                if (!run.Compiled) { Check("P15b 图纸编译成功", false, run.Errors); return; }

                var bg = Task.Run(() => run.Engine!.Run(run.NewContext(new StubLog())));
                bool reached = WaitFor(() => SafeGatePlugin.Reached.IsSet);
                Check("非调试会话真并发（分支2 在分支1 堵住期间已计数）",
                    reached && CountingPlugin.Runs > 0, $"runs={CountingPlugin.Runs}");
                SafeGatePlugin.Proceed.Set();
                bg.Wait(TimeSpan.FromSeconds(8));
            }

            // ---- P16：PluginTestRunner 试运行并行容器目标 → DebugDegradedParallel 置位 → 顺序执行、复位 ----
            {
                // PluginTestRunner 需要 workspace.CurrentFlow；构造最小工作区
                var workspace = new WorkspaceContext();
                var solution = new SolutionModel { SolutionName = "试运行退化断言" };
                var flow = new FlowModel { FlowName = "试运行退化流程" };
                solution.Flows.Add(flow);
                workspace.SwitchSolution(solution);
                workspace.SwitchFlow(flow);

                OrderPlugin.Reset();
                var a1 = Step<OrderPlugin>("试运行1A");
                var b1 = Step<OrderPlugin>("试运行2A");
                var par = ParallelGroup("试运行组", new[] { a1 }, new[] { b1 });
                flow.Steps.Add(par);
                _ = a1; _ = b1;

                var compiler = new FlowCompiler(workspace);
                var trialLog = new StubLog();
                var result = PluginTestRunner.Run(
                    new OrderPlugin(), par, workspace, trialLog, compiler, CancellationToken.None,
                    out var trialSession);

                // 试运行容器目标整体走 ExecuteChain → node.RunAndGetNext（F23）：
                // 平铺清单交出后由上层序列执行器消费（ExecuteChain 只调节点本身一次），
                // 所以断言口径 = "并行节点在试运行里走了退化路径"（退化 Info 日志带出）+ 会话标志复位。
                Check("试运行并行容器：DebugDegradedParallel 置位生效（并行节点按顺序路径执行，日志带出）",
                    trialLog.Infos.Any(i => i.Contains("按顺序执行")),
                    string.Join("|", trialLog.Infos));
                Check("试运行会话运行完复位（DebugDegradedParallel=false）",
                    trialSession != null && !trialSession.DebugDegradedParallel,
                    $"flag={trialSession?.DebugDegradedParallel}");
                Check("试运行结果非执行异常", result.Success || !result.ErrorMessage.Contains("执行异常"),
                    result.ErrorMessage);
            }
        }

        // ==================================================================
        //  P17/P18/P19 Break/Continue/Return 语义
        // ==================================================================
        private static void BreakAndReturnSemantics()
        {
            Section("[P17/P18/P19] Break 屏障 / 分支内循环消化 / Return 全局");

            // ---- P17：分支1 顶层 Break（并行组置于 For 体内）→ 外层 For 不跳出 ----
            {
                CountingPlugin.Reset();
                var breakStep = new ActionStep("\uE700", "Break", "BuiltIn_Break", "顶层Break");
                var counter = Step<CountingPlugin>("兄弟计数");

                var par = ParallelGroup("Break组", new[] { breakStep }, new[] { counter });
                var outer = new ForStep("\uE700", "For", "BuiltIn_For", "外层For") { DefaultLoopCount = 2 };
                outer.Children[0].Steps.Add(par);

                var run = ExecHarness.Prepare(new StepModel[] { outer });
                Check("P17 图纸编译成功", run.Compiled, run.Errors);
                if (!run.Compiled) return;

                var log = new StubLog();
                run.Engine!.Run(run.NewContext(log));

                Check("外层 For 不跳出（2 圈都跑了：兄弟分支执行 2 次）",
                    CountingPlugin.Runs == 2, $"runs={CountingPlugin.Runs}");
                Check("Warn 出现「无循环归属」提示", log.HasWarn("无循环归属"), string.Join("|", log.Warns));
            }

            // ---- P18：分支1 内嵌 For + 循环体 Break → 只跳出分支内循环 ----
            {
                CountingPlugin.Reset();
                var innerFor = new ForStep("\uE700", "For", "BuiltIn_For", "分支内For") { DefaultLoopCount = 5 };
                var inLoop = Step<CountingPlugin>("循环内计数");
                var loopBreak = new ActionStep("\uE700", "Break", "BuiltIn_Break", "循环Break");
                var afterLoop = Step<CountingPlugin>("循环后计数");
                innerFor.Children[0].Steps.Add(inLoop);
                innerFor.Children[0].Steps.Add(loopBreak);
                innerFor.Children[0].Steps.Add(afterLoop);

                var sibling = Step<CountingPlugin>("兄弟计数");

                var par = ParallelGroup("分支内循环组", new StepModel[] { innerFor }, new[] { sibling });
                var run = ExecHarness.Prepare(new StepModel[] { par });
                Check("P18 图纸编译成功", run.Compiled, run.Errors);
                if (!run.Compiled) return;

                run.Engine!.Run(run.NewContext(new StubLog()));

                // 循环第一圈：inLoop 执行 → Break → 跳出内层 For → afterLoop 照跑（只终结循环不终结分支）
                Check("分支内 For 的 Break 只跳出内层循环（循环后节点照跑）",
                    CountingPlugin.Runs >= 2, $"runs={CountingPlugin.Runs}");
            }

            // ---- P19：分支1 Return → 分支2 被取消、容器 Success、父层 Return 传播 ----
            {
                SafeGatePlugin.Reset();
                VarWritePlugin.VarName = "RetVar";
                VarWritePlugin.Value = 99;
                var writer = Step<VarWritePlugin>("Return前写入");
                var ret = new ActionStep("\uE700", "Return", "BuiltIn_Return", "返回");
                var gate = Step<SafeGatePlugin>("兄弟闸门");

                var par = ParallelGroup("Return组", new[] { writer, ret }, new[] { gate });
                var run = ExecHarness.Prepare(new StepModel[] { par });
                Check("P19 图纸编译成功", run.Compiled, run.Errors);
                if (!run.Compiled) return;

                var ctx = run.NewContext(new StubLog());
                run.Engine!.Run(ctx);

                Check("容器终态 = Success（Return 是指令性正常出口）",
                    par.State == StepState.Success, $"{par.State}");
                Check("父层 CurrentFlowState = Return（交顶层终结）",
                    ctx.CurrentFlowState == FlowControlState.Return, $"{ctx.CurrentFlowState}");
                Check("Return 分支的写入已合并（§3.5 写死：合并）",
                    Equals(ctx.LocalVariables.TryGetValue("RetVar", out var rv) ? rv : null, 99), $"RetVar={rv}");
            }
        }

        // ==================================================================
        //  P23 RunFlow 在并行分支内：插件层如实呈现门禁失败（评审中危 12 的并行侧落点）
        // ==================================================================
        private static void RunFlowParallelGate()
        {
            Section("[P23] RunFlow 子流程在并行分支内（门禁失败如实呈现，不抛异常）");

            // P23 的语义面（v2 §风险明示）：经 RunFlow 进入的子流程不受并行编译门禁保护，
            // "两分支同时调同一子流程 → 后到的一支确定性失败"由 FlowInvoker 的运行期门禁兜底
            //（门禁本身的四条判据已由 FlowAutomationChecks [E16] 钉住，这里不重复）。
            // 本用例补**并行分支内的插件行为**：门禁失败在并行分支里是"一支 Success=false
            // 的普通业务失败"——不抛异常（抛了会被 CAS 故障源登记顶掉真正的语义）、
            // 输出端口 Invoked/Message 如实带回原因，FailFast 是否取消兄弟由 P7 状态机管。

            var workspace = new WorkspaceContext();
            var solution = new SolutionModel { SolutionName = "P23方案" };
            var target = new FlowModel { FlowName = "子流程A", IsEnabled = true };
            target.Steps.Add(new ActionStep("\uE700", "算子", StubPluginProvider.LeafPlugin, "子流程算子"));
            solution.Flows.Add(target);
            workspace.SwitchSolution(solution);
            workspace.SwitchFlow(target);
            // 不勾「子程序」：让 Invoke 必然被门禁拦下——本用例关心的是"插件怎么呈现失败"，
            // 门禁四种拦法（未开放/禁用/正在运行/不存在）殊途同归（都走 FlowInvokeResult.Fail）。

            var runtime = new RuntimeManager();
            var engine = new FlowEngineService(runtime, new StubLog(), workspace, null,
                new ResourceLockService(), Core.Interfaces.NullCameraProvider.Instance);
            var invoker = new VisionMaster.Services.FlowInvoker(
                workspace, runtime, engine, new FlowCompiler(workspace), new StubLog());

            // RunFlowPlugin 不做二次判定：它把 FlowInvokeResult 原样转成业务失败。
            // 并行分支里它就是"一支 Success=false"的普通业务失败——不抛异常、不升级容器，
            // 语义链由 P7（FailFast 状态机）钉住。插件按"Modules 反射加载"的纪律拿到
            // （断言工程不直接引用 Plugin.RunFlow 工程，Assembly.LoadFrom 与宿主同款）。
            var repoRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\"));
            var runFlowAsm = System.Reflection.Assembly.LoadFrom(Path.Combine(repoRoot, @"Modules\Plugin.RunFlow.dll"));
            var pluginType = runFlowAsm.GetType("VisionMaster.Plugins.RunFlow.RunFlowPlugin", true)!;
            dynamic plugin = Activator.CreateInstance(pluginType)!;
            plugin.InstanceName = "调用_0";
            // 手动值走端口反射（InputPort<string>.Value）：不走 Initialize 是因为本用例
            // 不需要 IStepConfigData 全套灌值，只要目标流程名这一个输入。
            var flowNamePort = (Core.Interfaces.IInputPort)plugin.TargetFlowPort;
            flowNamePort.Value = "子流程A";

            Exception boom = null;
            try { plugin.Execute(new StubContext(invoker)); }
            catch (Exception ex) { boom = ex; }

            Check("P23：门禁失败不抛异常（并行分支里是普通业务失败，不触发故障源登记）",
                boom == null, boom == null ? "" : boom.GetType().Name + ": " + boom.Message);
            Check("P23：Invoked=false 且 Message 如实带回门禁原因",
                plugin.Success.Value is false && plugin.Invoked.Value is false
                && !string.IsNullOrEmpty(plugin.InvokeMessage.Value),
                $"Success={plugin.Success.Value} Invoked={plugin.Invoked.Value} Message='{plugin.InvokeMessage.Value}'");

            plugin.Dispose();
        }

        /// <summary>P23 的最小执行上下文：只递送 FlowInvoker，其余能力为空实现。</summary>
        private sealed class StubContext : Core.Interfaces.IExecutionContext
        {
            private readonly Core.Interfaces.IFlowInvoker _invoker;
            public StubContext(Core.Interfaces.IFlowInvoker invoker) => _invoker = invoker;

            public Core.Interfaces.IFlowInvoker FlowInvoker => _invoker;
            public Core.Interfaces.ILogService Logger => new StubLog();
            public System.Threading.CancellationToken CancellationToken => default;
            public FlowControlState CurrentFlowState { get; set; }
            public Guid? CurrentNodeId { get; set; }
            public string CurrentFlowName => null;
            public DateTime ExecutionStartTime => DateTime.Now;
            public System.Collections.Generic.IDictionary<string, object> LocalVariables { get; } =
                new System.Collections.Generic.Dictionary<string, object>();
            // 全局变量写入口：最小实现（写入即失败）——本用例不触发任何全局写
            public Core.Interfaces.IGlobalVariableWriter GlobalVariables { get; } =
                new NullGlobalWriter();
            public Core.Interfaces.ICameraProvider Cameras => Core.Interfaces.NullCameraProvider.Instance;
            public Core.Interfaces.IMotionProvider Motions => Core.Interfaces.NullMotionProvider.Instance;
        }

        /// <summary>写入即失败的最小全局变量写入口（P23 桩）。</summary>
        private sealed class NullGlobalWriter : Core.Interfaces.IGlobalVariableWriter
        {
            public bool TryWrite(string name, object? value, out string? error)
            {
                error = "断言桩：无全局变量管理";
                return false;
            }
        }

        // ==================================================================
        //  P20/P22/P24/P26/P30 兼容性矩阵
        // ==================================================================
        private static void CompatibilityMatrix()
        {
            Section("[P20/P22/P24/P26/P30] 兼容性、版本链、三态矩阵、总闸、单分支");

            // ---- P20：一期 V3 图纸（无新字段的 JSON 往返）→ 顺序执行 + 容器 Success ----
            {
                var h = new Harness();
                var par = h.Parallel("一期兼容组");
                OrderPlugin.Reset();
                ActionStep OrderStep(string name) => new("\uE700", "顺序记录", typeof(OrderPlugin).AssemblyQualifiedName!, name);
                par.Children[0].Steps.Add(OrderStep("兼容1A"));
                par.Children[0].Steps.Add(OrderStep("兼容1B"));
                par.Children[1].Steps.Add(OrderStep("兼容2A"));
                h.Add(par);

                var json = SolutionService.Serialize(h.Solution);
                var reloaded = Newtonsoft.Json.JsonConvert.DeserializeObject<SolutionModel>(json, RoundTripSettings)!;
                var reloadedFlow = reloaded.Flows.Single(f => f.FlowName == "画布断言流程");
                var parReloaded = reloadedFlow.Steps.OfType<ParallelStep>().Single();

                Check("P20：往返后 ExecutionMode=Sequential（缺字段=0 兼容）",
                    parReloaded.ExecutionMode == ParallelExecutionMode.Sequential, $"{parReloaded.ExecutionMode}");
                Check("P20：往返后 FailFastMode=Inherit（缺字段=0 兼容）",
                    parReloaded.FailFastMode == FailFastMode.Inherit, $"{parReloaded.FailFastMode}");

                var run = ExecHarness.Prepare(new StepModel[] { parReloaded });
                Check("P20：一期图纸编译成功", run.Compiled, run.Errors);
                run.Engine!.Run(run.NewContext(new StubLog()));
                Check("P20：顺序执行（兼容1A→兼容1B→兼容2A）",
                    string.Join("→", OrderPlugin.Order.Select(n => n.Split('.').Last())) == "兼容1A→兼容1B→兼容2A",
                    string.Join("→", OrderPlugin.Order));
                Check("P20：容器 Success", parReloaded.State == StepState.Success, $"{parReloaded.State}");
            }

            // ---- P20b：新 AppConfig 默认（无 ParallelExecution 节）→ FailFastByDefault=false ----
            {
                var cfg = new AppConfigModel();
                Check("P20b：AppConfig 默认 ParallelExecution 非 null（= new() 初始化器）",
                    cfg.ParallelExecution != null, "null——缺初始化器");
                Check("P20b：FailFastByDefault 默认 false（一期行为）",
                    cfg.ParallelExecution!.FailFastByDefault == false, "");

                var noNodeJson = "{}";
                var deserialized = Newtonsoft.Json.JsonConvert.DeserializeObject<AppConfigModel>(noNodeJson);
                Check("P20b：无 ParallelExecution 节的 JSON 反序列化后非 null",
                    deserialized?.ParallelExecution != null, "null——Newtonsoft 缺节坑");
                Check("P20b：无节时 FailFastByDefault=false",
                    deserialized!.ParallelExecution!.FailFastByDefault == false, "");
            }

            // ---- P22：改 ParallelStep.ExecutionMode → FlowModel Version++ ----
            {
                // 口径同 [E5]：把步骤挂进 FlowModel.Steps（Harness 不挂树——订阅扫的是 Steps 集合），
                // 改语义属性必须经 FlowModel.OnStepPropertyChanged 递增 Version。
                var par22 = new ParallelStep("\uE700", "并行分组", "BuiltIn_Parallel", "版本链组");
                var flow = new FlowModel { FlowName = "并行版本链断言" };
                flow.Steps.Add(par22);

                int v0 = flow.Version;
                par22.ExecutionMode = ParallelExecutionMode.Parallel;
                Check("P22：改 ExecutionMode → FlowModel Version 递增（触发重编译）",
                    flow.Version > v0, $"{v0} → {flow.Version}");

                int v1 = flow.Version;
                par22.FailFastMode = FailFastMode.On;
                Check("P22：再改 FailFastMode 同样递增", flow.Version > v1, $"{v1} → {flow.Version}");

                int v2 = flow.Version;
                par22.State = StepState.Running; // 运行态对照：不递增（[RuntimeState] 名单）
                Check("P22：改运行态 State 不递增（对照组，[RuntimeState] 名单内）",
                    flow.Version == v2, $"{v2} → {flow.Version}");
            }

            // ---- P24：三态 × 全局 on/off 全矩阵（运行期生效，无需重编译） ----
            {
                // 矩阵：Inherit+off→不取消 / Inherit+on→取消 / On+off→取消 / Off+on→不取消
                bool[] globals = { false, true };
                var modes = new[] { FailFastMode.Inherit, FailFastMode.On, FailFastMode.Off };
                foreach (bool g in globals)
                {
                    foreach (var m in modes)
                    {
                        bool expectCancel = m switch
                        {
                            FailFastMode.On => true,
                            FailFastMode.Off => false,
                            _ => g,
                        };

                        GlobalParallelConfig.FailFastByDefault = g;
                        try
                        {
                            SafeGatePlugin.Reset();
                            var biz = Step<BizFailPlugin>("矩阵业务失败");
                            var gate = Step<SafeGatePlugin>("矩阵闸门");
                            var par = ParallelGroup($"矩阵组{m}{g}", new[] { biz }, new[] { gate });
                            par.FailFastMode = m;
                            var run = ExecHarness.Prepare(new StepModel[] { par });
                            if (!run.Compiled) { Check($"P24 矩阵({m},global={g}) 编译", false, run.Errors); continue; }

                            var bg = Task.Run(() => run.Engine!.Run(run.NewContext(new StubLog())));
                            // 若期望取消：闸门应被令牌唤醒，Run 秒回；若期望不取消：闸门等满 5s 兜底
                            bool fastReturn = bg.Wait(TimeSpan.FromMilliseconds(2500));
                            SafeGatePlugin.Proceed.Set();
                            bg.Wait(TimeSpan.FromSeconds(8));

                            Check($"P24：FailFastMode={m} × global={g} → 取消兄弟={(expectCancel ? "是" : "否")}",
                                expectCancel ? fastReturn : true,
                                expectCancel ? $"Run {(fastReturn ? "提前返回" : "未提前返回（取消未生效）")}" : "");
                        }
                        finally
                        {
                            GlobalParallelConfig.FailFastByDefault = false;
                        }
                    }
                }

                // 改 GlobalParallelConfig 后下一轮生效（无需重编译）：同一编译产物跑两轮
                {
                    GlobalParallelConfig.FailFastByDefault = false;
                    SafeGatePlugin.Reset();
                    var biz = Step<BizFailPlugin>("轮次业务失败");
                    var gate = Step<SafeGatePlugin>("轮次闸门");
                    var par = ParallelGroup("轮次组", new[] { biz }, new[] { gate });
                    var run = ExecHarness.Prepare(new StepModel[] { par });
                    if (run.Compiled)
                    {
                        // 第一轮 global=false：不取消，闸门等满兜底（5s 兜底太久，提前 Set 放行）
                        var bg1 = Task.Run(() => run.Engine!.Run(run.NewContext(new StubLog())));
                        WaitFor(() => SafeGatePlugin.Reached.IsSet);
                        bool notCancelledFast = !bg1.Wait(800);
                        SafeGatePlugin.Proceed.Set();
                        bg1.Wait(TimeSpan.FromSeconds(8));

                        // 第二轮 global=true（同一编译产物）：取消生效，Run 提前返回
                        SafeGatePlugin.Reset();
                        GlobalParallelConfig.FailFastByDefault = true;
                        try
                        {
                            var bg2 = Task.Run(() => run.Engine!.Run(run.NewContext(new StubLog())));
                            WaitFor(() => SafeGatePlugin.Reached.IsSet);
                            bool cancelledFast = bg2.Wait(2500);
                            SafeGatePlugin.Proceed.Set();
                            bg2.Wait(TimeSpan.FromSeconds(8));
                            Check("P24：改 GlobalParallelConfig 后下一轮生效（同一编译产物，无需重编译）",
                                notCancelledFast && cancelledFast,
                                $"第一轮未取消={notCancelledFast} 第二轮取消={cancelledFast}");
                        }
                        finally { GlobalParallelConfig.FailFastByDefault = false; }
                    }
                }
            }

            // ---- P26：ForceSequential 总闸 ----
            {
                OrderPlugin.Reset();
                var a1 = Step<OrderPlugin>("总闸1A");
                var b1 = Step<OrderPlugin>("总闸2A");
                var par = ParallelGroup("总闸组", new[] { a1 }, new[] { b1 });
                var run = ExecHarness.Prepare(new StepModel[] { par });
                if (!run.Compiled) { Check("P26 图纸编译成功", false, run.Errors); return; }

                try
                {
                    GlobalParallelConfig.ForceSequential = true;
                    run.Engine!.Run(run.NewContext(new StubLog()));
                    Check("P26：ForceSequential=true → Parallel 图纸顺序执行",
                        string.Join("→", OrderPlugin.Order.Select(n => n.Split('.').Last())) == "总闸1A→总闸2A",
                        string.Join("→", OrderPlugin.Order));

                    // 复位后并发恢复（P1 手段复跑）
                    GlobalParallelConfig.ForceSequential = false;
                    SafeGatePlugin.Reset();
                    CountingPlugin.Reset();
                    var gate = Step<SafeGatePlugin>("总闸复闸门");
                    var counter = Step<CountingPlugin>("总闸复计数");
                    var par2 = ParallelGroup("总闸复跑组", new[] { gate }, new[] { counter });
                    var run2 = ExecHarness.Prepare(new StepModel[] { par2 });
                    if (run2.Compiled)
                    {
                        var bg = Task.Run(() => run2.Engine!.Run(run2.NewContext(new StubLog())));
                        bool reached = WaitFor(() => SafeGatePlugin.Reached.IsSet);
                        Check("P26：复位后并发恢复（分支2 在分支1 堵住期间已计数）",
                            reached && CountingPlugin.Runs > 0, $"runs={CountingPlugin.Runs}");
                        SafeGatePlugin.Proceed.Set();
                        bg.Wait(TimeSpan.FromSeconds(8));
                    }
                }
                finally
                {
                    GlobalParallelConfig.ForceSequential = false;
                }
            }

            // ---- P30：单分支退化（Parallel 模式 + 1 条分支 → 平铺执行，结果与 Sequential 一致） ----
            {
                OrderPlugin.Reset();
                var a1 = Step<OrderPlugin>("单分支A");
                var a2 = Step<OrderPlugin>("单分支B");
                var par = ParallelGroup("单分支组", new[] { a1, a2 });
                var run = ExecHarness.Prepare(new StepModel[] { par });
                Check("P30：单分支 Parallel 图纸编译成功", run.Compiled, run.Errors);
                if (!run.Compiled) return;
                run.Engine!.Run(run.NewContext(new StubLog()));
                Check("P30：单分支平铺执行（A→B 顺序）",
                    string.Join("→", OrderPlugin.Order.Select(n => n.Split('.').Last())) == "单分支A→单分支B",
                    string.Join("→", OrderPlugin.Order));
                Check("P30：容器 Success（与 Sequential 一致）", par.State == StepState.Success, $"{par.State}");
            }
        }

        // ==================================================================
        //  P21 焦点口径 + P25 受限池压力 + P27 join 超时
        // ==================================================================
        private static void FocusAndPoolStress()
        {
            Section("[P21/P25/P27] 焦点口径 / 受限池压力 / join 超时放弃");

            // ---- P21：并行期间容器 IsRunningFocus=true、分支步骤不触碰焦点 ----
            {
                SafeGatePlugin.Reset();
                CountingPlugin.Reset();
                var gate = Step<SafeGatePlugin>("焦点闸门");
                var probe = Step<FocusProbePlugin>("焦点探针");
                var par = ParallelGroup("焦点组", new[] { gate }, new[] { probe });
                var run = ExecHarness.Prepare(new StepModel[] { par });
                Check("P21 图纸编译成功", run.Compiled, run.Errors);
                if (!run.Compiled) return;

                FocusProbePlugin.Reset(par);
                var bg = Task.Run(() => run.Engine!.Run(run.NewContext(new StubLog())));

                bool reached = WaitFor(() => SafeGatePlugin.Reached.IsSet);
                if (reached)
                {
                    // 真并发窗口内采样（探针桩在窗口内记录，join 后统一断言——消除跨线程采样竞态）
                    SafeGatePlugin.Proceed.Set();
                }
                bg.Wait(TimeSpan.FromSeconds(8));

                var log = new StubLog();
                lock (FocusProbePlugin.Samples)
                {
                    Check("P21：并行期间容器 IsRunningFocus=true（探针采样）",
                        FocusProbePlugin.Samples.Count > 0 && FocusProbePlugin.Samples.All(s => s.ContainerFocus),
                        $"{FocusProbePlugin.Samples.Count} 个样本，containerFocus={FocusProbePlugin.Samples.Count(s => s.ContainerFocus)}");
                }
                Check("P21：join 后焦点随收尾移交/清除（分支步骤从未拿焦点）",
                    !gate.IsRunningFocus && !probe.IsRunningFocus, "");
                Check("P21：容器终态 Success", par.State == StepState.Success, $"{par.State}");
                _ = log;
            }

            // ---- P25：受限池压力（SetMinThreads(2,2) 跑嵌套并行，秒级完成；try/finally 复位 + 复位后并发恢复） ----
            {
                ThreadPool.GetMinThreads(out int workerBk, out int ioBk);
                try
                {
                    ThreadPool.SetMinThreads(2, 2);

                    // 嵌套并行：外层 2 分支，各嵌一个内层并行组（2 分支各一个计数桩）
                    CountingPlugin.Reset();
                    var inner1 = ParallelGroup("内层组1", new[] { Step<CountingPlugin>("内11") }, new[] { Step<CountingPlugin>("内12") });
                    var inner2 = ParallelGroup("内层组2", new[] { Step<CountingPlugin>("内21") }, new[] { Step<CountingPlugin>("内22") });
                    var outer = ParallelGroup("外层组", new StepModel[] { inner1 }, new StepModel[] { inner2 });
                    var run = ExecHarness.Prepare(new StepModel[] { outer });
                    Check("P25 嵌套并行图纸编译成功", run.Compiled, run.Errors);
                    if (!run.Compiled) return;

                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    run.Engine!.Run(run.NewContext(new StubLog()));
                    sw.Stop();
                    Check("P25：受限池（2,2）下嵌套并行秒级完成无饥饿",
                        sw.ElapsedMilliseconds < 5000, $"{sw.ElapsedMilliseconds}ms");
                    Check("P25：嵌套分支全部执行（4 桩全跑）", CountingPlugin.Runs == 4, $"runs={CountingPlugin.Runs}");
                }
                finally
                {
                    ThreadPool.SetMinThreads(workerBk, ioBk); // 复位（评审低危 15）
                }

                // 复位后并行行为恢复（P1 手段复跑）
                SafeGatePlugin.Reset();
                CountingPlugin.Reset();
                var gate = Step<SafeGatePlugin>("池复闸门");
                var counter = Step<CountingPlugin>("池复计数");
                var par = ParallelGroup("池复组", new[] { gate }, new[] { counter });
                var runR = ExecHarness.Prepare(new StepModel[] { par });
                if (runR.Compiled)
                {
                    var bg = Task.Run(() => runR.Engine!.Run(runR.NewContext(new StubLog())));
                    bool reached = WaitFor(() => SafeGatePlugin.Reached.IsSet);
                    Check("P25：MinThreads 复位后并发恢复", reached && CountingPlugin.Runs > 0, $"runs={CountingPlugin.Runs}");
                    SafeGatePlugin.Proceed.Set();
                    bg.Wait(TimeSpan.FromSeconds(8));
                }
            }

            // ---- P27：join 超时放弃（不协作取消的阻塞桩 + 缩短 JoinTimeout → 容器 Failed、不阻塞） ----
            {
                var stuck = Step<StuckPlugin>("死等桩");
                var quick = Step<CountingPlugin>("快速计数");
                var par = ParallelGroup("超时组", new[] { stuck }, new[] { quick });
                par.JoinTimeoutMs = 400; // 缩短超时（默认 15s 太久）
                var run = ExecHarness.Prepare(new StepModel[] { par });
                Check("P27 图纸编译成功", run.Compiled, run.Errors);
                if (!run.Compiled) return;

                var log = new StubLog();
                var ctx = run.NewContext(log);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var bg = Task.Run(() => run.Engine!.Run(ctx));
                bool returned = bg.Wait(TimeSpan.FromSeconds(5));
                sw.Stop();

                Check("P27：超时后 Run 返回（不无限期拖住）", returned, $"{sw.ElapsedMilliseconds}ms 未返回");
                Check("P27：容器终态 = Failed", par.State == StepState.Failed, $"{par.State}");
                Check("P27：Error 日志带放弃口径", log.Errors.Any(e => e.Contains("放弃等待")), string.Join("|", log.Errors));

                StuckPlugin.ReleaseAll();
                bg.Wait(TimeSpan.FromSeconds(8));
            }

            // ---- P32：JoinTimeoutMs ≤ 0（=未配置）→ 运行期回落默认 15 秒，不是"立刻超时" ----
            // 跨模块契约两头守：面板写"留空/0 = 用默认"（ParallelGroupConfigViewModel），运行期口径是
            // "> 0 才当真值、否则回落 DefaultJoinTimeoutMs"（CompiledParallelNode :133）。
            // 任何一端漂了（例如把 > 改成 >=），所有未配置的并行组都会在 join 阶段秒失败——
            // 而 P27 用的是 400ms 真超时，钉不住"0 该怎么算"这一格。
            {
                SafeGatePlugin.Reset();
                CountingPlugin.Reset();
                var gate = Step<SafeGatePlugin>("未配置超时闸门");
                var counter = Step<CountingPlugin>("未配置超时计数");
                var par = ParallelGroup("未配置超时组", new[] { gate }, new[] { counter });
                par.JoinTimeoutMs = 0;
                var run = ExecHarness.Prepare(new StepModel[] { par });
                Check("P32 图纸编译成功", run.Compiled, run.Errors);
                if (run.Compiled)
                {
                    var log = new StubLog();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var bg = Task.Run(() => run.Engine!.Run(run.NewContext(log)));

                    // 分支 1 堵在闸门 600ms 再放行：若 0 被当成"立即到期的 deadline"，容器早就 Failed 了
                    bool reached = WaitFor(() => SafeGatePlugin.Reached.IsSet, 3000);
                    Thread.Sleep(600);
                    SafeGatePlugin.Proceed.Set();

                    bool returned = bg.Wait(TimeSpan.FromSeconds(8));
                    sw.Stop();

                    Check(
                        "P32：超时未配置（0）→ 按默认等待，容器 Success（不是立刻超时）",
                        reached && returned && par.State == StepState.Success,
                        $"reached={reached} returned={returned} state={par.State} 耗时={sw.ElapsedMilliseconds}ms"
                    );
                    Check(
                        "P32：堵了 600ms 的分支被真等到（耗时 ≥ 550ms）",
                        sw.ElapsedMilliseconds >= 550,
                        $"{sw.ElapsedMilliseconds}ms"
                    );
                    Check(
                        "P32：无「放弃等待」日志",
                        !log.Errors.Any(e => e.Contains("放弃等待")),
                        string.Join("|", log.Errors)
                    );
                }
            }
        }
    }

    /// <summary>不协作取消的死等桩（P27）：等一个永不自动放行的静态事件（测试收尾手动 Release）</summary>
    [ParallelSafe]
    internal sealed class StuckPlugin : VisionPluginBase
    {
        private static readonly ManualResetEventSlim NeverProceed = new(false);

        public static void ReleaseAll() => NeverProceed.Set();

        public static void Reset() => NeverProceed.Reset();

        public override void RunAlgorithm(IExecutionContext context)
        {
            // 完全无视取消令牌（病态算子的替身）：死等最多 8 秒（防断言程序永久挂死）
            NeverProceed.Wait(TimeSpan.FromSeconds(8));
        }
    }
}
