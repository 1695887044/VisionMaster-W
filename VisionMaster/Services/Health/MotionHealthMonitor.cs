using System;
using System.Collections.Generic;
using System.Linq;
using Core.Interfaces;

namespace VisionMaster.Services.Health
{
    /// <summary>运动卡健康行（卡级一行；轴级只做汇总）。</summary>
    public sealed record MotionHealthRow(
        Guid CardId,
        string Caption,
        string DriverText,
        string AddressText,
        string StateText,
        HealthLevel Level,
        string DetailText,
        string AxisText,
        string HomingText,
        string LastFaultText,
        string CountersText,
        int AbnormalAxisCount)
    {
        public string LevelKey => HealthLevels.Key(Level);
        public string LevelText => HealthLevels.Text(Level);
    }

    /// <summary>运动健康快照。</summary>
    public sealed record MotionHealthSnapshot(DateTime Timestamp, IReadOnlyList<MotionHealthRow> Rows)
    {
        public int Total => Rows.Count;
        public int OkCount => Rows.Count(r => r.Level == HealthLevel.Ok);
        public int WarnCount => Rows.Count(r => r.Level == HealthLevel.Warn);
        public int ErrorCount => Rows.Count(r => r.Level == HealthLevel.Error);
        public int IdleCount => Rows.Count(r => r.Level == HealthLevel.Idle);
        public HealthLevel Level => HealthLevels.Aggregate(Rows.Select(r => r.Level));

        public string Summary => Total == 0
            ? "当前方案未配置运动卡"
            : $"运动卡 {OkCount}/{Total} 正常" +
              (WarnCount > 0 ? $" · {WarnCount} 警告" : "") +
              (ErrorCount > 0 ? $" · {ErrorCount} 故障" : "");
    }

    /// <summary>
    /// 运动卡健康采集器（只读）。
    ///
    /// <para><b>判级只看当前可观测状态</b>（<c>State</c> + 轴快照），<b>不看 <c>LastFault</c>、
    /// 不看累计计数</b>。理由：<c>LastFault</c> 在 <c>MotionDeviceBase</c> 里全仓没有清空点，
    /// 而且"命令被拒绝"也会写它——拿它判级会让修好的卡永久红灯，且真正的伺服报警会被一次
    /// 软限位拒绝顶掉。<c>LastFault</c> 只作为"最近故障（历史）"展示。</para>
    ///
    /// <para><b><c>IsHomed</c> 是卡级</b>：任一轴回零成功即为 true，界面必须写"已回零（卡级）"。</para>
    /// </summary>
    public sealed class MotionHealthMonitor
    {
        private readonly IMotionProvider _provider;

        public MotionHealthMonitor(IMotionProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        /// <summary>采集一拍。只读，绝不调用 Connect/Enqueue/EmergencyStop/ClearAlarm/ResetCounters。</summary>
        public MotionHealthSnapshot Capture()
        {
            var rows = new List<MotionHealthRow>();
            foreach (var descriptor in _provider.Cards)
            {
                if (descriptor == null) continue;
                rows.Add(CaptureOne(descriptor));
            }
            return new MotionHealthSnapshot(DateTime.Now, rows);
        }

        private MotionHealthRow CaptureOne(MotionDescriptor descriptor)
        {
            string caption = string.IsNullOrWhiteSpace(descriptor.DisplayName)
                ? descriptor.Caption
                : descriptor.DisplayName;
            string driver = string.IsNullOrWhiteSpace(descriptor.DriverTypeKey)
                ? "未选择驱动"
                : descriptor.DriverTypeKey;
            string address = string.IsNullOrWhiteSpace(descriptor.Address) ? "—" : descriptor.Address;

            if (!_provider.TryGetDevice(descriptor.Id, out var device) || device == null)
            {
                return new MotionHealthRow(
                    descriptor.Id, caption, driver, address,
                    "无运行实例", HealthLevel.Error,
                    "已配置但未创建设备（驱动插件未加载或创建失败，详见日志）",
                    "—", "—", "—", "—", 0);
            }

            try
            {
                var state = device.State;
                string detail = device.StateDetail ?? string.Empty;
                bool live = state is MotionCardState.Online or MotionCardState.Alarm;

                var axes = descriptor.Axes ?? new List<AxisMapping>();
                int enabledAxes = axes.Count(a => a.Enabled);
                int capabilityAxes = device.Capabilities.AxisCount;
                int abnormal = 0;
                string firstAbnormal = string.Empty;

                if (live)
                {
                    foreach (var axis in axes)
                    {
                        if (!axis.Enabled) continue;
                        var status = device.GetAxisStatus(axis.PhysicalIndex);
                        if (status == null || !status.HasCriticalSignal) continue;
                        abnormal++;
                        if (firstAbnormal.Length == 0)
                            firstAbnormal = $"{axis.LogicalName} {ReasonOf(status)}";
                    }
                }

                var (level, stateText, note) = Classify(
                    state, detail, descriptor.AutoConnect, device.IsHomed,
                    enabledAxes, capabilityAxes, abnormal);

                string detailText = string.IsNullOrWhiteSpace(note) ? detail : Append(detail, note);

                string axisText = capabilityAxes > 0
                    ? $"启用 {enabledAxes}/{axes.Count} · 能力 {capabilityAxes}"
                    : $"启用 {enabledAxes}/{axes.Count}";
                if (abnormal > 0)
                    axisText += $" · 异常 {abnormal}（{firstAbnormal}）";

                return new MotionHealthRow(
                    descriptor.Id, caption, driver, address,
                    stateText, level, detailText,
                    axisText,
                    HomingText(device.IsHomed, live, capabilityAxes, device.Capabilities.SupportsAbsoluteEncoder),
                    LastFaultText(device.LastFault),
                    $"排队 {device.PendingCommandCount} · 执行 {device.ExecutedCommandCount} · " +
                    $"拒 {device.RejectedCommandCount} · 故障 {device.FaultCount}",
                    abnormal);
            }
            catch (Exception ex)
            {
                return new MotionHealthRow(
                    descriptor.Id, caption, driver, address,
                    "读取失败", HealthLevel.Error,
                    $"读取设备状态失败：{ex.Message}",
                    "—", "—", "—", "—", 0);
            }
        }

        private static (HealthLevel Level, string StateText, string Note) Classify(
            MotionCardState state, string detail, bool autoConnect, bool isHomed,
            int enabledAxes, int capabilityAxes, int abnormalAxes)
        {
            switch (state)
            {
                case MotionCardState.SafeStopped:
                    // 与"离线"是两件事：卡还连着，但拒绝一切命令，必须先断开再连接。
                    return (HealthLevel.Error, "已安全停机", "需先断开再连接；增量式编码器重连后必须重新回零");

                case MotionCardState.Alarm:
                    return (HealthLevel.Error, "报警", "通信正常，仅接受「清除报警」");

                case MotionCardState.Online:
                    if (abnormalAxes > 0)
                        return (HealthLevel.Error, "在线 · 轴异常", "轴快照出现急停/限位/报警");
                    if (!isHomed && enabledAxes > 0 && capabilityAxes > 0)
                        return (HealthLevel.Warn, "在线 · 未回零", "增量式位置不可信，定位/点动被门禁拒绝");
                    return (HealthLevel.Ok, "在线", "");

                case MotionCardState.Connecting:
                    return (HealthLevel.Warn, "连接中", "");

                case MotionCardState.Closed:
                    if (detail.StartsWith("连接失败", StringComparison.Ordinal))
                        return (HealthLevel.Warn, "未连接", "连接失败，详见状态说明");
                    return autoConnect
                        ? (HealthLevel.Warn, "未连接", "已勾选自动连接，但当前未连接")
                        : (HealthLevel.Idle, "未连接", "未启用自动连接");

                default:
                    return (HealthLevel.Warn, "未知", $"未知状态：{state}");
            }
        }

        private static string HomingText(bool isHomed, bool live, int capabilityAxes, bool absoluteEncoder)
        {
            if (!live || capabilityAxes == 0) return "—";
            if (isHomed)
                return absoluteEncoder ? "位置可信（绝对式）" : "已回零（卡级）";
            return absoluteEncoder ? "未回零（绝对式异常）" : "未回零";
        }

        private static string LastFaultText(MotionFault? fault)
        {
            if (fault == null) return "—";
            string severity = fault.Severity switch
            {
                MotionFaultSeverity.Notice => "提示",
                MotionFaultSeverity.Recoverable => "可恢复",
                MotionFaultSeverity.RequiresReset => "需复位",
                MotionFaultSeverity.RequiresService => "需维修",
                _ => fault.Severity.ToString()
            };
            string axis = string.IsNullOrWhiteSpace(fault.Axis) ? "" : $"（{fault.Axis}）";
            return $"{severity} · {fault.Message}{axis}（历史）";
        }

        /// <summary>轴异常原因：优先报警消息，其次急停/限位。</summary>
        private static string ReasonOf(AxisStatus status)
        {
            if (status.Alarm && !string.IsNullOrWhiteSpace(status.AlarmMessage)) return status.AlarmMessage;
            if (status.EmergencyStop) return "急停";
            if (status.PositiveLimit) return "正限位";
            if (status.NegativeLimit) return "负限位";
            return "报警";
        }

        private static string Append(string head, string tail)
            => string.IsNullOrWhiteSpace(head) ? tail : head.TrimEnd('；', ' ') + "；" + tail;
    }
}
