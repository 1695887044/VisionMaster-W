namespace VisionMaster.Communications
{
    /// <summary>
    /// 轮询地址的协议家族（批量规划器只按这两个家族分组，其余协议走单读兜底）
    /// </summary>
    public enum PollProtocol
    {
        /// <summary>西门子 S7（按字节编址）</summary>
        S7,
        /// <summary>Modbus TCP/RTU（寄存器区按字编址，位区按点编址）</summary>
        Modbus
    }

    /// <summary>
    /// 轮询地址所在的传输家族。与 <see cref="PollProtocol"/> 是<b>正交</b>的两个维度：
    /// 协议决定"怎么编址"，传输决定"多读一个单元要花多少代价"。
    /// </summary>
    /// <remarks>
    /// <para>为什么批量规划器需要它："把一个段内的空档一起读回来"这件事，在两种传输上的账完全不同——</para>
    /// <para>· <b>以太网</b>：多读 119 个寄存器 = 238 字节，在 100Mbps 上是零头，而省下的是整整一次往返
    /// （实测单次往返 ≈ 1ms）。所以空档近乎免费，段大小只受"单请求上限"约束；</para>
    /// <para>· <b>串口</b>：多读 1 个寄存器 = 2 字节 × 11 位 ÷ 9600bps ≈ 2.29ms 线时。多读 118 个 ≈ 270ms，
    /// 比一次往返还贵两个数量级——此时"合并"反而是亏的。</para>
    /// <para>早期实现只按协议分档、不看传输，于是 TCP 上白白发了大量可以合并的请求
    /// （实测 1000 点 / 步长 10 的稀疏点表被拆成 1000 段，923 请求/s 把 1000ms 周期占满）。</para>
    /// </remarks>
    public enum PollTransportKind
    {
        /// <summary>以太网（TCP）：空档代价可忽略，按单请求上限放开合并</summary>
        Ethernet,

        /// <summary>串口（RTU/ASCII 等）：空档要花真实线时，按保守常数合并</summary>
        Serial
    }

    /// <summary>
    /// <para>结构化的轮询地址：把"读哪里、读多少"从字符串猜测变成直接字段。</para>
    /// <para>批量轮询规划器按 <see cref="GroupKey"/> 分组、按 <see cref="Start"/> 排序合并区间，
    /// 再用 <see cref="SegmentPrefix"/> + 段起点拼出 HSL 可直接解析的段地址。</para>
    /// <para>纯运行时对象，不参与序列化。</para>
    /// </summary>
    public sealed class PollAddress
    {
        /// <summary>协议家族</summary>
        public PollProtocol Protocol { get; init; }

        /// <summary>
        /// 分组键：同组变量才可能合并读。
        /// S7 如 "M"、"DB7"、"V"；Modbus 如 "FC1"(线圈)、"FC3"(保持寄存器)
        /// </summary>
        public string GroupKey { get; init; } = string.Empty;

        /// <summary>
        /// 段地址前缀：与段起点数字拼接即为 HSL 可读地址。
        /// S7 如 "MB"、"DB7.DBB"；Modbus 为富地址 "x=3;"
        /// </summary>
        public string SegmentPrefix { get; init; } = string.Empty;

        /// <summary>
        /// 变量起始地址（S7=字节偏移；Modbus=0 基址的寄存器号/位点号）
        /// </summary>
        public int Start { get; init; }

        /// <summary>
        /// 占用单元数（S7=字节数；Modbus 寄存器区=寄存器数；Modbus 位区=点数）
        /// </summary>
        public int SpanUnits { get; init; }

        /// <summary>是否为 Modbus 位区（线圈/离散输入，返回数据按位打包，1 单元=1 点）</summary>
        public bool IsBitArea { get; init; }

        /// <summary>是否为单 bit 访问（S7 byte.bit / Modbus 位区变量）</summary>
        public bool IsBitAccess { get; init; }

        /// <summary>bit 偏移（0-7），非位访问为 -1</summary>
        public int BitOffset { get; init; } = -1;

        /// <summary>本变量的段地址（HSL 可解析）</summary>
        public string SegmentAddress => SegmentPrefix + Start;

        /// <summary>区间结束（不含），用于贪心合并的相邻判断</summary>
        public int EndUnit => Start + SpanUnits;

        /// <summary>变量值的字节长度（位区/位访问固定 1 字节切片）</summary>
        public int ValueByteLength => IsBitArea || IsBitAccess
            ? 1
            : Protocol == PollProtocol.Modbus ? SpanUnits * 2 : SpanUnits;

        /// <summary>
        /// 单请求单元数上限（来自 HSL ModbusInfo.BuildReadModbusCommand 的分包限制与 S7 PDU 保守值）
        /// </summary>
        public int MaxSegmentUnits => Protocol switch
        {
            PollProtocol.Modbus => IsBitArea ? 2000 : 120,
            _ => 110 // S7：留足 PDU 余量
        };
    }
}
