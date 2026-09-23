using System.ComponentModel.DataAnnotations;
using UI.Attributes;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>串口连接配置类。</para>
    /// <para>用于配置 Modbus RTU 等串口通信协议的参数。</para>
    /// </summary>
    public class SerialConfig : ConnectionConfigBase
    {
        /// <summary>
        /// <para>获取或设置串口号。</para>
        /// <para>格式：COM1, COM2, ...</para>
        /// <para>默认值：COM1</para>
        /// </summary>
        [SuperDisplay(Name = "串口号", GroupPath = "串口参数", Order = 1, ColSpan = 6)]
        [Icon(IconCode = "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8z")]
        [Required(ErrorMessage = "串口号不能为空")]
        [RegexValidation(@"^COM\d+$", "格式必须如 COM1, COM2")]
        public string PortName { get; set; } = "COM1";

        /// <summary>
        /// <para>获取或设置波特率。</para>
        /// <para>常用值：2400, 4800, 9600, 19200, 38400, 57600, 115200</para>
        /// <para>默认值：9600</para>
        /// </summary>
        [SuperDisplay(Name = "波特率", GroupPath = "串口参数", Order = 2, ColSpan = 6)]
        public int BaudRate { get; set; } = 9600;

        /// <summary>
        /// <para>获取或设置数据位。</para>
        /// <para>默认值：8</para>
        /// </summary>
        [SuperDisplay(Name = "数据位", GroupPath = "串口参数", Order = 3, ColSpan = 4)]
        public int DataBits { get; set; } = 8;

        /// <summary>
        /// <para>获取或设置校验位。</para>
        /// <para>默认值：None</para>
        /// </summary>
        [SuperDisplay(Name = "校验位", GroupPath = "串口参数", Order = 4, ColSpan = 4)]
        public ParityMode Parity { get; set; } = ParityMode.None;

        /// <summary>
        /// <para>获取或设置停止位。</para>
        /// <para>默认值：One</para>
        /// </summary>
        [SuperDisplay(Name = "停止位", GroupPath = "串口参数", Order = 5, ColSpan = 4)]
        public StopBitsMode StopBits { get; set; } = StopBitsMode.One;

        /// <summary>
        /// <para><b>串口不使用"连接超时"</b>——HSL 的 <c>ModbusRtu.Open()</c> 只是打开本地串口句柄，
        /// 不存在网络建连等待，所以基类的 <see cref="ConnectionConfigBase.TimeoutMs"/> 在串口上无处消费。</para>
        /// <para>串口真正生效的超时是「读超时(ms)」（<see cref="ConnectionConfigBase.ReadTimeoutMs"/>）。</para>
        /// <para>这里用 <c>override</c> 把它从属性面板隐藏（<c>Visible = false</c>），
        /// 避免界面上出现一个"改了没有任何反应"的输入框。字段本身仍保留在模型中（默认 3000），
        /// 以保证与老方案的序列化兼容。</para>
        /// </summary>
        [SuperDisplay(Visible = false)]
        public override int TimeoutMs { get; set; } = 3000;

        /// <summary>
        /// <para>创建串口连接对象。</para>
        /// </summary>
        /// <returns>串口连接实例</returns>

        /// <summary>
        /// <para>克隆当前配置对象。</para>
        /// </summary>
        /// <returns>配置副本</returns>
        public override ConnectionConfigBase Clone() => new SerialConfig
        {
            TimeoutMs = TimeoutMs, 
            RetryCount = RetryCount, 
            RetryIntervalMs = RetryIntervalMs,
            ReadTimeoutMs = ReadTimeoutMs,
            PortName = PortName, 
            BaudRate = BaudRate, 
            DataBits = DataBits, 
            Parity = Parity, 
            StopBits = StopBits
        };
    }
}
