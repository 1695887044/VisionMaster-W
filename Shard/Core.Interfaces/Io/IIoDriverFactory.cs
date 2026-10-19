using System;

namespace Core.Interfaces.Io
{
    /// <summary>
    /// 驱动注册标记。driverId 是稳定键（如 modbus.tcp / modbus.rtu / siemens.s7），
    /// 配置里只存它，不存 AssemblyQualifiedName。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class IoDriverAttribute : Attribute
    {
        public IoDriverAttribute(string driverId) => DriverId = driverId;

        public string DriverId { get; }
        public int ContractVersion { get; set; } = 1;
    }

    /// <summary>
    /// 驱动工厂：宿主按 driverId 选工厂并创建设备。
    /// 配置类型用于反射属性窗口（[SuperDisplay] / FlatPropertyGrid）。
    /// </summary>
    public interface IIoDriverFactory
    {
        string DriverId { get; }
        string DisplayName { get; }
        string Description { get; }
        IoCapabilities Capabilities { get; }

        Type? BusConfigType { get; }
        Type? DeviceConfigType { get; }

        IIoDevice Create(IoDeviceDescriptor descriptor, IoBusDescriptor bus, IIoRuntimeContext context);
    }

    /// <summary>驱动信息（宿主注册表/插件缓存使用）。</summary>
    public sealed class IoDriverInfo
    {
        public IoDriverInfo(string driverId, Type factoryType, string displayName, string description, int contractVersion)
        {
            DriverId = driverId;
            FactoryType = factoryType;
            DisplayName = displayName;
            Description = description;
            ContractVersion = contractVersion;
        }

        public string DriverId { get; }
        public Type FactoryType { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public int ContractVersion { get; }
        public string TypeKey => FactoryType.AssemblyQualifiedName ?? FactoryType.FullName ?? string.Empty;
    }
}
