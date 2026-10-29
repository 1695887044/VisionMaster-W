using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using VisionMaster.Communications;

namespace VisionMaster.Binding
{
    /// <summary>
    /// <para>写批会话：把"一个步骤内的多次变量写"攒成一批，在步骤结束时一次性下发。</para>
    /// <para><b>为什么需要它</b>：变量写是同步阻塞的，N 次 <c>TryWrite</c> 就是 N 次设备往返。
    /// 但插件只写变量、不该关心什么时候发（"无感"要求），所以由执行器在步骤边界开/关会话，
    /// 插件代码一行不改。</para>
    /// <para><b>为什么按步骤切批</b>：步骤是流程里的顺序单位。步骤 1 写 D100、步骤 2 写 D101，
    /// 各自在步骤末尾冲刷 → 两帧、两个 PLC 扫描，顺序与今天一致。若按轮询周期攒批，
    /// 两步会落进同一帧，PLC 同一扫描看到两者，步骤边界就被抹平了。</para>
    /// <para><b>作用域</b>：<see cref="AsyncLocal{T}"/>，并行分支各自独立——两条分支的写绝不能合进同一帧。</para>
    /// <para><b>失败语义</b>：只有整批回执，没有逐项回执（设备只回"这一帧成功/失败"）。
    /// 失败项<b>不更新镜像</b>（镜像 = 设备真值，不能向界面撒谎），由调用方决定怎么报。</para>
    /// </summary>
    public sealed class WriteBatchSession : IDisposable
    {
        private static readonly AsyncLocal<WriteBatchSession?> CurrentSlot = new();

        /// <summary>当前上下文是否处于批会话中；为 null 时 <c>TryWrite</c> 行为与改造前逐字一致</summary>
        public static WriteBatchSession? Current => CurrentSlot.Value;

        /// <summary>开一个批会话（嵌套调用返回同一实例；后到的 urgent=true 会把整个会话升级为插队）</summary>
        public static WriteBatchSession Begin(bool urgent = false)
        {
            if (CurrentSlot.Value is { } existing)
            {
                if (urgent) existing.Urgent = true;
                return existing;
            }

            var session = new WriteBatchSession { Urgent = urgent };
            CurrentSlot.Value = session;
            return session;
        }

        /// <summary>
        /// 冲刷当前上下文的会话（没有会话时是空操作）。
        /// <para>给"写后立刻读"用：读之前必须把待写发出去，否则读到的是旧镜像——
        /// 对方源码复核也确认了这一点（它写完再读同样会读到旧值，且没有屏蔽机制）。</para>
        /// </summary>
        /// <returns>失败条数；无会话时为 0</returns>
        public static int FlushCurrent() => CurrentSlot.Value?.Flush() ?? 0;

        /// <summary>
        /// 是否走插队车道。由 <c>VariableAssignment</c> 的"等待完成"勾选设置——
        /// 勾上仍然合并（1 帧写 N 个值），只是排队位置提到轮询与常规读写之前。
        /// </summary>
        public bool Urgent { get; private set; }

        /// <summary>一条待写记录</summary>
        private sealed class Pending
        {
            public required string ConnectionName { get; init; }
            public required BatchWriteItem Item { get; init; }
            public required object? MirrorValue { get; init; }
            public required Action<object?> ApplyMirror { get; init; }
            public required Func<IReadOnlyList<BatchWriteItem>, BatchWriteResult> Flush { get; init; }
            public required Action<BatchWriteItemResult> OnFailed { get; init; }
        }

        private readonly List<Pending> _pending = new();
        private bool _disposed;

        /// <summary>待写条数（诊断用）</summary>
        public int PendingCount => _pending.Count;

        public bool IsEmpty => _pending.Count == 0;

        /// <summary>
        /// 记一笔待写。由 <c>NetworkVariableModel</c> 在会话内调用——调用方拿到 true 表示"已受理"，
        /// 不代表已到 PLC。
        /// </summary>
        public void Record(
            string connectionName,
            BatchWriteItem item,
            object? mirrorValue,
            Action<object?> applyMirror,
            Func<IReadOnlyList<BatchWriteItem>, BatchWriteResult> flush,
            Action<BatchWriteItemResult> onFailed)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(WriteBatchSession));

            _pending.Add(new Pending
            {
                ConnectionName = connectionName,
                Item = item,
                MirrorValue = mirrorValue,
                ApplyMirror = applyMirror,
                Flush = flush,
                OnFailed = onFailed,
            });
        }

        /// <summary>
        /// 冲刷：按连接分组下发，成功项才更新镜像。幂等——冲刷后待写清空，重复调用是空操作。
        /// </summary>
        /// <returns>失败条数</returns>
        public int Flush()
        {
            if (_pending.Count == 0) return 0;

            // 按连接分组，保持"同连接内按下标一一对应"：结果下标是组内下标，不是全局下标
            var groups = _pending
                .GroupBy(p => p.ConnectionName, StringComparer.Ordinal)
                .ToList();

            var snapshot = _pending.ToList();
            _pending.Clear();

            int failures = 0;

            foreach (var group in groups)
            {
                var records = group.ToList();
                var items = records.Select(r => r.Item).ToArray();

                BatchWriteResult result;
                try
                {
                    result = records[0].Flush(items);
                }
                catch (Exception ex)
                {
                    // 冲刷委托本身炸了：整组按"未尝试 + 失败"处理，绝不吞掉
                    var error = new BatchWriteItemResult(false, false, $"批写失败：{ex.Message}");
                    foreach (var record in records)
                        record.OnFailed(error);
                    failures += records.Count;
                    continue;
                }

                for (int i = 0; i < records.Count; i++)
                {
                    var itemResult = i < result.Items.Count
                        ? result.Items[i]
                        : new BatchWriteItemResult(false, false, "批写结果缺失");

                    if (itemResult.Success)
                    {
                        records[i].ApplyMirror(records[i].MirrorValue);
                    }
                    else
                    {
                        records[i].OnFailed(itemResult);
                        failures++;
                    }
                }
            }

            _ = snapshot;
            return failures;
        }

        /// <summary>退出会话作用域。不自动冲刷——冲刷必须显式，否则失败无处上报。</summary>
        public void Dispose()
        {
            _disposed = true;
            if (ReferenceEquals(CurrentSlot.Value, this))
                CurrentSlot.Value = null;
        }
    }
}
