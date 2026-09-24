using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>批量轮询规划器：把"变量清单"编译成"段读 + 兜底单读"的轮询委托，供 <see cref="PollScheduler"/> 按扫描组各持一份使用。</para>
    /// <para>为什么需要它：旧实现每个变量发一条读命令（N 个变量 = N 次往返），在 20~50ms 周期下既打满网络又拖长扫描时间。
    /// 批量化思路：同协议、同存储区、地址相近的变量合并成一次块读，一次读回一整片区域，再在内存里切片解码。</para>
    /// <para>编译期做三件事：</para>
    /// <para>1) 规划——按 协议+存储区 分组、按起始地址排序、贪心合并成若干"段"（间隙容忍 + 单请求上限封顶）；</para>
    /// <para>2) 兜底——地址无法结构化解析、类型无法解码、或类型字节数大于地址跨度的变量，退回旧的按类型单读；</para>
    /// <para>3) 预编译——解码所需信息（段内偏移、位序号、目标类型）在编译期算好，轮询时零解析零反射。</para>
    /// <para>执行契约（与 <see cref="ConnectionWorker"/> 的约定）：返回 true = 本轮通信成功；返回 false = 通信级故障（触发断线重连）。
    /// 判定规则：只有"本轮所有读都失败"才判为通信级故障——只要有一次读成功就说明链路是活的，
    /// 个别段失败按变量级错误消化（限量日志），避免一个坏地址把好连接打进无限重连循环。</para>
    /// <para>"个别段恒坏"不等于"没事"：另有一条<b>旁路健康通道</b>——每个段各自累计"连续失败轮数"，
    /// 连续达 <see cref="FaultedSegmentThreshold"/> 轮即计为异常段（此刻发一条不节流告警），
    /// 由 <see cref="GetHealthSnapshot"/> 上到诊断面板。它与上面的返回值判定<b>互不干扰</b>。</para>
    /// <para>线程约定：<see cref="Poll"/> 只由所属连接的 Worker 线程调用，故内部无锁。</para>
    /// </summary>
    public sealed class PollBatchPlanner
    {
        #region 编译期数据结构

        /// <summary>段内一个变量的解码信息（编译期算好，运行期不再解析字符串）</summary>
        private sealed class SegmentItem
        {
            public CommunicationVariable Variable = null!;

            /// <summary>相对段起点的单元偏移</summary>
            public int UnitOffset;

            /// <summary>本变量占用单元数（S7=字节；Modbus 寄存器区=寄存器；位区=点）</summary>
            public int SpanUnits;

            /// <summary>位访问的 bit 序号（0-7）；-1 = 非位访问</summary>
            public int BitOffset = -1;

            /// <summary>目标 CLR 类型（已剥掉 Nullable）</summary>
            public Type ValueType = null!;

            public bool IsString;
            public bool IsByteArray;
        }

        /// <summary>一次块读覆盖的连续区域 + 区域内所有变量</summary>
        private sealed class ReadSegment
        {
            public PollProtocol Protocol;
            public string GroupKey = string.Empty;

            /// <summary>
            /// 本段所属连接名（编译期从段内首个变量取）。
            /// <para>C5：限流日志表 <see cref="_logTicks"/> 是 <c>static</c>（跨连接共享），
            /// 若 key 里不含连接名，两条连接读<b>同一个地址</b>时会共用同一个限流窗口——
            /// 后一条的告警被前一条的 5 秒窗口吞掉，日志里永远看不到第二台设备出问题。</para>
            /// </summary>
            public string ConnectionName = string.Empty;

            public string SegmentPrefix = string.Empty;
            public int Start;
            public int EndUnit;

            /// <summary>是否 Modbus 位区（线圈/离散输入，按位打包返回）</summary>
            public bool IsBitArea;

            public readonly List<SegmentItem> Items = new();

            /// <summary>本段可用的 HSL 地址（如 "x=3;100" / "MB100" / "DB7.DBB100"）</summary>
            public string Address => SegmentPrefix + Start;

            /// <summary>段跨度（单元数）</summary>
            public int SpanUnits => EndUnit - Start;

            /// <summary>段内每个单元占用的字节数（Modbus 寄存器区 1 单元 = 2 字节，其余 1:1）</summary>
            public int BytesPerUnit => Protocol == PollProtocol.Modbus && !IsBitArea ? 2 : 1;

            /// <summary>
            /// 本段"连续失败"的轮数（成功一轮即归零）。
            /// 用途：把"某个地址恒坏"从"仅一条限流日志"提升为"诊断面板可见 + 跨阈值一次告警"。
            /// 线程：仅 Worker 线程写，UI 线程会读（<see cref="GetHealthSnapshot"/>），故读写都走 <see cref="Volatile"/>。
            /// </summary>
            public int ConsecutiveFailures;

            /// <summary>最近一次失败原因（成功时清空）；与 <see cref="ConsecutiveFailures"/> 一起构成坏段摘要</summary>
            public string? LastError;
        }

        /// <summary>兜底单读项：仍走 <see cref="ICommunicationConnection.Read{T}"/> 反射调用</summary>
        private sealed class SingleRead
        {
            public CommunicationVariable Variable = null!;
            public MethodInfo ReadMethod = null!;
        }

        #endregion

        #region 编译期参数

        // 间隙容忍：两段之间允许"多读"的空洞上限。太小则合并率低，太大则每轮白读大量无关数据。
        private const int GapUnitsS7 = 16;              // S7 按字节编址，16 字节空洞可接受
        private const int GapUnitsModbusRegister = 8;   // Modbus 寄存器区，8 个寄存器（16 字节）
        private const int GapUnitsModbusBit = 64;       // Modbus 位区，64 个点位

        /// <summary>同一 key 的失败日志最短间隔（毫秒），防轮询周期级刷屏</summary>
        private const long LogThrottleMs = 5000;

        /// <summary>
        /// 段"连续失败"多少轮才算<b>异常段</b>（诊断面板据此计数、并在此刻发一条不节流的告警）。
        /// <para>取 3 而不是 1：单轮抖动（PLC 忙、瞬时丢包）不该报警；连续 3 轮都失败，
        /// 才说明这个地址是真的读不到（配置错、越界、存储区不存在）。</para>
        /// </summary>
        public const int FaultedSegmentThreshold = 3;

        private static readonly ConcurrentDictionary<string, long> _logTicks = new();

        private static readonly ConcurrentDictionary<Type, MethodInfo> _readMethodCache = new();

        #endregion

        private readonly List<ReadSegment> _segments;
        private readonly List<SingleRead> _fallbacks;
        private readonly Action<string>? _log;

        private PollBatchPlanner(List<ReadSegment> segments, List<SingleRead> fallbacks, Action<string>? log)
        {
            _segments = segments;
            _fallbacks = fallbacks;
            _log = log;
        }

        #region 公共属性

        /// <summary>合并后的段读数量（= 每轮设备往返次数）</summary>
        public int SegmentCount => _segments.Count;

        /// <summary>兜底单读数量</summary>
        public int FallbackCount => _fallbacks.Count;

        /// <summary>参与轮询的读写项总数；为 0 表示没有可轮询变量（上层应把调度器置空，避免空转）</summary>
        public int PollItemCount => _segments.Count + _fallbacks.Count;

        /// <summary>
        /// 段健康快照（供诊断面板读取）：异常段数 + 首个坏段摘要。
        /// <para>为什么只报"首个"：面板只有一列，塞 N 条摘要读不了；先把最典型的那条摆出来，完整清单靠日志。
        /// 异常段数用 <c>x/y</c> 形式显示（y 取 <see cref="SegmentCount"/>），能同时看出"坏了几个"和"一共几段"。</para>
        /// <para>注意：这里**不参与** <see cref="Poll"/> 的返回值判定——链路活性只看"是否全失败"，
        /// 坏段只走这条旁路通道，避免一个坏地址把好连接打进无限重连循环。</para>
        /// <para>线程：段状态仅 Worker 线程写，本方法可由 UI 线程调用；读到的最多是"上一拍"的值，诊断够用。</para>
        /// </summary>
        public (int FaultedCount, string? FirstDetail) GetHealthSnapshot()
        {
            int faulted = 0;
            string? first = null;

            foreach (var seg in _segments)
            {
                int failures = Volatile.Read(ref seg.ConsecutiveFailures);
                if (failures < FaultedSegmentThreshold) continue;

                faulted++;
                first ??= $"{seg.Address}（连续失败 {failures} 轮）：{seg.LastError}";
            }

            return (faulted, first);
        }

        #endregion

        #region 编译：变量清单 → 轮询委托

        /// <summary>
        /// 编译轮询计划。
        /// </summary>
        /// <param name="variables">某连接下的全部已注册变量</param>
        /// <param name="log">编译期告警输出（只写不可行的变量，不进热路径）</param>
        public static PollBatchPlanner Build(IEnumerable<CommunicationVariable> variables, Action<string>? log = null)
        {
            var batchable = new List<(PollAddress Address, CommunicationVariable Variable)>();
            var fallbacks = new List<SingleRead>();

            foreach (var variable in variables ?? Enumerable.Empty<CommunicationVariable>())
            {
                if (variable == null || variable.AccessMode == VariableAccessMode.WriteOnly)
                    continue; // 只写变量不轮询

                var valueType = ResolveClrType(variable.ValueType);
                if (valueType == null)
                {
                    log?.Invoke($"变量 {variable.ConnectionName}.{variable.VariableName} 的值类型无效（{variable.ValueType}），已跳过轮询");
                    continue;
                }

                var poll = PollAddressResolver.Resolve(variable);
                if (poll == null)
                {
                    // 地址不是可批量形式（如 S7 的 SM/AI 区、非法串）：能单读就单读
                    if (TryCreateSingleRead(variable, valueType, out var single))
                    {
                        fallbacks.Add(single);
                        continue;
                    }
                    log?.Invoke($"变量 {variable.ConnectionName}.{variable.VariableName} 地址无法解析且类型不支持单读（{variable.Address} / {valueType.Name}），已跳过轮询");
                    continue;
                }

                // 变长类型：必须有结构化地址配置才知道读多长（旧字符串地址推不出长度）
                bool isString = valueType == typeof(string);
                bool isByteArray = valueType == typeof(byte[]);
                if (isString || isByteArray)
                {
                    if (variable.AddressConfig == null)
                    {
                        log?.Invoke($"变量 {variable.ConnectionName}.{variable.VariableName} 是变长类型（{valueType.Name}）但缺少结构化地址配置，无法确定长度，已跳过轮询");
                        continue;
                    }
                }
                else if (HslHelper.MinByteCount(valueType) == 0)
                {
                    log?.Invoke($"变量 {variable.ConnectionName}.{variable.VariableName} 的类型 {valueType.Name} 无法按字节流解码，已跳过轮询");
                    continue;
                }

                // 类型所需字节数 > 地址声明的跨度：批量切片必然解错，退回单读保证值正确
                if (!isString && !isByteArray && !poll.IsBitArea && !poll.IsBitAccess
                    && HslHelper.MinByteCount(valueType) > poll.ValueByteLength)
                {
                    if (TryCreateSingleRead(variable, valueType, out var single))
                    {
                        fallbacks.Add(single);
                        log?.Invoke($"变量 {variable.ConnectionName}.{variable.VariableName} 的类型 {valueType.Name} 需 {HslHelper.MinByteCount(valueType)} 字节，" +
                                    $"大于地址跨度 {poll.ValueByteLength} 字节，改用单读");
                    }
                    continue;
                }

                // 单个变量自身就超过单请求上限（如 S7 上超长字符串）：无法做成一次块读
                if (poll.SpanUnits > poll.MaxSegmentUnits)
                {
                    if (!isString && !isByteArray && TryCreateSingleRead(variable, valueType, out var tooLong))
                    {
                        fallbacks.Add(tooLong);
                        log?.Invoke($"变量 {variable.ConnectionName}.{variable.VariableName} 跨度为 {poll.SpanUnits} 单元，" +
                                    $"超过单请求上限 {poll.MaxSegmentUnits}，改用单读");
                    }
                    else
                    {
                        log?.Invoke($"变量 {variable.ConnectionName}.{variable.VariableName} 跨度为 {poll.SpanUnits} 单元，" +
                                    $"超过单请求上限 {poll.MaxSegmentUnits}，无法轮询");
                    }
                    continue;
                }

                batchable.Add((poll, variable));
            }

            var segments = MergeSegments(batchable);
            CompileItems(segments, batchable);
            return new PollBatchPlanner(segments, fallbacks, log);
        }

        /// <summary>按 协议+存储区 分组、起始地址排序后贪心合并成段</summary>
        private static List<ReadSegment> MergeSegments(List<(PollAddress Address, CommunicationVariable Variable)> batchable)
        {
            var result = new List<ReadSegment>();

            foreach (var group in batchable.GroupBy(x => (x.Address.Protocol, x.Address.GroupKey)))
            {
                ReadSegment? current = null;

                foreach (var (poll, variable) in group.OrderBy(x => x.Address.Start).ThenBy(x => x.Address.EndUnit))
                {
                    if (current != null)
                    {
                        int newEnd = Math.Max(current.EndUnit, poll.EndUnit);
                        bool overflow = newEnd - current.Start > poll.MaxSegmentUnits; // 段过长 → 超过单请求上限
                        bool tooFar = poll.Start - current.EndUnit > GapOf(poll);      // 空洞太大 → 白读太多

                        if (overflow || tooFar)
                        {
                            result.Add(current);
                            current = null;
                        }
                    }

                    if (current == null)
                    {
                        current = new ReadSegment
                        {
                            Protocol = poll.Protocol,
                            GroupKey = poll.GroupKey,
                            ConnectionName = variable.ConnectionName, // C5：限流 key 需要它，编译期取一次
                            SegmentPrefix = poll.SegmentPrefix,
                            Start = poll.Start,
                            EndUnit = poll.EndUnit,
                            IsBitArea = poll.IsBitArea
                        };
                    }

                    current.EndUnit = Math.Max(current.EndUnit, poll.EndUnit);
                }

                if (current != null)
                    result.Add(current);
            }

            return result;
        }

        /// <summary>把变量挂到所属段上，并预计算段内偏移 / 位序号 / 目标类型</summary>
        private static void CompileItems(
            List<ReadSegment> segments,
            List<(PollAddress Address, CommunicationVariable Variable)> batchable)
        {
            // 段划分是按 (Protocol, GroupKey) 顺序生成的，这里用同样的键+区间包含关系回落
            foreach (var (poll, variable) in batchable)
            {
                var seg = segments.FirstOrDefault(s =>
                    s.Protocol == poll.Protocol
                    && s.GroupKey == poll.GroupKey
                    && poll.Start >= s.Start
                    && poll.EndUnit <= s.EndUnit);

                if (seg == null)
                    continue; // 理论上不会发生；万一漏挂，该变量本轮不更新（安全侧）

                var clrType = ResolveClrType(variable.ValueType)!;
                seg.Items.Add(new SegmentItem
                {
                    Variable = variable,
                    UnitOffset = poll.Start - seg.Start,
                    SpanUnits = poll.SpanUnits,
                    BitOffset = poll.BitOffset,
                    ValueType = clrType,
                    IsString = clrType == typeof(string),
                    IsByteArray = clrType == typeof(byte[])
                });
            }
        }

        private static int GapOf(PollAddress poll)
        {
            if (poll.Protocol == PollProtocol.S7) return GapUnitsS7;
            return poll.IsBitArea ? GapUnitsModbusBit : GapUnitsModbusRegister;
        }

        /// <summary>值类型字符串 → CLR 类型（剥掉 Nullable，无效返回 null）</summary>
        private static Type? ResolveClrType(string? typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return null;
            var t = Type.GetType(typeName);
            if (t == null) return null;
            return Nullable.GetUnderlyingType(t) ?? t;
        }

        private static bool TryCreateSingleRead(CommunicationVariable variable, Type valueType, out SingleRead single)
        {
            single = null!;
            if (!valueType.IsValueType) return false; // Read<T> 约束 T : struct

            var method = _readMethodCache.GetOrAdd(valueType, t => typeof(ICommunicationConnection)
                .GetMethod(nameof(ICommunicationConnection.Read))!
                .MakeGenericMethod(t));

            single = new SingleRead { Variable = variable, ReadMethod = method };
            return true;
        }

        #endregion

        #region 运行：一轮轮询

        /// <summary>执行一轮轮询（仅由所属连接的 Worker 线程调用）</summary>
        public bool Poll(ICommunicationConnection connection)
        {
            int attempted = 0;
            int failed = 0;

            // 字序取自连接本身（ModbusTcp 跟随配置，S7/串口恒 ABCD），保证与单点读同源
            var byteOrder = connection.ByteOrder;

            foreach (var seg in _segments)
            {
                attempted++;
                try
                {
                    if (seg.IsBitArea)
                        ApplyBits(seg, connection.ReadBits(seg.Address, (ushort)seg.SpanUnits));
                    else
                        ApplyBytes(seg, connection.ReadBytes(seg.Address, (ushort)seg.SpanUnits), byteOrder);

                    // 读成功即归零：地址修好后诊断面板的"异常段"自然消失，不需要人工复位
                    Volatile.Write(ref seg.ConsecutiveFailures, 0);
                    seg.LastError = null;
                }
                catch (Exception ex)
                {
                    failed++;
                    // 段内所有变量降级 Uncertain：值保留旧值，UI 黄点提示"最近一次读取失败"
                    foreach (var item in seg.Items)
                        item.Variable.MarkUncertain();

                    int failures = Volatile.Read(ref seg.ConsecutiveFailures) + 1;
                    Volatile.Write(ref seg.ConsecutiveFailures, failures);
                    seg.LastError = ex.Message;

                    // 跨阈值那一刻发一条"不节流"的告警：坏段从"看得见"升级为"被通知"。
                    // 只在这一刻发（failures == 阈值），之后回落限流——否则恒坏段会每轮刷一条日志。
                    if (failures == FaultedSegmentThreshold)
                        _log?.Invoke($"[轮询] 段连续 {failures} 轮读失败，已计为异常段：{seg.Address}（{seg.SpanUnits} 单元）: {ex.Message}");

                    // C5：key 必须带连接名——_logTicks 是 static 的，不含连接名时两条连接读同一地址会互相吞日志
                    LogThrottled($"seg:{seg.ConnectionName}.{seg.Address}", $"段读失败 {seg.Address}（{seg.SpanUnits} 单元）: {ex.Message}");
                }
            }

            foreach (var single in _fallbacks)
            {
                attempted++;
                try
                {
                    var value = single.ReadMethod.Invoke(connection, new object[] { single.Variable.Address });
                    single.Variable.UpdateValue(value);
                }
                catch (Exception ex)
                {
                    failed++;
                    single.Variable.MarkUncertain(); // 单读失败：保留旧值，降级 Uncertain
                    var inner = (ex as TargetInvocationException)?.InnerException ?? ex;
                    LogThrottled($"var:{single.Variable.ConnectionName}.{single.Variable.VariableName}",
                        $"单读失败 {single.Variable.VariableName}（{single.Variable.Address}）: {inner.Message}");
                }
            }

            if (attempted == 0) return true;   // 无读项：不算故障（连接活性由其它读写刷新）
            return failed < attempted;         // 全失败 ⇒ 通信级故障，交给 Worker 断线重连
        }

        /// <summary>解码非位区段的字节流（S7 字节段 / Modbus 寄存器段）</summary>
        private void ApplyBytes(ReadSegment seg, byte[] data, ByteOrderFormat order)
        {
            if (data == null) return;
            int bytesPerUnit = seg.BytesPerUnit;

            foreach (var item in seg.Items)
            {
                try
                {
                    int byteOffset = item.UnitOffset * bytesPerUnit;
                    int byteLength = item.SpanUnits * bytesPerUnit;

                    if (byteOffset < 0 || byteOffset + byteLength > data.Length)
                    {
                        item.Variable.MarkUncertain(); // 设备返回字节不足：解不出本变量的新值
                        LogThrottled($"short:{item.Variable.ConnectionName}.{seg.Address}#{item.Variable.VariableName}",
                            $"段 {seg.Address} 实际返回 {data.Length} 字节，不足解码 {item.Variable.VariableName}（需 {byteOffset + byteLength} 字节）");
                        continue;
                    }

                    object? value = item.BitOffset >= 0
                        ? DecodeBit(data, byteOffset, item.BitOffset, item.ValueType)
                        : DecodeValue(data, byteOffset, byteLength, item, order);

                    if (value != null)
                        item.Variable.UpdateValue(value);
                }
                catch (Exception ex)
                {
                    item.Variable.MarkUncertain(); // 解码异常：本变量本轮没有可用新值
                    LogThrottled($"decode:{item.Variable.ConnectionName}.{item.Variable.VariableName}",
                        $"解码失败 {item.Variable.VariableName}（{item.Variable.Address}）: {ex.Message}");
                }
            }
        }

        /// <summary>解码 Modbus 位区段的位数组（线圈/离散输入）</summary>
        private void ApplyBits(ReadSegment seg, bool[] bits)
        {
            if (bits == null) return;

            foreach (var item in seg.Items)
            {
                try
                {
                    if (item.UnitOffset < 0 || item.UnitOffset >= bits.Length)
                    {
                        item.Variable.MarkUncertain(); // 设备返回点数不足：解不出本变量的新值
                        LogThrottled($"short:{item.Variable.ConnectionName}.{seg.Address}#{item.Variable.VariableName}",
                            $"位区段 {seg.Address} 实际返回 {bits.Length} 点，不足解码 {item.Variable.VariableName}（需第 {item.UnitOffset + 1} 点）");
                        continue;
                    }

                    item.Variable.UpdateValue(FromBit(bits[item.UnitOffset], item.ValueType));
                }
                catch (Exception ex)
                {
                    item.Variable.MarkUncertain(); // 解码异常：本变量本轮没有可用新值
                    LogThrottled($"decode:{item.Variable.ConnectionName}.{item.Variable.VariableName}",
                        $"解码失败 {item.Variable.VariableName}（{item.Variable.Address}）: {ex.Message}");
                }
            }
        }

        /// <summary>按段内字节偏移切出值（数值按连接字序解码；string/byte[] 原样取用长度）</summary>
        private static object? DecodeValue(byte[] data, int offset, int length, SegmentItem item, ByteOrderFormat order)
        {
            var raw = new byte[length];
            Array.Copy(data, offset, raw, 0, length);

            if (item.IsByteArray) return raw;
            if (item.IsString) return Encoding.ASCII.GetString(raw).TrimEnd('\0'); // 与写链路 ASCII 对称
            return HslHelper.ConvertToByType(raw, item.ValueType, order);
        }

        /// <summary>S7 位访问：取指定字节的第 N 位</summary>
        private static object DecodeBit(byte[] data, int byteOffset, int bitOffset, Type type)
        {
            if (bitOffset is < 0 or > 7) throw new ArgumentOutOfRangeException(nameof(bitOffset));
            bool bit = ((data[byteOffset] >> bitOffset) & 1) == 1;
            return type == typeof(bool) ? bit : Convert.ChangeType(bit, type);
        }

        /// <summary>bool → 目标类型（Modbus 位区变量按数值类型声明时也能读出 0/1）</summary>
        private static object FromBit(bool bit, Type type)
            => type == typeof(bool) ? bit : Convert.ChangeType(bit, type);

        private void LogThrottled(string key, string message)
        {
            if (_log == null) return;

            long now = Environment.TickCount64;
            if (_logTicks.TryGetValue(key, out long last) && now - last < LogThrottleMs) return;

            _logTicks[key] = now;
            _log(message);
        }

        #endregion
    }
}
