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
    public class ModbusTcpConnection : ICommunicationConnection, ILinkHealthProbe, IConnectionFailureDetail
    {
        private readonly ModbusTcpNet _device;
        private readonly ModbusTcpConfig _config;
        private bool _isConnected;

        // 建连失败分类（IConnectionFailureDetail）：Worker 线程写、任意线程读
        private volatile bool _refusedFailure;
        private volatile string? _lastConnectError;

        /// <summary>refused 分类探测的等待上限：回环/局域网拒绝是毫秒级；黑洞地址最多等这么久就放弃分类</summary>
        private const int RefusedProbeTimeoutMs = 500;

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
            // KeepAlive 落地（EthernetConfigBase.EnableKeepAlive / KeepAliveIntervalMs 此前是零消费的死配置）：
            // 下发到 HSL 的 SocketKeepAliveTime——HSL 在 ConnectServer 成功那一刻对底层 socket 做 IOControl
            // （NetworkDoubleBase.ConnectServer → SetKeepAlive(time, interval)，即"连接成功后才启用"）。
            // 关闭时不设置：保持 HSL 默认 -1（不启用），未启用时行为与配置落地前零变化。
            // 注：契约默认值就是 EnableKeepAlive=true / 30000ms（UI「开启 KeepAlive」默认勾选），
            // 故默认配置自此起真的启用 30s TCP keepalive（半开连接可被内核探活）。
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
        /// 建连失败分类：HSL 只给一句消息（含 OS 本地化文本），无法直接拿到 SocketError。两级判定：
        /// <para>① 消息识别（主通道，零成本）：OS 的 refused 文本（中文「积极拒绝」/英文「actively refused」）。
        /// 实测本机（装了本地代理/TUN）回环拒绝也被 OS 层延迟到 ~2s 才回 RST，任何探测方案都会把失败耗时翻倍，
        /// 故 refused 分类不走"先探测"，走消息识别。</para>
        /// <para>② 快速失败探测（补充，覆盖消息未覆盖的区域设置）：HSL 对快速失败的建连会内部重试一次
        /// （NetSupport.CreateSocketAndConnect），refused 场景总耗时约百毫秒级（正常网络）、远小于建连超时；
        /// 真正的网络超时会耗满 <c>ConnectTimeOut</c>。只在「没耗满超时预算」（留 100ms 调度余量，封顶 1.5s）时
        /// 做一次毫秒级 socket 探测拿确定的 SocketError——超时路径（探测会拖慢它）永远不进探测分支。</para>
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
            // 富地址 "x=2;" 为离散输入（FC2），其余位区地址按线圈（FC1）处理。
            // 必须按 x= 段解析而不是判前缀：带站号的地址是 "s=2;x=2;100"，前缀判断会把它当线圈。
            var result = ModbusRichAddress.IsDiscreteInput(address)
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