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
        [SuperDisplay(Name = "PLC 网络号", GroupPath = "FINS专有参数", Order = 5, ColSpan = 4)]
        [RangeValidation(0, 127, "网络号范围：0-127")]
        public byte NetworkNumber { get; set; } = 0;

        /// <summary>
        /// <para>获取或设置 PLC 节点号。</para>
        /// <para>范围：0-254</para>
        /// <para>默认值：0</para>
        /// </summary>
        [SuperDisplay(Name = "PLC 节点号", GroupPath = "FINS专有参数", Order = 6, ColSpan = 4)]
        [RangeValidation(0, 254, "节点号范围：0-254")]
        public byte NodeNumber { get; set; } = 0;

        /// <summary>
        /// <para>获取或设置 PLC 单元号。</para>
        /// <para>范围：0-255</para>
        /// <para>默认值：0</para>
        /// </summary>
        [SuperDisplay(Name = "PLC 单元号", GroupPath = "FINS专有参数", Order = 7, ColSpan = 4)]
        [RangeValidation(0, 255, "单元号范围：0-255")]
        public byte UnitNumber { get; set; } = 0;

        /// <summary>
        /// <para>获取或设置响应超时时间（毫秒）。</para>
        /// <para>默认值：5000ms</para>
        /// </summary>
        [SuperDisplay(Name = "响应超时(ms)", GroupPath = "FINS专有参数", Order = 8, ColSpan = 6)]
        public int ResponseTimeoutMs { get; set; } = 5000;

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
        public override ICommunicationConnection CreateConnection() => throw new NotImplementedException();

        /// <summary>
        /// <para>克隆当前配置对象。</para>
        /// </summary>
        /// <returns>配置副本</returns>
        public override ConnectionConfigBase Clone() => new OmronFinsConfig
        {
            TimeoutMs = TimeoutMs,
            RetryCount = RetryCount,
            RetryIntervalMs = RetryIntervalMs,
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