using HslCommunication;
using HslCommunication.ModBus;
using System;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>Modbus TCP 协议连接实现类。</para>
    /// <para>基于 HslCommunication 库实现 Modbus TCP 协议的通信功能。</para>
    /// <para>支持标准 Modbus TCP 协议的读写操作。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// // 使用 CommunicationConfig 创建
    /// var config = CommunicationConfig.CreateModbusTcp("PLC_1", "192.168.1.100", 502);
    /// var connection = new ModbusTcpConnection(config);
    /// 
    /// // 连接设备
    /// if (connection.Connect())
    /// {
    ///     // 读取保持寄存器
    ///     short value = connection.Read&lt;short&gt;("40001");
    ///     
    ///     // 写入保持寄存器
    ///     connection.Write("40001", (short)100);
    ///     
    ///     connection.Disconnect();
    /// }
    /// </code>
    /// </example>
    /// <seealso cref="BaseConnection{TDevice}"/>
    /// <seealso cref="ModbusTcpConfig"/>
    public class ModbusTcpConnection : BaseConnection<ModbusTcpNet>
    {
        #region 属性

        /// <summary>
        /// <para>获取或设置连接名称。</para>
        /// </summary>
        public override string ConnectionName { get; set; } = string.Empty;

        /// <summary>
        /// <para>获取通信协议类型。</para>
        /// <para>返回 CommunicationType.ModbusTcp。</para>
        /// </summary>
        public override CommunicationType Type => CommunicationType.ModbusTcp;

        /// <summary>
        /// <para>获取或设置连接配置对象。</para>
        /// <para>应为 ModbusTcpConfig 类型。</para>
        /// </summary>
        public override ConnectionConfigBase? Config { get; protected set; }

        #endregion

        #region 构造方法

        /// <summary>
        /// <para>使用 CommunicationConfig 创建 Modbus TCP 连接。</para>
        /// </summary>
        /// <param name="config">通信配置对象</param>
        /// <exception cref="InvalidOperationException">当配置类型不正确时抛出</exception>
        public ModbusTcpConnection(CommunicationConfig config)
        {
            if (config.Config is not ModbusTcpConfig modbusConfig)
                throw new InvalidOperationException("需要 Modbus TCP 配置");
            Config = modbusConfig;
            ConnectionName = config.ConnectionName;
        }

        /// <summary>
        /// <para>使用 ModbusTcpConfig 创建 Modbus TCP 连接。</para>
        /// </summary>
        /// <param name="config">Modbus TCP 配置对象</param>
        public ModbusTcpConnection(ModbusTcpConfig config)
        {
            Config = config;
            ConnectionName = config.IpAddress + ":" + config.Port;
        }

        #endregion

        #region 保护方法

        /// <summary>
        /// <para>初始化 Modbus TCP 设备对象。</para>
        /// <para>创建 ModbusTcpNet 实例并配置连接参数。</para>
        /// </summary>
        protected override void InitializeDevice()
        {
            var tcpConfig = Config as EthernetConfigBase;
            _device = new ModbusTcpNet(tcpConfig!.IpAddress, tcpConfig.Port)
            {
                ConnectTimeOut = tcpConfig.TimeoutMs,
                AddressStartWithZero = true
            };
        }

        /// <summary>
        /// <para>连接到 Modbus TCP 服务器。</para>
        /// </summary>
        /// <returns>连接操作结果</returns>
        protected override OperateResult ConnectServer() => _device!.ConnectServer();

        /// <summary>
        /// <para>关闭 Modbus TCP 连接。</para>
        /// </summary>
        protected override void CloseConnection() => _device?.ConnectClose();

        /// <summary>
        /// <para>读取布尔值（线圈或离散输入）。</para>
        /// </summary>
        /// <param name="address">Modbus 地址，如 "00001"（线圈）或 "10001"（离散输入）</param>
        /// <returns>读取结果</returns>
        protected override OperateResult<bool> ReadBool(string address) => _device!.ReadBool(address);

        /// <summary>
        /// <para>读取 Int16 值（保持寄存器或输入寄存器）。</para>
        /// </summary>
        /// <param name="address">Modbus 地址，如 "30001"（输入寄存器）或 "40001"（保持寄存器）</param>
        /// <returns>读取结果</returns>
        protected override OperateResult<short> ReadInt16(string address) => _device!.ReadInt16(address);

        /// <summary>
        /// <para>读取 UInt16 值。</para>
        /// </summary>
        /// <param name="address">Modbus 地址</param>
        /// <returns>读取结果</returns>
        protected override OperateResult<ushort> ReadUInt16(string address) => _device!.ReadUInt16(address);

        /// <summary>
        /// <para>读取 Int32 值。</para>
        /// </summary>
        /// <param name="address">Modbus 地址</param>
        /// <returns>读取结果</returns>
        protected override OperateResult<int> ReadInt32(string address) => _device!.ReadInt32(address);

        /// <summary>
        /// <para>读取 UInt32 值。</para>
        /// </summary>
        /// <param name="address">Modbus 地址</param>
        /// <returns>读取结果</returns>
        protected override OperateResult<uint> ReadUInt32(string address) => _device!.ReadUInt32(address);

        /// <summary>
        /// <para>读取 Float 值。</para>
        /// </summary>
        /// <param name="address">Modbus 地址</param>
        /// <returns>读取结果</returns>
        protected override OperateResult<float> ReadFloat(string address) => _device!.ReadFloat(address);

        /// <summary>
        /// <para>读取 Double 值。</para>
        /// </summary>
        /// <param name="address">Modbus 地址</param>
        /// <returns>读取结果</returns>
        protected override OperateResult<double> ReadDouble(string address) => _device!.ReadDouble(address);

        /// <summary>
        /// <para>读取字节数组。</para>
        /// </summary>
        /// <param name="address">Modbus 地址</param>
        /// <param name="length">要读取的字节长度</param>
        /// <returns>读取结果</returns>
        protected override OperateResult<byte[]> ReadBytesCore(string address, ushort length) => _device!.Read(address, length);

        /// <summary>
        /// <para>写入布尔值（线圈）。</para>
        /// </summary>
        /// <param name="address">Modbus 线圈地址，如 "00001"</param>
        /// <param name="value">要写入的值</param>
        /// <returns>写入结果</returns>
        protected override OperateResult WriteBool(string address, bool value) => _device!.Write(address, value);

        /// <summary>
        /// <para>写入 Int16 值（保持寄存器）。</para>
        /// </summary>
        /// <param name="address">Modbus 保持寄存器地址，如 "40001"</param>
        /// <param name="value">要写入的值</param>
        /// <returns>写入结果</returns>
        protected override OperateResult WriteInt16(string address, short value) => _device!.Write(address, value);

        /// <summary>
        /// <para>写入 UInt16 值。</para>
        /// </summary>
        /// <param name="address">Modbus 地址</param>
        /// <param name="value">要写入的值</param>
        /// <returns>写入结果</returns>
        protected override OperateResult WriteUInt16(string address, ushort value) => _device!.Write(address, value);

        /// <summary>
        /// <para>写入 Int32 值。</para>
        /// </summary>
        /// <param name="address">Modbus 地址</param>
        /// <param name="value">要写入的值</param>
        /// <returns>写入结果</returns>
        protected override OperateResult WriteInt32(string address, int value) => _device!.Write(address, value);

        /// <summary>
        /// <para>写入 UInt32 值。</para>
        /// </summary>
        /// <param name="address">Modbus 地址</param>
        /// <param name="value">要写入的值</param>
        /// <returns>写入结果</returns>
        protected override OperateResult WriteUInt32(string address, uint value) => _device!.Write(address, value);

        /// <summary>
        /// <para>写入 Float 值。</para>
        /// </summary>
        /// <param name="address">Modbus 地址</param>
        /// <param name="value">要写入的值</param>
        /// <returns>写入结果</returns>
        protected override OperateResult WriteFloat(string address, float value) => _device!.Write(address, value);

        /// <summary>
        /// <para>写入 Double 值。</para>
        /// </summary>
        /// <param name="address">Modbus 地址</param>
        /// <param name="value">要写入的值</param>
        /// <returns>写入结果</returns>
        protected override OperateResult WriteDouble(string address, double value) => _device!.Write(address, value);

        /// <summary>
        /// <para>写入字节数组。</para>
        /// </summary>
        /// <param name="address">Modbus 地址</param>
        /// <param name="data">要写入的字节数组</param>
        /// <returns>写入结果</returns>
        protected override OperateResult WriteBytesCore(string address, byte[] data) => _device!.Write(address, data);

        #endregion

        #region 公共方法

        /// <summary>
        /// <para>获取连接的字符串表示。</para>
        /// </summary>
        /// <returns>包含IP地址和端口的字符串</returns>
        public override string ToString() => $"ModbusTcp[{((Config as EthernetConfigBase)?.IpAddress)}:{((Config as EthernetConfigBase)?.Port)}]";

        #endregion
    }
}