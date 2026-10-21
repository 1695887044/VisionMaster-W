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
    /// <para>1) 规划——按 协议+存储区 分组、按起始地址排序、贪心合并成若干"段"（间隙容忍 + 单请求上限封顶）；
    /// 间隙容忍按<b>传输</b>分档（见 <see cref="PollTransportKind"/>）：以太网上空档近乎免费，只受单请求上限约束；</para>
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

            /// <summary>
            /// 是否为"元素数组"（int[] / double[] / bool[]…）。
            /// <para>byte[] 不算：它是原始字节块，走 <see cref="IsByteArray"/> 的整段切片语义。</para>
            /// </summary>
            public bool IsArray;

            /// <summary>数组元素类型（<see cref="IsArray"/> 为 true 时有效）</summary>
            public Type? ElementType;

            /// <summary>数组元素个数（来自地址配置的 <see cref="DeviceAddressBase.Length"/>）</summary>
            public int ElementCount;

            /// <summary>字符串数组：每元素固定字节数（来自 <see cref="DeviceAddressBase.ElementByteLength"/>）</summary>
            public int ElementByteLength;

            /// <summary>是否为字符串数组（元素按 ASCII 解码并 TrimEnd('\0')）</summary>
            public bool IsStringArray;
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

            /// <summary>
            /// 本段是否允许"多读空洞"把邻居一起读回来。
            /// <para>false = 该段由某个关了 <see cref="CommunicationVariable.MergeAdjacentRead"/> 的变量独占，
            /// 跨度严格等于它自己的区间，不与任何邻居合并。</para>
            /// </summary>
            public bool MergeAdjacent = true;

            public readonly List<SegmentItem> Items = new();

            /// <summary>
            /// 编译期归属：本段由哪些变量组成（合并时钉死，不再事后按区间反推）。
            /// <para>反推的问题：关了合并的变量其地址可能落在邻居段的跨度内，那时"第一个包含它的段"
            /// 是邻居那段 → 本变量挂错、自己这段一个 Item 都没有（每轮白发一次请求）。</para>
            /// </summary>
            public readonly List<(PollAddress Address, CommunicationVariable Variable)> Owned = new();

            /// <summary>本段可用的 HSL 地址（如 "x=3;100" / "MB100" / "DB7.DBB100"）</summary>
            public string Address => SegmentPrefix + Start;

            /// <summary>段跨度（单元数）</summary>
            public int SpanUnits => EndUnit - Start;

            /// <summary>段内每个单元占用的字节数（Modbus 寄存器区 1 单元 = 2 字节，其余 1:1）</summary>
            public int BytesPerUnit =>
                Protocol is PollProtocol.Modbus or PollProtocol.Mc or PollProtocol.Fins && !IsBitArea ? 2 : 1;

            /// <summary>
            /// 本段"连续失败"的轮数（成功一轮即归零）。
            /// 用途：把"某个地址恒坏"从"仅一条限流日志"提升为"诊断面板可见 + 跨阈值一次告警"。
            /// 线程：仅 Worker 线程写，UI 线程会读（<see cref="GetHealthSnapshot"/>），故读写都走 <see cref="Volatile"/>。
            /// </summary>
            public int ConsecutiveFailures;

            /// <summary>最近一次失败原因（成功时清空）；与 <see cref="ConsecutiveFailures"/> 一起构成坏段摘要</summary>
            public string? LastError;

            /// <summary>
            /// 坏段隔离：连续失败达阈值后，本段被移出正常轮询，改为每 <see cref="IsolationProbeIntervalMs"/> 低频探测一次。
            /// <para>为什么需要它：一个恒坏地址（配置错/越界/从站掉线）在 100ms 周期下会每秒刷 10 次超时，
            /// 每次都烧满读超时并拖慢同拍的其它段。隔离后它只按 5s 探测，链路与其它段恢复正常节奏。</para>
            /// <para>恢复判据是"探测成功一次"，不需要人工复位——地址修好后自然回到正常轮询。</para>
            /// </summary>
            public long NextProbeTicks;

            /// <summary>隔离期间是否已经发过"已隔离"日志（每段只发一次，避免刷屏）</summary>
            public bool IsolationLogged;
        }

        /// <summary>兜底单读项：仍走 <see cref="ICommunicationConnection.Read{T}"/> 反射调用</summary>
        private sealed class SingleRead
        {
            public CommunicationVariable Variable = null!;
            public MethodInfo ReadMethod = null!;
        }

        #endregion

        #region 编译期参数

        // 间隙容忍：两段之间允许"多读"的空洞上限（**串口档**专用，见 GapOf）。
        // 太小则合并率低，太大则每轮白读大量无关数据。
        // ⚠ 以太网上不用这些常数：那里空档近乎免费，阈值直接取单请求上限（理由见 PollTransportKind 的文档）。
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

        /// <summary>
        /// 隔离段的低频探测间隔（毫秒）。
        /// <para>取 5s：既让恒坏段不再按 100ms 周期烧超时，又不至于让"现场刚把线接上"要等太久才恢复。
        /// 与 IO 层旧实现的 <c>IsolatedProbeIntervalMs</c> 取同一口径。</para>
        /// </summary>
        public const int IsolationProbeIntervalMs = 5000;

        private static readonly ConcurrentDictionary<string, long> _logTicks = new();

        private static readonly ConcurrentDictionary<Type, MethodInfo> _readMethodCache = new();

        #endregion

        private readonly List<ReadSegment> _segments;
        private readonly List<SingleRead> _fallbacks;
        private readonly Action<string>? _log;

        /// <summary>
        /// 是否启用坏段隔离（编译期从连接配置抄进来的快照）。
        /// <para>默认 false = 每轮都试，与改造前一字不差——诊断面板的"连续失败 N 轮"
        /// 正是靠每轮真读才累加得出来的。</para>
        /// </summary>
        private readonly bool _isolateFaultedSegments;

        private PollBatchPlanner(List<ReadSegment> segments, List<SingleRead> fallbacks,
                                 Action<string>? log, bool isolateFaultedSegments)
        {
            _segments = segments;
            _fallbacks = fallbacks;
            _log = log;
            _isolateFaultedSegments = isolateFaultedSegments;
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
        /// <param name="transport">
        /// 传输家族（决定"空档代价"，见 <see cref="PollTransportKind"/>）。
        /// <para><b>刻意不给默认值</b>：默认值会让"调用方忘了传"静默退回保守档，行为悄悄变旧、编译期毫无提示——
        /// 本会话修掉的构造器缺陷正是同一类坑。必填能让编译器替我们盯住每个调用点。</para>
        /// </param>
        /// <param name="log">编译期告警输出（只写不可行的变量，不进热路径）</param>
        public static PollBatchPlanner Build(
            IEnumerable<CommunicationVariable> variables,
            PollTransportKind transport,
            Action<string>? log = null,
            bool isolateFaultedSegments = false)
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

                // 变长/数组类型：必须有结构化地址配置才知道读多长（旧字符串地址推不出长度）
                bool isString = valueType == typeof(string);
                bool isByteArray = valueType == typeof(byte[]);
                bool isArray = valueType.IsArray && !isByteArray;
                Type? elementType = isArray ? valueType.GetElementType() : null;
                bool isStringArray = isArray && elementType == typeof(string);
                bool isBoolArray = isArray && elementType == typeof(bool);

                if (isString || isByteArray || isArray)
                {
                    if (variable.AddressConfig == null)
                    {
                        log?.Invoke($"变量 {variable.ConnectionName}.{variable.VariableName} 是变长/数组类型（{valueType.Name}）但缺少结构化地址配置，无法确定长度，已跳过轮询");
                        continue;
                    }
                }

                if (isArray)
                {
                    if (isStringArray)
                    {
                        // 字符串数组必须有"每元素固定字节数"：没有它无法知道切分边界，硬解只会得到乱码。
                        if (variable.AddressConfig!.ElementByteLength < 1)
                        {
                            log?.Invoke($"变量 {variable.ConnectionName}.{variable.VariableName} 是字符串数组但未配置每元素字节数，已跳过轮询");
                            continue;
                        }
                    }
                    else if (elementType == null || HslHelper.MinByteCount(elementType) == 0)
                    {
                        log?.Invoke($"变量 {variable.ConnectionName}.{variable.VariableName} 的数组元素类型 {elementType?.Name ?? "null"} 无法按字节流解码，已跳过轮询");
                        continue;
                    }

                    // bool[] 只有 Modbus 位区（线圈/离散输入）有唯一语义；S7 的"打包位 vs 每字节一个"没有定论。
                    if (isBoolArray && !poll.IsBitArea)
                    {
                        log?.Invoke($"变量 {variable.ConnectionName}.{variable.VariableName} 是 bool 数组，但地址不是 Modbus 位区（线圈/离散输入），已跳过轮询");
                        continue;
                    }

                    if (!isBoolArray && (poll.IsBitArea || poll.IsBitAccess))
                    {
                        log?.Invoke($"变量 {variable.ConnectionName}.{variable.VariableName} 是数值数组，但地址是位区/位访问，已跳过轮询");
                        continue;
                    }

                    if (variable.AddressConfig!.Length < 1)
                    {
                        log?.Invoke($"变量 {variable.ConnectionName}.{variable.VariableName} 的数组元素个数无效（{variable.AddressConfig.Length}），已跳过轮询");
                        continue;
                    }
                }
                else if (!isString && !isByteArray && HslHelper.MinByteCount(valueType) == 0)
                {
                    log?.Invoke($"变量 {variable.ConnectionName}.{variable.VariableName} 的类型 {valueType.Name} 无法按字节流解码，已跳过轮询");
                    continue;
                }

                // 类型所需字节数 > 地址声明的跨度：批量切片必然解错，退回单读保证值正确。
                // 数组的跨度由 Length 元素个数决定，不走这条（上面已按元素类型校验）。
                if (!isString && !isByteArray && !isArray && !poll.IsBitArea && !poll.IsBitAccess
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

            var segments = MergeSegments(batchable, transport);
            CompileItems(segments, batchable);
            return new PollBatchPlanner(segments, fallbacks, log, isolateFaultedSegments);
        }

        /// <summary>按 协议+存储区 分组、起始地址排序后贪心合并成段</summary>
        private static List<ReadSegment> MergeSegments(
            List<(PollAddress Address, CommunicationVariable Variable)> batchable,
            PollTransportKind transport)
        {
            var result = new List<ReadSegment>();

            foreach (var group in batchable.GroupBy(x => (x.Address.Protocol, x.Address.GroupKey)))
            {
                ReadSegment? current = null;

                foreach (var (poll, variable) in group.OrderBy(x => x.Address.Start).ThenBy(x => x.Address.EndUnit))
                {
                    // 该变量关了「区域合并读」：自成一段，跨度严格等于它自己，且不与任何邻居合并。
                    // 仍然走段读（一次读回自己的跨度、按结构化解码），只是不把邻居一起读回来。
                    if (!variable.MergeAdjacentRead)
                    {
                        if (current != null)
                        {
                            result.Add(current);
                            current = null;
                        }

                        var solo = new ReadSegment
                        {
                            Protocol = poll.Protocol,
                            GroupKey = poll.GroupKey,
                            ConnectionName = variable.ConnectionName,
                            SegmentPrefix = poll.SegmentPrefix,
                            Start = poll.Start,
                            EndUnit = poll.EndUnit,
                            IsBitArea = poll.IsBitArea,
                            MergeAdjacent = false
                        };
                        // 归属在合并时就钉死：不能事后按"区间包含"回落——
                        // 该变量的地址可能落在上一个合并段的跨度内（邻居是跨多个寄存器的变量），
                        // 那时"第一个包含它的段"是邻居那段，本变量会被挂错、自己这段空转成一次白发的请求。
                        solo.Owned.Add((poll, variable));
                        result.Add(solo);
                        continue;
                    }

                    if (current != null)
                    {
                        int newEnd = Math.Max(current.EndUnit, poll.EndUnit);
                        bool overflow = newEnd - current.Start > poll.MaxSegmentUnits; // 段过长 → 超过单请求上限
                        bool tooFar = poll.Start - current.EndUnit > GapOf(poll, transport); // 空洞太大 → 白读太多

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

                    current.Owned.Add((poll, variable));
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
            // 归属由 MergeSegments 在合并时钉死（段自己持有 Owned），这里不再按区间反推：
            // 反推在"不合并的变量落在邻居跨度内"时会挂错段，留下一个空段（每轮白发一次请求）。
            foreach (var seg in segments)
            {
                foreach (var (poll, variable) in seg.Owned)
                {
                    var clrType = ResolveClrType(variable.ValueType)!;
                    bool isArray = clrType.IsArray && clrType != typeof(byte[]);
                    bool isStringArray = isArray && clrType.GetElementType() == typeof(string);
                    seg.Items.Add(new SegmentItem
                    {
                        Variable = variable,
                        UnitOffset = poll.Start - seg.Start,
                        SpanUnits = poll.SpanUnits,
                        BitOffset = poll.BitOffset,
                        ValueType = clrType,
                        IsString = clrType == typeof(string),
                        IsByteArray = clrType == typeof(byte[]),
                        IsArray = isArray,
                        ElementType = isArray ? clrType.GetElementType() : null,
                        ElementCount = isArray ? variable.AddressConfig?.Length ?? 0 : 0,
                        IsStringArray = isStringArray,
                        ElementByteLength = isStringArray ? variable.AddressConfig?.ElementByteLength ?? 0 : 0
                    });
                }
            }
        }

        /// <summary>
        /// 相邻两段之间允许多大的空档（单元数）。超过就断开、另起一段。
        /// <para>两档的差别就是"多读一个单元要花多少代价"，见 <see cref="PollTransportKind"/>：</para>
        /// <para>· <b>以太网</b>：空档几乎免费，于是阈值直接取该区的单请求上限——等价于"不限空档"，
        /// 段大小完全由 <see cref="PollAddress.MaxSegmentUnits"/> 决定。
        /// 代价可算：最坏一个段白读 上限−1 个单元（Modbus 寄存器区 = 119 寄存器 = 238 字节，S7 = 109 字节，
        /// 位区 = 1999 点 = 250 字节），在 TCP 上都是零头，而省下的每次往返实测约 1ms。</para>
        /// <para>· <b>串口</b>：空档要花线时（9600bps 下 1 个寄存器 ≈ 2.29ms），沿用下面的保守常数。</para>
        /// </summary>
        private static int GapOf(PollAddress poll, PollTransportKind transport)
        {
            if (transport == PollTransportKind.Ethernet) return poll.MaxSegmentUnits;

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
            => Poll(connection, null) != PollCycleOutcome.LinkFault;

        /// <summary>
        /// 执行一轮轮询；<paramref name="yieldRequested"/> 在每段之前被询问一次，
        /// 返回 true 时立即以 <see cref="PollCycleOutcome.Yielded"/> 收尾（不 MarkSuccess、不判故障）。
        /// <para>只在"段与段之间"让路：段内不能中断，否则半包读会导致协议解析错位。</para>
        /// </summary>
        public PollCycleOutcome Poll(
            ICommunicationConnection connection,
            Func<bool>? yieldRequested)
        {
            int attempted = 0;
            int failed = 0;

            // 链路级故障短路标志：段读失败且连接自证"链路已死"时置位，
            // 跳过剩余段与全部单读，本轮返回 LinkFault 让 Worker 立刻断开重连。
            bool linkFault = false;

            // 字序取自连接本身（ModbusTcp 跟随配置，S7/串口恒 ABCD），保证与单点读同源
            var byteOrder = connection.ByteOrder;

            long now = Environment.TickCount64;

            foreach (var seg in _segments)
            {
                // 插队命令优先：段与段之间让路。这里不能放到 try 里面——
                // 让路不是故障，不能走 MarkUncertain / 失败计数。
                if (yieldRequested?.Invoke() == true)
                    return PollCycleOutcome.Yielded;

                // 坏段隔离（仅当连接显式开启）：连续失败达阈值的段被移出正常轮询，
                // 改为每 IsolationProbeIntervalMs 低频探测一次。
                // 跳过时**不顺延** NextProbeTicks——顺延会让探测时刻一直往后推、永远不到期。
                if (_isolateFaultedSegments && IsIsolated(seg, now))
                    continue;

                attempted++;
                try
                {
                    if (seg.IsBitArea)
                        ApplyBits(seg, connection.ReadBits(seg.Address, (ushort)seg.SpanUnits));
                    else
                        ApplyBytes(seg, ReadSegmentBytes(connection, seg), byteOrder);

                    // 读成功即归零：地址修好后诊断面板的"异常段"自然消失，不需要人工复位
                    Volatile.Write(ref seg.ConsecutiveFailures, 0);
                    seg.LastError = null;
                    seg.IsolationLogged = false;   // 恢复后允许下次再报一次"已隔离"
                    seg.NextProbeTicks = 0;
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

                    // 进入隔离那一刻单独报一次（含"多久探测一次"），让现场知道它不再按周期读了。
                    // 顺延探测时刻也只在这里做：跳过的轮次不改它，否则每轮都往后推、永远到不了期。
                    if (_isolateFaultedSegments && failures >= FaultedSegmentThreshold)
                    {
                        seg.NextProbeTicks = Environment.TickCount64 + IsolationProbeIntervalMs;

                        if (!seg.IsolationLogged)
                        {
                            seg.IsolationLogged = true;
                            _log?.Invoke($"[轮询] 段已隔离（移出正常轮询，每 {IsolationProbeIntervalMs}ms 探测一次）：{seg.Address}: {ex.Message}");
                        }
                    }

                    // C5：key 必须带连接名——_logTicks 是 static 的，不含连接名时两条连接读同一地址会互相吞日志
                    LogThrottled($"seg:{seg.ConnectionName}.{seg.Address}", $"段读失败 {seg.Address}（{seg.SpanUnits} 单元）: {ex.Message}");

                    // early-abort：段读失败后问一句"是链路死了，还是只是这个地址读不到"。
                    // 探针读的是底层 socket 的错误标志（传输层 I/O 是否失败），不含"帧收到了但内容是错误码"
                    // 的情况（Modbus 非法地址 / S7 越界），所以个别坏地址不会误触发短路。
                    // 不做这层判断的话，半开链路下每段都要烧掉一整个读超时：段数 × ReadTimeoutMs
                    // （167 段 × 3s ≈ 8 分钟才判掉线，实测 5 段就已是 15s）。
                    if (connection is ILinkHealthProbe probe && probe.HasLinkFault)
                    {
                        linkFault = true;
                        _log?.Invoke($"[轮询] 段读失败且链路已判定断开，跳过剩余 {_segments.Count - attempted} 段并立即重连：{seg.Address}: {ex.Message}");
                        break;
                    }
                }
            }

            // 链路已死就不要再跑单读：那些读必然也一样超时，而且它们若"部分成功"，
            // 后面的 failed < attempted 会把这次链路故障算成通信正常，短路就白做了。
            if (!linkFault)
            {
                foreach (var single in _fallbacks)
                {
                    // 兜底单读之间同样让路：一条单读就是一次设备往返，插队命令没必要排在它后面
                    if (yieldRequested?.Invoke() == true)
                        return PollCycleOutcome.Yielded;

                    attempted++;
                    object? value;
                    try
                    {
                        value = single.ReadMethod.Invoke(connection, new object[] { single.Variable.Address });
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        single.Variable.MarkUncertain(); // 单读失败：保留旧值，降级 Uncertain
                        var inner = (ex as TargetInvocationException)?.InnerException ?? ex;
                        LogThrottled($"var:{single.Variable.ConnectionName}.{single.Variable.VariableName}",
                            $"单读失败 {single.Variable.VariableName}（{single.Variable.Address}）: {inner.Message}");
                        continue;
                    }

                    // F1：读成功之后的派发单独兜底——下游订阅者抛异常不算"单读失败"
                    PublishValue(single.Variable, value);
                }
            }

            if (attempted == 0) return PollCycleOutcome.Completed;   // 无读项：不算故障（连接活性由其它读写刷新）
            if (linkFault) return PollCycleOutcome.LinkFault;        // ★ 必须显式判：前几段成功、后段链路断时 failed < attempted 会误判为正常
            return failed < attempted
                ? PollCycleOutcome.Completed
                : PollCycleOutcome.LinkFault;                        // 全失败 ⇒ 通信级故障，交给 Worker 断线重连
        }

        /// <summary>
        /// 单变量读取（供「立刻读」的插队车道使用）。
        ///
        /// 【为什么不能用 <see cref="Poll(ICommunicationConnection, Func{bool}?)"/> 代替】
        /// Poll 的判定是"全失败 = 通信级故障"：单变量计划里唯一一段读失败时
        /// <c>failed == attempted</c>，会被判成 LinkFault——一个坏地址被误报成链路断开。
        /// 立刻读需要的是"这个变量读没读到"的精确结果，所以单独走一条只解码、不发布、
        /// 不改质量戳的路径。
        ///
        /// 【前置条件】计划里恰好一个可读项（一个段或一个兜底单读）。
        /// 【线程】仅由所属连接的 Worker 线程调用。
        /// </summary>
        public bool TryReadOne(
            ICommunicationConnection connection,
            out object? value,
            out string? error)
        {
            value = null;
            error = null;

            if (PollItemCount != 1)
            {
                error = $"单变量读取计划不成立（可读项 {PollItemCount} 个）";
                return false;
            }

            var byteOrder = connection.ByteOrder;

            foreach (var seg in _segments)
            {
                try
                {
                    if (seg.IsBitArea)
                    {
                        var bits = connection.ReadBits(seg.Address, (ushort)seg.SpanUnits);
                        value = DecodeBitItem(bits, seg.Items[0]);
                    }
                    else
                    {
                        var data = ReadSegmentBytes(connection, seg);
                        value = DecodeItem(data, seg, seg.Items[0], byteOrder);
                    }

                    if (value == null)
                    {
                        error = "解码结果为空";
                        return false;
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }

            foreach (var single in _fallbacks)
            {
                try
                {
                    value = single.ReadMethod.Invoke(connection, new object[] { single.Variable.Address });
                    if (value == null)
                    {
                        error = "读取结果为空";
                        return false;
                    }
                    return true;
                }
                catch (TargetInvocationException ex)
                {
                    error = (ex.InnerException ?? ex).Message;
                    return false;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }

            error = "没有可读项（变量类型或地址不受支持）";
            return false;
        }

        /// <summary>
        /// 该段本轮是否跳过（隔离中且还没到探测时刻）。
        /// <para>隔离判据是"连续失败达阈值 + 未到下次探测时刻"，两个条件缺一不可：
        /// 只看失败数会让坏段永远不再尝试（地址修好也恢复不了）。</para>
        /// </summary>
        private static bool IsIsolated(ReadSegment seg, long now)
        {
            if (Volatile.Read(ref seg.ConsecutiveFailures) < FaultedSegmentThreshold) return false;
            return now < seg.NextProbeTicks;
        }

        /// <summary>
        /// 把已解码的值推给变量（F1）。
        /// <para><b>为什么必须与"解码"分开 try</b>：<see cref="CommunicationVariable.UpdateValue"/> 会同步 Invoke
        /// 下游订阅者（UI 绑定的 PropertyChanged 链、SCADA 运行态、报警引擎）。下游任何一处抛异常时，
        /// 值其实**已经读到并写进变量**——若和解码共用 catch，变量会被错误地 <c>MarkUncertain()</c>
        /// （UI 黄点"读取失败"）并记一条误导性的"解码失败"日志。</para>
        /// <para>这里只记限流日志：不改质量戳、不计入 <c>failed</c>（通信本身是成功的）。</para>
        /// </summary>
        private void PublishValue(CommunicationVariable variable, object? value)
        {
            try
            {
                // 工程量换算（Scale/Offset/小数位）：管道一直存在（DeviceAddressBase.ConvertToEngineering），
                // 但改造前只有<b>写</b>路径调了 ConvertToRaw、<b>读</b>路径从没调过——
                // 于是"配了缩放系数的变量写进去是工程值、读回来却是原始值"，读写不对称。
                // 默认 EnableConversion=false，故不配换算的方案行为零变化。
                variable.UpdateValue(variable.ToEngineering(value));
            }
            catch (Exception ex)
            {
                LogThrottled($"publish:{variable.ConnectionName}.{variable.VariableName}",
                    $"值已更新但下游处理异常 {variable.VariableName}（{variable.Address}）: {ex.Message}");
            }
        }        /// <summary>解码非位区段的字节流（S7 字节段 / Modbus 寄存器段）</summary>
        /// <summary>
        /// 读一段：优先走 <see cref="ISegmentReader"/>（MC/FINS 的"字"单位），
        /// 否则退回 <see cref="ICommunicationConnection.ReadBytes"/>（Modbus/S7 的既有语义）。
        /// </summary>
        private static byte[] ReadSegmentBytes(ICommunicationConnection connection, ReadSegment seg)
            => connection is ISegmentReader reader
                ? reader.ReadSegment(seg.Address, (ushort)seg.SpanUnits)
                : connection.ReadBytes(seg.Address, (ushort)seg.SpanUnits);

        private void ApplyBytes(ReadSegment seg, byte[] data, ByteOrderFormat order)
        {
            if (data == null) return;

            foreach (var item in seg.Items)
            {
                object? value;
                try
                {
                    // 解码只保留一份实现（DecodeItem）：轮询与立刻读共用，
                    // 两处各写一遍迟早出现"轮询读得对、立刻读读错"的漂移。
                    value = DecodeItem(data, seg, item, order);
                }
                catch (Exception ex)
                {
                    item.Variable.MarkUncertain(); // 解码异常：本变量本轮没有可用新值
                    LogThrottled($"decode:{item.Variable.ConnectionName}.{item.Variable.VariableName}",
                        $"解码失败 {item.Variable.VariableName}（{item.Variable.Address}）: {ex.Message}");
                    continue;
                }

                if (value != null)
                    PublishValue(item.Variable, value);
            }
        }

        /// <summary>解码 Modbus 位区段的位数组（线圈/离散输入）</summary>
        private void ApplyBits(ReadSegment seg, bool[] bits)
        {
            if (bits == null) return;

            foreach (var item in seg.Items)
            {
                object? value;
                try
                {
                    value = DecodeBitItem(bits, item);
                }
                catch (Exception ex)
                {
                    item.Variable.MarkUncertain(); // 解码异常：本变量本轮没有可用新值
                    LogThrottled($"decode:{item.Variable.ConnectionName}.{item.Variable.VariableName}",
                        $"解码失败 {item.Variable.VariableName}（{item.Variable.Address}）: {ex.Message}");
                    continue;
                }

                PublishValue(item.Variable, value);
            }
        }

        /// <summary>
        /// 按段内偏移解码一个变量（数值按连接字序；string/byte[] 原样取用长度）。
        /// <para>轮询（<see cref="ApplyBytes"/>）与立刻读（<see cref="TryReadOne"/>）共用这一份，
        /// 保证两条路径的字节序、类型解码、长度校验完全一致。</para>
        /// </summary>
        private static object? DecodeItem(byte[] data, ReadSegment seg, SegmentItem item, ByteOrderFormat order)
        {
            int byteOffset = item.UnitOffset * seg.BytesPerUnit;
            int byteLength = item.SpanUnits * seg.BytesPerUnit;

            if (byteOffset < 0 || byteOffset + byteLength > data.Length)
                throw new InvalidOperationException(
                    $"段 {seg.Address} 实际返回 {data.Length} 字节，不足解码 {item.Variable.VariableName}（需 {byteOffset + byteLength} 字节）");

            return item.BitOffset >= 0
                ? DecodeBit(data, byteOffset, item.BitOffset, item.ValueType)
                : DecodeValue(data, byteOffset, byteLength, item, order);
        }

        /// <summary>从位区点数组里取一个变量的值（轮询与立刻读共用）</summary>
        private static object DecodeBitItem(bool[] bits, SegmentItem item)
        {
            if (item.IsArray)
            {
                int end = item.UnitOffset + item.ElementCount;
                if (item.UnitOffset < 0 || item.ElementCount < 1 || end > bits.Length)
                    throw new InvalidOperationException(
                        $"位区段实际返回 {bits.Length} 点，不足解码 {item.Variable.VariableName}（需第 {item.UnitOffset + 1}~{end} 点）");

                var array = Array.CreateInstance(item.ElementType!, item.ElementCount);
                for (int i = 0; i < item.ElementCount; i++)
                    array.SetValue(FromBit(bits[item.UnitOffset + i], item.ElementType!), i);
                return array;
            }

            if (item.UnitOffset < 0 || item.UnitOffset >= bits.Length)
                throw new InvalidOperationException(
                    $"位区段实际返回 {bits.Length} 点，不足解码 {item.Variable.VariableName}（需第 {item.UnitOffset + 1} 点）");

            return FromBit(bits[item.UnitOffset], item.ValueType);
        }

        /// <summary>
        /// 按段内字节偏移切出值（数值按连接字序解码；string/byte[] 原样取用长度）。
        /// <para>P-A3：数值类型直接传段内切片给 <see cref="HslHelper.ConvertToByType(ReadOnlySpan{byte}, Type, ByteOrderFormat)"/>，
        /// 不再为每个变量复制一个中间数组（只有 string/byte[] 本身必须产出新对象）。</para>
        /// </summary>
        private static object? DecodeValue(byte[] data, int offset, int length, SegmentItem item, ByteOrderFormat order)
        {
            var slice = new ReadOnlySpan<byte>(data, offset, length);

            // 字符串数组：按"每元素固定字节数"切分，ASCII 解码并去掉尾部 '\0'。
            // 宽度必须来自地址配置（ElementByteLength），不能从类型推断——String 本身没有固定宽度。
            if (item.IsStringArray)
            {
                int elementSize = item.ElementByteLength;
                if (elementSize < 1 || item.ElementCount < 1)
                    throw new InvalidOperationException(
                        $"变量 {item.Variable.VariableName} 的字符串数组缺少每元素字节数/元素个数");

                int needed = elementSize * item.ElementCount;
                if (needed > slice.Length)
                    throw new InvalidOperationException(
                        $"段 {item.Variable.Address} 实际返回 {slice.Length} 字节，不足解码 {item.Variable.VariableName}（需 {needed} 字节）");

                var strings = new string[item.ElementCount];
                for (int i = 0; i < item.ElementCount; i++)
                {
                    var element = slice.Slice(i * elementSize, elementSize);
                    strings[i] = Encoding.ASCII.GetString(element).TrimEnd('\0');
                }
                return strings;
            }

            // 元素数组：按元素大小逐个解码。字序仍按连接配置（每个元素独立套用），
            // 与标量走同一个 HslHelper.ConvertToByType，保证"标量读得对、数组读错"不会发生。
            if (item.IsArray)
            {
                var elementType = item.ElementType
                    ?? throw new InvalidOperationException($"变量 {item.Variable.VariableName} 缺少数组元素类型");
                int elementSize = HslHelper.MinByteCount(elementType);
                if (elementSize <= 0 || item.ElementCount < 1)
                    throw new InvalidOperationException(
                        $"变量 {item.Variable.VariableName} 的数组元素类型/个数无效（{elementType.Name} × {item.ElementCount}）");

                int needed = item.ElementCount * elementSize;
                if (needed > slice.Length)
                    throw new InvalidOperationException(
                        $"段 {item.Variable.Address} 实际返回 {slice.Length} 字节，不足解码 {item.Variable.VariableName}（需 {needed} 字节）");

                var array = Array.CreateInstance(elementType, item.ElementCount);
                for (int i = 0; i < item.ElementCount; i++)
                {
                    var element = slice.Slice(i * elementSize, elementSize);
                    array.SetValue(HslHelper.ConvertToByType(element, elementType, order), i);
                }
                return array;
            }

            if (item.IsByteArray) return slice.ToArray();
            if (item.IsString) return Encoding.ASCII.GetString(slice).TrimEnd('\0'); // 与写链路 ASCII 对称
            return HslHelper.ConvertToByType(slice, item.ValueType, order);
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
