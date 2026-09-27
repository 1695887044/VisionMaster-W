using System;

namespace Core.Interfaces
{
    /// <summary>
    /// 一次运动故障的完整描述（分级 + 厂商错误码 + 可读原因 + 处理建议 + 发生轴）。
    ///
    /// 为什么要有 <see cref="Suggestion"/>：这是现场最缺、也最省售后成本的一个字段。
    /// 厂商 SDK 只给一个数字错误码（正运动是 int、雷赛是另一套），
    /// 现场工程师拿到"-7"什么也做不了，只能拍照打电话。
    /// 驱动把"码 → 人话 + 该怎么办"的映射做掉，现场能自己处理掉一大半。
    ///
    /// 为什么要有 <see cref="Severity"/>：见 <see cref="MotionFaultSeverity"/> 的注释 ——
    /// "复位一下就行"与"叫维修"必须让使用者分得清。
    /// </summary>
    public sealed class MotionFault
    {
        /// <summary>发生时刻</summary>
        public DateTime Time { get; init; } = DateTime.Now;

        /// <summary>严重度（决定自动恢复 / 需复位 / 需维修）</summary>
        public MotionFaultSeverity Severity { get; init; } = MotionFaultSeverity.Recoverable;

        /// <summary>厂商错误码（0 表示"不是 SDK 返回的码，而是宿主自己判定的"，如看门狗超时）</summary>
        public int Code { get; init; }

        /// <summary>可读原因（中文，直接进日志与界面）</summary>
        public string Message { get; init; } = string.Empty;

        /// <summary>处理建议（中文；能给就给，现场少打一个电话）</summary>
        public string Suggestion { get; init; } = string.Empty;

        /// <summary>相关轴（逻辑名；非轴级故障为空）</summary>
        public string Axis { get; init; } = string.Empty;

        /// <summary>相关命令（追踪"哪一条命令引发了它"；非命令触发的为空）</summary>
        public Guid? CommandId { get; init; }

        /// <summary>是否会自动恢复（界面据此决定提示措辞：等待重连 / 请手动复位）</summary>
        public bool IsAutoRecoverable => Severity == MotionFaultSeverity.Recoverable;

        /// <summary>拼成一行日志文案</summary>
        public override string ToString()
        {
            var parts = $"[{Severity}] {Message}";
            if (Code != 0) parts += $"（错误码 {Code}）";
            if (!string.IsNullOrEmpty(Axis)) parts += $"，轴 {Axis}";
            if (!string.IsNullOrEmpty(Suggestion)) parts += $"；建议：{Suggestion}";
            return parts;
        }
    }
}
