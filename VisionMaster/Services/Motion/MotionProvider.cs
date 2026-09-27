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
    /// 一个可用的运动卡驱动（= 一个驱动插件类型），供「运动卡设置」界面让用户选"厂商/型号"。
    /// 与 <see cref="CameraDriverInfo"/> 同形。
    /// </summary>
    public sealed class MotionDriverInfo
    {
        /// <summary>类型键（AssemblyQualifiedName）。与 <see cref="MotionDescriptor.DriverTypeKey"/> 同口径</summary>
        public string TypeKey { get; init; } = string.Empty;

        /// <summary>给用户看的名字（驱动类上 [Display].Name）</summary>
        public string DisplayName { get; init; } = string.Empty;

        /// <summary>一句话说明</summary>
        public string Description { get; init; } = string.Empty;

        /// <summary>驱动类型（创建实例用）</summary>
        public Type DriverType { get; init; } = null!;
    }

    /// <summary>
    /// 运动卡仓库：方案级运动卡配置 ⇄ 运行态设备实例的唯一对接处。
    ///
    /// 与 <see cref="CameraProvider"/> 同构（同三段职责、同一套"按需对齐"判据），
    /// 差别只有两处，都是运动本身带来的：
    ///   1. <b>寻址键是地址（IP/槽位）而不是序列号</b>。运动卡没有序列号，
    ///      但它有一个天然唯一、用户也读得到的身份：连接地址。流程步骤按它引用设备。
    ///   2. <b>自动连接之后还要考虑回零</b>。增量式编码器的卡连上后位置不可信，
    ///      而"要不要强制回零"取决于驱动上报的能力位（SupportsAbsoluteEncoder），
    ///      这里只把结果写进日志与状态，不在宿主层替用户决定流程该怎么走。
    /// </summary>
    public sealed class MotionProvider : IMotionProvider, IDisposable
    {
        private readonly ILogService _log;
        private readonly IReadOnlyWorkspaceContext _workspace;
        private readonly IPluginProvider _plugins;

        private readonly object _gate = new();

        /// <summary>运行态设备表（按内部 Id）</summary>
        private readonly Dictionary<Guid, IMotionDevice> _devices = new();

        /// <summary>地址 → Id 的寻址索引。与 <see cref="TryGetByKey"/> 同源</summary>
        private readonly Dictionary<string, Guid> _addressIndex = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>已解析过的驱动类型缓存（键 = AssemblyQualifiedName）</summary>
        private readonly Dictionary<string, Type> _driverTypeCache = new(StringComparer.Ordinal);

        private bool _disposed;

        /// <summary>配置是否被改过（由运动卡设置界面在编辑后置位），见 <see cref="EnsureSynced"/></summary>
        private int _configDirty;

        /// <summary>
        /// 同步重入闸（见 <see cref="EnsureSynced"/>）：1 = 正在对齐设备表。
        /// 没有它时，"同步 → 广播事件 → 事件里读设备 → 再同步"会无限递归，
        /// 直接以 StackOverflowException 结束进程（无法捕获、没有堆栈可查）。
        /// </summary>
        private int _syncing;

        /// <summary>上次对齐时的方案对象（换方案 = 整批卡可能要换）</summary>
        private SolutionModel? _lastSyncedSolution;

        /// <summary>上次对齐时的卡数量</summary>
        private int _lastSyncedCount = -1;

        public MotionProvider(ILogService log, IReadOnlyWorkspaceContext workspace, IPluginProvider plugins)
        {
            _log = log;
            _workspace = workspace;
            _plugins = plugins;
        }

        /// <summary>设备表发生增删/重建后触发（界面订阅刷新列表）</summary>
        public event EventHandler? DevicesChanged;

        #region 配置视图（来自当前方案）

        /// <inheritdoc />
        public IReadOnlyList<MotionDescriptor> Cards
        {
            get
            {
                var list = _workspace?.CurrentSolution?.MotionCards;
                if (list == null) return Array.Empty<MotionDescriptor>();

                lock (_gate) return list.ToList();
            }
        }

        /// <summary>地址是否可用（新增/编辑时校验唯一性；<paramref name="excludeId"/> 用于"改自己"时排除自身）</summary>
        public bool IsAddressAvailable(string address, Guid excludeId)
        {
            var key = NormalizeAddress(address);
            if (key.Length == 0) return false;

            lock (_gate)
            {
                var list = _workspace?.CurrentSolution?.MotionCards;
                if (list == null) return true;

                return !list.Any(c =>
                    c.Id != excludeId &&
                    string.Equals(NormalizeAddress(c.Address), key, StringComparison.OrdinalIgnoreCase));
            }
        }

        #endregion

        #region 驱动发现

        /// <summary>
        /// 可用运动卡驱动列表（来自 PluginService 扫描到的运动插件表）。
        /// 与相机同口径：每次都重新解析而不缓存结论 —— 缓存一个空的结论会让界面永远显示"没有可用驱动"。
        /// </summary>
        public IReadOnlyList<MotionDriverInfo> AvailableDrivers
        {
            get
            {
                var result = new List<MotionDriverInfo>();

                var table = _plugins?.MotionPlugins;
                if (table == null) return result;

                foreach (var kv in table)
                {
                    var typeKey = kv.Value?.ModuleTypeName;
                    if (string.IsNullOrWhiteSpace(typeKey)) continue;

                    var type = ResolveDriverType(typeKey);
                    if (type == null || !typeof(IMotionDevice).IsAssignableFrom(type))
                    {
                        _log?.Warn($"[MotionProvider] 驱动「{kv.Key}」的类型无法解析或未实现 IMotionDevice：{typeKey}");
                        continue;
                    }

                    var att = type.GetCustomAttribute<DisplayAttribute>();
                    result.Add(new MotionDriverInfo
                    {
                        TypeKey = typeKey,
                        DisplayName = att?.Name ?? kv.Key,
                        Description = att?.Description ?? string.Empty,
                        DriverType = type,
                    });
                }

                return result;
            }
        }

        /// <summary>
        /// 按类型键解析驱动类型。先 Type.GetType，失败再遍历已加载程序集按全名找 ——
        /// 插件是 Assembly.LoadFrom 进来的，其程序集未必在探测路径上，
        /// 只靠 Type.GetType 会表现为"插件加载成功但卡建不出来"。
        /// </summary>
        private Type? ResolveDriverType(string typeKey)
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

        #region 运行态设备查找（IMotionProvider）

        /// <inheritdoc />
        public bool TryGetDevice(Guid cardId, out IMotionDevice device)
        {
            EnsureSynced();

            lock (_gate)
            {
                return _devices.TryGetValue(cardId, out device!);
            }
        }

        /// <inheritdoc />
        public bool TryGetByKey(string address, out IMotionDevice device)
        {
            EnsureSynced();

            device = null!;

            var key = NormalizeAddress(address);
            if (key.Length == 0) return false;

            lock (_gate)
            {
                if (!_addressIndex.TryGetValue(key, out var id)) return false;
                return _devices.TryGetValue(id, out device!);
            }
        }

        /// <inheritdoc />
        public bool TryGetByName(string displayName, out IMotionDevice device)
        {
            EnsureSynced();

            device = null!;
            if (string.IsNullOrWhiteSpace(displayName)) return false;

            lock (_gate)
            {
                foreach (var kv in _devices)
                {
                    if (string.Equals(kv.Value.Descriptor.DisplayName, displayName, StringComparison.Ordinal))
                    {
                        device = kv.Value;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 运动卡设置界面在编辑（增删改）后调用，标记"需要重新对齐"。
        /// 界面不直接调 <see cref="SyncFromSolution"/>：编辑是连续动作，
        /// 每次改都对齐一遍会反复销毁重建设备、把连接断掉。
        /// </summary>
        public void MarkConfigDirty() => Volatile.Write(ref _configDirty, 1);

        /// <summary>
        /// 按需对齐运行态设备表（三个廉价判据：脏位 / 方案对象 / 卡数量）。
        /// 与相机同理：插件在**流程线程**上经 <c>IExecutionContext.Motions</c> 取设备，
        /// 那时界面可能刚加了一张卡还没保存 —— 只在方案加载时同步一次会出现
        /// "界面里明明有这张卡，流程却报找不到"。
        /// </summary>
        public void EnsureSynced()
        {
            if (_disposed) return;

            // ★ 重入闸（不是优化，是必需的）：
            //   SyncFromSolution 会广播 DevicesChanged，而事件处理里通常立刻读设备
            //   （"同步完了，刷新一下显示"）；读设备又回到 TryGetDevice → EnsureSynced。
            //   若此时同步状态还没复位，就会再进一次 SyncFromSolution → 再广播 → 栈一路加深，
            //   最终 StackOverflowException（.NET 里**无法捕获**，进程直接消失、没有堆栈）。
            //   实测：在「运动卡设置」里改轴参数必崩 —— 改动会 MarkConfigDirty，每次按键都置脏位。
            //   有了它，同步期间再进来的调用直接返回：它想要的结果，
            //   由**正在进行的那次**同步产生，不需要也无法重入。
            if (Interlocked.CompareExchange(ref _syncing, 1, 0) != 0) return;

            try
            {
                var solution = _workspace?.CurrentSolution;
                var count = solution?.MotionCards?.Count ?? 0;

                if (Volatile.Read(ref _configDirty) == 0 &&
                    ReferenceEquals(solution, _lastSyncedSolution) &&
                    count == _lastSyncedCount)
                    return;

                SyncFromSolution();
            }
            finally
            {
                Volatile.Write(ref _syncing, 0);
            }
        }

        #endregion

        #region 与方案配置对齐

        /// <summary>
        /// 把运行态设备表对齐到当前方案的运动卡配置。返回过程中的错误说明（供界面提示）。
        ///
        /// 重建判据（Id 不变但下列任一变化 → 销毁重建）：
        ///   驱动类型变了（换厂商）、地址变了（指向了另一张物理卡）。
        /// 只改显示名/备注/参数/轴映射不重建 —— 重建会把连接断掉、队列清空，而用户只是想改个名字或限位。
        /// （轴映射是"运行时从 Descriptor 里读"的，改了立即生效，不需要重建。）
        /// </summary>
        public IReadOnlyList<string> SyncFromSolution()
        {
            var errors = new List<string>();
            if (_disposed) return errors;

            var configs = _workspace?.CurrentSolution?.MotionCards;
            if (configs == null)
            {
                // 没有方案：把所有运行态设备收掉，避免"方案关了但卡还连着"
                ReleaseAll();
                RememberSynced(null, 0);
                return errors;
            }

            List<IMotionDevice> toDispose = new();

            lock (_gate)
            {
                var wanted = new HashSet<Guid>(configs.Select(c => c.Id));

                // ① 方案里没有的：销毁（用户删了卡，物理连接必须跟着断）
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
                        string.Equals(NormalizeAddress(existing.Descriptor.Address), NormalizeAddress(descriptor.Address), StringComparison.OrdinalIgnoreCase))
                    {
                        // 驱动与地址都没变：把最新配置灌进设备（参数/显示名/轴映射可能改过）
                        existing.Descriptor.DisplayName = descriptor.DisplayName;
                        existing.Descriptor.Remarks = descriptor.Remarks;
                        existing.Descriptor.AutoConnect = descriptor.AutoConnect;
                        existing.Descriptor.CardModel = descriptor.CardModel;
                        existing.Descriptor.Axes = descriptor.Axes;
                        existing.Descriptor.Params = descriptor.Params;
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

                RebuildAddressIndexLocked();
            }

            // Dispose 放锁外：Disconnect() 会安全停轴并关句柄，可能耗时，持锁会把状态轮询全堵住
            foreach (var device in toDispose)
            {
                try { device.Dispose(); }
                catch (Exception ex) { _log?.Warn($"[MotionProvider] 释放运动卡「{device.Descriptor?.Caption}」失败：{ex.Message}"); }
            }

            // ★ 顺序：**先记状态、再广播事件**。
            //   事件处理里通常会立刻读设备（刷新显示），而读设备会回到 EnsureSynced ——
            //   若此时脏位还没清、方案引用也还没更新，那次调用会认为"仍需要同步"，
            //   于是再进一次 SyncFromSolution、再广播…… 栈无限加深（StackOverflowException）。
            //   反过来之后，事件处理里的那次 EnsureSynced 因"三个判据都不变"直接返回。
            RememberSynced(_workspace?.CurrentSolution, configs.Count);
            DevicesChanged?.Invoke(this, EventArgs.Empty);
            return errors;
        }

        /// <summary>记下本次对齐的依据，并清掉脏位（否则 EnsureSynced 每次都重新对齐）</summary>
        private void RememberSynced(SolutionModel? solution, int count)
        {
            _lastSyncedSolution = solution;
            _lastSyncedCount = count;
            Volatile.Write(ref _configDirty, 0);
        }

        /// <summary>
        /// 自动连接：把所有勾了"自动连接"的卡连上。
        /// 失败只记日志不中断 —— 一张卡连不上不该让别的卡也起不来。
        ///
        /// 连上之后额外做一件事：**把"是否需要回零"写进日志**。
        /// 增量式编码器的卡连上时位置是不可信的，此时若流程直接 MoveAbs，
        /// 卡会按错误的当前坐标算行程 —— 这是最危险的一类静默错误，必须在日志里说清。
        /// </summary>
        public void ConnectAutoStart()
        {
            List<IMotionDevice> targets;
            lock (_gate)
            {
                targets = _devices.Values.Where(d => d.Descriptor.AutoConnect).ToList();
            }

            foreach (var device in targets)
            {
                try
                {
                    if (!device.Connect())
                    {
                        _log?.Warn($"[MotionProvider] 自动连接运动卡「{device.Descriptor.Caption}」失败：{device.StateDetail}");
                        continue;
                    }

                    if (!device.Capabilities.SupportsAbsoluteEncoder)
                    {
                        _log?.Warn(
                            $"[MotionProvider] 运动卡「{device.Descriptor.Caption}」为增量式编码器："
                            + "当前位置不可信，流程开始前必须先回零（建议在流程首步放「轴回零」）");
                    }
                }
                catch (Exception ex)
                {
                    _log?.Warn($"[MotionProvider] 自动连接运动卡「{device.Descriptor.Caption}」异常：{ex.Message}");
                }
            }
        }

        /// <summary>断开并释放全部运行态设备（关闭方案 / 退出程序时调用）</summary>
        public void ReleaseAll()
        {
            List<IMotionDevice> all;
            lock (_gate)
            {
                all = _devices.Values.ToList();
                _devices.Clear();
                _addressIndex.Clear();
            }

            foreach (var device in all)
            {
                try { device.Dispose(); }
                catch (Exception ex) { _log?.Warn($"[MotionProvider] 释放运动卡失败：{ex.Message}"); }
            }

            if (all.Count > 0) DevicesChanged?.Invoke(this, EventArgs.Empty);
        }

        private IMotionDevice? CreateDevice(MotionDescriptor descriptor, List<string> errors)
        {
            var driver = AvailableDrivers.FirstOrDefault(d =>
                string.Equals(d.TypeKey, descriptor.DriverTypeKey, StringComparison.Ordinal));

            if (driver == null)
            {
                var message = $"运动卡「{descriptor.Caption}」的驱动「{descriptor.DriverTypeKey}」不可用（插件未加载或被删除）";
                errors.Add(message);
                _log?.Error($"[MotionProvider] {message}");
                return null;
            }

            try
            {
                // 驱动契约：必须提供 Xxx(MotionDescriptor) 构造函数（MotionDeviceBase 的基构造强制了这一形状）
                if (Activator.CreateInstance(driver.DriverType, new object[] { descriptor }) is not IMotionDevice device)
                {
                    var message = $"驱动「{driver.DisplayName}」不是有效的 IMotionDevice";
                    errors.Add(message);
                    _log?.Error($"[MotionProvider] {message}");
                    return null;
                }

                // 日志注入：驱动本体不引用宿主，日志只能由这里塞进去
                if (device is MotionDeviceBase baseDevice) baseDevice.Log = _log;

                return device;
            }
            catch (Exception ex)
            {
                var message = $"创建运动卡「{descriptor.Caption}」失败：{ex.GetType().Name}: {ex.Message}";
                errors.Add(message);
                _log?.Error($"[MotionProvider] {message}");
                return null;
            }
        }

        /// <summary>重建地址索引（调用方必须持 _gate）</summary>
        private void RebuildAddressIndexLocked()
        {
            _addressIndex.Clear();
            foreach (var kv in _devices)
            {
                var key = NormalizeAddress(kv.Value.Descriptor.Address);
                if (key.Length == 0) continue;

                // 地址重复时保留先到者并告警：两张卡同地址会让"按地址取设备"变成随机命中，
                // 现场表现是"命令发给 A 卡、动的是 B 卡"，最难查。界面侧另有 IsAddressAvailable 提前拦。
                if (_addressIndex.ContainsKey(key))
                {
                    _log?.Warn($"[MotionProvider] 运动卡地址「{key}」重复，按地址取设备将只命中先注册的那一张；请到「运动卡设置」修正");
                    continue;
                }
                _addressIndex[key] = kv.Key;
            }
        }

        private static string NormalizeAddress(string? address)
            => string.IsNullOrWhiteSpace(address) ? string.Empty : address.Trim();

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
