using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Newtonsoft.Json;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 一个 SCADA 画面：一块固定尺寸的设计画布 + 画布上的图元集合 + 网格/对齐这类设计期设置。
    ///
    /// 尺寸语义：<see cref="Width"/>/<see cref="Height"/> 是<b>设计像素</b>（画布的逻辑坐标范围），
    /// 运行态"适应窗口"的缩放是视图侧的事，模型里不存缩放比例——存了就会出现
    /// "同一份画面在不同机器上落盘出不同内容"的怪事。
    /// </summary>
    public class ScadaPage : ScadaModelBase, IScadaEventHost
    {
        private Guid _pageId = Guid.NewGuid();
        private string _name = "画面";
        private string _description = string.Empty;
        private double _width = 1920;
        private double _height = 1080;
        private string _background = "#FF1E1E1E";
        private bool _showGrid = true;
        private double _gridSize = 10;
        private bool _snapToGrid = true;
        private int _version;
        private ObservableCollection<ScadaElement> _elements = new();
        private ObservableCollection<ScadaLayer> _layers = new();
        private ObservableCollection<ScadaEventHook> _eventHooks = new();

        /// <summary>
        /// 身份 → 图元的查找索引。
        ///
        /// 为什么必须有：<see cref="FindElement"/> 的调用方都是"高频 + 按 Id 单点取"——
        /// S3 编辑器选中/命中测试、S4 运行态按绑定目标取图元。线性扫描在万级图元下
        /// 单次就是一万次 Guid 比较，几十帧就退化成秒级卡顿（压力断言实测 20 万次查询 10.9s）。
        /// 与 <c>VariableRegistry</c> 用字典替掉线性扫描是同一个决定。
        ///
        /// 重复身份的口径与 <c>VariableRegistry</c> 一致：<b>保留集合中靠前者</b>，
        /// 让解析结果不随插入顺序漂移（脏数据不该让画面打不开）。
        /// </summary>
        private readonly Dictionary<Guid, ScadaElement> _elementIndex = new();

        /// <summary>
        /// 已挂上属性变更订阅的图元。
        ///
        /// 为什么不能只靠 <c>NotifyCollectionChangedEventArgs</c> 摘订阅：<c>Clear()</c> 走 Reset
        /// 分支且 <c>OldItems</c> 为 <c>null</c>，拿不到"被移除的是谁"——照着参数摘就会漏掉它们，
        /// 表现为清空画面后旧图元仍被本页钉住不放（泄漏），且改旧图元还会把本页的版本号刷高。
        /// 有这张表就能在 Reset/Replace 时补摘，同时让挂/摘成为幂等操作。
        /// </summary>
        private readonly HashSet<ScadaElement> _subscribedElements = new();

        /// <summary>
        /// 已挂上属性变更订阅的图层。与 <see cref="_subscribedElements"/> 同一个理由：
        /// <c>Layers.Clear()</c> 走 Reset 分支拿不到被移除的是谁，只照事件参数摘就会漏摘。
        /// </summary>
        private readonly HashSet<ScadaLayer> _subscribedLayers = new();

        /// <summary>
        /// 已挂上属性变更订阅的画面钩子。与上面两张表同一个理由：
        /// <c>EventHooks.Clear()</c> 走 Reset 分支拿不到被移除的是谁，只照事件参数摘就会漏摘。
        /// </summary>
        private readonly HashSet<ScadaEventHook> _subscribedHooks = new();

        /// <summary>
        /// 索引重建挂起标志（批量迁移期用）。
        /// 旧方案补发身份时会逐个改写 <c>ElementId</c>，若每次都整表重建就是 O(n²)；
        /// 挂起后由 <see cref="EnsureIdentity"/> 在批量结束时重建一次。
        /// </summary>
        private bool _suspendIndexRebuild;

        /// <summary>画面稳定身份（切换/跳转画面按它寻址；不吃改名影响）</summary>
        public Guid PageId
        {
            get => _pageId;
            set => SetProperty(ref _pageId, value);
        }

        /// <summary>画面名（标签页标题、画面跳转下拉框用；不参与寻址）</summary>
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value ?? string.Empty);
        }

        /// <summary>画面说明（备注用途，运行态不读）</summary>
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value ?? string.Empty);
        }

        /// <summary>画布宽（设计像素）</summary>
        public double Width
        {
            get => _width;
            set => SetProperty(ref _width, value);
        }

        /// <summary>画布高（设计像素）</summary>
        public double Height
        {
            get => _height;
            set => SetProperty(ref _height, value);
        }

        /// <summary>
        /// 背景色（<c>#AARRGGBB</c> 文本）。
        /// 领域层不解析它，转 <c>System.Windows.Media.Color</c> 由 S2 控件层负责——
        /// 这正是 VM.Scada 能保持"不引用 WPF、可在无界面场景复用"的原因之一。
        /// </summary>
        public string Background
        {
            get => _background;
            set => SetProperty(ref _background, value ?? string.Empty);
        }

        /// <summary>设计器是否显示网格</summary>
        public bool ShowGrid
        {
            get => _showGrid;
            set => SetProperty(ref _showGrid, value);
        }

        /// <summary>网格间距（设计像素）</summary>
        public double GridSize
        {
            get => _gridSize;
            set => SetProperty(ref _gridSize, value);
        }

        /// <summary>拖动/缩放时是否吸附网格</summary>
        public bool SnapToGrid
        {
            get => _snapToGrid;
            set => SetProperty(ref _snapToGrid, value);
        }

        /// <summary>
        /// 是否启用<b>画面加载事件钩子</b>（<see cref="ScadaEventType.Loaded"/>）。
        ///
        /// 本属性<b>不落盘</b>，它只是 <see cref="EventHooks"/> 里"有没有 Loaded 这一条"的投影
        /// （与属性面板上"启动画面"那一行同源：真值在别处，行只是个开关）。
        /// 之所以不留一个独立布尔字段：那样就有两处真相——布尔勾着、钩子却被删了，
        /// 或者钩子在、布尔没勾，运行态该听谁的都说不清。S5 起动作归钩子管，开关就只能是投影。
        ///
        /// 勾选时会顺带补一条<b>默认动作</b>（记一行运行日志）。不补的话，"勾上了却什么都没发生"
        /// 是必然的（空动作表在执行侧等同于没配，见 <see cref="ScadaRuntime.RaiseElementEvent"/> 第三道闸门），
        /// 而 S4 的用户预期正是"这个框一勾，运行日志里就多一行加载记录"。
        /// 取消勾选是整条摘掉（含用户后来加的动作）——留着一条没人看见的钩子更糟。
        ///
        /// 默认 <c>false</c>，这个默认值<b>不能</b>改成 true：新建画面凭空带一条钩子，
        /// 等于每个画面都要在日志里吼一声，用户第一反应是"这软件怎么话这么多"。
        ///
        /// 设计期切画面<b>不</b>触发本钩子——组态软件的事件钩子是运行态语义，
        /// 编辑时切页只是换个画布画一遍，真触发的话"我在改图元，日志里蹦出一堆画面加载完成"，
        /// 那是在污染现场排障要用的东西。
        ///
        /// <b>属性面板现在不经过这里</b>：画面级事件行走的是 <see cref="IScadaEventHost"/> 那套通用路径
        /// （勾选框 → <see cref="GetOrAddEventHook"/> + 默认动作），因为「卸载」这类事件没有对应的投影字段，
        /// 让面板一半走投影、一半走通用路径才是真正的分叉。本属性保留为<b>模型层</b>的便捷开关
        /// （脚本、模板、将来"新建画面时预置一条加载动作"这类用法直接调它最省事），
        /// 两条路写出来的模型状态完全一致。
        /// </summary>
        [JsonIgnore]
        public bool EnableLoadedEvent
        {
            get => FindEventHook(ScadaEventType.Loaded) != null;
            set
            {
                if (value)
                {
                    // 已经勾着就原样返回：重复设 true 不该把画面标脏（版本号会因此空转）
                    if (FindEventHook(ScadaEventType.Loaded) != null)
                        return;

                    // 钩子 + 默认动作包在同一个作用域里：这是用户眼里的一次操作（勾一个框），
                    // 不该拆成"撤销一次只把动作删了、钩子还留着"的两步。
                    using (BeginEdit("启用画面加载事件"))
                    {
                        var hook = AddEventHook(ScadaEventType.Loaded);
                        hook.Actions.Add(ScadaChangeScope.Detached(() => new ScadaAction { Type = ScadaActionType.Log }));
                    }
                }
                else
                {
                    using (BeginEdit("停用画面加载事件"))
                    {
                        RemoveEventHook(ScadaEventType.Loaded);
                    }
                }
            }
        }

        /// <summary>
        /// 变更计数（只增不减，不落盘）。
        ///
        /// 作用与 <c>FlowModel.Version</c>（用于"改参数 → 自动重编译"）同源，只是消费者不同：
        /// 画面这一侧将来的消费者有两处——S3 编辑器（缩略图缓存失效、脏标记）与
        /// S4 运行态（图元/绑定变了要重建绑定表）。本阶段只负责如实递增，谁订阅谁解释。
        ///
        /// 递增规则：图元增删、任一图元的任一属性变更、图层增删、任意图层的任一属性变更，
        /// 都算一次变更（图层改名/隐藏会落盘，属于文档内容变了）。
        /// 由于模型里不放选中态这类编辑器态，"属性变了"就等于"文档内容真的变了"，无需排除名单。
        ///
        /// <b>刻意不走 <c>SetProperty</c></b>（唯一一处绕开基类收口的标量属性）：它是"写操作的回声"，
        /// 不是文档数据本身。借道基类会同时踩两处——
        /// ① <b>被写守卫误伤</b>：<c>Strict=true</c> 时，作用域外的一次 <c>Elements.Add</c> 会抛出
        ///    "直接写模型被拒：ScadaPage.Version"，把调用方指向一个根本没写的属性，
        ///    真正的违规点（集合增删）反而藏在异常消息之外；
        /// ② <b>被撤销栈误记</b>：版本号一旦可回滚，"撤销后版本号变小"，缩略图缓存/绑定表
        ///    会据此判断"没变过"从而不重建——这正是 S9 决策 6 要钉住的单调性。
        /// </summary>
        [JsonIgnore]
        public int Version
        {
            get => _version;
            set
            {
                if (_version == value)
                    return;

                _version = value;
                RaisePropertyChanged(nameof(Version));
            }
        }

        /// <summary>
        /// 打开一次可撤销的编辑（D3 统一写入口）。用法固定成一行：
        /// <code>
        /// using (page.BeginEdit("移动图元"))
        /// {
        ///     element.X = 120;
        ///     element.Y = 80;
        /// }
        /// </code>
        ///
        /// <b>为什么落点在画面</b>：撤销栈是"每个文档一份"的东西，而画面是编辑器的文档单位
        /// （<see cref="ScadaDocument"/> 里同时只有一张画面在编辑）。放在画面上，调用方写
        /// <c>page.BeginEdit(...)</c> 时天然带上了"改的是哪个文档"这层语义；
        /// 若暴露成静态的 <c>ScadaChangeScope.Begin</c>，读代码的人就得多想一步"栈是谁的"。
        ///
        /// <b>嵌套会合并</b>：内层不产记录，改动一律汇进最外层——所以
        /// "批量对齐 10 个图元"外层包一次即可，内层各 <c>Try*</c> 自开的作用域不会各占一个撤销位。
        /// </summary>
        public IScadaChangeScope BeginEdit(string label) => ScadaChangeScope.Begin(label);

        /// <summary>
        /// 图元集合。
        /// 订阅保活三件套（<c>ObjectCreationHandling.Replace</c> + 带 setter 的摘/挂 + <c>[JsonConstructor]</c>）
        /// 与 <c>FlowModel.Steps</c> 一致：反序列化必须走 setter，否则图元的属性订阅会丢，
        /// 表现为"打开方案后改图元属性，Version 不动"。
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<ScadaElement> Elements
        {
            get => _elements;
            set
            {
                var old = _elements;
                if (old != null)
                {
                    old.CollectionChanged -= OnElementsChanged;

                    // 摘订阅走登记表而不是 old 集合：两者正常时一致，但登记表是"真正挂过订阅的人"，
                    // 拿它兜底可以避免任何历史路径（Reset 漏摘等）留下的幽灵订阅被继承下来。
                    foreach (var element in _subscribedElements)
                        element.PropertyChanged -= OnElementPropertyChanged;

                    _subscribedElements.Clear();
                }

                _elements = value ?? new ObservableCollection<ScadaElement>();

                _elements.CollectionChanged += OnElementsChanged;
                foreach (var element in _elements)
                    Subscribe(element);

                RebuildIndex();
            }
        }

        /// <summary>
        /// 图层集合（落盘）。集合次序即图层列表里从上到下的次序（<b>不</b>参与叠放计算，
        /// 理由见 <see cref="ScadaLayer"/> 注释 ③）。
        ///
        /// 订阅保活三件套与 <see cref="Elements"/> 同一理由：图层的可见/锁定/改名会落盘，
        /// 属于文档内容变更，必须冒泡到 <see cref="Version"/>，否则"隐藏了一个图层却没提示保存"。
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<ScadaLayer> Layers
        {
            get => _layers;
            set
            {
                var old = _layers;
                if (old != null)
                {
                    old.CollectionChanged -= OnLayersChanged;

                    foreach (var layer in _subscribedLayers)
                        layer.PropertyChanged -= OnLayerPropertyChanged;

                    _subscribedLayers.Clear();
                }

                _layers = value ?? new ObservableCollection<ScadaLayer>();

                _layers.CollectionChanged += OnLayersChanged;
                foreach (var layer in _layers)
                    SubscribeLayer(layer);
            }
        }

        /// <summary>
        /// 画面级<b>事件钩子</b>（<see cref="ScadaEventType.Loaded"/> 与 <see cref="ScadaEventType.Unloaded"/>
        /// 两种都会真触发，见各自的枚举注释）。
        ///
        /// 与 <see cref="ScadaElement.EventHooks"/> 是同一个类型、同一套语义，只是"事件发生在我们自己身上"。
        /// 之所以不叫 <c>LoadedActions</c> 之类的专用名：画面事件本来就不止一个，拆成两个集合
        /// 就是把同一个概念按事件种类切碎（属性面板也因此能一份实现服务图元与画面两种宿主，
        /// 见 <see cref="IScadaEventHost"/>）。
        ///
        /// 落盘口径与图元一致：空集合 = 这个画面没配任何事件（正常状态，不是没配好）。
        /// <see cref="EnableLoadedEvent"/> 是这套集合上"有没有 Loaded"的投影，不是第二处真相。
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

        /// <summary>初始化画面（为初始空集合挂上订阅）</summary>
        [JsonConstructor]
        public ScadaPage()
        {
            _elements.CollectionChanged += OnElementsChanged;
            _layers.CollectionChanged += OnLayersChanged;
            _eventHooks.CollectionChanged += OnEventHooksChanged;
        }

        #region 事件钩子：查询与管理

        /// <summary>找某个事件的钩子，没配过返回 null（同事件多条时返回靠前者，与 <c>FindElement</c> 口径一致）</summary>
        public ScadaEventHook? FindEventHook(ScadaEventType eventType)
        {
            foreach (var hook in _eventHooks)
            {
                if (hook != null && hook.Event == eventType)
                    return hook;
            }

            return null;
        }

        /// <summary>找某个事件的钩子，没有就地建一条（属性面板"勾上某个事件"走这个）</summary>
        public ScadaEventHook GetOrAddEventHook(ScadaEventType eventType)
            => FindEventHook(eventType) ?? AddEventHook(eventType);

        /// <summary>新建一条空动作钩子并加入集合</summary>
        public ScadaEventHook AddEventHook(ScadaEventType eventType)
        {
            var hook = ScadaChangeScope.Detached(() => new ScadaEventHook { Event = eventType });

            using (BeginEdit($"新增事件钩子 [{eventType}]"))
            {
                _eventHooks.Add(hook);
            }

            return hook;
        }

        /// <summary>
        /// 摘掉某个事件的钩子（含它下面所有动作），返回是否真删掉了东西。
        /// 返回 bool 的理由与 <c>ScadaElement.RemoveEventHook</c> 相同：
        /// "取消勾选"在本来就没配的时候不该把画面版本号刷高。
        /// </summary>
        public bool RemoveEventHook(ScadaEventType eventType)
        {
            for (int i = 0; i < _eventHooks.Count; i++)
            {
                if (_eventHooks[i].Event != eventType)
                    continue;

                using (BeginEdit($"删除事件钩子 [{eventType}]"))
                {
                    _eventHooks.RemoveAt(i);
                }

                return true;
            }

            return false;
        }

        /// <summary>钩子集合增删 → 一次变更（挂/摘订阅的活交给登记表，理由同图元）</summary>
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

            Version++;
        }

        /// <summary>钩子内部变更（换事件、加动作、改动作参数）同样算画面变了</summary>
        private void OnHookPropertyChanged(object? sender, PropertyChangedEventArgs e) => Version++;

        /// <summary>挂钩子属性变更订阅（幂等）</summary>
        private void SubscribeHook(ScadaEventHook hook)
        {
            if (_subscribedHooks.Add(hook))
                hook.PropertyChanged += OnHookPropertyChanged;
        }

        /// <summary>摘钩子属性变更订阅（幂等）</summary>
        private void UnsubscribeHook(ScadaEventHook hook)
        {
            if (_subscribedHooks.Remove(hook))
                hook.PropertyChanged -= OnHookPropertyChanged;
        }

        #endregion

        #region 图层：查询与管理

        /// <summary>默认图层（列表第一项）。画面一个图层也没有时返回 null</summary>
        [JsonIgnore]
        public ScadaLayer? DefaultLayer => _layers.Count > 0 ? _layers[0] : null;

        /// <summary>按稳定身份找图层（找不到返回 null，表示"未分层/悬空归属"，语义见 <see cref="ScadaElement.LayerId"/>）</summary>
        public ScadaLayer? FindLayer(Guid layerId)
            => layerId == Guid.Empty
                ? null
                : _layers.FirstOrDefault(l => l.LayerId == layerId); // 重复身份取靠前者，与 FindElement 同一口径

        /// <summary>解析图元归属的图层（未分层、或归属的图层已被删掉 → null）</summary>
        public ScadaLayer? ResolveLayer(ScadaElement? element)
            => element == null ? null : FindLayer(element.LayerId);

        /// <summary>
        /// 图层是否可删除：<b>空图层</b>才可删，且画面不能删到没有任何图层。
        /// 不发明隐式级联（编辑器目前没有撤销，"删图层顺手删掉 30 个图元"是不可逆的数据丢失）。
        /// </summary>
        public bool CanRemoveLayer(ScadaLayer? layer)
            => layer != null && _layers.Count > 1 && !ContainsAnyElement(layer);

        /// <summary>本图层上的图元数（图层列表显示"3 个图元"、删除前判断能否删）</summary>
        public int CountElements(ScadaLayer? layer)
        {
            if (layer == null)
                return 0;

            int count = 0;
            foreach (var element in _elements)
            {
                if (element.LayerId == layer.LayerId)
                    count++;
            }

            return count;
        }

        /// <summary>新建图层并追加到列表末尾；名字留空则按"图层_N"自动命名（N 取未被占用的最小序号）</summary>
        public ScadaLayer AddLayer(string? name = null)
        {
            var layer = ScadaChangeScope.Detached(() => new ScadaLayer
            {
                Name = string.IsNullOrWhiteSpace(name) ? NextLayerName() : name!.Trim()
            });

            using (BeginEdit($"新建图层 [{layer.Name}]"))
            {
                _layers.Add(layer);
            }

            return layer;
        }

        /// <summary>
        /// 改名（画面内不许与其他图层重名）。校验口径照抄 <c>VariableRegistry.TryRename</c>：
        /// 空名拒、与原值等值幂等放行、查重用 <c>OrdinalIgnoreCase</c>、失败原因用可展示的中文带回。
        /// </summary>
        public bool TryRenameLayer(ScadaLayer? layer, string? newName, out string error)
        {
            error = string.Empty;

            if (layer == null || !_layers.Contains(layer))
            {
                error = "图层不存在，无法改名";
                return false;
            }

            var target = (newName ?? string.Empty).Trim();
            if (target.Length == 0)
            {
                error = "图层名不能为空";
                return false;
            }

            if (string.Equals(layer.Name, target, StringComparison.Ordinal))
                return true; // 幂等：连值都没变，不该把 Version 刷高一次

            if (_layers.Any(l => !ReferenceEquals(l, layer)
                                 && string.Equals(l.Name, target, StringComparison.OrdinalIgnoreCase)))
            {
                error = $"已存在同名图层 [{target}]，请更换名称";
                return false;
            }

            using (BeginEdit($"图层改名 [{target}]"))
            {
                layer.Name = target;
            }

            return true;
        }

        /// <summary>
        /// 删除图层。只允许删<b>空</b>图层，且画面必须留下至少一个图层——
        /// 非空图层被删时图元要么跟着消失、要么被悄悄塞进别的层，两者都是"用户没要求的改动"。
        /// 于是本方法宁可拒绝并把"还剩几个图元"如实回传，让用户先处理图元。
        /// </summary>
        public bool TryRemoveLayer(ScadaLayer? layer, out string error)
        {
            error = string.Empty;

            if (layer == null || !_layers.Contains(layer))
            {
                error = "图层不存在，无法删除";
                return false;
            }

            if (_layers.Count <= 1)
            {
                error = "画面至少需要保留一个图层，无法删除最后一个图层";
                return false;
            }

            int remaining = CountElements(layer);
            if (remaining > 0)
            {
                error = $"图层 [{layer.Name}] 上还有 {remaining} 个图元，请先删除或移走它们";
                return false;
            }

            using (BeginEdit($"删除图层 [{layer.Name}]"))
            {
                _layers.Remove(layer);
            }

            return true;
        }

        /// <summary>
        /// 调整图层在列表中的位置（<paramref name="targetIndex"/> 是移动后应处的下标）。
        /// 只动集合次序，不动任何图元的 ZIndex（见 <see cref="ScadaLayer"/> 注释 ③）。
        /// </summary>
        public bool TryMoveLayer(ScadaLayer? layer, int targetIndex, out string error)
        {
            error = string.Empty;

            if (layer == null)
            {
                error = "图层不存在，无法调整次序";
                return false;
            }

            int from = _layers.IndexOf(layer);
            if (from < 0)
            {
                error = "图层不属于本画面，无法调整次序";
                return false;
            }

            targetIndex = Math.Clamp(targetIndex, 0, _layers.Count - 1);
            if (from == targetIndex)
                return true; // 原地放置是空操作（同样不该刷 Version）

            using (BeginEdit($"图层排序 [{layer.Name}]"))
            {
                _layers.Move(from, targetIndex);
            }

            return true;
        }

        /// <summary>
        /// 把图元移到指定图层（<paramref name="target"/> 传 null = 取消分层归属）。
        /// 目标图层必须属于本画面，否则会把图元移进一个"画面里看不见的层"——直接拒绝。
        /// </summary>
        public bool TryAssignLayer(ScadaElement? element, ScadaLayer? target, out string error)
        {
            error = string.Empty;

            if (element == null || FindElement(element.ElementId) == null)
            {
                error = "图元不属于本画面，无法调整图层";
                return false;
            }

            if (target == null)
            {
                using (BeginEdit($"取消分层 [{element.Name}]"))
                {
                    element.LayerId = Guid.Empty;
                }

                return true;
            }

            if (FindLayer(target.LayerId) == null)
            {
                error = "目标图层不属于本画面，无法调整图层";
                return false;
            }

            using (BeginEdit($"移入图层 [{target.Name}]"))
            {
                element.LayerId = target.LayerId;
            }

            return true;
        }

        /// <summary>
        /// 调整图元的叠放次序（"谁盖住谁"只有 <see cref="ScadaElement.ZIndex"/> 一个来源）。
        ///
        /// 落点为什么在画面而不是让面板自己改 <c>ZIndex</c>：① D3 规定"谁能改模型"只能有一个答案；
        /// ② 叠放要先知道"整张画面的有序序列里，它上面那个是谁"，这件事只有画面自己算得出来。
        ///
        /// 实现是<b>先排序、再搬位、最后按 1..N 重编号</b>，而不是"和邻居对调两个 ZIndex"：
        /// 老工程（或手工改过的 .vms）里图元的 ZIndex 可能整片重复（默认值 0），
        /// 对调两个相等的值等于什么都没干，而重编号永远得到一条严格递增的序列——
        /// 叠放这件事的真相就是次序，不是那几个数值。代价是每个图元都会被写一次 ZIndex，
        /// 但 <see cref="ScadaElement.ZIndex"/> 的 setter 对同值短路，只有真正挪动过的才会冒泡变更。
        ///
        /// 已经在端点（最上面还要置顶、最下面还要置底）时是空操作，返回 <c>true</c> 且不刷
        /// <see cref="Version"/>——与 <see cref="TryMoveLayer"/> 的原地放置同一口径。
        /// </summary>
        public bool TryMoveElementZ(ScadaElement? element, ScadaZMove move, out string error)
        {
            error = string.Empty;

            if (element == null || FindElement(element.ElementId) == null)
            {
                error = "图元不属于本画面，无法调整叠放次序";
                return false;
            }

            // OrderBy 是稳定排序：ZIndex 相同的图元保持集合次序（老工程里整片 0 的情形走这条），
            // 于是重编号不会把本来就分不出先后的图元随机洗一遍。
            var ordered = _elements.OrderBy(e => e.ZIndex).ToList();
            int from = ordered.IndexOf(element);

            int to = move switch
            {
                ScadaZMove.ToFront => ordered.Count - 1,
                ScadaZMove.Forward => Math.Min(from + 1, ordered.Count - 1),
                ScadaZMove.Backward => Math.Max(from - 1, 0),
                ScadaZMove.ToBack => 0,
                // 不认识的取值按"原地不动"处理：坏在一个枚举上不该让整张画面炸掉
                // （与 ScadaActionTypeExtensions 对未知值报数而不是抛异常同一取舍）。
                _ => from,
            };

            if (to == from)
                return true; // 已经在端点，空操作

            ordered.RemoveAt(from);
            ordered.Insert(to, element);

            // 重编号 1..N：画面底图常见 ZIndex = 0，会被抬到 1，但它仍排在 ordered[0]，
            // 叠放关系不变。模型里没有任何逻辑依赖"0 = 背景"这个约定，
            // 而新增图元按 Elements.Count + 1 分配（见 ScadaEditorViewModel），
            // 重编号后恰好接着 N+1 往上排，两边不会打架。
            using (BeginEdit($"调整叠放次序 [{element.Name}]"))
            {
                for (int i = 0; i < ordered.Count; i++)
                    ordered[i].ZIndex = i + 1;
            }

            return true;
        }

        /// <summary>
        /// 删除图元（Delete 键 / 右键菜单）。
        ///
        /// 与 <see cref="TryMoveElementZ"/> 同一个理由放在画面这一层，而不是让编辑器直接
        /// <c>Elements.Remove</c>：① D3 规定"谁能改模型"只能有一个答案；② 删除必须罩在
        /// <see cref="BeginEdit"/> 里才能被记成一条可撤销操作——裸调 <c>Remove</c> 时
        /// 集合回调照样会记，但记下的是一条<b>没有操作名</b>的散记录，撤销按钮上就会显示
        /// "编辑"而不是"删除图元"。
        ///
        /// 图元自身的绑定与事件钩子<b>不在这里逐个清</b>：它们都挂在这个图元对象上，
        /// 对象一旦离开集合就不可达（<c>_elementIndex</c> 与属性订阅都由集合回调顺手摘掉），
        /// 逐个清反而多一条"漏清一处就留个幽灵"的路径。
        /// </summary>
        public bool TryRemoveElement(ScadaElement? element, out string error)
        {
            error = string.Empty;

            if (element == null || FindElement(element.ElementId) == null)
            {
                error = "图元不属于本画面，无法删除";
                return false;
            }

            using (BeginEdit($"删除图元 [{element.Name}]"))
            {
                _elements.Remove(element);
            }

            return true;
        }

        /// <summary>
        /// 添加图元（粘贴 / 模板物化 / 脚本批量生成）。
        ///
        /// 与 <see cref="TryRemoveElement"/> 是<b>对称</b>的两端，理由也同一个：D3 规定
        /// "谁能改模型"只能有一个答案，且新图元必须罩在 <see cref="BeginEdit"/> 里，
        /// 否则集合回调记下的是一条没有操作名的散记录，撤销按钮上显示"编辑"而不是"粘贴"。
        ///
        /// 判据与删除端<b>刻意不同</b>：删除端用 <see cref="FindElement"/> 判"是不是本画面的"，
        /// 因为要删的对象本该已在集合里；添加端的对象是<b>外来</b>的，按身份查一定是 null，
        /// 所以改用引用比较判"是不是已经在集合里了"。两个判据各自对着自己的语义，
        /// 不共用一个 helper。
        ///
        /// <b>身份（<see cref="ScadaElement.ElementId"/>）不在这里查重</b>：索引对重复身份
        /// 已有既定口径（<see cref="AddToIndex"/> 保留靠前者、<see cref="RebuildIndex"/> 同款），
        /// 在这里再拦一道只会多出一处"谁来保证唯一"的第二口径。物化端负责重编身份。
        /// </summary>
        /// <param name="element">要加入的图元（<c>null</c> 或已在本画面时拒绝）</param>
        /// <param name="error">失败原因（直接可展示给用户的中文文案）</param>
        public bool TryAddElement(ScadaElement? element, out string error)
        {
            error = string.Empty;

            if (element == null)
            {
                error = "图元为空，无法添加";
                return false;
            }

            if (_elements.Contains(element))
            {
                error = "图元已在本画面中，无法重复添加";
                return false;
            }

            using (BeginEdit($"添加图元 [{element.Name}]"))
            {
                _elements.Add(element);
            }

            return true;
        }

        /// <summary>
        /// 锁定 / 解锁一组图元（设计期防误挪）。
        ///
        /// <b>这里刻意不按 <see cref="IsElementEditable"/> 过滤</b>——这是本类里唯一一处
        /// 反着来的批量入口，理由很实在：<see cref="IsElementEditable"/> 把"已锁定"判为不可编辑，
        /// 而"解锁"的目标<b>恰恰就是那些已经锁上的图元</b>。照抄别的入口的过滤条件，
        /// 解锁会变成永远的空操作（菜单亮着、点下去什么也不发生），这种 bug 在真机上
        /// 只会被记成"锁了之后再也解不开"。锁定与解锁共用这一个入口，两条路都不许筛锁定态。
        ///
        /// 只过滤两件事：不属于本画面的（外来对象 / 撤销之后已经不在的）、重复项。
        ///
        /// 与 <see cref="TryMoveElementZ"/> 的端点口径一致：<b>全员已经是目标状态</b>时
        /// 是空操作，返回 <c>true</c> 且不产记录——重复点"锁定"不该在撤销栈里堆一串
        /// 什么都没干的记录，那会让用户按 Ctrl+Z 时"撤了半天画面没动"。
        /// 因此操作名里的数量是<b>真正发生变化的个数</b>，不是传进来的个数。
        /// </summary>
        /// <param name="elements">要改的图元（可以含重复项与不属于本画面的项，内部会过滤）</param>
        /// <param name="locked">目标状态：true = 锁定，false = 解锁</param>
        /// <param name="error">失败原因（直接可展示给用户的中文文案）</param>
        public bool TrySetElementLocked(IReadOnlyList<ScadaElement>? elements, bool locked, out string error)
        {
            error = string.Empty;

            if (elements == null || elements.Count == 0)
            {
                error = "至少要选中 1 个图元";
                return false;
            }

            var present = new List<ScadaElement>(elements.Count);

            foreach (var element in elements)
            {
                if (element == null || FindElement(element.ElementId) == null)
                    continue;

                if (present.Contains(element))
                    continue;

                present.Add(element);
            }

            if (present.Count == 0)
            {
                error = "选中的图元不属于本画面";
                return false;
            }

            // 只留"状态确实要变"的：数量口径与撤销记录的产出都按它算（见上面端点口径那段）
            var targets = present.Where(e => e.IsLocked != locked).ToList();

            if (targets.Count == 0)
                return true; // 已经是目标状态，空操作

            string label = locked
                ? (targets.Count == 1 ? $"锁定图元 [{targets[0].Name}]" : $"锁定 {targets.Count} 个图元")
                : (targets.Count == 1 ? $"解锁图元 [{targets[0].Name}]" : $"解锁 {targets.Count} 个图元");

            using (BeginEdit(label))
            {
                foreach (var element in targets)
                    element.IsLocked = locked;
            }

            return true;
        }

        /// <summary>
        /// 设置单个图元的<b>操作权限</b>（运行态谁能操作它）。
        ///
        /// <b>为什么要有这个入口，而不是让面板直接写 <see cref="ScadaElement.RequiredRole"/></b>
        /// ---------
        /// D3：改模型只有一个入口。裸写 setter 会绕过 <see cref="BeginEdit"/>，
        /// 于是"把权限从操作员提到工程师"这件事<b>不进撤销栈</b>——用户改错了只能再手改回来，
        /// 而权限是那种改错了当场看不出问题、等到运行态某个人按不动按钮才发现的配置。
        /// 这与 <see cref="TrySetElementLocked"/> 把 <see cref="ScadaElement.IsLocked"/> 收进来的理由同源。
        ///
        /// <b>为什么拒绝 <see cref="ScadaRole.Undefined"/></b>
        /// ---------
        /// <c>null</c> 与 <c>Undefined</c> 是两件事：前者是"不限制"（合法，默认值），
        /// 后者是<b>非法值</b>（判定侧一律拒绝，见 <see cref="ScadaRoleExtensions.Allows"/>）。
        /// 允许设计器写进 <c>Undefined</c>，等于造出一个"谁都按不动、界面上还看不出为什么"的图元——
        /// 这种配置一旦落盘，只能靠手工改 .vms 才能救回来。写入侧是唯一的防线，所以在这里挡住。
        ///
        /// <b>值没变时是空操作</b>（返回 <c>true</c> 且不产记录），口径与
        /// <see cref="TrySetElementLocked"/> / <see cref="TryMoveElementZ"/> 一致：
        /// 面板刷新、下拉框重选同一项都不该在撤销栈里堆记录。
        /// </summary>
        /// <param name="element">要改的图元</param>
        /// <param name="role">目标角色；<c>null</c> = 不限制</param>
        /// <param name="error">失败原因（直接可展示给用户的中文文案）</param>
        public bool TrySetRequiredRole(ScadaElement? element, ScadaRole? role, out string error)
        {
            error = string.Empty;

            if (element == null || FindElement(element.ElementId) == null)
            {
                error = "图元不属于本画面，无法设置操作权限";
                return false;
            }

            if (role is { } value && !value.IsDefined())
            {
                error = $"未知的角色取值（{(int)value}），无法设置操作权限";
                return false;
            }

            if (element.RequiredRole == role)
                return true; // 值没变，空操作

            string label = role is { } target
                ? $"设置操作权限 [{element.Name}] → {target.DisplayName()}"
                : $"取消操作权限限制 [{element.Name}]";

            using (BeginEdit(label))
            {
                element.RequiredRole = role;
            }

            return true;
        }

        /// <summary>
        /// 把一批图元组合成一个组（同组图元在编辑器里被当成一个整体选中与搬动）。
        ///
        /// <b>只写一个 Guid，不动几何、不动叠放、不动归属</b>：组合改变的是"编辑期怎么选中"，
        /// 不是"画出来什么样"。因此这个入口与 <see cref="TryAlignElements"/> 是反着的一对——
        /// 对齐必须筛掉锁定的（动不了的不能当基准），而组合<b>不筛锁定</b>：
        /// 组是一个关系，把一件锁住的底图和它旁边的标注捆在一起是合理诉求，
        /// 而锁定本身已经保证了它不会被拖走（画布的拖动只搬可编辑的那些），
        /// 在这里再筛一遍只会得到"选了三个、只有一个没进组"这种解释不清的结果。
        ///
        /// <b>全员已同属一组时是空操作</b>（返回 <c>true</c> 且不产记录）：
        /// 点中一个组员就会把整组选中（见 <see cref="GetGroupMembers"/>），
        /// 于是"选中一个组、再点一次组合"是很自然的误操作，它不该在撤销栈里堆一条什么都没改的记录。
        /// 判据是"所有目标共用同一个非空 GroupId"，而不是"曾经组合过"——
        /// 后者需要额外的状态，而这里没有任何状态可存。
        ///
        /// <b>不支持嵌套</b>：把两个已有的组再组合，结果是<b>合并成一个新组</b>（旧组 Id 就此消失，
        /// 可撤销），而不是套一层。理由见 <see cref="ScadaElement.GroupId"/> 的注释。
        ///
        /// 少于 2 个目标一律拒绝：一个成员的组与未分组没有任何行为差别，
        /// 允许它只会让用户以为自己建了个组。
        /// </summary>
        /// <param name="elements">要组合的图元（可以含重复项与不属于本画面的项，内部会过滤）</param>
        /// <param name="error">失败原因（直接可展示给用户的中文文案）</param>
        public bool TryGroupElements(IReadOnlyList<ScadaElement>? elements, out string error)
        {
            error = string.Empty;

            if (elements == null || elements.Count < 2)
            {
                error = "至少要选中 2 个图元才能组合";
                return false;
            }

            var targets = FilterPresent(elements);

            if (targets.Count < 2)
            {
                error = "可组合的图元不足 2 个（图元可能已不在本画面上）";
                return false;
            }

            Guid existing = targets[0].GroupId;

            if (existing != Guid.Empty)
            {
                bool sameGroup = true;

                for (int i = 1; i < targets.Count; i++)
                {
                    if (targets[i].GroupId != existing)
                    {
                        sameGroup = false;
                        break;
                    }
                }

                if (sameGroup)
                    return true; // 已经是一组了，空操作
            }

            var groupId = Guid.NewGuid();

            using (BeginEdit($"组合 {targets.Count} 个图元"))
            {
                foreach (var element in targets)
                    element.GroupId = groupId;
            }

            return true;
        }

        /// <summary>
        /// 解散一批图元所属的组（把它们的 <see cref="ScadaElement.GroupId"/> 清回 <c>Guid.Empty</c>）。
        ///
        /// 与 <see cref="TryGroupElements"/> 一样<b>不筛锁定</b>：组合与取消组合必须共用同一套
        /// "哪些图元算数"的口径，否则会出现"锁住之后解不开"——那正是
        /// <see cref="TrySetElementLocked"/> 的注释里点名的那个坑。
        ///
        /// <b>不做"必须选中整组"的校验</b>：组是模型里的一个 Guid，选中整组只是画布的便利行为，
        /// 取消组合这个动作本身对"选中的是组的一部分还是全部"没有区别——清掉就完了。
        /// 加一条"必须整组"的校验，等于给"撤销到一半的中间态"和"成员被删剩两个"的脏数据
        /// 各留一条走不通的路。
        ///
        /// 只对<b>确实有组</b>的图元动手（数量口径与撤销记录都按它算）：
        /// 混进来几个本来就没分组的，不算错，但也不该让操作名把它们的数量算进去。
        /// 全都没组时返回 <c>true</c> 且不产记录，与 <see cref="TrySetElementLocked"/> 的端点口径一致。
        /// </summary>
        /// <param name="elements">要取消组合的图元（可以含重复项与不属于本画面的项，内部会过滤）</param>
        /// <param name="error">失败原因（直接可展示给用户的中文文案）</param>
        public bool TryUngroupElements(IReadOnlyList<ScadaElement>? elements, out string error)
        {
            error = string.Empty;

            if (elements == null || elements.Count == 0)
            {
                error = "至少要选中 1 个图元";
                return false;
            }

            var present = FilterPresent(elements);

            if (present.Count == 0)
            {
                error = "选中的图元不属于本画面";
                return false;
            }

            var targets = present.Where(e => e.GroupId != Guid.Empty).ToList();

            if (targets.Count == 0)
                return true; // 本来就没分组，空操作

            // 单个时写名字、多个时写数量：与 TrySetElementLocked 的文案口径逐字同源。
            // 取消组合能落到"只有一个"（组合不会，它至少两个），
            // 撤销按钮上"取消组合 1 个图元"读起来像句病句，写名字才知道撤的是哪一个。
            using (BeginEdit(targets.Count == 1
                ? $"取消组合图元 [{targets[0].Name}]"
                : $"取消组合 {targets.Count} 个图元"))
            {
                foreach (var element in targets)
                    element.GroupId = Guid.Empty;
            }

            return true;
        }

        /// <summary>
        /// 把调用方递进来的一批图元收敛成"确实属于本画面、且去重"的一张表（顺序照传入顺序）。
        ///
        /// 组合与取消组合共用它，是为了让"属于本画面 / 去重"这两条过滤只有一份实现——
        /// 各写一遍的下场是"组合会跳过外来对象、取消组合却把它一起清了"这类劈叉。
        /// 与 <see cref="TrySetElementLocked"/> 里那段内联过滤逐字同源（那边暂时保持内联，
        /// 它的过滤与"只留状态要变的"那一步咬在一起，抽出来反而更难读）。
        /// </summary>
        private List<ScadaElement> FilterPresent(IReadOnlyList<ScadaElement> elements)
        {
            var present = new List<ScadaElement>(elements.Count);

            foreach (var element in elements)
            {
                if (element == null || FindElement(element.ElementId) == null)
                    continue;

                if (present.Contains(element))
                    continue;

                present.Add(element);
            }

            return present;
        }

        /// <summary>
        /// 把一组图元按 <paramref name="align"/> 摆整齐（六种对齐 + 两种分布）。
        ///
        /// 为什么整段摆在画面这一层，而不是让编辑器视图模型自己算完再写 X/Y：
        /// ① D3——"谁能改模型"只能有一个答案，视图模型直写 <c>element.X</c> 在
        ///    <see cref="ScadaWriteGuard.Strict"/> 下会被当场拒掉（<c>ScadaWriteGuard</c> 的类注释
        ///    点名的就是这个"一键对齐"漏网）；
        /// ② 对齐要读<b>整组的包围盒</b>（最左、最右、中轴），这是"一组图元放在一起看"才知道的事，
        ///    让每个调用点各算一遍，迟早出现"菜单预览的位置和点下去的位置差半格"；
        /// ③ 必须罩在 <see cref="BeginEdit"/> 里才能被记成<b>一条</b>可撤销操作——
        ///    批量挪 10 个图元若各记一条，用户要按 10 次 Ctrl+Z 才能回到对齐前。
        ///
        /// 锁定的图元<b>既不参与、也不作为基准</b>：拿一个动不了的图元当基准，结果就是
        /// "其余都挪了、就它没动"，看着像对齐失败。这与拖动/删除的口径一致
        /// （<see cref="IsElementEditable"/> 是唯一的编辑资格判定）。
        ///
        /// 数量不足（对齐 &lt; 2、分布 &lt; 3）或可编辑图元不足时返回 <c>false</c> 且不产记录。
        /// </summary>
        /// <param name="elements">参与排列的图元（可以含重复项与不属于本画面的项，内部会过滤）</param>
        /// <param name="align">排列动作</param>
        /// <param name="error">失败原因（直接可展示给用户的中文文案）</param>
        public bool TryAlignElements(IReadOnlyList<ScadaElement>? elements, ScadaAlign align, out string error)
        {
            error = string.Empty;

            int minimum = align.MinimumCount();

            if (elements == null || elements.Count < minimum)
            {
                error = $"「{align.DisplayName()}」至少要选中 {minimum} 个图元";
                return false;
            }

            // 过滤三件事：不属于本画面的（外来对象）、锁定的（动不了也不该当基准）、重复项。
            // 不在这里判 IsElementVisible：隐藏层上的图元根本选不中（画布在图层隐藏时就清掉了选中），
            // 多判一次等于给一条永远不会走到的分支写代码。
            var targets = new List<ScadaElement>(elements.Count);

            foreach (var element in elements)
            {
                if (element == null || FindElement(element.ElementId) == null)
                    continue;

                if (!IsElementEditable(element))
                    continue;

                if (!targets.Contains(element))
                    targets.Add(element);
            }

            if (targets.Count < minimum)
            {
                error = $"可排列的图元不足 {minimum} 个（锁定的图元不参与）";
                return false;
            }

            // 包围盒先算出来：六种对齐共用同一份基准，各算各的迟早出现"左对齐和水平居中对不上同一个框"。
            // 基准只由<b>参与排列的图元</b>决定（锁定的不参与、也不撑大包围盒），
            // 否则会出现"框比看得见的图元大一圈、对齐后整体偏出去"这种解释不通的结果。
            double minLeft = double.MaxValue, maxRight = double.MinValue;
            double minTop = double.MaxValue, maxBottom = double.MinValue;

            foreach (var element in targets)
            {
                minLeft = Math.Min(minLeft, element.X);
                maxRight = Math.Max(maxRight, element.X + element.Width);
                minTop = Math.Min(minTop, element.Y);
                maxBottom = Math.Max(maxBottom, element.Y + element.Height);
            }

            double centerX = (minLeft + maxRight) / 2;
            double centerY = (minTop + maxBottom) / 2;

            // 一次排列 = 一条撤销记录。标签里带上个数与动作名，撤销按钮上就能读出"这一步撤掉的是什么"。
            using (BeginEdit($"对齐 [{targets.Count} 个图元]：{align.DisplayName()}"))
            {
                switch (align)
                {
                    case ScadaAlign.Left:
                        foreach (var element in targets)
                            element.X = minLeft;
                        break;

                    case ScadaAlign.Right:
                        foreach (var element in targets)
                            element.X = maxRight - element.Width;
                        break;

                    case ScadaAlign.HorizontalCenter:
                        foreach (var element in targets)
                            element.X = centerX - element.Width / 2;
                        break;

                    case ScadaAlign.Top:
                        foreach (var element in targets)
                            element.Y = minTop;
                        break;

                    case ScadaAlign.Bottom:
                        foreach (var element in targets)
                            element.Y = maxBottom - element.Height;
                        break;

                    case ScadaAlign.VerticalCenter:
                        foreach (var element in targets)
                            element.Y = centerY - element.Height / 2;
                        break;

                    case ScadaAlign.DistributeHorizontal:
                        Distribute(targets, horizontal: true, minLeft, maxRight);
                        break;

                    case ScadaAlign.DistributeVertical:
                        Distribute(targets, horizontal: false, minTop, maxBottom);
                        break;

                    // 不认识的取值按空操作处理：坏在一个枚举上不该让整张画面炸掉
                    // （与 TryMoveElementZ 对未知方向的处理同一取舍）。
                    default:
                        break;
                }
            }

            return true;
        }

        /// <summary>
        /// 等间隙分布：<b>两端不动</b>，把中间几个按"相邻图元之间的空隙都一样大"重排。
        ///
        /// 为什么是"等间隙"而不是"等中心距"：图元宽度不一时，等中心距看上去仍是乱的
        /// （宽的挤在一起、窄的之间空一大片），而等间隙是肉眼唯一能验证"排匀了"的口径，
        /// 也是 Illustrator / PowerPoint 那一档软件的做法。
        ///
        /// 间隙可以是负数：图元本身重叠时"排匀"的结果就是均匀地重叠，不做额外处理——
        /// 强行把它们推开等于替用户决定"不该重叠"，那不是排列该管的事。
        /// </summary>
        private static void Distribute(List<ScadaElement> targets, bool horizontal, double start, double end)
        {
            // 稳定排序：位置相同的图元保持选中次序，不会每次点一下都换个排法。
            // 必须按同一根轴排序——水平分布按 X 排、垂直分布按 Y 排，拿错轴会把顺序搅乱。
            var ordered = targets
                .OrderBy(e => horizontal ? e.X : e.Y)
                .ToList();

            double sum = 0;
            foreach (var element in ordered)
                sum += horizontal ? element.Width : element.Height;

            double gap = ((end - start) - sum) / (ordered.Count - 1);

            double cursor = start;

            foreach (var element in ordered)
            {
                if (horizontal)
                {
                    element.X = cursor;
                    cursor += element.Width + gap;
                }
                else
                {
                    element.Y = cursor;
                    cursor += element.Height + gap;
                }
            }
        }

        /// <summary>
        /// 图元当前能不能被编辑（选中/拖动/改尺寸/改属性）。
        /// 图层锁定与图元锁定是<b>或</b>关系；未分层的图元不受图层约束。
        /// 唯一口径放在这里，视图侧（画布、属性面板、工具箱落点）各自读一份迟早会不一致。
        /// </summary>
        public bool IsElementEditable(ScadaElement? element)
            => element != null && !element.IsLocked && ResolveLayer(element)?.IsLocked != true;

        /// <summary>图元当前该不该显示（图层隐藏会盖掉图元自身的可见性；未分层则不受图层约束）</summary>
        public bool IsElementVisible(ScadaElement? element)
            => element != null && ResolveLayer(element)?.IsVisible != false;

        /// <summary>
        /// 与 <paramref name="element"/> 同属一组的全部图元（含它自己，按本画面图元顺序）。
        /// 未分组（<see cref="ScadaElement.GroupId"/> 为 <c>Guid.Empty</c>）返回空表。
        ///
        /// 这是"选中即整组"的唯一判据来源：画布点中一个组员时要把它的一整组一起选上，
        /// 而"谁跟谁同组"只有模型知道（画布手里只有控件的可视树，那棵树里没有组这回事）。
        /// 视图侧自己按 <c>GroupId</c> 再筛一遍，就会出现"画布认一组、菜单认另一组"的劈叉。
        ///
        /// <b>刻意不返回"只有一个成员时算作没有组"</b>：那种区别要靠调用方每次都判，
        /// 而单成员组与未分组在行为上本就等价（选中它只会选中它自己），多一个特例不换来任何东西。
        /// 调用方拿到的是一张可以直接铺成选中集合的表。
        ///
        /// 线性扫描而不是维护一张"组 → 成员"的索引：组是编辑期的低频关系
        /// （点一下鼠标才算一次），而索引要在增删图元、改归属、撤销回滚所有路径上保活，
        /// 那才是真正容易出错的地方（<c>_elementIndex</c> 是高频按 Id 单点取才值得单独养）。
        /// </summary>
        public IReadOnlyList<ScadaElement> GetGroupMembers(ScadaElement? element)
        {
            if (element == null || element.GroupId == Guid.Empty)
                return Array.Empty<ScadaElement>();

            var members = new List<ScadaElement>();

            foreach (var item in _elements)
            {
                if (item.GroupId == element.GroupId)
                    members.Add(item);
            }

            return members;
        }

        private bool ContainsAnyElement(ScadaLayer layer)
        {
            foreach (var element in _elements)
            {
                if (element.LayerId == layer.LayerId)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 图元落名：重名就续 _2、_3。
        ///
        /// 唯一性只在<b>同一画面内</b>要求：跨画面重名太常见且无害（不同页是不同上下文），
        /// 要全局唯一的话"复制一屏设备到另一页"会变成一场改名工程。
        ///
        /// 放在画面这一层而不是留在编辑器里，是因为粘贴与模板物化都在<b>领域层</b>跑
        /// （<see cref="ScadaClipboard"/>），领域层不能反向引用视图模型；而查重本身
        /// 只依赖 <see cref="Elements"/>，本来就是这个类自己的事。
        /// </summary>
        public string MakeUniqueElementName(string baseName)
        {
            if (!_elements.Any(e => string.Equals(e.Name, baseName, StringComparison.Ordinal)))
                return baseName;

            // 候选数上界取"已有图元数 + 2"：已有名字至多占掉 Elements.Count 个坑，
            // 所以这个区间里必定还有一个空位，循环不会跑飞。
            for (int n = 2; n <= _elements.Count + 2; n++)
            {
                string candidate = $"{baseName}_{n}";
                if (!_elements.Any(e => string.Equals(e.Name, candidate, StringComparison.Ordinal)))
                    return candidate;
            }

            return $"{baseName}_{_elements.Count + 1}";
        }

        /// <summary>取当前未被占用的最小"图层_N"（与 <c>NextPageName</c> 同一口径：忽略大小写）</summary>
        private string NextLayerName()
        {
            var used = new HashSet<string>(_layers.Select(l => l.Name), StringComparer.OrdinalIgnoreCase);

            int index = 1;
            while (used.Contains($"图层_{index}"))
                index++;

            return $"图层_{index}";
        }

        private void OnLayersChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            ScadaCollectionRecorder.Record(_layers, e);

            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var layer in _subscribedLayers)
                    layer.PropertyChanged -= OnLayerPropertyChanged;

                _subscribedLayers.Clear();

                foreach (var layer in _layers)
                    SubscribeLayer(layer);
            }
            else
            {
                if (e.NewItems != null)
                {
                    foreach (ScadaLayer layer in e.NewItems)
                        SubscribeLayer(layer);
                }

                if (e.OldItems != null)
                {
                    foreach (ScadaLayer layer in e.OldItems)
                        UnsubscribeLayer(layer);
                }
            }

            Version++;
        }

        /// <summary>图层的可见/锁定/改名都会落盘 → 冒泡成画面变更（脏标记与缩略图失效都靠它）</summary>
        private void OnLayerPropertyChanged(object? sender, PropertyChangedEventArgs e) => Version++;

        /// <summary>挂图层属性变更订阅（幂等：登记表已存在则不重复挂）</summary>
        private void SubscribeLayer(ScadaLayer layer)
        {
            if (_subscribedLayers.Add(layer))
                layer.PropertyChanged += OnLayerPropertyChanged;
        }

        /// <summary>摘图层属性变更订阅（幂等：登记表没有则不动手）</summary>
        private void UnsubscribeLayer(ScadaLayer layer)
        {
            if (_subscribedLayers.Remove(layer))
                layer.PropertyChanged -= OnLayerPropertyChanged;
        }

        #endregion

        /// <summary>
        /// 按稳定身份找图元（找不到返回 null）。
        /// 走 <see cref="_elementIndex"/>，与集合规模无关；这是 S3 选中/命中测试与 S4 运行态取图元的主路径。
        /// </summary>
        public ScadaElement? FindElement(Guid elementId)
            => elementId != Guid.Empty && _elementIndex.TryGetValue(elementId, out var element) ? element : null;

        /// <summary>枚举本画面所有绑定（含停用的），供加载期解析/改名级联/运行态建表统一遍历</summary>
        public IEnumerable<ScadaBinding> EnumerateBindings()
            => _elements.SelectMany(e => e.Bindings);

        #region 绑定：统一写入口（D3）

        /// <summary>找本画面内某图元某属性上的绑定；图元不属于本画面、或该属性没绑过 → null</summary>
        public ScadaBinding? FindBinding(ScadaElement? element, string? targetProperty)
            => element != null && FindElement(element.ElementId) != null
                ? element.FindBinding(targetProperty)
                : null;

        /// <summary>
        /// 把 <paramref name="element"/> 的 <paramref name="targetProperty"/> 挂到变量
        /// <paramref name="variableId"/> 上——属性面板点 ƒx 选完变量的落点。
        ///
        /// 同属性已绑过 → <b>换绑</b>：复用原条目（<see cref="ScadaElement.GetOrAddBinding"/>），
        /// 保住用户已配的停用/格式串。口径是"一个属性至多一条绑定"，理由见该方法注释。
        ///
        /// 校验只做领域层管得着的两件事：图元属于本画面、属性键非空。
        /// 属性键<b>是不是这个图元类型声明的键</b>，领域层无从判断——描述符在 S2 的 Scada.Controls，
        /// 而领域层刻意不引用它（见 VM.Scada.csproj 的注释）。那一层校验由调用方用
        /// <c>ElementRegistry.FindProperty</c> 先做，运行态建表时还会再兜一次（见 ScadaRuntimeBinder.BuildBinding）。
        /// </summary>
        public bool TrySetBinding(ScadaElement? element, string? targetProperty,
            Guid variableId, string? variableName, out string error)
        {
            error = string.Empty;

            if (element == null || FindElement(element.ElementId) == null)
            {
                error = "图元不属于本画面，无法建立绑定";
                return false;
            }

            if (string.IsNullOrWhiteSpace(targetProperty))
            {
                error = "属性键为空，无法建立绑定";
                return false;
            }

            if (variableId == Guid.Empty)
            {
                // 空 Id 的绑定是"只能按名字找"的旧数据形态，不该由设计器现场制造出来：
                // 一旦落盘，这条绑定就永远走不上"改名不断"的正轨（见 ScadaBinding.IsLegacyByName）。
                error = "变量 Id 为空，无法建立绑定";
                return false;
            }

            using (BeginEdit($"绑定 {targetProperty}"))
            {
                element.GetOrAddBinding(targetProperty!).Bind(variableId, variableName);
            }

            return true;
        }

        /// <summary>
        /// 清除某图元某属性上的绑定。返回值表示"这次操作成立"——本来就没绑也算成立
        /// （结果与用户意图一致，且集合没动，画面版本号不会平白刷高）。
        /// </summary>
        public bool TryRemoveBinding(ScadaElement? element, string? targetProperty, out string error)
        {
            error = string.Empty;

            if (element == null || FindElement(element.ElementId) == null)
            {
                error = "图元不属于本画面，无法清除绑定";
                return false;
            }

            if (string.IsNullOrWhiteSpace(targetProperty))
            {
                error = "属性键为空，无法清除绑定";
                return false;
            }

            using (BeginEdit($"清除绑定 {targetProperty}"))
            {
                element.RemoveBinding(targetProperty);
            }

            return true;
        }

        #endregion

        /// <summary>
        /// 变量改名后的引用刷新：递归到每个图元的绑定/钩子上，另加<b>画面自己的钩子</b>
        /// （"页面加载时把某个变量清零"同样会引用变量名）。返回被改动的条数。
        /// </summary>
        public int RefreshVariableReferences(Guid variableId, string? oldName, string newName)
        {
            int changed = 0;

            foreach (var element in _elements)
                changed += element.RefreshVariableReferences(variableId, oldName, newName);

            foreach (var hook in _eventHooks)
            {
                if (hook != null)
                    changed += hook.RefreshVariableReferences(variableId, oldName, newName);
            }

            return changed;
        }

        /// <summary>
        /// 补发缺失的稳定身份（旧数据迁移）。返回补发的条数。
        ///
        /// 与 S0-a 给变量补发 Id、SolutionService 里 NormalizeBranchTypes 同属一类：
        /// 旧文件里没有身份字段 → 反序列化出来是 <c>Guid.Empty</c> → 这里统一补齐，
        /// 之后按 Id 寻址的代码路径才敢假设"身份总是有效"。
        ///
        /// 图层引入后本方法多做两件事（同一次调用、同一口径）：
        /// ① 图层集合为空（S1 存的画面没有这个字段）→ 补一个默认图层，图层列表才有落脚点；
        /// ② 图元的 <c>LayerId</c> 为 <c>Guid.Empty</c>（未分层）→ 统一归到默认图层。
        ///    这不是猜测：旧画面里的图元本来就平铺在一起，归成一层是唯一不丢东西的解释。
        ///    与之相对，<b>指向已删除图层的悬空 Id 一律不动</b>——那种情形只有用户知道图元该去哪，
        ///    悄悄换个层只会让人以为图元被删了。
        ///
        /// 注意<b>不</b>给绑定补 Id：绑定的 <c>Guid.Empty</c> 是有含义的（"只能按名字找"），
        /// 靠加载期按名解析成功后自愈，不能在这里凭空编一个。
        /// </summary>
        public int EnsureIdentity()
        {
            // 迁移不是用户编辑：旧数据补齐不该进撤销栈，否则"打开一个老工程后按 Ctrl+Z"
            // 会撤销掉一次用户从没做过的操作（典型是 EnsureIdentity 补的默认图层）。
            // 同理不该被写守卫拦——加载路径上本来就没有 BeginEdit 作用域。
            using (ScadaWriteGuard.Suspend())
            using (ScadaChangeScope.SuspendRecording())
            {
                return EnsureIdentityCore();
            }
        }

        private int EnsureIdentityCore()
        {
            int repaired = 0;

            if (_pageId == Guid.Empty)
            {
                PageId = Guid.NewGuid();
                repaired++;
            }

            foreach (var layer in _layers)
            {
                if (layer.LayerId != Guid.Empty)
                    continue;

                layer.LayerId = Guid.NewGuid();
                repaired++;
            }

            if (_layers.Count == 0)
            {
                AddLayer();
                repaired++;
            }

            // 补 Id 会逐个触发 ElementId / LayerId 变更通知；若每次都同步索引就是 O(n²)。
            // 挂起逐次维护，批量结束后整表重建一次（索引只在挂起期"短暂陈旧"，
            // 期间本方法不对外返回，没有并发读的机会）。
            _suspendIndexRebuild = true;
            var defaultLayer = DefaultLayer;
            try
            {
                foreach (var element in _elements)
                {
                    if (element.ElementId == Guid.Empty)
                    {
                        element.ElementId = Guid.NewGuid();
                        repaired++;
                    }

                    if (defaultLayer != null && element.LayerId == Guid.Empty)
                    {
                        element.LayerId = defaultLayer.LayerId;
                        repaired++;
                    }
                }
            }
            finally
            {
                _suspendIndexRebuild = false;
                RebuildIndex();
            }

            return repaired;
        }

        private void OnElementsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            ScadaCollectionRecorder.Record(_elements, e);

            // Reset（Clear() / 整体替换）拿不到"被移除的是谁"（OldItems 为 null），
            // 只能靠登记表全量摘一遍，再按当前集合重挂 + 重建索引。
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var element in _subscribedElements)
                    element.PropertyChanged -= OnElementPropertyChanged;

                _subscribedElements.Clear();

                foreach (var element in _elements)
                    Subscribe(element);

                RebuildIndex();
                Version++;
                return;
            }

            if (e.OldItems != null)
            {
                foreach (ScadaElement element in e.OldItems)
                {
                    Unsubscribe(element);
                    RemoveFromIndex(element);
                }
            }

            if (e.NewItems != null)
            {
                foreach (ScadaElement element in e.NewItems)
                {
                    Subscribe(element);
                    AddToIndex(element);
                }
            }

            Version++;
        }

        private void OnElementPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 身份被改写（旧数据补发 Id、粘贴副本重编 Id）时必须同步索引，
            // 否则 FindElement(新Id) 落空、FindElement(旧Id) 反而命中。
            if (!_suspendIndexRebuild
                && e.PropertyName == nameof(ScadaElement.ElementId)
                && sender is ScadaElement element)
            {
                ReindexElement(element);
            }

            Version++;
        }

        /// <summary>挂属性变更订阅（幂等：登记表已存在则不重复挂）</summary>
        private void Subscribe(ScadaElement element)
        {
            if (_subscribedElements.Add(element))
                element.PropertyChanged += OnElementPropertyChanged;
        }

        /// <summary>摘属性变更订阅（幂等：登记表没有则不动手）</summary>
        private void Unsubscribe(ScadaElement element)
        {
            if (_subscribedElements.Remove(element))
                element.PropertyChanged -= OnElementPropertyChanged;
        }

        /// <summary>整表重建索引（重复身份保留集合中靠前者，与 VariableRegistry 口径一致）</summary>
        private void RebuildIndex()
        {
            _elementIndex.Clear();

            foreach (var element in _elements)
                AddToIndex(element);
        }

        /// <summary>把图元加入索引；身份为空或该身份已被靠前者占用时不写入</summary>
        private void AddToIndex(ScadaElement element)
        {
            if (element.ElementId == Guid.Empty)
                return;

            if (!_elementIndex.ContainsKey(element.ElementId))
                _elementIndex[element.ElementId] = element;
        }

        /// <summary>
        /// 图元身份被改写后的索引修正：先让它的旧键失效，再按新身份重新登记。
        /// 只影响这一个图元，不整表重建。
        /// </summary>
        private void ReindexElement(ScadaElement element)
        {
            foreach (var key in _elementIndex.Where(kv => ReferenceEquals(kv.Value, element)).Select(kv => kv.Key).ToList())
                _elementIndex.Remove(key);

            AddToIndex(element);
        }

        /// <summary>
        /// 图元移出集合后的索引修正。
        /// 不能无脑按键删：重复身份时键指向靠前者，删错就等于"索引指向已移除的图元"。
        /// </summary>
        private void RemoveFromIndex(ScadaElement element)
        {
            if (element.ElementId == Guid.Empty)
                return;

            if (_elementIndex.TryGetValue(element.ElementId, out var current) && !ReferenceEquals(current, element))
                return; // 键被靠前者占着，本次移除的不是索引持有者

            _elementIndex.Remove(element.ElementId);

            // 若移除的正是重复身份里的靠前者，让集合中下一个同身份图元顶上来，
            // 否则 FindElement 会对一个仍然存在的图元返回 null。
            foreach (var candidate in _elements)
            {
                if (candidate.ElementId != element.ElementId)
                    continue;

                _elementIndex[element.ElementId] = candidate;
                break;
            }
        }
    }
}
