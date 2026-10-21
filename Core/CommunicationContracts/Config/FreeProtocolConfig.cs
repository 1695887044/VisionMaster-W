using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using UI.Attributes;

namespace VisionMaster.Communications
{
    /// <summary>自由协议文本编码。</summary>
    public enum FreeProtocolEncoding
    {
        [Display(Name = "ASCII")]
        [Description("ASCII")]
        Ascii = 0,

        [Display(Name = "UTF-8")]
        [Description("UTF-8")]
        Utf8 = 1,
    }

    /// <summary>
    /// 自由协议（通用 TCP 文本命令）配置。
    ///
    /// 适用场景：设备没有标准 PLC 协议，只接受"发一条 ASCII 命令、回一行文本"，
    /// 例如条码枪、称重仪、扭矩枪、部分光源控制器。它不是 Modbus 的替代品，
    /// 而是"客户自定义协议"的兜底通道。
    ///
    /// 地址语义：<see cref="ICommunicationConnection.Read{T}(string)"/> 的 address 就是
    /// 要发送的命令文本；<see cref="ICommunicationConnection.Write(string, object)"/> 的 address
    /// 含 <c>{0}</c> 时按模板格式化值，否则按"命令 + 值"拼接。响应按
    /// <see cref="FrameTerminator"/> 分帧，再按目标类型解析。
    /// </summary>
    public class FreeProtocolConfig : EthernetConfigBase
    {
        /// <summary>
        /// 帧结束符（支持 <c>\r</c> <c>\n</c> <c>\t</c> <c>\0</c> <c>\xNN</c> 转义）。
        /// 留空 = 读到超时/无更多数据为止（适合定长或设备主动断开的协议）。
        /// </summary>
        [SuperDisplay(Name = "帧结束符", Group = new string[] { "自由协议参数" }, Order = 5, ColSpan = 6,
            Description = "例如 \\r\\n；支持 \\r \\n \\t \\0 \\xNN；留空=读到超时")]
        public string FrameTerminator { get; set; } = "\\r\\n";

        /// <summary>命令与响应的文本编码。</summary>
        [SuperDisplay(Name = "文本编码", Group = new string[] { "自由协议参数" }, Order = 6, ColSpan = 6)]
        public FreeProtocolEncoding Encoding { get; set; } = FreeProtocolEncoding.Ascii;

        /// <summary>等待响应结束符的超时（毫秒）。</summary>
        [SuperDisplay(Name = "响应超时(ms)", Group = new string[] { "自由协议参数" }, Order = 7, ColSpan = 6)]
        [RangeValidation(100, 60000, "响应超时必须在 100 - 60000 之间")]
        public int ResponseTimeoutMs { get; set; } = 1000;

        /// <summary>单次响应字节上限（防止对端一直发导致内存膨胀）。</summary>
        [SuperDisplay(Name = "最大响应字节", Group = new string[] { "自由协议参数" }, Order = 8, ColSpan = 6)]
        [RangeValidation(1, 1048576, "最大响应字节必须在 1 - 1048576 之间")]
        public int MaxResponseBytes { get; set; } = 65536;

        /// <summary>
        /// 写入后是否等待响应。命令/响应型设备保持 true；只收不回的设备（如部分光源控制器）
        /// 设为 false，否则每次写都会白等一个响应超时。
        /// </summary>
        [SuperDisplay(Name = "写入等待响应", Group = new string[] { "自由协议参数" }, Order = 9, ColSpan = 6,
            Description = "命令/响应型设备保持开启；只收不回的设备关闭，避免每次写都等满超时")]
        public bool WriteExpectsResponse { get; set; } = true;

        public FreeProtocolConfig()
        {
            // 自由协议没有标准端口，5000 只是占位；现场按设备手册改
            Port = 5000;
        }

        public override bool Validate(out string errorMessage)
        {
            if (!base.Validate(out errorMessage)) return false;

            if (Port <= 0 || Port > 65535)
            {
                errorMessage = "端口必须在 1 - 65535 之间";
                return false;
            }
            if (ResponseTimeoutMs < 100 || ResponseTimeoutMs > 60000)
            {
                errorMessage = "响应超时必须在 100 - 60000 之间";
                return false;
            }
            if (MaxResponseBytes < 1 || MaxResponseBytes > 1048576)
            {
                errorMessage = "最大响应字节必须在 1 - 1048576 之间";
                return false;
            }
            if ((FrameTerminator ?? "").Length > 16)
            {
                errorMessage = "帧结束符最长 16 个字符（支持转义写法）";
                return false;
            }
            return true;
        }

        public override ConnectionConfigBase Clone() => new FreeProtocolConfig
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
            FrameTerminator = FrameTerminator,
            Encoding = Encoding,
            ResponseTimeoutMs = ResponseTimeoutMs,
            MaxResponseBytes = MaxResponseBytes,
            WriteExpectsResponse = WriteExpectsResponse,
        };
    }
}
