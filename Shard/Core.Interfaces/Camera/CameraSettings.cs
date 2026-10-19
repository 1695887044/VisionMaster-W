using System;

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
        /// <summary>
        /// 触发配置的结构版本。
        ///
        /// 为什么需要它：2026-10-19 之前海康驱动固定连续采图，界面上的 TriggerMode 只是配置值、
        /// 不生效。旧方案里即使存着 Software/RisingEdge，真实行为也是连续采图。接通驱动后如果
        /// 直接把旧值当"软触发"执行，旧流程会因为没人下发 TriggerSoftware 而全部等图超时。
        /// 所以旧配置（没有本字段 = 0）一律迁移成 Continuous，保持升级前的真实行为；
        /// 新建配置必须走 <see cref="CreateNew"/>（显式置 1），才按用户选择执行。
        ///
        /// ⚠ 默认值必须是 0，不能是 1：Newtonsoft 反序列化缺失字段时**保留属性初始值**，
        /// 初始值写成 1 就永远识别不出"旧 JSON 没这个字段"，迁移会静默失效。
        /// </summary>
        public const int CurrentTriggerSchemaVersion = 1;

        /// <summary>触发配置结构版本；0 = 接通触发之前的旧配置</summary>
        public int TriggerSchemaVersion { get; set; }

        /// <summary>新建一份"当前版本"的采集参数（旧配置请用反序列化 + <see cref="Normalized"/> 迁移）</summary>
        public static CameraSettings CreateNew() => new() { TriggerSchemaVersion = CurrentTriggerSchemaVersion };

        /// <summary>曝光时间（微秒）。默认 10000（10ms）</summary>
        public double ExposureTimeUs { get; set; } = 10000;

        /// <summary>增益（单位由驱动自行解释：有的 SDK 是 dB，有的是原始码值）</summary>
        public double Gain { get; set; }

        /// <summary>触发模式</summary>
        public CameraTriggerMode TriggerMode { get; set; } = CameraTriggerMode.Software;

        /// <summary>硬件触发的相机 IO 线号（0~3，对应 Line0~Line3）；软触发/连续采图忽略</summary>
        public int TriggerSourceLine { get; set; }

        /// <summary>触发延时（微秒）；0 = 不延时</summary>
        public double TriggerDelayUs { get; set; }

        /// <summary>
        /// 等一帧的超时（毫秒）。默认 3000。
        /// <b>必须是有限值</b>：没有超时的等图意味着相机一旦不触发，流程线程就永久卡死，
        /// 现场只能杀进程——这是参考工程 WaitOne() 无超时留下的最严重缺陷。
        /// </summary>
        public int FrameTimeoutMs { get; set; } = 3000;

        /// <summary>
        /// 环形帧队列容量（超出时丢最旧并计入溢出）。默认 8。
        /// 太小易丢帧、太大会掩盖"产能不匹配"这个真问题并把延迟越积越大。
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
            TriggerSchemaVersion = TriggerSchemaVersion,
            ExposureTimeUs = ExposureTimeUs,
            Gain = Gain,
            TriggerMode = TriggerMode,
            TriggerSourceLine = TriggerSourceLine,
            TriggerDelayUs = TriggerDelayUs,
            FrameTimeoutMs = FrameTimeoutMs,
            BufferCapacity = BufferCapacity,
            HeartbeatTimeoutMs = HeartbeatTimeoutMs
        };

        /// <summary>把非法的容量 / 超时收敛到可用区间（配置可被人工编辑，必须有兜底）</summary>
        public CameraSettings Normalized()
        {
            var copy = Clone();
            if (copy.TriggerSchemaVersion < CurrentTriggerSchemaVersion)
            {
                // 旧配置：接通触发之前驱动一直按连续采图跑，升级后必须保持真实行为。
                copy.TriggerMode = CameraTriggerMode.Continuous;
                copy.TriggerSchemaVersion = CurrentTriggerSchemaVersion;
            }
            copy.TriggerSourceLine = Math.Clamp(copy.TriggerSourceLine, 0, 3);
            if (copy.TriggerDelayUs < 0) copy.TriggerDelayUs = 0;
            if (copy.BufferCapacity < 1) copy.BufferCapacity = 1;
            if (copy.BufferCapacity > 1024) copy.BufferCapacity = 1024;
            if (copy.FrameTimeoutMs < 1) copy.FrameTimeoutMs = 3000;
            if (copy.HeartbeatTimeoutMs < 200) copy.HeartbeatTimeoutMs = 3000;
            return copy;
        }
    }
}
