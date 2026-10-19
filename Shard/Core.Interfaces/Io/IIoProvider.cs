using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Interfaces.Io
{
    /// <summary>
    /// IO 设备仓库：宿主侧维护“方案配置 ↔ 运行态设备”的对应关系。
    /// 与 <c>IMotionProvider</c> 同形，插件只面向这张表拿设备。
    /// </summary>
    public interface IIoProvider
    {
        IReadOnlyList<IoBusDescriptor> Buses { get; }
        IReadOnlyList<IIoDevice> Devices { get; }

        bool TryGetDevice(string deviceId, out IIoDevice device);
        bool TryGetDeviceByKey(string key, out IIoDevice device);

        void StartAll();
        void StopAll();
        void ReleaseAll();

        event EventHandler? DevicesChanged;
    }

    /// <summary>空实现：没有配置任何 IO 时的默认值，保证插件只判 TryGet 的返回值。</summary>
    public sealed class NullIoProvider : IIoProvider
    {
        public static NullIoProvider Instance { get; } = new();
        private NullIoProvider() { }

        public IReadOnlyList<IoBusDescriptor> Buses { get; } = Array.Empty<IoBusDescriptor>();
        public IReadOnlyList<IIoDevice> Devices { get; } = Array.Empty<IIoDevice>();
        public event EventHandler? DevicesChanged { add { } remove { } }

        public bool TryGetDevice(string deviceId, out IIoDevice device)
        {
            device = null!;
            return false;
        }

        public bool TryGetDeviceByKey(string key, out IIoDevice device)
        {
            device = null!;
            return false;
        }

        public void StartAll() { }
        public void StopAll() { }
        public void ReleaseAll() { }
    }
}
