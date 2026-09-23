using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Newtonsoft.Json;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 一条<b>变量事件</b>配置（落盘）："哪个变量、满足什么条件、就干哪些事"。
    ///
    /// 它解决的是什么问题
    /// ---------
    /// <see cref="ScadaElement.EventHooks"/> 只能表达"用户操作了这个图元"（按下/释放）；
    /// <see cref="ScadaPage.EventHooks"/> 只能表达"这一页进来了/走了"。
    /// 而工业现场大量动作是<b>由值驱动的</b>："液位超 80 就报警"、"开关量为真就启泵"——
    /// 这些没有"谁点了谁"，只有一个变量和一个条件。手册 7.5.2 把它们单列成一类，
    /// 可组态对象直接写的就是"变量"，本类就是那条口径的落点。
    ///
    /// 为什么<b>一条记录 = 一个变量</b>，而不是"一条记录 = 一个事件"
    /// ---------
    /// 同一个变量上常常要配好几条（"更改数值就记一笔 + 超上限就停机"）。
    /// 若一个事件一条记录，用户改一次变量引用就得改好几处，改名级联也会散成好几份。
    /// 一个变量一条记录、记录里再挂若干 <see cref="EventHooks"/>，
    /// 与 <see cref="ScadaElement"/> "一个图元挂若干钩子"的形状完全一致——
    /// 编辑逻辑（<see cref="IScadaEventHost"/>）因此可以<b>一份实现服务三种宿主</b>。
    ///
    /// 为什么阈值（<see cref="UpperLimit"/> / <see cref="LowerLimit"/>）在这里而不在变量定义上
    /// ---------
    /// 手册原话是"超出<b>变量的上限</b>时发生"，读起来像阈值属于变量。但 VM.Core 的变量定义
    /// 是给流程与算法用的，它不认识"组态"这回事；把组态阈值写进去，等于让"改一个画面上的
    /// 报警门限"去动流程的变量表，还会随方案文件一起被复制到别的工程。
    /// 所以语义按手册、存储落 SCADA 侧：阈值跟着画面走，不污染变量定义。
    ///
    /// 寻址口径与 <see cref="ScadaBinding"/> / <see cref="ScadaAlarmDefinition"/> 逐字一致
    /// （Id 优先、名字兜底），于是变量改名级联（<see cref="ScadaDocument.RefreshVariableReferences"/>）
    /// 也一并修到本类上，不必为它发明第三套寻址规则。
    /// </summary>
    public class ScadaVariableEvent : ScadaModelBase, IScadaEventHost
    {
        private Guid _variableId;
        private string? _variableName;
        private double? _upperLimit;
        private double? _lowerLimit;
        private bool _isEnabled = true;
        private ObservableCollection<ScadaEventHook> _eventHooks = new();

        /// <summary>
        /// 已挂上属性变更订阅的事件钩子。理由与 <see cref="ScadaElement"/> 的
        /// <c>_subscribedHooks</c> 完全相同：<c>EventHooks.Clear()</c> 走 Reset 分支拿不到
        /// 被移除的是谁，照事件参数摘必漏（表现为删光钩子后旧钩子仍被本记录钉住）。
        /// </summary>
        private readonly HashSet<ScadaEventHook> _subscribedHooks = new();

        /// <summary>被监视变量的稳定身份（权威寻址键）。<c>Guid.Empty</c> = 旧数据，只能按名字找</summary>
        public Guid VariableId
        {
            get => _variableId;
            set => SetProperty(ref _variableId, value);
        }

        /// <summary>变量名（展示串 + 旧数据兜底键）。机器寻址一律用 <see cref="VariableId"/></summary>
        public string? VariableName
        {
            get => _variableName;
            set => SetProperty(ref _variableName, value);
        }

        /// <summary>
        /// 上限：非 BOOL 型变量的值越过它时发 <see cref="ScadaEventType.ValueOverUpperLimit"/>。
        /// <c>null</c> = <b>不判上限</b>（默认），不是"上限为 0"。
        /// </summary>
        public double? UpperLimit
        {
            get => _upperLimit;
            set => SetProperty(ref _upperLimit, value);
        }

        /// <summary>下限：非 BOOL 型变量的值越过它时发 <see cref="ScadaEventType.ValueUnderLowerLimit"/>。<c>null</c> = 不判下限</summary>
        public double? LowerLimit
        {
            get => _lowerLimit;
            set => SetProperty(ref _lowerLimit, value);
        }

        /// <summary>
        /// 是否启用。停用时运行态完全跳过它（连变量都不订阅），配置保留。
        /// 默认 <c>true</c>：新建一条就是要用的，让用户先去勾一个框才生效是多余的一步。
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        /// <summary>
        /// 已配的事件钩子（"更改数值 → 记一笔""上限 → 停机"……）。
        ///
        /// 订阅保活三件套（照 <see cref="ScadaElement.EventHooks"/> 的范式）：
        /// <c>ObjectCreationHandling.Replace</c> + 带 setter 的摘/挂 + <c>[JsonConstructor]</c> 兜底。
        /// 少任何一件，反序列化都会绕过 setter 把订阅链断掉，而且不报错。
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<ScadaEventHook> EventHooks
        {
            get => _eventHooks;
            set
            {
                var old = _eventHooks;
                if (old != null)
                {
                    old.CollectionChanged -= OnEventHooksChanged;

                    foreach (var hook in _subscribedHooks)
                        hook.PropertyChanged -= OnHookPropertyChanged;

                    _subscribedHooks.Clear();
                }

                _eventHooks = value ?? new ObservableCollection<ScadaEventHook>();

                _eventHooks.CollectionChanged += OnEventHooksChanged;

                foreach (var hook in _eventHooks)
                    SubscribeHook(hook);
            }
        }

        /// <summary>初始化记录（为初始空集合挂上订阅）</summary>
        [JsonConstructor]
        public ScadaVariableEvent()
        {
            _eventHooks.CollectionChanged += OnEventHooksChanged;
        }

        /// <summary>是否是"只能按名字找"的旧数据（只有这种才需要改名级联修名字）</summary>
        [JsonIgnore]
        public bool IsLegacyByName => VariableId == Guid.Empty && !string.IsNullOrWhiteSpace(VariableName);

        /// <summary>
        /// 条件的人话描述（面板列表与日志用），如 <c>"超上限 80"</c> / <c>"低于下限 5"</c>。
        /// 一条都没配阈值时返回空串——"什么都没判"不该显示成"上下限都是 0"。
        /// </summary>
        [JsonIgnore]
        public string ConditionText
        {
            get
            {
                var parts = new List<string>(2);

                if (_upperLimit is { } upper)
                    parts.Add($"超上限 {upper.ToString("0.###", CultureInfo.InvariantCulture)}");

                if (_lowerLimit is { } lower)
                    parts.Add($"低于下限 {lower.ToString("0.###", CultureInfo.InvariantCulture)}");

                return string.Join(" / ", parts);
            }
        }

        /// <summary>
        /// 绑定/自愈回填：写入变量稳定身份与最新名字。语义与 <see cref="ScadaBinding.Bind"/> 相同，
        /// 两种调用场景也相同（用户选了变量 / 加载旧方案后按名解析成功把 Id 补回来）。
        /// </summary>
        public void Bind(Guid variableId, string? variableName)
        {
            VariableId = variableId;
            VariableName = variableName;
        }

        /// <summary>
        /// 命中判定：判断"被改名的变量"是不是本记录监视的那个。口径与 <see cref="ScadaBinding.Matches"/> 逐条同构——
        /// 有 Id 只认 Id（名字可能已过期而 Id 永不变），没 Id 才按名字比且忽略大小写。
        /// </summary>
        public bool Matches(Guid variableId, string? oldName)
        {
            if (VariableId != Guid.Empty)
                return variableId != Guid.Empty && VariableId == variableId;

            return !string.IsNullOrEmpty(oldName)
                && string.Equals(VariableName, oldName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 变量改名后的引用刷新：命中的动作换上最新名字并按需回填稳定身份。
        /// 返回被改动的动作条数（口径与 <see cref="ScadaElement.RefreshVariableReferences"/> 一致）。
        /// </summary>
        public int RefreshVariableReferences(Guid variableId, string? oldName, string newName)
        {
            int changed = 0;

            foreach (var hook in _eventHooks)
            {
                if (hook != null)
                    changed += hook.RefreshVariableReferences(variableId, oldName, newName);
            }

            return changed;
        }

        #region IScadaEventHost

        /// <summary>打开一次可撤销的编辑（D3 统一写入口），用法与 <see cref="ScadaElement.BeginEdit"/> 一致</summary>
        public IScadaChangeScope BeginEdit(string label) => ScadaChangeScope.Begin(label);

        /// <summary>
        /// 找某个事件已配的钩子；没配过返回 null。
        /// 同事件有多条时返回<b>靠前者</b>（与 <see cref="ScadaElement.FindEventHook"/> 同一口径）。
        /// </summary>
        public ScadaEventHook? FindEventHook(ScadaEventType eventType)
        {
            foreach (var hook in _eventHooks)
            {
                if (hook != null && hook.Event == eventType)
                    return hook;
            }

            return null;
        }

        /// <summary>找某个事件的钩子，没有就地建一条（事件编辑器"勾上某个事件"就调这个）</summary>
        public ScadaEventHook GetOrAddEventHook(ScadaEventType eventType)
        {
            using (BeginEdit($"配置变量事件 [{eventType}]"))
            {
                return FindEventHook(eventType) ?? AddEventHook(eventType);
            }
        }

        /// <summary>新建一条空动作钩子并加入集合（要"复用已有的那条"请先用 <see cref="GetOrAddEventHook"/>）</summary>
        public ScadaEventHook AddEventHook(ScadaEventType eventType)
        {
            var hook = ScadaChangeScope.Detached(() => new ScadaEventHook { Event = eventType });

            using (BeginEdit($"新增变量事件钩子 [{eventType}]"))
            {
                _eventHooks.Add(hook);
            }

            return hook;
        }

        /// <summary>
        /// 摘掉某个事件的钩子（含它下面所有动作），返回是否真删掉了东西。
        /// 返回 bool 的理由与 <see cref="ScadaElement.RemoveEventHook"/> 相同：
        /// "取消勾选一个本来就没配的事件"不该把画面版本号刷高。
        /// </summary>
        public bool RemoveEventHook(ScadaEventType eventType)
        {
            for (int i = 0; i < _eventHooks.Count; i++)
            {
                if (_eventHooks[i].Event != eventType)
                    continue;

                using (BeginEdit($"删除变量事件钩子 [{eventType}]"))
                {
                    _eventHooks.RemoveAt(i);
                }

                return true;
            }

            return false;
        }

        #endregion

        /// <summary>
        /// 钩子集合变化 → 转译成 <see cref="EventHooks"/> 的属性变更（同 <see cref="ScadaElement.OnEventHooksChanged"/>，
        /// 不再重复论证"为什么不走 CollectionChanged"）。
        /// </summary>
        private void OnEventHooksChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            ScadaCollectionRecorder.Record(_eventHooks, e);

            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var hook in _subscribedHooks)
                    hook.PropertyChanged -= OnHookPropertyChanged;

                _subscribedHooks.Clear();

                foreach (var hook in _eventHooks)
                    SubscribeHook(hook);
            }
            else
            {
                if (e.NewItems != null)
                {
                    foreach (ScadaEventHook hook in e.NewItems)
                        SubscribeHook(hook);
                }

                if (e.OldItems != null)
                {
                    foreach (ScadaEventHook hook in e.OldItems)
                        UnsubscribeHook(hook);
                }
            }

            RaisePropertyChanged(nameof(EventHooks));
        }

        /// <summary>单个钩子的变更（换事件类型、增删动作）继续往上冒，最终落到画面版本号</summary>
        private void OnHookPropertyChanged(object? sender, PropertyChangedEventArgs e)
            => RaisePropertyChanged(nameof(EventHooks));

        /// <summary>挂钩子属性变更订阅（幂等：登记表已存在则不重复挂）</summary>
        private void SubscribeHook(ScadaEventHook hook)
        {
            // null 项只可能来自被手工改坏的 .vms：HashSet.Add(null) 会抛，
            // 不挡就成了"一条坏钩子让整个方案打不开"（理由同 ScadaEventHook.Subscribe）。
            if (hook is null)
                return;

            if (_subscribedHooks.Add(hook))
                hook.PropertyChanged += OnHookPropertyChanged;
        }

        /// <summary>摘钩子属性变更订阅（幂等：登记表没有则不动手）</summary>
        private void UnsubscribeHook(ScadaEventHook hook)
        {
            if (hook is null)
                return;

            if (_subscribedHooks.Remove(hook))
                hook.PropertyChanged -= OnHookPropertyChanged;
        }
    }
}
