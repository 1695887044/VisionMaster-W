using System;
using System.Collections.Generic;
using System.Linq;
using Core.Interfaces;

namespace VisionMaster.Services.Health
{
    /// <summary>相机健康行（不可变快照的 UI 投影来源）。</summary>
    public sealed record CameraHealthRow(
        Guid CameraId,
        string DisplayName,
        string SerialNo,
        string DriverText,
        string StateText,
        HealthLevel Level,
        string DetailText,
        string FrameRateText,
        string CountersText,
        string LastFrameText,
        bool HasOverflow)
    {
        public string LevelKey => HealthLevels.Key(Level);
        public string LevelText => HealthLevels.Text(Level);
    }

    /// <summary>相机健康快照。</summary>
    public sealed record CameraHealthSnapshot(DateTime Timestamp, IReadOnlyList<CameraHealthRow> Rows)
    {
        public int Total => Rows.Count;
        public int OkCount => Rows.Count(r => r.Level == HealthLevel.Ok);
        public int WarnCount => Rows.Count(r => r.Level == HealthLevel.Warn);
        public int ErrorCount => Rows.Count(r => r.Level == HealthLevel.Error);
        public int IdleCount => Rows.Count(r => r.Level == HealthLevel.Idle);
        public HealthLevel Level => HealthLevels.Aggregate(Rows.Select(r => r.Level));

        public string Summary => Total == 0
            ? "当前方案未配置相机"
            : $"相机 {OkCount}/{Total} 正常" +
              (WarnCount > 0 ? $" · {WarnCount} 警告" : "") +
              (ErrorCount > 0 ? $" · {ErrorCount} 故障" : "");
    }

    /// <summary>
    /// 相机健康采集器（只读）。
    ///
    /// <para><b>为什么只给"本采样周期收到帧"判 Ok</b>：契约没有 <c>LastFrameTime</c>，
    /// 单拍计数无法证明"此刻正在出图"；只有"两次采样之间计数确实增长"才是出图证据。
    /// 连接本身（<c>Streaming</c> 状态）不算——基类在调用驱动**之前**就把状态置成 Streaming。</para>
    ///
    /// <para><b>为什么不用 1 秒内计数不增长判故障</b>：低节拍/触发相机在一个采样周期内合法地
    /// 没有新帧；设备重建还会让计数归零。这些都只判 Warn，且文案明确写"不代表掉线"。</para>
    /// </summary>
    public sealed class CameraHealthMonitor
    {
        private readonly ICameraProvider _provider;
        private readonly Func<long> _clockMs;

        private sealed class Baseline
        {
            public ICameraDevice? Device;
            public long Received;
            public long Overflow;
            public long Dropped;
            public long TickMs;
        }

        private readonly Dictionary<Guid, Baseline> _baselines = new();
        private readonly object _gate = new();

        /// <param name="provider">相机仓库（宿主 <c>CameraProvider</c>）。</param>
        /// <param name="clockMs">单调时钟；测试可注入。</param>
        public CameraHealthMonitor(ICameraProvider provider, Func<long>? clockMs = null)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _clockMs = clockMs ?? (() => Environment.TickCount64);
        }

        /// <summary>采集一拍。只读，绝不调用 Open/Close/StartStream/ResetCounters。</summary>
        public CameraHealthSnapshot Capture()
        {
            var now = _clockMs();
            var rows = new List<CameraHealthRow>();

            foreach (var descriptor in _provider.Cameras)
            {
                if (descriptor == null) continue;
                rows.Add(CaptureOne(descriptor, now));
            }

            lock (_gate)
            {
                var alive = new HashSet<Guid>(rows.Select(r => r.CameraId));
                foreach (var id in _baselines.Keys.ToList())
                {
                    if (!alive.Contains(id)) _baselines.Remove(id);
                }
            }

            return new CameraHealthSnapshot(DateTime.Now, rows);
        }

        private CameraHealthRow CaptureOne(CameraDescriptor descriptor, long now)
        {
            string display = string.IsNullOrWhiteSpace(descriptor.DisplayName)
                ? descriptor.Caption
                : descriptor.DisplayName;
            string driver = string.IsNullOrWhiteSpace(descriptor.DriverTypeKey)
                ? "未选择驱动"
                : descriptor.DriverTypeKey;

            if (!_provider.TryGetDevice(descriptor.Id, out var device) || device == null)
            {
                return new CameraHealthRow(
                    descriptor.Id, display, descriptor.SerialNo, driver,
                    "未创建", HealthLevel.Error,
                    "运行态设备未创建：驱动插件未加载或不可用",
                    "—", "—", "—", false);
            }

            try
            {
                var state = device.State;
                string detail = device.StateDetail ?? string.Empty;
                long received = device.ReceivedFrameCount;
                long overflow = device.OverflowCount;
                long dropped = device.DroppedFrameCount;
                int pending = device.PendingFrameCount;

                var (delta, overflowDelta, droppedDelta, reset, hasBaseline) =
                    UpdateBaseline(descriptor.Id, device, received, overflow, dropped, now);

                var (level, stateText, note) = Classify(
                    state, detail, descriptor.AutoConnect, received, delta, reset, hasBaseline);

                if (overflowDelta > 0)
                {
                    note = Append(note, $"本周期队列溢出 +{overflowDelta} 帧（丢帧=漏检）");
                    if (level < HealthLevel.Warn) level = HealthLevel.Warn;
                }

                if (droppedDelta > 0)
                {
                    note = Append(note, $"本周期丢弃 +{droppedDelta} 帧");
                    if (level < HealthLevel.Warn) level = HealthLevel.Warn;
                }

                string detailText = string.IsNullOrWhiteSpace(note) ? detail : Append(detail, note);

                return new CameraHealthRow(
                    descriptor.Id, display, descriptor.SerialNo, driver,
                    stateText, level, detailText,
                    FrameRateText(delta, now, hasBaseline, state),
                    $"收 {received} · 待 {pending} · 溢 {overflow} · 丢 {dropped}",
                    LastFrameText(delta, hasBaseline, state),
                    overflow > 0);
            }
            catch (Exception ex)
            {
                return new CameraHealthRow(
                    descriptor.Id, display, descriptor.SerialNo, driver,
                    "读取失败", HealthLevel.Error,
                    $"读取设备状态失败：{ex.Message}",
                    "—", "—", "—", false);
            }
        }

        private (long? Delta, long OverflowDelta, long DroppedDelta, bool Reset, bool HasBaseline)
            UpdateBaseline(Guid id, ICameraDevice device, long received, long overflow, long dropped, long now)
        {
            lock (_gate)
            {
                if (!_baselines.TryGetValue(id, out var previous))
                {
                    _baselines[id] = new Baseline
                    {
                        Device = device, Received = received, Overflow = overflow,
                        Dropped = dropped, TickMs = now
                    };
                    return (null, 0, 0, false, false);
                }

                // 设备重建（引用变化）或计数倒退：本拍不产出增量，按"重置"处理。
                bool reset = !ReferenceEquals(previous.Device, device)
                             || received < previous.Received
                             || overflow < previous.Overflow
                             || dropped < previous.Dropped;

                long elapsed = now - previous.TickMs;
                // 间隔过短（手动连点刷新）或过长（面板关闭后重开）：不形成基线。
                bool usable = !reset && elapsed >= 200 && elapsed <= 5000;

                long overflowDelta = reset ? 0 : Math.Max(0, overflow - previous.Overflow);
                long droppedDelta = reset ? 0 : Math.Max(0, dropped - previous.Dropped);
                long? delta = usable ? Math.Max(0, received - previous.Received) : null;

                if (reset || elapsed > 5000)
                {
                    _baselines[id] = new Baseline
                    {
                        Device = device, Received = received, Overflow = overflow,
                        Dropped = dropped, TickMs = now
                    };
                    return (null, overflowDelta, droppedDelta, reset, false);
                }

                if (usable)
                {
                    previous.Received = received;
                    previous.Overflow = overflow;
                    previous.Dropped = dropped;
                    previous.TickMs = now;
                }

                return (delta, overflowDelta, droppedDelta, reset, usable);
            }
        }

        private static (HealthLevel Level, string StateText, string Note) Classify(
            CameraConnectionState state, string detail, bool autoConnect,
            long received, long? delta, bool reset, bool hasBaseline)
        {
            if (detail.StartsWith("打开失败", StringComparison.Ordinal))
                return (HealthLevel.Error, "打开失败", "");
            if (detail.Contains("启动采流失败", StringComparison.Ordinal))
                return (HealthLevel.Error, "启动失败", "");

            switch (state)
            {
                case CameraConnectionState.Connecting:
                    if (detail.Contains("未收到心跳", StringComparison.Ordinal)
                        || detail.Contains("等待设备恢复", StringComparison.Ordinal))
                        return (HealthLevel.Error, "已掉线", "");
                    return (HealthLevel.Warn, "连接中", "");

                case CameraConnectionState.Closed:
                    return autoConnect
                        ? (HealthLevel.Warn, "未连接", "已勾选自动连接，但当前未连接")
                        : (HealthLevel.Idle, "未连接", "未启用自动连接");

                case CameraConnectionState.Online:
                    return autoConnect
                        ? (HealthLevel.Warn, "在线未采流", "已勾选自动连接，请检查是否启动采流失败")
                        : (HealthLevel.Idle, "在线未采流", "在线，未开始采流");

                case CameraConnectionState.Streaming:
                    if (reset) return (HealthLevel.Warn, "采流中", "设备已重建，计数归零，正在重建基线");
                    if (received == 0) return (HealthLevel.Warn, "采流中", "已请求采流，尚未收到帧（可能未触发）");
                    if (!hasBaseline) return (HealthLevel.Idle, "采流中", $"累计收到 {received} 帧（本拍未形成基线）");
                    if (delta is > 0) return (HealthLevel.Ok, "采流中", $"本采样周期收到 {delta} 帧");
                    return (HealthLevel.Warn, "采流中", "本采样周期未收到新帧（可能未触发或已停拍；不代表掉线）");

                default:
                    return (HealthLevel.Warn, "未知", $"未知状态：{state}");
            }
        }

        private static string FrameRateText(long? delta, long now, bool hasBaseline, CameraConnectionState state)
        {
            // 没有设备时间戳：这里只是"本采样周期的平均收帧速率"，不是相机曝光帧率。
            if (!hasBaseline || delta == null || state != CameraConnectionState.Streaming) return "—";
            return $"{delta.Value:F0}/拍";
        }

        private static string LastFrameText(long? delta, bool hasBaseline, CameraConnectionState state)
        {
            if (state != CameraConnectionState.Streaming) return "—";
            if (!hasBaseline || delta == null) return "—";
            return delta.Value > 0 ? $"本周期 +{delta}" : "本周期 0";
        }

        private static string Append(string head, string tail)
            => string.IsNullOrWhiteSpace(head) ? tail : head.TrimEnd('；', ' ') + "；" + tail;
    }
}
