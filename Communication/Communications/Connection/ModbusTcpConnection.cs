using HslCommunication;
using HslCommunication.Core;
using HslCommunication.ModBus;
using System;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>Modbus TCP 协议连接实现类。</para>
    /// <para>封装 HslCommunication 的 ModbusTcpNet 设备，提供标准化的通信接口。</para>
    /// </summary>
    public class ModbusTcpConnection : ICommunicationConnection
    {
        private readonly ModbusTcpNet _device;
        private readonly ModbusTcpConfig _config;
        private bool _isConnected;

        /// <inheritdoc />
        public string ConnectionName { get; }

        /// <inheritdoc />
        public CommunicationType Type => CommunicationType.ModbusTcp;

        /// <inheritdoc />
        public ConnectionConfigBase? Config { get; set; }

        /// <inheritdoc />
        public bool IsConnected => _isConnected;

        /// <inheritdoc />
        public ByteOrderFormat ByteOrder => _config.ByteOrder;

        /// <inheritdoc />
        public ModbusTcpConnection(ModbusTcpConfig config)
        {
            _config = config;
            Config = config;
            ConnectionName = $"{config.IpAddress}:{config.Port}";
            
            _device = new ModbusTcpNet();
            _device.IpAddress = config.IpAddress;
            _device.Port = config.Port;
            // 字序必须下发到设备的 ByteTransform：HSL 的 ModbusTcpNet 默认是 DataFormat.CDAB，
            // 写链路（Write(addr,int/float/...) → ByteTransform.TransByte）与 HSL 强类型读都走它。
            // 不下发就会出现"读侧跟随配置、写侧永远 CDAB"的读写不对称：
            // 软件写 int 1221 再读回可能不是 1221（16 位因 CDAB 与 ABCD 退化等价而看不出来）。
            _device.ByteTransform = new RegularByteTransform(HslHelper.ToDataFormat(config.ByteOrder));
            // 把配置的"连接超时"真正下发到设备：HSL 的 ConnectTimeOut 默认是 10000ms，
            // 与界面上的 TimeoutMs（默认 3000ms）是两套数。不下发的话会出现两种错位：
            // ① 用户把超时调大（如慢速 VPN 想等 20 秒）→ socket 仍 10 秒就放弃；
            // ② 用户把超时调小（如想 500ms 快速失败）→ 上层 500ms 就报失败，但建连线程还占着 10 秒。
            _device.ConnectTimeOut = config.TimeoutMs;
            // 读超时是另一回事：它约束"每次读写帧等对端回包"的时长（HSL 默认 5000ms）。
            // 与连接超时分开配置，才能既让慢链路建连有耐心、又让掉线设备快速暴露故障。
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
            // Modbus 读取长度按寄存器粒度：bool/byte/short=1，int/uint/float=2，long/ulong/double=4
            var result = _device.Read(address, HslHelper.RegisterCount<T>());
            if (!result.IsSuccess) throw new InvalidOperationException(result.Message);
            return HslHelper.ConvertTo<T>(result.Content, _config.ByteOrder);
        }

        /// <inheritdoc />
        public void Write(string address, object value)
        {
            if (!_isConnected) throw new InvalidOperationException("设备未连接");
            // 按值类型分发 HSL 强类型重载：bool→FC5/15、short/ushort→FC6、多寄存器→FC16
            HslHelper.WriteTyped(_device, address, value, WriteProtocolFamily.Modbus);
        }

        /// <inheritdoc />
        public bool[] ReadBits(string address, ushort count)
        {
            if (!_isConnected) throw new InvalidOperationException("设备未连接");
            // 富地址 "x=2;" 为离散输入（FC2），其余位区地址按线圈（FC1）处理
            var result = address.StartsWith("x=2;", StringComparison.Ordinal)
                ? _device.ReadDiscrete(address, count)
                : _device.ReadCoil(address, count);
            if (!result.IsSuccess) throw new InvalidOperationException(result.Message);
            return result.Content;
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
        public override string ToString() => $"ModbusTcp[{ConnectionName}]";
    }
}