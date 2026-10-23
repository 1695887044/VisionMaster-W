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
    /// 通讯设备的运行态快照（目录的输入）。
    ///
    /// 为什么先抽一层而不是让目录直接吃 <c>ICommunicationManager</c>：
    /// 目录要能在断言宿主里用假数据跑（协议连接要真 socket，跑不动），
    /// 而 <c>ICommunicationManager</c> 有二十多个成员，为测试写一个全桩不值当。
    /// 适配器在宿主侧（<c>CommunicationDeviceStateSource</c>）只做"读配置 + 读链路状态"。
    /// </summary>
    public readonly record struct CommDeviceState(
        string Name,
        string Protocol,
        bool Connected,
        string State,
        string StateText,
        string LastError,
        bool Enabled,
        int ReadCycleMs);

    /// <summary>通讯设备状态源：目录每拍取一次快照（实现方保证不抛、返回空而不是 null）。</summary>
    public interface ICommDeviceStateSource
    {
        IReadOnlyList<CommDeviceState> Snapshot();
    }

    /// <summary>
    /// 通讯系统变量目录：每个连接投影成**一个复合变量 + 若干字段**。
    ///
    /// 命名与身份
    /// ---------
    /// <code>
    /// System.Comm.PLC_Modbus          : CommDevice   ← 父变量（整台设备）
    /// System.Comm.PLC_Modbus.Connected : bool
    /// System.Comm.PLC_Modbus.State     : string（枚举成员名）
    /// System.Comm.PLC_Modbus.StateText : string（中文）
    /// System.Comm.PLC_Modbus.LastError : string
    /// System.Comm.PLC_Modbus.Protocol  : string
    /// System.Comm.PLC_Modbus.Enabled   : bool
    /// System.Comm.PLC_Modbus.ReadCycleMs : int
    /// </code>
    /// 父变量 Id = <c>SHA1("comm-dev|{连接名}")</c>，字段 Id = <c>SHA1("comm-var|{连接名}|{字段}")</c>。
    /// 连接名是通讯层唯一的稳定键（配置里没有 Guid），**改名等于换一台设备**：旧 Id 失效、
    /// 新 Id 生成——与"删掉再新建"同语义，这一点写在 docs/code-changes 里。
    ///
    /// 线程
    /// ---------
    /// 定时器线程做"重建 + 发布"，整表交换；读方（注册表/界面）只拿不可变快照。
    /// 与运动目录同一口径：拓扑没变时不动集合，值变化只走模型自身的 INPC。
    /// </summary>
    public sealed class CommSystemVariableCatalog : ISystemVariableProvider, IDisposable
    {
        /// <summary>通讯系统变量的顶层前缀（用户变量新建/改名据此拒绝）</summary>
        public const string NamePrefix = "System.Comm.";

        private const string IdKeyPrefix = "comm-var";
        private const string DeviceIdKeyPrefix = "comm-dev";
        private const string DeviceFieldKey = "$device";
        private const string OwnerName = "Comm";

        private readonly record struct FieldDef(string Key, Type DataType, string Label);

        /// <summary>字段表：顺序即展开顺序（连接状态在前，协议/周期在后）</summary>
        private static readonly FieldDef[] Fields =
        {
            new("Connected", typeof(bool), "已连接"),
            new("State", typeof(string), "状态"),
            new("StateText", typeof(string), "状态文本"),
            new("LastError", typeof(string), "最后错误"),
            new("Protocol", typeof(string), "协议"),
            new("Enabled", typeof(bool), "已启用"),
            new("ReadCycleMs", typeof(int), "默认组周期(ms)"),
        };

        private const string FieldConnected = "Connected";
        private const string FieldState = "State";
        private const string FieldStateText = "StateText";
        private const string FieldLastError = "LastError";
        private const string FieldProtocol = "Protocol";
        private const string FieldEnabled = "Enabled";
        private const string FieldReadCycleMs = "ReadCycleMs";

        private readonly object _gate = new();
        private readonly ICommDeviceStateSource _source;

        private Dictionary<(string Name, string Field), SystemVariableModel> _models = new();
        private IReadOnlyList<IVariable> _ordered = Array.Empty<IVariable>();

        private Timer? _timer;
        private bool _disposed;

        public CommSystemVariableCatalog(ICommDeviceStateSource source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <inheritdoc />
        public string Name => "Comm";

        /// <inheritdoc />
        public event EventHandler? Changed;

        /// <inheritdoc />
        public IReadOnlyList<IVariable> Snapshot()
        {
            lock (_gate) return _ordered;
        }

        /// <summary>按名字取模型（断言/诊断用；注册表另有自己的索引）</summary>
        public bool TryGet(string name, out SystemVariableModel variable)
        {
            variable = null!;
            if (string.IsNullOrWhiteSpace(name)) return false;

            Dictionary<(string Name, string Field), SystemVariableModel> models;
            lock (_gate) models = _models;

            // 线性找一次即可：通讯设备数量是"条"级，不值得再维护一张名字表
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

        /// <summary>启动周期刷新（幂等）。默认 500ms：通讯状态不是毫秒级量，没必要跟运动抢 100ms。</summary>
        public void Start(TimeSpan? interval = null)
        {
            if (_disposed || _timer != null) return;

            var period = interval ?? TimeSpan.FromMilliseconds(500);
            _timer = new Timer(_ => Tick(), null, TimeSpan.Zero, period);
        }

        /// <summary>停止周期刷新（幂等；已建好的变量保留最后一帧）</summary>
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
                // 后台诊断链路：目录坏了不该把进程带下去（与运动目录同一口径）
                System.Diagnostics.Debug.WriteLine($"[CommSystemVariableCatalog] 刷新失败（已跳过本拍）：{ex.Message}");
            }
        }

        /// <summary>立刻重建 + 发布一拍（定时器正常路径；断言用它避免等定时器）</summary>
        public void Refresh()
        {
            IReadOnlyList<CommDeviceState> devices;
            try
            {
                devices = _source.Snapshot() ?? Array.Empty<CommDeviceState>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CommSystemVariableCatalog] 状态源读取失败（保留上一拍）：{ex.Message}");
                return;
            }

            // 只归一化一次：Rebuild 与 Publish 必须看到同一份设备表（名字已去空白、已去重）
            var desired = Normalize(devices);
            bool topologyChanged = Rebuild(desired);
            Publish(desired);

            if (topologyChanged)
                Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 归一化设备表：空名跳过、同名先到者胜、名字去首尾空白、按名字排序。
        /// <para>Rebuild 与 Publish 必须用**同一份**归一结果：Publish 若拿原始表，
        /// 名字带空白的自定义状态源会让字段值全部 miss（变量建出来了但永远不刷新）。</para>
        /// </summary>
        private static List<CommDeviceState> Normalize(IReadOnlyList<CommDeviceState> devices)
        {
            var desired = new List<CommDeviceState>(devices.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var device in devices)
            {
                var name = (device.Name ?? string.Empty).Trim();
                if (name.Length == 0) continue;
                if (!seen.Add(name)) continue;
                desired.Add(device with { Name = name });
            }

            desired.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return desired;
        }

        /// <summary>
        /// 按设备表重建投影（复用实例、只增删移）。
        /// 返回"拓扑是否变化"（增删了设备）；值变化不算拓扑，不触发宿主刷新。
        /// </summary>
        private bool Rebuild(List<CommDeviceState> desired)
        {
            Dictionary<(string, string), SystemVariableModel> previous;
            lock (_gate) previous = _models;

            var models = new Dictionary<(string, string), SystemVariableModel>(desired.Count * (Fields.Length + 1));
            var ordered = new List<IVariable>(desired.Count * (Fields.Length + 1));

            foreach (var device in desired)
            {
                var fieldModels = new List<IVariable>(Fields.Length);

                foreach (var field in Fields)
                {
                    var key = (device.Name, field.Key);
                    var name = BuildFieldName(device.Name, field.Key);

                    if (!previous.TryGetValue(key, out var model))
                    {
                        model = new SystemVariableModel(
                            ComputeVariableId(IdKeyPrefix, device.Name, field.Key),
                            name,
                            field.DataType,
                            BuildFieldDescription(device.Name, field.Label),
                            owner: OwnerName);
                    }
                    else
                    {
                        model.UpdateName(name);
                        model.UpdateDescription(BuildFieldDescription(device.Name, field.Label));
                    }

                    models[key] = model;
                    fieldModels.Add(model);
                }

                var deviceKey = (device.Name, DeviceFieldKey);
                var deviceName = BuildDeviceName(device.Name);
                var deviceDescription = $"通讯设备 {device.Name} 的只读状态快照（含 {Fields.Length} 个字段）";

                if (!previous.TryGetValue(deviceKey, out var deviceModel))
                {
                    deviceModel = new SystemVariableModel(
                        ComputeVariableId(DeviceIdKeyPrefix, device.Name, null),
                        deviceName,
                        typeof(CommDevice),
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

        /// <summary>把一拍设备状态写进父变量与字段（同值由模型自身短路）</summary>
        private void Publish(IReadOnlyList<CommDeviceState> devices)
        {
            Dictionary<(string Name, string Field), SystemVariableModel> models;
            lock (_gate) models = _models;

            foreach (var device in devices)
            {
                Write(models, device.Name, FieldConnected, device.Connected);
                Write(models, device.Name, FieldState, device.State);
                Write(models, device.Name, FieldStateText, device.StateText);
                Write(models, device.Name, FieldLastError, device.LastError);
                Write(models, device.Name, FieldProtocol, device.Protocol);
                Write(models, device.Name, FieldEnabled, device.Enabled);
                Write(models, device.Name, FieldReadCycleMs, device.ReadCycleMs);

                if (models.TryGetValue((device.Name, DeviceFieldKey), out var deviceModel))
                {
                    // 父快照与字段同口径：State = 枚举成员名（机器可判），StateText = 中文（给人看）
                    deviceModel.UpdateMirrorValue(new CommDevice(
                        device.Name,
                        device.Protocol,
                        device.Connected,
                        device.State,
                        device.StateText,
                        device.LastError,
                        device.Enabled,
                        device.ReadCycleMs));
                }
            }
        }

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
            Dictionary<(string Name, string Field), SystemVariableModel> models,
            string deviceName,
            string field,
            object? value)
        {
            if (models.TryGetValue((deviceName, field), out var model))
                model.UpdateMirrorValue(value);
        }

        private static string BuildDeviceName(string deviceName) => $"{NamePrefix}{deviceName}";

        private static string BuildFieldName(string deviceName, string field) => $"{NamePrefix}{deviceName}.{field}";

        private static string BuildFieldDescription(string deviceName, string label)
            => $"通讯设备 {deviceName} 的{label}（只读系统变量）";

        private static Guid ComputeVariableId(string prefix, string ownerKey, string? field)
        {
            string key = string.IsNullOrEmpty(field)
                ? $"{prefix}|{ownerKey}"
                : $"{prefix}|{ownerKey}|{field}";

            byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(key));

            Span<byte> bytes = stackalloc byte[16];
            hash.AsSpan(0, 16).CopyTo(bytes);
            return new Guid(bytes);
        }
    }
}
