using System;
using System.Collections.Generic;

namespace VisionMaster.Communications
{
    /// <summary>
    /// 通讯管理器接口，定义通讯管理的核心功能
    /// </summary>
    public interface ICommunicationManager
    {
        /// <summary>
        /// 通讯错误事件
        /// </summary>
        event EventHandler<CommunicationErrorEventArgs>? OnCommunicationError;

        /// <summary>
        /// 变量值变化事件
        /// </summary>
        event EventHandler<VariableChangedEventArgs>? OnVariableChanged;

        /// <summary>
        /// 获取指定连接
        /// </summary>
        /// <param name="connectionName">连接名称</param>
        /// <returns>连接对象</returns>
        ICommunicationConnection? GetConnection(string connectionName);

        /// <summary>
        /// 添加连接
        /// </summary>
        /// <param name="config">连接配置</param>
        /// <returns>是否添加成功</returns>
        bool AddConnection(CommunicationConfig config);

        /// <summary>
        /// 移除连接
        /// </summary>
        /// <param name="connectionName">连接名称</param>
        /// <returns>是否移除成功</returns>
        bool RemoveConnection(string connectionName);

        /// <summary>
        /// 更新连接配置
        /// </summary>
        /// <param name="config">新的连接配置</param>
        /// <returns>是否更新成功</returns>
        bool UpdateConnection(CommunicationConfig config);

        /// <summary>
        /// 获取所有连接配置
        /// </summary>
        /// <returns>连接配置列表</returns>
        List<CommunicationConfig> GetAllConnections();

        /// <summary>
        /// 启动所有连接和读取循环
        /// </summary>
        void StartAll();

        /// <summary>
        /// 停止所有通讯
        /// </summary>
        void StopAll();

        /// <summary>
        /// 写入变量值
        /// </summary>
        /// <param name="connectionName">连接名称</param>
        /// <param name="address">变量地址</param>
        /// <param name="value">写入值</param>
        void WriteVariable(string connectionName, string address, object value);

        /// <summary>
        /// 读取变量值
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="connectionName">连接名称</param>
        /// <param name="address">变量地址</param>
        /// <returns>读取的值</returns>
        T? ReadVariable<T>(string connectionName, string address);

        /// <summary>
        /// 注册通讯变量
        /// </summary>
        /// <param name="variable">通讯变量</param>
        void RegisterVariable(CommunicationVariable variable);

        /// <summary>
        /// 注销通讯变量
        /// </summary>
        /// <param name="connectionName">连接名称</param>
        /// <param name="variableName">变量名称</param>
        void UnregisterVariable(string connectionName, string variableName);

        /// <summary>
        /// 触发写入操作(加入写入队列)
        /// </summary>
        /// <param name="connectionName">连接名称</param>
        /// <param name="address">变量地址</param>
        /// <param name="value">写入值</param>
        /// <param name="valueType">值类型</param>
        void TriggerWrite(string connectionName, string address, object value, Type valueType);
    }
}
