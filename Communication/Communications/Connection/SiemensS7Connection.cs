using HslCommunication;
using HslCommunication.Profinet.Siemens;
using System;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>西门子 S7 协议连接实现类。</para>
    /// <para>封装 HslCommunication 的 SiemensS7Net 设备，提供标准化的通信接口。</para>
    /// </summary>
    public class SiemensS7Connection : ICommunicationConnection
    {
        private readonly SiemensS7Net _device;
        private bool _isConnected;

        /// <inheritdoc />
        public string ConnectionName { get; }

        /// <inheritdoc />
        public CommunicationType Type => CommunicationType.SiemensS7;

        /// <inheritdoc />
        public ConnectionConfigBase? Config { get; set; }

        /// <inheritdoc />
        public bool IsConnected => _isConnected;

        /// <inheritdoc />
        public SiemensS7Connection(SiemensS7Config config)
        {
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
        }

        /// <inheritdoc />
        public bool Connect()
        {
            var result = _device.ConnectServer();
            _isConnected = result.IsSuccess;
            return _isConnected;
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
            // S7 按字节粒度读取：bool/byte=1，short/ushort=2，int/uint/float=4，long/ulong/double=8
            var result = _device.Read(address, (ushort)System.Runtime.InteropServices.Marshal.SizeOf<T>());
            if (!result.IsSuccess) throw new InvalidOperationException(result.Message);
            return HslHelper.ConvertTo<T>(result.Content);
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