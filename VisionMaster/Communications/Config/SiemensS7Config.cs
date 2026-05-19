using UI.Attributes;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>西门子 S7 协议配置类。</para>
    /// <para>用于配置西门子 S7 系列 PLC 的连接参数。</para>
    /// </summary>
    public class SiemensS7Config : EthernetConfigBase
    {
        /// <summary>
        /// <para>获取或设置 CPU 类型。</para>
        /// <para>支持的值：S7200, S7300, S7400, S1200, S1500</para>
        /// <para>默认值：S1200</para>
        /// </summary>
        [SuperDisplay(Name = "CPU 类型", GroupPath = "S7专有参数", Order = 5, ColSpan = 4)]
        public string S7CpuType { get; set; } = "S1200";

        /// <summary>
        /// <para>获取或设置机架号 (Rack)。</para>
        /// <para>默认值：0</para>
        /// </summary>
        [SuperDisplay(Name = "机架号(Rack)", GroupPath = "S7专有参数", Order = 6, ColSpan = 4)]
        public byte Rack { get; set; } = 0;

        /// <summary>
        /// <para>获取或设置插槽号 (Slot)。</para>
        /// <para>默认值：0</para>
        /// </summary>
        [SuperDisplay(Name = "插槽号(Slot)", GroupPath = "S7专有参数", Order = 7, ColSpan = 4)]
        public byte Slot { get; set; } = 0;

        /// <summary>
        /// <para>创建西门子 S7 连接对象。</para>
        /// </summary>
        /// <returns>S7 连接实例</returns>
        public override ICommunicationConnection CreateConnection() => new SiemensS7Connection(this);

        /// <summary>
        /// <para>克隆当前配置对象。</para>
        /// </summary>
        /// <returns>配置副本</returns>
        public override ConnectionConfigBase Clone() => new SiemensS7Config
        {
            TimeoutMs = TimeoutMs, 
            RetryCount = RetryCount, 
            RetryIntervalMs = RetryIntervalMs,
            IpAddress = IpAddress, 
            Port = Port, 
            EnableKeepAlive = EnableKeepAlive, 
            KeepAliveIntervalMs = KeepAliveIntervalMs,
            S7CpuType = S7CpuType, 
            Rack = Rack, 
            Slot = Slot
        };
    }
}