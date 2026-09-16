using System;
using HslCommunication.Core.Address;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>轮询地址解析器：把"变量"变成"结构化轮询地址 PollAddress"。</para>
    /// <para>优先走结构化配置（AddressConfig，零解析成本）；
    /// 旧方案/手工注册的变量没有配置对象时，按历史字符串约定回退解析（兼容不炸）。</para>
    /// </summary>
    public static class PollAddressResolver
    {
        /// <summary>
        /// 解析变量的轮询地址；返回 null 表示不可批量读（调用方回退单变量字符串读）。
        /// </summary>
        public static PollAddress? Resolve(CommunicationVariable variable)
        {
            if (variable == null) return null;

            // 首选：结构化配置直接生成
            if (variable.AddressConfig != null && variable.AddressConfig.TryCreatePollAddress(out var poll))
                return poll;

            // 回退：按旧字符串约定解析
            return ParseLegacy(variable.Address, variable.ValueType);
        }

        /// <summary>
        /// <para>解析历史地址字符串：</para>
        /// <para>S7 串（DB1.DBW10 / M10.2 / VB100…）交给 HSL 的 ParseFrom 验证并还原字节偏移；</para>
        /// <para>Modbus 兼容富地址 "x=3;100" 与旧 4xxxx 约定（40001→保持寄存器 0 基址 0）。</para>
        /// </summary>
        public static PollAddress? ParseLegacy(string? address, string? valueType = null)
        {
            if (string.IsNullOrWhiteSpace(address)) return null;
            var s = address.Trim();

            int elementSize = SizeOfTypeName(valueType);

            // ---- S7：能按西门子语法解析就按 S7 处理（对 Modbus 串必然解析失败，不会误判） ----
            var s7 = S7AddressData.ParseFrom(s);
            if (s7.IsSuccess)
                return FromS7Parsed(s7.Content, elementSize, HasExplicitBitSuffix(s));

            // ---- Modbus ----
            int fc = -1;
            int start = -1;

            if (s.IndexOf(';') >= 0)
            {
                // 富地址 "s=1;x=4;100"
                foreach (var part in s.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var p = part.Trim();
                    if (p.StartsWith("x=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!int.TryParse(p.Substring(2), out fc)) return null;
                    }
                    else if (p.StartsWith("s=", StringComparison.OrdinalIgnoreCase) ||
                             p.StartsWith("w=", StringComparison.OrdinalIgnoreCase))
                    {
                        // 站号/写功能码不参与分组（站号由连接配置决定）
                    }
                    else if (!int.TryParse(p, out start))
                    {
                        return null;
                    }
                }
                if (fc < 0) fc = 3; // 与 HSL 默认功能码一致
            }
            else
            {
                // 旧 5 位约定：去掉可能的 ".bit" 尾缀后按区域前缀换算 0 基址
                var head = s;
                int dot = head.IndexOf('.');
                if (dot > 0) head = head.Substring(0, dot);
                if (!int.TryParse(head, out int n) || n < 1) return null;

                switch (n)
                {
                    case >= 40001 and <= 49999: fc = 3; start = n - 40001; break; // 保持寄存器
                    case >= 30001 and <= 39999: fc = 4; start = n - 30001; break; // 输入寄存器
                    case >= 10001 and <= 19999: fc = 2; start = n - 10001; break; // 离散输入
                    default: fc = 1; start = n - 1; break;                        // 线圈（00001~09999）
                }
            }

            if (fc is < 1 or > 4 || start < 0) return null;

            bool bitArea = fc is 1 or 2;
            int units = bitArea ? 1 : Math.Max(1, (elementSize + 1) / 2);

            return new PollAddress
            {
                Protocol = PollProtocol.Modbus,
                GroupKey = $"FC{fc}",
                SegmentPrefix = $"x={fc};",
                Start = start,
                SpanUnits = units,
                IsBitArea = bitArea,
                IsBitAccess = bitArea,
                BitOffset = -1
            };
        }

        /// <summary>由 HSL 解析结果还原 S7 结构化地址（SM/AI/AQ/P/T/C 区不批量，返回 null）</summary>
        /// <param name="a">HSL 解析结果</param>
        /// <param name="elementSize">值类型的字节数（0 = 未知，按 1 处理）</param>
        /// <param name="explicitBitAddress">地址串是否带显式位后缀（M10.0 / DB1.DBX0.0）</param>
        private static PollAddress? FromS7Parsed(S7AddressData a, int elementSize, bool explicitBitAddress)
        {
            int byteAddr = a.AddressStart / 8;
            int bitRemainder = a.AddressStart % 8;

            string group, prefix;
            switch (a.DataCode)
            {
                case 132: group = $"DB{a.DbBlock}"; prefix = $"DB{a.DbBlock}.DBB"; break;
                case 131: group = "M"; prefix = "MB"; break;
                case 129: group = "I"; prefix = "IB"; break;
                case 130: group = "Q"; prefix = "QB"; break;
                default: return null; // SM/AI/AQ/P/T/C：保守起见不合并
            }

            // <para>位访问判定：显式 ".bit" 后缀，或字节内位偏移非 0（M10.3）。</para>
            // <para>修复点：只看"位偏移非 0"会把 M10.0 当成整字节读——8 个位里任一位为 1 就读出 true，
            // 值域被放大 8 倍；显式后缀是区分"M10.0（第 0 位）"与"MB10（整字节）"的唯一依据，
            // 两者在 HSL 解析结果里都是 AddressStart=80、位偏移 0，无法从事后结果反推。</para>
            bool bitAccess = explicitBitAddress || bitRemainder != 0;
            int units = bitAccess ? 1 : Math.Max(1, elementSize);

            return new PollAddress
            {
                Protocol = PollProtocol.S7,
                GroupKey = group,
                SegmentPrefix = prefix,
                Start = byteAddr,
                SpanUnits = units,
                IsBitArea = false,
                IsBitAccess = bitAccess,
                BitOffset = bitAccess ? bitRemainder : -1
            };
        }

        /// <summary>地址串是否以 ".0"~".7" 结尾（显式位地址，如 M10.0 / DB1.DBX0.0）</summary>
        private static bool HasExplicitBitSuffix(string address)
        {
            int dot = address.LastIndexOf('.');
            return dot >= 0
                && address.Length - dot == 2
                && address[dot + 1] is >= '0' and <= '7';
        }

        /// <summary>类型名 → 字节数（AssemblyQualifiedName 或短名均可）；未知返回 0</summary>
        private static int SizeOfTypeName(string? typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return 0;
            var name = typeName.Trim();
            int comma = name.IndexOf(',');
            if (comma > 0) name = name.Substring(0, comma);

            return name switch
            {
                "System.Boolean" or "System.SByte" or "System.Byte" => 1,
                "System.Int16" or "System.UInt16" => 2,
                "System.Int32" or "System.UInt32" or "System.Single" => 4,
                "System.Int64" or "System.UInt64" or "System.Double" => 8,
                _ => 0 // String/ByteArray/未知：长度不可靠，交给调用方处理
            };
        }
    }
}
