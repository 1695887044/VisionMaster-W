using HslCommunication;
using HslCommunication.ModBus;
using HslCommunication.Profinet.Siemens;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UI.Attributes;

namespace VisionMaster.Communications
{
    public enum CommunicationType
    {
        ModbusTcp,
        ModbusRtu,
        SiemensS7,
        FreeProtocol
    }

    public enum VariableAccessMode
    {
        ReadOnly,
        ReadWrite
    }

    [Serializable]
    public class CommunicationConfig
    {
        [SuperDisplay(Name = "连接名称" )]
        public string ConnectionName { get; set; } = new Guid().ToString();
        [SuperDisplay(Name = "通信类型")]
        public CommunicationType Type { get; set; } = CommunicationType.ModbusTcp;
        [SuperDisplay(Name = "IP地址")]   
        public string IpAddress { get; set; } = "127.0.0.1";
        [SuperDisplay(Name = "端口号")]
        public int Port { get; set; } = 502;
        [SuperDisplay(Name = "超时时间")]
        public int ConnectionTimeout { get; set; } = 3000;
        public byte Station { get; set; } = 1;
        public string SerialPort { get; set; } = "COM1";
        public int BaudRate { get; set; } = 9600;
        [SuperDisplay(Name = "西门子类型")]
        public SiemensPLCS S7CpuType { get; set; } = SiemensPLCS.S1200;
        [SuperDisplay(Name = "读取周期")]
        public int ReadCycleMs { get; set; } = 1000;
        public bool IsEnabled { get; set; } = true;
        public bool IsVisible { get; set; } = true;
    }

    public interface ICommunicationConnection : IDisposable
    {
        string ConnectionName { get; }
        CommunicationType Type { get; }
        bool IsConnected { get; }
        bool Connect();
        void Disconnect();
        T? Read<T>(string address);
        void Write(string address, object value);
        byte[] ReadBytes(string address, ushort length);
        void WriteBytes(string address, byte[] data);
    }

    public class ModbusTcpConnection : ICommunicationConnection
    {
        private ModbusTcpNet? _device;
        private bool _isConnected = false;
        
        public string ConnectionName { get; private set; }
        public CommunicationType Type => CommunicationType.ModbusTcp;
        public bool IsConnected => _isConnected;

        public ModbusTcpConnection(CommunicationConfig config)
        {
            ConnectionName = config.ConnectionName;
            _device = new ModbusTcpNet(config.IpAddress, config.Port, config.Station)
            {
                ConnectTimeOut = config.ConnectionTimeout,
                AddressStartWithZero = true
            };
        }

        public bool Connect()
        {
            if (_device == null) return false;
            var result = _device.ConnectServer();
            _isConnected = result.IsSuccess;
            return result.IsSuccess;
        }

        public void Disconnect()
        {
            _device?.ConnectClose();
            _isConnected = false;
        }

        public T? Read<T>(string address)
        {
            if (_device == null || !_isConnected) return default;

            try
            {
                if (typeof(T) == typeof(bool))
                {
                    var result = _device.ReadBool(address);
                    if (result.IsSuccess) return (T)(object)result.Content;
                }
                else if (typeof(T) == typeof(short))
                {
                    var result = _device.ReadInt16(address);
                    if (result.IsSuccess) return (T)(object)result.Content;
                }
                else if (typeof(T) == typeof(ushort))
                {
                    var result = _device.ReadUInt16(address);
                    if (result.IsSuccess) return (T)(object)result.Content;
                }
                else if (typeof(T) == typeof(int))
                {
                    var result = _device.ReadInt32(address);
                    if (result.IsSuccess) return (T)(object)result.Content;
                }
                else if (typeof(T) == typeof(uint))
                {
                    var result = _device.ReadUInt32(address);
                    if (result.IsSuccess) return (T)(object)result.Content;
                }
                else if (typeof(T) == typeof(float))
                {
                    var result = _device.ReadFloat(address);
                    if (result.IsSuccess) return (T)(object)result.Content;
                }
                else if (typeof(T) == typeof(double))
                {
                    var result = _device.ReadDouble(address);
                    if (result.IsSuccess) return (T)(object)result.Content;
                }
                else if (typeof(T) == typeof(byte))
                {
                    var result = _device.Read(address, 1);
                    if (result.IsSuccess && result.Content.Length > 0) return (T)(object)result.Content[0];
                }
            }
            catch { }

            return default;
        }

        public void Write(string address, object value)
        {
            if (_device == null || !_isConnected) throw new InvalidOperationException("设备未连接");

            OperateResult result;
            if (value is bool b) result = _device.Write(address, b);
            else if (value is short s) result = _device.Write(address, s);
            else if (value is ushort us) result = _device.Write(address, us);
            else if (value is int i) result = _device.Write(address, i);
            else if (value is uint ui) result = _device.Write(address, ui);
            else if (value is float f) result = _device.Write(address, f);
            else if (value is double d) result = _device.Write(address, d);
            else if (value is byte b2) result = _device.Write(address, b2);
            else throw new NotSupportedException($"不支持的类型: {value.GetType().Name}");

            if (!result.IsSuccess)
                throw new Exception($"写入失败: {result.Message}");
        }

        public byte[] ReadBytes(string address, ushort length)
        {
            if (_device == null || !_isConnected) throw new InvalidOperationException("设备未连接");
            var result = _device.Read(address, length);
            if (!result.IsSuccess) throw new Exception($"读取失败: {result.Message}");
            return result.Content;
        }

        public void WriteBytes(string address, byte[] data)
        {
            if (_device == null || !_isConnected) throw new InvalidOperationException("设备未连接");
            var result = _device.Write(address, data);
            if (!result.IsSuccess) throw new Exception($"写入失败: {result.Message}");
        }

        public void Dispose()
        {
            Disconnect();
        }
    }

    public class SiemensS7Connection : ICommunicationConnection
    {
        private SiemensS7Net? _device;
        private bool _isConnected = false;
        
        public string ConnectionName { get; private set; }
        public CommunicationType Type => CommunicationType.SiemensS7;
        public bool IsConnected => _isConnected;

        public SiemensS7Connection(CommunicationConfig config)
        {
            ConnectionName = config.ConnectionName;
            
            SiemensPLCS cpuType = config.S7CpuType;

            
            _device = new SiemensS7Net(cpuType, config.IpAddress)
            {
                ConnectTimeOut = config.ConnectionTimeout
            };
        }

        public bool Connect()
        {
            if (_device == null) return false;
            var result = _device.ConnectServer();
            _isConnected = result.IsSuccess;
            return result.IsSuccess;
        }

        public void Disconnect()
        {
            _device?.ConnectClose();
            _isConnected = false;
        }

        public T? Read<T>(string address)
        {
            if (_device == null || !_isConnected) return default;

            try
            {
                if (typeof(T) == typeof(bool))
                {
                    var result = _device.ReadBool(address);
                    if (result.IsSuccess) return (T)(object)result.Content;
                }
                else if (typeof(T) == typeof(byte))
                {
                    var result = _device.ReadByte(address);
                    if (result.IsSuccess) return (T)(object)result.Content;
                }
                else if (typeof(T) == typeof(short))
                {
                    var result = _device.ReadInt16(address);
                    if (result.IsSuccess) return (T)(object)result.Content;
                }
                else if (typeof(T) == typeof(ushort))
                {
                    var result = _device.ReadUInt16(address);
                    if (result.IsSuccess) return (T)(object)result.Content;
                }
                else if (typeof(T) == typeof(int))
                {
                    var result = _device.ReadInt32(address);
                    if (result.IsSuccess) return (T)(object)result.Content;
                }
                else if (typeof(T) == typeof(uint))
                {
                    var result = _device.ReadUInt32(address);
                    if (result.IsSuccess) return (T)(object)result.Content;
                }
                else if (typeof(T) == typeof(float))
                {
                    var result = _device.ReadFloat(address);
                    if (result.IsSuccess) return (T)(object)result.Content;
                }
                else if (typeof(T) == typeof(double))
                {
                    var result = _device.ReadDouble(address);
                    if (result.IsSuccess) return (T)(object)result.Content;
                }
            }
            catch { }

            return default;
        }

        public void Write(string address, object value)
        {
            if (_device == null || !_isConnected) throw new InvalidOperationException("设备未连接");

            OperateResult result;
            if (value is bool b) result = _device.Write(address, b);
            else if (value is byte b2) result = _device.Write(address, b2);
            else if (value is short s) result = _device.Write(address, s);
            else if (value is ushort us) result = _device.Write(address, us);
            else if (value is int i) result = _device.Write(address, i);
            else if (value is uint ui) result = _device.Write(address, ui);
            else if (value is float f) result = _device.Write(address, f);
            else if (value is double d) result = _device.Write(address, d);
            else throw new NotSupportedException($"不支持的类型: {value.GetType().Name}");

            if (!result.IsSuccess)
                throw new Exception($"写入失败: {result.Message}");
        }

        public byte[] ReadBytes(string address, ushort length)
        {
            if (_device == null || !_isConnected) throw new InvalidOperationException("设备未连接");
            var result = _device.Read(address, length);
            if (!result.IsSuccess) throw new Exception($"读取失败: {result.Message}");
            return result.Content;
        }

        public void WriteBytes(string address, byte[] data)
        {
            if (_device == null || !_isConnected) throw new InvalidOperationException("设备未连接");
            var result = _device.Write(address, data);
            if (!result.IsSuccess) throw new Exception($"写入失败: {result.Message}");
        }

        public void Dispose()
        {
            Disconnect();
        }
    }

    public class CommunicationErrorEventArgs : EventArgs
    {
        public string ConnectionName { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public Exception? Exception { get; set; }
    }

    public class VariableChangedEventArgs : EventArgs
    {
        public string ConnectionName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public object? OldValue { get; set; }
        public object? NewValue { get; set; }
    }

    public class VariableWriteRequest
    {
        public string ConnectionName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public object Value { get; set; } = null!;
        public Type ValueType { get; set; } = typeof(object);
    }

    public class CommunicationVariable
    {
        public string ConnectionName { get; set; } = string.Empty;
        public string VariableName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public Type ValueType { get; set; } = typeof(object);
        public VariableAccessMode AccessMode { get; set; } = VariableAccessMode.ReadOnly;
        public object? CurrentValue { get; private set; }
        public DateTime LastUpdateTime { get; private set; }

        public event EventHandler<object?>? ValueChanged;

        public void UpdateValue(object? newValue)
        {
            if (CurrentValue != newValue)
            {
                CurrentValue = newValue;
                LastUpdateTime = DateTime.Now;
                ValueChanged?.Invoke(this, newValue);
            }
        }
    }
}
