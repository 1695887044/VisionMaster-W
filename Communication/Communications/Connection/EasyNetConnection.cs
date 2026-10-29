using System;
using System.Globalization;
using System.Net.Sockets;
using System.Text;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>汇川 Easy 系列 PLC 专用 TCP 协议（EasyNet）连接实现。</para>
    /// <para><b>为什么不用 HslCommunication</b>：本仓内置的 HSL 是旧版（net48），只带
    /// <c>InovanceTcpNet</c>，没有 <c>InovanceEasyNet</c>；而 EasyNet 的报文（22 字节头 + 位寻址）
    /// 与 Modbus 完全不同，套不进去。故这里自持 TcpClient，报文编解码见 <see cref="EasyNetCodec"/>。</para>
    /// <para><b>字节序</b>：原版 <c>RegularByteTransform</c> 实测为小端（线路 <c>12 34</c> → 0x3412），
    /// 在本仓的解码模型里对应 <see cref="ByteOrderFormat.DCBA"/>。声明成 ABCD 会让所有 16 位以上数值颠倒。</para>
    /// <para><b>线程</b>：设备 I/O 由 <see cref="ConnectionWorker"/> 串行化到连接专属线程；
    /// 本类内部仍加锁，防止 <c>TestConnection</c> 等旁路调用与主链路并发操作同一 socket。</para>
    /// </summary>
    public sealed class EasyNetConnection : ICommunicationConnection, ISegmentReader,
        ILinkHealthProbe, IConnectionFailureDetail
    {
        private readonly EasyNetConfig _config;
        private readonly object _ioLock = new object();

        private TcpClient? _client;
        private NetworkStream? _stream;
        private bool _isConnected;

        // 链路健康探针：收发异常/超时置位，一次成功读写清零
        private volatile bool _lastIoFailed;

        // 建连失败分类（与其它 TCP 连接同一口径）
        private volatile bool _refusedFailure;
        private volatile string? _lastConnectError;

        public EasyNetConnection(EasyNetConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            Config = config;
            ConnectionName = $"{config.IpAddress}:{config.Port}(EasyNet)";
        }

        public string ConnectionName { get; }

        public CommunicationType Type => CommunicationType.EasyNet;

        public ConnectionConfigBase? Config { get; set; }

        public bool IsConnected => _isConnected;

        /// <summary>EasyNet 数据按小端传输；本仓语义下必须映射成 DCBA（全字节倒排）。</summary>
        public ByteOrderFormat ByteOrder => ByteOrderFormat.DCBA;

        /// <summary>上一次收发是否发生链路级 I/O 故障（不做 I/O、无副作用，供轮询短路用）。</summary>
        public bool HasLinkFault => _lastIoFailed;

        public bool IsLastConnectFailureRefused => _refusedFailure;

        public string? LastConnectError => _lastConnectError;

        #region 建连 / 断开

        public bool Connect()
        {
            // 与 SendAndReceive 共用同一把锁：Connect/Disconnect 也动 socket，不能让旁路调用并发切链
            lock (_ioLock) return ConnectCore();
        }

        private bool ConnectCore()
        {
            DisconnectCore();

            if (!IPAddressOrHost(_config.IpAddress, out string host))
            {
                _lastConnectError = $"IP 地址不合法：{_config.IpAddress}";
                return false;
            }

            var client = new TcpClient { NoDelay = true };
            int timeoutMs = Math.Max(100, _config.TimeoutMs);

            try
            {
                var ar = client.BeginConnect(host, _config.Port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(timeoutMs))
                {
                    client.Close();
                    _refusedFailure = false;
                    _lastConnectError = $"连接超时（{timeoutMs}ms）";
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
                _refusedFailure = false;
                _lastConnectError = ex.Message;
                return false;
            }

            _client = client;
            _stream = client.GetStream();
            _stream.ReadTimeout = Math.Max(100, _config.ReadTimeoutMs);
            _stream.WriteTimeout = Math.Max(100, _config.ReadTimeoutMs);

            // KeepAlive 落地（属性面板上两项对 EasyNet 也可见，不消费就是"改了不生效"）
            if (_config.EnableKeepAlive && _config.KeepAliveIntervalMs > 0)
                TryEnableKeepAlive(client, _config.KeepAliveIntervalMs);

            _isConnected = true;
            _lastIoFailed = false;
            _refusedFailure = false;
            _lastConnectError = null;
            return true;
        }

        public void Disconnect()
        {
            lock (_ioLock) DisconnectCore();
        }

        private void DisconnectCore()
        {
            try { _stream?.Dispose(); } catch { /* 已失效 */ }
            try { _client?.Close(); } catch { /* 已关闭 */ }
            _stream = null;
            _client = null;
            _isConnected = false;
        }

        /// <summary>TCP KeepAlive（Win32 keepalivetime/interval 都取配置值，与 EthernetConfigBase 注释同口径）。</summary>
        private static void TryEnableKeepAlive(TcpClient client, int intervalMs)
        {
            try
            {
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                var values = new byte[12];
                BitConverter.GetBytes(1u).CopyTo(values, 0);
                BitConverter.GetBytes((uint)intervalMs).CopyTo(values, 4);
                BitConverter.GetBytes((uint)intervalMs).CopyTo(values, 8);
                client.Client.IOControl(IOControlCode.KeepAliveValues, values, null);
            }
            catch
            {
                // 平台/权限不支持时保持系统默认；不影响连接与读写
            }
        }

        /// <summary>只探测、不影响当前连接（已连接时上层会跳过本方法）。</summary>
        public bool TestConnection()
        {
            if (!IPAddressOrHost(_config.IpAddress, out string host)) return false;

            try
            {
                using var probe = new TcpClient { NoDelay = true };
                var ar = probe.BeginConnect(host, _config.Port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(Math.Max(100, _config.TimeoutMs))) return false;
                probe.EndConnect(ar);
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region 读

        public T Read<T>(string address) where T : struct
        {
            var info = Parse(address);

            if (typeof(T) == typeof(bool))
                return (T)(object)ReadBitsCore(info, 1)[0];

            if (HslHelper.MinByteCount(typeof(T)) == 0)
                throw new NotSupportedException($"EasyNet 不支持读取类型 {typeof(T).Name}（只支持布尔与 1/2/4/8 字节数值）");

            int words = HslHelper.RegisterCount<T>();
            byte[] data = ReadWordsCore(info, words);
            return HslHelper.ConvertTo<T>(data, ByteOrderFormat.DCBA);
        }

        public bool[] ReadBits(string address, ushort count)
        {
            if (count == 0) return Array.Empty<bool>();
            return ReadBitsCore(Parse(address), count);
        }

        public byte[] ReadBytes(string address, ushort length)
        {
            if (length == 0) return Array.Empty<byte>();
            int words = (length + 1) / 2;
            byte[] data = ReadWordsCore(Parse(address), words);
            if (data.Length == length) return data;

            var trimmed = new byte[length];
            Array.Copy(data, trimmed, length);
            return trimmed;
        }

        /// <summary>批量规划器专用：<paramref name="units"/> 是字（EasyNet 字区地址单位）。</summary>
        public byte[] ReadSegment(string address, ushort units)
        {
            if (units == 0) return Array.Empty<byte>();
            return ReadWordsCore(Parse(address), units);
        }

        private byte[] ReadWordsCore(EasyNetAddressInfo info, int words)
        {
            var request = EasyNetCodec.BuildReadCommand(info, words * 16);
            byte[] response = SendAndReceive(request);

            if (EasyNetCodec.IsErrorResponse(response, out _, out string error))
                throw new InvalidOperationException(error);

            if (!EasyNetCodec.ValidateReadEcho(request, response, words * 2, out string echoError))
                throw new InvalidOperationException(echoError);

            var data = new byte[words * 2];
            Array.Copy(response, EasyNetCodec.HeaderLength, data, 0, data.Length);
            return data;
        }

        private bool[] ReadBitsCore(EasyNetAddressInfo info, int count)
        {
            var request = EasyNetCodec.BuildReadCommand(info, count);
            byte[] response = SendAndReceive(request);

            if (EasyNetCodec.IsErrorResponse(response, out _, out string error))
                throw new InvalidOperationException(error);

            int bytes = (count + 7) / 8;
            if (!EasyNetCodec.ValidateReadEcho(request, response, bytes, out string echoError))
                throw new InvalidOperationException(echoError);

            return EasyNetCodec.UnpackBits(response, EasyNetCodec.HeaderLength, count);
        }

        #endregion

        #region 写

        public void Write(string address, object value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var info = Parse(address);

            switch (value)
            {
                case bool b:
                    WriteBitsCore(info, new[] { b });
                    return;

                case bool[] bits:
                    WriteBitsCore(info, bits);
                    return;

                case byte[] bytes:
                    WriteWordsCore(info, bytes);
                    return;

                case string text:
                    // 原版在 WordLength=1 时把字符串补成偶数字节再下发（定长字符串区不会留下半个字）
                    WriteWordsCore(info, PadToEven(Encoding.ASCII.GetBytes(text ?? string.Empty)));
                    return;

                case Array array:
                    WriteWordsCore(info, EncodeArray(array));
                    return;

                default:
                    WriteWordsCore(info, EncodeScalar(value));
                    return;
            }
        }

        public void WriteBytes(string address, byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            WriteWordsCore(Parse(address), data);
        }

        private void WriteWordsCore(EasyNetAddressInfo info, byte[] data)
        {
            var request = EasyNetCodec.BuildWriteBytesCommand(info, data);
            byte[] response = SendAndReceive(request);

            if (EasyNetCodec.IsErrorResponse(response, out _, out string error))
                throw new InvalidOperationException(error);

            if (!EasyNetCodec.ValidateWriteAck(response, out string ackError))
                throw new InvalidOperationException(ackError);
        }

        private void WriteBitsCore(EasyNetAddressInfo info, bool[] bits)
        {
            var request = EasyNetCodec.BuildWriteBitsCommand(info, bits);
            byte[] response = SendAndReceive(request);

            if (EasyNetCodec.IsErrorResponse(response, out _, out string error))
                throw new InvalidOperationException(error);

            if (!EasyNetCodec.ValidateWriteAck(response, out string ackError))
                throw new InvalidOperationException(ackError);
        }

        /// <summary>
        /// 标量 → 线路字节（小端）。byte/sbyte 写 1 字节：EasyNet 位粒度写，改低字节不会碰同字高字节；
        /// 读侧 <c>Read&lt;byte&gt;</c> 取字内首字节，读写对称。
        /// </summary>
        private static byte[] EncodeScalar(object value) => value switch
        {
            byte b => new[] { b },
            sbyte sb => new[] { unchecked((byte)sb) },
            short s => BitConverter.GetBytes(s),
            ushort us => BitConverter.GetBytes(us),
            int i => BitConverter.GetBytes(i),
            uint ui => BitConverter.GetBytes(ui),
            long l => BitConverter.GetBytes(l),
            ulong ul => BitConverter.GetBytes(ul),
            float f => BitConverter.GetBytes(f),
            double d => BitConverter.GetBytes(d),
            _ => throw new NotSupportedException($"EasyNet 不支持写入类型 {value.GetType().Name}")
        };

        private static byte[] EncodeArray(Array array)
        {
            Type? elementType = array.GetType().GetElementType();
            if (elementType == null)
                throw new NotSupportedException("无法确定数组元素类型");

            int size = ElementSize(elementType);
            var data = new byte[array.Length * size];
            for (int i = 0; i < array.Length; i++)
            {
                object? item = array.GetValue(i);
                if (item == null)
                    throw new NotSupportedException($"EasyNet 数组第 {i} 项为 null");

                byte[] encoded = EncodeScalar(item);
                if (encoded.Length != size)
                    throw new NotSupportedException($"EasyNet 数组元素 {elementType.Name} 编码长度异常");

                Array.Copy(encoded, 0, data, i * size, size);
            }
            return data;
        }

        private static int ElementSize(Type type) => System.Type.GetTypeCode(type) switch
        {
            TypeCode.Byte or TypeCode.SByte => 1,
            TypeCode.Int16 or TypeCode.UInt16 => 2,
            TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Single => 4,
            TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Double => 8,
            _ => throw new NotSupportedException($"EasyNet 不支持数组元素类型 {type.Name}")
        };

        private static byte[] PadToEven(byte[] data)
        {
            if ((data.Length & 1) == 0) return data;

            var padded = new byte[data.Length + 1];
            Array.Copy(data, padded, data.Length);
            return padded;
        }

        #endregion

        #region 收发与校验

        private byte[] SendAndReceive(byte[] request)
        {
            lock (_ioLock)
            {
                EnsureConnected();

                try
                {
                    // 帧头 [0..1] 本身就是总长（UInt16 小端），整帧一次写出即与"先写长度前缀再写正文"字节完全一致。
                    // 分两次 Write 会被 Nagle + 延迟 ACK 咬（第二段要等第一段被 ACK，最多白加 40ms）——
                    // 现在靠 NoDelay=true 兜住了，但少一次系统调用、且不依赖 NoDelay 更稳。
                    _stream!.Write(request, 0, request.Length);
                    _stream.Flush();

                    byte[] response = ReceiveFrame();
                    _lastIoFailed = false;
                    return response;
                }
                catch (Exception ex)
                {
                    // 链路级失败：置探针标志并断开，让 Worker 立刻判死重连（半开链路下不必等满每个段的读超时）
                    _lastIoFailed = true;
                    try { DisconnectCore(); } catch { /* 断开失败不改变故障判定 */ }
                    throw new InvalidOperationException($"EasyNet 收发失败：{ex.Message}", ex);
                }
            }
        }

        private byte[] ReceiveFrame()
        {
            byte[] head = ReadExactly(2);
            int total = BitConverter.ToUInt16(head, 0);
            if (total < 9)
                throw new InvalidOperationException($"EasyNet 响应帧长度非法：{total}");

            byte[] rest = ReadExactly(total - 2);
            var frame = new byte[total];
            Array.Copy(head, frame, 2);
            Array.Copy(rest, 0, frame, 2, rest.Length);
            return frame;
        }

        private byte[] ReadExactly(int count)
        {
            var buffer = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = _stream!.Read(buffer, offset, count - offset);
                if (read <= 0) throw new System.IO.IOException("对端关闭了连接");
                offset += read;
            }
            return buffer;
        }

        private void EnsureConnected()
        {
            if (!_isConnected || _stream == null)
                throw new InvalidOperationException("设备未连接");
        }

        private static EasyNetAddressInfo Parse(string address)
        {
            if (!EasyNetCodec.TryParseAddress(address, out var info, out string error))
                throw new InvalidOperationException(error);
            return info;
        }

        private static bool IPAddressOrHost(string? text, out string host)
        {
            host = (text ?? string.Empty).Trim();
            return host.Length > 0;
        }

        #endregion

        public void Dispose()
        {
            Disconnect();
        }

        public override string ToString() => $"EasyNet[{ConnectionName}]";
    }
}
