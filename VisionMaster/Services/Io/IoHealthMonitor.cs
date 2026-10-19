using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Core.Interfaces.Io;

namespace VisionMaster.Services.Io
{
    /// <summary>单条总线的健康快照（不可变值语义，可跨线程传给界面）。</summary>
    public sealed record IoBusHealth(
        string BusId,
        string DisplayName,
        string Transport,
        int DeviceCount,
        int OnlineCount,
        int FaultedCount,
        double AveragePollMs);

    /// <summary>单个设备的健康快照。</summary>
    public sealed record IoDeviceHealth(
        string DeviceId,
        string DisplayName,
        string DriverId,
        string BusId,
        IoDeviceState State,
        string StateDetail,
        DateTime LastSuccessTime,
        long WriteCount,
        long RejectedWriteCount,
        long FaultCount,
        int IsolatedPointCount,
        IReadOnlyList<string> IsolatedPoints);

    /// <summary>单个点位的健康快照。</summary>
    public sealed record IoPointHealth(
        string DeviceId,
        string PointId,
        string DisplayName,
        IoPointKind Kind,
        bool Enabled,
        IoQuality Quality,
        object? Value,
        DateTime LastUpdateTime);

    /// <summary>整个 IO 子系统的健康快照（一次拿到总线/设备/点位三层，界面按 1s 轮询即可）。</summary>
    public sealed record IoHealthSnapshot(
        DateTime Timestamp,
        IReadOnlyList<IoBusHealth> Buses,
        IReadOnlyList<IoDeviceHealth> Devices,
        IReadOnlyList<IoPointHealth> Points)
    {
        public int TotalDevices => Devices.Count;
        public int OnlineDevices => Devices.Count(d => d.State == IoDeviceState.Online);
        public int IsolatedPoints => Devices.Sum(d => d.IsolatedPointCount);
        public bool HasFault => Devices.Any(d => d.State is IoDeviceState.Faulted or IoDeviceState.Reconnecting);

        /// <summary>一行摘要（日志/状态栏用）。</summary>
        public string Summary =>
            $"总线 {Buses.Count} / 设备 {OnlineDevices}/{TotalDevices} 在线" +
            (IsolatedPoints > 0 ? $" / 隔离点 {IsolatedPoints}" : "") +
            (HasFault ? " / 有故障设备" : "");

        /// <summary>多行明细（诊断面板 / 日志导出用）。</summary>
        public string ToDetailText()
        {
            var sb = new StringBuilder();
            foreach (var device in Devices)
            {
                sb.AppendLine(
                    $"[{device.BusId}] {device.DeviceId}（{device.DriverId}）：{device.State}" +
                    (string.IsNullOrWhiteSpace(device.StateDetail) ? "" : $" — {device.StateDetail}") +
                    (device.IsolatedPointCount > 0
                        ? $"；隔离点：{string.Join("、", device.IsolatedPoints)}"
                        : ""));
            }
            return sb.ToString();
        }
    }

    /// <summary>IO 健康快照构建：从 provider 与变量目录聚合。</summary>
    public sealed class IoHealthMonitor
    {
        private readonly IoProvider _provider;
        private readonly Binding.IoVariableCatalog? _variables;

        public IoHealthMonitor(IoProvider provider, Binding.IoVariableCatalog? variables = null)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _variables = variables;
        }

        public IoHealthSnapshot Capture()
        {
            var buses = _provider.Buses;
            var devices = _provider.Devices;

            var busHealth = buses.Select(bus =>
            {
                var children = devices
                    .Where(d => string.Equals(d.Descriptor.BusId, bus.BusId, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // 实测轮询周期：取该总线上"有样本"的设备的滑动平均再平均。
                // 没有样本（刚启动 / 全部离线）时保持 0，界面显示"—"而不是假装 0ms。
                var samples = children
                    .OfType<IoDeviceBase>()
                    .Select(d => d.AveragePollMs)
                    .Where(ms => ms > 0)
                    .ToList();

                return new IoBusHealth(
                    bus.BusId,
                    bus.DisplayName,
                    bus.TransportKind.ToString(),
                    children.Count,
                    children.Count(d => d.State == IoDeviceState.Online),
                    children.Count(d => d.State == IoDeviceState.Faulted),
                    samples.Count == 0 ? 0 : Math.Round(samples.Average(), 2));
            }).ToList();

            var deviceHealth = devices.Select(device =>
            {
                var isolated = device is IoDeviceBase baseDevice
                    ? baseDevice.IsolatedPoints
                    : (IReadOnlyList<string>)Array.Empty<string>();
                return new IoDeviceHealth(
                    device.Descriptor.DeviceId,
                    device.Descriptor.DisplayName,
                    device.Descriptor.DriverId,
                    device.Descriptor.BusId,
                    device.State,
                    device.StateDetail,
                    device.LastSuccessTime,
                    device.WriteCount,
                    device.RejectedWriteCount,
                    device.FaultCount,
                    isolated.Count,
                    isolated.ToList());
            }).ToList();

            var pointHealth = new List<IoPointHealth>();
            if (_variables != null)
            {
                foreach (var variable in _variables.Variables)
                {
                    pointHealth.Add(new IoPointHealth(
                        variable.DeviceId,
                        variable.PointId,
                        variable.Point.DisplayName,
                        variable.Point.Kind,
                        variable.Point.Enabled,
                        ToIoQuality(variable.Quality),
                        variable.Value,
                        variable.LastUpdateTime));
                }
            }

            return new IoHealthSnapshot(DateTime.Now, busHealth, deviceHealth, pointHealth);
        }

        private static IoQuality ToIoQuality(VisionMaster.VariableQuality quality) => quality switch
        {
            VisionMaster.VariableQuality.Good => IoQuality.Good,
            VisionMaster.VariableQuality.Uncertain => IoQuality.Uncertain,
            VisionMaster.VariableQuality.Disabled => IoQuality.Disabled,
            _ => IoQuality.Bad
        };
    }
}
