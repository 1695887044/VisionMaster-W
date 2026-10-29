using System;
using System.Collections.Generic;
using System.Linq;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>批写规划器：把 N 个写请求算成 M 个段（纯计算，不碰 socket、不碰队列）。</para>
    /// <para><b>只合并严格连续的同一区地址</b>（<c>prev.End == next.Start</c>）。空隙一律断开，
    /// 绝不用本地镜像填充——那条路会把 PLC 正在改的寄存器回滚成旧值（对方源码复核确认：
    /// 它填的是未初始化堆内存，等于往 PLC 写垃圾）。</para>
    /// <para><b>不进合并的四种情况</b>（各自成段、走单值写，行为与今天逐字一致）：
    /// ① 没有结构化地址；② 位区（位写语义不同，走 <c>Write(addr, bool[])</c>）；
    /// ③ 段地址覆盖串非空（U 区十六进制 / MC 八进制区，拼段地址会打到别的地址）；
    /// ④ 编码后字节数与地址跨度不符（窄类型提升口径不同，合并会多写邻居字节）。</para>
    /// </summary>
    internal static class WriteBatchPlanner
    {
        /// <summary>一段的规划结果</summary>
        internal sealed class Segment
        {
            public required string Address { get; init; }
            public required int Start { get; init; }
            public required int SpanUnits { get; set; }

            /// <summary>合并段的载荷（按 <see cref="PollAddress.BytesPerUnit"/> 槽位排布）；null = 单项走强类型写</summary>
            public byte[]? Payload { get; set; }

            /// <summary>本段覆盖的请求下标（与 <see cref="BatchWriteItem"/> 下标一致）</summary>
            public List<int> ItemIndices { get; } = new();

            /// <summary>是否合并段（false = 单项走 <c>conn.Write(addr, value)</c>）</summary>
            public bool Merged => Payload != null;
        }

        /// <summary>
        /// 规划一批写。返回的段按"覆盖的最小请求下标"排序，便于按段报错时对应到行号区间。
        /// </summary>
        internal static List<Segment> Build(
            IReadOnlyList<BatchWriteItem> items,
            WriteProtocolFamily family,
            ByteOrderFormat order)
        {
            var segments = new List<Segment>();
            var mergeGroups = new Dictionary<(PollProtocol, string), List<int>>();

            for (int i = 0; i < items.Count; i++)
            {
                if (!CanMerge(items[i], family, out _))
                {
                    segments.Add(Single(items[i], i));
                    continue;
                }

                var key = (items[i].Poll!.Protocol, items[i].Poll!.GroupKey);
                if (!mergeGroups.TryGetValue(key, out var list))
                    mergeGroups[key] = list = new List<int>();
                list.Add(i);
            }

            foreach (var entry in mergeGroups)
            {
                var indices = entry.Value
                    .OrderBy(i => items[i].Poll!.Start)
                    .ThenBy(i => items[i].Poll!.SpanUnits)
                    .ToList();

                Segment? current = null;
                foreach (int index in indices)
                {
                    var poll = items[index].Poll!;
                    int slotBytes = poll.SpanUnits * poll.BytesPerUnit;
                    byte[] encoded = HslHelper.EncodeForWire(items[index].Value, family, order);

                    if (current != null)
                    {
                        bool contiguous = poll.Start == current.Start + current.SpanUnits;
                        bool withinLimit = current.SpanUnits + poll.SpanUnits <= poll.MaxWriteUnits;
                        if (!contiguous || !withinLimit)
                        {
                            segments.Add(current);
                            current = null;
                        }
                    }

                    if (current == null)
                    {
                        current = new Segment
                        {
                            Address = poll.SegmentAddress,
                            Start = poll.Start,
                            SpanUnits = poll.SpanUnits,
                            Payload = new byte[slotBytes],
                        };
                        Array.Copy(encoded, current.Payload, slotBytes);
                        current.ItemIndices.Add(index);
                        continue;
                    }

                    int offset = (poll.Start - current.Start) * poll.BytesPerUnit;
                    var grown = new byte[(current.SpanUnits + poll.SpanUnits) * poll.BytesPerUnit];
                    Array.Copy(current.Payload!, grown, current.Payload!.Length);
                    Array.Copy(encoded, 0, grown, offset, slotBytes);
                    current.Payload = grown;
                    current.SpanUnits += poll.SpanUnits;
                    current.ItemIndices.Add(index);
                }

                if (current != null)
                    segments.Add(current);
            }

            return segments.OrderBy(s => s.ItemIndices[0]).ToList();
        }

        /// <summary>
        /// 该项能否进合并段。四种否决情况见类型注释。
        /// </summary>
        private static bool CanMerge(BatchWriteItem item, WriteProtocolFamily family, out string reason)
        {
            reason = string.Empty;

            if (item.Poll == null)
            {
                reason = "没有结构化地址";
                return false;
            }

            if (item.Poll.IsBitArea)
            {
                reason = "位区（位写语义不同）";
                return false;
            }

            if (item.Poll.SegmentAddressOverride != null)
            {
                reason = "段地址是十六/八进制编址区（拼段地址会错位）";
                return false;
            }

            int slotBytes = item.Poll.SpanUnits * item.Poll.BytesPerUnit;
            if (slotBytes <= 0)
            {
                reason = "地址跨度无效";
                return false;
            }

            // 编码后字节数与槽位必须相等：不等说明窄类型提升口径与地址跨度不一致（如 EasyNet 的 byte），
            // 合并会写到邻居字节上——这种情况退回单值写，宁可多一次往返。
            int encoded;
            try
            {
                encoded = HslHelper.EncodeForWire(item.Value, family, ByteOrderFormat.ABCD).Length;
            }
            catch (Exception ex)
            {
                reason = $"无法编码：{ex.Message}";
                return false;
            }

            if (encoded != slotBytes)
            {
                reason = $"编码 {encoded} 字节与地址跨度 {slotBytes} 字节不一致";
                return false;
            }

            return true;
        }

        private static Segment Single(BatchWriteItem item, int index)
        {
            var segment = new Segment
            {
                Address = item.Address,
                Start = item.Poll?.Start ?? 0,
                SpanUnits = item.Poll?.SpanUnits ?? 0,
                Payload = null,
            };
            segment.ItemIndices.Add(index);
            return segment;
        }
    }

    /// <summary>
    /// <para>批执行器：按段依次下发，段间可让路、段失败即停。</para>
    /// <para>从 <c>AdvancedCommunicationManager.TryWriteBatch</c> 里抽出来单独一个类，只为<b>可测</b>：
    /// 让路与短路两条语义要能注入判据（<c>shouldYield</c> / 执行委托），否则只能靠真设备碰运气。</para>
    /// </summary>
    internal static class WriteBatchExecutor
    {
        /// <summary>一段怎么发：合并段走 WriteBytes，单项走强类型 Write</summary>
        internal delegate void SegmentWriter(WriteBatchPlanner.Segment segment, BatchWriteItem item);

        /// <summary>执行结果</summary>
        internal sealed class Outcome
        {
            public required BatchWriteItemResult[] Items { get; init; }
            public required BatchWriteSegmentResult[] Segments { get; init; }
            public bool StoppedByLinkFault { get; set; }
            public bool YieldedToUrgent { get; set; }
        }

        /// <summary>
        /// 逐段执行。
        /// <para>让路判据在<b>每段之前</b>问一次：命中即停，剩余段与其中项标 <c>Attempted=false</c>
        /// （"没发出去"与"发了失败"必须能分开）。让路不是故障，<c>StoppedByLinkFault</c> 保持 false。</para>
        /// <para>段失败即停：已发出去的收不回，后续段不再发。</para>
        /// </summary>
        internal static Outcome Run(
            IReadOnlyList<WriteBatchPlanner.Segment> segments,
            IReadOnlyList<BatchWriteItem> items,
            SegmentWriter write,
            Func<bool>? shouldYield,
            Func<bool>? hasLinkFault)
        {
            var itemResults = new BatchWriteItemResult[items.Count];
            var segmentResults = new BatchWriteSegmentResult[segments.Count];
            var outcome = new Outcome { Items = itemResults, Segments = segmentResults };

            for (int s = 0; s < segments.Count; s++)
            {
                var segment = segments[s];

                if (shouldYield?.Invoke() == true)
                {
                    outcome.YieldedToUrgent = true;
                    MarkNotAttempted(s, segments, itemResults, segmentResults, "有插队命令待执行，本段让路");
                    return outcome;
                }

                bool success = false;
                string? error = null;
                try
                {
                    if (segment.Merged)
                        write(segment, items[segment.ItemIndices[0]]);
                    else
                        write(segment, items[segment.ItemIndices[0]]);
                    success = true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    outcome.StoppedByLinkFault = hasLinkFault?.Invoke() == true;
                }

                segmentResults[s] = new BatchWriteSegmentResult
                {
                    Address = segment.Address,
                    SpanUnits = segment.SpanUnits,
                    Attempted = true,
                    Success = success,
                    Error = error,
                    ItemIndices = segment.ItemIndices.ToArray(),
                };

                foreach (int index in segment.ItemIndices)
                    itemResults[index] = new BatchWriteItemResult(true, success, error);

                if (!success)
                {
                    MarkNotAttempted(s + 1, segments, itemResults, segmentResults,
                        outcome.StoppedByLinkFault ? "链路已断开，本段未下发" : "前面的段已失败，本段未下发");
                    return outcome;
                }
            }

            return outcome;
        }

        private static void MarkNotAttempted(
            int fromIndex,
            IReadOnlyList<WriteBatchPlanner.Segment> segments,
            BatchWriteItemResult[] itemResults,
            BatchWriteSegmentResult[] segmentResults,
            string reason)
        {
            for (int s = fromIndex; s < segments.Count; s++)
            {
                segmentResults[s] = new BatchWriteSegmentResult
                {
                    Address = segments[s].Address,
                    SpanUnits = segments[s].SpanUnits,
                    Attempted = false,
                    Success = false,
                    Error = reason,
                    ItemIndices = segments[s].ItemIndices.ToArray(),
                };

                foreach (int index in segments[s].ItemIndices)
                    itemResults[index] = new BatchWriteItemResult(false, false, reason);
            }
        }
    }
}
