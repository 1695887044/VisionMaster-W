using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Newtonsoft.Json;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 一个<b>事件钩子</b>：某个事件 + 命中后要按顺序执行的一串动作。
    ///
    /// 类名刻意用 Hook 而不是 Trigger：WPF 里 <c>Trigger</c> 已经被"样式触发器"占用了，
    /// 同一份代码里两种 Trigger 是纯粹的阅读事故。组态侧的说法本来就是"事件钩子"
    /// （在设计界面上能看见、能配的那一排事件），叫 Hook 反而与用户口径一致。
    ///
    /// 为什么一个事件要挂<b>一串</b>动作而不是一个动作：这是组态的常态需求——
    /// "点启动按钮"要同时写变量、记日志、跳到运行画面。只存一个动作的话，
    /// 加第二个动作就得改 .vms 结构，而改结构的代价由所有历史工程承担。
    /// 一次做对，后面只是往集合里加项。
    ///
    /// 同一个事件配了<b>两条</b>钩子（手工改文件、或复制图元带过来）怎么办：按集合顺序依次执行，
    /// 不报错也不合并。这与"一串动作"的语义自洽——两条钩子就是一串长一点的动作。
    /// 属性面板走 <see cref="ScadaElement.GetOrAddEventHook"/>，一个事件只会长出一行，
    /// 所以正常操作路径下不会出现重复行。
    /// </summary>
    public class ScadaEventHook : ScadaModelBase
    {
        private ScadaEventType _event;
        private ObservableCollection<ScadaAction> _actions = new();

        /// <summary>
        /// 已挂上属性变更订阅的动作。
        /// 与 <c>ScadaElement._subscribedBindings</c> 同一个理由：<c>Actions.Clear()</c> 走 Reset
        /// 分支且 <c>OldItems</c> 为 null，只照事件参数摘订阅会漏掉它们——表现为用户删光动作后
        /// 旧动作仍被本钩子钉住（泄漏），且改旧动作还会把画面版本号刷高（脏标记失真）。
        /// </summary>
        private readonly HashSet<ScadaAction> _subscribedActions = new();

        /// <summary>
        /// 打开一次可撤销的编辑（D3 统一写入口），用法与 <see cref="ScadaPage.BeginEdit"/> 一致。
        /// 属性面板上"加一条动作/删一条动作/改动作内容"都走这个作用域。
        /// </summary>
        public IScadaChangeScope BeginEdit(string label) => ScadaChangeScope.Begin(label);

        /// <summary>本钩子对应的事件（取值必须是图元描述符里声明过的那个事件）</summary>
        public ScadaEventType Event
        {
            get => _event;
            set => SetProperty(ref _event, value);
        }

        /// <summary>
        /// 命中后要执行的动作（顺序执行，空集合 = 这个事件配了但没动作，执行侧当没配处理）。
        ///
        /// 订阅保活三件套（照 <see cref="ScadaElement.Bindings"/> 的范式）：
        /// <c>ObjectCreationHandling.Replace</c> + 带 setter 的摘/挂 + <c>[JsonConstructor]</c> 兜底。
        /// 少任何一件，反序列化都会绕过 setter 把订阅链断掉，而且不报错。
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<ScadaAction> Actions
        {
            get => _actions;
            set
            {
                var old = _actions;
                if (old != null)
                {
                    old.CollectionChanged -= OnActionsChanged;

                    foreach (var action in _subscribedActions)
                        action.PropertyChanged -= OnActionPropertyChanged;

                    _subscribedActions.Clear();
                }

                _actions = value ?? new ObservableCollection<ScadaAction>();

                _actions.CollectionChanged += OnActionsChanged;

                foreach (var action in _actions)
                    Subscribe(action);
            }
        }

        /// <summary>初始化钩子（为初始空集合挂上订阅）</summary>
        [JsonConstructor]
        public ScadaEventHook()
        {
            _actions.CollectionChanged += OnActionsChanged;
        }

        /// <summary>
        /// 变量改名后的引用刷新：命中的动作换上最新名字并按需回填稳定身份。
        /// 返回被改动的动作条数（口径与 <see cref="ScadaElement.RefreshVariableReferences"/> 一致，
        /// 绑定与动作的条数相加，便于断言回答"这次改名到底动了哪里"）。
        /// </summary>
        public int RefreshVariableReferences(Guid variableId, string? oldName, string newName)
        {
            int changed = 0;

            foreach (var action in _actions)
            {
                if (action == null || !action.Matches(variableId, oldName))
                    continue;

                action.BindVariable(variableId, newName);
                changed++;
            }

            return changed;
        }

        /// <summary>
        /// 动作集合内容变化 → 转译成一次属性变更再往上冒。
        /// 理由与 <c>ScadaElement.OnBindingsChanged</c> 完全相同：上层只对每个对象的 PropertyChanged
        /// 挂一个处理器来累计版本号，不再往"每个对象的每个子集合"挂一层，所以集合变更必须在这里
        /// 变成属性变更，否则"加了一条动作"根本不脏。
        /// </summary>
        private void OnActionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            ScadaCollectionRecorder.Record(_actions, e);

            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var action in _subscribedActions)
                    action.PropertyChanged -= OnActionPropertyChanged;

                _subscribedActions.Clear();

                foreach (var action in _actions)
                    Subscribe(action);
            }
            else
            {
                if (e.NewItems != null)
                {
                    foreach (ScadaAction action in e.NewItems)
                        Subscribe(action);
                }

                if (e.OldItems != null)
                {
                    foreach (ScadaAction action in e.OldItems)
                        Unsubscribe(action);
                }
            }

            RaisePropertyChanged(nameof(Actions));
        }

        /// <summary>单条动作的属性变更（改日志内容、换绑变量）同样要往上冒</summary>
        private void OnActionPropertyChanged(object? sender, PropertyChangedEventArgs e)
            => RaisePropertyChanged(nameof(Actions));

        /// <summary>挂动作属性变更订阅（幂等：登记表已存在则不重复挂）</summary>
        private void Subscribe(ScadaAction action)
        {
            // null 项只可能来自被手工改坏的 .vms：反序列化会原样塞进集合，而执行侧本来就会跳过它。
            // 这里必须挡一下——HashSet.Add(null) 是直接抛 NullReferenceException 的，
            // 不挡就成了"一条坏动作让整个画面打不开"，与"坏在一条上只丢那一条"的口径相反。
            if (action is null)
                return;

            if (_subscribedActions.Add(action))
                action.PropertyChanged += OnActionPropertyChanged;
        }

        /// <summary>摘动作属性变更订阅（幂等：登记表没有则不动手；null 的处理理由见 <see cref="Subscribe"/>）</summary>
        private void Unsubscribe(ScadaAction action)
        {
            if (action is null)
                return;

            if (_subscribedActions.Remove(action))
                action.PropertyChanged -= OnActionPropertyChanged;
        }
    }
}
