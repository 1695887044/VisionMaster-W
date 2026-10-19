using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Core.Interfaces;
using Core.Interfaces.Io;
using VisionMaster.Services;

namespace VisionMaster.Services.Io
{
    /// <summary>
    /// IO 设备仓库：把方案里的 IO 总线/设备配置变成运行态设备。
    /// 与 MotionProvider 同形：插件只认 IIoProvider，不认识方案/界面。
    /// </summary>
    public sealed class IoProvider : IIoProvider
    {
        private readonly IoDriverRegistry _registry;
        private readonly ILogService _log;
        private readonly object _gate = new();
        private List<IoBusDescriptor> _buses = new();
        private List<IoDeviceDescriptor> _descriptors = new();
        private List<IIoDevice> _devices = new();

        public IoProvider(IoDriverRegistry registry, ILogService log)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public IReadOnlyList<IoBusDescriptor> Buses
        {
            get { lock (_gate) return _buses.ToList(); }
        }

        public IReadOnlyList<IIoDevice> Devices
        {
            get { lock (_gate) return _devices.ToList(); }
        }

        public event EventHandler? DevicesChanged;

        public bool TryGetDevice(string deviceId, out IIoDevice device)
        {
            device = null!;
            if (string.IsNullOrWhiteSpace(deviceId)) return false;
            lock (_gate)
            {
                device = _devices.FirstOrDefault(d =>
                    string.Equals(d.Descriptor.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))!;
                return device != null;
            }
        }

        public bool TryGetDeviceByKey(string key, out IIoDevice device)
        {
            device = null!;
            if (string.IsNullOrWhiteSpace(key)) return false;
            lock (_gate)
            {
                device = _devices.FirstOrDefault(d =>
                    string.Equals(d.Descriptor.DeviceId, key, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(d.Descriptor.DisplayName, key, StringComparison.OrdinalIgnoreCase))!;
                return device != null;
            }
        }

        /// <summary>用方案里的配置重建运行态设备。会先停掉并释放旧设备。</summary>
        public void Apply(IEnumerable<IoBusDescriptor>? buses, IEnumerable<IoDeviceDescriptor>? devices, bool autoStart)
        {
            var busList = buses?.ToList() ?? new List<IoBusDescriptor>();
            var deviceList = devices?.ToList() ?? new List<IoDeviceDescriptor>();

            ReleaseAll();

            var created = new List<IIoDevice>();
            var busMap = busList.ToDictionary(b => b.BusId, StringComparer.OrdinalIgnoreCase);
            var context = new IoRuntimeContext(_log);

            foreach (var descriptor in deviceList)
            {
                if (descriptor == null || !descriptor.Enabled) continue;
                if (!descriptor.Validate(out var error))
                {
                    _log.Warn($"[IO] 设备配置无效「{descriptor.DeviceId}」：{error}");
                    continue;
                }
                if (!busMap.TryGetValue(descriptor.BusId, out var bus))
                {
                    _log.Warn($"[IO] 设备「{descriptor.DeviceId}」找不到总线「{descriptor.BusId}」");
                    continue;
                }
                if (!bus.Validate(out var busError))
                {
                    _log.Warn($"[IO] 总线配置无效「{bus.BusId}」：{busError}");
                    continue;
                }

                try
                {
                    var device = _registry.Create(descriptor, bus, context);
                    if (device is IoDeviceBase baseDevice)
                        baseDevice.StartWorker();
                    created.Add(device);
                }
                catch (Exception ex)
                {
                    _log.Error($"[IO] 创建设备「{descriptor.DeviceId}」失败：{ex.Message}");
                }
            }

            lock (_gate)
            {
                _buses = busList;
                _descriptors = deviceList;
                _devices = created;
            }

            DevicesChanged?.Invoke(this, EventArgs.Empty);

            if (autoStart)
                StartAll();
        }

        public void StartAll()
        {
            List<IIoDevice> devices;
            lock (_gate) devices = _devices.ToList();
            foreach (var device in devices)
            {
                if (device.State == IoDeviceState.Online) continue;
                _ = Task.Run(() =>
                {
                    try { device.Connect(); }
                    catch (Exception ex) { _log.Warn($"[IO] 设备「{device.Descriptor.DeviceId}」连接失败：{ex.Message}"); }
                });
            }
        }

        public void StopAll()
        {
            List<IIoDevice> devices;
            lock (_gate) devices = _devices.ToList();
            foreach (var device in devices)
            {
                try { device.Disconnect(); } catch { }
            }
        }

        public void ReleaseAll()
        {
            List<IIoDevice> devices;
            lock (_gate)
            {
                devices = _devices;
                _devices = new List<IIoDevice>();
            }

            foreach (var device in devices)
            {
                try { device.Dispose(); } catch { }
            }

            DevicesChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
