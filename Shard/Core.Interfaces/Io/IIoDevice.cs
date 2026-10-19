using System;
using System.Collections.Generic;
using System.Threading;

namespace Core.Interfaces.Io
{
    /// <summary>
    /// 运行态 IO 设备契约。所有会阻塞的操作都必须有超时；读写最终都串行到设备 Worker。
    /// </summary>
    public interface IIoDevice : IDisposable
    {
        IoDeviceDescriptor Descriptor { get; }
        IoBusDescriptor Bus { get; }
        IoDeviceState State { get; }
        string StateDetail { get; }
        IoCapabilities Capabilities { get; }

        event EventHandler? StateChanged;
        event EventHandler<IoPointsChangedEventArgs>? PointsChanged;
        event EventHandler<IoFault>? FaultOccurred;

        bool Connect();
        void Disconnect();

        /// <summary>读轮询缓存，不发起通信。流程/界面/SCADA 的常规读取走这里。</summary>
        bool TryRead(string pointId, out IoPointValue value);

        /// <summary>高优先级即时读：绕过缓存，发一次真实请求。</summary>
        IoPointValue ReadNow(string pointId, int timeoutMs, CancellationToken ct, out string? error);

        /// <summary>入队写；返回“收下了吗”，完成语义看 <see cref="IoWriteResult"/>。</summary>
        IoWriteResult Write(string pointId, IoPointValue value, string? ownerId = null);

        /// <summary>入队写并等待回执；超时返回 false，错误文案区分“未执行/可能已执行”。</summary>
        bool WriteAndWait(string pointId, IoPointValue value, int timeoutMs,
                          CancellationToken ct, out string? error, string? ownerId = null);

        DateTime LastSuccessTime { get; }
        long WriteCount { get; }
        long RejectedWriteCount { get; }
        long FaultCount { get; }
        void ResetCounters();
    }
}
