using HslCommunication;
using HslCommunication.Profinet.Siemens;
using System;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>西门子 S7 协议连接实现类。</para>
    /// <para>基于 HslCommunication 库实现西门子 S7 系列 PLC 的通信功能。</para>
    /// <para>支持 S7-200 Smart、S7-1200、S7-1500、S7-300、S7-400 等型号。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// // 使用 CommunicationConfig 创建
    /// var config = CommunicationConfig.CreateSiemensS7("PLC_1", "192.168.1.100", SiemensPLCS.S1200);
    /// var connection = new SiemensS7Connection(config);
    /// 
    /// // 连接设备
    /// if (connection.Connect())
    /// {
    ///     // 读取数据块
    ///     short value = connection.Read&lt;short&gt;("DB1.DBW0");
    ///     
    ///     // 写入数据块
    ///     connection.Write("DB1.DBW0", (short)100);
    ///     
    ///     connection.Disconnect();
    /// }
    /// </code>
    /// </example>
    /// <seealso cref="BaseConnection{TDevice}"/>
    /// <seealso cref="SiemensS7Config"/>
    public class SiemensS7Connection : BaseConnection<SiemensS7Net>
    {
        #region 属性

        /// <summary>
        /// <para>获取或设置连接名称。</para>
        /// </summary>
        public override string ConnectionName { get; set; } = string.Empty;

        /// <summary>
        /// <para>获取通信协议类型。</para>
        /// <para>返回 CommunicationType.SiemensS7。</para>
        /// </summary>
        public override CommunicationType Type => CommunicationType.SiemensS7;

        /// <summary>
        /// <para>获取或设置连接配置对象。</para>
        /// <para>应为 SiemensS7Config 类型。</para>
        /// </summary>
        public override ConnectionConfigBase? Config { get; protected set; }

        #endregion

        #region 构造方法

        /// <summary>
        /// <para>使用 CommunicationConfig 创建西门子 S7 连接。</para>
        /// </summary>
        /// <param name="config">通信配置对象</param>
        /// <exception cref="InvalidOperationException">当配置类型不正确时抛出</exception>
        public SiemensS7Connection(CommunicationConfig config)
        {
            if (config.Config is not SiemensS7Config s7Config)
                throw new InvalidOperationException("需要西门子S7配置");
            Config = s7Config;
            ConnectionName = config.ConnectionName;
        }

        /// <summary>
        /// <para>使用 SiemensS7Config 创建西门子 S7 连接。</para>
        /// </summary>
        /// <param name="config">西门子 S7 配置对象</param>
        public SiemensS7Connection(SiemensS7Config config)
        {
            Config = config;
            ConnectionName = config.IpAddress + ":" + config.Port + "(" + config.S7CpuType + ")";
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// <para>将字符串类型的 CPU 类型转换为 HslCommunication 的 SiemensPLCS 枚举。</para>
        /// </summary>
        /// <returns>SiemensPLCS 枚举值</returns>
        private SiemensPLCS GetCpuType() => Config switch
        {
            SiemensS7Config s7 => s7.S7CpuType switch
            {
                "Smart200" => SiemensPLCS.S200Smart,
                "S1200" => SiemensPLCS.S1200,
                "S1500" => SiemensPLCS.S1500,
                "S300" => SiemensPLCS.S300,
                "S400" => SiemensPLCS.S400,
                _ => SiemensPLCS.S1200
            },
            _ => SiemensPLCS.S1200
        };

        #endregion

        #region 保护方法

        /// <summary>
        /// <para>初始化西门子 S7 设备对象。</para>
        /// <para>创建 SiemensS7Net 实例并配置连接参数。</para>
        /// </summary>
        protected override void InitializeDevice()
        {
            var s7Config = Config as SiemensS7Config;
            _device = new SiemensS7Net(GetCpuType(), s7Config!.IpAddress)
            {
                ConnectTimeOut = s7Config.TimeoutMs,
                Rack = s7Config.Rack,
                Slot = s7Config.Slot
            };
            _device.Port = s7Config.Port;
        }

        /// <summary>
        /// <para>连接到西门子 S7 PLC。</para>
        /// </summary>
        /// <returns>连接操作结果</returns>
        protected override OperateResult ConnectServer() => _device!.ConnectServer();

        /// <summary>
        /// <para>关闭西门子 S7 连接。</para>
        /// </summary>
        protected override void CloseConnection() => _device?.ConnectClose();

        /// <summary>
        /// <para>读取布尔值（输入/输出位、标记位等）。</para>
        /// </summary>
        /// <param name="address">S7 地址，如 "I0.0"、"Q0.0"、"M0.0"、"DB1.DBX0.0"</param>
        /// <returns>读取结果</returns>
        protected override OperateResult<bool> ReadBool(string address) => _device!.ReadBool(address);

        /// <summary>
        /// <para>读取 Int16 值。</para>
        /// </summary>
        /// <param name="address">S7 地址，如 "DB1.DBW0"</param>
        /// <returns>读取结果</returns>
        protected override OperateResult<short> ReadInt16(string address) => _device!.ReadInt16(address);

        /// <summary>
        /// <para>读取 UInt16 值。</para>
        /// </summary>
        /// <param name="address">S7 地址</param>
        /// <returns>读取结果</returns>
        protected override OperateResult<ushort> ReadUInt16(string address) => _device!.ReadUInt16(address);

        /// <summary>
        /// <para>读取 Int32 值。</para>
        /// </summary>
        /// <param name="address">S7 地址</param>
        /// <returns>读取结果</returns>
        protected override OperateResult<int> ReadInt32(string address) => _device!.ReadInt32(address);

        /// <summary>
        /// <para>读取 UInt32 值。</para>
        /// </summary>
        /// <param name="address">S7 地址</param>
        /// <returns>读取结果</returns>
        protected override OperateResult<uint> ReadUInt32(string address) => _device!.ReadUInt32(address);

        /// <summary>
        /// <para>读取 Float 值。</para>
        /// </summary>
        /// <param name="address">S7 地址</param>
        /// <returns>读取结果</returns>
        protected override OperateResult<float> ReadFloat(string address) => _device!.ReadFloat(address);

        /// <summary>
        /// <para>读取 Double 值。</para>
        /// </summary>
        /// <param name="address">S7 地址</param>
        /// <returns>读取结果</returns>
        protected override OperateResult<double> ReadDouble(string address) => _device!.ReadDouble(address);

        /// <summary>
        /// <para>读取字节数组。</para>
        /// </summary>
        /// <param name="address">S7 地址</param>
        /// <param name="length">要读取的字节长度</param>
        /// <returns>读取结果</returns>
        protected override OperateResult<byte[]> ReadBytesCore(string address, ushort length) => _device!.Read(address, length);

        /// <summary>
        /// <para>写入布尔值。</para>
        /// </summary>
        /// <param name="address">S7 地址</param>
        /// <param name="value">要写入的值</param>
        /// <returns>写入结果</returns>
        protected override OperateResult WriteBool(string address, bool value) => _device!.Write(address, value);

        /// <summary>
        /// <para>写入 Int16 值。</para>
        /// </summary>
        /// <param name="address">S7 地址</param>
        /// <param name="value">要写入的值</param>
        /// <returns>写入结果</returns>
        protected override OperateResult WriteInt16(string address, short value) => _device!.Write(address, value);

        /// <summary>
        /// <para>写入 UInt16 值。</para>
        /// </summary>
        /// <param name="address">S7 地址</param>
        /// <param name="value">要写入的值</param>
        /// <returns>写入结果</returns>
        protected override OperateResult WriteUInt16(string address, ushort value) => _device!.Write(address, value);

        /// <summary>
        /// <para>写入 Int32 值。</para>
        /// </summary>
        /// <param name="address">S7 地址</param>
        /// <param name="value">要写入的值</param>
        /// <returns>写入结果</returns>
        protected override OperateResult WriteInt32(string address, int value) => _device!.Write(address, value);

        /// <summary>
        /// <para>写入 UInt32 值。</para>
        /// </summary>
        /// <param name="address">S7 地址</param>
        /// <param name="value">要写入的值</param>
        /// <returns>写入结果</returns>
        protected override OperateResult WriteUInt32(string address, uint value) => _device!.Write(address, value);

        /// <summary>
        /// <para>写入 Float 值。</para>
        /// </summary>
        /// <param name="address">S7 地址</param>
        /// <param name="value">要写入的值</param>
        /// <returns>写入结果</returns>
        protected override OperateResult WriteFloat(string address, float value) => _device!.Write(address, value);

        /// <summary>
        /// <para>写入 Double 值。</para>
        /// </summary>
        /// <param name="address">S7 地址</param>
        /// <param name="value">要写入的值</param>
        /// <returns>写入结果</returns>
        protected override OperateResult WriteDouble(string address, double value) => _device!.Write(address, value);

        /// <summary>
        /// <para>写入字节数组。</para>
        /// </summary>
        /// <param name="address">S7 地址</param>
        /// <param name="data">要写入的字节数组</param>
        /// <returns>写入结果</returns>
        protected override OperateResult WriteBytesCore(string address, byte[] data) => _device!.Write(address, data);

        #endregion

        #region 公共方法

        /// <summary>
        /// <para>获取连接的字符串表示。</para>
        /// </summary>
        /// <returns>包含IP地址、端口和CPU类型的字符串</returns>
        public override string ToString() => $"SiemensS7[{((Config as SiemensS7Config)?.IpAddress)}:{((Config as SiemensS7Config)?.Port)}({((Config as SiemensS7Config)?.S7CpuType)})]";

        #endregion
    }
}