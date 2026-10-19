using System;

namespace Core.Interfaces.Io
{
    /// <summary>
    /// 字节级链路。非线程安全：只允许所属总线 Worker 调用。
    /// </summary>
    public interface IIoTransport : IDisposable
    {
        string Name { get; }
        bool IsOpen { get; }
        bool Open(out string? error);
        void Close();

        /// <summary>一问一答。返回 false 时 error 说明原因。</summary>
        bool Exchange(ReadOnlySpan<byte> request, Span<byte> response,
                      out int responseLength, out string? error);
    }

    /// <summary>
    /// 协议编解码：负责地址解析、组帧、解帧、异常码映射。
    /// 驱动返回原始值；缩放/偏移/死区由 <c>IoDeviceBase</c> 统一处理。
    /// </summary>
    public interface IIoProtocol
    {
        string Name { get; }

        bool TryRead(IIoTransport transport, IoPointDescriptor point,
                     out IoPointValue value, out string? error);

        bool TryWrite(IIoTransport transport, IoPointDescriptor point,
                      IoPointValue value, out string? error);

        /// <summary>
        /// 批量读。linkError 非空 = 链路级故障；frameError 非空 = 整帧协议异常；
        /// 两者都为空时 values 里是读到的点。驱动能 per-point 报错时写进 results。
        /// </summary>
        bool TryReadBatch(IIoTransport transport, IReadOnlyList<IoPointDescriptor> points,
                          IDictionary<string, IoPointValue> values,
                          ICollection<IoPointReadResult>? results,
                          out string? linkError, out string? frameError);
    }
}
