namespace Core.Interfaces
{
    /// <summary>
    /// 相机触发模式。
    /// </summary>
    public enum CameraTriggerMode
    {
        /// <summary>软触发：由软件下令曝光一次</summary>
        Software = 0,

        /// <summary>上升沿：外部信号（光电 / 编码器 / PLC）上升沿触发</summary>
        RisingEdge = 1,

        /// <summary>下降沿：外部信号下降沿触发</summary>
        FallingEdge = 2
    }

    /// <summary>
    /// 相机连接状态（推模式下的生命周期）。
    ///
    /// 四态对两类相机都成立：
    ///   网络相机 = Closed → Connecting（等待客户端心跳）→ Online（客户端在但没送帧）→ Streaming（正在收帧）
    ///   真机     = Closed → Connecting（打开设备 / 退避重连中）→ Online（已连接未采流）→ Streaming（采流中）
    ///
    /// 为什么必须把 "Online（在线但没送帧）" 与 "Connecting/Closed（掉线）" 分开
    /// ---------
    /// 现场看到"没有图"时的第一句话必然是"相机到底在不在"。这两件事的原因与处置完全不同：
    ///   在线但没送帧 = 没触发 / 用户按了暂停 —— 该去查触发链路，不该报故障；
    ///   掉线         = 网络或供电问题 —— 该报故障。
    /// 混成一态，界面就会把"没触发"渲染成"相机掉线"，把运维引到一个根本不存在的故障上。
    /// </summary>
    public enum CameraConnectionState
    {
        /// <summary>未打开（用户尚未连接）</summary>
        Closed = 0,

        /// <summary>正在建立连接 / 正在后台退避重连 / 等待客户端接入</summary>
        Connecting = 1,

        /// <summary>已连接但未采流（在线，等待触发或等待开始采流）</summary>
        Online = 2,

        /// <summary>已开始采流（是否真的在出图由 ReceivedFrameCount / PendingFrameCount 表达）</summary>
        Streaming = 3
    }
}
