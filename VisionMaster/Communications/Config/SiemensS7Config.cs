using HslCommunication.Profinet.Siemens;
using UI.Attributes;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>西门子 S7 协议配置类。</para>
    /// <para>用于配置西门子 S7 系列 PLC 的连接参数。</para>
    /// </summary>
    public class SiemensS7Config : EthernetConfigBase
    {

        [SuperDisplay(Name = "CPU 类型", GroupPath = "S7专有参数", Order = 5, ColSpan = 4)]
        public SiemensPLCS S7CpuType { get; set; } = SiemensPLCS.S1200;

        [SuperDisplay(Name = "机架号(Rack)", GroupPath = "S7专有参数", Order = 6, ColSpan = 4)]
        public byte Rack { get; set; } = 0;


        public override int Port { get; set; } = 102;

        [SuperDisplay(Name = "插槽号(Slot)", GroupPath = "S7专有参数", Order = 7, ColSpan = 4)]
        public byte Slot { get; set; } = 0;


        public override ICommunicationConnection CreateConnection() => new SiemensS7Connection(this);

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