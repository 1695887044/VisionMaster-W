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

    /// <summary>
    /// <para>链路健康探针（可选能力）：让轮询层在段读失败后，能区分"链路断了"与"只是这个地址读不到"。</para>
    /// <para>为什么需要它：批量轮询是"一个段一次往返"，若 socket 已经死掉，段读循环会把剩余每一段
    /// 都各烧掉一整个读超时。半开场景（拔网线 / 防火墙丢包 / 服务端进程假死）下，
    /// 掉线检测延迟 ≈ 段数 × ReadTimeoutMs：5 段 × 3s = 15s 尚可忍，
    /// 167 段 × 3s ≈ 8 分钟就不可接受了。有了它，轮询层可在链路故障那一刻立即中断剩余段。</para>
    /// <para>不具备该能力的实现（如串口）不必实现本接口，轮询层按"不支持"处理，行为与旧版一致。</para>
    /// </summary>
    public interface ILinkHealthProbe
    {
        /// <summary>
        /// 上一次底层传输 I/O 是否发生了<b>链路级</b>故障（收发超时 / 连接被重置 / 建连被拒）。
        /// <para>实现约定：必须无副作用、不做 I/O、可跨线程读。</para>
        /// <para><b>不得</b>把"帧收到了、只是内容是错误码"（如 Modbus 非法地址异常、S7 地址越界）
        /// 算作链路故障——否则一个坏地址会把整条好连接打进无限重连循环，
        /// 破坏"个别地址恒坏按变量级错误消化"的既有设计。</para>
        /// </summary>
        bool HasLinkFault { get; }
    }
}
