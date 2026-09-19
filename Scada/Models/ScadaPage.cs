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
    public class ScadaPage : BindableBase
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

                    var hook = AddEventHook(ScadaEventType.Loaded);
                    hook.Actions.Add(new ScadaAction { Type = ScadaActionType.Log });
                }
                else
                {
                    RemoveEventHook(ScadaEventType.Loaded);
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
        /// </summary>
        [JsonIgnore]
        public int Version
        {
            get => _version;
            set => SetProperty(ref _version, value);
        }

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
        /// 画面级<b>事件钩子</b>（本画面目前只有 <see cref="ScadaEventType.Loaded"/> 一种会真触发）。
        ///
        /// 与 <see cref="ScadaElement.EventHooks"/> 是同一个类型、同一套语义，只是"事件发生在我们自己身上"。
        /// 之所以不叫 <c>LoadedActions</c> 之类的专用名：Unloaded（S8 画面导航）已经排在路线上，
        /// 到那时再加一个集合就是把同一个概念拆成两处。
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
            var hook = new ScadaEventHook { Event = eventType };
            _eventHooks.Add(hook);
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

                _eventHooks.RemoveAt(i);
                return true;
            }

            return false;
        }

        /// <summary>钩子集合增删 → 一次变更（挂/摘订阅的活交给登记表，理由同图元）</summary>
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
            var layer = new ScadaLayer
            {
                Name = string.IsNullOrWhiteSpace(name) ? NextLayerName() : name!.Trim()
            };

            _layers.Add(layer);
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

            layer.Name = target;
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

            _layers.Remove(layer);
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

            _layers.Move(from, targetIndex);
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
                element.LayerId = Guid.Empty;
                return true;
            }

            if (FindLayer(target.LayerId) == null)
            {
                error = "目标图层不属于本画面，无法调整图层";
                return false;
            }

            element.LayerId = target.LayerId;
            return true;
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

        private bool ContainsAnyElement(ScadaLayer layer)
        {
            foreach (var element in _elements)
            {
                if (element.LayerId == layer.LayerId)
                    return true;
            }

            return false;
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

            element.GetOrAddBinding(targetProperty!).Bind(variableId, variableName);
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

            element.RemoveBinding(targetProperty);
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
