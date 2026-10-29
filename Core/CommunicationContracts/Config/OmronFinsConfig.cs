using UI.Attributes;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>欧姆龙 FINS 协议配置类。</para>
    /// <para>用于配置欧姆龙 PLC 的 FINS 协议连接参数。</para>
    /// </summary>
    public class OmronFinsConfig : EthernetConfigBase
    {
        /// <summary>
        /// <para>获取或设置 PLC 网络号。</para>
        /// <para>范围：0-127</para>
        /// <para>默认值：0</para>
        /// </summary>
        [SuperDisplay(Name = "PLC 网络号", Group = new string[] { "FINS专有参数" }, Order = 5, ColSpan = 4)]
        [RangeValidation(0, 127, "网络号范围：0-127")]
        public byte NetworkNumber { get; set; } = 0;

        /// <summary>
        /// <para>获取或设置 PLC 节点号。</para>
        /// <para>范围：0-254</para>
        /// <para>默认值：0</para>
        /// </summary>
        [SuperDisplay(Name = "PLC 节点号", Group = new string[] { "FINS专有参数" }, Order = 6, ColSpan = 4)]
        [RangeValidation(0, 254, "节点号范围：0-254")]
        public byte NodeNumber { get; set; } = 0;

        /// <summary>
        /// <para>获取或设置 PLC 单元号。</para>
        /// <para>范围：0-255</para>
        /// <para>默认值：0</para>
        /// </summary>
        [SuperDisplay(Name = "PLC 单元号", Group = new string[] { "FINS专有参数" }, Order = 7, ColSpan = 4)]
        [RangeValidation(0, 255, "单元号范围：0-255")]
        public byte UnitNumber { get; set; } = 0;

        /// <summary>
        /// <para>获取或设置响应超时时间（毫秒）——FINS 实际生效的读等待。</para>
        /// <para>默认值：0（跟随通用「读超时」<see cref="ConnectionConfigBase.ReadTimeoutMs"/>，默认 5000ms）。</para>
        /// <para>为什么默认 0 而不是写死 5000：写死 5000 会把通用「读超时」永久遮蔽——
        /// 用户在界面上改「读超时」对 FINS 毫无作用（2026-10-29 修正）。
        /// 专属字段优先、0 = 跟随，与 FreeProtocol 同一口径。</para>
        /// </summary>
        [SuperDisplay(Name = "响应超时(ms)", Group = new string[] { "FINS专有参数" }, Order = 8, ColSpan = 6,
            Description = "FINS 实际生效的读等待；0 = 跟随通用「读超时」")]
        [RangeValidation(0, 60000, "响应超时必须在 0 - 60000 之间（0 = 跟随读超时）")]
        public int ResponseTimeoutMs { get; set; } = 0;

        /// <summary>
        /// <para>初始化欧姆龙 FINS 配置。</para>
        /// <para>默认端口设置为 9600（FINS 协议默认端口）。</para>
        /// </summary>
        public OmronFinsConfig()
        {
            Port = 9600;
        }

        /// <summary>
        /// <para>创建欧姆龙 FINS 连接对象。</para>
        /// <para>注意：此方法尚未实现。</para>
        /// </summary>
        /// <returns>FINS 连接实例</returns>

        /// <summary>
        /// <para>克隆当前配置对象。</para>
        /// <para>⚠ 必须拷贝基类 <see cref="ConnectionConfigBase.Type"/>（默认 0 = ModbusTcp）：
        /// 漏拷会让编辑弹窗保存时报"协议类型不匹配"。</para>
        /// </summary>
        /// <returns>配置副本</returns>
        public override ConnectionConfigBase Clone() => new OmronFinsConfig
        {
            Type = Type,
            TimeoutMs = TimeoutMs,
            RetryCount = RetryCount,
            RetryIntervalMs = RetryIntervalMs,
            ReadTimeoutMs = ReadTimeoutMs,
            IpAddress = IpAddress,
            Port = Port,
            EnableKeepAlive = EnableKeepAlive,
            KeepAliveIntervalMs = KeepAliveIntervalMs,
            NetworkNumber = NetworkNumber,
            NodeNumber = NodeNumber,
            UnitNumber = UnitNumber,
            ResponseTimeoutMs = ResponseTimeoutMs
        };
    }
}

