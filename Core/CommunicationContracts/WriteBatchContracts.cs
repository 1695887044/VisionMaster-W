using System;
using System.Collections.Generic;
using System.Linq;

namespace VisionMaster.Communications
{
    /// <summary>批写的一项：调用方给出的原始请求。</summary>
    public sealed class BatchWriteItem
    {
        /// <summary>设备地址串（未解析出结构化地址时直接用这个地址做单值写）</summary>
        public required string Address { get; init; }

        /// <summary>结构化地址；为 null 表示该协议没有结构化模型（走单值写，不参与合并）</summary>
        public PollAddress? Poll { get; init; }

        /// <summary>要写入的值（已按变量声明类型归一）</summary>
        public required object Value { get; init; }

        /// <summary>变量的声明类型</summary>
        public required Type ValueType { get; init; }
    }

    /// <summary>
    /// 一项的写入结果。
    /// <para><b><see cref="Attempted"/> 与 <see cref="Success"/> 必须分开</b>：前者 false 表示"没发出去"
    /// （前面的段已失败 / 链路已断），后者 false 表示"发了但设备没接受"。混成一个 bool 就分不清
    /// "没跑"和"跑红了"。</para>
    /// </summary>
    public readonly record struct BatchWriteItemResult(bool Attempted, bool Success, string? Error);

    /// <summary>一段（一次设备往返）的结果。合并写的最小可判定单位就是段——设备只回"这一帧成功/失败"。</summary>
    public sealed class BatchWriteSegmentResult
    {
        public required string Address { get; init; }
        public required int SpanUnits { get; init; }
        public required bool Attempted { get; init; }
        public required bool Success { get; init; }
        public string? Error { get; init; }

        /// <summary>本段覆盖的请求下标（升序）</summary>
        public required IReadOnlyList<int> ItemIndices { get; init; }
    }

    /// <summary>批写结果：逐项 + 逐段。</summary>
    public sealed class BatchWriteResult
    {
        public BatchWriteResult(
            IReadOnlyList<BatchWriteItemResult> items,
            IReadOnlyList<BatchWriteSegmentResult> segments,
            bool stoppedByLinkFault)
        {
            Items = items;
            Segments = segments;
            StoppedByLinkFault = stoppedByLinkFault;
        }

        /// <summary>与请求<b>按下标一一对应</b>的逐项结果</summary>
        public IReadOnlyList<BatchWriteItemResult> Items { get; }

        public IReadOnlyList<BatchWriteSegmentResult> Segments { get; }

        /// <summary>是否因链路自证断开而提前停止（此时后续项 <c>Attempted=false</c>）</summary>
        public bool StoppedByLinkFault { get; }

        /// <summary>便捷属性：只回答"全都没问题吗"，逐项细节一律走 <see cref="Items"/></summary>
        public bool AllSucceeded => Items.All(x => x.Success);

        public int AttemptedCount => Items.Count(x => x.Attempted);
    }
}
