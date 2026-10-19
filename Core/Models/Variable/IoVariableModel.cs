using System;
using System.Threading;
using Core.Interfaces;
using Core.Interfaces.Io;
using VisionMaster.Core;
using VisionMaster.Services;
using Prism.Mvvm;
using VisionMaster.Communications;

namespace VisionMaster.Models
{
    /// <summary>IO 点变量基类：输入只读、输出可写；质量/时间戳与网络变量同口径。</summary>
    public abstract class IoVariableModel : BindableBase, IVariable, IVariableDisplaySource
    {
        private readonly IIoProvider _provider;
        private object? _value;
        private VariableQuality _quality = VariableQuality.Bad;
        private DateTime _lastUpdateTime;

        protected IoVariableModel(
            Guid variableId, string name, Type dataType,
            IIoProvider provider, string deviceId, string pointId,
            IoPointDescriptor point, string description)
        {
            VariableId = variableId;
            Name = name;
            DataType = dataType;
            _provider = provider;
            DeviceId = deviceId;
            PointId = pointId;
            Point = point;
            Description = description;
        }

        public Guid VariableId { get; }
        public string Name { get; }
        public Type DataType { get; }
        public VariableType VariableType => VariableType.Io;
        public string? ConnectionName => DeviceId;
        public DeviceAddressBase? AddressConfig => null;
        public object? DefaultValue { get; set; }
        public string Description { get; set; }

        public string DeviceId { get; }
        public string PointId { get; }
        public IoPointDescriptor Point { get; }

        public object? Value => _value;

        object IPort.Value
        {
            get => _value!;
            set => WriteFromPort(value);
        }

        public VariableQuality Quality => _quality;
        public DateTime LastUpdateTime => _lastUpdateTime;
        public event EventHandler? ValueChanged;

        internal void Publish(object? value, VariableQuality quality, DateTime time)
        {
            if (!Equals(_value, value))
            {
                _value = value;
                RaisePropertyChanged(nameof(Value));
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }

            if (_quality != quality)
            {
                _quality = quality;
                RaisePropertyChanged(nameof(Quality));
            }

            _lastUpdateTime = time;
            _lastPublishedValue = value;
            RaisePropertyChanged(nameof(LastUpdateTime));
        }

        /// <summary>上一次发布的值（审计里的“旧值”）。</summary>
        private object? _lastPublishedValue;

        protected object? LastPublishedValue => _lastPublishedValue;

        protected abstract void WriteFromPort(object? value);

        public void ResetToDefault() { }

        string IVariableDisplaySource.BindingKey => Name;
        string IVariableDisplaySource.DisplayLabel => string.IsNullOrEmpty(Description) ? Name : Description;
        Type IVariableDisplaySource.DataType => DataType;
        object? IVariableDisplaySource.Value => Value;
        string IVariableDisplaySource.SourceLabel => "IO";
        bool IVariableDisplaySource.IsReadOnly => this is not IWritableVariable;

        void IVariableDisplaySource.WriteValue(object? newValue)
        {
            // UI 双向绑定误触不抛；真正的写走 IWritableVariable。
            if (this is IWritableVariable writable)
                writable.TryWrite(newValue, out _);
        }

        protected bool TryResolveDevice(out IIoDevice device)
        {
            if (_provider.TryGetDevice(DeviceId, out device))
                return true;
            device = null!;
            return false;
        }

        protected static IoPointValue ToIoValue(IoDataType type, object? value, out string? error)
        {
            error = null;
            if (value == null)
            {
                error = "写入值不能为空";
                return IoPointValue.Bad(type);
            }

            if (type == IoDataType.Bool)
            {
                if (value is bool b) return IoPointValue.FromBool(b);
                if (bool.TryParse(value.ToString(), out var parsed)) return IoPointValue.FromBool(parsed);
                error = $"值「{value}」无法转换为 Bool";
                return IoPointValue.Bad(type);
            }

            try
            {
                double numeric = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
                return IoPointValue.FromRaw(type, (long)Math.Round(numeric), numeric);
            }
            catch
            {
                error = $"值「{value}」无法转换为 {type}";
                return IoPointValue.Bad(type);
            }
        }
    }

    /// <summary>输入点：类型层面只读，不实现 IWritableVariable。</summary>
    public sealed class IoInputVariableModel : IoVariableModel
    {
        public IoInputVariableModel(Guid variableId, string name, Type dataType,
                                    IIoProvider provider, string deviceId, string pointId,
                                    IoPointDescriptor point, string description)
            : base(variableId, name, dataType, provider, deviceId, pointId, point, description) { }

        protected override void WriteFromPort(object? value)
            => throw new NotSupportedException($"IO 输入点「{Name}」是只读的");
    }

    /// <summary>输出点：可写，显式写走设备确认。</summary>
    public sealed class IoOutputVariableModel : IoVariableModel, IWritableVariable
    {
        private readonly IoOutputLeaseRegistry? _leaseRegistry;

        public IoOutputVariableModel(Guid variableId, string name, Type dataType,
                                     IIoProvider provider, string deviceId, string pointId,
                                     IoPointDescriptor point, string description,
                                     IoOutputLeaseRegistry? leaseRegistry = null)
            : base(variableId, name, dataType, provider, deviceId, pointId, point, description)
        {
            _leaseRegistry = leaseRegistry;
        }

        public bool TryWrite(object? value, out string? error)
        {
            error = null;

            // 写入者身份：流程内 = 流程/分支 owner；流程外 = manual。
            // 这条决定了并行分支抢同一个 DO 时谁能写、谁被明确拒绝。
            string owner = IoWriteScope.CurrentOwner?.Trim() is { Length: > 0 } flowOwner
                ? flowOwner
                : IoOutputLeaseRegistry.ManualOwner;

            if (!TryResolveDevice(out var device))
            {
                error = $"IO 设备「{DeviceId}」未配置";
                RecordAudit(owner, value, success: false, error);
                return false;
            }

            if (device.State != IoDeviceState.Online)
            {
                error = $"IO 设备「{DeviceId}」当前离线：{device.StateDetail}";
                RecordAudit(owner, value, success: false, error);
                return false;
            }

            if (!Point.AllowManualWrite &&
                string.Equals(owner, IoOutputLeaseRegistry.ManualOwner, StringComparison.OrdinalIgnoreCase))
            {
                error = $"IO 输出点「{Name}」已禁止手动写";
                RecordAudit(owner, value, success: false, error);
                return false;
            }

            // 写租约：流程持有点位时手动写返回明确冲突，而不是静默最后写入者赢。
            if (_leaseRegistry != null && !Point.AllowSharedWrite)
            {
                int leaseTimeout = Point.LeaseTimeoutMs > 0 ? Point.LeaseTimeoutMs : 30000;
                if (!_leaseRegistry.TryAcquire(PointId, owner,
                        leaseTimeout, allowShared: false, out error))
                {
                    error = $"写入被拒绝：{error}";
                    RecordAudit(owner, value, success: false, error);
                    return false;
                }
            }

            var ioValue = ToIoValue(Point.DataType, value, out error);
            if (error != null)
            {
                RecordAudit(owner, value, success: false, error);
                return false;
            }

            // 写超时取设备级配置（默认 500ms），不再写死。
            int timeout = 500;
            if (TryResolveDevice(out var target) && target.Descriptor?.Params != null)
                timeout = Math.Max(50, target.Descriptor.Params.WriteTimeoutMs);

            if (!device.WriteAndWait(PointId, ioValue, timeout, CancellationToken.None, out error))
            {
                RecordAudit(owner, value, success: false, error);
                return false;
            }

            Publish(value, VariableQuality.Good, DateTime.Now);
            RecordAudit(owner, value, success: true, null);
            return true;
        }

        /// <summary>
        /// 写入审计：谁（流程 owner / manual）、哪个点、旧值→新值、结果。
        /// 审计是"手工写输出"可追溯的唯一凭证，失败也要记（"没写进去"与"没尝试"是两件事）。
        /// </summary>
        private void RecordAudit(string owner, object? newValue, bool success, string? error)
        {
            try
            {
                string oldValue = LastPublishedValue?.ToString() ?? "(无)";
                AuditLogger.Log(
                    AuditOperationType.DeviceControl,
                    success ? "IO 输出写入" : "IO 输出写入失败",
                    owner,
                    owner,
                    $"点位={DeviceId}.{PointId}；旧值={oldValue}；新值={newValue}；" +
                    (success ? "结果=成功" : $"结果=失败（{error}）"),
                    success);
            }
            catch
            {
                // 审计落盘故障不能影响写入结果
            }
        }

        protected override void WriteFromPort(object? value)
        {
            if (!TryWrite(value, out var error))
                System.Diagnostics.Debug.WriteLine($"[IoVariable] {Name} 写入失败：{error}");
        }
    }

    internal static class IoPointWriteExtensions
    {
        public static int DeviceWriteTimeoutMs(this IoPointDescriptor point) => 500;
    }}
