using UI.Attributes;

namespace VisionMaster.Communications
{
    /// <summary>
    /// 西门子 S7 CPU 类型（自声明枚举，成员名与 Hsl SiemensPLCS 一一对应，
    /// 由通讯连接层负责映射，保证契约层不依赖 Hsl 库）。
    /// </summary>
    public enum S7CpuType
    {
        S200,
        S200Smart,
        S300,
        S400,
        S500,
        S1200,
        S1500
    }

    /// <summary>
    /// <para>西门子 S7 协议配置类。</para>
    /// <para>用于配置西门子 S7 系列 PLC 的连接参数。</para>
    /// </summary>
    public class SiemensS7Config : EthernetConfigBase
    {

        [SuperDisplay(Name = "CPU 类型", GroupPath = "S7专有参数", Order = 5, ColSpan = 4)]
        public S7CpuType S7CpuType { get; set; } = S7CpuType.S1200;

        [SuperDisplay(Name = "机架号(Rack)", GroupPath = "S7专有参数", Order = 6, ColSpan = 4)]
        public byte Rack { get; set; } = 0;


        public override int Port { get; set; } = 102;

        [SuperDisplay(Name = "插槽号(Slot)", GroupPath = "S7专有参数", Order = 7, ColSpan = 4)]
        public byte Slot { get; set; } = 0;



        public override ConnectionConfigBase Clone() => new SiemensS7Config
        {
            TimeoutMs = TimeoutMs,
            RetryCount = RetryCount,
            RetryIntervalMs = RetryIntervalMs,
            ReadTimeoutMs = ReadTimeoutMs,
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
