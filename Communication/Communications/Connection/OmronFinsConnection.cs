using System;
using System.Linq;
using HslCommunication;
using HslCommunication.Core.Address;
using HslCommunication.Profinet.Omron;

namespace VisionMaster.Communications
{
    /// <summary>
    /// 欧姆龙 FINS/TCP 连接实现。
    ///
    /// 地址族（由 HSL 的 OmronFinsAddress 解析）：
    ///   D100（DM 区）、CIO100、W100（Work）、H100（Hold）、A100（Auxiliary）；
    ///   位访问用 "D100.0" 这类形态（由 HSL 解析器决定，连接层不自己拆字符串）。
    ///
    /// 字节序：FINS 服务器/PLC 的多寄存器数值按 CDAB（字交换小端）解释，
    /// 与 <see cref="OmronFinsServer"/> 的 <c>DataFormat.CDAB</c> 同源。
    /// </summary>
    public sealed class OmronFinsConnection : ICommunicationConnection, ISegmentReader, ILinkHealthProbe, IConnectionFailureDetail
    {
        private readonly OmronFinsConfig _config;
        private readonly OmronFinsNet _device;
        private bool _isConnected;

        private volatile bool _refusedFailure;
        private volatile string? _lastConnectError;

        private const int RefusedProbeTimeoutMs = 500;

        public OmronFinsConnection(OmronFinsConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            Config = config;

            _device = new OmronFinsNet(config.IpAddress, config.Port)
            {
                // FINS 头字段：目标网络号 / 目标节点号 / 目标单元号
                DNA = config.NetworkNumber,
                DA1 = config.NodeNumber,
                DA2 = config.UnitNumber,
                // 上位机节点号由 PLC 在建连时协商；默认 1，多连接场景由 HSL 内部处理
                SA1 = 1,
                ConnectTimeOut = config.TimeoutMs,
                // 响应超时优先、0 = 跟随通用「读超时」（专属字段优先的口径，见 OmronFinsConfig）。
                // 旧默认值写死 5000 时这里永远走第一个分支，用户改「读超时」对 FINS 毫无作用。
                ReceiveTimeOut = Math.Max(100, config.ResponseTimeoutMs > 0
                    ? config.ResponseTimeoutMs
                    : config.ReadTimeoutMs),
            };

            if (config.EnableKeepAlive && config.KeepAliveIntervalMs > 0)
                _device.SocketKeepAliveTime = config.KeepAliveIntervalMs;

            ConnectionName = $"{config.IpAddress}:{config.Port}(FINS N{config.NetworkNumber}/Node{config.NodeNumber}/Unit{config.UnitNumber})";
        }

        public string ConnectionName { get; }

        public CommunicationType Type => CommunicationType.OmronFins;

        public ConnectionConfigBase? Config { get; set; }

        public bool IsConnected => _isConnected;

        /// <summary>FINS 多寄存器数值按 CDAB（字交换小端）解释，与 HSL 服务端默认一致</summary>
        public ByteOrderFormat ByteOrder => ByteOrderFormat.CDAB;

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

            if (typeof(T) == typeof(bool))
            {
                var bit = _device.ReadBool(address);
                if (!bit.IsSuccess) throw new InvalidOperationException(bit.Message);
                return (T)(object)bit.Content;
            }

            var result = _device.Read(address, HslHelper.RegisterCount<T>());
            if (!result.IsSuccess) throw new InvalidOperationException(result.Message);
            return HslHelper.ConvertTo<T>(result.Content, ByteOrderFormat.CDAB);
        }

        public void Write(string address, object value)
        {
            if (!_isConnected) throw new InvalidOperationException("设备未连接");
            HslHelper.WriteTyped(_device, address, value, WriteProtocolFamily.Word);
        }

        public bool[] ReadBits(string address, ushort count)
        {
            throw new NotSupportedException("FINS 不支持位区批量读，请使用单点读或按字读取后按位切片");
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

        /// <summary>批量规划器专用：length 是"字"（FINS 地址单位），不换算字节。</summary>
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

        /// <summary>地址校验：与真实读写共用 HSL 的 OmronFinsAddress 解析器。</summary>
        public static bool TryValidateAddress(string? address, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(address))
            {
                error = "地址不能为空";
                return false;
            }

            var parsed = OmronFinsAddress.ParseFrom(address.Trim());
            if (parsed.IsSuccess) return true;

            error = $"地址「{address}」不符合 FINS 地址格式：{parsed.Message}";
            return false;
        }

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

        public override string ToString() => $"OmronFins[{ConnectionName}]";
    }
}
