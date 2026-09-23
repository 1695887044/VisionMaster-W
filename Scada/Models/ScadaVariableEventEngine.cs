using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 变量事件引擎：把"变量值"变成"一组动作"的那台机器。
    ///
    /// 它解决的是什么问题
    /// ---------
    /// 画面绑定回答"这个值该显示成什么"，报警回答"这个值该不该喊人"，
    /// 而本引擎回答第三问：<b>"这个值变了，该顺手干点什么"</b>——
    /// 记一笔日志、把另一个变量置 1、切到某个画面。手册 7.5.2 把这一类单列成
    /// "可组态对象 = 变量"的事件（更改数值 / 值为真 / 值为假 / 上限 / 下限），
    /// 本类就是那条口径的运行态实现。
    ///
    /// 为什么<b>不</b>并进 <see cref="ScadaAlarmEngine"/>
    /// ---------
    /// 两者都由值驱动，但引擎的"态"完全不同：报警有严重度、确认状态、历史记录、延时与回差，
    /// 它要回答的是"这条异常了结没有"；变量事件<b>没有态</b>——值一变就发一次，
    /// 发完即忘（这正是"边沿触发"的定义）。硬合并的结果是报警的态机里塞进一堆
    /// 用不到的分支，而变量事件得先绕过延时/回差/确认才能发出去。
    ///
    /// 为什么<b>不</b>塞进 <see cref="ScadaRuntime"/>
    /// ---------
    /// 与报警引擎同一条理由：<see cref="ScadaRuntime"/> 刻意不加锁（"只在 UI 线程上用"），
    /// 而本引擎的输入来自 <see cref="IScadaValueHandle.ValueChanged"/>，网络变量由后台轮询线程改值，
    /// 事件天然可能在非 UI 线程到达。两件事的线程模型相反，分开是唯一诚实的选择。
    ///
    /// 线程约定
    /// ---------
    /// <b>本类自带上锁</b>（<c>_gate</c>），值变化回调在锁内改态；
    /// 但<b>事件一律在锁外抛</b>——先收集待发条目、出锁再回调。
    /// 否则订阅方在回调里再调一次引擎（比如"收到事件顺手重挂一次"）就是死锁。
    /// 订阅方要在回调里碰 UI，<b>切线程是订阅方的责任</b>（见 <see cref="ScadaRuntimeHost"/> 的做法）。
    ///
    /// 为什么"挂载那一刻"不算一次触发
    /// ---------
    /// 手册这几条事件的定义全是<b>边沿</b>（"由假变真""越过上限"）。挂载时读到的第一个值
    /// 只是基线，不是"变化"——否则软件一启动，所有"值为真"的钩子都会跑一遍，
    /// 现场表现就是"开机时莫名其妙执行了一堆动作"。所以 <see cref="Attach"/> 只记基线、不发事件。
    ///
    /// 自动跟随配置
    /// ---------
    /// 挂载后引擎盯住 <see cref="ScadaDocument.VariableEvents"/> 及其中的每一条记录：
    /// 增删记录、改阈值、换变量、改启用都会置脏，下一次 <see cref="Tick"/> 自动重挂。
    /// 调用方只需 <see cref="Attach"/> + 定时 <see cref="Tick"/>，不必在每条编辑通路上记得通知引擎。
    /// </summary>
    public sealed class ScadaVariableEventEngine
    {
        private readonly object _gate = new();
        private readonly ScadaDocument _document;
        private readonly IScadaValueSource? _source;
        private readonly List<Slot> _slots = new();

        /// <summary>配置变更脏标记（理由与 <see cref="ScadaAlarmEngine"/> 的同名字段逐字相同）</summary>
        private bool _dirty;

        private bool _attached;

        /// <summary>累计命中并广播出去的钩子条数（自检用；只在锁内改）</summary>
        private int _raisedCount;

        /// <param name="document">变量事件表所在的文档（内存里的当前方案）</param>
        /// <param name="source">值通道解析器；传 null 则本引擎只建空转的槽（断言用）</param>
        public ScadaVariableEventEngine(ScadaDocument document, IScadaValueSource? source)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _source = source;
        }

        /// <summary>
        /// 一条钩子被命中。<b>可能在非 UI 线程上抛出</b>（值来自后台轮询线程时）。
        ///
        /// 带的是"哪条变量事件记录"与"哪条钩子"两样，而不是一个拼好的动作表：
        /// 判命中留在领域层、执行交给分发器，与 <see cref="ScadaRuntime.ElementEventRaised"/> 同一分工。
        /// 记录一并带出去，订阅方才能把"变量名"写进日志（分发器不认识模型，也不该认识）。
        /// </summary>
        public event Action<ScadaVariableEvent, ScadaEventHook>? VariableEventRaised;

        /// <summary>是否已挂载（重复 <see cref="Attach"/> 是空操作，重复 <see cref="Detach"/> 也是）</summary>
        public bool IsAttached
        {
            get { lock (_gate) return _attached; }
        }

        /// <summary>槽数（= 变量事件记录条数；自检用）</summary>
        public int SlotCount
        {
            get { lock (_gate) return _slots.Count; }
        }

        /// <summary>成功订上变量的槽数（被停用或解析不到变量的槽不计；自检"摘表无残留"靠它）</summary>
        public int SubscribedCount
        {
            get { lock (_gate) return _slots.Count(s => s.Handle != null); }
        }

        /// <summary>累计广播出去的钩子条数（自检用；<see cref="Detach"/> 不清零）</summary>
        public int RaisedCount
        {
            get { lock (_gate) return _raisedCount; }
        }

        /// <summary>
        /// 挂载：按当前变量事件表建槽、订阅变量，并记下每个变量的<b>基线值</b>（不触发任何钩子）。
        /// </summary>
        public void Attach()
        {
            lock (_gate)
            {
                if (_attached)
                    return;

                _attached = true;
                _dirty = false;
                _slots.Clear();

                // 盯住配置本身：运行中增删记录或改字段 → 下一次 Tick 自动重挂（理由见 _dirty）
                _document.VariableEvents.CollectionChanged += OnRecordsChanged;

                foreach (var record in _document.VariableEvents)
                {
                    HookRecord(record);

                    var slot = new Slot(record);

                    if (record.IsEnabled
                        && _source != null
                        && _source.TryResolve(record.VariableId, record.VariableName, out var handle)
                        && handle != null)
                    {
                        slot.Handle = handle;

                        // 基线：挂载不是"值变了"，所以这里只记值、不判边沿（见类注释）
                        slot.LastValue = handle.Value;

                        EventHandler handler = (sender, e) => OnValueChanged(slot);
                        slot.Handler = handler;
                        handle.ValueChanged += handler;
                    }

                    _slots.Add(slot);
                }
            }
        }

        /// <summary>
        /// 卸载：退订所有变量与配置、丢掉槽。可重复调用（关窗口、切方案、重新挂载三条路都会走到这里）。
        /// </summary>
        public void Detach()
        {
            lock (_gate)
            {
                _attached = false;
                _dirty = false;

                _document.VariableEvents.CollectionChanged -= OnRecordsChanged;

                foreach (var slot in _slots)
                {
                    // 按"挂过谁就摘谁"退订，而不是重新遍历文档——文档可能已经被换过
                    // （反序列化走的是 VariableEvents 的 setter，会整体替换集合实例）。
                    UnhookRecord(slot.Definition);

                    if (slot.Handle != null && slot.Handler != null)
                        slot.Handle.ValueChanged -= slot.Handler;

                    slot.Handler = null;
                }

                _slots.Clear();
            }
        }

        /// <summary>
        /// 按当前配置重新挂载（用户运行中改了变量事件配置，或增删了记录）。
        /// 通常<b>不需要</b>手工调用：配置一改引擎就置脏，下一次 <see cref="Tick"/> 自动重挂。
        /// </summary>
        public void Reload()
        {
            Detach();
            Attach();
        }

        /// <summary>
        /// 节拍：把"配置改过了"这件事转成一次重挂。
        ///
        /// 本引擎<b>没有时间维度</b>（边沿判定只看新旧两个值），所以节拍不做判定、
        /// 也不要求高频——它只是让"改完配置"这件事有一个确定的落地时机，
        /// 而不必在每一条编辑通路上都记得通知引擎（理由与 <see cref="ScadaAlarmEngine.Tick"/> 相同）。
        /// </summary>
        public void Tick()
        {
            bool dirty;

            lock (_gate)
            {
                if (!_attached)
                    return;

                dirty = _dirty;
                _dirty = false;
            }

            // 重挂在锁外做：它内部会重新走一遍 Attach，而本类的约定是"不在锁内做重活"
            if (dirty)
                Reload();
        }

        // ── 内部：配置变更监视 ────────────────────────────────────────────────────

        /// <summary>
        /// 变量事件记录增删 → 更新逐条订阅（摘掉被删的、挂上新增的）并置脏。
        ///
        /// 摘订阅这一步不能省：被删掉的 <see cref="ScadaVariableEvent"/> 若还挂着本引擎的回调，
        /// 它就被引擎引用着无法回收——而"删配置"在组态时是高频动作。
        /// </summary>
        private void OnRecordsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems)
                {
                    if (item is ScadaVariableEvent record)
                        UnhookRecord(record);
                }
            }

            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems)
                {
                    if (item is ScadaVariableEvent record)
                        HookRecord(record);
                }
            }

            MarkDirty();
        }

        /// <summary>某条记录的字段变了（改阈值、换变量、改启用……）→ 置脏，下一拍重挂</summary>
        private void OnRecordChanged(object? sender, PropertyChangedEventArgs e) => MarkDirty();

        private void HookRecord(ScadaVariableEvent record)
            => record.PropertyChanged += OnRecordChanged;

        private void UnhookRecord(ScadaVariableEvent record)
            => record.PropertyChanged -= OnRecordChanged;

        /// <summary>置脏。只有挂着的时候才置——没挂时挂载流程本来就会读最新配置</summary>
        private void MarkDirty()
        {
            lock (_gate)
            {
                if (_attached)
                    _dirty = true;
            }
        }

        // ── 内部：边沿判定 ────────────────────────────────────────────────────────

        /// <summary>值变化回调（<b>可能在后台轮询线程</b>）</summary>
        private void OnValueChanged(Slot slot)
        {
            var pending = new List<Action>();

            lock (_gate)
            {
                if (!_attached || slot.Handle == null)
                    return;

                var oldValue = slot.LastValue;
                var newValue = slot.Handle.Value;

                slot.LastValue = newValue;

                Evaluate(slot.Definition, oldValue, newValue, pending);
            }

            Flush(pending);
        }

        /// <summary>
        /// 一次值变化该发哪几条事件。<b>必须在锁内调用</b>，
        /// <paramref name="pending"/> 收集要出锁后才抛的条目。
        ///
        /// 判定顺序与 <see cref="ScadaEventType"/> 的取值顺序一致：
        /// 一次变化同时命中多条时（"值从 90 掉到 3"既算更改数值、又算低于下限），
        /// 动作的执行顺序因此是确定的——日志读起来才是稳定的。
        /// </summary>
        private void Evaluate(ScadaVariableEvent record, object? oldValue, object? newValue, List<Action> pending)
        {
            if (!ScadaValueConvert.SameValue(oldValue, newValue))
                Raise(record, ScadaEventType.ValueChanged, pending);

            var oldBool = ScadaValueConvert.ToBoolean(oldValue);
            var newBool = ScadaValueConvert.ToBoolean(newValue);

            // 严格边沿：只有"确知旧值是假"才算"由假变真"。
            // 旧值判不了（null / 非数非布）时不算边沿——宁可漏一条说不清的触发，
            // 也不要在变量刚接上、值还没来的时候先发一堆假动作。
            if (oldBool == false && newBool == true)
                Raise(record, ScadaEventType.ValueBecameTrue, pending);

            if (oldBool == true && newBool == false)
                Raise(record, ScadaEventType.ValueBecameFalse, pending);

            if (record.UpperLimit is { } upper
                && ScadaValueConvert.TryToDouble(oldValue, out var upperBefore)
                && ScadaValueConvert.TryToDouble(newValue, out var upperAfter)
                && upperBefore <= upper
                && upperAfter > upper)
            {
                Raise(record, ScadaEventType.ValueOverUpperLimit, pending);
            }

            if (record.LowerLimit is { } lower
                && ScadaValueConvert.TryToDouble(oldValue, out var lowerBefore)
                && ScadaValueConvert.TryToDouble(newValue, out var lowerAfter)
                && lowerBefore >= lower
                && lowerAfter < lower)
            {
                Raise(record, ScadaEventType.ValueUnderLowerLimit, pending);
            }
        }

        /// <summary>
        /// 把某一类事件在这条记录上的钩子全捞出来（命中口径见 <see cref="ScadaHookMatcher"/>）。
        /// <b>必须在锁内调用</b>——它要累加 <see cref="_raisedCount"/>。
        /// </summary>
        private void Raise(ScadaVariableEvent record, ScadaEventType eventType, List<Action> pending)
        {
            _raisedCount += ScadaHookMatcher.Raise(
                record.EventHooks,
                eventType,
                hook => pending.Add(() => VariableEventRaised?.Invoke(record, hook)));
        }

        /// <summary>把收集到的事件在<b>锁外</b>抛出去（理由见类注释的线程约定）</summary>
        private static void Flush(List<Action> pending)
        {
            foreach (var action in pending)
                action();
        }

        /// <summary>
        /// 一条记录在运行期的槽：记录 + 值通道 + 上一次见到的值。
        ///
        /// "上一次见到的值"就是边沿判定的全部状态——本引擎没有态机、没有历史，
        /// 因为手册这几条事件的语义本身就是"看一眼前后两个值"。
        /// </summary>
        private sealed class Slot
        {
            public Slot(ScadaVariableEvent definition) => Definition = definition;

            public ScadaVariableEvent Definition { get; }

            /// <summary>值通道；null = 被停用或解析不到变量（该槽完全空转）</summary>
            public IScadaValueHandle? Handle { get; set; }

            /// <summary>挂在通道上的那个委托实例——退订必须用同一个实例，所以要留着</summary>
            public EventHandler? Handler { get; set; }

            /// <summary>上一次见到的值（挂载时以当时的值为基线）</summary>
            public object? LastValue { get; set; }
        }
    }
}
