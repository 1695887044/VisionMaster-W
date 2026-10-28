namespace Core.Interfaces
{
    /// <summary>一轮流程的最终结果分类（引擎/宿主/插件共用的契约枚举）。</summary>
    public enum FlowRoundOutcome
    {
        /// <summary>业务判定通过。</summary>
        Ok = 0,

        /// <summary>业务判定不通过（不是技术故障）。</summary>
        Ng = 1,

        /// <summary>跑完了但没拿到判定（未配置判定源 / 判定步骤被跳过 / 类型不符）。</summary>
        Unknown = 2,

        /// <summary>被停止/取消，未跑完。</summary>
        Aborted = 3,

        /// <summary>执行异常（技术故障）。</summary>
        Faulted = 4,
    }

    /// <summary>判定来源：出问题时能回答"这个 OK/NG 是谁给的"。</summary>
    public enum FlowVerdictSource
    {
        None = 0,
        ResultStep = 1,
        Variable = 2,
        StepPort = 3,
    }

    /// <summary>判定解析结果。</summary>
    public readonly record struct FlowVerdict(
        FlowRoundOutcome Outcome,
        FlowVerdictSource Source,
        string Reason);
}
