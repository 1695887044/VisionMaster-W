using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Threading;
using Core.Interfaces;
using VisionMaster.Models;

namespace VisionMaster.Services
{
    /// <summary>
    /// 一个可用相机驱动（= 一个驱动插件类型），供「相机设置」界面让用户选"厂商/类型"。
    /// </summary>
    public sealed class CameraDriverInfo
    {
        /// <summary>类型键（AssemblyQualifiedName）。与 <see cref="CameraDescriptor.DriverTypeKey"/> 同口径</summary>
        public string TypeKey { get; init; } = string.Empty;

        /// <summary>给用户看的名字（驱动类上 [Display].Name）</summary>
        public string DisplayName { get; init; } = string.Empty;

        /// <summary>一句话说明</summary>
        public string Description { get; init; } = string.Empty;

        /// <summary>驱动类型（创建实例用）</summary>
        public Type DriverType { get; init; }
    }

    /// <summary>
    /// 相机仓库：方案级相机配置 ⇄ 运行态设备实例的唯一对接处。
    ///
    /// 三段职责，界限必须清楚：
    ///   1) <b>配置</b>存在方案的 <see cref="SolutionModel.CameraConfigs"/> 里，随 .vms 落盘（谁改谁负责保存方案）；
    ///   2) <b>运行态</b>（设备实例、帧队列、状态机）存在本类的 <c>_devices</c> 里，随进程生死，从不序列化；
    ///   3) <b>对齐</b>由 <see cref="SyncFromSolution"/> 一处完成——方案里多了就建、少了就销毁、驱动或序列号改了
    ///      就重建。散在各处做"顺手 new 一个/顺手 Dispose 一个"，早晚出现"界面上两台、实际三台"的错位。
    ///
    /// 为什么运行态不放进方案对象：设备实例持有帧队列与线程；一旦能被序列化，
    /// 打开旧方案时就会尝试把一张几 MB 的图连同队列状态一起反序列化回来（参考工程
    /// 用 [NonSerialized] 打补丁就是为了绕开这个，但每个字段都得记得打）。
    /// </summary>
    public sealed class CameraProvider : ICameraProvider, IDisposable
    {
        private readonly ILogService _log;
        private readonly IReadOnlyWorkspaceContext _workspace;
        private readonly IPluginProvider _plugins;

        private readonly object _gate = new();

        /// <summary>运行态设备表（按内部 Id）</summary>
        private readonly Dictionary<Guid, ICameraDevice> _devices = new();

        /// <summary>序列号 → Id 的寻址索引。与 <see cref="TryGetDeviceBySerial"/> 同源</summary>
        private readonly Dictionary<string, Guid> _serialIndex = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>已解析过的驱动类型缓存（键 = AssemblyQualifiedName）</summary>
        private readonly Dictionary<string, Type> _driverTypeCache = new(StringComparer.Ordinal);

        private bool _disposed;

        /// <summary>
        /// 配置是否被改过（由相机设置界面在编辑后置位）。
        /// 用它 + "方案对象变了 / 相机数量变了" 两个廉价判据来决定要不要重新对齐 ——
        /// 见 <see cref="EnsureSynced"/> 的说明。
        /// </summary>
        private int _configDirty;

        /// <summary>上次对齐时的方案对象（换方案 = 整批相机可能要换）</summary>
        private SolutionModel _lastSyncedSolution;

        /// <summary>上次对齐时的相机数量（增删一台就足以判定需要重新对齐）</summary>
        private int _lastSyncedCount = -1;

        public CameraProvider(ILogService log, IReadOnlyWorkspaceContext workspace, IPluginProvider plugins)
        {
            _log = log;
            _workspace = workspace;
            _plugins = plugins;
        }

        /// <summary>设备表发生增删/重建后触发（界面订阅刷新列表）</summary>
        public event EventHandler DevicesChanged;

        #region 配置视图（来自当前方案）

        /// <inheritdoc />
        public IReadOnlyList<CameraDescriptor> Cameras
        {
            get
            {
                var list = _workspace?.CurrentSolution?.CameraConfigs;
                if (list == null) return Array.Empty<CameraDescriptor>();

                lock (_gate) return list.ToList();
            }
        }

        /// <summary>序列号是否可用（新增/改名时校验唯一性；<paramref name="excludeId"/> 用于"改自己"时排除自身）</summary>
        public bool IsSerialAvailable(string serialNo, Guid excludeId)
        {
            var key = NormalizeSerial(serialNo);
            if (key.Length == 0) return false;

            lock (_gate)
            {
                var list = _workspace?.CurrentSolution?.CameraConfigs;
                if (list == null) return true;

                return !list.Any(c =>
                    c.Id != excludeId &&
                    string.Equals(NormalizeSerial(c.SerialNo), key, StringComparison.OrdinalIgnoreCase));
            }
        }

        #endregion

        #region 驱动发现

        /// <summary>
        /// 可用相机驱动列表（来自 PluginService 扫描到的相机插件表）。
        /// 每次都重新解析而不缓存结果：插件是启动期一次性扫描的，但"类型能否被解析出来"
        /// 受程序集加载时机影响，缓存一个空的结论会让界面永远显示"没有可用驱动"。
        /// </summary>
        public IReadOnlyList<CameraDriverInfo> AvailableDrivers
        {
            get
            {
                var result = new List<CameraDriverInfo>();

                var table = _plugins?.CameraPlugins;
                if (table == null) return result;

                foreach (var kv in table)
                {
                    var typeKey = kv.Value?.ModuleTypeName;
                    if (string.IsNullOrWhiteSpace(typeKey)) continue;

                    var type = ResolveDriverType(typeKey);
                    if (type == null || !typeof(ICameraDevice).IsAssignableFrom(type))
                    {
                        _log?.Warn($"[CameraProvider] 驱动「{kv.Key}」的类型无法解析或未实现 ICameraDevice：{typeKey}");
                        continue;
                    }

                    var att = type.GetCustomAttribute<DisplayAttribute>();
                    result.Add(new CameraDriverInfo
                    {
                        TypeKey = typeKey,
                        DisplayName = att?.Name ?? kv.Key,
                        Description = att?.Description ?? string.Empty,
                        DriverType = type
                    });
                }

                return result;
            }
        }

        /// <summary>
        /// 按类型键解析驱动类型。
        /// 先走 Type.GetType（覆盖"已加载程序集"的常规情况），失败再遍历已加载程序集按全名找：
        /// 插件是 Assembly.LoadFrom 进来的，其程序集未必在探测路径上，
        /// 只靠 Type.GetType 会在某些机器上偶发解析不到——那会表现为"插件加载成功但相机建不出来"。
        /// </summary>
        private Type ResolveDriverType(string typeKey)
        {
            lock (_gate)
            {
                if (_driverTypeCache.TryGetValue(typeKey, out var cached)) return cached;
            }

            var type = Type.GetType(typeKey, throwOnError: false);
            if (type == null)
            {
                var fullName = typeKey.Split(',')[0].Trim();
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        type = asm.GetType(fullName, throwOnError: false);
                    }
                    catch
                    {
                        // 个别动态程序集反射会抛，跳过即可
                    }
                    if (type != null) break;
                }
            }

            if (type != null)
            {
                lock (_gate) { _driverTypeCache[typeKey] = type; }
            }

            return type;
        }

        #endregion

        #region 运行态设备查找（ICameraProvider）

        /// <inheritdoc />
        public bool TryGetDevice(Guid cameraId, out ICameraDevice device)
        {
            EnsureSynced();

            lock (_gate)
            {
                return _devices.TryGetValue(cameraId, out device);
            }
        }

        /// <inheritdoc />
        public bool TryGetDeviceBySerial(string serialNo, out ICameraDevice device)
        {
            EnsureSynced();

            device = null;

            var key = NormalizeSerial(serialNo);
            if (key.Length == 0) return false;

            lock (_gate)
            {
                if (!_serialIndex.TryGetValue(key, out var id)) return false;
                return _devices.TryGetValue(id, out device);
            }
        }

        /// <summary>
        /// 相机设置界面在编辑（增删改）后调用，标记"需要重新对齐"。
        /// 界面自己不直接调 SyncFromSolution：编辑是连续动作（改个名、调个曝光），
        /// 每次改都对齐一遍会反复销毁重建设备、把连接断掉。标记 + 下一次取用时对齐，代价最小。
        /// </summary>
        public void MarkConfigDirty() => Volatile.Write(ref _configDirty, 1);

        /// <summary>
        /// 按需对齐运行态设备表。
        ///
        /// 为什么是"按需"而不是"只在方案加载时同步一次"
        /// ---------
        /// 插件通过 <c>IExecutionContext.Cameras</c> 取相机，而这个动作发生在**流程线程**上，
        /// 那时界面可能刚加了一台相机还没保存。只在加载时同步一次，会出现
        /// "界面里明明有这台相机，流程却报找不到"——而用户认为自己已经配好了。
        /// 这里用三个廉价判据（脏位、方案对象、相机数量）在每次取用时兜底，
        /// 对齐本身只在判据变化时才真正执行，热路径上只是三次字段读取。
        /// </summary>
        public void EnsureSynced()
        {
            if (_disposed) return;

            var solution = _workspace?.CurrentSolution;
            var count = solution?.CameraConfigs?.Count ?? 0;

            if (Volatile.Read(ref _configDirty) == 0 &&
                ReferenceEquals(solution, _lastSyncedSolution) &&
                count == _lastSyncedCount)
                return;

            SyncFromSolution();
        }

        #endregion

        #region 与方案配置对齐

        /// <summary>
        /// 把运行态设备表对齐到当前方案的相机配置。返回过程中的错误说明（供界面提示）。
        ///
        /// 重建判据（Id 不变但下列任一变化 → 销毁重建）：
        ///   驱动类型变了（换了厂商）、序列号变了（指向了另一台物理设备）。
        /// 只改显示名/备注/参数不重建——重建会把连接断掉、队列清空，而用户只是想改个名字。
        /// </summary>
        public IReadOnlyList<string> SyncFromSolution()
        {
            var errors = new List<string>();
            if (_disposed) return errors;

            var configs = _workspace?.CurrentSolution?.CameraConfigs;
            if (configs == null)
            {
                // 没有方案：把所有运行态设备收掉，避免"方案关了但相机还连着"
                ReleaseAll();
                RememberSynced(null, 0);
                return errors;
            }

            List<ICameraDevice> toDispose = new();

            lock (_gate)
            {
                var wanted = new HashSet<Guid>(configs.Select(c => c.Id));

                // ① 方案里没有的：销毁（用户删了相机，物理连接必须跟着断）
                foreach (var id in _devices.Keys.Where(id => !wanted.Contains(id)).ToList())
                {
                    toDispose.Add(_devices[id]);
                    _devices.Remove(id);
                }

                // ② 方案里有的：按需新建 / 重建
                foreach (var descriptor in configs)
                {
                    if (_devices.TryGetValue(descriptor.Id, out var existing) &&
                        string.Equals(existing.Descriptor.DriverTypeKey, descriptor.DriverTypeKey, StringComparison.Ordinal) &&
                        string.Equals(NormalizeSerial(existing.Descriptor.SerialNo), NormalizeSerial(descriptor.SerialNo), StringComparison.OrdinalIgnoreCase))
                    {
                        // 驱动与序列号都没变：把最新配置灌进设备（参数/显示名可能改过）
                        existing.Descriptor.DisplayName = descriptor.DisplayName;
                        existing.Descriptor.Remarks = descriptor.Remarks;
                        existing.Descriptor.AutoConnect = descriptor.AutoConnect;
                        existing.Descriptor.Settings = descriptor.Settings?.Clone() ?? new CameraSettings();
                        continue;
                    }

                    if (existing != null)
                    {
                        toDispose.Add(existing);
                        _devices.Remove(descriptor.Id);
                    }

                    var device = CreateDevice(descriptor, errors);
                    if (device != null) _devices[descriptor.Id] = device;
                }

                RebuildSerialIndexLocked();
            }

            // Dispose 放锁外：Close() 会回调驱动、可能耗时（真机要关句柄），持锁调用会把取图线程全堵住
            foreach (var device in toDispose)
            {
                try { device.Dispose(); }
                catch (Exception ex) { _log?.Warn($"[CameraProvider] 释放相机「{device.Descriptor?.Caption}」失败：{ex.Message}"); }
            }

            DevicesChanged?.Invoke(this, EventArgs.Empty);
            RememberSynced(_workspace?.CurrentSolution, configs.Count);
            return errors;
        }

        /// <summary>记下本次对齐的依据，并清掉脏位（否则 EnsureSynced 每帧都会重新对齐一遍）</summary>
        private void RememberSynced(SolutionModel solution, int count)
        {
            _lastSyncedSolution = solution;
            _lastSyncedCount = count;
            Volatile.Write(ref _configDirty, 0);
        }

        /// <summary>
        /// 自动连接：把所有勾了"自动连接"的相机连上并开始采流。
        /// 失败只记日志不中断——一台相机连不上不该让其它相机也起不来。
        /// </summary>
        public void ConnectAutoStart()
        {
            List<ICameraDevice> targets;
            lock (_gate)
            {
                targets = _devices.Values.Where(d => d.Descriptor.AutoConnect).ToList();
            }

            foreach (var device in targets)
            {
                try
                {
                    if (!device.Open())
                    {
                        _log?.Warn($"[CameraProvider] 自动连接相机「{device.Descriptor.Caption}」失败：{device.StateDetail}");
                        continue;
                    }
                    device.StartStream();
                }
                catch (Exception ex)
                {
                    _log?.Warn($"[CameraProvider] 自动连接相机「{device.Descriptor.Caption}」异常：{ex.Message}");
                }
            }
        }

        /// <summary>断开并释放全部运行态设备（关闭方案 / 退出程序时调用）</summary>
        public void ReleaseAll()
        {
            List<ICameraDevice> all;
            lock (_gate)
            {
                all = _devices.Values.ToList();
                _devices.Clear();
                _serialIndex.Clear();
            }

            foreach (var device in all)
            {
                try { device.Dispose(); }
                catch (Exception ex) { _log?.Warn($"[CameraProvider] 释放相机失败：{ex.Message}"); }
            }

            if (all.Count > 0) DevicesChanged?.Invoke(this, EventArgs.Empty);
        }

        private ICameraDevice CreateDevice(CameraDescriptor descriptor, List<string> errors)
        {
            var driver = AvailableDrivers.FirstOrDefault(d =>
                string.Equals(d.TypeKey, descriptor.DriverTypeKey, StringComparison.Ordinal));

            if (driver == null)
            {
                var message = $"相机「{descriptor.Caption}」的驱动「{descriptor.DriverTypeKey}」不可用（插件未加载或被删除）";
                errors.Add(message);
                _log?.Error($"[CameraProvider] {message}");
                return null;
            }

            try
            {
                // 驱动契约：必须提供 Xxx(CameraDescriptor) 构造函数（CameraDeviceBase 的基构造强制了这一形状）
                if (Activator.CreateInstance(driver.DriverType, new object[] { descriptor }) is not ICameraDevice device)
                {
                    var message = $"驱动「{driver.DisplayName}」不是有效的 ICameraDevice";
                    errors.Add(message);
                    _log?.Error($"[CameraProvider] {message}");
                    return null;
                }

                // 日志注入：驱动本体不引用宿主，日志只能由这里塞进去
                if (device is CameraDeviceBase baseDevice) baseDevice.Log = _log;

                return device;
            }
            catch (Exception ex)
            {
                var message = $"创建相机「{descriptor.Caption}」失败：{ex.GetType().Name}: {ex.Message}";
                errors.Add(message);
                _log?.Error($"[CameraProvider] {message}");
                return null;
            }
        }

        /// <summary>重建序列号索引（调用方必须持 _gate）</summary>
        private void RebuildSerialIndexLocked()
        {
            _serialIndex.Clear();
            foreach (var kv in _devices)
            {
                var key = NormalizeSerial(kv.Value.Descriptor.SerialNo);
                if (key.Length == 0) continue;

                // 序列号重复时保留先到者并告警：两个同序列号的设备会让"按序列号取图"变成随机命中，
                // 现场表现是"A 相机的图跑到 B 流程里"，最难查。界面侧另有 IsSerialAvailable 提前拦。
                if (_serialIndex.ContainsKey(key))
                {
                    _log?.Warn($"[CameraProvider] 序列号「{key}」重复，按序列号取图将只命中先注册的那一台；请到「系统 → 相机设置」修正");
                    continue;
                }
                _serialIndex[key] = kv.Key;
            }
        }

        private static string NormalizeSerial(string serialNo)
            => string.IsNullOrWhiteSpace(serialNo) ? string.Empty : serialNo.Trim();

        #endregion

        #region 释放

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ReleaseAll();
        }

        #endregion
    }
}
