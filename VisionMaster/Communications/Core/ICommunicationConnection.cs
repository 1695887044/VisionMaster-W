using System;

namespace VisionMaster.Communications
{
    /// <summary>
    /// <para>通讯连接接口，定义所有通信连接的标准行为。</para>
    /// <para>所有具体的通信连接（如Modbus TCP、S7、串口等）都必须实现此接口。</para>
    /// <para>该接口继承IDisposable，确保连接资源能够被正确释放。</para>
    /// </summary>
    /// <example>
    /// <code>
    /// // 实现示例
    /// public class MyConnection : ICommunicationConnection
    /// {
    ///     public string ConnectionName { get; set; }
    ///     public CommunicationType Type => CommunicationType.ModbusTcp;
    ///     public bool IsConnected => _socket?.Connected ?? false;
    ///     
    ///     public bool Connect() { /* 实现连接逻辑 */ }
    ///     public void Disconnect() { /* 实现断开逻辑 */ }
    ///     public T? Read&lt;T&gt;(string address) { /* 实现读取逻辑 */ }
    ///     public void Write(string address, object value) { /* 实现写入逻辑 */ }
    /// }
    /// </code>
    /// </example>
    /// <seealso cref="ICommunicationManager"/>
    /// <seealso cref="CommunicationType"/>
    public interface ICommunicationConnection : IDisposable
    {
        #region 属性

        /// <summary>
        /// <para>获取连接的名称标识。</para>
        /// <para>该名称在系统中应该是唯一的，用于标识和引用特定的连接。</para>
        /// </summary>
        /// <value>连接的唯一名称，如"PLC_1"、"Modbus_Server"等</value>
        string ConnectionName { get; }

        /// <summary>
        /// <para>获取当前连接的通信协议类型。</para>
        /// <para>用于标识连接使用的具体协议，便于系统进行协议特定的配置和处理。</para>
        /// </summary>
        /// <value>通信协议类型枚举值</value>
        /// <seealso cref="CommunicationType"/>
        CommunicationType Type { get; }

        /// <summary>
        /// <para>获取当前连接是否处于已连接状态。</para>
        /// <para>该属性用于快速检查连接状态，避免在断开连接时执行读写操作。</para>
        /// </summary>
        /// <value>如果已连接返回true，否则返回false</value>
        bool IsConnected { get; }

        #endregion

        #region 连接管理

        /// <summary>
        /// <para>建立与远程设备的通信连接。</para>
        /// <para>调用此方法后，应等待连接建立完成或超时。</para>
        /// </summary>
        /// <returns>
        /// <para>如果连接成功建立返回true。</para>
        /// <para>如果连接失败返回false，可能的原因包括：</para>
        /// <list type="bullet">
        ///   <item>网络不可达</item>
        ///   <item>设备无响应</item>
        ///   <item>认证失败</item>
        ///   <item>连接超时</item>
        /// </list>
        /// </returns>
        /// <example>
        /// <code>
        /// var connection = new ModbusTcpConnection(config);
        /// if (connection.Connect())
        /// {
        ///     Console.WriteLine("连接成功");
        /// }
        /// else
        /// {
        ///     Console.WriteLine("连接失败");
        /// }
        /// </code>
        /// </example>
        /// <remarks>
        /// <para>实现注意事项：</para>
        /// <list type="number">
        ///   <item>应在方法内部处理重试逻辑</item>
        ///   <item>设置适当的超时时间</item>
        ///   <item>连接成功后应触发连接状态变更事件</item>
        /// </list>
        /// </remarks>
        bool Connect();

        /// <summary>
        /// <para>断开与远程设备的通信连接。</para>
        /// <para>调用此方法后，连接将被关闭，IsConnected属性应立即变为false。</para>
        /// </summary>
        /// <example>
        /// <code>
        /// connection.Disconnect();
        /// Debug.Assert(!connection.IsConnected);
        /// </code>
        /// </example>
        /// <remarks>
        /// <para>实现注意事项：</para>
        /// <list type="number">
        ///   <item>应释放所有占用的资源</item>
        ///   <item>关闭底层的socket或串口</item>
        ///   <item>触发连接状态变更事件</item>
        /// </list>
        /// </remarks>
        void Disconnect();

        /// <summary>
        /// <para>测试连接是否可用。</para>
        /// <para>与Connect()不同，此方法不会建立新连接，而是检查现有连接是否仍然有效。</para>
        /// </summary>
        /// <returns>
        /// <para>如果连接可用返回true。</para>
        /// <para>如果连接不可用或已断开返回false。</para>
        /// </returns>
        /// <example>
        /// <code>
        /// // 健康检查示例
        /// if (connection.TestConnection())
        /// {
        ///     Console.WriteLine("连接正常");
        /// }
        /// else
        /// {
        ///     Console.WriteLine("连接已断开，需要重新连接");
        /// }
        /// </code>
        /// </example>
        /// <seealso cref="ConnectionHealthCheck"/>
        bool TestConnection();

        #endregion

        #region 数据读写

        /// <summary>
        /// <para>从指定地址读取数据。</para>
        /// <para>支持读取多种数据类型，包括基本数值类型和布尔类型。</para>
        /// </summary>
        /// <typeparam name="T">
        /// <para>要读取的数据类型。</para>
        /// <para>支持的类型包括：</para>
        /// <list type="bullet">
        ///   <item>bool - 布尔值（线圈、离散输入）</item>
        ///   <item>byte/short/int/uint/ushort - 整数类型</item>
        ///   <item>float/double - 浮点数类型</item>
        /// </list>
        /// </typeparam>
        /// <param name="address">
        /// <para>通信地址字符串。</para>
        /// <para>地址格式因协议而异：</para>
        /// <list type="bullet">
        ///   <item>Modbus: "40001" (保持寄存器), "10001" (输入) 等</item>
        ///   <item>S7: "DB1.DBW0" (数据块字), "M0.0" (标记位) 等</item>
        /// </list>
        /// </param>
        /// <returns>
        /// <para>读取到的数据值。</para>
        /// <para>如果读取失败返回对应类型的默认值（如0或false）。</para>
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// 当连接未建立时抛出此异常。
        /// </exception>
        /// <example>
        /// <code>
        /// // 读取不同类型的数据
        /// short temperature = connection.Read&lt;short&gt;("40001");
        /// bool isRunning = connection.Read&lt;bool&gt;("10001");
        /// float pressure = connection.Read&lt;float&gt;("40010");
        /// </code>
        /// </example>
        /// <remarks>
        /// <para>实现注意事项：</para>
        /// <list type="number">
        ///   <item>地址格式必须符合对应协议规范</item>
        ///   <item>数据类型必须与地址对应的数据类型匹配</item>
        ///   <item>读取操作应有超时保护</item>
        ///   <item>读取失败时应记录详细错误信息</item>
        /// </list>
        /// </remarks>
        T? Read<T>(string address);

        /// <summary>
        /// <para>向指定地址写入数据。</para>
        /// <para>支持写入多种数据类型。</para>
        /// </summary>
        /// <param name="address">
        /// <para>通信地址字符串，格式同Read方法。</para>
        /// </param>
        /// <param name="value">
        /// <para>要写入的值。</para>
        /// <para>值的类型应与地址对应的数据类型匹配。</para>
        /// </param>
        /// <exception cref="InvalidOperationException">
        /// 当连接未建立时抛出此异常。
        /// </exception>
        /// <exception cref="ArgumentException">
        /// 当值类型与地址不匹配时抛出此异常。
        /// </exception>
        /// <example>
        /// <code>
        /// // 写入不同类型的数据
        /// connection.Write("40001", (short)100);
        /// connection.Write("10001", true);
        /// connection.Write("40010", 12.5f);
        /// </code>
        /// </example>
        /// <remarks>
        /// <para>实现注意事项：</para>
        /// <list type="number">
        ///   <item>写入操作前应检查连接状态</item>
        ///   <item>写入后可能需要验证写入是否成功</item>
        ///   <item>某些协议可能需要将写入操作转换为底层字节</item>
        /// </list>
        /// </remarks>
        void Write(string address, object value);

        /// <summary>
        /// <para>从指定地址读取原始字节数组。</para>
        /// <para>用于需要直接访问底层字节数据的场景，如读取复杂数据结构。</para>
        /// </summary>
        /// <param name="address">通信地址字符串</param>
        /// <param name="length">
        /// <para>要读取的字节长度。</para>
        /// <para>不同协议可能有不同的最大限制。</para>
        /// </param>
        /// <returns>读取到的字节数组</returns>
        /// <exception cref="InvalidOperationException">
        /// 当连接未建立时抛出此异常。
        /// </exception>
        /// <example>
        /// <code>
        /// // 读取100个字节
        /// var bytes = connection.ReadBytes("40001", 100);
        /// </code>
        /// </example>
        byte[] ReadBytes(string address, ushort length);

        /// <summary>
        /// <para>向指定地址写入原始字节数组。</para>
        /// <para>用于需要直接写入底层字节数据的场景。</para>
        /// </summary>
        /// <param name="address">通信地址字符串</param>
        /// <param name="data">要写入的字节数组</param>
        /// <exception cref="InvalidOperationException">
        /// 当连接未建立时抛出此异常。
        /// </exception>
        /// <example>
        /// <code>
        /// // 写入字节数组
        /// var data = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        /// connection.WriteBytes("40001", data);
        /// </code>
        /// </example>
        void WriteBytes(string address, byte[] data);

        #endregion
    }
}
