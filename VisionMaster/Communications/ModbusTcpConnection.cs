using HslCommunication;
using HslCommunication.ModBus;

namespace VisionMaster.Communications
{
    /// <summary>
    /// Modbus TCP连接实现类
    /// </summary>
    public class ModbusTcpConnection : ICommunicationConnection
    {
        private ModbusTcpNet? _device;
        private bool _isConnected = false;

        /// <summary>
        /// 连接名称
        /// </summary>
        public string ConnectionName { get; private set; }

        /// <summary>
        /// 通讯类型
        /// </summary>
        public CommunicationType Type => CommunicationType.ModbusTcp;

        /// <summary>
        /// 是否已连接
        /// </summary>
        public bool IsConnected => _isConnected;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="config">通讯配置</param>
        public ModbusTcpConnection(CommunicationConfig config)
        {
            ConnectionName = config.ConnectionName;
            _device = new ModbusTcpNet(config.IpAddress, config.Port, config.Station)
            {
                ConnectTimeOut = config.ConnectionTimeout,
                AddressStartWithZero = true
            };
        }

        /// <summary>
        /// 连接到设备
        /// </summary>
        /// <returns>是否连接成功</returns>
        public bool Connect()
        {
            if (_device == null) return false;
            var result = _device.ConnectServer();
            _isConnected = result.IsSuccess;
            return result.IsSuccess;
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            _device?.ConnectClose();
            _isConnected = false;
        }

        /// <summary>
        /// 根据类型读取值
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="address">通讯地址</param>
        /// <returns>读取的值</returns>
        public T? Read<T>(string address)
        {
            if (_device == null || !_isConnected) return default;

            try
            {
                // 根据不同类型调用对应的读取方法
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
                    // 使用Read方法读取单个字节
                    var result = _device.Read(address, 1);
                    if (result.IsSuccess && result.Content.Length > 0) return (T)(object)result.Content[0];
                }
            }
            catch { }

            return default;
        }

        /// <summary>
        /// 写入值到指定地址
        /// </summary>
        /// <param name="address">通讯地址</param>
        /// <param name="value">写入值</param>
        public void Write(string address, object value)
        {
            if (_device == null || !_isConnected) throw new InvalidOperationException("设备未连接");

            // 根据不同类型调用对应的写入方法
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

        /// <summary>
        /// 读取字节数组
        /// </summary>
        /// <param name="address">通讯地址</param>
        /// <param name="length">读取长度</param>
        /// <returns>字节数组</returns>
        public byte[] ReadBytes(string address, ushort length)
        {
            if (_device == null || !_isConnected) throw new InvalidOperationException("设备未连接");
            var result = _device.Read(address, length);
            if (!result.IsSuccess) throw new Exception($"读取失败: {result.Message}");
            return result.Content;
        }

        /// <summary>
        /// 写入字节数组
        /// </summary>
        /// <param name="address">通讯地址</param>
        /// <param name="data">字节数组</param>
        public void WriteBytes(string address, byte[] data)
        {
            if (_device == null || !_isConnected) throw new InvalidOperationException("设备未连接");
            var result = _device.Write(address, data);
            if (!result.IsSuccess) throw new Exception($"写入失败: {result.Message}");
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Disconnect();
        }
    }
}
