using System;
using System.Globalization;

namespace VisionMaster.Communications
{
    /// <summary>EasyNet 地址家族：位区 / 字区 / U 系列（32 位裸地址）。</summary>
    public enum EasyNetAddressKind
    {
        /// <summary>位区（X/Y/M/S/B）：地址本身就是位号。</summary>
        BitArea,

        /// <summary>字区（D/R/W）：地址是字号，线上折算成 字号×16 + 位偏移。</summary>
        WordArea,

        /// <summary>U 系列（U/UB/UW）：地址是十六进制位地址，4 字节裸写、不做类型码合并。</summary>
        U
    }

    /// <summary>地址解析结果（报文 [14..17] 与合并用的位地址）。</summary>
    public sealed class EasyNetAddressInfo
    {
        /// <summary>写入报文 [14..17] 的 4 字节地址。</summary>
        public byte[] AddressBytes { get; init; } = new byte[4];

        /// <summary>线上位地址（Y/S 已加 0x80000 基址；U 为原始 32 位值）。</summary>
        public int WireBitAddress { get; init; }

        /// <summary>区内的位地址（Y/S 未加基址、D/R/W 已折算成 字号×16+位、U 原样）。</summary>
        public int LocalBitAddress { get; init; }

        /// <summary>地址家族。</summary>
        public EasyNetAddressKind Kind { get; init; }

        /// <summary>区名（X/Y/M/S/B/D/R/W/U）。</summary>
        public string Area { get; init; } = string.Empty;
    }

    /// <summary>
    /// <para>EasyNet 报文编解码（汇川 Easy 系列专用 TCP 协议）。</para>
    /// <para>与其它协议不同，本仓内置的 HslCommunication 是旧版、不含 <c>InovanceEasyNet</c>，
    /// 故报文层在这里自己实现；字段语义按原版 13.0.2 的 <c>InovanceEasyNet</c> 还原：</para>
    /// <para>· 定界：前 2 字节 = 整帧总长（UInt16 小端，含自身）；</para>
    /// <para>· 请求：22 字节固定头（[8]=1 读 / 2 写，[14..17] 地址，[18..20] 位数 LE）；</para>
    /// <para>· 读响应：整帧回显请求 + 数据（[16] 的类型码高 4 位被清），数据从 [22] 起、按位 LSB-first；</para>
    /// <para>· 写响应：9 字节固定帧（[8]=0x06）；错误帧 [8]=0x0F、错误码在 [10..11] LE。</para>
    /// <para>地址编码：X/Y 八进制、Y 加 0x80000；M/S 十进制、S 加 0x80000；B/D/R/W 十进制
    /// （D/R/W 字号×16，可带 <c>.bit</c>）；U/UB/UW 十六进制 32 位裸地址。
    /// U 系列不做类型码合并是原版行为（只有位地址 20~23 位全 1 才会被 PLC 路由进 U 区），这里原样保留。</para>
    /// </summary>
    public static class EasyNetCodec
    {
        /// <summary>请求头长度（读请求固定 22 字节；写请求 = 22 + 载荷）。</summary>
        public const int HeaderLength = 22;

        /// <summary>协议默认端口（唯一真相源在 Core 的 <see cref="EasyNetConfig.DefaultPortNumber"/>）。</summary>
        public const int DefaultPort = EasyNetConfig.DefaultPortNumber;

        /// <summary>位区与字区的分界（Y = X + 0x80000，S = M + 0x80000）。</summary>
        private const int AreaSplit = 0x80000;

        /// <summary>非 U 地址的位地址上限（20 位；再大就会被类型码覆盖）。</summary>
        private const int MaxBitAddress = 0xFFFFF;

        private const byte TypeXy = 0x00;
        private const byte TypeMs = 0x10;
        private const byte TypeB = 0x20;
        private const byte TypeD = 0x40;
        private const byte TypeR = 0x50;
        private const byte TypeW = 0x60;
        private const byte TypeU = 0xF0;

        private static readonly byte[] ReadTemplate =
            Convert.FromHexString("16000103010000000100010000004000600010000000");

        private static readonly byte[] WriteTemplate =
            Convert.FromHexString("16000103010000000200010000002000600010000000");

        /// <summary>
        /// 解析地址字符串为报文地址。
        /// <para>支持 <c>U2100070</c> / <c>UB 2100070</c> / <c>UW 21001C0</c>（十六进制，允许前缀后空格与 0x）、
        /// <c>D100</c> / <c>D100.5</c> / <c>R10</c> / <c>W20</c>、<c>M0</c> / <c>S0</c> / <c>B0</c>、
        /// <c>X10</c> / <c>Y10</c>（八进制）。</para>
        /// </summary>
        public static bool TryParseAddress(string? address, out EasyNetAddressInfo info, out string error)
        {
            info = null!;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(address))
            {
                error = "地址不能为空";
                return false;
            }

            string s = address.Trim();
            try
            {
                // U 系列必须先判：UB/UW 也以 U 开头
                if (StartsWithNoCase(s, "UB")) return ParseU(s.Substring(2), out info, out error);
                if (StartsWithNoCase(s, "UW")) return ParseU(s.Substring(2), out info, out error);
                if (StartsWithNoCase(s, "U")) return ParseU(s.Substring(1), out info, out error);

                if (StartsWithAndValue(s, "W")) return ParseWord(s, 'W', TypeW, out info, out error);
                if (StartsWithAndValue(s, "D")) return ParseWord(s, 'D', TypeD, out info, out error);
                if (StartsWithAndValue(s, "R")) return ParseWord(s, 'R', TypeR, out info, out error);

                if (StartsWithAndValue(s, "X")) return ParseBit(s, 'X', TypeXy, 8, 0, out info, out error);
                if (StartsWithAndValue(s, "Y")) return ParseBit(s, 'Y', TypeXy, 8, AreaSplit, out info, out error);
                if (StartsWithAndValue(s, "M")) return ParseBit(s, 'M', TypeMs, 10, 0, out info, out error);
                if (StartsWithAndValue(s, "S")) return ParseBit(s, 'S', TypeMs, 10, AreaSplit, out info, out error);
                if (StartsWithAndValue(s, "B")) return ParseBit(s, 'B', TypeB, 10, 0, out info, out error);

                error = "不支持的 EasyNet 地址（支持 X/Y/M/S/B/D/R/W/U）";
                return false;
            }
            catch (Exception ex)
            {
                error = "地址解析失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>构建读命令（bits = 要读的位数；读字时由调用方传 words×16）。</summary>
        public static byte[] BuildReadCommand(EasyNetAddressInfo address, int bits)
        {
            if (bits <= 0 || bits > 0xFFFFFF)
                throw new ArgumentOutOfRangeException(nameof(bits), $"EasyNet 读位数必须在 1~{0xFFFFFF} 之间（实际 {bits}）");

            var command = (byte[])ReadTemplate.Clone();
            address.AddressBytes.CopyTo(command, 14);
            command[18] = (byte)bits;
            command[19] = (byte)(bits >> 8);
            command[20] = (byte)(bits >> 16);
            return command;
        }

        /// <summary>构建字写命令（位数 = 字节数×8，载荷按原始字节下发）。</summary>
        public static byte[] BuildWriteBytesCommand(EasyNetAddressInfo address, byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            var command = new byte[HeaderLength + data.Length];
            Array.Copy(WriteTemplate, command, HeaderLength);
            address.AddressBytes.CopyTo(command, 14);
            data.CopyTo(command, HeaderLength);

            int total = command.Length;
            command[0] = (byte)total;
            command[1] = (byte)(total >> 8);
            int bits = data.Length * 8;
            command[18] = (byte)bits;
            command[19] = (byte)(bits >> 8);
            command[20] = (byte)(bits >> 16);
            return command;
        }

        /// <summary>构建位写命令（位数 = bool 个数，载荷按 LSB-first 打包）。</summary>
        public static byte[] BuildWriteBitsCommand(EasyNetAddressInfo address, bool[] bits)
        {
            if (bits == null) throw new ArgumentNullException(nameof(bits));
            return BuildWriteBytesCommand(address, PackBits(bits), bits.Length);
        }

        private static byte[] BuildWriteBytesCommand(EasyNetAddressInfo address, byte[] payload, int bitCount)
        {
            var command = new byte[HeaderLength + payload.Length];
            Array.Copy(WriteTemplate, command, HeaderLength);
            address.AddressBytes.CopyTo(command, 14);
            payload.CopyTo(command, HeaderLength);

            int total = command.Length;
            command[0] = (byte)total;
            command[1] = (byte)(total >> 8);
            command[18] = (byte)bitCount;
            command[19] = (byte)(bitCount >> 8);
            command[20] = (byte)(bitCount >> 16);
            return command;
        }

        /// <summary>位数组 → LSB-first 字节（与原版 <c>ToByteArray</c> 同约定）。</summary>
        public static byte[] PackBits(bool[] values)
        {
            var data = new byte[(values.Length + 7) / 8];
            for (int i = 0; i < values.Length; i++)
                if (values[i]) data[i / 8] |= (byte)(1 << (i % 8));
            return data;
        }

        /// <summary>字节 → 位数组（只取前 <paramref name="count"/> 位）。数据不足直接抛，不静默补 false。</summary>
        public static bool[] UnpackBits(byte[] data, int offset, int count)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));

            int needed = (count + 7) / 8;
            if (offset < 0 || offset + needed > data.Length)
                throw new InvalidOperationException(
                    $"位数据不足：需要 {needed} 字节，实际可用 {Math.Max(0, data.Length - offset)} 字节");

            var bits = new bool[count];
            for (int i = 0; i < count; i++)
                bits[i] = (data[offset + i / 8] & (1 << (i % 8))) != 0;
            return bits;
        }

        /// <summary>
        /// 错误帧判定：<c>[8] == 0x0F</c> 是 PLC 错误帧，错误码取 <c>[10..11]</c> 小端
        /// （帧长 &lt; 14 时按原版记 10000）。
        /// </summary>
        public static bool IsErrorResponse(byte[] response, out int errorCode, out string message)
        {
            errorCode = 0;
            message = string.Empty;
            if (response == null || response.Length < 9 || response[8] != 0x0F) return false;

            errorCode = response.Length >= 14 ? BitConverter.ToUInt16(response, 10) : 10000;
            message = $"PLC 返回错误码 {errorCode}（帧：{ToHex(response)}）";
            return true;
        }

        /// <summary>
        /// 读响应回显校验：命令字节必须是"读成功"形态、地址低 20 位必须与请求一致，数据长度不得少于期望。
        /// <para>为什么只比 [14][15] 与 [16] 低 4 位：PLC 回显时会清掉 [16] 的高 4 位（类型码），
        /// 而真机是否原样回显 [17]（U 地址的 bit24-31）与位数没有证据——比得太严会把
        /// "链路正常"误判成每段都失败。一问一答协议没有流水线，命令+地址+长度足够识别错位。</para>
        /// <para><b>为什么命令字节不比相等</b>：实测汇川 H5U 模拟器（h5u_daemon）对读请求（[8]=0x01）
        /// 回的是 <c>[8]=0x02</c>，而 HSL 参考服务器原样回显 0x01；HSL 原版客户端根本不校验这一字节。
        /// 按相等比会把真机上的每一次成功读都判成失败（表现为整轮段读全红 → 误判链路故障 → 界面一直"重连中"）。</para>
        /// <para>为什么必须校验：半包/串包/上一条的迟到响应一旦被当成这一条，
        /// 静默错值比报错难查得多。</para>
        /// </summary>
        public static bool ValidateReadEcho(
            byte[] request, byte[] response, int expectedDataBytes, out string error)
        {
            error = string.Empty;

            if (response == null || response.Length < HeaderLength)
            {
                error = $"读响应过短（{response?.Length ?? 0} 字节，至少需要 {HeaderLength} 字节）";
                return false;
            }

            // 命令字节只认"读成功"的两种形态：0x01（参考服务器原样回显）与 0x02（H5U 真机/模拟器）。
            // 错误帧 0x0F 已在上层 IsErrorResponse 拦掉，走到这里的 0x0F 会被判非法——与旧行为一致。
            if (response[8] is not (0x01 or 0x02))
            {
                error = $"读响应命令非法（请求 0x{request[8]:X2}，响应 0x{response[8]:X2}，只接受 0x01/0x02）";
                return false;
            }

            if (response[14] != request[14]
                || response[15] != request[15]
                || (response[16] & 0x0F) != (request[16] & 0x0F))
            {
                error = $"读响应地址与请求不一致（请求 {ToHex(request, 14, 3)}，响应 {ToHex(response, 14, 3)}）";
                return false;
            }

            int dataLength = response.Length - HeaderLength;
            if (dataLength < expectedDataBytes)
            {
                error = $"读响应数据 {dataLength} 字节，少于期望 {expectedDataBytes} 字节";
                return false;
            }

            return true;
        }

        /// <summary>写响应校验：9 字节固定帧且 <c>[8] == 0x06</c>。</summary>
        public static bool ValidateWriteAck(byte[] response, out string error)
        {
            error = string.Empty;
            if (response == null || response.Length < 9)
            {
                error = $"写响应过短（{response?.Length ?? 0} 字节）";
                return false;
            }
            if (response[8] != 0x06)
            {
                error = $"写响应未确认（[8]=0x{response[8]:X2}，期望 0x06；帧：{ToHex(response)}）";
                return false;
            }
            return true;
        }

        private static bool ParseU(string body, out EasyNetAddressInfo info, out string error)
        {
            info = null!;
            error = string.Empty;

            string text = body.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(2);

            if (!int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value) || value < 0)
            {
                error = $"U 区地址「{body.Trim()}」不是有效的十六进制位地址";
                return false;
            }

            var bytes = BitConverter.GetBytes(value);
            info = new EasyNetAddressInfo
            {
                AddressBytes = bytes,
                WireBitAddress = value,
                LocalBitAddress = value,
                Kind = EasyNetAddressKind.U,
                Area = "U"
            };
            return true;
        }

        private static bool ParseWord(
            string address, char area, byte typeNibble, out EasyNetAddressInfo info, out string error)
        {
            info = null!;
            error = string.Empty;

            string body = address.Substring(1);
            int dot = body.IndexOf('.');
            string wordText = dot >= 0 ? body.Substring(0, dot) : body;
            string bitText = dot >= 0 ? body.Substring(dot + 1) : string.Empty;

            if (!int.TryParse(wordText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int word) || word < 0)
            {
                error = $"{area} 区字号「{wordText}」不是有效的十进制非负整数";
                return false;
            }

            int bit = 0;
            if (dot >= 0)
            {
                if (!int.TryParse(bitText, NumberStyles.Integer, CultureInfo.InvariantCulture, out bit)
                    || bit is < 0 or > 15)
                {
                    error = $"{area} 区位偏移「{bitText}」必须在 0~15 之间";
                    return false;
                }
            }

            long bitAddress = (long)word * 16 + bit;
            if (bitAddress > MaxBitAddress)
            {
                error = $"{area} 区地址超出 20 位范围（字号×16 后必须 ≤ 0x{MaxBitAddress:X}）";
                return false;
            }

            return BuildStandard(area.ToString(), (int)bitAddress, (int)bitAddress, typeNibble,
                EasyNetAddressKind.WordArea, out info, out error);
        }

        private static bool ParseBit(
            string address, char area, byte typeNibble, int radix, int baseValue,
            out EasyNetAddressInfo info, out string error)
        {
            info = null!;
            error = string.Empty;

            string body = address.Substring(1).Trim();
            int local;
            if (radix == 8)
            {
                // 八进制必须走 Convert.ToInt32(text, 8)：int.TryParse 会按十进制解释 "10"
                try
                {
                    local = Convert.ToInt32(body, 8);
                }
                catch (Exception ex)
                {
                    error = $"{area} 区地址「{body}」不是有效的八进制非负整数：{ex.Message}";
                    return false;
                }
            }
            else if (!int.TryParse(body, NumberStyles.Integer, CultureInfo.InvariantCulture, out local))
            {
                error = $"{area} 区地址「{body}」不是有效的十进制非负整数";
                return false;
            }

            if (local < 0)
            {
                error = $"{area} 区地址「{body}」必须是非负整数";
                return false;
            }

            long wire = (long)local + baseValue;
            if (wire > MaxBitAddress)
            {
                error = $"{area} 区地址「{body}」超出 20 位位地址范围";
                return false;
            }

            return BuildStandard(area.ToString(), (int)wire, local, typeNibble,
                EasyNetAddressKind.BitArea, out info, out error);
        }

        private static bool BuildStandard(
            string area, int wireBitAddress, int localBitAddress, byte typeNibble,
            EasyNetAddressKind kind, out EasyNetAddressInfo info, out string error)
        {
            info = null!;
            error = string.Empty;

            var bytes = new byte[4];
            bytes[0] = (byte)wireBitAddress;
            bytes[1] = (byte)(wireBitAddress >> 8);
            bytes[2] = (byte)(((wireBitAddress >> 16) & 0x0F) | typeNibble);
            bytes[3] = 0;

            info = new EasyNetAddressInfo
            {
                AddressBytes = bytes,
                WireBitAddress = wireBitAddress,
                LocalBitAddress = localBitAddress,
                Kind = kind,
                Area = area
            };
            return true;
        }

        private static bool StartsWithAndValue(string address, string prefix)
            => address.StartsWith(prefix, StringComparison.Ordinal)
               && address.Length > prefix.Length
               && address.Substring(prefix.Length).Trim().Length > 0;

        private static bool StartsWithNoCase(string address, string prefix)
            => address.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

        private static string ToHex(byte[] data) => Convert.ToHexString(data);

        private static string ToHex(byte[] data, int offset, int count)
            => Convert.ToHexString(data, offset, count);
    }
}
