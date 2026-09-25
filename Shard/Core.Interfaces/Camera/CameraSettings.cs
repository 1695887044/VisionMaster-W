namespace Core.Interfaces
{
    /// <summary>
    /// 相机采集参数（随方案落盘，属于 <see cref="CameraDescriptor"/>）。
    ///
    /// 归属口径：曝光 / 增益 / 触发模式属于**相机**（方案级资源），
    /// 而"等一帧最多等多久"属于**步骤**（采集步骤自己的输入端口可覆盖
    /// <see cref="FrameTimeoutMs"/>）。两者都在这里给出默认值，便于相机单独试采。
    /// </summary>
    public class CameraSettings
    {
        /// <summary>曝光时间（微秒）。默认 10000（10ms）</summary>
        public double ExposureTimeUs { get; set; } = 10000;

        /// <summary>增益（单位由驱动自行解释：有的 SDK 是 dB，有的是原始码值）</summary>
        public double Gain { get; set; }

        /// <summary>触发模式</summary>
        public CameraTriggerMode TriggerMode { get; set; } = CameraTriggerMode.Software;

        /// <summary>
        /// 等一帧的超时（毫秒）。默认 3000。
        /// <b>必须是有限值</b>：没有超时的等图意味着相机一旦不触发，流程线程就永久卡死，
        /// 现场只能杀进程——这是参考工程 WaitOne() 无超时留下的最严重缺陷。
        /// </summary>
        public int FrameTimeoutMs { get; set; } = 3000;

        /// <summary>
        /// 环形帧队列容量（超出时丢最旧并计入溢出）。默认 8。
        /// 太小易丢帧，太大会掩盖"产能不匹配"这个真问题并把延迟越积越大。
        /// </summary>
        public int BufferCapacity { get; set; } = 8;

        /// <summary>
        /// 心跳超时（毫秒）：多久没有收到"设备还活着"的证据就判掉线。默认 3000。
        /// 网络相机用它区分"客户端在但没送帧"与"客户端没了"；
        /// 真机驱动可忽略（SDK 自带掉线回调，走 NotifyDeviceLost）。
        /// </summary>
        public int HeartbeatTimeoutMs { get; set; } = 3000;

        /// <summary>拷贝一份（避免调用方与设备共享同一个实例后互相改）</summary>
        public CameraSettings Clone() => new()
        {
            ExposureTimeUs = ExposureTimeUs,
            Gain = Gain,
            TriggerMode = TriggerMode,
            FrameTimeoutMs = FrameTimeoutMs,
            BufferCapacity = BufferCapacity,
            HeartbeatTimeoutMs = HeartbeatTimeoutMs
        };

        /// <summary>把非法的容量 / 超时收敛到可用区间（配置可被人工编辑，必须有兜底）</summary>
        public CameraSettings Normalized()
        {
            var copy = Clone();
            if (copy.BufferCapacity < 1) copy.BufferCapacity = 1;
            if (copy.BufferCapacity > 1024) copy.BufferCapacity = 1024;
            if (copy.FrameTimeoutMs < 1) copy.FrameTimeoutMs = 3000;
            if (copy.HeartbeatTimeoutMs < 200) copy.HeartbeatTimeoutMs = 3000;
            return copy;
        }
    }
}
