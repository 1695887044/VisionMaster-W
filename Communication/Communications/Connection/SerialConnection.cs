using HslCommunication;
using HslCommunication.ModBus;
using System;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>串口 Modbus RTU 协议连接实现类。</para>
    /// <para>封装 HslCommunication 的 ModbusRtu 设备，提供标准化的通信接口。</para>
    /// </summary>
    public class SerialConnection : ICommunicationConnection
    {
        private readonly ModbusRtu _device;
        private bool _isConnected;

        /// <inheritdoc />
        public string ConnectionName { get; }

        /// <inheritdoc />
        public CommunicationType Type => CommunicationType.ModbusRtu;

        /// <inheritdoc />
        public ConnectionConfigBase? Config { get; set; }

        /// <inheritdoc />
        public bool IsConnected => _isConnected;

        /// <inheritdoc />
        public SerialConnection(SerialConfig config)
        {
            Config = config;
            ConnectionName = $"{config.PortName}@{config.BaudRate}";
            
            _device = new ModbusRtu();
            _device.SerialPortInni(
                config.PortName, 
                config.BaudRate, 
                config.DataBits, 
                GetStopBits(config.StopBits), 
                GetParity(config.Parity));
        }

        private static System.IO.Ports.StopBits GetStopBits(StopBitsMode mode) => mode switch
        {
            StopBitsMode.One => System.IO.Ports.StopBits.One,
            StopBitsMode.OnePointFive => System.IO.Ports.StopBits.OnePointFive,
            StopBitsMode.Two => System.IO.Ports.StopBits.Two,
            _ => System.IO.Ports.StopBits.One
        };

        private static System.IO.Ports.Parity GetParity(ParityMode mode) => mode switch
        {
            ParityMode.None => System.IO.Ports.Parity.None,
            ParityMode.Odd => System.IO.Ports.Parity.Odd,
            ParityMode.Even => System.IO.Ports.Parity.Even,
            _ => System.IO.Ports.Parity.None
        };

        /// <inheritdoc />
        public bool Connect()
        {
            var result = _device.Open();
            _isConnected = result.IsSuccess;
            return _isConnected;
        }

        /// <inheritdoc />
        public void Disconnect()
        {
            _device.Close();
            _isConnected = false;
        }

        /// <inheritdoc />
        public bool TestConnection()
        {
            var result = _device.Open();
            _device.Close();
            return result.IsSuccess;
        }

        /// <inheritdoc />
        public T Read<T>(string address) where T : struct
        {
            if (!_isConnected) throw new InvalidOperationException("设备未连接");
            // 修复点：旧实现固定读 1 个寄存器，int/float 只拿到 2 字节、long/double 拿到 2 字节，
            // 高半部分永远丢失——与 ModbusTcp 共用寄存器粒度计算。
            var result = _device.Read(address, HslHelper.RegisterCount<T>());
            if (!result.IsSuccess) throw new InvalidOperationException(result.Message);
            return HslHelper.ConvertTo<T>(result.Content);
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
        public override string ToString() => $"Serial[{ConnectionName}]";
    }
}