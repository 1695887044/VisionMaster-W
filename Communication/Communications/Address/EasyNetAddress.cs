using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using UI.Attributes;

namespace VisionMaster.Communications
{
    /// <summary>
    /// EasyNet 的存储区。顺序即变量管理下拉顺序：先字区（D/R/W），再位区（M/S/B/X/Y），最后 U 系列。
    /// <para>U 区地址是**十六进制位地址**（与汇川导出文件里的 <c>UB 2100070</c> / <c>UW 21001C0</c> 同口径）；
    /// X/Y 是八进制；其余十进制。</para>
    /// </summary>
    public enum EasyNetArea
    {
        [Display(Name = "D 数据寄存器"), Description("D 数据寄存器（十进制字地址）")]
        D,

        [Display(Name = "R 文件寄存器"), Description("R 文件寄存器（十进制字地址）")]
        R,

        [Display(Name = "W 链接寄存器"), Description("W 链接寄存器（十进制字地址）")]
        W,

        [Display(Name = "M 内部继电器"), Description("M 内部继电器（十进制位地址）")]
        M,

        [Display(Name = "S 特殊继电器"), Description("S 特殊继电器（十进制位地址）")]
        S,

        [Display(Name = "B 特殊继电器"), Description("B 特殊继电器（十进制位地址）")]
        B,

        [Display(Name = "X 输入继电器（八进制）"), Description("X 输入继电器（八进制位地址）")]
        X,

        [Display(Name = "Y 输出继电器（八进制）"), Description("Y 输出继电器（八进制位地址）")]
        Y,

        [Display(Name = "U 寄存器（十六进制位地址）"), Description("U/UB/UW 寄存器（十六进制位地址，32 位裸寻址）")]
        U,
    }

    /// <summary>
    /// <para>EasyNet 地址配置。</para>
    /// <para><b>Offset 的单位随区</b>：D/R/W 填字号（D100 = 100）；M/S/B/X/Y 填位号；U 填十六进制位地址
    /// （与汇川导出文件一致，如 <c>2100070</c>）。数值/字符串在 U 区从该位地址起按字展开，必须字对齐。</para>
    /// <para><b>批量读</b>：U 区字变量按 <c>位地址/16</c> 折算成字号参与段合并，段地址由
    /// <see cref="PollAddress.SegmentAddressOverride"/> 回写成十六进制（规划器直接用十进制拼
    /// <c>前缀+Start</c> 会把 0x21001C 写成 "21001C" 再被当成十六进制解析——那是 MC 当年踩过的二次错位）。
    /// 位变量按位合并，同样用覆盖串回写。</para>
    /// </summary>
    public class EasyNetAddress : DeviceAddressBase<EasyNetArea>
    {
        /// <summary>EasyNet 的字是 16 位，位偏移范围 0~15（D100.15）。</summary>
        public override int MaxBitOffset => 15;

        /// <summary>位区：地址本身就是位号，数值读写也按位展开（本类只把布尔放进批量）。</summary>
        public bool IsBitArea => Area is EasyNetArea.M or EasyNetArea.S or EasyNetArea.B
                                        or EasyNetArea.X or EasyNetArea.Y;

        /// <summary>当前区的编址进制（X/Y 八进制、U 十六进制、其余十进制）。</summary>
        public int Radix => Area switch
        {
            EasyNetArea.X or EasyNetArea.Y => 8,
            EasyNetArea.U => 16,
            _ => 10
        };

        protected override bool TryParseBaseStart(out int start)
        {
            start = 0;
            if (string.IsNullOrWhiteSpace(Offset)) return false;

            string text = Offset.Trim();

            if (Area == EasyNetArea.U)
            {
                if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    text = text.Substring(2);
                return int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out start)
                       && start >= 0;
            }

            if (Radix == 8)
            {
                try
                {
                    start = Convert.ToInt32(text, 8);
                    return start >= 0;
                }
                catch
                {
                    return false;
                }
            }

            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out start)
                   && start >= 0;
        }

        protected override string BuildAddress()
        {
            int start = EffectiveStart ?? 0;

            if (Area == EasyNetArea.U)
                return "U" + start.ToString("X", CultureInfo.InvariantCulture);

            string body = Format(start, Radix);

            if (IsBitArea)
                return $"{Area}{body}";

            // 字区：D/R/W，可带 .位（D100.5）
            return BitOffset >= 0 ? $"{Area}{body}.{BitOffset}" : $"{Area}{body}";
        }

        public override (bool IsValid, string ErrorMessage) Validate()
        {
            var baseResult = base.Validate();
            if (!baseResult.IsValid) return baseResult;

            if (Area == EasyNetArea.U)
            {
                if (DataType == DataValueType.Boolean)
                {
                    if (BitOffset >= 0)
                        return (false, "U 区布尔量本身就是位地址，不要再填位偏移");
                    return (true, string.Empty);
                }

                if (BitOffset >= 0)
                    return (false, "U 区数值不要填位偏移（位访问请把数据类型设为 Boolean）");

                int bitStart = EffectiveStart ?? 0;
                if ((bitStart & 0x0F) != 0)
                    return (false, $"U 区数值地址必须按 16 位（字）对齐，当前 0x{bitStart:X} 不是字对齐");

                return (true, string.Empty);
            }

            if (IsBitArea)
            {
                if (BitOffset >= 0)
                    return (false, $"{Area} 区地址直接写位号，不要再填位偏移");
                return (true, string.Empty);
            }

            // 字区 D/R/W
            if (DataType != DataValueType.Boolean && BitOffset >= 0)
                return (false, $"{Area} 区数值不要填位偏移（位访问请把数据类型设为 Boolean）");

            if (DataType == DataValueType.Boolean && BitOffset < 0)
                return (false, $"{Area} 区布尔量必须填位偏移（如 D100.0）");

            return (true, string.Empty);
        }

        public override bool TryCreatePollAddress(out PollAddress? poll)
        {
            poll = null;
            if (EffectiveStart is not int start || start < 0) return false;

            if (Area == EasyNetArea.U)
            {
                if (DataType == DataValueType.Boolean)
                {
                    int bitCount = Math.Max(1, Length);
                    poll = new PollAddress
                    {
                        Protocol = PollProtocol.EasyNet,
                        // 位与字分两个组：位段走 ReadBits/布尔解码，字段走 ReadSegment/字节解码，
                        // 两条解码路径不能混在一个段里（混了会把字当布尔逐位展开）。
                        GroupKey = "U|B",
                        SegmentPrefix = "U",
                        SegmentAddressOverride = "U" + start.ToString("X", CultureInfo.InvariantCulture),
                        Start = start,
                        SpanUnits = bitCount,
                        IsBitArea = true,
                        IsBitAccess = true,
                        BitOffset = -1
                    };
                    return true;
                }

                // 数值/字符串：U 区结构体按 16 位对齐；不对齐就退回单读，不猜
                if ((start & 0x0F) != 0) return false;

                int words = Math.Max(1, (TotalBytes + 1) / 2);
                poll = new PollAddress
                {
                    Protocol = PollProtocol.EasyNet,
                    GroupKey = "U|W",
                    SegmentPrefix = "U",
                    SegmentAddressOverride = "U" + start.ToString("X", CultureInfo.InvariantCulture),
                    Start = start / 16,
                    SpanUnits = words,
                    IsBitArea = false,
                    IsBitAccess = false,
                    BitOffset = -1
                };
                return true;
            }

            if (IsBitArea)
            {
                // 位区的数值读（M0 当字）协议支持，但批量规划器的位路径只解布尔；数值走单读
                if (DataType != DataValueType.Boolean) return false;

                int bitCount = Math.Max(1, Length);
                poll = new PollAddress
                {
                    Protocol = PollProtocol.EasyNet,
                    GroupKey = Area.ToString(),
                    SegmentPrefix = Area.ToString(),
                    // X/Y 是八进制：前缀+十进制 Start 拼回去会二次错位，用覆盖串按区内进制回写
                    SegmentAddressOverride = Area + Format(start, Radix),
                    Start = start,
                    SpanUnits = bitCount,
                    IsBitArea = true,
                    IsBitAccess = true,
                    BitOffset = -1
                };
                return true;
            }

            // 字区 D/R/W 的布尔（D100.5）：作为"1 个字的段成员"进段，跟着所属字一起读回。
            // <para>EasyNet 是位寻址协议，读一个字的 16 个位是免费的；旧实现把它一律排除到兜底单读，
            // 结果是每个字区位点每轮各烧一次往返（2026-10-29 修正）。</para>
            // <para><b>三条坐标前提缺一不可</b>：① Start 用<b>字号</b>（不是线上位地址 1605）；
            // ② SpanUnits = 1 字；③ SegmentAddressOverride 必须留 null——MergeSegments 的
            // <c>ThenBy(EndUnit)</c> 会让 SpanUnits=1 的位变量排成段首，覆盖串一旦带上 <c>.bit</c>，
            // 整段都从位地址起读，段内所有变量一起错且无异常（静默错值）。</para>
            if (DataType == DataValueType.Boolean)
            {
                poll = new PollAddress
                {
                    Protocol = PollProtocol.EasyNet,
                    GroupKey = Area.ToString(),
                    SegmentPrefix = Area.ToString(),
                    Start = start,
                    SpanUnits = 1,
                    IsBitArea = false,
                    IsBitAccess = true,
                    BitOffset = BitOffset
                };
                return true;
            }

            int wordUnits = Math.Max(1, (TotalBytes + 1) / 2);
            poll = new PollAddress
            {
                Protocol = PollProtocol.EasyNet,
                GroupKey = Area.ToString(),
                SegmentPrefix = Area.ToString(),
                Start = start,
                SpanUnits = wordUnits,
                IsBitArea = false,
                IsBitAccess = false,
                BitOffset = -1
            };
            return true;
        }

        public override bool TryCreateElementAddress(
            int index, out DeviceAddressBase? elementAddress, out string? error)
        {
            elementAddress = null;
            error = null;

            if (index < 0)
            {
                error = "数组下标不能为负数";
                return false;
            }

            if (index >= Length)
            {
                error = $"下标 {index} 超出数组长度 {Length}";
                return false;
            }

            int start = EffectiveStart ?? 0;
            EasyNetAddress element;

            if (Area == EasyNetArea.U)
            {
                if (DataType == DataValueType.Boolean)
                {
                    element = new EasyNetAddress
                    {
                        Area = Area,
                        Offset = (start + index).ToString("X", CultureInfo.InvariantCulture),
                        DataType = DataValueType.Boolean,
                        BitOffset = -1,
                        Length = 1
                    };
                }
                else if (DataType == DataValueType.String && ElementByteLength > 0)
                {
                    int bitsPerElement = ElementByteLength * 8;
                    element = new EasyNetAddress
                    {
                        Area = Area,
                        Offset = (start + index * bitsPerElement).ToString("X", CultureInfo.InvariantCulture),
                        DataType = DataValueType.ByteArray,
                        BitOffset = -1,
                        Length = ElementByteLength
                    };
                }
                else if (DataType == DataValueType.String)
                {
                    error = "字符串数组未配置每元素字节数，无法按元素写入";
                    return false;
                }
                else
                {
                    int bitsPerElement = Math.Max(1, TypeSize) * 8;
                    element = new EasyNetAddress
                    {
                        Area = Area,
                        Offset = (start + index * bitsPerElement).ToString("X", CultureInfo.InvariantCulture),
                        DataType = DataType,
                        BitOffset = -1,
                        Length = 1
                    };
                }
            }
            else if (IsBitArea)
            {
                element = new EasyNetAddress
                {
                    Area = Area,
                    Offset = Format(start + index, Radix),
                    DataType = DataValueType.Boolean,
                    BitOffset = -1,
                    Length = 1
                };
            }
            else
            {
                if (DataType == DataValueType.ByteArray)
                {
                    error = "EasyNet 字区按 16 位字寻址，byte[] 不支持按字节写入（会覆盖同字另一半）";
                    return false;
                }

                if (DataType == DataValueType.Boolean)
                {
                    int bit = (BitOffset < 0 ? 0 : BitOffset) + index;
                    element = new EasyNetAddress
                    {
                        Area = Area,
                        Offset = (start + bit / 16).ToString(CultureInfo.InvariantCulture),
                        DataType = DataValueType.Boolean,
                        BitOffset = bit % 16,
                        Length = 1
                    };
                }
                else if (DataType == DataValueType.String && ElementByteLength > 0)
                {
                    if (ElementByteLength % 2 != 0)
                    {
                        error = $"字符串元素宽度 {ElementByteLength} 字节不是 2 的倍数，无法按字对齐写入";
                        return false;
                    }

                    element = new EasyNetAddress
                    {
                        Area = Area,
                        Offset = (start + index * (ElementByteLength / 2)).ToString(CultureInfo.InvariantCulture),
                        DataType = DataValueType.ByteArray,
                        BitOffset = -1,
                        Length = ElementByteLength
                    };
                }
                else if (DataType == DataValueType.String)
                {
                    error = "字符串数组未配置每元素字节数，无法按元素写入";
                    return false;
                }
                else
                {
                    if (TypeSize < 2)
                    {
                        error = $"EasyNet 字区按 16 位字寻址，{DataType}（{TypeSize} 字节）不支持按元素写入";
                        return false;
                    }

                    element = new EasyNetAddress
                    {
                        Area = Area,
                        Offset = (start + index * (TypeSize / 2)).ToString(CultureInfo.InvariantCulture),
                        DataType = DataType,
                        BitOffset = -1,
                        Length = 1
                    };
                }
            }

            element.EnableConversion = false;

            var validation = element.Validate();
            if (!validation.IsValid)
            {
                error = validation.ErrorMessage;
                return false;
            }

            elementAddress = element;
            return true;
        }

        private static string Format(int value, int radix) => radix switch
        {
            8 => Convert.ToString(value, 8),
            16 => value.ToString("X", CultureInfo.InvariantCulture),
            _ => value.ToString(CultureInfo.InvariantCulture)
        };
    }
}
