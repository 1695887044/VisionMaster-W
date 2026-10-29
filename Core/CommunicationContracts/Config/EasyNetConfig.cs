namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>汇川 EasyNet（Easy 系列 PLC 专用 TCP 协议）链路配置。</para>
    /// <para>协议本身没有可调参数：固定 22 字节头 + 2 字节长度前缀、默认端口 12939、
    /// 数据按位寻址且字节序固定小端（见 <c>EasyNetConnection.ByteOrder</c>）。
    /// 故本类只保留以太网基类的通用参数（IP/端口/超时/KeepAlive），不额外加协议字段。</para>
    /// </summary>
    public class EasyNetConfig : EthernetConfigBase
    {
        /// <summary>协议默认端口（取自 HslCommunicationDemo 的 EasyNet 页签）。</summary>
        public const int DefaultPortNumber = 12939;

        public EasyNetConfig()
        {
            Port = DefaultPortNumber;
        }

        /// <summary>
        /// 默认端口展示在属性面板上，但允许改（现场可能做端口映射）。
        /// 写法与 <see cref="SiemensS7Config.Port"/> 一致：只覆盖默认值，属性特性沿用基类。
        /// </summary>
        public override int Port { get; set; } = DefaultPortNumber;

        public override ConnectionConfigBase Clone() => new EasyNetConfig
        {
            // 与其它协议同一口径：Type 必须显式拷贝，否则副本会被 Validate 判"协议类型不匹配"
            Type = Type,
            TimeoutMs = TimeoutMs,
            RetryCount = RetryCount,
            RetryIntervalMs = RetryIntervalMs,
            ReadTimeoutMs = ReadTimeoutMs,
            IpAddress = IpAddress,
            Port = Port,
            EnableKeepAlive = EnableKeepAlive,
            KeepAliveIntervalMs = KeepAliveIntervalMs,
        };
    }
}
