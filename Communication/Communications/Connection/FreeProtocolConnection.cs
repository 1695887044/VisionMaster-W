using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VisionMaster.Communications
{
    /// <summary>
    /// 自由协议（通用 TCP 文本命令）连接实现。
    ///
    /// 语义：
    ///   Read&lt;T&gt;(address)  → 发送 address 文本 + 帧结束符，读到结束符后按 T 解析；
    ///   Write(address, value) → address 含 {0} 时按模板格式化，否则"address + value"，
    ///                           发送后按 <see cref="FreeProtocolConfig.WriteExpectsResponse"/> 决定是否等响应。
    ///
    /// 它不是"万能协议"：没有校验、没有重发、没有二进制长度前缀。现场设备如果是
    /// 长度前缀/CRC 帧，应该用插件/脚本或后续的帧编辑器，而不是硬塞进这里。
    /// 这一层的价值是让"一行 ASCII 命令"的设备不必再写一个 C# 插件。
    /// </summary>
    public sealed class FreeProtocolConnection : ICommunicationConnection, ILinkHealthProbe, IConnectionFailureDetail
    {
        private readonly FreeProtocolConfig _config;
        private readonly object _ioLock = new object();
        private readonly byte[] _terminator;

        private TcpClient? _client;
        private NetworkStream? _stream;
        private bool _isConnected;
        private volatile bool _lastIoFailed;
        private volatile bool _refusedFailure;
        private volatile string? _lastConnectError;

        /// <summary>
        /// 字节间隙超时：对端两次发送之间最多等多久。
        /// <para>由协议专属的 <see cref="FreeProtocolConfig.ResponseTimeoutMs"/> 控制；它 ≤0 时回落到通用读超时。</para>
        /// </summary>
        private int GapTimeoutMs => Math.Max(100, _config.ResponseTimeoutMs > 0
            ? _config.ResponseTimeoutMs
            : _config.ReadTimeoutMs);

        /// <summary>
        /// 整帧总时限：从发出命令到收完一帧的上限，由通用「读超时」控制。
        /// <para><b>为什么两个都要</b>：只有间隙超时时，对端每 &lt;间隙 挤一个字节就能无限拖长一帧
        /// （实测症状是"连接看着活着、这一帧永远收不完"）。有了总时限，"改读超时"也才真正影响读——
        /// 旧实现里通用读超时被响应超时完全遮蔽，改它只改了写超时。</para>
        /// </summary>
        private int FrameDeadlineMs => Math.Max(100, _config.ReadTimeoutMs);

        public FreeProtocolConnection(FreeProtocolConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            Config = config;
            _terminator = DecodeTerminator(config.FrameTerminator, TextEncoding);
            ConnectionName = $"{config.IpAddress}:{config.Port}(FreeProtocol)";
        }

        public string ConnectionName { get; }

        public CommunicationType Type => CommunicationType.FreeProtocol;

        public ConnectionConfigBase? Config { get; set; }

        public bool IsConnected => _isConnected;

        /// <summary>文本协议没有多寄存器字序概念；保留契约值</summary>
        public ByteOrderFormat ByteOrder => ByteOrderFormat.ABCD;

        public bool HasLinkFault => _lastIoFailed;

        public bool IsLastConnectFailureRefused => _refusedFailure;

        public string? LastConnectError => _lastConnectError;

        private System.Text.Encoding TextEncoding =>
            _config.Encoding == FreeProtocolEncoding.Utf8
                ? System.Text.Encoding.UTF8
                : System.Text.Encoding.ASCII;

        public bool Connect()
        {
            Disconnect();

            if (!IPAddress.TryParse(_config.IpAddress, out var ip))
            {
                _lastConnectError = $"IP 地址不合法：{_config.IpAddress}";
                return false;
            }

            var client = new TcpClient { NoDelay = true };
            try
            {
                var ar = client.BeginConnect(ip, _config.Port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(Math.Max(100, _config.TimeoutMs)))
                {
                    client.Close();
                    _lastConnectError = $"连接超时（{_config.TimeoutMs}ms）";
                    return false;
                }
                client.EndConnect(ar);
            }
            catch (SocketException ex)
            {
                client.Close();
                _refusedFailure = ex.SocketErrorCode == SocketError.ConnectionRefused;
                _lastConnectError = (_refusedFailure ? "[ConnectionRefused] " : "") + ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                client.Close();
                _lastConnectError = ex.Message;
                return false;
            }

            _client = client;
            _stream = client.GetStream();
            // 套接字级超时只表达"字节间隙"：两次发送之间最多等多久。
            // 整帧总时限（通用「读超时」）由 ReadResponseBytes 的 deadline 控制——
            // 两个字段各管一件事，界面上两个框都真的有用。
            _stream.ReadTimeout = GapTimeoutMs;
            _stream.WriteTimeout = GapTimeoutMs;
            _isConnected = true;
            _refusedFailure = false;
            _lastConnectError = null;
            _lastIoFailed = false;
            return true;
        }

        public void Disconnect()
        {
            try { _stream?.Dispose(); } catch { }
            try { _client?.Close(); } catch { }
            _stream = null;
            _client = null;
            _isConnected = false;
        }

        public bool TestConnection()
        {
            if (!IPAddress.TryParse(_config.IpAddress, out var ip)) return false;
            try
            {
                using var client = new TcpClient();
                var ar = client.BeginConnect(ip, _config.Port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(Math.Max(100, _config.TimeoutMs))) return false;
                client.EndConnect(ar);
                return true;
            }
            catch { return false; }
        }

        public T Read<T>(string address) where T : struct
        {
            string text = SendAndReceive(address).Trim();
            try
            {
                if (typeof(T) == typeof(bool))
                {
                    if (text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                        text.Equals("on", StringComparison.OrdinalIgnoreCase))
                        return (T)(object)true;
                    if (text == "0" || text.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                        text.Equals("off", StringComparison.OrdinalIgnoreCase))
                        return (T)(object)false;
                    throw new FormatException("不是可识别的布尔值（1/0/true/false/on/off）");
                }

                if (typeof(T) == typeof(char))
                {
                    if (text.Length == 0) throw new FormatException("响应为空");
                    return (T)(object)text[0];
                }

                return (T)Convert.ChangeType(text, typeof(T), CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"自由协议响应「{text}」无法转换为 {typeof(T).Name}：{ex.Message}");
            }
        }

        public void Write(string address, object value)
        {
            string text = FormatValue(value);
            string command = address.Contains("{0}", StringComparison.Ordinal)
                ? string.Format(CultureInfo.InvariantCulture, address, text)
                : address + text;

            if (_config.WriteExpectsResponse)
                SendAndReceive(command);
            else
                SendOnly(command);
        }

        public bool[] ReadBits(string address, ushort count)
            => throw new NotSupportedException("自由协议没有位区语义；请按文本/字节读取后自行解析");

        public byte[] ReadBytes(string address, ushort length)
        {
            EnsureConnected();
            lock (_ioLock)
            {
                SendBytes(Concat(Encode(address), _terminator));
                var raw = ReadResponseBytes();
                return raw.Take(length).ToArray();
            }
        }

        public void WriteBytes(string address, byte[] data)
        {
            EnsureConnected();
            lock (_ioLock)
            {
                SendBytes(Concat(Encode(address), data ?? Array.Empty<byte>(), _terminator));
                if (_config.WriteExpectsResponse)
                    ReadResponseBytes();
            }
        }

        /// <summary>发送命令并读一条完整响应（含结束符剥离）。</summary>
        private string SendAndReceive(string command)
        {
            EnsureConnected();
            lock (_ioLock)
            {
                SendBytes(Concat(Encode(command), _terminator));
                return TextEncoding.GetString(ReadResponseBytes());
            }
        }

        private void SendOnly(string command)
        {
            EnsureConnected();
            lock (_ioLock) SendBytes(Concat(Encode(command), _terminator));
        }

        private void EnsureConnected()
        {
            if (!_isConnected || _stream == null)
                throw new InvalidOperationException("设备未连接");
        }

        private void SendBytes(byte[] payload)
        {
            try
            {
                _stream!.Write(payload, 0, payload.Length);
                _stream.Flush();
                _lastIoFailed = false;
            }
            catch (Exception ex)
            {
                _lastIoFailed = true;
                throw new InvalidOperationException($"自由协议发送失败：{ex.Message}", ex);
            }
        }

        /// <summary>
        /// 读到结束符（或超时/对端关闭）为止，返回去掉结束符的原始字节。
        /// 空结束符时，把"整帧超时"视为一帧结束（适合定长/对端主动断开的设备）。
        /// <para>两道闸：套接字级 = 字节间隙（<see cref="GapTimeoutMs"/>）；这里 = 整帧总时限
        /// （<see cref="FrameDeadlineMs"/>）。只有间隙闸时，对端每 &lt;间隙 挤一个字节就能无限拖长一帧。</para>
        /// </summary>
        private byte[] ReadResponseBytes()
        {
            var buffer = new List<byte>(256);
            var one = new byte[1];
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int deadlineMs = FrameDeadlineMs;

            while (true)
            {
                // 整帧闸：每收一个字节查一次，最多超出一个间隙的时长
                if (clock.ElapsedMilliseconds >= deadlineMs)
                {
                    _lastIoFailed = true;
                    if (_terminator.Length == 0 && buffer.Count > 0) break; // 空结束符：读到超时即一帧
                    throw new TimeoutException(
                        $"自由协议整帧超时（{deadlineMs}ms 内未收完，已收 {buffer.Count} 字节；字节间隙超时 {GapTimeoutMs}ms）");
                }

                int n;
                try
                {
                    n = _stream!.Read(one, 0, 1);
                }
                catch (IOException ex)
                {
                    _lastIoFailed = true;
                    if (_terminator.Length == 0 && buffer.Count > 0) break;
                    throw new TimeoutException(
                        $"自由协议等待响应超时（间隙 {GapTimeoutMs}ms，整帧上限 {deadlineMs}ms）：{ex.Message}", ex);
                }

                if (n <= 0) break; // 对端关闭

                buffer.Add(one[0]);
                if (buffer.Count > _config.MaxResponseBytes)
                {
                    _lastIoFailed = true;
                    throw new InvalidOperationException(
                        $"自由协议响应超过 {_config.MaxResponseBytes} 字节上限（检查帧结束符是否匹配）");
                }

                if (_terminator.Length > 0 && EndsWith(buffer, _terminator))
                {
                    buffer.RemoveRange(buffer.Count - _terminator.Length, _terminator.Length);
                    break;
                }
            }

            _lastIoFailed = false;
            return buffer.ToArray();
        }

        private static bool EndsWith(List<byte> buffer, byte[] suffix)
        {
            if (buffer.Count < suffix.Length) return false;
            int offset = buffer.Count - suffix.Length;
            for (int i = 0; i < suffix.Length; i++)
                if (buffer[offset + i] != suffix[i]) return false;
            return true;
        }

        private byte[] Encode(string text) => TextEncoding.GetBytes(text ?? string.Empty);

        private static string FormatValue(object? value) => value switch
        {
            null => string.Empty,
            bool b => b ? "1" : "0",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

        private static byte[] Concat(params byte[][] parts)
        {
            int total = parts.Sum(p => p?.Length ?? 0);
            var result = new byte[total];
            int offset = 0;
            foreach (var part in parts)
            {
                if (part == null || part.Length == 0) continue;
                Buffer.BlockCopy(part, 0, result, offset, part.Length);
                offset += part.Length;
            }
            return result;
        }

        /// <summary>
        /// 结束符转义解析：\r \n \t \0 \\ \xNN；其余字符按当前编码写。
        /// 用 List&lt;byte&gt; 而不是先拼字符串，是为了让 \xNN 能表达任意字节。
        /// </summary>
        private static byte[] DecodeTerminator(string? raw, System.Text.Encoding encoding)
        {
            if (string.IsNullOrEmpty(raw)) return Array.Empty<byte>();

            var bytes = new List<byte>();
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c != '\\' || i + 1 >= raw.Length)
                {
                    bytes.AddRange(encoding.GetBytes(new[] { c }));
                    continue;
                }

                char next = raw[++i];
                switch (next)
                {
                    case 'r': bytes.Add(0x0D); break;
                    case 'n': bytes.Add(0x0A); break;
                    case 't': bytes.Add(0x09); break;
                    case '0': bytes.Add(0x00); break;
                    case '\\': bytes.Add(0x5C); break;
                    case 'x':
                    case 'X':
                        if (i + 2 < raw.Length &&
                            byte.TryParse(raw.Substring(i + 1, 2), NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture, out byte hex))
                        {
                            bytes.Add(hex);
                            i += 2;
                        }
                        else
                        {
                            bytes.AddRange(encoding.GetBytes(new[] { next }));
                        }
                        break;
                    default:
                        bytes.AddRange(encoding.GetBytes(new[] { next }));
                        break;
                }
            }
            return bytes.ToArray();
        }

        public void Dispose()
        {
            Disconnect();
        }

        public override string ToString() => $"FreeProtocol[{ConnectionName}]";
    }
}
