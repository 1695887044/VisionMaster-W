using System;
using System.ComponentModel.DataAnnotations;

namespace Core.Interfaces.Io
{
    /// <summary>IO 点位类型。P1 只支持数字量/模拟量输入输出，不包含高速计数/脉冲。</summary>
    public enum IoPointKind
    {
        [Display(Name = "数字输入")] DigitalInput,
        [Display(Name = "数字输出")] DigitalOutput,
        [Display(Name = "模拟输入")] AnalogInput,
        [Display(Name = "模拟输出")] AnalogOutput
    }

    public enum IoAccess
    {
        [Display(Name = "只读")] ReadOnly,
        [Display(Name = "只写")] WriteOnly,
        [Display(Name = "读写")] ReadWrite
    }

    public enum IoDataType
    {
        [Display(Name = "布尔")] Bool,
        [Display(Name = "16 位整数")] Int16,
        [Display(Name = "16 位无符号")] UInt16,
        [Display(Name = "32 位整数")] Int32,
        [Display(Name = "32 位无符号")] UInt32,
        [Display(Name = "浮点")] Float,
        [Display(Name = "双精度")] Double
    }

    /// <summary>点位质量。Disabled 与 Bad 分开：禁用是设计状态，不是故障。</summary>
    public enum IoQuality
    {
        [Display(Name = "良好")] Good,
        [Display(Name = "不确定")] Uncertain,
        [Display(Name = "故障")] Bad,
        [Display(Name = "禁用")] Disabled
    }

    public enum IoDeviceState
    {
        [Display(Name = "未连接")] Closed,
        [Display(Name = "连接中")] Connecting,
        [Display(Name = "在线")] Online,
        [Display(Name = "降级")] Degraded,
        [Display(Name = "重连中")] Reconnecting,
        [Display(Name = "故障")] Faulted,
        [Display(Name = "禁用")] Disabled
    }

    public enum IoBusState
    {
        [Display(Name = "已关闭")] Closed,
        [Display(Name = "打开中")] Opening,
        [Display(Name = "已打开")] Open,
        [Display(Name = "故障")] Faulted
    }

    public enum IoFailSafe
    {
        [Display(Name = "保持")] Hold,
        [Display(Name = "断开")] Off,
        [Display(Name = "闭合")] On,
        [Display(Name = "安全值")] Value
    }

    public enum IoTransportKind
    {
        [Display(Name = "TCP")] Tcp,
        [Display(Name = "串口")] Serial
    }

    public enum IoParity
    {
        [Display(Name = "无")] None,
        [Display(Name = "奇校验")] Odd,
        [Display(Name = "偶校验")] Even
    }

    public enum IoStopBits
    {
        [Display(Name = "1")] One,
        [Display(Name = "1.5")] OnePointFive,
        [Display(Name = "2")] Two
    }

    /// <summary>多寄存器数值的字序。16 位及以下时 ABCD 与 CDAB 等价。</summary>
    public enum IoByteOrder
    {
        [Display(Name = "ABCD")] ABCD,
        [Display(Name = "BADC")] BADC,
        [Display(Name = "CDAB")] CDAB,
        [Display(Name = "DCBA")] DCBA
    }

    public enum IoWriteOutcome
    {
        [Display(Name = "已接收")] Accepted,
        [Display(Name = "已执行")] Executed,
        [Display(Name = "未执行")] TimedOutNotExecuted,
        [Display(Name = "可能已执行")] TimedOutMayHaveExecuted,
        [Display(Name = "拒绝")] Rejected
    }

    [Flags]
    public enum IoCapabilities
    {
        None = 0,
        DigitalInput = 1 << 0,
        DigitalOutput = 1 << 1,
        AnalogInput = 1 << 2,
        AnalogOutput = 1 << 3,
        BatchRead = 1 << 4,
        Readback = 1 << 5
    }

    /// <summary>
    /// 一个点位的值。Numeric 是工程量（缩放后），Raw 是驱动读到的原始值。
    /// 数字量用 Boolean；模拟量用 Numeric/Raw。
    /// </summary>
    public readonly record struct IoPointValue(
        IoDataType Type,
        double Numeric,
        long Raw,
        bool Boolean,
        IoQuality Quality,
        DateTime Timestamp)
    {
        public static IoPointValue Bad(IoDataType type) =>
            new(type, 0, 0, false, IoQuality.Bad, default);

        public static IoPointValue Disabled(IoDataType type) =>
            new(type, 0, 0, false, IoQuality.Disabled, default);

        public static IoPointValue FromBool(bool value) =>
            new(IoDataType.Bool, value ? 1 : 0, value ? 1 : 0, value, IoQuality.Good, DateTime.Now);

        public static IoPointValue FromRaw(IoDataType type, long raw, double numeric) =>
            new(type, numeric, raw, raw != 0, IoQuality.Good, DateTime.Now);
    }

    public readonly record struct IoPointReadResult(
        string PointId,
        bool Success,
        IoPointValue Value,
        string? Error)
    {
        public static IoPointReadResult Ok(string pointId, IoPointValue value) =>
            new(pointId, true, value, null);

        public static IoPointReadResult Fail(string pointId, string? error) =>
            new(pointId, false, IoPointValue.Bad(IoDataType.Bool), error);
    }

    public readonly record struct IoWriteRequest(string PointId, IoPointValue Value, string? OwnerId = null);

    public sealed record IoFault(
        DateTime Time,
        string Kind,
        string Code,
        string Message,
        string? PointId = null);

    public readonly struct IoWriteResult
    {
        public IoWriteResult(bool accepted, IoWriteOutcome outcome, string? error, long sequence, Task completion)
        {
            Accepted = accepted;
            Outcome = outcome;
            Error = error;
            Sequence = sequence;
            Completion = completion ?? Task.CompletedTask;
        }

        public bool Accepted { get; }
        public IoWriteOutcome Outcome { get; }
        public string? Error { get; }
        public long Sequence { get; }
        public Task Completion { get; }

        public static IoWriteResult Reject(string error) =>
            new(false, IoWriteOutcome.Rejected, error, 0, Task.CompletedTask);

        public static IoWriteResult Accept(long sequence, Task completion) =>
            new(true, IoWriteOutcome.Accepted, null, sequence, completion);
    }

    public sealed class IoPointsChangedEventArgs : EventArgs
    {
        public IoPointsChangedEventArgs(IReadOnlyList<IoPointReadResult> changes)
            => Changes = changes ?? Array.Empty<IoPointReadResult>();

        public IReadOnlyList<IoPointReadResult> Changes { get; }
    }
}
