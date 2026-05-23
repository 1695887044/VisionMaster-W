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
            _device = new SiemensS7Net(config.S7CpuType, config.IpAddress);
            _device.Port = config.Port;
            _device.Rack = config.Rack;
            _device.Slot = config.Slot;
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
            var result = _device.Read(address, 1);
            if (!result.IsSuccess) throw new InvalidOperationException(result.Message);
            return HslHelper.ConvertTo<T>(result.Content);
        }

        /// <inheritdoc />
        public void Write(string address, object value)
        {
            if (!_isConnected) throw new InvalidOperationException("设备未连接");
            var bytes = HslHelper.GetValueArray(value);
            var result = _device.Write(address, bytes);
            if (!result.IsSuccess) throw new InvalidOperationException(result.Message);
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