using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;
using UI.Attributes;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>Modbus TCP 协议配置类。</para>
    /// <para>用于配置 Modbus TCP 协议的连接参数。</para>
    /// </summary>
    public class ModbusTcpConfig : EthernetConfigBase
    {
        /// <summary>
        /// <para>多寄存器数值（32/64 位）在线路上的字节排列顺序，读写两条链路共用同一字序。</para>
        /// <para>ABCD = 大端标准；CDAB = 字序交换（多数国产 PLC/仪表默认）；另见 <see cref="ByteOrderFormat"/>。</para>
        /// <para>⚠ 落盘键名刻意保留历史拼写 "ByteoRDER"：改名会让存量 communications.json / 方案文件里的字序丢失并回落默认值。</para>
        /// </summary>
        [SuperDisplay(Name = "字节排序", GroupPath = "网络参数", Order = 6, ColSpan = 8)]
        [JsonProperty("ByteoRDER")]
        public ByteOrderFormat ByteOrder { get; set; } = ByteOrderFormat.CDAB;

        /// <summary>
        /// <para>克隆当前配置对象。</para>
        /// </summary>
        /// <returns>配置副本</returns>
        public override ConnectionConfigBase Clone() => new ModbusTcpConfig
        {
            TimeoutMs = TimeoutMs, 
            RetryCount = RetryCount, 
            RetryIntervalMs = RetryIntervalMs,
            ReadTimeoutMs = ReadTimeoutMs,
            IpAddress = IpAddress, 
            Port = Port, 
            EnableKeepAlive = EnableKeepAlive, 
            KeepAliveIntervalMs = KeepAliveIntervalMs,
            ByteOrder = ByteOrder
        };
    }
}
