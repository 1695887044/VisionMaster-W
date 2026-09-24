using System;

namespace VisionMaster.Communications
{

    public interface ICommunicationConnection : IDisposable
    {

        string ConnectionName { get; }

        CommunicationType Type { get; }

        bool IsConnected { get; }

        /// <summary>
        /// <para>本连接多寄存器数值（32/64 位）的线路字节序，供轮询批量规划器切片解码时对齐。</para>
        /// <para>没有字序概念的协议（S7 等）恒为 <see cref="ByteOrderFormat.ABCD"/>。
        /// 若这里与单点读的字序不一致，会出现"界面上读一次对、变量轮询却错"的诡异现象。</para>
        /// </summary>
        ByteOrderFormat ByteOrder { get; }


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
