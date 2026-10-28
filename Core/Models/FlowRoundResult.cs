using System;
using System.Collections.Generic;
using Core.Interfaces;

namespace VisionMaster.Models
{
    /// <summary>单步的执行快照（轮次下钻的步骤时间线）。</summary>
    public sealed record FlowStepRecord(
        Guid StepId,
        string StepName,
        string State,
        double ElapsedMs)
    {
        public string ElapsedText => $"{ElapsedMs:N0} ms";
    }

    /// <summary>
    /// 一轮流程的不可变记录（L2 引擎一等契约）。
    ///
    /// 为什么要有它：同一件事实（"这一轮跑得怎么样"）此前散在引擎事件、PerformanceMonitor、
    /// 样例方案自己的计数、宿主统计四处；消费者各算一套必然漂移。引擎在每轮结束时产出这一条，
    /// 底栏 / SCADA / 上报 / 报表 / 子流程调用都读它。
    ///
    /// 只记录**已完成的轮次**；被停止/取消的轮次不产出记录（避免"半轮"污染计数）。
    /// </summary>
    public sealed class FlowRoundResult
    {
        public Guid FlowId { get; init; }
        public string FlowName { get; init; } = string.Empty;
        public string SessionId { get; init; } = string.Empty;

        /// <summary>该会话自己的轮次序号（从 1 开始；会话重建后归零）</summary>
        public long RoundIndex { get; init; }

        public DateTime StartedAtUtc { get; init; }
        public DateTime CompletedAtUtc { get; init; }
        public double CycleTimeMs { get; init; }

        public FlowRoundOutcome Outcome { get; init; } = FlowRoundOutcome.Unknown;
        public FlowVerdictSource VerdictSource { get; init; } = FlowVerdictSource.None;
        public string Reason { get; init; } = string.Empty;

        public Guid? FailedStepId { get; init; }
        public string FailedStepName { get; init; } = string.Empty;

        public int StepCount { get; init; }
        public int CompletedStepCount { get; init; }

        /// <summary>本轮各步骤的快照（顺序 = 深表顺序；耗时取 LastRunTimeMs）</summary>
        public IReadOnlyList<FlowStepRecord> Steps { get; init; } = Array.Empty<FlowStepRecord>();

        public bool IsDebug { get; init; }

        // ---- 展示口径（UI / 日志共用，避免每处各写一套格式化） ----

        public string OutcomeText => Outcome switch
        {
            FlowRoundOutcome.Ok => "OK",
            FlowRoundOutcome.Ng => "NG",
            FlowRoundOutcome.Aborted => "中止",
            FlowRoundOutcome.Faulted => "故障",
            _ => "未判定",
        };

        public string VerdictSourceText => VerdictSource switch
        {
            FlowVerdictSource.ResultStep => "结果输出步骤",
            FlowVerdictSource.Variable => "结果变量",
            FlowVerdictSource.StepPort => "步骤端口",
            _ => "未配置",
        };

        public string CycleText => $"{CycleTimeMs:N0} ms";
        public string RoundText => $"#{RoundIndex}";
        public string StepsText => $"{CompletedStepCount} / {StepCount}";
        public string StartedText => StartedAtUtc.ToLocalTime().ToString("HH:mm:ss.fff");
        public string CompletedText => CompletedAtUtc.ToLocalTime().ToString("HH:mm:ss.fff");
    }

    /// <summary>
    /// 判定解析器：引擎在轮末调用，把"这一轮 OK/NG"从插件/变量里读出来。
    ///
    /// 为什么由宿主实现：引擎不依赖工作区/变量注册表；解析器在 VisionMaster 层实现，
    /// 引擎只依赖这个接口。没有注册解析器时，引擎记 <see cref="FlowRoundOutcome.Unknown"/>。
    /// </summary>
    public interface IFlowVerdictResolver
    {
        FlowVerdict Resolve(FlowSession session);
    }
}
