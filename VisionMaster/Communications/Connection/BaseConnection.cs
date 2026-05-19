using HslCommunication;
using System;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>通信连接泛型基类，提供所有连接实现的通用框架。</para>
    /// <para>该类封装了连接管理的通用逻辑，包括连接状态管理、线程安全保护和数据读写的类型分发。</para>
    /// <para>具体的协议实现需要继承此类并实现协议特定的抽象方法。</para>
    /// </summary>
    /// <typeparam name="TDevice">
    /// <para>HslCommunication库中的设备类型。</para>
    /// <para>如：ModbusTcpNet、SiemensS7Net、ModbusRtu等。</para>
    /// </typeparam>
    /// <example>
    /// <code>
    /// public class MyProtocolConnection : BaseConnection&lt;MyProtocolNet&gt;
    /// {
    ///     public override string ConnectionName { get; set; } = "MyConnection";
    ///     public override CommunicationType Type => CommunicationType.MyProtocol;
    ///     public override ConnectionConfigBase? Config { get; protected set; }
    ///     
    ///     protected override void InitializeDevice()
    ///     {
    ///         // 初始化设备对象
    ///         _device = new MyProtocolNet(...);
    ///     }
    ///     
    ///     // 实现其他抽象方法...
    /// }
    /// </code>
    /// </example>
    /// <seealso cref="ICommunicationConnection"/>
    /// <seealso cref="ModbusTcpConnection"/>
    /// <seealso cref="SiemensS7Connection"/>
    public abstract class BaseConnection<TDevice> : ICommunicationConnection where TDevice : class, IDisposable
    {
        #region 私有字段

        /// <summary>
        /// <para>底层设备对象。</para>
        /// <para>由具体协议实现类在InitializeDevice中初始化。</para>
        /// </summary>
        protected TDevice? _device;

        /// <summary>
        /// <para>连接状态标志。</para>
        /// <para>true表示已连接，false表示未连接。</para>
        /// </summary>
        protected bool _isConnected = false;

        /// <summary>
        /// <para>线程同步锁。</para>
        /// <para>用于保护连接状态和设备操作的线程安全。</para>
        /// </summary>
        protected readonly object _lock = new();

        #endregion

        #region 属性

        /// <summary>
        /// <para>获取或设置连接名称。</para>
        /// <para>在系统中应该是唯一的，用于标识和引用特定的连接。</para>
        /// </summary>
        public abstract string ConnectionName { get; set; }

        /// <summary>
        /// <para>获取连接的通信协议类型。</para>
        /// <para>由具体协议实现类返回对应的枚举值。</para>
        /// </summary>
        public abstract CommunicationType Type { get; }

        /// <summary>
        /// <para>获取或设置连接配置对象。</para>
        /// <para>包含连接所需的参数（IP地址、端口、超时时间等）。</para>
        /// </summary>
        public abstract ConnectionConfigBase? Config { get; protected set; }

        /// <summary>
        /// <para>获取当前连接是否处于已连接状态。</para>
        /// <para>只有当_isConnected为true且_device不为null时才返回true。</para>
        /// </summary>
        public bool IsConnected => _isConnected && _device != null;

        #endregion

        #region 抽象方法

        /// <summary>
        /// <para>初始化设备对象。</para>
        /// <para>具体协议实现类需要在此方法中创建并配置底层设备对象。</para>
        /// </summary>
        protected abstract void InitializeDevice();

        /// <summary>
        /// <para>连接到服务器。</para>
        /// <para>具体协议实现类需要实现协议特定的连接逻辑。</para>
        /// </summary>
        /// <returns>连接操作结果</returns>
        protected abstract OperateResult ConnectServer();

        /// <summary>
        /// <para>关闭连接。</para>
        /// <para>具体协议实现类需要实现协议特定的断开逻辑。</para>
        /// </summary>
        protected abstract void CloseConnection();

        /// <summary>
        /// <para>读取布尔值。</para>
        /// </summary>
        /// <param name="address">通信地址</param>
        /// <returns>读取结果</returns>
        protected abstract OperateResult<bool> ReadBool(string address);

        /// <summary>
        /// <para>读取Int16值。</para>
        /// </summary>
        /// <param name="address">通信地址</param>
        /// <returns>读取结果</returns>
        protected abstract OperateResult<short> ReadInt16(string address);

        /// <summary>
        /// <para>读取UInt16值。</para>
        /// </summary>
        /// <param name="address">通信地址</param>
        /// <returns>读取结果</returns>
        protected abstract OperateResult<ushort> ReadUInt16(string address);

        /// <summary>
        /// <para>读取Int32值。</para>
        /// </summary>
        /// <param name="address">通信地址</param>
        /// <returns>读取结果</returns>
        protected abstract OperateResult<int> ReadInt32(string address);

        /// <summary>
        /// <para>读取UInt32值。</para>
        /// </summary>
        /// <param name="address">通信地址</param>
        /// <returns>读取结果</returns>
        protected abstract OperateResult<uint> ReadUInt32(string address);

        /// <summary>
        /// <para>读取Float值。</para>
        /// </summary>
        /// <param name="address">通信地址</param>
        /// <returns>读取结果</returns>
        protected abstract OperateResult<float> ReadFloat(string address);

        /// <summary>
        /// <para>读取Double值。</para>
        /// </summary>
        /// <param name="address">通信地址</param>
        /// <returns>读取结果</returns>
        protected abstract OperateResult<double> ReadDouble(string address);

        /// <summary>
        /// <para>读取字节数组。</para>
        /// </summary>
        /// <param name="address">通信地址</param>
        /// <param name="length">要读取的字节长度</param>
        /// <returns>读取结果</returns>
        protected abstract OperateResult<byte[]> ReadBytesCore(string address, ushort length);

        /// <summary>
        /// <para>写入布尔值。</para>
        /// </summary>
        /// <param name="address">通信地址</param>
        /// <param name="value">要写入的值</param>
        /// <returns>写入结果</returns>
        protected abstract OperateResult WriteBool(string address, bool value);

        /// <summary>
        /// <para>写入Int16值。</para>
        /// </summary>
        /// <param name="address">通信地址</param>
        /// <param name="value">要写入的值</param>
        /// <returns>写入结果</returns>
        protected abstract OperateResult WriteInt16(string address, short value);

        /// <summary>
        /// <para>写入UInt16值。</para>
        /// </summary>
        /// <param name="address">通信地址</param>
        /// <param name="value">要写入的值</param>
        /// <returns>写入结果</returns>
        protected abstract OperateResult WriteUInt16(string address, ushort value);

        /// <summary>
        /// <para>写入Int32值。</para>
        /// </summary>
        /// <param name="address">通信地址</param>
        /// <param name="value">要写入的值</param>
        /// <returns>写入结果</returns>
        protected abstract OperateResult WriteInt32(string address, int value);

        /// <summary>
        /// <para>写入UInt32值。</para>
        /// </summary>
        /// <param name="address">通信地址</param>
        /// <param name="value">要写入的值</param>
        /// <returns>写入结果</returns>
        protected abstract OperateResult WriteUInt32(string address, uint value);

        /// <summary>
        /// <para>写入Float值。</para>
        /// </summary>
        /// <param name="address">通信地址</param>
        /// <param name="value">要写入的值</param>
        /// <returns>写入结果</returns>
        protected abstract OperateResult WriteFloat(string address, float value);

        /// <summary>
        /// <para>写入Double值。</para>
        /// </summary>
        /// <param name="address">通信地址</param>
        /// <param name="value">要写入的值</param>
        /// <returns>写入结果</returns>
        protected abstract OperateResult WriteDouble(string address, double value);

        /// <summary>
        /// <para>写入字节数组。</para>
        /// </summary>
        /// <param name="address">通信地址</param>
        /// <param name="data">要写入的字节数组</param>
        /// <returns>写入结果</returns>
        protected abstract OperateResult WriteBytesCore(string address, byte[] data);

        #endregion

        #region 公共方法

        /// <summary>
        /// <para>建立与远程设备的通信连接。</para>
        /// <para>此方法是线程安全的，使用锁保护连接状态。</para>
        /// </summary>
        /// <returns>
        /// <para>如果连接成功建立返回true。</para>
        /// <para>如果连接失败返回false。</para>
        /// </returns>
        /// <example>
        /// <code>
        /// var connection = new ModbusTcpConnection(config);
        /// if (connection.Connect())
        /// {
        ///     Console.WriteLine("连接成功");
        /// }
        /// </code>
        /// </example>
        public bool Connect()
        {
            lock (_lock)
            {
                if (_isConnected) return true;
                try
                {
                    InitializeDevice();
                    var result = ConnectServer();
                    _isConnected = result.IsSuccess;
                    return _isConnected;
                }
                catch
                {
                    _isConnected = false;
                    return false;
                }
            }
        }

        /// <summary>
        /// <para>断开与远程设备的通信连接。</para>
        /// <para>此方法是线程安全的，使用锁保护连接状态。</para>
        /// </summary>
        /// <example>
        /// <code>
        /// connection.Disconnect();
        /// </code>
        /// </example>
        public void Disconnect()
        {
            lock (_lock)
            {
                CloseConnection();
                _isConnected = false;
            }
        }

        /// <summary>
        /// <para>测试连接是否可用。</para>
        /// <para>此方法会尝试建立连接然后立即断开，用于验证连接配置是否正确。</para>
        /// </summary>
        /// <returns>
        /// <para>如果连接测试成功返回true。</para>
        /// <para>如果连接测试失败返回false。</para>
        /// </returns>
        /// <example>
        /// <code>
        /// if (connection.TestConnection())
        /// {
        ///     Console.WriteLine("连接配置正确");
        /// }
        /// </code>
        /// </example>
        public bool TestConnection()
        {
            var result = Connect();
            Disconnect();
            return result;
        }

        /// <summary>
        /// <para>从指定地址读取数据。</para>
        /// <para>根据类型参数自动选择对应的读取方法。</para>
        /// </summary>
        /// <typeparam name="T">要读取的数据类型</typeparam>
        /// <param name="address">通信地址</param>
        /// <returns>读取到的数据值</returns>
        /// <exception cref="InvalidOperationException">当设备未连接时抛出</exception>
        /// <example>
        /// <code>
        /// short temp = connection.Read&lt;short&gt;("40001");
        /// bool status = connection.Read&lt;bool&gt;("10001");
        /// </code>
        /// </example>
        public T? Read<T>(string address)
        {
            if (!IsConnected) throw new InvalidOperationException("设备未连接");

            return typeof(T).Name switch
            {
                nameof(Boolean) => (T)(object)ReadBool(address).Content,
                nameof(Int16) => (T)(object)ReadInt16(address).Content,
                nameof(UInt16) => (T)(object)ReadUInt16(address).Content,
                nameof(Int32) => (T)(object)ReadInt32(address).Content,
                nameof(UInt32) => (T)(object)ReadUInt32(address).Content,
                nameof(Single) => (T)(object)ReadFloat(address).Content,
                nameof(Double) => (T)(object)ReadDouble(address).Content,
                nameof(Byte) => (T)(object)ReadBytesCore(address, 1).Content[0],
                _ => default
            };
        }

        /// <summary>
        /// <para>从指定地址读取字节数组。</para>
        /// </summary>
        /// <param name="address">通信地址</param>
        /// <param name="length">要读取的字节长度</param>
        /// <returns>读取到的字节数组</returns>
        /// <exception cref="InvalidOperationException">当设备未连接或读取失败时抛出</exception>
        /// <example>
        /// <code>
        /// var bytes = connection.ReadBytes("40001", 10);
        /// </code>
        /// </example>
        public byte[] ReadBytes(string address, ushort length)
        {
            if (!IsConnected) throw new InvalidOperationException("设备未连接");
            var result = ReadBytesCore(address, length);
            if (!result.IsSuccess) throw new InvalidOperationException(result.Message);
            return result.Content;
        }

        /// <summary>
        /// <para>向指定地址写入数据。</para>
        /// <para>根据值的类型自动选择对应的写入方法。</para>
        /// </summary>
        /// <param name="address">通信地址</param>
        /// <param name="value">要写入的值</param>
        /// <exception cref="InvalidOperationException">当设备未连接或写入失败时抛出</exception>
        /// <exception cref="NotSupportedException">当值类型不支持时抛出</exception>
        /// <example>
        /// <code>
        /// connection.Write("40001", (short)100);
        /// connection.Write("10001", true);
        /// </code>
        /// </example>
        public void Write(string address, object value)
        {
            if (!IsConnected) throw new InvalidOperationException("设备未连接");

            OperateResult result = value switch
            {
                bool b => WriteBool(address, b),
                byte b => WriteBytesCore(address, new[] { b }),
                short s => WriteInt16(address, s),
                ushort us => WriteUInt16(address, us),
                int i => WriteInt32(address, i),
                uint ui => WriteUInt32(address, ui),
                float f => WriteFloat(address, f),
                double d => WriteDouble(address, d),
                _ => throw new NotSupportedException($"不支持的类型: {value.GetType()}")
            };

            if (!result.IsSuccess) throw new InvalidOperationException(result.Message);
        }

        /// <summary>
        /// <para>向指定地址写入字节数组。</para>
        /// </summary>
        /// <param name="address">通信地址</param>
        /// <param name="data">要写入的字节数组</param>
        /// <exception cref="InvalidOperationException">当设备未连接或写入失败时抛出</exception>
        /// <example>
        /// <code>
        /// byte[] data = new byte[] { 0x01, 0x02, 0x03 };
        /// connection.WriteBytes("40001", data);
        /// </code>
        /// </example>
        public void WriteBytes(string address, byte[] data)
        {
            if (!IsConnected) throw new InvalidOperationException("设备未连接");
            var result = WriteBytesCore(address, data);
            if (!result.IsSuccess) throw new InvalidOperationException(result.Message);
        }

        /// <summary>
        /// <para>释放连接使用的所有资源。</para>
        /// <para>断开连接并释放底层设备对象。</para>
        /// </summary>
        public void Dispose()
        {
            Disconnect();
            _device?.Dispose();
        }

        #endregion
    }
}