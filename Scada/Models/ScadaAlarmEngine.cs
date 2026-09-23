using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 报警引擎：把"变量值"变成"报警态"的那台机器。
    ///
    /// 它解决的是什么问题
    /// ---------
    /// 画面绑定回答的是"这个值该显示成什么"，报警回答的是"这个值该不该喊人"。
    /// 后者是运行态里唯一<b>有时间维度</b>的东西：条件要连续成立够久才算（延时）、
    /// 要抖多少才放行（回差）、多久收不到值算断线（超时）、报警要等人确认（态机）。
    /// 这些规则必须住在一个能被无界面断言钉住的地方——否则每次调阈值都得开界面等现场复现。
    ///
    /// 为什么<b>不</b>塞进 <see cref="ScadaRuntime"/>
    /// ---------
    /// <see cref="ScadaRuntime"/> 的边界写得很清楚：它只读文档、只管"该显示哪一页"，
    /// 而且<b>刻意不加锁</b>（"只在 UI 线程上用"）。报警引擎恰恰相反——它的输入来自
    /// <see cref="IScadaValueHandle.ValueChanged"/>，而网络变量由后台轮询线程改值，
    /// 事件天然可能在非 UI 线程到达。两件事的线程模型相反，硬塞进一个类里，
    /// 要么给运行态平白加一把锁，要么让报警在后台线程上裸奔。分开是唯一诚实的选择。
    ///
    /// 线程约定
    /// ---------
    /// <b>本类自带上锁</b>（<c>_gate</c>），任何线程都可以调 <see cref="Tick"/> /
    /// <see cref="Acknowledge"/>；值变化回调也在锁内改态。
    /// 但<b>事件一律在锁外抛</b>——先收集待发记录、出锁再回调。
    /// 否则订阅方在回调里再调一次引擎（比如"报警来了顺手确认一下"）就是死锁。
    /// 订阅方若要在回调里碰 UI，切线程是订阅方的责任（与 <see cref="IScadaValueHandle"/> 同一约定）。
    ///
    /// 边界（本阶段刻意不做的事）
    /// ---------
    /// - <b>不落盘</b>：历史留在内存（<see cref="MaxHistoryRecords"/> 条上限），
    ///   按天滚动写文件由宿主做——文件 IO 不该拖慢值变化这条热路径。
    /// - <b>不做动作</b>：不弹窗、不写变量、不切画面。报警只发事实（哪条报警、到了哪个态），
    ///   要执行什么交给订阅方——与 <see cref="ScadaRuntime.ElementEventRaised"/> 同一分工。
    /// - <b>不读文档以外的东西</b>：定义来自 <see cref="ScadaDocument.Alarms"/>，
    ///   值的解析走 <see cref="IScadaValueSource"/>（Id 优先、名字兜底）。
    /// - <b>定义解析不到变量时静默跳过</b>：不产生"配置错误"报警（那属于组态检查，
    ///   是编辑器该在保存时提示的事，不是运行态该刷屏的事）。
    ///
    /// 自动跟随配置
    /// ---------
    /// 挂载后引擎会盯住 <see cref="ScadaDocument.Alarms"/> 及其中的每一条定义：
    /// 运行中增删报警、改阈值、换变量都会置脏，下一次 <see cref="Tick"/> 自动重挂。
    /// 调用方只需 <see cref="Attach"/> + 定时 <see cref="Tick"/>，不必在每条编辑通路上记得通知引擎。
    /// </summary>
    public sealed class ScadaAlarmEngine
    {
        /// <summary>
        /// 内存里保留的报警记录上限。超过就从最老的丢。
        ///
        /// 为什么必须有上限：报警记录是唯一"随运行时长增长"的东西，而组态软件要连开几个月。
        /// 2000 条足够覆盖"最近几天的所有报警"，更早的查询本来就该去查落盘的历史文件
        /// ——内存不是历史库。
        /// </summary>
        public const int MaxHistoryRecords = 2000;

        private readonly object _gate = new();
        private readonly ScadaDocument _document;
        private readonly IScadaValueSource? _source;
        private readonly Func<DateTime> _clock;
        private readonly List<Slot> _slots = new();
        private readonly List<ScadaAlarmRecord> _history = new();

        /// <summary>
        /// 配置变更脏标记。运行中改阈值、改条件种类、增删报警都会置上它，
        /// 下一次 <see cref="Tick"/> 自动重挂。
        ///
        /// 为什么要这个标记而不是"让宿主改完配置自己调 <see cref="Reload"/>"：
        /// 报警配置有<b>两条</b>编辑通路（属性面板逐字段改、报警列表增删），
        /// 未来还会加（导入配置、批量启用）。要求每条通路都记得通知引擎，
        /// 早晚会漏一条——而漏掉的表现是"改了阈值但不生效"，最难排查的那种 bug。
        /// 让引擎自己盯住文档，"谁改的"就不再是引擎需要知道的事。
        /// </summary>
        private bool _dirty;

        private bool _attached;

        /// <param name="document">报警定义所在的文档（内存里的当前方案）</param>
        /// <param name="source">值通道解析器；传 null 则本引擎只发"没订阅上"的空转（断言用）</param>
        public ScadaAlarmEngine(ScadaDocument document, IScadaValueSource? source)
            : this(document, source, null)
        {
        }

        /// <summary>
        /// 带上时钟的构造：所有"现在几点"都从 <paramref name="clock"/> 取。
        ///
        /// 为什么要留这个口子：本引擎最核心的三条规则——激活延时、通信断线、回差——
        /// <b>全部是时间函数</b>。若内部直接调 <c>DateTime.UtcNow</c>，断言就只能靠
        /// <c>Thread.Sleep</c> 去等真实时间：慢（每条要等几百毫秒）、而且一旦机器繁忙就会随机失败，
        /// 那种"偶尔红一次"的断言最后一定会被当成噪声忽略掉。注入时钟之后，
        /// 断言能把十分钟一口气推过去，延时/断线/恢复全变成确定性判定。
        /// 生产代码不传（走 <c>DateTime.UtcNow</c>），行为一字不变。
        /// </summary>
        /// <param name="document">报警定义所在的文档</param>
        /// <param name="source">值通道解析器；传 null 则所有槽空转</param>
        /// <param name="clock">时间源；null = <c>DateTime.UtcNow</c></param>
        public ScadaAlarmEngine(ScadaDocument document, IScadaValueSource? source, Func<DateTime>? clock)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _source = source;
            _clock = clock ?? (() => DateTime.UtcNow);
        }

        /// <summary>是否已挂上（挂上才会订阅变量、才吃 <see cref="Tick"/>）</summary>
        public bool IsAttached
        {
            get { lock (_gate) return _attached; }
        }

        /// <summary>本次挂载建了几条槽（= 文档里报警定义的条数）</summary>
        public int SlotCount
        {
            get { lock (_gate) return _slots.Count; }
        }

        /// <summary>其中真正订阅上变量的条数（<see cref="SlotCount"/> 减它 = 被停用或解析失败的）</summary>
        public int SubscribedCount
        {
            get { lock (_gate) return _slots.Count(s => s.Handle != null); }
        }

        /// <summary>
        /// 此刻挂在报警列表上的记录（<b>按严重度降序、同级按激活时间降序</b>）。
        ///
        /// 为什么排序放在引擎里而不是面板里：现场"最要紧的排最上面"是一条产品规则，
        /// 不是某个面板的显示偏好——状态栏计数、报警条、导出都该是同一个次序。
        ///
        /// 为什么从 <c>_slots</c> 取而不是从 <c>_history</c> 筛：历史是追加的流水，
        /// 配置重载后旧流水里的"激活"记录会变成永远清不掉的幽灵；槽才是"此刻真相"。
        /// </summary>
        public IReadOnlyList<ScadaAlarmRecord> ActiveAlarms
        {
            get
            {
                lock (_gate)
                {
                    return _slots.Where(s => s.Record != null)
                                 .Select(s => s.Record!)
                                 .OrderByDescending(r => r.Severity)
                                 .ThenByDescending(r => r.ActivatedAtUtc)
                                 .ToList();
                }
            }
        }

        /// <summary>等操作员确认的条数（激活 + 已恢复未确认）。报警条上的那个红点数字</summary>
        public int UnacknowledgedCount
        {
            get
            {
                lock (_gate)
                    return _slots.Count(s => s.Record != null && s.Record.State.NeedsAcknowledge());
            }
        }

        /// <summary>当前最高严重度；一条报警都没有时为 null（状态栏据此决定要不要变红）</summary>
        public ScadaAlarmSeverity? HighestActiveSeverity
        {
            get
            {
                lock (_gate)
                {
                    var active = _slots.Where(s => s.Record != null).Select(s => s.Record!.Severity).ToList();
                    return active.Count == 0 ? null : active.Max();
                }
            }
        }

        /// <summary>内存里的报警流水（按发生顺序，旧 → 新）。要"最新在前"请自行反转或调 <see cref="RecentRecords"/></summary>
        public IReadOnlyList<ScadaAlarmRecord> History
        {
            get { lock (_gate) return _history.ToList(); }
        }

        /// <summary>取最近 <paramref name="max"/> 条记录，<b>最新在前</b>（历史面板的默认视图）</summary>
        public IReadOnlyList<ScadaAlarmRecord> RecentRecords(int max)
        {
            if (max <= 0)
                return Array.Empty<ScadaAlarmRecord>();

            lock (_gate)
            {
                int take = Math.Min(max, _history.Count);
                var result = new List<ScadaAlarmRecord>(take);

                for (int i = _history.Count - 1; i >= 0 && result.Count < take; i--)
                    result.Add(_history[i]);

                return result;
            }
        }

        /// <summary>一条报警<b>刚被激活</b>（从正常态进入激活态）。落盘与弹窗挂这一条</summary>
        public event Action<ScadaAlarmRecord>? AlarmRaised;

        /// <summary>一条报警的态<b>变了</b>（确认 / 恢复 / 重新激活）。实时列表刷新挂这一条</summary>
        public event Action<ScadaAlarmRecord>? AlarmChanged;

        /// <summary>一条报警<b>彻底了结</b>（回到正常态、从实时列表消失）</summary>
        public event Action<ScadaAlarmRecord>? AlarmCleared;

        /// <summary>
        /// 挂载：按当前文档里的定义建槽、订阅变量，并对每条定义<b>立即初判一次</b>。
        ///
        /// 为什么必须初判：启动时变量早就越限了（比如设备停在那里、温度本来就高），
        /// 若不初判，这条报警要等到值<b>下一次变化</b>才报——现场表现就是"软件开着好好的，
        /// 什么都没报，一动才发现早就超了"。报警系统漏报一次，用户就再也不信它。
        /// </summary>
        public void Attach() => Attach(_clock());

        /// <inheritdoc cref="Attach()"/>
        /// <param name="nowUtc">当前时刻（UTC）。断言里传假时间才能测延时与断线</param>
        public void Attach(DateTime nowUtc)
        {
            var pending = new List<Action>();

            lock (_gate)
            {
                if (_attached) return;

                _attached = true;
                _dirty = false;
                _slots.Clear();

                // 盯住配置本身：运行中增删报警或改字段 → 下一次 Tick 自动重挂（理由见 _dirty）
                _document.Alarms.CollectionChanged += OnAlarmsChanged;

                foreach (var definition in _document.Alarms)
                {
                    HookDefinition(definition);

                    var slot = new Slot(definition, nowUtc);

                    if (definition.IsEnabled
                        && _source != null
                        && _source.TryResolve(definition.VariableId, definition.VariableName, out var handle)
                        && handle != null)
                    {
                        slot.Handle = handle;
                        slot.LastValue = handle.Value;

                        EventHandler handler = (sender, e) => OnValueChanged(slot);
                        slot.Handler = handler;
                        handle.ValueChanged += handler;
                    }

                    _slots.Add(slot);
                }

                foreach (var slot in _slots)
                    Evaluate(slot, nowUtc, pending);
            }

            Flush(pending);
        }

        /// <summary>
        /// 卸载：退订所有变量与配置、丢掉槽。<b>历史保留</b>（宿主可能还要把它落盘）。
        ///
        /// 可重复调用——关窗口、切方案、重新挂载三条路都会走到这里，不该互相甩异常。
        /// </summary>
        public void Detach()
        {
            lock (_gate)
            {
                _attached = false;
                _dirty = false;

                _document.Alarms.CollectionChanged -= OnAlarmsChanged;

                foreach (var slot in _slots)
                {
                    // 按"挂过谁就摘谁"退订，而不是重新遍历文档——文档可能已经被换过
                    // （反序列化走的是 Alarms 的 setter，会整体替换集合实例）。
                    UnhookDefinition(slot.Definition);

                    if (slot.Handle != null && slot.Handler != null)
                        slot.Handle.ValueChanged -= slot.Handler;

                    slot.Handler = null;
                }

                _slots.Clear();
            }
        }

        /// <summary>
        /// 按当前定义重新挂载（用户运行中改了报警配置，或增删了报警）。
        /// 已建立的报警态<b>不</b>保留：配置都变了，"上次那条还算不算"没有正确答案，
        /// 从当前值重新判一遍才是可解释的行为。
        ///
        /// 通常<b>不需要</b>手工调用：配置一改引擎就会置脏标记，下一次 <see cref="Tick"/> 自动重挂。
        /// 留成公开方法是为了给"改完想立刻看到结果"的调用方（比如组态界面的"应用"按钮）。
        /// </summary>
        public void Reload() => Reload(_clock());

        /// <inheritdoc cref="Reload()"/>
        /// <param name="nowUtc">当前时刻（UTC）</param>
        public void Reload(DateTime nowUtc)
        {
            Detach();
            Attach(nowUtc);
        }

        /// <summary>
        /// 节拍：推进时间。负责两件只有"时间流逝"才能触发的事——
        /// ① <b>激活延时</b>到期（条件一直成立但还没到延时秒数）；
        /// ② <b>通信断线</b>（多久没收到新值）。
        ///
        /// 由宿主用定时器驱动（WPF 侧 <c>DispatcherTimer</c>）。领域层不自己起定时器：
        /// 那会把"节拍多快""跑在哪个线程"这两条宿主决策写进模型，
        /// 而断言里我们要能一口气把假时间推十分钟。
        ///
        /// 频率建议 100~500ms：延时与断线的分辨率就是节拍周期，再快是白烧 CPU。
        /// </summary>
        public void Tick() => Tick(_clock());

        /// <inheritdoc cref="Tick()"/>
        /// <param name="nowUtc">当前时刻（UTC）</param>
        public void Tick(DateTime nowUtc)
        {
            bool dirty;

            lock (_gate)
            {
                if (!_attached) return;

                dirty = _dirty;
                _dirty = false;
            }

            // 重挂在锁外做：它内部会重新走一遍 Attach，而 Attach 的事件要在锁外抛。
            // 重挂本身已经做了初判，这一拍不必再判一次。
            if (dirty)
            {
                Reload(nowUtc);
                return;
            }

            var pending = new List<Action>();

            lock (_gate)
            {
                if (!_attached) return;

                foreach (var slot in _slots)
                    Evaluate(slot, nowUtc, pending);
            }

            Flush(pending);
        }

        // ── 内部：配置变更监视 ────────────────────────────────────────────────────

        /// <summary>
        /// 报警定义集合增删 → 更新逐条订阅（摘掉被删的、挂上新增的）并置脏。
        ///
        /// 摘订阅这一步不能省：被删掉的 <see cref="ScadaAlarmDefinition"/> 若还挂着本引擎的回调，
        /// 它就被引擎引用着无法回收——删一百条报警就漏一百个对象，而"删配置"在组态时是高频动作。
        /// </summary>
        private void OnAlarmsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems)
                {
                    if (item is ScadaAlarmDefinition definition)
                        UnhookDefinition(definition);
                }
            }

            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems)
                {
                    if (item is ScadaAlarmDefinition definition)
                        HookDefinition(definition);
                }
            }

            MarkDirty();
        }

        /// <summary>某条报警定义的字段变了（改阈值、换变量、改严重度……）→ 置脏，下一拍重挂</summary>
        private void OnDefinitionChanged(object? sender, PropertyChangedEventArgs e) => MarkDirty();

        private void HookDefinition(ScadaAlarmDefinition definition)
            => definition.PropertyChanged += OnDefinitionChanged;

        private void UnhookDefinition(ScadaAlarmDefinition definition)
            => definition.PropertyChanged -= OnDefinitionChanged;

        /// <summary>置脏。只有挂着的时候才置——没挂时挂载流程本来就会读最新配置</summary>
        private void MarkDirty()
        {
            lock (_gate)
            {
                if (_attached)
                    _dirty = true;
            }
        }

        /// <summary>
        /// 确认<b>某一条</b>报警（实时列表上那一行后面的"确认"按钮）。
        /// 返回真正发生了迁移的条数（0 = 这条报警不在、或者早就确认过了）。
        ///
        /// 语义分两种，都在 <see cref="AcknowledgeCore"/> 里：
        /// 还在报警 → 转"已确认"（报警还在，但操作员知道了）；
        /// 已经恢复 → 直接了结、从列表消失。
        /// </summary>
        public int Acknowledge(Guid alarmId)
        {
            if (alarmId == Guid.Empty)
                return 0;

            var pending = new List<Action>();
            int changed = 0;

            lock (_gate)
            {
                if (!_attached) return 0;

                var now = _clock();

                foreach (var slot in _slots)
                {
                    if (slot.Record == null || slot.Record.AlarmId != alarmId)
                        continue;

                    if (AcknowledgeCore(slot, slot.Record, now, pending))
                        changed++;
                }
            }

            Flush(pending);
            return changed;
        }

        /// <summary>确认全部待确认报警（报警条上的"全部确认"）。返回确认条数</summary>
        public int AcknowledgeAll()
        {
            var pending = new List<Action>();
            int changed = 0;

            lock (_gate)
            {
                if (!_attached) return 0;

                var now = _clock();

                foreach (var slot in _slots)
                {
                    if (slot.Record == null)
                        continue;

                    if (AcknowledgeCore(slot, slot.Record, now, pending))
                        changed++;
                }
            }

            Flush(pending);
            return changed;
        }

        /// <summary>清空内存历史（宿主落盘完成后调用，或断言段收尾）。不影响当前报警态</summary>
        public void ClearHistory()
        {
            lock (_gate)
                _history.Clear();
        }

        // ── 内部：态迁移 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 一条槽的完整判定：从正常态出发判"要不要报"，从非正常态出发判"要不要恢复"。
        /// <b>必须在锁内调用</b>，<paramref name="pending"/> 收集要出锁后才抛的事件。
        /// </summary>
        private void Evaluate(Slot slot, DateTime nowUtc, List<Action> pending)
        {
            // 没订阅上（被停用 / 变量解析不到）的槽完全空转：否则一条指向已删变量的
            // "断线报警"会按时长出来，而它连值都没有过。
            if (slot.Handle == null)
                return;

            var definition = slot.Definition;

            if (slot.Record == null)
            {
                if (!IsTriggered(slot, nowUtc))
                {
                    slot.ConditionSinceUtc = null; // 条件断了，延时重新计时
                    return;
                }

                // 延时确认：条件要连续成立够久。断线不吃延时——它本身就是"等够了"的结论，
                // 再叠一层等于把用户配的超时时间翻倍。
                if (definition.DelaySeconds > 0 && definition.Kind != ScadaAlarmKind.Stale)
                {
                    slot.ConditionSinceUtc ??= nowUtc;

                    if ((nowUtc - slot.ConditionSinceUtc.Value).TotalSeconds < definition.DelaySeconds)
                        return;
                }

                Raise(slot, nowUtc, pending);
                return;
            }

            var record = slot.Record;

            // "已恢复未确认"时条件又成立 → 退回「激活」：这条报警还没被操作员确认过，
            // 现在它又犯了，对他来说就是"还没处理完，又来了"，不该停在"已恢复"上装作没事。
            if (record.State == ScadaAlarmState.Recovered && IsTriggered(slot, nowUtc))
            {
                record.State = ScadaAlarmState.Active;
                record.RecoveredAtUtc = null; // 这一轮还没恢复，时长继续累计
                pending.Add(() => AlarmChanged?.Invoke(record));
                return;
            }

            if (IsStillTriggered(slot, nowUtc))
                return;

            Recover(slot, nowUtc, pending);
        }

        /// <summary>条件是否成立（<b>无回差</b>，用于"要不要报"）</summary>
        private static bool IsTriggered(Slot slot, DateTime nowUtc)
        {
            var definition = slot.Definition;

            if (definition.Kind == ScadaAlarmKind.Stale)
                return (nowUtc - slot.LastValueAtUtc).TotalSeconds >= definition.StaleSeconds;

            if (definition.Kind == ScadaAlarmKind.BoolOn)
                return ScadaValueConvert.ToBoolean(slot.LastValue) == true;

            if (definition.Kind == ScadaAlarmKind.BoolOff)
                return ScadaValueConvert.ToBoolean(slot.LastValue) == false;

            // 值拿不到或不是数（字符串变量）→ 判不了，就当没触发。
            // 宁可漏一条"本来也说不清"的报警，也不要因为类型不对刷一堆假报警。
            if (!ScadaValueConvert.TryToDouble(slot.LastValue, out var value))
                return false;

            return definition.Kind == ScadaAlarmKind.High || definition.Kind == ScadaAlarmKind.HighHigh
                ? value > definition.Threshold
                : value < definition.Threshold;
        }

        /// <summary>
        /// 条件是否<b>仍然</b>成立（四限类<b>带回差</b>，用于"要不要恢复"）。
        /// 与 <see cref="IsTriggered"/> 分开，就是回差的实现方式：
        /// 进来时按裸阈值判，出去时要多让开一个回差——两个阈值之间那段宽度，就是抖动被吸收的地方。
        /// </summary>
        private static bool IsStillTriggered(Slot slot, DateTime nowUtc)
        {
            var definition = slot.Definition;

            if (!definition.Kind.IsLimit())
                return IsTriggered(slot, nowUtc);

            if (!ScadaValueConvert.TryToDouble(slot.LastValue, out var value))
                return false;

            double band = definition.Deadband;

            return definition.Kind == ScadaAlarmKind.High || definition.Kind == ScadaAlarmKind.HighHigh
                ? value > definition.Threshold - band
                : value < definition.Threshold + band;
        }

        /// <summary>进入激活态：建记录、进历史、发两条事件</summary>
        private void Raise(Slot slot, DateTime nowUtc, List<Action> pending)
        {
            var record = new ScadaAlarmRecord(slot.Definition, nowUtc, ScadaValueConvert.ValueText(slot.LastValue));

            slot.Record = record;
            slot.ConditionSinceUtc = null;
            AddHistory(record);

            pending.Add(() => AlarmRaised?.Invoke(record));
            pending.Add(() => AlarmChanged?.Invoke(record));
        }

        /// <summary>
        /// 离开激活态。分岔点是"操作员确认过没有"——
        /// 没确认过就恢复的，停在 <see cref="ScadaAlarmState.Recovered"/> 等一次确认；
        /// 已确认的，直接了结。
        /// </summary>
        private void Recover(Slot slot, DateTime nowUtc, List<Action> pending)
        {
            var record = slot.Record;
            if (record == null || record.State == ScadaAlarmState.Recovered)
                return; // 已经恢复过，别把恢复时刻一路往后推

            record.RecoveredAtUtc = nowUtc;

            if (record.State == ScadaAlarmState.Active)
            {
                record.State = ScadaAlarmState.Recovered;
            }
            else
            {
                record.State = ScadaAlarmState.Normal;
                record.ClearedAtUtc = nowUtc;
                slot.Record = null;
            }

            pending.Add(() => AlarmChanged?.Invoke(record));

            if (record.State == ScadaAlarmState.Normal)
                pending.Add(() => AlarmCleared?.Invoke(record));
        }

        /// <summary>
        /// 确认一条记录的态迁移本体（<b>必须在锁内调用</b>）。
        /// 返回是否真的动了——"早就确认过了"要返回 false，否则"全部确认"会报出一个虚高的条数。
        /// </summary>
        private bool AcknowledgeCore(Slot slot, ScadaAlarmRecord record, DateTime nowUtc, List<Action> pending)
        {
            switch (record.State)
            {
                case ScadaAlarmState.Active:
                    record.State = ScadaAlarmState.Acknowledged;
                    record.AcknowledgedAtUtc = nowUtc;
                    break;

                case ScadaAlarmState.Recovered:
                    // 已恢复 + 确认 = 这件事彻底了结
                    record.State = ScadaAlarmState.Normal;
                    record.AcknowledgedAtUtc = nowUtc;
                    record.ClearedAtUtc = nowUtc;
                    slot.Record = null;
                    break;

                default:
                    return false; // Acknowledged：已经按过了
            }

            pending.Add(() => AlarmChanged?.Invoke(record));

            if (record.State == ScadaAlarmState.Normal)
                pending.Add(() => AlarmCleared?.Invoke(record));

            return true;
        }

        /// <summary>值变化回调（<b>可能在后台轮询线程</b>）</summary>
        private void OnValueChanged(Slot slot)
        {
            var now = _clock();
            var pending = new List<Action>();

            lock (_gate)
            {
                if (!_attached || slot.Handle == null)
                    return;

                slot.LastValueAtUtc = now;
                slot.LastValue = slot.Handle.Value;

                Evaluate(slot, now, pending);
            }

            Flush(pending);
        }

        /// <summary>把收集到的事件在<b>锁外</b>抛出去（理由见类注释的线程约定）</summary>
        private static void Flush(List<Action> pending)
        {
            foreach (var action in pending)
                action();
        }

        private void AddHistory(ScadaAlarmRecord record)
        {
            _history.Add(record);

            if (_history.Count > MaxHistoryRecords)
                _history.RemoveRange(0, _history.Count - MaxHistoryRecords);
        }

        /// <summary>
        /// 一条定义在运行期的槽：定义 + 值通道 + 当前态。
        ///
        /// 为什么不建"定义 → 槽"的字典索引：报警条数在几十条量级，
        /// 而字典要在每次增删定义时同步维护——多一处会不同步的结构，
        /// 换来的只是把 O(n) 扫描变成 O(1)，在这个规模上不划算（与成组不做索引同一判断）。
        /// </summary>
        private sealed class Slot
        {
            public Slot(ScadaAlarmDefinition definition, DateTime nowUtc)
            {
                Definition = definition;
                LastValueAtUtc = nowUtc;
            }

            public ScadaAlarmDefinition Definition { get; }

            /// <summary>值通道；null = 被停用或解析不到变量（该槽完全空转）</summary>
            public IScadaValueHandle? Handle { get; set; }

            /// <summary>挂在通道上的那个委托实例——退订必须用同一个实例，所以要留着</summary>
            public EventHandler? Handler { get; set; }

            /// <summary>本次报警的记录；null = 处于正常态</summary>
            public ScadaAlarmRecord? Record { get; set; }

            /// <summary>条件开始连续成立的时刻（延时计时起点）；条件一断就清空</summary>
            public DateTime? ConditionSinceUtc { get; set; }

            /// <summary>最后一次收到值的时刻（断线判定的基准）</summary>
            public DateTime LastValueAtUtc { get; set; }

            /// <summary>最后一次收到的值快照</summary>
            public object? LastValue { get; set; }
        }
    }
}
