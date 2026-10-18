using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Core.Interfaces;
using VisionMaster.Binding;
using VisionMaster.Scada;

namespace VisionMaster.Services.Motion
{
    /// <summary>
    /// 运动轴状态发布器：把"运动卡的轮询快照"投影成 SCADA 能读的只读数据源。
    ///
    /// 它在整条链路里的位置
    /// ---------
    /// <code>
    ///   IMotionDevice.PollStatusCore（驱动自己的轮询线程，默认 10ms）
    ///        │  GetAxisStatus（读缓存，不发通信）
    ///        ▼
    ///   MotionStatePublisher（本类，100ms 一拍）
    ///        │  不可变快照交换
    ///        ▼
    ///   IScadaAxisSource  →  轴面板（按统一节拍拉当前轴）
    /// </code>
    ///
    /// 三条设计口径
    /// ---------
    /// ① <b>拉模式、不是推模式</b>：运动卡 10ms 轮询一次，若按值变化推事件，
    ///    一屏几十个轴面板会把 UI 线程刷满。这里只维护"最近一次快照"，
    ///    消费方在自己的节拍里读（轴面板搭 <see cref="ScadaBeatSource"/>）。
    /// ② <b>每拍每物理轴只读一次</b>：<c>GetAxisStatus</c> 读的是驱动缓存，不发通信；
    ///    即使几十个面板都绑同一根轴，也只在发布器里读一次。
    /// ③ <b>离线不编 0</b>：拿不到快照时给 <see cref="ScadaAxisSample.Invalid"/>，
    ///    让面板显示"未知"，而不是把"不知道"伪装成"在原点"。
    ///
    /// 线程
    /// ---------
    /// 定时器回调在后台线程；<see cref="TryGetInfo"/> / <see cref="TryGetSample"/>
    /// 用"整表交换 + 锁内取引用"的方式读，不长时间持锁。
    /// <see cref="Changed"/> 也在后台线程触发，消费方若要碰 UI 必须自己切线程
    /// （轴面板刻意不订阅它，而是在节拍里按 <see cref="Version"/> 判断）。
    /// </summary>
    public sealed class MotionStatePublisher : IScadaAxisSource, IDisposable
    {
        /// <summary>发布周期。100ms 是"操作员看着不卡、又不至于把 UI 刷满"的折中；
        /// 真正的实时性由运动卡自己的 10ms 轮询保证，这里只是 SCADA 的采样节拍。</summary>
        private const int PublishIntervalMs = 100;

        private readonly ILogService _log;
        private readonly IMotionProvider _motions;
        private readonly MotionAxisRegistry _registry;

        /// <summary>
        /// 系统变量目录（可选）：把每拍快照同时投影成只读的 <c>Motion.{轴名}.{字段}</c> 变量，
        /// 供插件 / SCADA / 报警 / 监视统一按"变量"读取，不必各自认识运动模块。
        /// 为 null 时本类行为与从前完全一致（断言宿主 / 无工作区的简化容器就是这么用的）。
        /// </summary>
        private readonly SystemVariableCatalog? _systemVariables;

        /// <summary>整表交换的闸：读方只取引用，写方整表替换</summary>
        private readonly object _swapGate = new();

        private IReadOnlyDictionary<Guid, ScadaAxisInfo> _infos =
            new Dictionary<Guid, ScadaAxisInfo>();

        private IReadOnlyDictionary<Guid, ScadaAxisSample> _samples =
            new Dictionary<Guid, ScadaAxisSample>();

        private string _fingerprint = string.Empty;
        private int _version;
        private int _tickBusy;
        private Timer? _timer;
        private bool _disposed;

        /// <summary>
        /// 构造发布器。
        /// </summary>
        /// <param name="log">日志</param>
        /// <param name="motions">运动卡提供者（本类是唯一接触它的地方之一）</param>
        /// <param name="registry">轴注册表（派生索引，方案一变自刷新）</param>
        /// <param name="systemVariables">
        /// 系统变量目录（**可选**）：给了就把每拍快照同时投影成只读的
        /// <c>Motion.{轴名}.{字段}</c> 变量；不给则本类行为与从前完全一致
        /// （断言宿主与无工作区的简化容器走这一档）。
        /// </param>
        public MotionStatePublisher(
            ILogService log,
            IMotionProvider motions,
            MotionAxisRegistry registry,
            SystemVariableCatalog? systemVariables = null)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _motions = motions ?? throw new ArgumentNullException(nameof(motions));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _systemVariables = systemVariables;
        }

        /// <inheritdoc />
        public int Version => Volatile.Read(ref _version);

        /// <inheritdoc />
        public event EventHandler? Changed;

        /// <summary>启动周期发布（幂等）</summary>
        public void Start()
        {
            if (_disposed || _timer != null) return;

            _timer = new Timer(_ => Tick(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(PublishIntervalMs));
        }

        /// <summary>停止发布（幂等；不释放快照，已打开的画面仍可读到最后一帧）</summary>
        public void Stop()
        {
            _timer?.Dispose();
            _timer = null;
        }

        /// <summary>
        /// 立刻发布一拍（同步）。正常路径由定时器驱动；这是给自检/诊断用的入口——
        /// 与 <c>ScadaRuntimeBinder.FlushNow</c> 同一角色，避免断言里靠 sleep 等定时器。
        /// </summary>
        public void RefreshNow() => Tick();

        private void Tick()
        {
            if (_disposed) return;

            // 上一拍还没跑完就跳过这一拍：宁可少采一次，也不让回调叠着跑。
            if (Interlocked.Exchange(ref _tickBusy, 1) != 0) return;

            try
            {
                Publish();
            }
            catch (Exception ex)
            {
                // 发布器是后台诊断链路，绝不能把应用带下去
                _log?.Warn($"[MotionStatePublisher] 轴状态发布失败（已跳过本拍）：{ex.Message}");
            }
            finally
            {
                Volatile.Write(ref _tickBusy, 0);
            }
        }

        private void Publish()
        {
            // 注册表是派生索引，方案/轴表一变它会自刷新；这里拿到的是不可变快照。
            var bindings = _registry.Snapshot();

            var infos = new Dictionary<Guid, ScadaAxisInfo>(bindings.Count);
            var samples = new Dictionary<Guid, ScadaAxisSample>(bindings.Count);
            var fingerprint = new StringBuilder();

            foreach (var binding in bindings)
            {
                Guid axisId = binding.AxisId;
                if (axisId == Guid.Empty) continue; // 注册表会补历史身份；这里只是防御

                bool hasDevice = _motions.TryGetDevice(binding.CardId, out var device);
                bool online = hasDevice && device.State == MotionCardState.Online;
                AxisStatus? status = online ? device.GetAxisStatus(binding.AxisIndex) : null;
                bool available = online && status != null;

                infos[axisId] = new ScadaAxisInfo(
                    axisId, binding.Name, binding.CardCaption, binding.Enabled, available);

                samples[axisId] = status == null
                    ? ScadaAxisSample.Invalid(hasDevice
                        ? $"运动卡「{binding.CardCaption}」当前为 {device.State}"
                        : $"运动卡「{binding.CardCaption}」未连接")
                    : new ScadaAxisSample(
                        IsValid: true,
                        Error: null,
                        PositionMm: status.PositionMm,
                        VelocityMmPerS: status.VelocityMmPerS,
                        Moving: status.Moving,
                        InPosition: status.InPosition,
                        Enabled: status.Enabled,
                        Alarm: status.Alarm,
                        PositiveLimit: status.PositiveLimit,
                        NegativeLimit: status.NegativeLimit,
                        Origin: status.Origin,
                        EmergencyStop: status.EmergencyStop,
                        StatusText: status.Describe(),
                        TimestampUtc: DateTime.UtcNow);

                // 拓扑指纹只含"身份/名字/可用性"，不含位置等量值：
                // 量值每拍都在变，把它算进来会让 Changed/Version 变成 10Hz 的噪音。
                fingerprint.Append(axisId.ToString("N")).Append('|')
                           .Append(binding.Name).Append('|')
                           .Append(binding.CardCaption).Append('|')
                           .Append(binding.Enabled ? '1' : '0').Append('|')
                           .Append(available ? '1' : '0').Append(';');
            }

            // 整表交换：读方要么看到旧表、要么看到新表，绝不会看到"改到一半"的表。
            lock (_swapGate)
            {
                _infos = infos;
                _samples = samples;
            }

            // 系统变量投影：与上面同一份快照，先按轴表重建（增删/改名自动跟进、幂等），
            // 再把每根轴的量值写进 12 个只读变量。
            //
            // 为什么放在快照交换之后：投影失败绝不能影响 IScadaAxisSource 这条既有链路
            // （轴面板只认 TryGetSample）。Rebuild 内部先比拓扑、没变化就直接返回，
            // 所以"每拍都调"不会造成每拍清空重建集合。
            PublishSystemVariables(bindings, samples);

            string current = fingerprint.ToString();
            if (!string.Equals(current, _fingerprint, StringComparison.Ordinal))
            {
                _fingerprint = current;
                Interlocked.Increment(ref _version);
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// 把本拍快照投影成系统变量（<c>Motion.{轴名}.{字段}</c>）。
        ///
        /// 两条纪律：
        /// ① **先 Rebuild、再逐轴 Publish**：轴表变化（新增/删除/改名）由 Rebuild 跟进，
        ///    Publish 只负责把值写进已经存在的模型；顺序反了会在"新加的轴"上白写一拍。
        /// ② **失败只记日志、不外抛**：系统变量是给插件/画面读的便利通道，
        ///    它坏了不该把轴面板（IScadaAxisSource）一起带下水。
        /// </summary>
        private void PublishSystemVariables(
            IReadOnlyList<MotionAxisBinding> bindings,
            IReadOnlyDictionary<Guid, ScadaAxisSample> samples)
        {
            if (_systemVariables == null) return;

            try
            {
                _systemVariables.Rebuild(bindings);

                foreach (var pair in samples)
                    _systemVariables.Publish(pair.Key, pair.Value);
            }
            catch (Exception ex)
            {
                _log?.Warn($"[MotionStatePublisher] 系统变量投影失败（轴面板不受影响）：{ex.Message}");
            }
        }

        /// <inheritdoc />
        public bool TryGetInfo(Guid axisId, out ScadaAxisInfo info)
        {
            info = null!;
            if (axisId == Guid.Empty) return false;

            IReadOnlyDictionary<Guid, ScadaAxisInfo> snapshot;
            lock (_swapGate) snapshot = _infos;

            return snapshot.TryGetValue(axisId, out info!);
        }

        /// <inheritdoc />
        public bool TryGetSample(Guid axisId, out ScadaAxisSample sample)
        {
            sample = default;
            if (axisId == Guid.Empty) return false;

            IReadOnlyDictionary<Guid, ScadaAxisSample> snapshot;
            lock (_swapGate) snapshot = _samples;

            return snapshot.TryGetValue(axisId, out sample);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Stop();

            lock (_swapGate)
            {
                _infos = new Dictionary<Guid, ScadaAxisInfo>();
                _samples = new Dictionary<Guid, ScadaAxisSample>();
            }
        }
    }
}
