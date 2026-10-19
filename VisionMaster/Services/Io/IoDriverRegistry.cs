using System;
using System.Collections.Generic;
using System.Linq;
using Core.Interfaces;
using Core.Interfaces.Io;

namespace VisionMaster.Services.Io
{
    /// <summary>内置 IO 驱动注册表。插件扫描接入后可在这里追加外部驱动。</summary>
    public sealed class IoDriverRegistry
    {
        private readonly Dictionary<string, IIoDriverFactory> _factories =
            new(StringComparer.OrdinalIgnoreCase);

        public IoDriverRegistry()
        {
            Register(new SimulatedIoDriverFactory());
            Register(new ModbusTcpIoDriverFactory());
            Register(new ModbusRtuIoDriverFactory());
            Register(new SiemensS7IoDriverFactory());
        }

        public IReadOnlyList<IIoDriverFactory> All => _factories.Values.ToList();

        public void Register(IIoDriverFactory factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (string.IsNullOrWhiteSpace(factory.DriverId))
                throw new ArgumentException("驱动 Id 不能为空", nameof(factory));

            if (_factories.TryGetValue(factory.DriverId, out var existing) &&
                !ReferenceEquals(existing, factory))
            {
                throw new InvalidOperationException(
                    $"重复的 IO 驱动 Id「{factory.DriverId}」：{existing.GetType().Name} / {factory.GetType().Name}");
            }

            _factories[factory.DriverId] = factory;
        }

        public bool TryGet(string driverId, out IIoDriverFactory factory)
        {
            factory = null!;
            if (string.IsNullOrWhiteSpace(driverId)) return false;
            return _factories.TryGetValue(driverId.Trim(), out factory!);
        }

        public IIoDevice Create(IoDeviceDescriptor descriptor, IoBusDescriptor bus, IIoRuntimeContext context)
        {
            if (!TryGet(descriptor.DriverId, out var factory))
                throw new InvalidOperationException($"未找到 IO 驱动「{descriptor.DriverId}」");
            return factory.Create(descriptor, bus, context);
        }
    }
}
