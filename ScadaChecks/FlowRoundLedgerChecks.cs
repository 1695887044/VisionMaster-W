using System;
using Core.Interfaces;
using VisionMaster.Models;
using VisionMaster.Services;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [FRL] 轮次台账：按流程隔离 / 最近 N 轮 / 容量裁剪 / 删除与清空。
        /// 这是轮次下钻的数据底座；没有它，"最近一轮"只能靠 UI 自己猜。
        /// </summary>
        private static void FlowRoundLedgerChecks()
        {
            Section("[FRL] 轮次台账：隔离 / 最近 N 轮 / 容量裁剪");

            var ledger = new FlowRoundLedger(capacity: 2);
            Guid a = Guid.NewGuid();
            Guid b = Guid.NewGuid();

            ledger.Add(Rec(a, 1));
            ledger.Add(Rec(a, 2));
            ledger.Add(Rec(a, 3));
            ledger.Add(Rec(b, 1));
            ledger.Add(Rec(b, 2));

            Check("FRL-1 容量 2：A 只留最近 2 轮", ledger.Count(a) == 2, $"{ledger.Count(a)}");
            Check("FRL-2 Latest(A) = #3", ledger.Latest(a)?.RoundIndex == 3, $"{ledger.Latest(a)?.RoundIndex}");
            var recent = ledger.Recent(a, 2);
            Check("FRL-3 Recent(A,2) = [#2,#3]（旧→新）",
                recent.Count == 2 && recent[0].RoundIndex == 2 && recent[1].RoundIndex == 3,
                string.Join(",", System.Linq.Enumerable.Select(recent, r => r.RoundIndex)));
            Check("FRL-4 B 不被 A 影响：Latest(B)=#2", ledger.Latest(b)?.RoundIndex == 2, $"{ledger.Latest(b)?.RoundIndex}");

            ledger.Remove(a);
            Check("FRL-5 Remove(A)：A 清空，B 保留",
                ledger.Latest(a) == null && ledger.Count(a) == 0 && ledger.Latest(b)?.RoundIndex == 2,
                $"A={ledger.Count(a)} B={ledger.Latest(b)?.RoundIndex}");

            ledger.Add(null!);
            ledger.Add(new FlowRoundResult { FlowId = Guid.Empty, RoundIndex = 9 });
            Check("FRL-6 null / Guid.Empty 不落账", ledger.Count(Guid.Empty) == 0, $"{ledger.Count(Guid.Empty)}");

            Check("FRL-7 Recent(未知流程) = 空、Recent(A,0) = 空",
                ledger.Recent(Guid.NewGuid(), 5).Count == 0 && ledger.Recent(b, 0).Count == 0,
                "empty");

            ledger.Clear();
            Check("FRL-8 Clear 后全空", ledger.Latest(b) == null && ledger.Count(b) == 0, "empty");

            Check("FRL-9 默认容量 50", new FlowRoundLedger().Capacity == 50, "50");
        }

        private static FlowRoundResult Rec(Guid flowId, long index)
            => new()
            {
                FlowId = flowId,
                FlowName = "Flow_" + flowId.ToString("N").Substring(0, 4),
                RoundIndex = index,
                StartedAtUtc = DateTime.UtcNow.AddMilliseconds(-500),
                CompletedAtUtc = DateTime.UtcNow,
                CycleTimeMs = 500,
                Outcome = index % 2 == 0 ? FlowRoundOutcome.Ng : FlowRoundOutcome.Ok,
                Reason = "test",
                VerdictSource = FlowVerdictSource.ResultStep,
                FailedStepName = index % 2 == 0 ? "判定表 · 外观" : string.Empty,
                StepCount = 10,
                CompletedStepCount = 10,
            };
    }
}
