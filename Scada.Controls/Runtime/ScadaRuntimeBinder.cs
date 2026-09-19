using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Threading;
using VisionMaster.Scada;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 运行态数据泵：把「工程变量的值变化」变成「图元属性的变化」。
    ///
    /// 它在整条运行态链路上的位置
    /// ---------
    /// <code>
    ///   变量（后台轮询线程改值）
    ///        │  ValueChanged（可能在任意线程）
    ///        ▼
    ///   脏值暂存表（键 = 目标：控件 + 属性）      ← 只记"该写什么"，不碰控件
    ///        │  首次脏值把「排一次帧」的标志立起来（同一帧只排一次）
    ///        ▼
    ///   Dispatcher.BeginInvoke（切回 UI 线程，一次刷完）
    ///        ▼
    ///   ScadaElementBase.TryApplyRuntimeValue  →  控件依赖属性（不碰模型）
    ///        │  失败
    ///        ▼
    ///   ScadaDiagnosticOverlay 角标 + 日志回调
    /// </code>
    ///
    /// 四条纪律（对应 S6 的设计决策）
    /// ---------
    /// ① <b>写控件、不碰模型</b>：运行值只活在控件上，停止时重刷一遍就自动回到设计值。
    /// ② <b>脏值暂存 + 单次排队合并</b>：后台线程只写暂存表并立标志，UI 线程一次刷完。
    ///    20ms 周期里一个变量变十次，界面只重绘一次；十个变量同帧变，也只排一次 Dispatcher。
    /// ③ <b>按变量订阅一次 + 反向分发</b>：表结构是「变量 → 一批目标」，
    ///    同一个变量被十个图元绑着也只挂一个 handler、只解析一次。
    /// ④ <b>失败可见</b>：变量没解析到 → 橙角标；值转换失败 → 红角标；两者都写日志。
    ///
    /// 为什么不做历史值/趋势：那是另一条数据通路（要落库、要降采样），
    /// 塞进本类只会让"刷新一帧"这条热路径变重。本类只负责"此刻的值"。
    /// </summary>
    public sealed class ScadaRuntimeBinder
    {
        private readonly ScadaCanvas _canvas;
        private readonly IScadaValueSource _valueSource;
        private readonly ScadaPage _page;
        private readonly Action<ScadaDiagnosticLevel, string>? _report;
        private readonly Dispatcher _dispatcher;

        /// <summary>暂存表与排队标志的保护锁：值变化可能来自多个后台线程</summary>
        private readonly object _gate = new();

        /// <summary>变量 → 订阅（键见 <see cref="KeyOf"/>）。一个变量只有一份，多个目标共享</summary>
        private readonly Dictionary<string, Subscription> _subscriptions = new(StringComparer.Ordinal);

        /// <summary>脏值暂存：目标 → 待写的最新值（同一目标一帧内被写多次只留最后一次）</summary>
        private readonly Dictionary<Target, object?> _dirty = new();

        /// <summary>控件 → 该控件上各来源的诊断状态，用来算"该不该打点、打什么颜色"</summary>
        private readonly Dictionary<ScadaElementBase, Dictionary<object, Diagnostic>> _diagnostics = new();

        /// <summary>0/1 的排队标志，用 <see cref="Interlocked"/> 保证多线程下同一帧只排一次</summary>
        private int _flushQueued;

        private bool _running;

        /// <param name="canvas">承载画面的画布（建表靠它的 <see cref="ScadaCanvas.EnumerateControls"/>，
        /// 打点靠它的 <see cref="ScadaCanvas.Diagnostics"/>）</param>
        /// <param name="valueSource">变量值通道（领域层接口；VM.Core 侧提供注册表适配实现）</param>
        /// <param name="page">正在运行的画面（只用于日志文案里点名"哪个画面"）</param>
        /// <param name="report">诊断上报回调（宿主接日志用；角标之外的第二条记录）</param>
        public ScadaRuntimeBinder(
            ScadaCanvas canvas,
            IScadaValueSource valueSource,
            ScadaPage page,
            Action<ScadaDiagnosticLevel, string>? report = null)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            _valueSource = valueSource ?? throw new ArgumentNullException(nameof(valueSource));
            _page = page ?? throw new ArgumentNullException(nameof(page));
            _report = report;

            // Dispatcher 在建表期就取好：值变化回调里取的话，那时可能已经在别的线程上了
            _dispatcher = canvas.Dispatcher;
        }

        /// <summary>是否已建表（重复 Start 是空操作，重复 Stop 也是）</summary>
        public bool IsRunning => _running;

        /// <summary>已建表并成功解析的绑定条数（自检用）</summary>
        public int BoundCount { get; private set; }

        /// <summary>未能建表的绑定条数：变量没解析到、属性键不认识（自检用）</summary>
        public int MissCount { get; private set; }

        /// <summary>当前挂着的变量订阅数（一个变量一份；自检"摘表无残留"靠它）</summary>
        public int SubscriptionCount => _subscriptions.Count;

        /// <summary>
        /// 建表：扫一遍当前已渲染的图元控件与它们的绑定，解析变量、挂订阅、先刷一遍当前值。
        ///
        /// <b>必须在首帧渲染之后调用</b>——控件是在布局阶段才被造出来的
        /// （<c>OnApplyTemplate → RebuildElements</c>），会话 <c>Start()</c> 那一刻画布还是空的。
        /// 宿主的挂载点是窗口的 <c>ContentRendered</c>（见 ScadaRuntimeHost.OnFirstFrameRendered）。
        ///
        /// 为什么建完表要立刻刷一遍：操作员打开画面就该看到此刻的真实状态，
        /// 而不是等下一次变量变化——一个不常动的开关可能几分钟都不变一次。
        /// </summary>
        public void Start()
        {
            if (_running)
                return;

            _running = true;

            foreach (var control in _canvas.EnumerateControls())
            {
                if (control.Element is not { } element)
                    continue;

                foreach (var binding in element.Bindings)
                    BuildBinding(control, element, binding);
            }

            // 建表期收集的脏值在这里统一落一次（走同一条合并路径，不另开一条刷值代码）
            QueueFlushIfDirty();
        }

        /// <summary>
        /// 摘表：退掉全部订阅、清空暂存与诊断、把控件刷回设计值。
        ///
        /// 必须在窗口关闭时调用（ScadaRuntimeHost.OnWindowClosed）。<b>不退订阅就等于泄漏</b>：
        /// 变量注册表是长生命周期对象，它拿着控件的引用，窗口关一百次就留下一百份控件树。
        /// </summary>
        public void Stop()
        {
            if (!_running)
                return;

            _running = false;

            // 订阅是"一个变量一份"，所以这里逐个退一次就够，不需要引用计数
            foreach (var subscription in _subscriptions.Values)
                subscription.Handle.ValueChanged -= subscription.Handler;

            _subscriptions.Clear();

            lock (_gate)
                _dirty.Clear();

            _diagnostics.Clear();
            _canvas.Diagnostics.Clear();

            BoundCount = 0;
            MissCount = 0;

            // 停止 = 回到设计值：运行值只活在控件上，重刷一遍模型就自动盖掉它。
            // 这一步就是"写控件不写模型"换来的全部好处——不需要任何恢复现场的备份表。
            foreach (var control in _canvas.EnumerateControls())
                control.Refresh();
        }

        /// <summary>
        /// 立刻把暂存表刷到控件上。<b>必须在 UI 线程调用</b>。
        ///
        /// 正常路径用不到它（<see cref="QueueFlushIfDirty"/> 会自己排 Dispatcher）；
        /// 它是给自检工程用的：断言里没有消息循环，BeginInvoke 排进去的回调永远不会执行，
        /// 于是需要一个"同步刷一帧"的入口才能钉住刷新结果。
        /// </summary>
        public void FlushNow() => Flush();

        #region 建表

        private void BuildBinding(ScadaElementBase control, ScadaElement element, ScadaBinding binding)
        {
            if (binding is null || !binding.IsEnabled)
                return; // 停用的绑定不建表：既不订阅、也不打点（"临时摘掉一条绑定看现象"要的就是干净）

            if (string.IsNullOrWhiteSpace(binding.TargetProperty))
                return; // 还没选属性，不算错

            var property = ElementRegistry.FindProperty(element.TypeKey, binding.TargetProperty);
            if (property is null)
            {
                MissCount++;
                Report(control, binding, ScadaDiagnosticLevel.Warning,
                    $"画面「{_page.Name}」图元「{element.Name}」：属性「{binding.TargetProperty}」不在 {element.TypeKey} 的属性表里，这条绑定不会生效");
                return;
            }

            if (!_valueSource.TryResolve(binding.VariableId, binding.VariableName, out var handle) || handle is null)
            {
                MissCount++;
                Report(control, binding, ScadaDiagnosticLevel.Warning,
                    $"画面「{_page.Name}」图元「{element.Name}」：变量「{Describe(binding)}」没有找到，这条绑定不会刷新");
                return;
            }

            var subscription = GetOrAddSubscription(handle);

            var target = new Target
            {
                Control = control,
                Property = property,
                Format = binding.DisplayFormat,
                VariableName = handle.Name,
            };

            subscription.Targets.Add(target);

            // 建表即刷当前值：先把此刻的值写进暂存表，由 Start() 末尾那次统一刷帧落下去。
            // 少了这一步，一个不常动的开关要等它下一次变化才会显示（可能几分钟），
            // 操作员打开画面看到的就是设计值而不是现场真实状态。
            lock (_gate)
                _dirty[target] = handle.Value;

            BoundCount++;
        }

        /// <summary>
        /// 取这个变量已有的订阅，没有就挂一个。
        /// 这是"按变量订阅一次"的落点：同一个变量被十个图元绑着，这里也只进一次 add。
        /// </summary>
        private Subscription GetOrAddSubscription(IScadaValueHandle handle)
        {
            var key = KeyOf(handle);

            if (_subscriptions.TryGetValue(key, out var existing))
                return existing;

            var subscription = new Subscription(handle);
            subscription.Handler = (_, _) => OnValueChanged(subscription);
            handle.ValueChanged += subscription.Handler;

            _subscriptions[key] = subscription;
            return subscription;
        }

        /// <summary>
        /// 订阅键：<b>Id 优先、名字兜底</b>，与寻址口径一致。
        /// 句柄拿不出 Id（理论上只有假数据源会这样）时退回名字，保证"同名的仍然只订一次"。
        /// </summary>
        private static string KeyOf(IScadaValueHandle handle)
            => handle.VariableId != Guid.Empty
                ? "id:" + handle.VariableId.ToString("D")
                : "name:" + handle.Name;

        private static string Describe(ScadaBinding binding)
            => string.IsNullOrWhiteSpace(binding.VariableName) ? "(未指定)" : binding.VariableName!;

        #endregion

        #region 脏值暂存与合并刷帧

        /// <summary>
        /// 值变化回调。<b>可能在后台线程</b>，所以这里只做两件线程安全的事：
        /// 往暂存表里记"该写什么"、立一个"排一次帧"的标志。绝不在这里碰控件。
        /// </summary>
        private void OnValueChanged(Subscription subscription)
        {
            lock (_gate)
            {
                foreach (var target in subscription.Targets)
                    _dirty[target] = subscription.Handle.Value; // 同一目标一帧内变多次只留最后一次
            }

            QueueFlushIfDirty();
        }

        private void QueueFlushIfDirty()
        {
            lock (_gate)
            {
                if (_dirty.Count == 0)
                    return;
            }

            // 同一帧只排一次：十个变量同帧变、或一个变量变十次，都只往 Dispatcher 里放一个回调
            if (Interlocked.CompareExchange(ref _flushQueued, 1, 0) != 0)
                return;

            _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Flush));
        }

        /// <summary>
        /// 刷一帧（UI 线程）：把暂存表整表取走并清空，然后逐个目标写控件。
        /// 先清空再写，是为了让写入过程中新到的值走"下一帧"，而不是把这一帧无限拉长。
        /// </summary>
        private void Flush()
        {
            Interlocked.Exchange(ref _flushQueued, 0);

            KeyValuePair<Target, object?>[] batch;

            lock (_gate)
            {
                if (_dirty.Count == 0)
                    return;

                batch = _dirty.ToArray();
                _dirty.Clear();
            }

            foreach (var pair in batch)
                Apply(pair.Key, pair.Value);
        }

        private void Apply(Target target, object? value)
        {
            if (target.Control.TryApplyRuntimeValue(target.Property, value, target.Format, out var error))
            {
                Clear(target.Control, target); // 原来是坏值、现在转得动了，角标要撤掉
                return;
            }

            Report(target.Control, target, ScadaDiagnosticLevel.Error,
                $"画面「{_page.Name}」图元「{target.Control.Element?.Name}」的属性「{target.Property.DisplayName}」：变量「{target.VariableName}」{error}");
        }

        #endregion

        #region 诊断角标

        /// <summary>
        /// 记一条诊断并按聚合结果刷新角标。
        ///
        /// 为什么要"聚合"而不是直接 Set：角标是按<b>控件</b>画的（一个控件只有一个右上角），
        /// 而一个控件可能同时有两条绑定出问题。若各自 Set，后一条会盖掉前一条的颜色与提示；
        /// 若各自 Clear，转好一条会把另一条还在的问题抹掉。所以按控件攒一张状态表，
        /// 取<b>最严重</b>的那条显示（红 &gt; 橙），全部转好才撤点。
        /// </summary>
        private void Report(ScadaElementBase control, object source, ScadaDiagnosticLevel level, string message)
        {
            if (!_diagnostics.TryGetValue(control, out var states))
            {
                states = new Dictionary<object, Diagnostic>();
                _diagnostics[control] = states;
            }

            states[source] = new Diagnostic(level, message);
            RefreshMarker(control);

            _report?.Invoke(level, message);
        }

        private void Clear(ScadaElementBase control, object source)
        {
            if (!_diagnostics.TryGetValue(control, out var states) || !states.Remove(source))
                return;

            if (states.Count == 0)
                _diagnostics.Remove(control);

            RefreshMarker(control);
        }

        private void RefreshMarker(ScadaElementBase control)
        {
            if (!_diagnostics.TryGetValue(control, out var states) || states.Count == 0)
            {
                _canvas.Diagnostics.Remove(control);
                return;
            }

            var worst = default(Diagnostic);

            foreach (var state in states.Values)
            {
                if (worst.Message is null || state.Level > worst.Level)
                    worst = state;
            }

            _canvas.Diagnostics.Set(control, worst.Level, worst.Message);
        }

        #endregion

        /// <summary>一个变量的一份订阅：句柄 + 转发用的 handler + 依赖它的全部目标</summary>
        private sealed class Subscription
        {
            public Subscription(IScadaValueHandle handle) => Handle = handle;

            public IScadaValueHandle Handle { get; }

            /// <summary>
            /// 退订要靠同一个委托实例，所以 handler 必须存下来——
            /// 用 lambda 现写一个 `-=` 是永远摘不掉的（每次都是新对象），那正是订阅泄漏最隐蔽的写法。
            /// </summary>
            public EventHandler Handler { get; set; } = null!;

            public List<Target> Targets { get; } = new();
        }

        /// <summary>一个刷值目标：写到哪个控件的哪条属性、用什么格式</summary>
        private sealed class Target
        {
            public ScadaElementBase Control { get; init; } = null!;

            public ElementPropertyDescriptor Property { get; init; } = null!;

            public string? Format { get; init; }

            /// <summary>变量展示名（诊断文案用）</summary>
            public string VariableName { get; init; } = string.Empty;
        }

        private readonly record struct Diagnostic(ScadaDiagnosticLevel Level, string Message);
    }
}
