using UI.Attributes;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>三菱 MC 协议配置类。</para>
    /// <para>用于配置三菱 PLC 的 MC 协议连接参数。</para>
    /// </summary>
    public class MitsubishiMcConfig : EthernetConfigBase
    {
        /// <summary>
        /// <para>获取或设置 PLC 型号。</para>
        /// <para>支持的值：QnA, Q, L, FX, A</para>
        /// <para>默认值：QnA</para>
        /// </summary>
        [SuperDisplay(Name = "PLC 型号", GroupPath = "MC专有参数", Order = 5, ColSpan = 6)]
        public string PlcModel { get; set; } = "QnA";

        /// <summary>
        /// <para>获取或设置网络号。</para>
        /// <para>范围：0-255</para>
        /// <para>默认值：0</para>
        /// </summary>
        [SuperDisplay(Name = "网络号", GroupPath = "MC专有参数", Order = 6, ColSpan = 4)]
        [RangeValidation(0, 255, "网络号范围：0-255")]
        public byte NetworkNumber { get; set; } = 0;

        /// <summary>
        /// <para>获取或设置站号。</para>
        /// <para>范围：0-255</para>
        /// <para>默认值：0</para>
        /// </summary>
        [SuperDisplay(Name = "站号", GroupPath = "MC专有参数", Order = 7, ColSpan = 4)]
        [RangeValidation(0, 255, "站号范围：0-255")]
        public byte StationNumber { get; set; } = 0;

        /// <summary>
        /// <para>获取或设置协议类型。</para>
        /// <para>默认值：Tcp</para>
        /// </summary>
        [SuperDisplay(Name = "协议类型", GroupPath = "MC专有参数", Order = 8, ColSpan = 6)]
        public McProtocolType ProtocolType { get; set; } = McProtocolType.Tcp;

        /// <summary>
        /// <para>获取或设置帧格式。</para>
        /// <para>默认值：Binary</para>
        /// </summary>
        [SuperDisplay(Name = "帧格式", GroupPath = "MC专有参数", Order = 9, ColSpan = 6)]
        public McFrameFormat FrameFormat { get; set; } = McFrameFormat.Binary;

        /// <summary>
        /// <para>初始化三菱 MC 配置。</para>
        /// <para>默认端口设置为 8000（MC 协议默认端口）。</para>
        /// </summary>
        public MitsubishiMcConfig()
        {
            Port = 8000;
        }

        /// <summary>
        /// <para>创建三菱 MC 连接对象。</para>
        /// <para>注意：此方法尚未实现。</para>
        /// </summary>
        /// <returns>MC 连接实例</returns>
        public override ICommunicationConnection CreateConnection() => throw new NotImplementedException();

        /// <summary>
        /// <para>克隆当前配置对象。</para>
        /// </summary>
        /// <returns>配置副本</returns>
        public override ConnectionConfigBase Clone() => new MitsubishiMcConfig
        {
            TimeoutMs = TimeoutMs,
            RetryCount = RetryCount,
            RetryIntervalMs = RetryIntervalMs,
            IpAddress = IpAddress,
            Port = Port,
            EnableKeepAlive = EnableKeepAlive,
            KeepAliveIntervalMs = KeepAliveIntervalMs,
            PlcModel = PlcModel,
            NetworkNumber = NetworkNumber,
            StationNumber = StationNumber,
            ProtocolType = ProtocolType,
            FrameFormat = FrameFormat
        };
    }
}