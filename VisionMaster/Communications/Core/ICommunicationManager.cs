using System;
using System.Collections.Generic;

namespace VisionMaster.Communications
{

    public interface ICommunicationManager
    {
        #region 事件

        /// <summary>
        /// <para>通讯错误事件。</para>
        /// <para>当通讯过程中发生错误时触发此事件，如连接断开、读写失败等。</para>
        /// </summary>
        /// <event>
        /// <para>事件触发时机：</para>
        /// <list type="number">
        ///   <item>连接意外断开时</item>
        ///   <item>读取数据失败时</item>
        ///   <item>写入数据失败时</item>
        ///   <item>连接超时</item>
        /// </list>
        /// </event>
        /// <example>
        /// <code>
        /// manager.OnCommunicationError += (s, e) =>
        /// {
        ///     Console.WriteLine($"错误: {e.ConnectionName} - {e.ErrorMessage}");
        /// };
        /// </code>
        /// </example>
        event EventHandler<CommunicationErrorEventArgs>? OnCommunicationError;

        /// <summary>
        /// <para>变量值变化事件。</para>
        /// <para>当注册的通讯变量值发生变化时触发此事件。</para>
        /// </summary>
        /// <remarks>
        /// <para>此事件仅对通过RegisterVariable注册的变量生效。</para>
        /// <para>对于主动读取的变量，不会触发此事件。</para>
        /// </remarks>
        /// <example>
        /// <code>
        /// manager.OnVariableChanged += (s, e) =>
        /// {
        ///     Console.WriteLine($"变量变化: {e.VariableName} = {e.NewValue}");
        /// };
        /// </code>
        /// </example>
        event EventHandler<VariableChangedEventArgs>? OnVariableChanged;

        #endregion

        #region 连接管理

        /// <summary>
        /// <para>获取指定名称的连接对象。</para>
        /// </summary>
        /// <param name="connectionName">
        /// <para>连接的名称。</para>
        /// <para>该名称在添加连接时指定，应保证唯一性。</para>
        /// </param>
        /// <returns>
        /// <para>如果找到对应名称的连接，返回连接对象。</para>
        /// <para>如果未找到，返回null。</para>
        /// </returns>
        /// <example>
        /// <code>
        /// var connection = manager.GetConnection("PLC_1");
        /// if (connection?.IsConnected == true)
        /// {
        ///     var value = connection.Read&lt;short&gt;("40001");
        /// }
        /// </code>
        /// </example>
        ICommunicationConnection? GetConnection(string connectionName);

        /// <summary>
        /// <para>向管理器添加一个新的通讯连接。</para>
        /// </summary>
        /// <param name="config">
        /// <para>连接配置对象。</para>
        /// <para>配置应包含连接所需的所有信息，如IP地址、端口号、协议类型等。</para>
        /// </param>
        /// <returns>
        /// <para>如果添加成功返回true。</para>
        /// <para>如果添加失败（如配置无效、名称重复等）返回false。</para>
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// 当config为null时抛出此异常。
        /// </exception>
        /// <example>
        /// <code>
        /// var config = new ModbusTcpConfig
        /// {
        ///     Name = "PLC_1",
        ///     IpAddress = "192.168.1.100",
        ///     Port = 502
        /// };
        /// if (manager.AddConnection(config))
        /// {
        ///     Console.WriteLine("连接添加成功");
        /// }
        /// </code>
        /// </example>
        /// <seealso cref="CommunicationConfig"/>
        bool AddConnection(CommunicationConfig config);

        /// <summary>
        /// <para>从管理器中移除指定的通讯连接。</para>
        /// <para>移除前会先断开连接，释放相关资源。</para>
        /// </summary>
        /// <param name="connectionName">要移除的连接名称</param>
        /// <returns>
        /// <para>如果移除成功返回true。</para>
        /// <para>如果未找到对应连接返回false。</para>
        /// </returns>
        /// <remarks>
        /// <para>移除操作会：</para>
        /// <list type="number">
        ///   <item>断开连接（如果处于连接状态）</item>
        ///   <item>注销所有关联的变量</item>
        ///   <item>清理相关资源</item>
        /// </list>
        /// </remarks>
        bool RemoveConnection(string connectionName);

        /// <summary>
        /// <para>更新指定连接的配置信息。</para>
        /// <para>如果连接处于连接状态，更新后需要重新连接才能使新配置生效。</para>
        /// </summary>
        /// <param name="config">新的连接配置</param>
        /// <returns>
        /// <para>如果更新成功返回true。</para>
        /// <para>如果未找到对应连接返回false。</para>
        /// </returns>
        /// <remarks>
        /// <para>不建议在连接处于活动状态时频繁更新配置。</para>
        /// <para>某些配置项（如IP地址、端口号）的更改会导致连接断开。</para>
        /// </remarks>
        bool UpdateConnection(CommunicationConfig config);

        /// <summary>
        /// <para>获取管理器中所有连接的配置信息。</para>
        /// </summary>
        /// <returns>
        /// <para>包含所有连接配置的列表。</para>
        /// <para>列表中的每个元素都是配置对象的副本。</para>
        /// </returns>
        /// <example>
        /// <code>
        /// var allConfigs = manager.GetAllConnections();
        /// foreach (var config in allConfigs)
        /// {
        ///     Console.WriteLine($"{config.Name}: {config.Type}");
        /// }
        /// </code>
        /// </example>
        List<CommunicationConfig> GetAllConnections();

        /// <summary>
        /// <para>启动所有已添加的通讯连接。</para>
        /// <para>会依次连接所有配置好的设备，并启动相关的读取循环。</para>
        /// </summary>
        /// <remarks>
        /// <para>启动操作会：</para>
        /// <list type="number">
        ///   <item>尝试连接所有已配置但未连接的设备</item>
        ///   <item>启动轮询读取（如果已配置）</item>
        ///   <item>启动写入队列处理</item>
        /// </list>
        /// </remarks>
        /// <example>
        /// <code>
        /// manager.StartAll();
        /// </code>
        /// </example>
        /// <seealso cref="StopAll"/>
        void StartAll();

        /// <summary>
        /// <para>停止所有通讯活动。</para>
        /// <para>会断开所有连接，停止读取循环。</para>
        /// </summary>
        /// <remarks>
        /// <para>停止操作会：</para>
        /// <list type="number">
        ///   <item>停止写入队列处理</item>
        ///   <item>停止所有轮询读取</item>
        ///   <item>断开所有设备连接</item>
        /// </list>
        /// </remarks>
        /// <example>
        /// <code>
        /// manager.StopAll();
        /// </code>
        /// </example>
        /// <seealso cref="StartAll"/>
        void StopAll();

        #endregion

        #region 数据读写

        /// <summary>
        /// <para>向指定的通讯地址写入数据。</para>
        /// <para>这是一个同步写入操作，会阻塞直到写入完成或超时。</para>
        /// </summary>
        /// <param name="connectionName">目标连接名称</param>
        /// <param name="address">通讯地址</param>
        /// <param name="value">要写入的值</param>
        /// <exception cref="InvalidOperationException">
        /// 当连接不存在或未连接时抛出此异常。
        /// </exception>
        /// <example>
        /// <code>
        /// // 写入各种类型的数据
        /// manager.WriteVariable("PLC_1", "40001", (short)100);
        /// manager.WriteVariable("PLC_1", "Q0.0", true);
        /// manager.WriteVariable("PLC_1", "40010", 12.5f);
        /// </code>
        /// </example>
        /// <seealso cref="ReadVariable{T}(string, string)"/>
        void WriteVariable(string connectionName, string address, object value);

        /// <summary>
        /// <para>从指定的通讯地址读取数据。</para>
        /// <para>这是一个同步读取操作，会阻塞直到数据返回或超时。</para>
        /// </summary>
        /// <typeparam name="T">要读取的数据类型</typeparam>
        /// <param name="connectionName">源连接名称</param>
        /// <param name="address">通讯地址</param>
        /// <returns>
        /// <para>读取到的数据值。</para>
        /// <para>如果读取失败或类型不匹配，返回对应类型的默认值。</para>
        /// </returns>
        /// <exception cref="InvalidOperationException">
        /// 当连接不存在或未连接时抛出此异常。
        /// </exception>
        /// <example>
        /// <code>
        /// // 读取各种类型的数据
        /// short temp = manager.ReadVariable&lt;short&gt;("PLC_1", "40001");
        /// bool isRunning = manager.ReadVariable&lt;bool&gt;("PLC_1", "Q0.0");
        /// float pressure = manager.ReadVariable&lt;float&gt;("PLC_1", "40010");
        /// </code>
        /// </example>
        /// <seealso cref="WriteVariable(string, string, object)"/>
        T? ReadVariable<T>(string connectionName, string address);

        #endregion

        #region 变量管理

        /// <summary>
        /// <para>注册一个通讯变量进行监控。</para>
        /// <para>注册后，系统会自动轮询该变量的值，并在值变化时触发OnVariableChanged事件。</para>
        /// </summary>
        /// <param name="variable">
        /// <para>要注册的通讯变量。</para>
        /// <para>变量应包含连接名称、地址、数据类型等信息。</para>
        /// </param>
        /// <remarks>
        /// <para>注册变量会：</para>
        /// <list type="number">
        ///   <item>创建数据点并关联到指定连接</item>
        ///   <item>添加到轮询列表</item>
        ///   <item>启动对该变量的监控</item>
        /// </list>
        /// </remarks>
        /// <example>
        /// <code>
        /// var variable = new CommunicationVariable
        /// {
        ///     Name = "Temperature",
        ///     ConnectionName = "PLC_1",
        ///     Address = "40001",
        ///     DataType = typeof(short)
        /// };
        /// manager.RegisterVariable(variable);
        /// </code>
        /// </example>
        /// <seealso cref="UnregisterVariable(string, string)"/>
        /// <seealso cref="CommunicationVariable"/>
        void RegisterVariable(CommunicationVariable variable);

        /// <summary>
        /// <para>注销一个已注册的通讯变量。</para>
        /// <para>注销后，系统将停止对该变量的监控，不再触发值变化事件。</para>
        /// </summary>
        /// <param name="connectionName">变量所属的连接名称</param>
        /// <param name="variableName">要注销的变量名称</param>
        /// <remarks>
        /// <para>注销操作会：</para>
        /// <list type="number">
        ///   <item>从轮询列表中移除</item>
        ///   <item>释放相关资源</item>
        ///   <item>停止监控</item>
        /// </list>
        /// </remarks>
        /// <example>
        /// <code>
        /// manager.UnregisterVariable("PLC_1", "Temperature");
        /// </code>
        /// </example>
        /// <seealso cref="RegisterVariable(CommunicationVariable)"/>
        void UnregisterVariable(string connectionName, string variableName);

        /// <summary>
        /// <para>触发写入操作，将写入请求加入队列。</para>
        /// <para>与WriteVariable不同，此方法不会立即执行写入，而是将请求加入队列，异步处理。</para>
        /// </summary>
        /// <param name="connectionName">目标连接名称</param>
        /// <param name="address">通讯地址</param>
        /// <param name="value">要写入的值</param>
        /// <param name="valueType">值的类型</param>
        /// <remarks>
        /// <para>队列写入的优点：</para>
        /// <list type="bullet">
        ///   <item>不会阻塞调用线程</item>
        ///   <item>可以合并连续的写入请求</item>
        ///   <item>提高批量写入的效率</item>
        /// </list>
        /// </remarks>
        /// <example>
        /// <code>
        /// // 快速连续调用会被合并
        /// manager.TriggerWrite("PLC_1", "40001", (short)100, typeof(short));
        /// manager.TriggerWrite("PLC_1", "40002", (short)200, typeof(short));
        /// </code>
        /// </example>
        void TriggerWrite(string connectionName, string address, object value, Type valueType);

        #endregion
    }
}
