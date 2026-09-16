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
