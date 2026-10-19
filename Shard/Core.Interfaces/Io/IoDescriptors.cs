using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json;

namespace Core.Interfaces.Io
{
    /// <summary>IO 总线配置：一条链路（TCP 连接或串口）以及挂在其上的设备。</summary>
    public sealed class IoBusDescriptor
    {
        /// <summary>当前配置结构版本。加载时低于此值走迁移，高于此值明确拒绝。</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>配置结构版本（驱动/字段变更时递增，配套迁移钩子）。</summary>
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>稳定总线标识（方案内唯一、创建后不可改）。</summary>
        [Required]
        public string BusId { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public IoTransportKind TransportKind { get; set; } = IoTransportKind.Tcp;

        public TcpTransportConfig Tcp { get; set; } = new();

        public SerialTransportConfig Serial { get; set; } = new();

        public IoReconnectPolicy Reconnect { get; set; } = new();

        /// <summary>单个轮询请求允许占用的最大时间（毫秒）。用于控制写命令的最坏等待。</summary>
        public int MaxPollFrameTimeMs { get; set; } = 50;

        public string Caption =>
            !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName
            : !string.IsNullOrWhiteSpace(BusId) ? BusId
            : "(未命名总线)";

        public bool Validate(out string error)
        {
            error = string.Empty;
            if (SchemaVersion > CurrentSchemaVersion)
            {
                error = $"总线配置版本 {SchemaVersion} 高于当前软件支持的 {CurrentSchemaVersion}，请升级软件";
                return false;
            }
            if (string.IsNullOrWhiteSpace(BusId)) { error = "总线标识不能为空"; return false; }
            if (TransportKind == IoTransportKind.Tcp)
            {
                if (string.IsNullOrWhiteSpace(Tcp.Host)) { error = "TCP 主机不能为空"; return false; }
                if (Tcp.Port <= 0 || Tcp.Port > 65535) { error = "TCP 端口必须在 1-65535 之间"; return false; }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(Serial.PortName)) { error = "串口名不能为空"; return false; }
                if (Serial.BaudRate <= 0) { error = "波特率必须大于 0"; return false; }
            }
            if (MaxPollFrameTimeMs < 1) { error = "单帧最大时间必须大于 0"; return false; }
            return true;
        }
    }

    public sealed class TcpTransportConfig
    {
        [Required]
        public string Host { get; set; } = "127.0.0.1";

        public int Port { get; set; } = 502;

        public int ConnectTimeoutMs { get; set; } = 3000;

        public int ReadTimeoutMs { get; set; } = 1000;
    }

    public sealed class SerialTransportConfig
    {
        [Required]
        public string PortName { get; set; } = "COM1";

        public int BaudRate { get; set; } = 9600;

        public int DataBits { get; set; } = 8;

        public IoParity Parity { get; set; } = IoParity.None;

        public IoStopBits StopBits { get; set; } = IoStopBits.One;

        public int ReadTimeoutMs { get; set; } = 1000;
    }

    public sealed class IoReconnectPolicy
    {
        public bool Enabled { get; set; } = true;
        public int BaseIntervalMs { get; set; } = 1000;
        public int MaxIntervalMs { get; set; } = 30000;
        /// <summary>0 = 无限重试。</summary>
        public int MaxAttempts { get; set; }
        public int RefusedRetryIntervalMs { get; set; } = 1000;
    }

    /// <summary>一个 IO 设备（一个从站 / 一个 PLC / 一个 SDK 设备）。</summary>
    public sealed class IoDeviceDescriptor
    {
        public const int CurrentSchemaVersion = 1;

        /// <summary>配置结构版本（驱动私有配置变更时递增）。</summary>
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public string DeviceId { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        [Required]
        public string BusId { get; set; } = string.Empty;

        [Required]
        public string DriverId { get; set; } = string.Empty;

        public bool Enabled { get; set; } = true;

        public bool AutoConnect { get; set; } = true;

        /// <summary>驱动私有配置的 JSON（Modbus 站号、S7 机架/槽位等）。</summary>
        public string DriverConfigJson { get; set; } = "{}";

        public List<IoPointDescriptor> Points { get; set; } = new();

        public IoParams Params { get; set; } = new();

        public string Caption =>
            !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName
            : !string.IsNullOrWhiteSpace(DeviceId) ? DeviceId
            : "(未命名设备)";

        public bool Validate(out string error)
        {
            error = string.Empty;
            if (SchemaVersion > CurrentSchemaVersion)
            {
                error = $"设备配置版本 {SchemaVersion} 高于当前软件支持的 {CurrentSchemaVersion}，请升级软件";
                return false;
            }
            if (string.IsNullOrWhiteSpace(DeviceId)) { error = "设备标识不能为空"; return false; }
            if (string.IsNullOrWhiteSpace(BusId)) { error = "设备必须挂在一条总线上"; return false; }
            if (string.IsNullOrWhiteSpace(DriverId)) { error = "驱动不能为空"; return false; }

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var point in Points)
            {
                if (point == null) continue;
                if (string.IsNullOrWhiteSpace(point.PointId)) { error = "点位标识不能为空"; return false; }
                if (!ids.Add(point.PointId)) { error = $"点位标识重复：{point.PointId}"; return false; }
                if (!point.Validate(out var pointError)) { error = $"点位 {point.PointId}：{pointError}"; return false; }
            }
            return true;
        }
    }

    public sealed class IoPointDescriptor
    {
        [Required]
        public string PointId { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public IoPointKind Kind { get; set; } = IoPointKind.DigitalInput;

        /// <summary>物理地址。格式由协议驱动解释（Modbus 40001 / S7 DB1.DBX0.0）。</summary>
        [Required]
        public string Address { get; set; } = string.Empty;

        public IoDataType DataType { get; set; } = IoDataType.Bool;

        public IoAccess Access { get; set; } = IoAccess.ReadOnly;

        public double Scale { get; set; } = 1.0;

        public double Offset { get; set; }

        public string Unit { get; set; } = string.Empty;

        /// <summary>模拟量变化死区（工程量）。数字量忽略。</summary>
        public double Deadband { get; set; }

        /// <summary>数字量防抖时间（毫秒）。0 = 关闭。</summary>
        public int DebounceMs { get; set; }

        public IoFailSafe FailSafe { get; set; } = IoFailSafe.Hold;

        public string SafeValue { get; set; } = string.Empty;

        public bool Enabled { get; set; } = true;

        public bool AllowManualWrite { get; set; } = true;

        /// <summary>允许被多个写入者共享（默认关闭 = 严格单写者）。</summary>
        public bool AllowSharedWrite { get; set; }

        /// <summary>点位级写租约超时（毫秒）；0 = 用设备默认。</summary>
        public int LeaseTimeoutMs { get; set; }

        public string Description { get; set; } = string.Empty;

        public IoByteOrder? ByteOrderOverride { get; set; }

        public bool IsWritable =>
            Kind == IoPointKind.DigitalOutput || Kind == IoPointKind.AnalogOutput ||
            Access == IoAccess.WriteOnly || Access == IoAccess.ReadWrite;

        public string Caption =>
            !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName
            : !string.IsNullOrWhiteSpace(PointId) ? PointId
            : "(未命名点位)";

        public bool Validate(out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(PointId)) { error = "点位标识不能为空"; return false; }
            if (string.IsNullOrWhiteSpace(Address)) { error = "点位地址不能为空"; return false; }
            if (Scale == 0) { error = "缩放系数不能为 0"; return false; }
            if (Deadband < 0) { error = "死区不能为负数"; return false; }
            if (DebounceMs < 0) { error = "防抖时间不能为负数"; return false; }

            bool digital = Kind == IoPointKind.DigitalInput || Kind == IoPointKind.DigitalOutput;
            if (digital && DataType != IoDataType.Bool)
            { error = "数字量点位的类型必须是 Bool"; return false; }
            if (!digital && DataType == IoDataType.Bool)
            { error = "模拟量点位的类型不能是 Bool"; return false; }
            return true;
        }
    }

    public sealed class IoParams
    {
        public int ConnectTimeoutMs { get; set; } = 3000;
        public int WriteTimeoutMs { get; set; } = 500;
        public int CommandQueueCapacity { get; set; } = 1024;
        public int CommandEnqueueTimeoutMs { get; set; } = 2000;
        /// <summary>写成功后忽略轮询回显的时间；0 = 自动取 2 个轮询周期。</summary>
        public int WriteEchoGraceMs { get; set; }
        public List<IoPollGroup> PollGroups { get; set; } = new();
    }

    public sealed class IoPollGroup
    {
        public string Name { get; set; } = "default";
        public int PeriodMs { get; set; } = 50;
        public List<string> PointIds { get; set; } = new();
    }

    /// <summary>运行时上下文：驱动只拿日志等最小能力，不拿容器。</summary>
    public interface IIoRuntimeContext
    {
        ILogService Log { get; }
    }

    public sealed class IoRuntimeContext : IIoRuntimeContext
    {
        public IoRuntimeContext(ILogService log) => Log = log;
        public ILogService Log { get; }
    }
}
