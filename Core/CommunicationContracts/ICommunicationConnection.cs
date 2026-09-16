using System;

namespace VisionMaster.Communications
{

    public interface ICommunicationConnection : IDisposable
    {

        string ConnectionName { get; }

        CommunicationType Type { get; }

        bool IsConnected { get; }


        bool Connect();

        void Disconnect();


        bool TestConnection();


        T Read<T>(string address) where T : struct;

        void Write(string address, object value);

        byte[] ReadBytes(string address, ushort length);

        void WriteBytes(string address, byte[] data);

        /// <summary>
        /// <para>位区批量读（Modbus FC1 线圈 / FC2 离散输入），供轮询批量化规划器使用。</para>
        /// <para>不支持位区批量读的协议（如 S7，其位随字节段批量读后切片解码）抛 NotSupportedException。</para>
        /// </summary>
        bool[] ReadBits(string address, ushort count);
    }
}
