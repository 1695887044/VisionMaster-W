namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>Modbus TCP 协议配置类。</para>
    /// <para>用于配置 Modbus TCP 协议的连接参数。</para>
    /// </summary>
    public class ModbusTcpConfig : EthernetConfigBase
    {
        /// <summary>
        /// <para>创建 Modbus TCP 连接对象。</para>
        /// </summary>
        /// <returns>Modbus TCP 连接实例</returns>
        public override ICommunicationConnection CreateConnection() => new ModbusTcpConnection(this);

        /// <summary>
        /// <para>克隆当前配置对象。</para>
        /// </summary>
        /// <returns>配置副本</returns>
        public override ConnectionConfigBase Clone() => new ModbusTcpConfig
        {
            TimeoutMs = TimeoutMs, 
            RetryCount = RetryCount, 
            RetryIntervalMs = RetryIntervalMs,
            IpAddress = IpAddress, 
            Port = Port, 
            EnableKeepAlive = EnableKeepAlive, 
            KeepAliveIntervalMs = KeepAliveIntervalMs
        };
    }
}