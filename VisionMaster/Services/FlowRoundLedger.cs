using System;
using System.Collections.Generic;
using System.Linq;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 轮次台账：每流程保留最近 N 轮（旧 → 新），供下钻/诊断查询。
    ///
    /// 为什么单独成类：轮次记录是纯数据，宿主来源（FlowRuntimeVariableSource）要同时管
    /// 引擎事件、会话、快照；把"保留多少轮 / 怎么按流程隔离"拆出来，才能在没有引擎的
    /// 断言宿主里直接钉住（ScadaChecks 的 FRL-1~6）。
    ///
    /// 线程：本类自身不加锁，调用方（来源）在 _gate 内调用；单测是单线程。
    /// </summary>
    public sealed class FlowRoundLedger
    {
        private readonly int _capacity;
        private readonly Dictionary<Guid, Queue<FlowRoundResult>> _byFlow = new();

        public FlowRoundLedger(int capacity = 50)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
        }

        public int Capacity => _capacity;

        public void Add(FlowRoundResult record)
        {
            if (record == null || record.FlowId == Guid.Empty) return;

            if (!_byFlow.TryGetValue(record.FlowId, out var history))
            {
                history = new Queue<FlowRoundResult>();
                _byFlow[record.FlowId] = history;
            }

            history.Enqueue(record);
            while (history.Count > _capacity)
                history.Dequeue();
        }

        /// <summary>最近一轮；没有返回 null。</summary>
        public FlowRoundResult? Latest(Guid flowId)
        {
            if (!_byFlow.TryGetValue(flowId, out var history) || history.Count == 0)
                return null;

            return history.Last();
        }

        /// <summary>最近 max 轮（旧 → 新）；没有返回空。</summary>
        public IReadOnlyList<FlowRoundResult> Recent(Guid flowId, int max)
        {
            if (max <= 0 || !_byFlow.TryGetValue(flowId, out var history) || history.Count == 0)
                return Array.Empty<FlowRoundResult>();

            return history.Skip(Math.Max(0, history.Count - max)).ToList();
        }

        public int Count(Guid flowId)
            => _byFlow.TryGetValue(flowId, out var history) ? history.Count : 0;

        public void Remove(Guid flowId) => _byFlow.Remove(flowId);

        public void Clear() => _byFlow.Clear();
    }
}
