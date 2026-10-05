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
    public class ModbusTcpConnection : ICommunicationConnection, ILinkHealthProbe
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
        /// <remarks>
        /// <para>取 HSL 管道自身的链路错误判定 <c>CommunicationPipe.IsConnectError()</c>：
        /// 它是"上一次传输 I/O 是否失败"的状态（收发异常/超时时自增 <c>connectErrorCount</c>，
        /// 一次成功读又自动清零，socket 被关掉则直接判错），与异常类型、与报错语言都无关——
        /// 产品的连接实现把所有 HSL 错误一律包成 InvalidOperationException(result.Message)，
        /// 原始 SocketException 已丢失，靠异常类型或消息文本判定都不可靠。</para>
        /// <para>只表达"链路级"故障：Modbus 非法地址异常是在帧正常收到之后才由 UnpackResponseContent
        /// 解析出错误码的（此时管道读已成功、计数已清零），所以个别坏地址不会误判为链路断开——
        /// 这一点对本短路判断至关重要，否则一个坏地址会把整条好连接打进无限重连。</para>
        /// </remarks>
        public bool HasLinkFault => _device.CommunicationPipe?.IsConnectError() ?? false;

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