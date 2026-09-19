using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Newtonsoft.Json;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 画面图元（SCADA 组态里的一个可视对象）。
    ///
    /// 结构上分三块：
    /// ① <b>身份</b>：<see cref="ElementId"/>（稳定，画面内部按它寻址）+ <see cref="Name"/>（给人看的标签）；
    /// ② <b>几何</b>：位置/尺寸/旋转/Z 序——每个可视对象都有，且是编辑器拖拽矩形直接改的量，
    ///    所以写成强类型字段而不是塞进属性袋：塞进去会让"拖动一个矩形"退化成字符串解析；
    /// ③ <b>类型 + 属性袋</b>：<see cref="TypeKey"/> 标识"这是什么图元"，<see cref="Properties"/> 装该图元
    ///    特有的属性（文字、量程、刻度数……）。
    ///
    /// 为什么类型用字符串而不是派生类：
    /// - 派生类方案要求领域层每加一个图元就改一次代码，而图元库是要不断扩的；
    /// - 而且多态会往 .vms 里写 <c>$type</c>，正好踩在序列化白名单（SafeSerializationBinder）的校验路径上。
    /// 属性袋也不做类型化（一律 string）：领域层不该知道"#FF0000 怎么变成画刷"，
    /// 转换规则归 S2 的图元描述符，一处解释、一处使用。
    ///
    /// 刻意<b>不放</b>选中态（IsSelected）：模型只承载要落盘的文档数据，编辑器态属于 S3 的视图模型。
    /// FlowModel 的教训摆在那里——把运行期/编辑器态混进模型，就必须维护一份"这些属性变更不算变更"的
    /// 反射排除名单，名单一旦漏项，每次鼠标划过都会把版本号刷爆。
    /// </summary>
    public class ScadaElement : BindableBase
    {
        private Guid _elementId = Guid.NewGuid();
        private Guid _layerId;
        private string _name = string.Empty;
        private string _typeKey = string.Empty;
        private double _x;
        private double _y;
        private double _width = 120;
        private double _height = 40;
        private double _rotation;
        private int _zIndex;
        private bool _isLocked;
        private Dictionary<string, string> _properties = NewPropertyBag();
        private ObservableCollection<ScadaBinding> _bindings = new();
        private ObservableCollection<ScadaEventHook> _eventHooks = new();

        /// <summary>
        /// 已挂上属性变更订阅的绑定。
        ///
        /// 与 <c>ScadaPage._subscribedElements</c> 同一个理由：<c>Bindings.Clear()</c> 走
        /// <c>NotifyCollectionChangedAction.Reset</c> 且 <c>OldItems</c> 为 <c>null</c>，
        /// 只照事件参数摘订阅会漏掉它们——表现为"清空绑定后换绑变量，画面版本号还在跳"（脏标记失真），
        /// 且旧绑定被本图元钉住不放。有这张表就能在 Reset 时补摘，并让挂/摘幂等。
        /// </summary>
        private readonly HashSet<ScadaBinding> _subscribedBindings = new();

        /// <summary>
        /// 已挂上属性变更订阅的事件钩子。与 <see cref="_subscribedBindings"/> 同一个理由
        /// （<c>EventHooks.Clear()</c> 走 Reset 分支拿不到被移除的是谁，照参数摘必漏）。
        /// </summary>
        private readonly HashSet<ScadaEventHook> _subscribedHooks = new();

        /// <summary>图元稳定身份（画面内部按它寻址，复制图元时重新生成）</summary>
        public Guid ElementId
        {
            get => _elementId;
            set => SetProperty(ref _elementId, value);
        }

        /// <summary>图元名（图层列表、属性面板标题用；不参与寻址）</summary>
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value ?? string.Empty);
        }

        /// <summary>
        /// 所属图层的稳定身份。
        ///
        /// 只存 <b>引用</b>（一个 Guid），不在图层对象上挂图元子集合——归属关系一旦存两处，
        /// 就必然出现"图元在 A 层的集合里、LayerId 指向 B 层"的双主状态。
        ///
        /// <c>Guid.Empty</c> 是有含义的值，不是"没初始化"：它表示<b>未分层</b>——
        /// 手工取消分层、或画面一个图层都没有时才会留下这个值。
        /// 未分层与"指向已删除图层的悬空 Id"两种情况都由
        /// <see cref="ScadaPage.ResolveLayer(ScadaElement)"/> 返回 <c>null</c> 如实表达，
        /// 渲染/编辑侧按"可见、按自身 ZIndex、只看自身锁"处理：脏数据不该让画面打不开，
        /// 也不该被悄悄改写成"归属某个图层"（那会把用户的图元藏进他没锁定的层里，反而像丢了）。
        /// 加载期 <see cref="ScadaPage.EnsureIdentity"/> 只做两件补齐：图层集合为空时补一个默认图层；
        /// <b>旧文件里从来没写过这个字段</b>（值必然是 <c>Guid.Empty</c>）的平铺图元统一归进默认图层——
        /// 那不是猜归属，旧画面里的图元本来就铺在一起，归成一层是唯一不丢东西的解释。
        /// 而<b>指向已删除图层的悬空 Id 一律不动</b>：那种情形只有用户知道图元该去哪。
        /// </summary>
        public Guid LayerId
        {
            get => _layerId;
            set => SetProperty(ref _layerId, value);
        }

        /// <summary>
        /// 图元类型键（形如 "Hmi.Rectangle" / "Hmi.Button" / "Chart.Trend"，取值由 S2 的注册表定义）。
        ///
        /// 领域层只负责搬运它，不认识任何取值：S2 的图元注册表按它造控件与属性描述符，
        /// 将来加图元库只需往注册表里加一条，模型与序列化一行不改。
        /// </summary>
        public string TypeKey
        {
            get => _typeKey;
            set => SetProperty(ref _typeKey, value ?? string.Empty);
        }

        /// <summary>左上角 X（画面像素坐标，原点在画面左上角）</summary>
        public double X
        {
            get => _x;
            set => SetProperty(ref _x, value);
        }

        /// <summary>左上角 Y（画面像素坐标）</summary>
        public double Y
        {
            get => _y;
            set => SetProperty(ref _y, value);
        }

        /// <summary>宽（画面像素，恒正；左右翻转将来另立标志位，不靠负宽表达）</summary>
        public double Width
        {
            get => _width;
            set => SetProperty(ref _width, value);
        }

        /// <summary>高（画面像素，恒正）</summary>
        public double Height
        {
            get => _height;
            set => SetProperty(ref _height, value);
        }

        /// <summary>旋转角（度，顺时针，绕图元中心）。0 表示不旋转</summary>
        public double Rotation
        {
            get => _rotation;
            set => SetProperty(ref _rotation, value);
        }

        /// <summary>
        /// Z 序（越大越靠前）。
        /// 只存数值不做集合重排：图层顺序是"画的时候怎么叠"的语义，重排集合会让
        /// 撤销/重做与选中态都跟着错位，而渲染侧按它排一次序的成本可以忽略。
        /// </summary>
        public int ZIndex
        {
            get => _zIndex;
            set => SetProperty(ref _zIndex, value);
        }

        /// <summary>设计期锁定（锁定后编辑器不允许拖动/改尺寸，防止误挪已摆好的底图）</summary>
        public bool IsLocked
        {
            get => _isLocked;
            set => SetProperty(ref _isLocked, value);
        }

        /// <summary>
        /// 图元特有属性的键值袋（键名由 S2 描述符声明，值一律以字符串承载）。
        ///
        /// setter 挡 null 让消费方可以无脑索引而不必判空；<c>ObjectCreationHandling.Replace</c>
        /// 保证反序列化时整体替换而不是往默认实例里叠加（与 SolutionModel.Flows 同一处理）。
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public Dictionary<string, string> Properties
        {
            get => _properties;
            set => _properties = value ?? NewPropertyBag();
        }

        /// <summary>
        /// 绑定集合（图元属性 ← 工程变量）。
        ///
        /// 订阅保活三件套（照抄 FlowModel.Steps 的范式）：
        /// <c>ObjectCreationHandling.Replace</c> + 带 setter 的摘/挂 + <c>[JsonConstructor]</c> 兜底。
        /// 少了任何一件，反序列化都会绕过 setter，集合变更通知就断了（而且不报错）。
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<ScadaBinding> Bindings
        {
            get => _bindings;
            set
            {
                var old = _bindings;
                if (old != null)
                {
                    old.CollectionChanged -= OnBindingsChanged;

                    // 摘订阅走登记表（真正挂过订阅的人），避免历史路径留下的幽灵订阅被继承。
                    foreach (var binding in _subscribedBindings)
                        binding.PropertyChanged -= OnBindingPropertyChanged;

                    _subscribedBindings.Clear();
                }

                _bindings = value ?? new ObservableCollection<ScadaBinding>();

                _bindings.CollectionChanged += OnBindingsChanged;
                foreach (var binding in _bindings)
                    Subscribe(binding);
            }
        }

        /// <summary>
        /// 事件钩子（"按下这个按钮要干什么"）。
        ///
        /// 与 <see cref="Bindings"/> 是两码事：绑定是<b>数据流</b>（变量 → 图元外观，运行态持续刷），
        /// 钩子是<b>控制流</b>（用户操作 → 一串动作，命中一次执行一次）。两者都是"图元上挂的可扩展列表"，
        /// 但生命周期与执行方完全不同，合并成一个集合只会让执行侧每次都得先判类型。
        ///
        /// 为什么存"事件 → 动作表"而不是每个事件一个动作字段：见 <see cref="ScadaEventHook"/>。
        /// 本集合是<b>用户配了什么</b>，而"这个图元<b>能</b>配什么"由描述符的 Events 清单声明——
        /// 一个图元配了它类型没声明的事件，运行态永远不会触发它（控件不会去发那个事件），
        /// 属性面板也不会给出那一行。
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

        /// <summary>初始化图元（为初始空集合挂上订阅）</summary>
        [JsonConstructor]
        public ScadaElement()
        {
            _bindings.CollectionChanged += OnBindingsChanged;
            _eventHooks.CollectionChanged += OnEventHooksChanged;
        }

        /// <summary>
        /// 读取属性袋里的一个值；不存在时返回 <paramref name="fallback"/>。
        /// 消费方（S2 控件）据此实现"描述符给了默认值、模型没写过就用默认值"。
        /// </summary>
        public string GetProperty(string key, string fallback = "")
            => !string.IsNullOrEmpty(key) && _properties.TryGetValue(key, out var value)
                ? value
                : fallback;

        /// <summary>
        /// 写属性袋。传 null/空串即<b>删除该键</b>而不是存一个空串——
        /// 让"没配过的属性"与"显式配成空"在文件里长得一样，避免 .vms 里堆一批
        /// 与描述符默认值重复的空条目（改默认值时旧文件不会被这些垃圾条目钉住）。
        /// </summary>
        public void SetProperty(string key, string? value)
        {
            if (string.IsNullOrEmpty(key))
                return;

            if (string.IsNullOrEmpty(value))
                _properties.Remove(key);
            else
            {
                if (_properties.TryGetValue(key, out var old) && string.Equals(old, value, StringComparison.Ordinal))
                    return; // 同值不通知：避免把 Version 刷高、把 S3 的缓存白失效一次

                _properties[key] = value!;
            }

            RaisePropertyChanged(nameof(Properties));
        }

        /// <summary>
        /// 变量改名后的引用刷新：命中的绑定/动作换上最新名字，并按需回填稳定身份。
        ///
        /// 返回被改动的条数（0 表示本图元与这个变量无关）。返回值存在的意义是让
        /// 断言与日志能回答"这次改名到底动了哪里"——改名级联最怕的就是静默。
        /// 绑定与动作合并计数：调用方（注册表的改名级联）只关心"有没有漏"，不关心漏在哪一类。
        /// </summary>
        /// <param name="variableId">改名变量的稳定身份</param>
        /// <param name="oldName">改名前的旧名（旧数据按名命中用）</param>
        /// <param name="newName">改名后的新名</param>
        public int RefreshVariableReferences(Guid variableId, string? oldName, string newName)
        {
            int changed = 0;

            foreach (var binding in _bindings)
            {
                if (binding == null || !binding.Matches(variableId, oldName))
                    continue;

                // 自愈：这条是"按名"存下来的旧绑定，既然此刻按名命中了，就把稳定身份补齐。
                // 与 VariableRegistry.ResolveGlobalLink 是同一套思路——迁移只发生一次，
                // 落盘后该绑定改为按 Id 寻址，此后再改名同样不会断。
                binding.Bind(variableId, newName);
                changed++;
            }

            foreach (var hook in _eventHooks)
            {
                if (hook != null)
                    changed += hook.RefreshVariableReferences(variableId, oldName, newName);
            }

            return changed;
        }

        #region 绑定：查询与管理

        /// <summary>
        /// 找某个图元属性上已配的绑定；没配过返回 null。
        /// 同属性有多条时返回<b>靠前者</b>（与 <see cref="FindEventHook"/> 同一口径）。
        ///
        /// 属性键比较用 <c>Ordinal</c>：这些键（<c>$X</c> / <c>Value</c> …）是描述符声明的标识符，
        /// 不是给人看的文本，大小写不同就是两个不同的键——与 <c>ElementRegistry.FindProperty</c> 同一口径。
        /// </summary>
        public ScadaBinding? FindBinding(string? targetProperty)
        {
            if (string.IsNullOrEmpty(targetProperty))
                return null;

            foreach (var binding in _bindings)
            {
                if (binding != null && string.Equals(binding.TargetProperty, targetProperty, StringComparison.Ordinal))
                    return binding;
            }

            return null;
        }

        /// <summary>
        /// 找某个图元属性的绑定，没有就地建一条（属性面板点 ƒx 选完变量的落点）。
        ///
        /// 为什么是"复用"而不是"再添一条"：一个属性同时被两个变量驱动时，运行态只有后到者生效，
        /// 而用户在界面上完全看不出这件事。所以口径定成<b>一个属性至多一条绑定</b>，
        /// 换变量就是改这一条（<see cref="ScadaBinding.Bind"/>），已配的停用/格式串一并保住。
        /// </summary>
        public ScadaBinding GetOrAddBinding(string targetProperty)
            => FindBinding(targetProperty) ?? AddBinding(targetProperty);

        /// <summary>新建一条绑定并加入集合（要"复用已有的那条"请先用 <see cref="GetOrAddBinding"/>）</summary>
        public ScadaBinding AddBinding(string targetProperty)
        {
            var binding = new ScadaBinding { TargetProperty = targetProperty };
            _bindings.Add(binding);
            return binding;
        }

        /// <summary>
        /// 摘掉某个图元属性的绑定，返回是否真删掉了东西。
        ///
        /// 返回 bool 的理由与 <see cref="RemoveEventHook"/> 相同：属性面板上"清除绑定"在
        /// 本来就没绑的时候不该把画面版本号刷高（不然点一下空按钮就提示未保存）。
        /// </summary>
        public bool RemoveBinding(string? targetProperty)
        {
            if (string.IsNullOrEmpty(targetProperty))
                return false;

            for (int i = 0; i < _bindings.Count; i++)
            {
                if (!string.Equals(_bindings[i].TargetProperty, targetProperty, StringComparison.Ordinal))
                    continue;

                _bindings.RemoveAt(i);
                return true;
            }

            return false;
        }

        #endregion

        /// <summary>
        /// 找某个事件已配的钩子；没配过返回 null。
        /// 同事件有多条时返回<b>靠前者</b>（与画面按 Id 建索引时"重复身份保留靠前者"同一口径）。
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

        /// <summary>
        /// 找某个事件的钩子，没有就地建一条（属性面板"勾上某个事件"就调这个）。
        /// 返回的实例始终在集合里，调用方可以直接往 <see cref="ScadaEventHook.Actions"/> 里加动作。
        /// </summary>
        public ScadaEventHook GetOrAddEventHook(ScadaEventType eventType)
            => FindEventHook(eventType) ?? AddEventHook(eventType);

        /// <summary>新建一条空动作钩子并加入集合（要"复用已有的那条"请先用 <see cref="GetOrAddEventHook"/>）</summary>
        public ScadaEventHook AddEventHook(ScadaEventType eventType)
        {
            var hook = new ScadaEventHook { Event = eventType };
            _eventHooks.Add(hook);
            return hook;
        }

        /// <summary>
        /// 摘掉某个事件的钩子（含它下面所有动作），返回是否真删掉了东西。
        ///
        /// 为什么返回 bool：属性面板上"取消勾选一个事件"必须能区分"删掉了一条配置"与
        /// "本来就没配"——前者要标脏，后者不该把画面版本号刷高（不然切一遍事件的勾选框就提示未保存）。
        /// </summary>
        public bool RemoveEventHook(ScadaEventType eventType)
        {
            for (int i = 0; i < _eventHooks.Count; i++)
            {
                if (_eventHooks[i].Event != eventType)
                    continue;

                _eventHooks.RemoveAt(i);
                return true;
            }

            return false;
        }

        private static Dictionary<string, string> NewPropertyBag() => new();

        /// <summary>
        /// 绑定集合内容变化 → 以 <see cref="Bindings"/> 属性变更的形式再广播一次。
        ///
        /// 为什么这么做：上层（ScadaPage）只对每个图元的 PropertyChanged 挂了一个处理器来累计版本号，
        /// 没再往"每个图元的每个子集合"上挂一层。集合内容变化本是 CollectionChanged 事件，
        /// 这里把它转译成属性变更，页面侧的版本号就不会漏掉"增删一条绑定"这类改动——
        /// 否则要维护嵌套两级订阅，漏摘一次就是泄漏。
        /// </summary>
        private void OnBindingsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // Reset（Clear() / 整体替换）拿不到"被移除的是谁"，只能靠登记表全量补摘。
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var binding in _subscribedBindings)
                    binding.PropertyChanged -= OnBindingPropertyChanged;

                _subscribedBindings.Clear();

                foreach (var binding in _bindings)
                    Subscribe(binding);
            }
            else
            {
                if (e.NewItems != null)
                {
                    foreach (ScadaBinding binding in e.NewItems)
                        Subscribe(binding);
                }

                if (e.OldItems != null)
                {
                    foreach (ScadaBinding binding in e.OldItems)
                        Unsubscribe(binding);
                }
            }

            RaisePropertyChanged(nameof(Bindings));
        }

        /// <summary>挂绑定属性变更订阅（幂等：登记表已存在则不重复挂）</summary>
        private void Subscribe(ScadaBinding binding)
        {
            if (_subscribedBindings.Add(binding))
                binding.PropertyChanged += OnBindingPropertyChanged;
        }

        /// <summary>摘绑定属性变更订阅（幂等：登记表没有则不动手）</summary>
        private void Unsubscribe(ScadaBinding binding)
        {
            if (_subscribedBindings.Remove(binding))
                binding.PropertyChanged -= OnBindingPropertyChanged;
        }

        /// <summary>
        /// 单条绑定的属性变更（换绑变量、改格式串、启停用）同样要往上冒。
        ///
        /// 少了它，画面版本号就只能表达"图元/绑定条数变了"，表达不了"绑定内容变了"：
        /// 变量改名刷了一遍绑定、或用户在设计器里把绑定的变量换掉，脏标记（Version）都不会动，
        /// 于是"改了却没提示保存"、"运行态以为绑定表不用重建"。订阅由登记表 _subscribedBindings
        /// 统一增删（幂等），这样集合被 Clear() 走 Reset 分支时也能如实摘干净。
        /// </summary>
        private void OnBindingPropertyChanged(object? sender, PropertyChangedEventArgs e)
            => RaisePropertyChanged(nameof(Bindings));

        /// <summary>
        /// 钩子集合变化 → 转译成 <see cref="EventHooks"/> 的属性变更（同 <see cref="OnBindingsChanged"/>，
        /// 不再重复论证"为什么不走 CollectionChanged"）。
        /// </summary>
        private void OnEventHooksChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
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

        /// <summary>
        /// 单个钩子的变更（换事件类型、增删动作、改动作内容——钩子已把动作的变更汇总成
        /// 它自己 <c>Actions</c> 的一次属性变更）继续往上冒，最终落到画面版本号。
        /// </summary>
        private void OnHookPropertyChanged(object? sender, PropertyChangedEventArgs e)
            => RaisePropertyChanged(nameof(EventHooks));

        /// <summary>挂钩子属性变更订阅（幂等：登记表已存在则不重复挂）</summary>
        private void SubscribeHook(ScadaEventHook hook)
        {
            if (_subscribedHooks.Add(hook))
                hook.PropertyChanged += OnHookPropertyChanged;
        }

        /// <summary>摘钩子属性变更订阅（幂等：登记表没有则不动手）</summary>
        private void UnsubscribeHook(ScadaEventHook hook)
        {
            if (_subscribedHooks.Remove(hook))
                hook.PropertyChanged -= OnHookPropertyChanged;
        }
    }
}
