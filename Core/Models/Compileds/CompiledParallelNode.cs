using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using VisionMaster.Services;
// 本文件 using 了 System.Threading，裸写的 ExecutionContext 会和 System.Threading.ExecutionContext 撞名
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace VisionMaster.Models
{
    /// <summary>
    /// 编译后的并行分组节点。
    ///
    /// 二期（真并行执行，方案 v2）：
    ///  · 同步外形内部扇出（§2）：RunAndGetNext 签名不变，内部完成
    ///    "退化判定 → groupCts → 调度 → 分支体 → 可中断 join → 纯函数裁决 → 变量合并 → 上抛/Return"，
    ///    对上层完全透明；
    ///  · 退化路径（§4 四入口矩阵）：Sequential 模式 / 单分支 / 会话调试退化标志 /
    ///    ForceSequential 总闸 → 一期平铺语义原样执行（旧路径保留为私有方法）；
    ///  · 并行节点不变量（§3.6）：
    ///    1) 分支步骤只写自己 StepModel 的 State/耗时，绝不触碰 session.FocusedStep（含 Skipped 路径，
    ///       由 UpdateStepRuntimeState 的整方法级分支快捷路径保证）；
    ///    2) 容器只由父线程写（Running/终态在扇出前/join 后的父线程上报）；
    ///    3) 分支内 CurrentNodeId 写的是 branchCtx 实例字段，父上下文停在容器 Id。
    /// </summary>
    public class CompiledParallelNode : CompiledNode
    {
        /// <summary>并行分支的执行清单（按图纸声明顺序；Sequential 模式下平铺交出）</summary>
        public List<List<CompiledNode>> Branches { get; set; } = new();

        /// <summary>
        /// 编译期旁注（不阻断编译的提醒，如分支数超过建议上限）。
        /// CompilationResult 只有 Errors（非空即失败）没有警告通道，观感类提醒放这里，
        /// 运行首圈由引擎日志带出。
        /// </summary>
        public List<string> CompileNotes { get; set; } = new();

        /// <summary>运行期旁注由编译器按 v2 填（FailFast/ExecutionMode 相关提示），与 CompileNotes 同通道带出</summary>
        public List<string> RuntimeNotes { get; set; } = new();

        // ===================== groupCts 生命周期调试计数器（P5 断言） =====================

        /// <summary>累计创建的 LinkedTokenSource 数（Interlocked 维护，断言"连续 N 轮后 Created==Disposed"）</summary>
        public static long GroupCtsCreated;

        /// <summary>累计释放的 LinkedTokenSource 数</summary>
        public static long GroupCtsDisposed;

        /// <summary>join 的运行期防护（评审高危 6）：同步 join 在非池线程（带 SynchronizationContext）被调时 Warn</summary>
        private static void WarnIfJoinOnNonPoolThread(ILogService logger)
        {
            if (SynchronizationContext.Current != null)
                logger?.Warn("[并行] 并行分组的同步汇合运行在带 SynchronizationContext 的线程（如 UI 线程）——请经引擎/Task.Run 调用，否则可能死锁");
        }

        public override List<CompiledNode> RunAndGetNext(IExecutionContext context)
        {
            context.CurrentNodeId = Id;

            foreach (var note in CompileNotes)
                context.Logger?.Warn(note);
            foreach (var note in RuntimeNotes)
                context.Logger?.Info(note);

            // A2 同口径：容器也要上报状态与耗时（父线程写，不变量 2）
            UpdateStepRuntimeState(context, StepRuntimeState.Running);

            // ===== ① 退化判定（§4）=====
            var parallelStep = Blueprint as ParallelStep;
            bool debugDegraded =
                context is ExecutionContext execCtx && execCtx.CurrentSession is { DebugDegradedParallel: true };
            bool degraded =
                parallelStep == null                        // 拿不到模型（防御）：按一期平铺
                || parallelStep.ExecutionMode == ParallelExecutionMode.Sequential
                || Branches.Count <= 1                       // 单分支：平铺等价
                || debugDegraded                              // 调试会话/试运行（四入口矩阵的置位入口在引擎侧）
                || GlobalParallelConfig.ForceSequential;      // 进程级总闸

            if (degraded)
            {
                if (debugDegraded && parallelStep?.ExecutionMode == ParallelExecutionMode.Parallel)
                    context.Logger?.Info($"并行组 '{Name}' 在调试/试运行会话中按顺序执行（就地退化，用户裁决 3 + 主会话口径 4）");
                return RunSequentialFlattened(context);
            }

            // ===== ② 扇出前令牌检查（评审中危 10：扇出前才判 Skipped，与既有 :45-48 逐位一致）=====
            if (context.CancellationToken.IsCancellationRequested)
            {
                UpdateStepRuntimeState(context, StepRuntimeState.Skipped);
                return null;
            }

            // join 线程防护（运行期检查，非 Debug 断言）
            WarnIfJoinOnNonPoolThread(context.Logger);

            return RunParallel(context, parallelStep!);
        }

        /// <summary>一期平铺路径原样保留（退化场景的执行体）</summary>
        private List<CompiledNode> RunSequentialFlattened(IExecutionContext context)
        {
            if (context.CancellationToken.IsCancellationRequested)
            {
                UpdateStepRuntimeState(context, StepRuntimeState.Skipped);
                return null;
            }

            UpdateStepRuntimeState(context, StepRuntimeState.Success);

            // 平铺交出全部分支：RunSequence 挨个执行，等效"分支1 → 分支2 → …"顺序语义。
            // 空分支自然被跳过（清单为空不压栈）。
            return Branches.SelectMany(b => b).ToList();
        }

        // ==================================================================================
        //  真并行执行体（§2 步骤②~⑩）
        // ==================================================================================

        private List<CompiledNode> RunParallel(IExecutionContext context, ParallelStep parallelStep)
        {
            // 生效 FailFast（§3.2：运行期解析，改全局配置下一轮生效）
            _effectiveFailFast = parallelStep.FailFastMode switch
            {
                FailFastMode.On => true,
                FailFastMode.Off => false,
                _ => GlobalParallelConfig.FailFastByDefault,
            };
            _joinAbandoned = false;

            int timeoutMs = parallelStep.JoinTimeoutMs > 0 ? parallelStep.JoinTimeoutMs : ParallelStep.DefaultJoinTimeoutMs;

            var branchContexts = new ParallelBranchExecutionContext[Branches.Count];
            int failureSource = -1;                 // 失败源登记槽（CAS；Interlocked 要求按引用传递，故局部变量）
            Exception? firstFault = null;             // 失败源的异常（仅登记成功的分支写）
            results = new ParallelBranchResult[Branches.Count];

            // ===== using 保证 groupCts 任何路径都 Dispose（评审高危 1：父令牌是会话级，不释放则每轮挂一份注册）=====
            using (var groupCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken))
            {
                Interlocked.Increment(ref GroupCtsCreated);

                for (int i = 0; i < Branches.Count; i++)
                    branchContexts[i] = new ParallelBranchExecutionContext((ExecutionContext)context, i, groupCts.Token);

                // ===== 调度（评审高危 6）：D = min(n-1, 核数-1) 条 Task.Run，末分支在 join 线程就地执行 =====
                int n = Branches.Count;
                int taskCount = Math.Min(n - 1, Environment.ProcessorCount - 1);
                if (taskCount < 0) taskCount = 0;

                var tasks = new Task[taskCount];
                for (int i = 0; i < taskCount; i++)
                {
                    int branchIndex = i;
                    // 注意：不带 groupCts.Token 启动——带 token 时"调度前已取消"会让任务直接 Canceled、
                    // 分支体一次都不执行（其分支上下文与记账就此丢失）。分支体内部对取消完全协作
                    // （RunSequence 每节点检查 + 桩算子步进检查），不需要外层再砍一刀。
                    tasks[i] = Task.Run(() => ExecuteBranch(branchIndex, branchContexts[branchIndex], groupCts,
                        ref failureSource, ref firstFault));
                }

                // join 线程就地执行末分支（及超出的余量——n-1 > taskCount 时中间段也在此顺序消化）
                try
                {
                    for (int i = taskCount; i < n; i++)
                        ExecuteBranch(i, branchContexts[i], groupCts, ref failureSource, ref firstFault);
                }
                finally
                {
                    // ===== 可中断 join（评审中危 11）：WaitAll 轮询步进 + deadline =====
                    JoinWithDeadline(tasks, context, groupCts, timeoutMs, branchContexts);
                }

                // ===== 裁决（纯函数）=====
                // join 超时放弃（§3.1 写死口径）优先于 Adjudicate 的一般规则：
                // 容器 Failed + Error 日志（已在 JoinWithDeadline 记）+ 不抛 + 不合并。
                if (_joinAbandoned)
                {
                    UpdateStepRuntimeState(context, StepRuntimeState.Failed);
                    return null; // groupCts 的 using 在方法出口 Dispose（Created==Disposed 计数不受影响）
                }

                // 【空位补齐】带 token 的 Task.Run 在调度前被取消 → 任务直接 Canceled、
                // ExecuteBranch 从未执行、results 槽位留 null（P4 实测根因）。
                // join 完成后统一补 Cancelled，裁决/合并才不会拿到 null 项。
                lock (_resultsLock)
                {
                    for (int i = 0; i < results.Length; i++)
                    {
                        if (results[i] == null)
                            results[i] = new ParallelBranchResult
                            {
                                BranchIndex = i,
                                Outcome = ParallelBranchResult.BranchOutcome.Cancelled,
                            };
                    }
                }

                var verdict = Adjudicate(results.ToList(), parentCancelledBeforeJoin: false);

                // ===== 变量合并（§3.5 合并表：按分支终态过滤 + 记账集合 + 冲突告警）=====
                if (verdict.MergeVariables)
                    MergeBranchVariables(context, branchContexts, results);

                // ===== ⑨ 失败源异常原样上抛（规则 1）=====
                if (verdict.RethrowFailureSource)
                {
                    UpdateStepRuntimeState(context, StepRuntimeState.Failed);
                    var fault = FindFailureSourceException(results);
                    if (fault != null)
                        ExceptionDispatchInfo.Capture(fault).Throw();
                    throw new InvalidOperationException($"并行组 '{Name}' 存在失败分支但异常对象缺失（内部错误）");
                }

                // ===== ⑩ Return 交顶层终结（规则 2）=====
                if (verdict.PropagateReturn)
                {
                    UpdateStepRuntimeState(context, StepRuntimeState.Success);
                    context.CurrentFlowState = FlowControlState.Return;
                    return null;
                }

                // 终态呈现
                switch (verdict.State)
                {
                    case ContainerVerdict.VerdictState.Failed:
                        UpdateStepRuntimeState(context, StepRuntimeState.Failed);
                        break;
                    case ContainerVerdict.VerdictState.Skipped:
                        UpdateStepRuntimeState(context, StepRuntimeState.Skipped);
                        break;
                    case ContainerVerdict.VerdictState.Success:
                        UpdateStepRuntimeState(context, StepRuntimeState.Success);
                        break;
                    default:
                        // DeferToStepStates：BusinessFailed 的 FailFast 联动已取消兄弟；
                        // 容器终态由轮末 EscalateContainerFailures 按步骤态上浮（F11）
                        UpdateStepRuntimeState(context, StepRuntimeState.Success);
                        break;
                }

                // BusinessFailed × FailFast（评审高危 3 定死）：只取消兄弟，不登记失败源、不上抛、
                // 容器终态不在这里改（轮末 EscalateContainerFailures 会按步骤 Failed 上浮）

                // 计数器在 using 块内递增（等价 finally 语义）：上方的 return/throw 出口
                // （join 超时放弃 :179、失败源上抛 :208）也要保持 Created==Disposed 成立——
                // reviewer #2：放在 using 之后会让异常/超时路径漏计，P5 扩场景即假红
                Interlocked.Increment(ref GroupCtsDisposed);
            }

            // 并行组自带汇合，不向父序列交出子清单（与 For/While 同款骨架）
            return null;
        }

        /// <summary>join 轮询步进 + deadline；超时按 §3.1 口径放弃（容器 Failed + Error 日志 + 不再等）</summary>
        private void JoinWithDeadline(
            Task[] tasks,
            IExecutionContext context,
            CancellationTokenSource groupCts,
            int timeoutMs,
            ParallelBranchExecutionContext[] branchContexts)
        {
            if (tasks.Length == 0) return;

            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (!Task.WaitAll(tasks, 500))
            {
                if (DateTime.UtcNow >= deadline)
                {
                    // 超时放弃（§3.1 写死口径）：容器 Failed + Error 日志 + 不再等分支 + 不阻塞会话替换。
                    // 已启动分支可能还在写自己影子（影子与父域隔离，父域安全）。
                    string states;
                    ParallelBranchResult[] snapshot;
                    lock (_resultsLock)
                    {
                        snapshot = (ParallelBranchResult[])results.Clone();
                    }
                    states = string.Join(", ", snapshot.Select((r, i) =>
                        $"分支{i}:{(r == null ? "未收尾" : r.Outcome)}"));
                    context.Logger?.Error(
                        $"[并行] 并行组 '{Name}' 汇合等待超过 {timeoutMs}ms，放弃等待（容器标 Failed）。各分支终态：{states}。" +
                        "可能存在不协作取消的算子，请检查长阻塞步骤或调大 JoinTimeoutMs。");
                    lock (_resultsLock)
                    {
                        for (int i = 0; i < results.Length; i++)
                        {
                            if (results[i] == null)
                                results[i] = new ParallelBranchResult
                                {
                                    BranchIndex = i,
                                    Outcome = ParallelBranchResult.BranchOutcome.Cancelled,
                                };
                        }
                    }
                    MarkJoinAbandoned();
                    return;
                }
            }
        }

        /// <summary>join 超时放弃的落点标记（供裁决识别规则 4）：用字段而非返回值，避免改 JoinWithDeadline 签名</summary>
        private void MarkJoinAbandoned() => _joinAbandoned = true;

        private bool _joinAbandoned;

        /// <summary>分支终态登记的互斥锁：results 槽位由 Task 分支线程与 join 线程（末分支就地执行段）并发写</summary>
        private readonly object _resultsLock = new();

        /// <summary>本轮并行执行的分支终态槽（RunParallel 每次扇出前重建；RecordResult/JoinWithDeadline/裁决访问）</summary>
        private ParallelBranchResult[] results = Array.Empty<ParallelBranchResult>();

        /// <summary>本轮生效的 FailFast（§3.2 运行期解析；ExecuteBranch 的 BusinessFailed 联动读）</summary>
        private volatile bool _effectiveFailFast;

        /// <summary>
        /// 分支体（§3.1）：整体 try/catch——任务永不 faulted，一切结果（含异常）记入 results。
        /// </summary>
        private void ExecuteBranch(
            int branchIndex,
            ParallelBranchExecutionContext branchCtx,
            CancellationTokenSource groupCts,
            ref int failureSource,
            ref Exception? firstFault)
        {
            ParallelBranchResult.BranchOutcome outcome;
            Exception? captured = null;

            try
            {
                // 分支跑自己的 RunSequence：Break/Continue 只终结本分支（yield:true 交回，
                // 顶层孤儿指令在下方就地消化）；Return 是全局的（收尾判定并取消兄弟）。
                RunSequence(Branches[branchIndex], branchCtx, yieldToControlFlow: true);

                if (branchCtx.CurrentFlowState == FlowControlState.Return)
                {
                    outcome = ParallelBranchResult.BranchOutcome.Return;
                    groupCts.Cancel(); // 取消兄弟分支（§3.7：Return 终止整个流程）
                }
                else if (branchCtx.CurrentFlowState != FlowControlState.Normal)
                {
                    // 顶层孤儿 Break/Continue：就地清零 + Warn（与 CompiledNode.cs 顶层同款哲学）
                    branchCtx.Logger?.Warn(
                        $"并行分组 '{Name}' 分支 {branchIndex + 1} 顶层出现无循环归属的 {branchCtx.CurrentFlowState} 指令，已在本分支内忽略（请把 Break/Continue 放进循环体内）");
                    branchCtx.CurrentFlowState = FlowControlState.Normal;
                    outcome = ParallelBranchResult.BranchOutcome.FlowSignal;
                }
                else if (groupCts.IsCancellationRequested)
                {
                    outcome = ParallelBranchResult.BranchOutcome.Cancelled;
                }
                else
                {
                    // 步骤态判定（同线程访问分支步骤清单，无并发问题——步骤只被本分支线程写，F8）
                    bool anyFailed = BranchHasFailedStep(Branches[branchIndex]);
                    if (anyFailed)
                    {
                        outcome = ParallelBranchResult.BranchOutcome.BusinessFailed;
                        // FailFast 联动（§3.3.3）：只取消兄弟，不登记失败源、不记 Failed、不上抛
                        if (_effectiveFailFast)
                            groupCts.Cancel();
                    }
                    else
                    {
                        outcome = ParallelBranchResult.BranchOutcome.Success;
                    }
                }

                // 【静默早退补判】RunSequence 遇令牌取消是"静默 return"（不抛 OCE）——
                // 分支自己若是被兄弟取消打断的（此刻 groupCts 已取消但本轮上面三步都判不到：
                // Return/FlowSignal 不成立、上面判取消时可能尚未取消），必须补判 Cancelled，
                // 否则被取消分支会被误记 Success（P4/P24 的失败根因）。
                if (outcome is ParallelBranchResult.BranchOutcome.Success
                    && groupCts.IsCancellationRequested)
                {
                    outcome = ParallelBranchResult.BranchOutcome.Cancelled;
                }
            }
            catch (OperationCanceledException) when (groupCts.IsCancellationRequested)
            {
                // 协作取消的常态噪音：兄弟失败/外部停止/FailFast/Return 引发的取消——不上抛不参与裁决
                outcome = ParallelBranchResult.BranchOutcome.Cancelled;
            }
            catch (Exception ex)
            {
                if (Interlocked.CompareExchange(ref failureSource, branchIndex, -1) == -1)
                {
                    // 我抢到了"失败源"身份：我是真正触发取消的那个（评审高危 2）
                    firstFault = ex;
                    groupCts.Cancel();
                    outcome = ParallelBranchResult.BranchOutcome.Failed;
                    captured = ex;
                }
                else
                {
                    // 兄弟已先失败，我的异常是被取消连带引发的罕见路径——降级为取消噪音
                    branchCtx.Logger?.Warn(
                        $"并行分组 '{Name}' 分支 {branchIndex + 1} 在取消后仍抛出异常（已降级，失败源为分支 {failureSource + 1}）: {ex.Message}");
                    outcome = ParallelBranchResult.BranchOutcome.Cancelled;
                }
            }

            RecordResult(branchIndex, new ParallelBranchResult
            {
                BranchIndex = branchIndex,
                Outcome = outcome,
                Exception = captured,
                WrittenKeys = branchCtx.Registry.WrittenKeys,
            });
        }

        /// <summary>
        /// 分支终态登记（带互斥）：results 槽位被 Task 分支线程与 join 线程（末分支就地执行段）并发写，
        /// join 超时路径还要读"是否已收尾"——统一串行化，消除撕裂读。
        /// </summary>
        private void RecordResult(int branchIndex, ParallelBranchResult result)
        {
            lock (_resultsLock)
            {
                results[branchIndex] = result;
            }
        }

        /// <summary>分支步骤清单里是否有 Failed 步骤（步骤只被本分支线程写，读取无并发问题）</summary>
        private static bool BranchHasFailedStep(List<CompiledNode> branchNodes)
        {
            foreach (var node in branchNodes)
            {
                if (node.Blueprint?.State == StepState.Failed) return true;
                // 分支内嵌套容器的失败由其内部步骤反映（运行时状态已写在同一批 StepModel 上）
                if (NodeTreeHasFailedStep(node)) return true;
            }
            return false;
        }

        /// <summary>递归检查编译节点树对应的 StepModel 是否有 Failed（含嵌套容器分支）</summary>
        private static bool NodeTreeHasFailedStep(CompiledNode node)
        {
            if (node is CompiledIfNode ifNode)
                return ifNode.Branches.Any(b => b.ExecutionSteps.Any(NodeTreeHasFailedStep));
            if (node is CompiledWhileNode whileNode)
                return whileNode.LoopBranch?.ExecutionSteps.Any(NodeTreeHasFailedStep) == true;
            if (node is CompiledForNode forNode)
                return forNode.LoopBody.Any(NodeTreeHasFailedStep);
            if (node is CompiledParallelNode parallelNode)
                return parallelNode.Branches.Any(b => b.Any(NodeTreeHasFailedStep));
            return node.Blueprint?.State == StepState.Failed;
        }

        // ==================================================================================
        //  汇合裁决（§3.4，纯函数——表驱动断言 P6 直接喂桩数据）
        // ==================================================================================

        /// <summary>
        /// join 完成后的裁决（父线程、纯函数式）：
        /// 优先级 1 失败源 → 2 Return → 3 扇出前取消（外部早退，本方法不入此态）→ 4 join 超时 → 5 其余。
        /// </summary>
        /// <param name="results">全部分支终态（join 超时放弃时未收尾分支已补 Cancelled）</param>
        /// <param name="parentCancelledBeforeJoin">扇出前父令牌已取消（RunAndGetNext 已早退，防御位）</param>
        public static ContainerVerdict Adjudicate(
            IReadOnlyList<ParallelBranchResult> results,
            bool parentCancelledBeforeJoin = false)
        {
            if (parentCancelledBeforeJoin)
                return new ContainerVerdict
                {
                    State = ContainerVerdict.VerdictState.Skipped,
                    MergeVariables = false,
                };

            // 规则 1：存在 Failed 分支（失败源）→ 容器 Failed + 上抛源异常 + 跳过合并（父域保持快照）
            var failure = results.FirstOrDefault(r => r?.Outcome == ParallelBranchResult.BranchOutcome.Failed);
            if (failure != null)
                return new ContainerVerdict
                {
                    State = ContainerVerdict.VerdictState.Failed,
                    RethrowFailureSource = true,
                    MergeVariables = false,
                };

            // 规则 2：任一分支 Return → 容器 Success + CurrentFlowState=Return + 合并（含 Return 分支写入）
            if (results.Any(r => r?.Outcome == ParallelBranchResult.BranchOutcome.Return))
                return new ContainerVerdict
                {
                    State = ContainerVerdict.VerdictState.Success,
                    PropagateReturn = true,
                    MergeVariables = true,
                };

            // 规则 3：join 超时放弃 → 由调用方标记（results 里失败分支已被补 Cancelled 且容器 Failed）
            // —— 超时路径在 RunParallel 里先于 Adjudicate 判定并直接落终态，此处不再重复。

            // 规则 4（原规则 5）：全 Success / BusinessFailed / FlowSignal / Cancelled 混合 →
            // 步骤态上浮交给轮末 EscalateContainerFailures；合并仅取 Success/BusinessFailed/FlowSignal/Return 分支
            return new ContainerVerdict
            {
                State = ContainerVerdict.VerdictState.DeferToStepStates,
                MergeVariables = true,
            };
        }

        private static Exception? FindFailureSourceException(ParallelBranchResult[] results)
        {
            foreach (var r in results)
            {
                if (r?.Outcome == ParallelBranchResult.BranchOutcome.Failed)
                    return r.Exception;
            }
            return null;
        }

        // ==================================================================================
        //  变量合并（§3.5：记账字典 + 分支终态过滤 + 冲突告警）
        // ==================================================================================

        /// <summary>
        /// 按分支声明序 0→n 串行合并（后分支覆盖先分支，确定性）；
        /// Cancelled 整体丢弃（丢弃时 Warn）；Failed 分支不会到这里（规则 1 已跳过合并）。
        /// </summary>
        private static void MergeBranchVariables(
            IExecutionContext parentContext,
            ParallelBranchExecutionContext[] branchContexts,
            ParallelBranchResult[] results)
        {
            // 参与告警与合并的分支终态白名单（§3.5 合并表）
            bool Mergeable(ParallelBranchResult.BranchOutcome o) => o is ParallelBranchResult.BranchOutcome.Success
                or ParallelBranchResult.BranchOutcome.BusinessFailed
                or ParallelBranchResult.BranchOutcome.FlowSignal
                or ParallelBranchResult.BranchOutcome.Return;

            // 冲突告警按"真实写入集合"统计（同值写入也计数——P9b：记账制可见，按值比较的实现必红）
            var writeCounts = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (int i = 0; i < results.Length; i++)
            {
                var r = results[i];
                if (r == null || !Mergeable(r.Outcome)) continue;
                foreach (var key in r.WrittenKeys)
                {
                    if (!writeCounts.TryGetValue(key, out var writers))
                        writeCounts[key] = writers = new List<int>();
                    writers.Add(i);
                }
            }

            foreach (var kvp in writeCounts)
            {
                if (kvp.Value.Count >= 2)
                    parentContext.Logger?.Warn(
                        $"[并行] 并行组分支 {string.Join("/", kvp.Value.Select(b => b + 1))} 同时写入了同名运行时变量 '{kvp.Key}'，" +
                        "汇合按分支声明序取后者覆盖（确定性）；建议各分支写各自的变量，汇合后再聚合。");
            }

            // 丢弃告警：Cancelled 分支的影子写入（不合并、不参与上面的冲突统计）
            for (int i = 0; i < results.Length; i++)
            {
                var r = results[i];
                if (r?.Outcome != ParallelBranchResult.BranchOutcome.Cancelled) continue;
                var written = branchContexts[i].Registry.WrittenKeys;
                if (written.Count > 0)
                    parentContext.Logger?.Warn(
                        $"[并行] 分支 {i + 1} 被取消，其运行时变量写入已整体丢弃（{string.Join(", ", written)}）——半成品值会污染下游判定。");
            }

            // 合并本体：分支声明序串行写入父域（新建键直接并入；不比较值——Equals 抛出的风险从根上消除）
            var parentVars = parentContext.LocalVariables;
            for (int i = 0; i < results.Length; i++)
            {
                var r = results[i];
                if (r == null || !Mergeable(r.Outcome)) continue;
                var registry = branchContexts[i].Registry;
                foreach (var key in registry.WrittenKeys)
                {
                    if (registry.TryGetValue(key, out var value))
                        parentVars[key] = value;
                    else
                        parentVars.Remove(key); // 分支删掉了这个键（Remove 也记名）——如实同步
                }
            }
        }
    }
}
