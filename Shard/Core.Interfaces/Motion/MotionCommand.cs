using System;
using System.Threading;

namespace Core.Interfaces
{
    /// <summary>
    /// 一条运动命令。
    ///
    /// 设计要点（这几条决定了"运动控制好不好用"）：
    ///
    /// 1. <b>命令与执行分离</b>。<see cref="IMotionDevice.Enqueue"/> 只是"收下"，
    ///    真正的执行发生在卡的命令线程上。运动是异步的：下发 ≠ 到位。
    ///    流程里必须用独立的"等待到位"步骤去等，否则会出现"两条轴同时下发、实际串行执行"。
    ///
    /// 2. <b>单位一律是毫米（mm）</b>。脉冲/角度换算在驱动层按 <see cref="AxisMapping.UnitsPerMm"/> 做，
    ///    命令对象里不出现"脉冲"这个词 —— 否则每个步骤都要问"这张卡是脉冲还是毫米"。
    ///
    /// 3. <b>超时是命令自己的属性</b>，不是全局配置。回零可能要几十秒、Move 可能 200ms，
    ///    用同一个超时值等于要么频繁误判、要么卡死时长时间无响应。
    ///
    /// 4. <b>Retryable 是个显式标记</b>。通信类失败（写寄存器失败）重试是安全的；
    ///    但回零重试可能撞机械原点、Stop 重试可能把已经在停的轴再停一次 —— 这类必须标 false。
    /// </summary>
    public sealed class MotionCommand : IDisposable
    {
        /// <summary>命令身份（日志/界面追踪一条命令从入队到完成的全程）</summary>
        public Guid CommandId { get; } = Guid.NewGuid();

        /// <summary>命令种类</summary>
        public MotionCommandKind Kind { get; init; }

        /// <summary>目标物理轴号（轴级命令用；IO 命令忽略）</summary>
        public int PhysicalAxis { get; init; } = -1;

        /// <summary>逻辑轴名（只用于日志与界面显示，执行用 PhysicalAxis）</summary>
        public string LogicalAxis { get; init; } = string.Empty;

        /// <summary>目标位置（mm）。绝对运动=目标点；相对运动=增量（可为负）</summary>
        public double TargetMm { get; init; }

        /// <summary>速度（mm/s；&lt;=0 表示用卡级默认值）</summary>
        public double VelocityMmPerS { get; init; }

        /// <summary>加速度（mm/s²；&lt;=0 表示用卡级默认值）</summary>
        public double AccelMmPerS2 { get; init; }

        /// <summary>回零方式（仅 Home 命令有效）</summary>
        public HomeMode HomeMode { get; init; } = HomeMode.NegativeLimitIndex;

        /// <summary>
        /// 停止方式（仅 Stop 命令有效）：2 = 减速停，3 = 立即停（正运动 SDK 的约定值）。
        /// 默认减速停 —— 立即停对机械冲击很大，只在急停/限位这类场合才用。
        /// </summary>
        public int StopMode { get; init; } = 2;

        /// <summary>IO 点号（仅 SetOutput 有效）</summary>
        public int IoPort { get; init; } = -1;

        /// <summary>IO 值（仅 SetOutput 有效）</summary>
        public bool IoValue { get; init; }

        /// <summary>点动方向（仅 Jog 有效）：+1 正方向，-1 负方向</summary>
        public int JogDirection { get; init; } = 1;

        /// <summary>本命令的超时（&lt;=0 表示用卡级默认值）</summary>
        public TimeSpan Timeout { get; init; }

        /// <summary>失败后是否允许重试（见类型注释第 4 条：回零/停止必须 false）</summary>
        public bool Retryable { get; init; } = true;

        /// <summary>是否为"等它做完再往下"的命令（回零是；Move 不是 —— Move 下发即完成）</summary>
        public bool WaitsForCompletion { get; init; }

        // ── 以下为运行态，由 MotionDeviceBase 在自己程序集内维护 ──

        /// <summary>生命周期状态（由基类的命令线程更新）</summary>
        public MotionCommandState State { get; internal set; } = MotionCommandState.Queued;

        /// <summary>失败/超时原因（成功时为空串）</summary>
        public string Error { get; internal set; } = string.Empty;

        /// <summary>入队时刻</summary>
        public DateTime EnqueuedAt { get; } = DateTime.Now;

        /// <summary>完成时刻（未完成时为 default）</summary>
        public DateTime CompletedAt { get; internal set; }

        /// <summary>
        /// 命令完成信号：无论 Done / Failed / TimedOut / Canceled，结束时都会被 Set。
        ///
        /// 存在的意义：流程里的"等待到位"步骤需要一个统一的等待原语。
        /// 没有它，步骤只能自己写轮询循环（既费 CPU，又容易把超时与取消判断写错）。
        /// 由持有者负责 Dispose（它内部带 WaitHandle）。
        /// </summary>
        public ManualResetEventSlim Completion { get; } = new(false);

        /// <summary>一句话描述（日志用：谁在什么时候让哪个轴去哪）</summary>
        public string Describe()
        {
            var axis = string.IsNullOrEmpty(LogicalAxis) ? $"轴{PhysicalAxis}" : LogicalAxis;
            return Kind switch
            {
                MotionCommandKind.MoveAbsolute => $"{axis} 绝对运动 → {TargetMm:F3} mm @ {VelocityMmPerS:F1} mm/s",
                MotionCommandKind.MoveRelative => $"{axis} 相对运动 {TargetMm:+0.###;-0.###} mm @ {VelocityMmPerS:F1} mm/s",
                MotionCommandKind.Home => $"{axis} 回零（{HomeMode}）",
                MotionCommandKind.Stop => $"{axis} 停止（模式 {StopMode}）",
                MotionCommandKind.Enable => $"{axis} 使能",
                MotionCommandKind.Disable => $"{axis} 失能",
                MotionCommandKind.SetOutput => $"输出 {IoPort} ← {(IoValue ? "ON" : "OFF")}",
                MotionCommandKind.ClearAlarm => "清除报警",
                MotionCommandKind.Jog => $"{axis} 点动（{(JogDirection >= 0 ? "正方向" : "负方向")}）",
                _ => Kind.ToString(),
            };
        }

        /// <summary>释放完成信号（命令被丢弃/步骤释放时调用；重复调用安全）</summary>
        public void Dispose()
        {
            try { Completion.Dispose(); } catch { /* 释放路径上不再抛 */ }
        }
    }
}
