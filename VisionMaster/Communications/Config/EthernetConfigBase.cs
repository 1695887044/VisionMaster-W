using System.ComponentModel.DataAnnotations;
using UI.Attributes;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>以太网连接配置基类。</para>
    /// <para>用于配置基于 TCP/IP 的通信协议参数。</para>
    /// </summary>
    public abstract class EthernetConfigBase : ConnectionConfigBase
    {
        /// <summary>
        /// <para>获取或设置目标设备的 IP 地址。</para>
        /// <para>默认值：127.0.0.1</para>
        /// </summary>
        [SuperDisplay(Name = "IP 地址", GroupPath = "网络参数", Order = 1, ColSpan = 8)]
        [Required(ErrorMessage = "IP 地址不能为空")]
        [Icon(IconCode = "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z")]
        [RegexValidation(@"^((25[0-5]|2[0-4]\d|[01]?\d\d?)\.){3}(25[0-5]|2[0-4]\d|[01]?\d\d?)$", "IP 地址格式不合法")]
        public string IpAddress { get; set; } = "127.0.0.1";

        /// <summary>
        /// <para>获取或设置目标设备的端口号。</para>
        /// <para>范围：1-65535</para>
        /// <para>默认值：502 (Modbus TCP 默认端口)</para>
        /// </summary>
        [SuperDisplay(Name = "端口号", GroupPath = "网络参数", Order = 2, ColSpan = 4)]
        [RangeValidation(1, 65535, "端口范围：1-65535")]
        public int Port { get; set; } = 502;

        /// <summary>
        /// <para>获取或设置是否启用 KeepAlive 心跳机制。</para>
        /// <para>默认值：true</para>
        /// </summary>
        [SuperDisplay(Name = "开启 KeepAlive", GroupPath = "网络参数", Order = 3, ColSpan = 6)]
        public bool EnableKeepAlive { get; set; } = true;

        /// <summary>
        /// <para>获取或设置心跳间隔时间（毫秒）。</para>
        /// <para>默认值：30000ms (30秒)</para>
        /// </summary>
        [SuperDisplay(Name = "心跳间隔(ms)", GroupPath = "网络参数", Order = 4, ColSpan = 6)]
        public int KeepAliveIntervalMs { get; set; } = 30000;
    }
}