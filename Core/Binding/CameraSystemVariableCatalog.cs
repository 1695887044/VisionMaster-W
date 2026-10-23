using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Core.Interfaces;
using VisionMaster.Models;

namespace VisionMaster.Binding
{
    /// <summary>
    /// 相机系统变量目录：每台相机投影成**一个复合变量 + 若干字段**。
    ///
    /// 命名与身份
    /// ---------
    /// <code>
    /// System.Camera.Cam1                : CameraDevice  ← 父变量（整台相机）
    /// System.Camera.Cam1.Online         : bool
    /// System.Camera.Cam1.State          : string（枚举成员名）
    /// System.Camera.Cam1.StateText      : string（中文）
    /// System.Camera.Cam1.StateDetail    : string
    /// System.Camera.Cam1.ReceivedFrameCount : long
    /// System.Camera.Cam1.DroppedFrameCount  : long
    /// System.Camera.Cam1.OverflowCount      : long
    /// System.Camera.Cam1.PendingFrameCount  : int
    /// </code>
    /// 身份按相机**内部 Id**（<c>CameraDescriptor.Id</c>，改名不变）派生：
    /// 父 = <c>SHA1("camera-dev|{Id:N}")</c>，字段 = <c>SHA1("camera-var|{Id:N}|{字段}")</c>。
    /// 名字用 <c>Caption</c>（显示名，缺失回落序列号）；两台相机重名时追加序列号/序号，
    /// 保证名字索引里不打架。
    /// </summary>
    public sealed class CameraSystemVariableCatalog : ISystemVariableProvider, IDisposable
    {
        /// <summary>相机系统变量的顶层前缀</summary>
        public const string NamePrefix = "System.Camera.";

        private const string IdKeyPrefix = "camera-var";
        private const string DeviceIdKeyPrefix = "camera-dev";
        private const string DeviceFieldKey = "$device";
        private const string OwnerName = "Camera";

        private readonly record struct FieldDef(string Key, Type DataType, string Label);

        private static readonly FieldDef[] Fields =
        {
            new("Online", typeof(bool), "在线"),
            new("State", typeof(string), "状态"),
            new("StateText", typeof(string), "状态文本"),
            new("StateDetail", typeof(string), "状态详情"),
            new("ReceivedFrameCount", typeof(long), "累计收帧"),
            new("DroppedFrameCount", typeof(long), "丢帧数"),
            new("OverflowCount", typeof(long), "溢出数"),
            new("PendingFrameCount", typeof(int), "待处理帧"),
        };

        private const string FieldOnline = "Online";
        private const string FieldState = "State";
        private const string FieldStateText = "StateText";
        private const string FieldStateDetail = "StateDetail";
        private const string FieldReceived = "ReceivedFrameCount";
        private const string FieldDropped = "DroppedFrameCount";
        private const string FieldOverflow = "OverflowCount";
        private const string FieldPending = "PendingFrameCount";

        private readonly object _gate = new();
        private readonly ICameraProvider _provider;

        private Dictionary<(Guid CameraId, string Field), SystemVariableModel> _models = new();
        private IReadOnlyList<IVariable> _ordered = Array.Empty<IVariable>();

        private Timer? _timer;
        private bool _disposed;

        public CameraSystemVariableCatalog(ICameraProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        /// <inheritdoc />
        public string Name => "Camera";

        /// <inheritdoc />
        public event EventHandler? Changed;

        /// <inheritdoc />
        public IReadOnlyList<IVariable> Snapshot()
        {
            lock (_gate) return _ordered;
        }

        /// <summary>按名字取模型（断言/诊断用）</summary>
        public bool TryGet(string name, out SystemVariableModel variable)
        {
            variable = null!;
            if (string.IsNullOrWhiteSpace(name)) return false;

            Dictionary<(Guid, string), SystemVariableModel> models;
            lock (_gate) models = _models;

            foreach (var pair in models)
            {
                if (string.Equals(pair.Value.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    variable = pair.Value;
                    return true;
                }
            }

            return false;
        }

        /// <summary>启动周期刷新（幂等）。默认 500ms：帧计数是统计量，不需要逐帧刷新。</summary>
        public void Start(TimeSpan? interval = null)
        {
            if (_disposed || _timer != null) return;

            var period = interval ?? TimeSpan.FromMilliseconds(500);
            _timer = new Timer(_ => Tick(), null, TimeSpan.Zero, period);
        }

        public void Stop()
        {
            _timer?.Dispose();
            _timer = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }

        private void Tick()
        {
            try
            {
                Refresh();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CameraSystemVariableCatalog] 刷新失败（已跳过本拍）：{ex.Message}");
            }
        }

        /// <summary>立刻重建 + 发布一拍（断言用它避免等定时器）</summary>
        public void Refresh()
        {
            IReadOnlyList<CameraDescriptor> cameras;
            try
            {
                cameras = _provider.Cameras ?? Array.Empty<CameraDescriptor>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CameraSystemVariableCatalog] 相机清单读取失败（保留上一拍）：{ex.Message}");
                return;
            }

            var desired = BuildDesired(cameras);
            bool topologyChanged = Rebuild(desired);
            Publish(desired);

            if (topologyChanged)
                Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 归一化相机清单：跳过空 Id/空名，按 Id 去重，名字冲突时追加序列号或序号。
        /// 抽成纯函数是为了能被断言直接钉住（重名处理最容易写错）。
        /// </summary>
        private static List<(CameraDescriptor Descriptor, string Name)> BuildDesired(
            IReadOnlyList<CameraDescriptor> cameras)
        {
            var result = new List<(CameraDescriptor, string)>(cameras.Count);
            var seenIds = new HashSet<Guid>();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var camera in cameras)
            {
                if (camera == null) continue;
                if (camera.Id == Guid.Empty) continue;
                if (!seenIds.Add(camera.Id)) continue;

                var caption = (camera.Caption ?? string.Empty).Trim();
                if (caption.Length == 0) caption = "未命名相机";

                var name = caption;
                if (!usedNames.Add(name))
                {
                    // 重名：先试序列号（稳定、可读），再不行才用序号
                    name = $"{caption}_{camera.SerialNo}";
                    if (!usedNames.Add(name))
                    {
                        var index = 2;
                        do
                        {
                            name = $"{caption}#{index++}";
                        }
                        while (!usedNames.Add(name));
                    }
                }

                result.Add((camera, name));
            }

            result.Sort((a, b) => string.Compare(a.Item2, b.Item2, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        private bool Rebuild(List<(CameraDescriptor Descriptor, string Name)> desired)
        {
            Dictionary<(Guid, string), SystemVariableModel> previous;
            lock (_gate) previous = _models;

            var models = new Dictionary<(Guid, string), SystemVariableModel>(desired.Count * (Fields.Length + 1));
            var ordered = new List<IVariable>(desired.Count * (Fields.Length + 1));

            foreach (var (descriptor, name) in desired)
            {
                var fieldModels = new List<IVariable>(Fields.Length);

                foreach (var field in Fields)
                {
                    var key = (descriptor.Id, field.Key);
                    var fieldName = BuildFieldName(name, field.Key);

                    if (!previous.TryGetValue(key, out var model))
                    {
                        model = new SystemVariableModel(
                            ComputeVariableId(IdKeyPrefix, descriptor.Id, field.Key),
                            fieldName,
                            field.DataType,
                            BuildFieldDescription(name, field.Label),
                            owner: OwnerName);
                    }
                    else
                    {
                        model.UpdateName(fieldName);
                        model.UpdateDescription(BuildFieldDescription(name, field.Label));
                    }

                    models[key] = model;
                    fieldModels.Add(model);
                }

                var deviceKey = (descriptor.Id, DeviceFieldKey);
                var deviceName = BuildDeviceName(name);
                var deviceDescription =
                    $"相机 {name}（{descriptor.SerialNo}）的只读状态快照（含 {Fields.Length} 个字段）";

                if (!previous.TryGetValue(deviceKey, out var deviceModel))
                {
                    deviceModel = new SystemVariableModel(
                        ComputeVariableId(DeviceIdKeyPrefix, descriptor.Id, null),
                        deviceName,
                        typeof(CameraDevice),
                        deviceDescription,
                        fieldModels,
                        OwnerName);
                }
                else
                {
                    deviceModel.UpdateName(deviceName);
                    deviceModel.UpdateDescription(deviceDescription);
                    deviceModel.UpdateFields(fieldModels);
                }

                models[deviceKey] = deviceModel;

                ordered.Add(deviceModel);
                ordered.AddRange(fieldModels);
            }

            IReadOnlyList<IVariable> previousOrdered;
            lock (_gate)
            {
                previousOrdered = _ordered;
                _models = models;
                _ordered = ordered;
            }

            return !SameSequence(previousOrdered, ordered);
        }

        private void Publish(List<(CameraDescriptor Descriptor, string Name)> desired)
        {
            Dictionary<(Guid CameraId, string Field), SystemVariableModel> models;
            lock (_gate) models = _models;

            foreach (var (descriptor, name) in desired)
            {
                bool hasDevice = _provider.TryGetDevice(descriptor.Id, out var device) && device != null;

                if (!hasDevice)
                {
                    // 设备拿不到（驱动缺失/尚未创建）：只改"为什么不知道"，
                    // 计数保留上一拍的已知值——清 0 会把"不知道"伪装成"没收到帧"。
                    Write(models, descriptor.Id, FieldOnline, false);
                    Write(models, descriptor.Id, FieldState, "Unavailable");
                    Write(models, descriptor.Id, FieldStateText, "设备不可用");
                    Write(models, descriptor.Id, FieldStateDetail, "相机设备未创建或驱动未加载");

                    if (models.TryGetValue((descriptor.Id, DeviceFieldKey), out var unavailableModel))
                    {
                        var previous = unavailableModel.Value as CameraDevice;
                        unavailableModel.UpdateMirrorValue(previous is null
                            ? new CameraDevice(descriptor.Id, name, descriptor.SerialNo, false,
                                "Unavailable", "设备不可用", "相机设备未创建或驱动未加载", 0, 0, 0, 0)
                            : previous with
                            {
                                // 身份字段一并刷新：不可用期间改名，父变量的 Value 不能停在旧名
                                Name = name,
                                SerialNo = descriptor.SerialNo,
                                Online = false,
                                State = "Unavailable",
                                StateText = "设备不可用",
                                StateDetail = "相机设备未创建或驱动未加载"
                            });
                    }

                    continue;
                }

                var state = device.State;
                var stateName = state.ToString();
                var stateText = StateText(state);
                var online = state is CameraConnectionState.Online or CameraConnectionState.Streaming;

                Write(models, descriptor.Id, FieldOnline, online);
                Write(models, descriptor.Id, FieldState, stateName);
                Write(models, descriptor.Id, FieldStateText, stateText);
                Write(models, descriptor.Id, FieldStateDetail, device.StateDetail ?? string.Empty);
                Write(models, descriptor.Id, FieldReceived, device.ReceivedFrameCount);
                Write(models, descriptor.Id, FieldDropped, device.DroppedFrameCount);
                Write(models, descriptor.Id, FieldOverflow, device.OverflowCount);
                Write(models, descriptor.Id, FieldPending, device.PendingFrameCount);

                if (models.TryGetValue((descriptor.Id, DeviceFieldKey), out var deviceModel))
                {
                    deviceModel.UpdateMirrorValue(new CameraDevice(
                        descriptor.Id,
                        name,
                        descriptor.SerialNo,
                        online,
                        stateName,
                        stateText,
                        device.StateDetail ?? string.Empty,
                        device.ReceivedFrameCount,
                        device.DroppedFrameCount,
                        device.OverflowCount,
                        device.PendingFrameCount));
                }
            }
        }

        /// <summary>相机状态枚举 → 中文（界面与流程都用这一份，不各自 switch）</summary>
        public static string StateText(CameraConnectionState state) => state switch
        {
            CameraConnectionState.Closed => "未打开",
            CameraConnectionState.Connecting => "连接中",
            CameraConnectionState.Online => "在线",
            CameraConnectionState.Streaming => "采流中",
            _ => state.ToString()
        };

        private static bool SameSequence(IReadOnlyList<IVariable> left, IReadOnlyList<IVariable> right)
        {
            if (left.Count != right.Count) return false;

            for (var i = 0; i < left.Count; i++)
            {
                if (!ReferenceEquals(left[i], right[i])) return false;
            }

            return true;
        }

        private static void Write(
            Dictionary<(Guid CameraId, string Field), SystemVariableModel> models,
            Guid cameraId,
            string field,
            object? value)
        {
            if (models.TryGetValue((cameraId, field), out var model))
                model.UpdateMirrorValue(value);
        }

        private static string BuildDeviceName(string name) => $"{NamePrefix}{name}";

        private static string BuildFieldName(string name, string field) => $"{NamePrefix}{name}.{field}";

        private static string BuildFieldDescription(string name, string label)
            => $"相机 {name} 的{label}（只读系统变量）";

        private static Guid ComputeVariableId(string prefix, Guid ownerId, string? field)
        {
            string key = string.IsNullOrEmpty(field)
                ? $"{prefix}|{ownerId:N}"
                : $"{prefix}|{ownerId:N}|{field}";

            byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(key));

            Span<byte> bytes = stackalloc byte[16];
            hash.AsSpan(0, 16).CopyTo(bytes);
            return new Guid(bytes);
        }
    }
}
