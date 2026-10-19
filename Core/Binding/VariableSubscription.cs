using System;
using System.Collections.Generic;
using System.Linq;
using Core.Interfaces;
using VisionMaster.Models;

namespace VisionMaster.Binding
{
    /// <summary>
    /// 轻量变量订阅（对标商业 SCADA 的"发布订阅"层）。
    ///
    /// 为什么需要它：设备扫描 50ms、画面刷新 200ms、报警判定 50ms、历史 1s，
    /// 三者速率不同。若都直接订阅变量的 ValueChanged，画面会被高频事件刷爆，
    /// 而历史侧又希望拿到每一次变化。
    ///
    /// 三条口径：
    /// 1) <b>每个订阅方独立发布周期</b>（<see cref="PublishingIntervalMs"/>）：到点才回调一次；
    /// 2) <b>队列只保留最新</b>（QueueSize = 1）：消费方跟不上时丢中间值，不无限排队；
    /// 3) <b>丢了多少要计数</b>（<see cref="DroppedCount"/>）：静默丢更新等于隐瞒故障。
    ///
    /// 线程：变量变化可能在任意线程触发（设备 Worker / 发布器），本类内部加锁；
    /// 回调在调用方线程上触发，订阅方不得在其中阻塞。
    /// </summary>
    public sealed class VariableSubscription : IDisposable
    {
        private sealed class Entry
        {
            public IVariable Variable = null!;
            public object? LastPublishedValue;
            public bool HasPending;
            public object? PendingValue;
            public long LastPublishTicks;
            public long Dropped;
        }

        private readonly object _gate = new();
        private readonly Dictionary<Guid, Entry> _entries = new();
        private readonly List<IVariable> _subscribed = new();
        private readonly Action<IReadOnlyList<(IVariable Variable, object? Value)>> _callback;
        private readonly int _publishingIntervalMs;
        private bool _disposed;

        /// <param name="publishingIntervalMs">发布周期（毫秒）。100~250ms 适合画面，50ms 适合报警。</param>
        /// <param name="callback">到点回调：一次拿到本周期内所有变化变量（最新值）。</param>
        public VariableSubscription(
            int publishingIntervalMs,
            Action<IReadOnlyList<(IVariable Variable, object? Value)>> callback)
        {
            _publishingIntervalMs = Math.Max(1, publishingIntervalMs);
            _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        public int PublishingIntervalMs => _publishingIntervalMs;

        /// <summary>累计丢弃的中间更新数（队列只留最新，丢的计在这里）。</summary>
        public long DroppedCount
        {
            get { lock (_gate) { long sum = 0; foreach (var e in _entries.Values) sum += e.Dropped; return sum; } }
        }

        public int Count { get { lock (_gate) return _entries.Count; } }

        public void Subscribe(IVariable variable)
        {
            if (variable == null) return;
            lock (_gate)
            {
                if (_disposed) return;
                if (_entries.ContainsKey(variable.VariableId)) return;

                var entry = new Entry
                {
                    Variable = variable,
                    LastPublishedValue = variable.Value,
                    LastPublishTicks = Environment.TickCount64
                };
                _entries[variable.VariableId] = entry;
                variable.ValueChanged += OnValueChanged;
                _subscribed.Add(variable);
            }
        }

        public void Unsubscribe(IVariable variable)
        {
            if (variable == null) return;
            lock (_gate)
            {
                if (!_entries.Remove(variable.VariableId)) return;
                variable.ValueChanged -= OnValueChanged;
                _subscribed.Remove(variable);
            }
        }

        private void OnValueChanged(object? sender, EventArgs e)
        {
            if (sender is not IVariable variable) return;

            lock (_gate)
            {
                if (_disposed) return;
                if (!_entries.TryGetValue(variable.VariableId, out var entry)) return;

                // 队列容量 1：已有待发布值就覆盖并记一次丢弃
                if (entry.HasPending)
                    entry.Dropped++;

                entry.PendingValue = variable.Value;
                entry.HasPending = true;
            }
        }

        /// <summary>
        /// 推进发布：到期的变量各发一次（带最新值）。由宿主定时器调用（例如 50ms 一拍）。
        /// 返回本次实际发布的条数。
        /// </summary>
        public int Pump()
        {
            List<(IVariable Variable, object? Value)>? batch = null;
            long now = Environment.TickCount64;

            lock (_gate)
            {
                if (_disposed) return 0;

                foreach (var entry in _entries.Values)
                {
                    if (now - entry.LastPublishTicks < _publishingIntervalMs) continue;
                    entry.LastPublishTicks = now;

                    if (!entry.HasPending) continue;
                    entry.HasPending = false;

                    batch ??= new List<(IVariable, object?)>();
                    batch.Add((entry.Variable, entry.PendingValue));
                    entry.LastPublishedValue = entry.PendingValue;
                }
            }

            if (batch == null || batch.Count == 0) return 0;

            try { _callback(batch); }
            catch { /* 订阅方异常不得影响其它订阅方与设备线程 */ }

            return batch.Count;
        }

        public void Dispose()
        {
            List<IVariable> variables;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                variables = _subscribed.ToList();
                _entries.Clear();
                _subscribed.Clear();
            }

            foreach (var variable in variables)
            {
                try { variable.ValueChanged -= OnValueChanged; } catch { }
            }
        }
    }
}
