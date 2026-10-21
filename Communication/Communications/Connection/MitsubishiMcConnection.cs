using System;
using System.Linq;
using HslCommunication;
using HslCommunication.Profinet.Keyence;
using HslCommunication.Profinet.Melsec;
using HslCommunication.Profinet.Panasonic;

namespace VisionMaster.Communications
{
    /// <summary>
    /// 三菱 MC（QnA 兼容 3E 帧）连接实现。
    ///
    /// 覆盖三家同族协议：
    ///   三菱（D100 / M100 / X0 / Y0）、基恩士（DM100 / R100 / MR100）、松下（DT100 / WR100）。
    /// 三家共用 3E 帧，差异只在地址解析，所以用 <see cref="McVendor"/> 选客户端类，
    /// 而不是为每家复制一份连接代码。
    ///
    /// 帧格式：
    ///   <see cref="McFrameFormat.Binary"/> → MelsecMcNet（二进制 3E）
    ///   <see cref="McFrameFormat.Ascii"/>  → MelsecMcAsciiNet（ASCII 3E）
    /// UDP 暂不支持：<see cref="MitsubishiMcConfig.Validate"/> 在保存前就会拦住，
    /// 这里再抛一次是为了防"绕过 Validate 直接建连接"的调用方。
    /// </summary>
    public sealed class MitsubishiMcConnection : ICommunicationConnection, ISegmentReader, ILinkHealthProbe, IConnectionFailureDetail
    {
        private readonly MitsubishiMcConfig _config;
        private readonly MelsecMcNet _device;
        private bool _isConnected;

        private volatile bool _refusedFailure;
        private volatile string? _lastConnectError;

        /// <summary>refused 分类探测的等待上限：回环/局域网拒绝是毫秒级；黑洞地址最多等这么久就放弃分类</summary>
        private const int RefusedProbeTimeoutMs = 500;

        public MitsubishiMcConnection(MitsubishiMcConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            Config = config;

            if (config.ProtocolType == McProtocolType.Udp)
                throw new NotSupportedException("三菱 MC 的 UDP 模式暂未实现（当前支持 TCP 二进制/ASCII）");
            if (config.Vendor != McVendor.Mitsubishi && config.FrameFormat == McFrameFormat.Ascii)
                throw new NotSupportedException("基恩士/松下的 MC 变体当前只支持二进制帧");

            _device = config.Vendor switch
            {
                McVendor.Keyence => new KeyenceMcNet(config.IpAddress, config.Port),
                McVendor.Panasonic => new PanasonicMcNet(config.IpAddress, config.Port),
                _ => config.FrameFormat == McFrameFormat.Ascii
                    ? new MelsecMcAsciiNet(config.IpAddress, config.Port)
                    : new MelsecMcNet(config.IpAddress, config.Port),
            };

            // 3E 帧的网络号/站号；PLCNumber 默认 0xFF（本站），由 NetworkStationNumber 表达站号
            _device.NetworkNumber = config.NetworkNumber;
            _device.NetworkStationNumber = config.StationNumber;

            // 与 Modbus/S7 同一口径：把配置的超时真正下发到 HSL，否则界面上的值只是摆设
            _device.ConnectTimeOut = config.TimeoutMs;
            _device.ReceiveTimeOut = config.ReadTimeoutMs;
            if (config.EnableKeepAlive && config.KeepAliveIntervalMs > 0)
                _device.SocketKeepAliveTime = config.KeepAliveIntervalMs;

            ConnectionName = $"{config.IpAddress}:{config.Port}({config.Vendor}/{config.FrameFormat})";
        }

        public string ConnectionName { get; }

        public CommunicationType Type => CommunicationType.MitsubishiMc;

        public ConnectionConfigBase? Config { get; set; }

        public bool IsConnected => _isConnected;

        /// <summary>
        /// MC 3E 二进制协议的字数据按小端字节序传输（实测写 0x1234 读回线序 34 12；
        /// 32 位写 0x12345678 线序 78 56 34 12）。故解码用 DCBA（全字节倒排）。
        /// </summary>
        public ByteOrderFormat ByteOrder => ByteOrderFormat.DCBA;

        public bool HasLinkFault => _device.CommunicationPipe?.IsConnectError() ?? false;

        public bool IsLastConnectFailureRefused => _refusedFailure;

        public string? LastConnectError => _lastConnectError;

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

        public void Disconnect()
        {
            _device.ConnectClose();
            _isConnected = false;
        }

        public bool TestConnection()
        {
            var result = _device.ConnectServer();
            _device.ConnectClose();
            return result.IsSuccess;
        }

        public T Read<T>(string address) where T : struct
        {
            if (!_isConnected) throw new InvalidOperationException("设备未连接");

            // 位地址走 ReadBool；字地址按寄存器数读（1/2/4 字），再按大端解码
            if (typeof(T) == typeof(bool))
            {
                var bit = _device.ReadBool(address);
                if (!bit.IsSuccess) throw new InvalidOperationException(bit.Message);
                return (T)(object)bit.Content;
            }

            var result = _device.Read(address, HslHelper.RegisterCount<T>());
            if (!result.IsSuccess) throw new InvalidOperationException(result.Message);
            return HslHelper.ConvertTo<T>(result.Content, ByteOrderFormat.DCBA);
        }

        public void Write(string address, object value)
        {
            if (!_isConnected) throw new InvalidOperationException("设备未连接");
            // 字编址：byte/sbyte 补成 16 位字；bool 走位地址；其余走 HSL 强类型重载
            HslHelper.WriteTyped(_device, address, value, WriteProtocolFamily.Word);
        }

        public bool[] ReadBits(string address, ushort count)
        {
            // 与 S7 同一口径：MC 没有"位区批量读"的通用语义；批量规划器对 MC 走单读兜底
            throw new NotSupportedException("MC 不支持位区批量读，请使用单点读或按字读取后按位切片");
        }

        public byte[] ReadBytes(string address, ushort length)
        {
            if (!_isConnected) throw new InvalidOperationException("设备未连接");
            // 字节语义：给 byte[] 变量的调用方用；长度是字节，换算成字
            ushort words = (ushort)((length + 1) / 2);
            var result = _device.Read(address, words);
            if (!result.IsSuccess) throw new InvalidOperationException(result.Message);
            return result.Content.Take(length).ToArray();
        }

        /// <summary>批量规划器专用：length 是"字"（MC 地址单位），不换算字节。</summary>
        public byte[] ReadSegment(string address, ushort units)
        {
            if (!_isConnected) throw new InvalidOperationException("设备未连接");
            var result = _device.Read(address, units);
            if (!result.IsSuccess) throw new InvalidOperationException(result.Message);
            return result.Content;
        }

        public void WriteBytes(string address, byte[] data)
        {
            if (!_isConnected) throw new InvalidOperationException("设备未连接");
            var result = _device.Write(address, data);
            if (!result.IsSuccess) throw new InvalidOperationException(result.Message);
        }

        /// <summary>
        /// 地址校验（配置面板/断言用）：按厂商解析规则试"字地址"和"位地址"两种形态。
        /// 解析器来自 HSL，与真实读写同源，避免自己写正则漏掉厂商差异。
        /// </summary>
        public static bool TryValidateAddress(string? address, McVendor vendor, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(address))
            {
                error = "地址不能为空";
                return false;
            }

            MelsecMcNet probe = vendor switch
            {
                McVendor.Keyence => new KeyenceMcNet(),
                McVendor.Panasonic => new PanasonicMcNet(),
                _ => new MelsecMcNet(),
            };

            var word = probe.McAnalysisAddress(address.Trim(), 1, isBit: false);
            if (word.IsSuccess) return true;

            var bit = probe.McAnalysisAddress(address.Trim(), 1, isBit: true);
            if (bit.IsSuccess) return true;

            error = $"地址「{address}」不符合 {vendor} 的 MC 地址格式：{word.Message}";
            return false;
        }

        /// <summary>
        /// 建连失败分类：与 Modbus/S7 同一套规则（消息识别为主、快速失败探测为辅），
        /// 让上层能区分"目标拒绝连接"与"网络超时"。
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

        public void Dispose()
        {
            Disconnect();
            _device.Dispose();
        }

        public override string ToString() => $"MitsubishiMc[{ConnectionName}]";
    }
}
