using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Core.Interfaces;
using VisionMaster.Binding;

namespace ScadaChecks
{
    /// <summary>
    /// 通讯系统变量目录的假状态源：只装一张可改的表，不碰任何 socket。
    /// </summary>
    internal sealed class FakeCommDeviceStateSource : ICommDeviceStateSource
    {
        public List<CommDeviceState> Devices { get; } = new();

        public IReadOnlyList<CommDeviceState> Snapshot() => Devices.ToList();
    }

    /// <summary>
    /// 相机仓库的假实现：按 Id 登记"描述符 + 设备"。
    /// 移除设备（模拟驱动未加载）时只摘设备、保留描述符。
    /// </summary>
    internal sealed class FakeCameraProvider : ICameraProvider
    {
        private readonly List<CameraDescriptor> _descriptors = new();
        private readonly Dictionary<Guid, ICameraDevice> _devices = new();

        public IReadOnlyList<CameraDescriptor> Cameras => _descriptors.ToList();

        public void Add(CameraDescriptor descriptor, ICameraDevice? device)
        {
            _descriptors.Add(descriptor);
            if (device != null)
                _devices[descriptor.Id] = device;
        }

        public void RemoveDevice(Guid cameraId) => _devices.Remove(cameraId);

        public bool TryGetDevice(Guid cameraId, out ICameraDevice device)
        {
            if (_devices.TryGetValue(cameraId, out var found))
            {
                device = found;
                return true;
            }

            device = null!;
            return false;
        }

        public bool TryGetDeviceBySerial(string serialNo, out ICameraDevice device)
        {
            var descriptor = _descriptors.FirstOrDefault(
                d => string.Equals(d.SerialNo, serialNo, StringComparison.OrdinalIgnoreCase));

            if (descriptor != null && _devices.TryGetValue(descriptor.Id, out var found))
            {
                device = found;
                return true;
            }

            device = null!;
            return false;
        }
    }

    /// <summary>
    /// 相机设备的假实现：只有状态与计数，采集相关成员一律返回"没有"。
    /// </summary>
    internal sealed class FakeCameraDevice : ICameraDevice
    {
        public FakeCameraDevice(CameraDescriptor descriptor)
        {
            Descriptor = descriptor;
        }

        public CameraDescriptor Descriptor { get; }
        public CameraConnectionState State { get; set; } = CameraConnectionState.Closed;
        public string StateDetail { get; set; } = string.Empty;
        public long OverflowCount { get; set; }
        public long DroppedFrameCount { get; set; }
        public long ReceivedFrameCount { get; set; }
        public int PendingFrameCount { get; set; }

        public event EventHandler? StateChanged;

        public bool Open() => true;

        public void Close() { }

        public bool StartStream() => true;

        public void StopStream() { }

        public bool ApplySettings(CameraSettings settings) => true;

        public CameraSettings ReadSettings() => Descriptor.Settings;

        public void NotifyAlive() { }

        public bool PushFrame(CameraFrame frame, out string error)
        {
            error = "假设备不接帧";
            return false;
        }

        public bool TryGetLatest(out CameraFrame frame)
        {
            frame = null!;
            return false;
        }

        public bool WaitNextFrame(out CameraFrame frame, int timeoutMs, CancellationToken ct)
        {
            frame = null!;
            return false;
        }

        public void ResetCounters()
        {
            OverflowCount = 0;
            DroppedFrameCount = 0;
            ReceivedFrameCount = 0;
        }

        public void Dispose() { }

        /// <summary>测试里改状态后手动通知（产品代码不用）</summary>
        public void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
