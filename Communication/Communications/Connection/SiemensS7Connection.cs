using HslCommunication;
using HslCommunication.Profinet.Siemens;
using System;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>西门子 S7 协议连接实现类。</para>
    /// <para>封装 HslCommunication 的 SiemensS7Net 设备，提供标准化的通信接口。</para>
    /// </summary>
    public class SiemensS7Connection : ICommunicationConnection, ILinkHealthProbe, IConnectionFailureDetail
    {
        private readonly SiemensS7Net _device;
        private readonly SiemensS7Config _config;
        private bool _isConnected;

        // 建连失败分类（IConnectionFailureDetail）：与 ModbusTcpConnection 同一套口径，Worker 线程写、任意线程读
        private volatile bool _refusedFailure;
        private volatile string? _lastConnectError;

        /// <summary>refused 分类探测的等待上限：回环/局域网拒绝是毫秒级；黑洞地址最多等这么久就放弃分类</summary>
        private const int RefusedProbeTimeoutMs = 500;

        /// <inheritdoc />
        public string ConnectionName { get; }

        /// <inheritdoc />
        public CommunicationType Type => CommunicationType.SiemensS7;

        /// <inheritdoc />
        public ConnectionConfigBase? Config { get; set; }

        /// <inheritdoc />
        public bool IsConnected => _isConnected;

        /// <inheritdoc />
        /// <remarks>
        /// <para>取 HSL 管道自身的链路错误判定 <c>CommunicationPipe.IsConnectError()</c>
        /// （与 ModbusTcpConnection.HasLinkFault 同一原理）：只反映"上一次传输 I/O 是否失败"，
        /// S7 地址越界之类的协议错误发生在帧收到之后，不会误触发。</para>
        /// </remarks>
        public bool HasLinkFault => _device.CommunicationPipe?.IsConnectError() ?? false;

        /// <inheritdoc />
        /// <remarks>S7 报文本身就是大端字节流，没有 Modbus 那种"多寄存器字序"概念，恒为 ABCD。</remarks>
        public ByteOrderFormat ByteOrder => ByteOrderFormat.ABCD;

        /// <inheritdoc />
        public SiemensS7Connection(SiemensS7Config config)
        {
            _config = config;
            Config = config;
            ConnectionName = $"{config.IpAddress}:{config.Port}({config.S7CpuType})";
            // 契约层自声明枚举按名映射到 Hsl 枚举（成员名一致），未知值回退 S1200
            var cpu = Enum.TryParse<SiemensPLCS>(config.S7CpuType.ToString(), out var parsed)
                ? parsed : SiemensPLCS.S1200;
            _device = new SiemensS7Net(cpu, config.IpAddress);
            _device.Port = config.Port;
            _device.Rack = config.Rack;
            _device.Slot = config.Slot;
            // 与 ModbusTcpConnection 同理：把配置的"连接超时"下发到 HSL 的 TCP 建连超时，
            // 否则界面上的 TimeoutMs 对 HSL 无效（默认 10000ms 才是实际生效值）。
            // 注：S7 建连 = TCP 建连（受 TimeoutMs 约束）+ COTP 握手读取（受下面的 ReadTimeoutMs 约束），
            // 所以真实建连上限 ≈ 两者之和，两个值都要下发才封得住。
            _device.ConnectTimeOut = config.TimeoutMs;
            _device.ReceiveTimeOut = config.ReadTimeoutMs;
            // KeepAlive 落地：与 ModbusTcpConnection 同一机制（HSL NetworkDoubleBase.SocketKeepAliveTime，
            // 建连成功那一刻由 HSL 对底层 socket 做 IOControl）。关闭时不设置，行为与落地前零变化。
            if (config.EnableKeepAlive && config.KeepAliveIntervalMs > 0)
                _device.SocketKeepAliveTime = config.KeepAliveIntervalMs;
        }

        /// <inheritdoc />
        public bool IsLastConnectFailureRefused => _refusedFailure;

        /// <inheritdoc />
        public string? LastConnectError => _lastConnectError;

        /// <inheritdoc />
        public bool Connect()
        {
            long start = Environment.TickCount64;
            var result = _device.ConnectServer();
            if (!result.IsSuccess)
            {
                _isConnected = false;
                ClassifyConnectFailure(result.Message, Environment.TickCount64 - start);
                return false;
            }

            _isConnected = true;
            _refusedFailure = false;
            _lastConnectError = null;
            return true;
        }

        /// <summary>
        /// 建连失败分类（与 ModbusTcpConnection.ClassifyConnectFailure 同一套规则）：
        /// ① 消息识别（主通道）：TCP 建连被拒（refused）与协议无关，OS 文本直接判；
        /// ② 快速失败探测（补充）：COTP 握手超时（≈ReceiveTimeOut）与网络建连超时（≈ConnectTimeOut）
        /// 都会耗满超时预算、不进探测分支，行为不变；只有「没耗满超时预算」的快速失败才探测。
        /// </summary>
        private void ClassifyConnectFailure(string message, long elapsedMs)
        {
            int timeoutMs = _config.TimeoutMs > 0 ? _config.TimeoutMs : 3000;
            bool refused = HslHelper.MessageIndicatesConnectionRefused(message)
                || (elapsedMs < Math.Min(timeoutMs - 100, 1500)
                    && HslHelper.ProbeConnectionRefused(_config.IpAddress, _config.Port, RefusedProbeTimeoutMs));

            _refusedFailure = refused;
            _lastConnectError = refused ? "[ConnectionRefused] " + message : message;
        }

        /// <inheritdoc />
        public void Disconnect()
        {
            _device.ConnectClose();
            _isConnected = false;
        }

        /// <inheritdoc />
        public bool TestConnection()
        {
            var result = _device.ConnectServer();
            _device.ConnectClose();
            return result.IsSuccess;
        }

        /// <inheritdoc />
        public T Read<T>(string address) where T : struct
        {
            if (!_isConnected) throw new InvalidOperationException("设备未连接");
            // S7 按字节粒度读取：bool/byte=1，short/ushort=2，int/uint/float=4，long/ulong/double=8。
            // ⚠ 不能用 Marshal.SizeOf<T>()：它对 bool 返回 4（Win32 BOOL 的尺寸），
            // 会让 bool 读请求从 1 字节变成 4 字节——多读相邻字节，靠近区末还会越界读失败。
            // 值本身常常"碰巧对"（ConvertTo<bool> 只取首字节），属藏得住的错。
            // MinByteCount 与读/写链路的字节表同源；未知类型（decimal/DateTime/自定义 struct）
            // 返回 0，此时回退 Marshal.SizeOf 保持旧行为（读回字节但解不出值，由 ConvertTo 返回 default）
            int byteCount = HslHelper.MinByteCount(typeof(T));
            if (byteCount == 0)
                byteCount = System.Runtime.InteropServices.Marshal.SizeOf<T>();

            var result = _device.Read(address, (ushort)byteCount);
            if (!result.IsSuccess) throw new InvalidOperationException(result.Message);
            return HslHelper.ConvertTo<T>(result.Content, ByteOrderFormat.ABCD);
        }

        /// <inheritdoc />
        public void Write(string address, object value)
        {
            if (!_isConnected) throw new InvalidOperationException("设备未连接");
            // 按值类型分发：位地址 bool→WriteBit 指令，字节地址 bool→1 字节 0/1，数值→强类型重载
            HslHelper.WriteTyped(_device, address, value, WriteProtocolFamily.SiemensS7);
        }

        /// <inheritdoc />
        public bool[] ReadBits(string address, ushort count)
        {
            // S7 没有位区批量读语义：位点随字节段（MB/DBB）批量读回后由规划器按位切片解码
            throw new NotSupportedException("S7 不支持位区批量读，请使用字节段批量读 + 位切片解码");
        }

        /// <inheritdoc />
        public byte[] ReadBytes(string address, ushort length)
        {
            if (!_isConnected) throw new InvalidOperationException("设备未连接");
            var result = _device.Read(address, length);
            if (!result.IsSuccess) throw new InvalidOperationException(result.Message);
            return result.Content;
        }

        /// <inheritdoc />
        public void WriteBytes(string address, byte[] data)
        {
            if (!_isConnected) throw new InvalidOperationException("设备未连接");
            var result = _device.Write(address, data);
            if (!result.IsSuccess) throw new InvalidOperationException(result.Message);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Disconnect();
            _device.Dispose();
        }

        /// <inheritdoc />
        public override string ToString() => $"SiemensS7[{ConnectionName}]";
    }
}