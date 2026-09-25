using System;
using System.Threading;

namespace Core.Interfaces
{
    /// <summary>
    /// 相机设备契约（运行态）。一家厂商 = 一个实现。
    ///
    /// 采集语义是**推模式**：设备一旦开始采流就持续往内部的环形队列送帧，流程通过
    /// <see cref="WaitNextFrame"/> 消费。这条选择带来两条必须遵守的硬约束：
    ///
    /// 1) <b>SDK 收图回调线程绝不阻塞</b>。大恒 / 海康 / HALCON 的 SDK 都一样，
    ///    回调里一阻塞就会内部队列溢出、丢帧甚至掉线。回调只做"拷一份 + 入队"。
    /// 2) <b>帧队列有上限且溢出必须可见</b>。处理不过来时继续吃旧帧只会越来越滞后（雪崩），
    ///    所以丢最旧保最新；但"丢帧 = 漏检工件"，所以每一次丢弃都要计数并暴露给界面，
    ///    绝不允许静默吞掉。
    /// </summary>
    public interface ICameraDevice : IDisposable
    {
        /// <summary>本设备对应的配置（含 Id / 序列号 / 显示名 / 参数）</summary>
        CameraDescriptor Descriptor { get; }

        /// <summary>连接状态</summary>
        CameraConnectionState State { get; }

        /// <summary>状态补充说明（连接失败原因 / 重连说明 / 最后一次错误），供界面与日志直接显示</summary>
        string StateDetail { get; }

        /// <summary>累计溢出丢帧数（队列满 → 丢最旧）。界面必须能看见它</summary>
        long OverflowCount { get; }

        /// <summary>累计"非溢出"丢弃数（掉线清空队列等）。与溢出分开计，因为原因与处置完全不同</summary>
        long DroppedFrameCount { get; }

        /// <summary>累计收到的帧数</summary>
        long ReceivedFrameCount { get; }

        /// <summary>队列里当前待消费的帧数（诊断用）</summary>
        int PendingFrameCount { get; }

        /// <summary>
        /// 状态变化通知。注意：在**非 UI 线程**触发，订阅方（界面）必须自行调度回 UI 线程。
        /// 在锁外触发，订阅方可以安全地回调设备上的只读查询。
        /// </summary>
        event EventHandler StateChanged;

        /// <summary>打开设备（网络相机 = 登记序列号并开始接收；真机 = 打开 SDK 设备句柄）</summary>
        bool Open();

        /// <summary>关闭设备并清空队列</summary>
        void Close();

        /// <summary>开始采流（真机去启动采集；网络相机只是状态标记，帧仍由客户端送来）</summary>
        bool StartStream();

        /// <summary>停止采流（停止后送来的帧会被拒绝）</summary>
        void StopStream();

        /// <summary>把参数应用到设备并同步到 <see cref="CameraDescriptor.Settings"/>（配置界面用）</summary>
        bool ApplySettings(CameraSettings settings);

        /// <summary>读取设备当前生效的参数快照</summary>
        CameraSettings ReadSettings();

        /// <summary>
        /// "设备还活着"的证据（心跳）。
        /// 网络相机：宿主收图服务在收到心跳或帧时调用；
        /// 真机：SDK 心跳回调 / 一次成功取流时调用。
        /// 基类据此把"在线但没送帧"与"真掉线"分开——这是本设计最要紧的一处语义区分。
        /// </summary>
        void NotifyAlive();

        /// <summary>
        /// 送帧入口（推模式的核心）。
        /// 网络相机：由宿主收图服务解析完 HTTP 后调用；
        /// 真机：由 SDK 回调内部调用（驱动自己解出像素后转交）。
        /// </summary>
        /// <param name="frame">已解码好的原始像素帧（本方法会回填 CameraId / SerialNo / FrameId）</param>
        /// <param name="error">失败原因（中文，可直接进日志/界面）</param>
        bool PushFrame(CameraFrame frame, out string error);

        /// <summary>
        /// 只读取最新帧（**不消费**）。
        /// 供界面预览 / 监视画面使用：预览绝不能吃掉生产帧，否则一开监视窗口就会偶发漏检。
        /// </summary>
        bool TryGetLatest(out CameraFrame frame);

        /// <summary>
        /// 消费一帧（每帧只会被一个消费者取到一次）。取不到就等到超时或被取消。
        ///
        /// 返回 false 时调用方**必须**自己区分两种情况：
        ///   ct 已取消 → 取消（用户点了停止流程，不算业务失败）；
        ///   超时       → 业务失败（相机没出图）。
        /// 本方法不替调用方决定流程去向，也不触碰任何流程控制状态。
        /// </summary>
        bool WaitNextFrame(out CameraFrame frame, int timeoutMs, CancellationToken ct);

        /// <summary>清零溢出 / 丢弃计数（现场确认过之后重新开始观察）</summary>
        void ResetCounters();
    }
}
