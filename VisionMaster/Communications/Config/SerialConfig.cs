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
        /// <para>创建串口连接对象。</para>
        /// </summary>
        /// <returns>串口连接实例</returns>
        public override ICommunicationConnection CreateConnection() => new SerialConnection(this);

        /// <summary>
        /// <para>克隆当前配置对象。</para>
        /// </summary>
        /// <returns>配置副本</returns>
        public override ConnectionConfigBase Clone() => new SerialConfig
        {
            TimeoutMs = TimeoutMs, 
            RetryCount = RetryCount, 
            RetryIntervalMs = RetryIntervalMs,
            PortName = PortName, 
            BaudRate = BaudRate, 
            DataBits = DataBits, 
            Parity = Parity, 
            StopBits = StopBits
        };
    }
}