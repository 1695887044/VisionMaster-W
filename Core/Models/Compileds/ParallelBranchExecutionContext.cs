using Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;
using VisionMaster.Services;
// 本文件 using 了 System.Threading，裸写的 ExecutionContext 会和 System.Threading.ExecutionContext 撞名
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace VisionMaster.Models
{
    /// <summary>
    /// 并行分支的终态记录（二期 §3.3）。分支 Task 体把一切结果（含异常）记到这里，
    /// join 处统一裁决——任务永不 faulted（评审高危 6）。
    /// </summary>
    public sealed class ParallelBranchResult
    {
        /// <summary>分支序号（图纸声明序，0 基）</summary>
        public int BranchIndex { get; init; }

        /// <summary>分支终态</summary>
        public BranchOutcome Outcome { get; init; }

        /// <summary>失败源分支持有的异常（仅 Outcome=Failed 时非空；ExceptionDispatchInfo 原样上抛用）</summary>
        public Exception? Exception { get; init; }

        /// <summary>本分支写入过的变量名集合（来自记账字典；join 合并/告警用）</summary>
        public IReadOnlyCollection<string> WrittenKeys { get; init; } = Array.Empty<string>();

        /// <summary>分支终态（§3.3）</summary>
        public enum BranchOutcome
        {
            /// <summary>正常完成，全步骤成功</summary>
            Success,

            /// <summary>正常完成，但分支内有步骤标 Failed（业务失败，无异常对象）</summary>
            BusinessFailed,

            /// <summary>异常失败且本分支是失败源（§3.1 CAS 登记）</summary>
            Failed,

            /// <summary>被取消打断（外部停止 / 失败源取消 / FailFast 取消 / 超时放弃时未收尾）</summary>
            Cancelled,

            /// <summary>分支内 Return 节点触发（终止整个流程的全局指令）</summary>
            Return,

            /// <summary>分支顶层孤儿 Break/Continue 被就地消化后的正常收尾（视同 Success 合并）</summary>
            FlowSignal,
        }
    }

    /// <summary>
    /// 汇合裁决结论（§3.4，纯函数 Adjudicate 的输出；表驱动断言 P6 直接喂桩数据）。
    /// </summary>
    public sealed class ContainerVerdict
    {
        /// <summary>容器终态呈现</summary>
        public VerdictState State { get; init; }

        /// <summary>是否向父层上抛失败源异常（裁决规则 1；异常本体在 ParallelBranchResult.Exception）</summary>
        public bool RethrowFailureSource { get; init; }

        /// <summary>是否把 parentCtx.CurrentFlowState 置为 Return（裁决规则 2）</summary>
        public bool PropagateReturn { get; init; }

        /// <summary>是否执行变量合并（§3.5 合并表；false = 父域保持快照）</summary>
        public bool MergeVariables { get; init; }

        /// <summary>裁决出的容器终态</summary>
        public enum VerdictState
        {
            /// <summary>容器标 Success（Return 是指令性正常出口，与 CompiledReturnNode 同口径）</summary>
            Success,

            /// <summary>容器标 Failed（失败源 / join 超时；BusinessFailed 场景由轮末 EscalateContainerFailures 上浮）</summary>
            Failed,

            /// <summary>容器标 Skipped（扇出前父令牌已取消）</summary>
            Skipped,

            /// <summary>按步骤态交给轮末 EscalateContainerFailures 上浮（有失败步骤→Failed，否则 Success）</summary>
            DeferToStepStates,
        }
    }

    /// <summary>
    /// 并行分支执行上下文（二期）：继承 ExecutionContext（复用完整构造），
    /// 构造时把 LocalVariables 替换为记账字典（BranchVariableRegistry）。
    ///
    /// 关键事实：
    ///  · CurrentNodeId / CurrentFlowState 是本实例的字段（F2 实例隔离）——分支内写它
    ///    不会污染父上下文；父上下文的 CurrentNodeId 停在容器 Id；
    ///  · UpdateStepRuntimeState 认出本类型走"整方法级分支快捷路径"（只 State+timing，
    ///    绝不触碰 FocusedStep，含 Skipped）；
    ///  · CurrentSession/Logger/Workspace/Cameras/Motions/FlowInvoker 与父上下文同源共享
    ///    （会话对象本身线程安全语义见 F7-F8）。
    /// </summary>
    public sealed class ParallelBranchExecutionContext : ExecutionContext
    {
        /// <summary>本分支的序号（0 基，图纸声明序；报错/告警文案用）</summary>
        public int BranchIndex { get; }

        /// <summary>本分支的记账字典（构造时已替换 LocalVariables；join 后读 WrittenKeys）</summary>
        public BranchVariableRegistry Registry { get; }

        /// <summary>
        /// 构造：以父上下文为种子建分支上下文。
        /// LocalVariables 用记账字典（种子=父域浅拷）——protected init 只允许本族子类赋值。
        /// CancellationToken 换成并行组的 groupCts.Token（链着父令牌）：
        /// 兄弟失败/FailFast/Return 取消 groupCts 时，分支内的令牌检查
        /// （RunSequence 每节点 + 协作取消的算子）被立即唤醒——这是
        /// "失败 → 其余分支尽快取消"的通道；只靠父令牌的话，并行组内部取消
        /// 分支根本看不见，会一直跑到自然结束。
        /// </summary>
        public ParallelBranchExecutionContext(ExecutionContext parent, int branchIndex, CancellationToken branchToken)
            : base(parent.Logger, parent.CurrentSession, parent.Workspace, branchToken)
        {
            BranchIndex = branchIndex;
            Registry = new BranchVariableRegistry(parent.LocalVariables);
            LocalVariables = Registry;
        }
    }
}
