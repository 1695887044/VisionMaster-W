using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using HslCommunication.Core.Address;
using UI.Attributes;

namespace VisionMaster.Communications
{
    /// <summary>欧姆龙 FINS 的常用存储区。</summary>
    public enum FinsArea
    {
        [Display(Name = "D DM 区"), Description("D DM 区")]
        D,

        [Display(Name = "CIO 区"), Description("CIO 区")]
        CIO,

        [Display(Name = "W Work 区"), Description("W Work 区")]
        W,

        [Display(Name = "H Hold 区"), Description("H Hold 区")]
        H,

        [Display(Name = "A Auxiliary 区"), Description("A Auxiliary 区")]
        A,
    }

    /// <summary>
    /// FINS 地址配置：Area 选存储区，Offset 填十进制编号（如 D + 100 = D100）。
    /// 批量轮询只接字区；位地址（D100.0 等）走单读。
    /// </summary>
    public class FinsAddress : DeviceAddressBase<FinsArea>
    {
        /// <summary>
        /// FINS 的字是 16 位，位地址（D100.0 ~ D100.15）覆盖整字；
        /// 基类默认的 7 是"字节内位"（Modbus/S7），照搬会把 D100.12 静默钳成 D100.7。
        /// </summary>
        public override int MaxBitOffset => 15;

        protected override string BuildAddress()
        {
            int start = (int.TryParse(Offset, out int n) ? n : 0) + AddressOffset;
            string baseAddress = $"{Area}{start}";
            // FINS 的位地址是 "D100.0" 形态；字地址不带点
            return BitOffset >= 0 ? $"{baseAddress}.{BitOffset}" : baseAddress;
        }

        public override bool TryCreatePollAddress(out PollAddress? poll)
        {
            poll = null;
            if (!TryParse(out int start, out bool isBit, out _) || isBit) return false;

            start = Math.Max(0, start + AddressOffset);
            int words = Math.Max(1, (TotalBytes + 1) / 2);
            poll = new PollAddress
            {
                Protocol = PollProtocol.Fins,
                GroupKey = Area.ToString(),
                SegmentPrefix = Area.ToString(),
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

            // FINS 的布尔量必须带位偏移（D100.0），否则字区语义不明确
            if (DataType == DataValueType.Boolean && BitOffset < 0)
                return (false, "FINS 布尔量请填位偏移（如 D100.0）");

            if (!TryParse(out _, out _, out string error)) return (false, error);
            return (true, string.Empty);
        }

        private bool TryParse(out int start, out bool isBit, out string error)
        {
            start = 0;
            isBit = false;
            error = string.Empty;

            // 位地址必须把 BitOffset 拼进去（D100 + BitOffset=0 → D100.0），
            // 否则 TryCreatePollAddress 会把位地址当字地址批量。
            string text = BitOffset >= 0 ? $"{Area}{Offset}.{BitOffset}" : $"{Area}{Offset}";
            var parsed = OmronFinsAddress.ParseFrom(text);
            if (!parsed.IsSuccess)
            {
                error = $"地址「{text}」不符合 FINS 地址格式：{parsed.Message}";
                return false;
            }

            // HSL 的 AddressStart 是"位"单位（D100 → 1600），字地址要除以 16。
            // BitCode 是区域命令码（D 区的 BitCode 也不是 0），不能拿来判"位访问"；
            // 位访问的唯一可靠判据是地址里带 ".位号"（D100.0）。
            start = parsed.Content.AddressStart / 16;
            isBit = text.IndexOf('.') >= 0;
            return true;
        }
    }
}
