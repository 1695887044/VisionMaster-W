using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Core.Interfaces;
using Core.Interfaces.Io;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.Binding
{
    /// <summary>
    /// IO 变量目录：把方案里的 IO 点位投影成 System.Io.{DeviceId}.{PointId} 变量。
    /// 不落盘；确定性 Id 由 SolutionId + DeviceId + PointId 推导。
    /// </summary>
    public sealed class IoVariableCatalog : ISystemVariableProvider
    {
        public const string NamePrefix = "System.Io.";
        private const string IdKeyPrefix = "io-var";

        private readonly IWorkspaceManager _workspace;
        private readonly IIoProvider _provider;
        private readonly ILogService _log;
        private readonly Dictionary<(string DeviceId, string PointId), IoVariableModel> _models = new();
        private readonly HashSet<IIoDevice> _subscribed = new();

        /// <summary>
        /// _models 的读写锁。
        /// <para>写入方：<see cref="Rebuild"/>（方案切换 / <c>DevicesChanged</c>）与 <see cref="RemoveAll"/>；
        /// 读取方：1s 健康轮询的 <see cref="Snapshot"/>、界面读 <see cref="Variables"/>、
        /// 设备回调 <see cref="OnPointsChanged"/>/<see cref="OnStateChanged"/>。
        /// 普通 Dictionary 在并发增删时枚举会抛 InvalidOperationException，
        /// 而健康面板把"每秒读一次"变成了常态，必须加锁。</para>
        /// </summary>
        private readonly object _modelsGate = new();

        public IoVariableCatalog(IWorkspaceManager workspace, IIoProvider provider, ILogService log,
                                 IoOutputLeaseRegistry? leaseRegistry = null)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            LeaseRegistry = leaseRegistry;
            _provider.DevicesChanged += (_, _) => Rebuild();
        }

        /// <summary>输出写租约（可空：无租约时行为与旧版一致，手动写不做互斥）。</summary>
        public IoOutputLeaseRegistry? LeaseRegistry { get; }

        public string Name => "Io";

        /// <summary>状态变量缓存（按名字复用实例，避免每拍新建导致订阅失联）。</summary>
        private readonly object _statusGate = new();
        private readonly Dictionary<string, SystemVariableModel> _statusModels = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>提供方快照（有序、不可变）。</summary>
        public IReadOnlyList<IVariable> Snapshot()
        {
            List<IVariable> points;
            lock (_modelsGate)
            {
                points = _models
                    .OrderBy(p => p.Key.DeviceId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(p => p.Key.PointId, StringComparer.OrdinalIgnoreCase)
                    .Select(p => (IVariable)p.Value)
                    .ToList();
            }

            // 状态变量有独立的 _statusGate，放在 _modelsGate 外面拼，避免两把锁嵌套。
            return points.Concat(BuildStatusVariables()).ToList();
        }

        /// <summary>
        /// 站点/总线状态变量（只读诊断量）：
        /// System.Io.{DeviceId}.Online / State / LastError / LastSuccessTime / ConsecutiveFailures。
        ///
        /// 为什么也走系统变量：现场要能看到"哪个站离线、为什么"，而这些状态本身不是点位。
        /// 名称第二段用 DeviceId，与点位变量共用同一命名空间（装置归组）。
        /// </summary>
        private IReadOnlyList<IVariable> BuildStatusVariables()
        {
            var result = new List<IVariable>();
            var aliveKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            lock (_statusGate)
            {
                foreach (var device in _provider.Devices)
                {
                    var deviceId = device.Descriptor.DeviceId;
                    aliveKeys.Add(Add(deviceId, "Online", typeof(bool),
                        $"IO 设备 {deviceId} 是否在线（只读系统变量）"));
                    aliveKeys.Add(Add(deviceId, "State", typeof(string),
                        $"IO 设备 {deviceId} 的连接状态（只读系统变量）"));
                    aliveKeys.Add(Add(deviceId, "LastError", typeof(string),
                        $"IO 设备 {deviceId} 的最近错误（只读系统变量）"));
                }

                // 已删除的设备：状态变量一起摘掉
                foreach (var key in _statusModels.Keys.ToList())
                {
                    if (!aliveKeys.Contains(key))
                        _statusModels.Remove(key);
                }

                foreach (var key in aliveKeys)
                    result.Add(_statusModels[key]);

                return result;
            }

            string Add(string deviceId, string field, Type type, string description)
            {
                var name = $"{NamePrefix}{deviceId}.{field}";
                if (!_statusModels.ContainsKey(name))
                {
                    _statusModels[name] = new SystemVariableModel(
                        ComputeStatusId(deviceId, field), name, type, description);
                }
                return name;
            }
        }

        /// <summary>把设备状态写进状态变量（由 IO 模块定时调用）。</summary>
        public void PublishStatusValues()
        {
            lock (_statusGate)
            {
                foreach (var device in _provider.Devices)
                {
                    var deviceId = device.Descriptor.DeviceId;
                    WriteStatus($"{NamePrefix}{deviceId}.Online", device.State == IoDeviceState.Online);
                    WriteStatus($"{NamePrefix}{deviceId}.State", device.State.ToString());
                    WriteStatus($"{NamePrefix}{deviceId}.LastError", device.StateDetail ?? string.Empty);
                }
            }

            void WriteStatus(string name, object? value)
            {
                if (_statusModels.TryGetValue(name, out var model))
                    model.UpdateMirrorValue(value);
            }
        }

        private static Guid ComputeStatusId(string deviceId, string field)
        {
            string key = $"{IdKeyPrefix}-status|{deviceId}|{field}";
            byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(key));
            Span<byte> bytes = stackalloc byte[16];
            hash.AsSpan(0, 16).CopyTo(bytes);
            return new Guid(bytes);
        }

        /// <summary>投影变化（点位增删/设备重建）。SystemVariableHost 订阅后统一刷新集合。</summary>
        public event EventHandler? Changed;

        private SystemVariableHost? _host;

        /// <summary>接到统一宿主：接上之后本目录不再直接写集合，只产快照并通知 Host 刷新。</summary>
        public void AttachToHost(SystemVariableHost host)
        {
            _host = host;
            if (host == null) return;
            host.Register(this);
        }

        public IReadOnlyList<IoVariableModel> Variables
        {
            get { lock (_modelsGate) return _models.Values.ToList(); }
        }

        public void Rebuild()
        {
            var solution = _workspace.CurrentSolution;
            if (solution == null)
            {
                RemoveAll();
                return;
            }

            if (solution.SolutionId == Guid.Empty)
                solution.SolutionId = Guid.NewGuid();

            var desired = new Dictionary<(string, string), (IoDeviceDescriptor Device, IoPointDescriptor Point)>();
            foreach (var device in solution.IoDevices ?? new List<IoDeviceDescriptor>())
            {
                if (device == null || !device.Enabled) continue;
                foreach (var point in device.Points ?? new List<IoPointDescriptor>())
                {
                    if (point == null || !point.Enabled) continue;
                    desired[(device.DeviceId, point.PointId)] = (device, point);
                }
            }

            // 移除已不存在的点 / 新增或复用 —— 对 _models 的读写必须整段持锁：
            // 中途被 Snapshot()（1s 健康轮询）或设备回调看到半成品集合会抛/读到错配。
            bool changed = false;
            lock (_modelsGate)
            {
                foreach (var key in _models.Keys.ToList())
                {
                    if (desired.ContainsKey(key)) continue;
                    var model = _models[key];
                    _models.Remove(key);
                    if (_host == null) _workspace.SystemVariables.Remove(model);
                    changed = true;
                }

                foreach (var pair in desired)
                {
                    if (_models.ContainsKey(pair.Key)) continue;

                    var (device, point) = pair.Value;
                    var variableId = ComputeVariableId(solution.SolutionId, device.DeviceId, point.PointId);
                    var name = $"{NamePrefix}{device.DeviceId}.{point.PointId}";
                    var type = point.Kind is IoPointKind.DigitalInput or IoPointKind.DigitalOutput
                        ? typeof(bool)
                        : typeof(double);
                    var description = $"IO 设备 {device.DeviceId} 的点 {point.PointId}（{point.Kind}）";

                    IoVariableModel model = point.IsWritable
                        ? new IoOutputVariableModel(variableId, name, type, _provider, device.DeviceId, point.PointId, point, description, LeaseRegistry)
                        : new IoInputVariableModel(variableId, name, type, _provider, device.DeviceId, point.PointId, point, description);

                    _models[pair.Key] = model;
                    if (_host == null) _workspace.SystemVariables.Add(model);
                    changed = true;
                }
            }

            SubscribeDevices();

            // 状态变量（Online/State/LastError）也会随设备集合变化，需一并通知宿主。
            if (_host != null)
                Changed?.Invoke(this, EventArgs.Empty);
        }

        private void SubscribeDevices()
        {
            foreach (var device in _provider.Devices)
            {
                if (!_subscribed.Add(device)) continue;
                device.PointsChanged += OnPointsChanged;
                device.StateChanged += OnStateChanged;
            }

            foreach (var device in _subscribed.ToList())
            {
                if (_provider.Devices.Contains(device)) continue;
                device.PointsChanged -= OnPointsChanged;
                device.StateChanged -= OnStateChanged;
                _subscribed.Remove(device);
            }
        }

        private void OnPointsChanged(object? sender, IoPointsChangedEventArgs e)
        {
            if (sender is not IIoDevice device) return;
            foreach (var change in e.Changes)
            {
                IoVariableModel? model;
                lock (_modelsGate)
                {
                    if (!_models.TryGetValue((device.Descriptor.DeviceId, change.PointId), out model))
                        model = null;
                }
                if (model == null) continue;

                // Publish 会发 PropertyChanged/ValueChanged，放在锁外做，避免订阅方回调时持锁。
                model.Publish(ToValue(model, change.Value), ToQuality(change.Value.Quality), change.Value.Timestamp);
            }
        }

        private void OnStateChanged(object? sender, EventArgs e)
        {
            if (sender is not IIoDevice device) return;
            var quality = device.State == IoDeviceState.Online ? VariableQuality.Good : VariableQuality.Bad;

            List<IoVariableModel> targets;
            lock (_modelsGate)
            {
                targets = _models.Values
                    .Where(m => m.DeviceId == device.Descriptor.DeviceId)
                    .ToList();
            }

            foreach (var model in targets)
                model.Publish(model.Value, quality, DateTime.Now);
        }

        private void RemoveAll()
        {
            List<IoVariableModel> removed;
            lock (_modelsGate)
            {
                if (_models.Count == 0) return;
                removed = _models.Values.ToList();
                _models.Clear();
            }

            if (_host == null)
            {
                foreach (var model in removed)
                    _workspace.SystemVariables.Remove(model);
            }

            if (_host != null)
                Changed?.Invoke(this, EventArgs.Empty);
        }

        private static object? ToValue(IoVariableModel model, IoPointValue value)
        {
            if (model.DataType == typeof(bool)) return value.Boolean;
            return value.Numeric;
        }

        private static VariableQuality ToQuality(IoQuality quality) => quality switch
        {
            IoQuality.Good => VariableQuality.Good,
            IoQuality.Uncertain => VariableQuality.Uncertain,
            IoQuality.Disabled => VariableQuality.Disabled,
            _ => VariableQuality.Bad
        };

        public static Guid ComputeVariableId(Guid solutionId, string deviceId, string pointId)
        {
            string key = $"{IdKeyPrefix}|{solutionId:N}|{deviceId}|{pointId}";
            byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(key));
            Span<byte> bytes = stackalloc byte[16];
            hash.AsSpan(0, 16).CopyTo(bytes);
            return new Guid(bytes);
        }

        public static bool IsReservedName(string? name)
            => !string.IsNullOrWhiteSpace(name) &&
               name.Trim().StartsWith(NamePrefix, StringComparison.OrdinalIgnoreCase);
    }
}
