using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using UI.Attributes;

namespace VisionMaster.Communications
{
    /// <summary>MC 3E 帧的厂商变体（地址格式不同，客户端类不同）。</summary>
    public enum McVendor
    {
        [Display(Name = "三菱")]
        [Description("三菱")]
        Mitsubishi = 0,

        [Display(Name = "基恩士")]
        [Description("基恩士")]
        Keyence = 1,

        [Display(Name = "松下")]
        [Description("松下")]
        Panasonic = 2,
    }

    /// <summary>
    /// <para>三菱 MC 协议配置类。</para>
    /// <para>用于配置三菱 PLC 的 MC 协议连接参数。</para>
    /// </summary>
    public class MitsubishiMcConfig : EthernetConfigBase
    {
        /// <summary>
        /// 厂商变体：三菱 / 基恩士 / 松下。
        /// 三家都用 QnA 兼容 3E 帧，但地址解析规则不同（D100 vs DM100 vs DT100 等），
        /// 所以必须选对厂商；选错的表现是"地址解析失败"而不是连不上。
        /// </summary>
        [SuperDisplay(Name = "厂商", Group = new string[] { "MC专有参数" }, Order = 4, ColSpan = 6,
            Description = "三菱 / 基恩士 / 松下：同走 3E 帧，地址格式不同（D100 / DM100 / DT100）")]
        public McVendor Vendor { get; set; } = McVendor.Mitsubishi;

        /// <summary>
        /// <para>获取或设置 PLC 型号。</para>
        /// <para>支持的值：QnA, Q, L, FX, A</para>
        /// <para>默认值：QnA</para>
        /// <para>⚠ 当前连接实现（<c>MitsubishiMcConnection</c>）不消费本字段：3E 帧的读写命令
        /// 由地址类型决定，与型号无关。与 <c>SerialConfig.TimeoutMs</c> 同一口径——
        /// "改了没有任何反应"的输入框必须隐藏，字段本身保留以兼容存量配置的序列化。</para>
        /// </summary>
        [SuperDisplay(Visible = false)]
        public string PlcModel { get; set; } = "QnA";

        /// <summary>
        /// <para>获取或设置网络号。</para>
        /// <para>范围：0-255</para>
        /// <para>默认值：0</para>
        /// </summary>
        [SuperDisplay(Name = "网络号", Group = new string[] { "MC专有参数" }, Order = 6, ColSpan = 4)]
        [RangeValidation(0, 255, "网络号范围：0-255")]
        public byte NetworkNumber { get; set; } = 0;

        /// <summary>
        /// <para>获取或设置站号。</para>
        /// <para>范围：0-255</para>
        /// <para>默认值：0</para>
        /// </summary>
        [SuperDisplay(Name = "站号", Group = new string[] { "MC专有参数" }, Order = 7, ColSpan = 4)]
        [RangeValidation(0, 255, "站号范围：0-255")]
        public byte StationNumber { get; set; } = 0;

        /// <summary>
        /// <para>获取或设置协议类型。</para>
        /// <para>默认值：Tcp</para>
        /// </summary>
        [SuperDisplay(Name = "协议类型", Group = new string[] { "MC专有参数" }, Order = 8, ColSpan = 6)]
        public McProtocolType ProtocolType { get; set; } = McProtocolType.Tcp;

        /// <summary>
        /// <para>获取或设置帧格式。</para>
        /// <para>默认值：Binary</para>
        /// </summary>
        [SuperDisplay(Name = "帧格式", Group = new string[] { "MC专有参数" }, Order = 9, ColSpan = 6)]
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
        /// UDP 暂未实现：当前连接实现只提供 TCP 二进制/ASCII。
        /// 在 Validate 阶段就拦住，避免现场"参数全填好、保存时才报错"。
        /// </summary>
        public override bool Validate(out string errorMessage)
        {
            if (!base.Validate(out errorMessage)) return false;
            if (ProtocolType == McProtocolType.Udp)
            {
                errorMessage = "三菱 MC 的 UDP 模式暂未实现（当前支持 TCP 二进制/ASCII）；请把「协议类型」改为 Tcp。";
                return false;
            }
            if (Vendor != McVendor.Mitsubishi && FrameFormat == McFrameFormat.Ascii)
            {
                errorMessage = "基恩士/松下的 MC 变体当前只支持二进制帧；请把「帧格式」改为 Binary，或把「厂商」改回三菱。";
                return false;
            }
            return true;
        }

        /// <summary>
        /// <para>创建三菱 MC 连接对象。</para>
        /// <para>注意：此方法尚未实现。</para>
        /// </summary>
        /// <returns>MC 连接实例</returns>

        /// <summary>
        /// <para>克隆当前配置对象。</para>
        /// <para>⚠ 必须拷贝基类 <see cref="ConnectionConfigBase.Type"/>：它默认是 0（= ModbusTcp），
        /// 漏拷会让副本自报为 ModbusTcp，而 <see cref="CommunicationConfig.Validate"/> 会判
        /// "协议类型不匹配"——表现是"编辑连接点确定就失败"，且此时旧连接已被管理器移除。</para>
        /// </summary>
        /// <returns>配置副本</returns>
        public override ConnectionConfigBase Clone() => new MitsubishiMcConfig
        {
            Type = Type,
            TimeoutMs = TimeoutMs,
            RetryCount = RetryCount,
            RetryIntervalMs = RetryIntervalMs,
            ReadTimeoutMs = ReadTimeoutMs,
            IpAddress = IpAddress,
            Port = Port,
            EnableKeepAlive = EnableKeepAlive,
            KeepAliveIntervalMs = KeepAliveIntervalMs,
            PlcModel = PlcModel,
            Vendor = Vendor,
            NetworkNumber = NetworkNumber,
            StationNumber = StationNumber,
            ProtocolType = ProtocolType,
            FrameFormat = FrameFormat
        };
    }
}

