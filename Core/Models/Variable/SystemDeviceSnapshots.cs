using System;

namespace VisionMaster.Models
{
    /// <summary>
    /// 运动轴只读快照：**所有厂家、所有协议统一的一份结构**。
    ///
    /// 为什么要有它（而不是继续把 12 个字段拆成 12 个平级变量）
    /// ---------
    /// 现场对"轴"的心智模型是一个整体（EtherCAT 的 REF 402 Axis 就是这种形态：
    /// 不管伺服是谁家的，状态一律从同一套对象里读）。系统变量若只产 12 条
    /// <c>System.Motion.X.Position</c> 这样的平级行，注册表里就永远没有
    /// "X 这根轴"这个实体，消费方也拿不到整根轴。
    ///
    /// 本类型是 <c>System.Motion.{轴名}</c> 的**声明类型**与当前值：
    /// 字段由宿主归一（<c>MotionStatePublisher</c> 已经把各驱动的 AxisStatus 归一成
    /// <c>ScadaAxisSample</c>），驱动插件不允许自定义字段——这正是"统一结构"的含义。
    /// </summary>
    public sealed record MotionAxis(
        Guid AxisId,
        string AxisName,
        string CardName,
        bool IsValid,
        string? Error,
        double PositionMm,
        double VelocityMmPerS,
        bool Moving,
        bool InPosition,
        bool Enabled,
        bool Alarm,
        string AlarmMessage,
        bool PositiveLimit,
        bool NegativeLimit,
        bool Origin,
        bool EmergencyStop,
        string StatusText,
        DateTime TimestampUtc)
    {
        /// <summary>变量管理「当前值」列的摘要（完整字段在各子行里）</summary>
        public override string ToString()
            => IsValid
                ? $"{AxisName} · {PositionMm:0.###} mm · {StatusText}"
                : $"{AxisName} · 状态未知（{Error ?? "运动卡未连接"}）";

        /// <summary>
        /// 差分推送口径：**时间戳不参与相等判断**。
        ///
        /// 父变量每 100ms 写一拍，时间戳必然不同；若让它参与 Equals，父变量就会每拍
        /// 发一次 PropertyChanged/ValueChanged——与 <c>SystemVariableModel.UpdateMirrorValue</c>
        /// "只有值真的变了才通知"的约定相反（一屏几十个绑定会被白白刷满）。
        /// 时间戳仍保留在快照里，供"这一帧多旧"的诊断使用。
        /// </summary>
        public bool Equals(MotionAxis? other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other is null) return false;

            return AxisId == other.AxisId
                && string.Equals(AxisName, other.AxisName, StringComparison.Ordinal)
                && string.Equals(CardName, other.CardName, StringComparison.Ordinal)
                && IsValid == other.IsValid
                && string.Equals(Error, other.Error, StringComparison.Ordinal)
                && PositionMm.Equals(other.PositionMm)
                && VelocityMmPerS.Equals(other.VelocityMmPerS)
                && Moving == other.Moving
                && InPosition == other.InPosition
                && Enabled == other.Enabled
                && Alarm == other.Alarm
                && string.Equals(AlarmMessage, other.AlarmMessage, StringComparison.Ordinal)
                && PositiveLimit == other.PositiveLimit
                && NegativeLimit == other.NegativeLimit
                && Origin == other.Origin
                && EmergencyStop == other.EmergencyStop
                && string.Equals(StatusText, other.StatusText, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(AxisId);
            hash.Add(AxisName);
            hash.Add(CardName);
            hash.Add(IsValid);
            hash.Add(Error);
            hash.Add(PositionMm);
            hash.Add(VelocityMmPerS);
            hash.Add(Moving);
            hash.Add(InPosition);
            hash.Add(Enabled);
            hash.Add(Alarm);
            hash.Add(AlarmMessage);
            hash.Add(PositiveLimit);
            hash.Add(NegativeLimit);
            hash.Add(Origin);
            hash.Add(EmergencyStop);
            hash.Add(StatusText);
            return hash.ToHashCode();
        }
    }

    /// <summary>
    /// 通讯设备只读快照：Modbus / S7 / MC / FINS 一律同一结构。
    ///
    /// 字段来源是 <c>CommunicationConfig</c> 的运行态（State / LastError）与链路状态
    /// （<c>ICommunicationConnection.IsConnected</c>），不掺任何协议私有概念。
    /// </summary>
    public sealed record CommDevice(
        string Name,
        string Protocol,
        bool Connected,
        string State,
        string StateText,
        string LastError,
        bool Enabled,
        int ReadCycleMs)
    {
        public override string ToString()
            => Connected
                ? $"{Name} · {StateText}"
                : $"{Name} · {StateText}{(string.IsNullOrWhiteSpace(LastError) ? string.Empty : "（" + LastError + "）")}";
    }

    /// <summary>
    /// 相机只读快照：各品牌相机统一结构。
    ///
    /// 计数来自 <c>ICameraDevice</c>（收帧/丢帧/溢出/待处理），状态来自
    /// <c>CameraConnectionState</c>；<see cref="Online"/> 是"已连接或已在采流"的布尔投影，
    /// 让流程判断不必去认枚举成员名。
    /// </summary>
    public sealed record CameraDevice(
        Guid CameraId,
        string Name,
        string SerialNo,
        bool Online,
        string State,
        string StateText,
        string StateDetail,
        long ReceivedFrameCount,
        long DroppedFrameCount,
        long OverflowCount,
        int PendingFrameCount)
    {
        public override string ToString()
            => $"{Name} · {StateText} · 收帧 {ReceivedFrameCount}";
    }

    /// <summary>
    /// 复合系统变量：父变量本身是一个 <see cref="IVariable"/>（可整块绑定/监视），
    /// 同时按字段展开成若干子变量（各自有确定性 Id，可单独绑定）。
    ///
    /// 为什么放在模型层而不是只做界面分组：用户要求"每个轴输出一个 Axis 类型的属性"，
    /// 而"属性"必须是注册表里可解析的实体，界面分组满足不了这一点。
    /// </summary>
    public interface ISystemVariableContainer
    {
        /// <summary>子字段（只读、有序）。空集合表示这是普通叶子变量。</summary>
        System.Collections.Generic.IReadOnlyList<IVariable> Fields { get; }
    }
}
