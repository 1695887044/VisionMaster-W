using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using HslCommunication;
using HslCommunication.Core.Address;
using UI.Attributes;

namespace VisionMaster.Communications
{
    /// <summary>三菱/基恩士/松下 MC 的存储区（只列常用区；位区不参与批量）。</summary>
    public enum McArea
    {
        [Display(Name = "D 数据寄存器"), Description("D 数据寄存器")]
        D,

        [Display(Name = "W 链接寄存器"), Description("W 链接寄存器")]
        W,

        [Display(Name = "R 文件寄存器"), Description("R 文件寄存器")]
        R,

        [Display(Name = "ZR 扩展文件寄存器"), Description("ZR 扩展文件寄存器")]
        ZR,

        [Display(Name = "M 内部继电器"), Description("M 内部继电器")]
        M,

        [Display(Name = "X 输入继电器"), Description("X 输入继电器")]
        X,

        [Display(Name = "Y 输出继电器"), Description("Y 输出继电器")]
        Y,

        [Display(Name = "L 锁存继电器"), Description("L 锁存继电器")]
        L,

        [Display(Name = "B 链接继电器"), Description("B 链接继电器")]
        B,

        [Display(Name = "F 报警器"), Description("F 报警器")]
        F,

        [Display(Name = "S 步进继电器"), Description("S 步进继电器")]
        S,

        [Display(Name = "V 边缘继电器"), Description("V 边缘继电器")]
        V,
    }

    /// <summary>
    /// MC 地址配置：Area 选存储区，Offset 填该区的编号（如 D + 100 = D100）。
    /// 批量轮询只接字区（D/W/R/ZR）；位区（M/X/Y/L/B/F/S/V）走单读——
    /// 位区的"连续点打包"解码与字区完全不同，先不混在一起。
    ///
    /// <para><b>Offset 的进制随设备区</b>：X / Y / B / W 是十六进制编址（HSL 的
    /// <c>MelsecMcDataType</c> 里 <c>FromBase = 16</c>），ZR 的进制随厂商（三菱十进制、
    /// 基恩士十六进制），其余为十进制。故本类统一按 <see cref="Radix"/> 解析与回写，
    /// 用户填 <c>1A</c> 表示 X1A，填 <c>100</c> 表示该设备区里的 100（W 区即 0x100）。</para>
    ///
    /// <para><b>为什么 Start 必须按十进制保存、段地址必须显式覆盖</b>：批量规划器用
    /// <c>SegmentPrefix + Start</c> 拼段地址，而 Start 是解析后的十进制设备号。
    /// 对十六进制区直接拼回去会二次错位：用户填 W100（= 0x100 = 256）→ Start=256 →
    /// 段地址拼成 "W256" → HSL 再按十六进制解析成 0x256 = 598。单读打 256、批读打 598，
    /// 同一个变量读到两个不同设备。故这里显式提供 <c>SegmentAddressOverride</c>，
    /// 保证单读与批读给出同一个串。</para>
    /// </summary>
    public class McAddress : DeviceAddressBase<McArea>
    {
        /// <summary>
        /// 厂商变体（只影响 ZR 的进制与地址校验用的解析器）。
        /// 默认三菱——存量变量没有这个字段，反序列化后必须仍按三菱口径工作。
        /// </summary>
        [SuperDisplay(Name = "厂商变体")]
        public McVendor Vendor { get; set; } = McVendor.Mitsubishi;

        /// <summary>当前存储区的编址进制（X/Y/B/W=16；ZR 随厂商；其余=10）</summary>
        internal int Radix => Area switch
        {
            McArea.X or McArea.Y or McArea.B or McArea.W => 16,
            McArea.ZR => Vendor == McVendor.Keyence ? 16 : 10,
            _ => 10,
        };

        /// <summary>按存储区的进制解析 <see cref="DeviceAddressBase.Offset"/>（十六进制区允许 "1A" / "0x1A"）</summary>
        protected override bool TryParseBaseStart(out int start) => TryParseOffset(Offset, Radix, out start);

        protected override string BuildAddress()
        {
            int device = TryParseOffset(Offset, Radix, out int parsed) ? parsed : 0;
            // 位区同样要吃「地址偏移」：旧实现位区直接返回 {Area}{Offset}，
            // 导致"地址偏移"控件在位区填了完全无效（FINS 那边是加了的，两兄弟口径必须一致）。
            int start = Math.Max(0, device + AddressOffset);
            return $"{Area}{Format(start)}";
        }

        public override bool TryCreatePollAddress(out PollAddress? poll)
        {
            poll = null;
            if (IsBitArea(Area)) return false; // 位区不批量
            if (!TryParse(out int start, out _, out _)) return false;

            start = Math.Max(0, start + AddressOffset);
            int words = Math.Max(1, (TotalBytes + 1) / 2);
            poll = new PollAddress
            {
                Protocol = PollProtocol.Mc,
                GroupKey = Area.ToString(),
                SegmentPrefix = Area.ToString(),
                // 关键：段地址按该设备区的进制回写，不能把十进制 Start 直接拼回去
                SegmentAddressOverride = $"{Area}{Format(start)}",
                Start = start,
                SpanUnits = words,
                IsBitArea = false,
                IsBitAccess = false,
                BitOffset = -1,
            };
            return true;
        }

        public override (bool IsValid, string ErrorMessage) Validate()
        {
            var baseResult = base.Validate();
            if (!baseResult.IsValid) return baseResult;

            // D/W/R/ZR 是字区，不能直接当布尔点位；布尔请选 M/X/Y/L/B/F/S/V 位区
            if (DataType == DataValueType.Boolean && !IsBitArea(Area))
                return (false, "MC 的布尔量请选位区（M/X/Y/L/B/F/S/V）；D/W/R/ZR 是字区");

            // 反向同样要拦：位区按字读（把 16 个位当整数）语义不直观，且与"位区不批量"冲突。
            // 旧实现只拦了一个方向，位区配 Int32/Double 会在创建时全绿、轮询时才出怪值。
            if (DataType != DataValueType.Boolean && IsBitArea(Area))
                return (false, $"MC 的位区（{Area}）只能建布尔量；数值请选字区（D/W/R/ZR）");

            if (!TryParse(out _, out _, out string error)) return (false, error);
            return (true, string.Empty);
        }

        /// <summary>
        /// 按厂商解析器校验地址（三菱/基恩士/松下的地址语法不同：D100 / DM100 / DT100），
        /// 与真实读写同源，避免自己写正则漏掉厂商差异。
        /// </summary>
        private bool TryParse(out int start, out bool isBit, out string error)
        {
            start = 0;
            isBit = IsBitArea(Area);
            error = string.Empty;

            if (!TryParseOffset(Offset, Radix, out int device))
            {
                error = $"偏移量「{Offset}」不是有效的{(Radix == 16 ? "十六进制" : "十进制")}地址";
                return false;
            }

            string text = $"{Area}{Format(device)}";
            var parsed = Parser(text, 1, isBit);
            if (!parsed.IsSuccess)
            {
                error = $"地址「{text}」不符合 {Vendor} 的 MC 地址格式：{parsed.Message}";
                return false;
            }

            start = parsed.Content.AddressStart;
            return true;
        }

        private Func<string, ushort, bool, OperateResult<McAddressData>> Parser => Vendor switch
        {
            McVendor.Keyence => McAddressData.ParseKeyenceFrom,
            McVendor.Panasonic => McAddressData.ParsePanasonicFrom,
            _ => McAddressData.ParseMelsecFrom,
        };

        private string Format(int value) => Radix == 16
            ? value.ToString("X", CultureInfo.InvariantCulture)
            : value.ToString(CultureInfo.InvariantCulture);

        private static bool TryParseOffset(string? text, int radix, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string s = text.Trim();
            if (radix == 16 && s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(2);

            var style = radix == 16 ? NumberStyles.HexNumber : NumberStyles.Integer;
            return int.TryParse(s, style, CultureInfo.InvariantCulture, out value) && value >= 0;
        }

        private static bool IsBitArea(McArea area) => area is
            McArea.M or McArea.X or McArea.Y or McArea.L or
            McArea.B or McArea.F or McArea.S or McArea.V;
    }
}
