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
        /// 以太网连接的默认读超时（毫秒）。
        /// <para><b>为什么与基类默认 5000 不同</b>：以太网帧小、链路快，一次静默超时的代价是
        /// "占掉若干拍 + 断线重连"；5000ms 在 1000ms 周期下等于白等 5 拍。取 2000 是折中——
        /// 网关/无线场景仍留有余量，而真超时能早 3 秒暴露。</para>
        /// <para><b>为什么取 2000 而不是 1000</b>：误判超时的代价（断链 + 3s 建连 + 1~5s 退避）
        /// 比它想省下的那 1 秒更贵；Modbus TCP 转 RTU 网关（后端 9600bps）读 120 寄存器约 0.25s，
        /// 1000ms 只剩 1.3 倍余量，容易误判。</para>
        /// <para>串口不走这里（<see cref="SerialConfig"/> 直接继承基类），保持 5000ms：
        /// 2400bps 下单帧 256 字节本身就约 1.07s，压不下去。</para>
        /// </summary>
        private const int DefaultReadTimeoutMs = 2000;

        /// <summary>
        /// 以太网配置构造：把读超时默认值下调到 <see cref="DefaultReadTimeoutMs"/>。
        /// <para>放在基类构造器而不是工厂里：任何新建的以太网配置（含属性面板"新建连接"、
        /// 方案里缺该字段的老文件）都拿到同一个值，不需要每个协议各写一遍。</para>
        /// </summary>
        protected EthernetConfigBase()
        {
            ReadTimeoutMs = DefaultReadTimeoutMs;
        }
        /// <summary>
        /// <para>获取或设置目标设备的 IP 地址。</para>
        /// <para>默认值：127.0.0.1</para>
        /// </summary>
        [SuperDisplay(Name = "IP 地址", Group = new string[] { "网络参数" }, Order = 1, ColSpan = 8)]
        [Required(ErrorMessage = "IP 地址不能为空")]
        [Icon(IconCode = "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z")]
        [RegexValidation(@"^((25[0-5]|2[0-4]\d|[01]?\d\d?)\.){3}(25[0-5]|2[0-4]\d|[01]?\d\d?)$", "IP 地址格式不合法")]
        public string IpAddress { get; set; } = "127.0.0.1";

        /// <summary>
        /// <para>获取或设置目标设备的端口号。</para>
        /// <para>范围：1-65535</para>
        /// <para>默认值：502 (Modbus TCP 默认端口)</para>
        /// </summary>
        [SuperDisplay(Name = "端口号", Group = new string[] { "网络参数" }, Order = 2, ColSpan = 4)]
        [RangeValidation(1, 65535, "端口范围：1-65535")]
        public virtual int Port { get; set; } = 502;

        /// <summary>
        /// <para>获取或设置是否启用 KeepAlive 心跳机制。</para>
        /// <para>默认值：true</para>
        /// </summary>
        [SuperDisplay(Name = "开启 KeepAlive", Group = new string[] { "网络参数" }, Order = 3, ColSpan = 6)]
        public bool EnableKeepAlive { get; set; } = true;

        /// <summary>
        /// <para>获取或设置心跳间隔时间（毫秒）。</para>
        /// <para>默认值：30000ms (30秒)</para>
        /// <para>⚠ 语义：HSL 的 <c>SetKeepAlive(time, interval)</c> 两个参数都取本值（P-Q2）——
        /// 它同时是<b>首次探测前的空闲时长</b>（Win32 <c>keepalivetime</c>）与<b>探测间隔</b>
        /// （<c>keepaliveinterval</c>）。所以默认 30s 意味着"链路空闲 30 秒后才发第一个探测包"，
        /// 对半开链路的检测贡献弱于"心跳间隔"这个名字给人的直觉；要更快发现半开可调小本值。</para>
        /// </summary>
        [SuperDisplay(Name = "心跳间隔(ms)", Group = new string[] { "网络参数" }, Order = 4, ColSpan = 6)]
        public int KeepAliveIntervalMs { get; set; } = 30000;


        public override string ToString()
        {
            return IpAddress + ":" + Port;
        }
    }
}
