using System;

namespace VisionMaster.Communications
{
    /// <summary>
    /// 通讯连接接口
    /// </summary>
    public interface ICommunicationConnection : IDisposable
    {
        /// <summary>
        /// 连接名称
        /// </summary>
        string ConnectionName { get; }

        /// <summary>
        /// 通讯类型
        /// </summary>
        CommunicationType Type { get; }

        /// <summary>
        /// 是否已连接
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// 连接方法
        /// </summary>
        /// <returns>是否连接成功</returns>
        bool Connect();

        /// <summary>
        /// 断开连接
        /// </summary>
        void Disconnect();

        /// <summary>
        /// 测试连接
        /// </summary>
        /// <returns>是否成功</returns>
        bool TestConnection();

        /// <summary>
        /// 读取指定类型的值
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="address">通讯地址</param>
        /// <returns>读取的值</returns>
        T? Read<T>(string address);

        /// <summary>
        /// 写入值到指定地址
        /// </summary>
        /// <param name="address">通讯地址</param>
        /// <param name="value">写入值</param>
        void Write(string address, object value);

        /// <summary>
        /// 读取字节数组
        /// </summary>
        /// <param name="address">通讯地址</param>
        /// <param name="length">读取长度</param>
        /// <returns>字节数组</returns>
        byte[] ReadBytes(string address, ushort length);

        /// <summary>
        /// 写入字节数组
        /// </summary>
        /// <param name="address">通讯地址</param>
        /// <param name="data">字节数组</param>
        void WriteBytes(string address, byte[] data);
    }
}
