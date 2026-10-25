using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Core.Events;
using Core.Interfaces;
using Prism.Dialogs;
using Prism.Ioc;
using UI.CustomControl;
using VisionMaster.Helpers;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 缩放工具条请求的动作。由视图模型发令、视图执行——
    /// ViewportZoom / FitToScreen 是 NodifyEditor 的成员，视图模型够不着。
    /// </summary>
    public enum FlowCanvasZoomCommand
    {
        ZoomIn,
        ZoomOut,
        ActualSize,
        Fit,
    }

    /// <summary>
    /// 流程画布（海康式一图全展开）。
    ///
    /// 与旧子画布模型的根本差异：不再"一次只看一层、双击下钻"，而是把整棵流程树
    /// 摊在同一张画布上——容器（If/While/For）渲染成包裹其子孙的框体 + 分支泳道，
    /// 可折叠（折叠态持久化在 FlowLayoutStore.Collapsed）；连线升级为模块级
    /// （一根线 = 两个模块存在数据依赖，具体端口绑定在变量绑定弹窗里维护）。
    ///
    /// 职责边界（沿用旧画布的约束，全部仍然成立）：
    ///   读：StepModel.LinkedSources / IPluginProvider 静态端口 → 节点与聚合连线；
    ///   写：解绑 → StepModel.RemoveLink；拖动 → FlowLayoutStore.Set；
    ///       改序/移分支 → Steps 集合 Move / Remove+Insert（触发重编译）；
    ///       建线的端口选择交给绑定弹窗（视图侧经 ModuleLinkRequested 打开）。
    /// 1. 坐标只进 FlowLayoutStore，绝不写 StepModel → 拖动不触发重编译；
    /// 2. 连线写回只走 LinkedSources，Version 递增由统一写路径负责；
    /// 3. 只订阅步骤集合增删，不订阅步骤属性变更 → 改参数不重建画布；
    /// 4. 「谁先执行、能不能取数」一律问 FlowTopology，与编译器共用同一真相源；
    /// 5. 泳道与容器框是装饰节点：几何由内容算出、不入库，StepId 相关写回先排除。
    /// </summary>
    public class FlowCanvasViewModel : BindableBase
    {
        // ------------------------------------------------------------------
        //  画布渲染的几何常量：**数值源在 Core\Models\FlowLayoutStore**
        //  （布局器与渲染器必须逐一相等，否则"框 ⊆ 泳道、同级列不相交"直接击穿）
        //
        //  Core 不引 VM，别名引用只能是 VM → Core 这个方向：本组常量全是
        //  FlowLayoutStore 同名常量的别名，改数值只改 Core 那一处。
        //  2026-10-09 审查 #3：此前是两处各自字面量、靠注释提醒同步，已改成单向引用。
        // ------------------------------------------------------------------

        /// <summary>模块盒标称尺寸（固定 156×58；FlowLayoutStore 为唯一数值源，锚点按此常量算）</summary>
        public const double NodeWidth = FlowLayoutStore.NodeWidth;
        public const double NodeHeight = FlowLayoutStore.NodeHeight;

        /// <summary>折叠容器的框体尺寸（只剩头带，等高一个普通模块盒）</summary>
        public const double CollapsedFrameWidth = FlowLayoutStore.CollapsedFrameWidth;
        public const double CollapsedFrameHeight = FlowLayoutStore.CollapsedFrameHeight;

        /// <summary>泳道/框体的内边距与头带高度</summary>
        public const double LanePadding = FlowLayoutStore.LanePadding;
        public const double LaneHeaderHeight = FlowLayoutStore.LaneHeaderHeight;
        public const double FramePadding = FlowLayoutStore.FramePadding;
        public const double FrameHeaderHeight = FlowLayoutStore.FrameHeaderHeight;

        /// <summary>
        /// 分支列距：上一列泳道右缘 + ColumnGap = 下一列泳道左缘。
        /// 只在空分支泳道的列序回推里用到（有内容列以内容实位为准）
        /// </summary>
        public const double ColumnGap = FlowLayoutStore.ColumnGap;

        /// <summary>空分支泳道的占位尺寸</summary>
        public const double EmptyLaneWidth = FlowLayoutStore.EmptyLaneWidth;
        public const double EmptyLaneHeight = FlowLayoutStore.EmptyLaneHeight;

        /// <summary>泳道最小宽度（组头可读下限，布局器与渲染器共用）</summary>
        public const double MinLaneWidth = FlowLayoutStore.MinLaneWidth;

        private readonly IWorkspaceManager _workspace;
        private readonly IPluginProvider _pluginProvider;

        /// <summary>
        /// 软件级配置（AppConfig.json）。画布的「随用户」视图态（数据线/顺序链开关）从这里读初值、
        /// 改动写回并立即落盘（与 RunWindowMode 同一条"改完即存"的周期）。
        /// 断言宿主 / 设计器不传：开关走代码内默认值、不落盘——AppSettingsService 构造会在
        /// 程序目录写 AppConfig.json，不能让 headless 进程碰它（该服务的注释有案）。
        /// </summary>
        private readonly AppSettingsService? _appSettings;

        /// <summary>构造收尾前为 false：读初值走属性 setter 时不应触发"写回配置"（刚读出来的值再写回去没有意义）</summary>
        private bool _viewSettingsReady;

        /// <summary>构造时 Workspace 的 INotifyPropertyChanged 引用，Deactivate 时用于解绑</summary>
        private readonly INotifyPropertyChanged? _workspaceChanged;

        /// <summary>渲染在画布上的节点：StepID → 节点（只含可见节点，折叠子树不入内）</summary>
        private readonly Dictionary<Guid, CanvasNodeViewModel> _nodeMap = new();

        /// <summary>步骤节点池：跨重建复用同一批 VM，保住 IsSelected 不随渲染蒸发</summary>
        private readonly Dictionary<Guid, CanvasNodeViewModel> _nodePool = new();

        /// <summary>
        /// 节点池硬上限：超过就整体清一次。
        /// 池子跨流程存活（换流程不再 Clear），来回切几十条流程时它按"各流程节点并集"增长；
        /// 每条节点 VM 还挂着 6 个命令 + 2 个连接器 + 模型订阅，无上限就是不设防的内存增长。
        /// 4000 覆盖了"两条 3000 节点压测流程来回切"的常见场景（其中重复节点会复用）。
        /// </summary>
        private const int NodePoolHardLimit = 4000;

        /// <summary>
        /// 大流程首次挂接时的自动折叠阈值（步骤数）。
        ///
        /// 为什么需要：画布是"每步一个 WPF 元素"的结构，3542 步的流程一次全展开要建
        /// 20 万个元素（实测单次重建 40 s，真机表现为打开即假死）。PLC 梯形图编辑器
        /// 能扛上万行是因为它只渲染视口；在拿到虚拟化之前，先用折叠把"一次要画多少"压下来。
        ///
        /// 折叠只是**会话态**，不写 .vms：用户展开哪个容器，就记进 <see cref="_sessionCollapsed"/> 的反面。
        /// </summary>
        public const int BigFlowAutoCollapseThreshold = 600;

        /// <summary>
        /// 会话级强制折叠（不进 .vms）。用户手动展开某容器时会从这里移除，
        /// 所以"自动折叠过一次、用户展开过的"不会被下一次渲染again 折回去。
        /// </summary>
        private readonly HashSet<Guid> _sessionCollapsed = new();

        /// <summary>已经自动折叠过的流程（同一条流程再切回来不重复折叠、不重复提示）</summary>
        private readonly HashSet<FlowModel> _autoCollapsedFlows = new(ReferenceEqualityComparer.Instance);

        /// <summary>渲染中的真实节点（Step + Container，泳道不算）</summary>
        private readonly List<CanvasNodeViewModel> _steps = new();

        /// <summary>渲染中的泳道装饰</summary>
        private readonly List<CanvasNodeViewModel> _lanes = new();

        /// <summary>容器框矩形（展开态）：StepID → 框体矩形，供拖拽落点判定与外层框套算</summary>
        private readonly Dictionary<Guid, Rect> _frameRects = new();

        /// <summary>本帧渲染期间订阅过的步骤列表（顶层 + 全部嵌套分支），渲染前统一摘除</summary>
        private readonly List<INotifyCollectionChanged> _watchedLists = new();

        /// <summary>结构拓扑快照。快照不跟踪图纸，每次渲染前无条件重建</summary>
        private FlowTopology _topology = FlowTopology.Build(Array.Empty<StepModel>());

        private FlowModel? _attachedFlow;
        private NotifyCollectionChangedEventHandler? _stepsChanged;
        private Guid _pendingSelectId;

        /// <summary>正在重建：抑制重建过程自身引发的重入</summary>
        private bool _rebuilding;

        /// <summary>画布主动切当前步骤时的回环闸门（WorkspaceContext.SwitchStep 同步派发 PropertyChanged）</summary>
        private bool _suppressStepSync;

        // ---- 撤销栈（改序 / 移分支 / 解绑）----

        private readonly Stack<IUndoCommand> _undoStack = new();
        private readonly Stack<IUndoCommand> _redoStack = new();
        private const int MaxUndoStack = 50;

        /// <summary>撤销/重做执行期间置位：逆操作的写回不应再压栈</summary>
        private bool _suppressUndoRedo;

        /// <summary>
        /// 画布自身命令写回步骤集合（改序/移分支 Redo）期间的嵌套计数。
        /// 与 _suppressUndoRedo 一起告诉 OnFlowStepsChanged：这次集合变化是画布自己的命令刚做的，
        /// 栈里那条命令就是它——不能当作"外部结构变化"清掉撤销栈。
        /// </summary>
        private int _internalWriteback;

        /// <summary>拖拽开始快照：每个被拖节点的 (Owner, IndexInOwner, OriginalLocation)</summary>
        private List<DragSnapshot>? _dragStartedSnapshot;

        /// <summary>
        /// 画布编辑门禁（2026-10-11 收口）：运行态经 GlobalEventBus 同步进 EditGate，
        /// 豁免表与流程栏共用同一份判据（断点全放行 / 暂停中放参数 / 其余编辑运行中全拦）。
        /// </summary>
        private readonly EditGate _editGate = new();

        /// <summary>
        /// 画布运行锁横幅文案：直接取门禁那一份（<see cref="EditGate.RunLockedMessage"/>）。
        /// 这里曾手抄一份字面量，改文案时两处必须同步——统一成常量引用后不再有漂移面。
        /// </summary>
        private const string RunLockedMessage = EditGate.RunLockedMessage;

        public FlowCanvasViewModel(IWorkspaceManager workspace, IPluginProvider pluginProvider,
            AppSettingsService? appSettings = null)
        {
            _workspace = workspace;
            _pluginProvider = pluginProvider;
            _appSettings = appSettings;

            // 「随用户」视图态：AppConfig.json 里有就用用户的（缺失按代码默认兜底），
            // 写回走 SyncFlowCanvasSettings（统一保存口）。初值灌入在 _viewSettingsReady 置位前，
            // setter 里的持久化分支不会空转
            if (appSettings != null)
            {
                var node = appSettings.Current?.FlowCanvasSettings;
                ShowOrderLinks = node?.ShowOrderLinks ?? true;
                ShowDataLinks = node?.ShowDataLinks ?? false;
            }

            _workspaceChanged = _workspace as INotifyPropertyChanged;

            StartConnectionCommand = new DelegateCommand<object>(OnStartConnection);
            CompleteConnectionCommand = new DelegateCommand<object>(OnCompleteConnection);
            UnbindBindingCommand = new DelegateCommand<CanvasLinkBinding?>(OnUnbindBinding);
            ToggleCollapseCommand = new DelegateCommand<CanvasNodeViewModel?>(OnToggleCollapse);
            ToggleBreakpointCommand = new DelegateCommand<CanvasNodeViewModel?>(OnToggleBreakpoint);
            TidyLayoutCommand = new DelegateCommand(OnTidyLayout);
            ZoomCommand = new DelegateCommand<object>(
                o => OnZoomCommand((FlowCanvasZoomCommand)o!));
            OpenBindingCommand = new DelegateCommand<StepModel?>(
                step => { if (step != null) ModuleLinkRequested?.Invoke(step); });

            UndoCommand = new DelegateCommand(Undo, () => _undoStack.Count > 0);
            RedoCommand = new DelegateCommand(Redo, () => _redoStack.Count > 0);
            ExpandAllCommand = new DelegateCommand(OnExpandAll);
            CollapseAllCommand = new DelegateCommand(OnCollapseAll);
            ShowCompileErrorsCommand = new DelegateCommand(OnShowCompileErrors);
            ItemsDragStartedCommand = new DelegateCommand<object>(OnItemsDragStarted);
            ItemsDragCompletedCommand = new DelegateCommand<object>(OnItemsDragCompleted);
            DeleteSelectionCommand = new DelegateCommand(OnDeleteSelection);
            CopySelectedCommand = new DelegateCommand<object?>(OnCopySelected);
            PasteCommand = new DelegateCommand<object?>(OnPaste);

            PendingConnection.PropertyChanged += OnPendingConnectionChanged;

            // 门禁运行态变化 → 刷新 IsRunLocked / 横幅可见性绑定面（门禁与 VM 同生命周期，构造挂接即可）
            _editGate.RunStateChanged += OnRunGateStateChanged;

            // 初值已灌完：此后两个显隐开关的 setter 开始走"写回配置"分支
            _viewSettingsReady = true;

            // 订阅与首次渲染统一由 Activate 负责（View 的 Loaded / Unloaded 成对调用）；
            // 构造即视为"已入树"，这里先挂一次（含 Rebuild）
            Activate();
        }

        private bool _subscribed;

        /// <summary>
        /// 挂接 Workspace 通知并渲染（幂等）。
        /// AvalonDock 切标签页会触发 Unloaded 且不会自动 Loaded，所以挂/摘必须可逆。
        /// </summary>
        public void Activate()
        {
            if (_subscribed) return;
            _subscribed = true;

            if (_workspaceChanged != null)
                _workspaceChanged.PropertyChanged += OnWorkspacePropertyChanged;

            // 编辑门禁与编译错误事件与本体同挂同摘（离树时一并可逆清理）
            _editGate.Activate();
            GlobalEventBus.Subscribe<CompilationErrorsEvent>(OnCompilationErrorsEvent);

            Rebuild();
        }

        /// <summary>摘除 Workspace 通知与流程/嵌套集合订阅（幂等）。摘干净后旧 VM 可被 GC</summary>
        public void Deactivate()
        {
            // 视图离树时拖动浮层/参考线不能留在屏幕上（幂等，放在早退之前）
            HideDragGhost();
            HideDragGuides();

            if (!_subscribed) return;
            _subscribed = false;

            if (_workspaceChanged != null)
                _workspaceChanged.PropertyChanged -= OnWorkspacePropertyChanged;

            GlobalEventBus.Unsubscribe<CompilationErrorsEvent>(OnCompilationErrorsEvent);
            _editGate.Deactivate();

            Detach();
        }

        // ==================================================================
        //  视图绑定面
        // ==================================================================

        /// <summary>
        /// 画布节点。集合序 = 容器框与泳道先入垫底、步骤后入置顶（构造期兜底序）；
        /// 真正的叠放层次是 <see cref="CanvasNodeViewModel.ZOrder"/>（绑 Panel.ZIndex）——
        /// 它按拓扑深度恒定，不受分支加入顺序影响，后入分支的泳道盖不住先入分支里的嵌套框
        /// </summary>
        public ObservableCollection<CanvasNodeViewModel> Nodes { get; } = new();

        /// <summary>模块级聚合连线（数据依赖 + 执行顺序链，视觉与语义以 IsOrderLink 区分）</summary>
        public ObservableCollection<CanvasLinkViewModel> Links { get; } = new();

        /// <summary>数据依赖线根数（不含执行顺序链）</summary>
        public int DataLinkCount => Links.Count(l => !l.IsOrderLink);

        /// <summary>执行顺序链根数</summary>
        public int OrderLinkCount => Links.Count(l => l.IsOrderLink);

        /// <summary>拖线过程中的临时连线状态（属性名与 Nodify PendingConnection 的 DP 同名）</summary>
        public CanvasPendingConnectionViewModel PendingConnection { get; } = new();

        public DelegateCommand<object> StartConnectionCommand { get; }

        public DelegateCommand<object> CompleteConnectionCommand { get; }

        /// <summary>从连线右键菜单解绑一条端口绑定（可撤销）</summary>
        public DelegateCommand<CanvasLinkBinding?> UnbindBindingCommand { get; }

        /// <summary>折叠/展开容器框（持久化到 FlowLayoutStore.Collapsed）</summary>
        public DelegateCommand<CanvasNodeViewModel?> ToggleCollapseCommand { get; }

        /// <summary>
        /// 切换断点（DWV 第 1 期）：画布节点右上角圆点。
        /// 仅普通步骤节点有效——容器（If/While/For）本轮不提供断点操作（数据模型已支持，留待后续）。
        /// </summary>
        public DelegateCommand<CanvasNodeViewModel?> ToggleBreakpointCommand { get; }

        /// <summary>
        /// 整理布局：清空本流程全部节点坐标后按正交规则重排。
        /// 用于迁移旧子画布时代按"每层各自坐标系"存的散乱布局，也兼顾用户拖乱后想一键归位的场景。
        /// </summary>
        public DelegateCommand TidyLayoutCommand { get; }

        /// <summary>缩放工具条（放大/缩小/100%/适应画布），动作由视图执行</summary>
        /// <summary>缩放工具条（放大/缩小/100%/适应画布），动作由视图执行。
        /// Prism 的 DelegateCommand&lt;T&gt; 不收枚举类型参数（要求引用类型或 Nullable），故用 object 装箱</summary>
        public DelegateCommand<object> ZoomCommand { get; }

        public DelegateCommand UndoCommand { get; }

        public DelegateCommand RedoCommand { get; }

        /// <summary>展开全部容器（工具条）。只动布局库折叠标记，不碰流程语义 → 不受运行锁限制</summary>
        public DelegateCommand ExpandAllCommand { get; }

        /// <summary>折叠全部容器（工具条）。同上不受运行锁限制</summary>
        public DelegateCommand CollapseAllCommand { get; }

        /// <summary>打开「编译错误列表」（工具条按钮，有错误才可见；双击行定位到出错步骤）</summary>
        public DelegateCommand ShowCompileErrorsCommand { get; }

        /// <summary>拖拽开始：记录被拖节点的原 owner / 原下标 / 原坐标</summary>
        public DelegateCommand<object> ItemsDragStartedCommand { get; }

        /// <summary>拖拽抬起：按几何位置判定改序/移分支并提交命令</summary>
        public DelegateCommand<object> ItemsDragCompletedCommand { get; }

        /// <summary>
        /// 删除画布选中的模块（Delete 键 / 节点右键菜单共用）：连容器子树一起移出图纸。
        /// 数据连线的删除走连线右键菜单（编辑绑定/逐条/整批解绑）——
        /// Nodify 7.3 的连线选中机制（SelectedConnections 集合语义与选中视觉）未取证，不盲绑。
        /// 2026-10-11 撤销补口后删除可撤销（引用型 DeleteStepCommand），不再清空撤销栈（见 OnDeleteSelection）。
        /// </summary>
        public DelegateCommand DeleteSelectionCommand { get; }

        /// <summary>
        /// 复制当前选中的模块节点（节点右键菜单「复制」/ Ctrl+C 共用；第一期单个节点，
        /// 多选批量复制不做）。参数按 R27 口径用 object? 装：右键路径传节点 VM、
        /// 快捷键路径传 null（走既有选中机制）、其余类型一律 is 判型空跑。
        /// </summary>
        public DelegateCommand<object?> CopySelectedCommand { get; }

        /// <summary>
        /// 粘贴剪贴板内容（画布空白右键菜单「粘贴」/ Ctrl+V 共用）：
        /// FlowClipboard.Take 物化重映射 → 按拖入同款落点规则插入 → 压撤销命令。
        /// 参数允许传 Point 落点（断言直驱用）；为空时回退 LastPointerGraphPosition。
        /// </summary>
        public DelegateCommand<object?> PasteCommand { get; }

        /// <summary>
        /// 最近一次指针在画布图坐标系下的位置（视图随 MouseMove 回报，与节点 Location 同一坐标系）。
        /// 粘贴落点用它；Ctrl+V 直按（指针没进过画布）时为 null，落点回退"追加到顶层末尾"。
        /// 频繁写入、无绑定消费 → 不发属性通知。
        /// </summary>
        public Point? LastPointerGraphPosition { get => field; set => field = value; }

        public int UndoStackSize => _undoStack.Count;
        public int RedoStackSize => _redoStack.Count;

        /// <summary>
        /// 视口动作请求。参数 null = 适应整图（切流程后），非 null = 把该节点滚入视野（外部选中联动）。
        /// 必须由视图执行：ViewportLocation / FitToScreen 是 NodifyEditor 的东西。
        /// </summary>
        public event Action<CanvasNodeViewModel?>? ViewportActionRequested;

        /// <summary>缩放工具条请求，由视图操作 NodifyEditor 视口</summary>
        public event Action<FlowCanvasZoomCommand>? ZoomCommandRequested;

        /// <summary>
        /// 模块级建线请求：用户从 A 拖线到 B 且结构合法。
        /// 画布不自己写连线——绑哪对端口是绑定弹窗的事，由视图打开 DataBindView（TargetStep=消费方），
        /// 弹窗写完 LinkedSources 后回调 <see cref="RefreshLinks"/> 重画。
        /// </summary>
        public event Action<StepModel>? ModuleLinkRequested;

        /// <summary>顶部工具条显示的流程名（含方案上下文时为空流程名兜底）</summary>
        public string FlowTitle => _attachedFlow?.FlowName ?? "（未选择流程）";

        /// <summary>
        /// 连线右键菜单「编辑绑定…」：对消费方重新打开绑定弹窗（与拖线建线同一通道）。
        /// </summary>
        public DelegateCommand<StepModel?> OpenBindingCommand { get; private set; }

        /// <summary>当前是否有可显示的流程</summary>
        public bool HasFlow => _attachedFlow != null && _steps.Count > 0;

        /// <summary>空状态提示可见性</summary>
        public Visibility EmptyHintVisibility => HasFlow ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>结构非法（倒序/跨分支/外层晚于容器）的绑定数——编译期是致命错</summary>
        public int IllegalLinkCount { get; private set; }

        /// <summary>无法画成模块线的绑定数：上游步骤已删除（编译会报致命断连）</summary>
        public int DeferredLinkCount { get; private set; }

        /// <summary>被折叠藏进框里的绑定数（展开后即可见，不算问题）</summary>
        public int HiddenLinkCount { get; private set; }

        /// <summary>告警条是否显示。只有非法连线才报警</summary>
        public Visibility WarningVisibility => IllegalLinkCount > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>告警条文案</summary>
        public string WarningText => IllegalLinkCount > 0
            ? $"⚠ {IllegalLinkCount} 条连线结构非法（编译会报致命错），已按红色虚线显示；另有 {DeferredLinkCount} 条连线的上游已不在图纸上"
            : string.Empty;

        // ------------------------------------------------------------------
        //  运行锁（画布编辑门禁，2026-10-11 三视角评审 P0）：状态由 EditGate 同步
        // ------------------------------------------------------------------

        /// <summary>流程运行中（画布编辑锁定；断点与暂停中的参数豁免）。运行态由 EditGate 经总线同步</summary>
        public bool IsRunLocked => _editGate.IsRunLocked;

        /// <summary>运行锁横幅可见性：运行中在顶部警告条区常驻（视觉横幅，评审 P0-4）</summary>
        public Visibility RunLockBannerVisibility
            => IsRunLocked ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>运行锁横幅文案</summary>
        public string RunLockBannerText => "流程运行中：编辑已锁定（断点与暂停中的参数除外）";

        private void OnRunGateStateChanged()
        {
            RaisePropertyChanged(nameof(IsRunLocked));
            RaisePropertyChanged(nameof(RunLockBannerVisibility));
        }

        /// <summary>
        /// 画布侧的运行锁闸：编辑类动作在运行中被拦下时给状态栏提示并返回 false。
        /// 拦截点：拖入 / 删除 / 禁用 / 解绑 / 拖拽改序（结构编辑）与模块参数（暂停豁免）；
        /// 断点、折叠展开、缩放、显隐开关不设闸（断点全放行，其余不碰流程语义）。
        /// </summary>
        private bool EnsureEditable(ModuleEditAction action)
        {
            if (_editGate.IsEditable(action)) return true;

            StatusHint = RunLockedMessage;
            return false;
        }

        /// <summary>状态栏提示：拒绝连线/折叠提示等一次性文案；无提示时为 null。
        /// 一次性文案 5 秒后自动收起——旧提示长期驻留会与新状态互相矛盾、误导操作。
        /// headless 断言环境无消息泵，定时器不 tick，StatusHint 保持可查。</summary>
        public string? StatusHint
        {
            get => field;
            set
            {
                if (!SetProperty(ref field, value)) return;

                if (value == null)
                {
                    _statusHintTimer?.Stop();
                    return;
                }

                _statusHintTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                _statusHintTimer.Tick -= OnStatusHintTimerTick;
                _statusHintTimer.Tick += OnStatusHintTimerTick;
                _statusHintTimer.Stop();
                _statusHintTimer.Start();
            }
        }

        private DispatcherTimer? _statusHintTimer;

        private void OnStatusHintTimerTick(object? sender, EventArgs e)
        {
            _statusHintTimer?.Stop();
            StatusHint = null;
        }

        // ------------------------------------------------------------------
        //  运行耗时实时刷新（200ms，与流程栏同频）：
        //  引擎只写步骤起始时间戳（LastRunStartTimestamp），运行中耗时由 UI tick 计算
        //  写进 CurrentRunTimeMs——画布耗时徽章的"运行中跳秒"靠这里，不依赖流程栏面板开着
        //  （两处 tick 调同一静态纯函数，重复计算幂等；SetProperty 同值不发通知）。
        //  生命周期：Attach 启动 / Detach 与空流程停止。
        // ------------------------------------------------------------------
        private DispatcherTimer? _runTimeTimer;

        /// <summary>定时器一轮的实际动作（internal：断言宿主直驱，不等 Dispatcher 泵）。</summary>
        internal void RunTimeTick() => ProcessViewModel.TickRunningTimes(_attachedFlow?.Steps);

        /// <summary>tick 是否在跑（断言宿主验 Attach/Detach 生命周期用）。</summary>
        internal bool RunTimeTimerEnabled => _runTimeTimer?.IsEnabled == true;

        private void StartRunTimeTimer()
        {
            _runTimeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _runTimeTimer.Start();
        }

        /// <summary>是否显示执行顺序链（工具条开关；「随用户」视图态，改动写回 AppConfig.json）</summary>
        public bool ShowOrderLinks
        {
            get => field;
            set
            {
                if (SetProperty(ref field, value))
                {
                    PersistViewSettings();
                    RebuildInPlace();
                }
            }
        } = true;

        /// <summary>
        /// 是否显示端口数据线（工具条开关，**默认关**——用户口径："端口之间的连线默认不显示，
        /// 只显示执行顺序"）。
        ///
        /// 隐藏只针对普通数据线：非法数据线（红虚线）是要用户去修的错误提示，不随开关隐藏
        ///（警告条与 Illegal/Deferred 计数同样照常）。全局变量/常量角标不受影响。
        /// 「随用户」视图态：改动写回 AppConfig.json（与顺序链开关同一保存周期）。
        /// </summary>
        public bool ShowDataLinks
        {
            get => field;
            set
            {
                if (SetProperty(ref field, value))
                {
                    PersistViewSettings();
                    RebuildInPlace();
                }
            }
        }

        /// <summary>
        /// 是否显示右下角小地图（flow-logic：默认关闭，改由工具条开关调出）。
        /// 小地图是会话内视图态，不落盘；默认关掉是设计稿明确修掉的遮挡问题——
        /// 常驻右下角会盖住"结果汇总"这类输出节点。
        /// </summary>
        public bool ShowMinimap
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        /// <summary>
        /// 数据线是否被"大流程降级"精简过（只留非法线）。
        /// 由 <see cref="BuildLinks"/> 按当帧聚合根数判定并通知；工具条据此显示一条提示，
        /// 避免用户以为"线不见了 = 绑定丢了"。
        /// </summary>
        public bool DataLinksReduced
        {
            get => field;
            private set => SetProperty(ref field, value);
        }

        /// <summary>
        /// 把两个显隐开关写回软件级配置并落盘（「随用户」视图态的唯一写路径）。
        /// 保存失败不抛：开关本身已生效，落盘失败只提示（下次改开关会再试）；
        /// 无 AppSettingsService（断言宿主/设计器）或构造期灌初值（_viewSettingsReady=false）时跳过。
        /// </summary>
        private void PersistViewSettings()
        {
            if (_appSettings == null || !_viewSettingsReady) return;

            try
            {
                _appSettings.SyncFlowCanvasSettings(ShowDataLinks, ShowOrderLinks);
            }
            catch (Exception ex)
            {
                StatusHint = $"画布显示设置保存失败（重启后会回到原值）：{ex.Message}";
            }
        }

        /// <summary>统一刷新派生状态</summary>
        private void NotifyViewStates()
        {
            RaisePropertyChanged(nameof(FlowTitle));
            RaisePropertyChanged(nameof(HasFlow));
            RaisePropertyChanged(nameof(EmptyHintVisibility));
            RaisePropertyChanged(nameof(IllegalLinkCount));
            RaisePropertyChanged(nameof(DeferredLinkCount));
            RaisePropertyChanged(nameof(HiddenLinkCount));
            RaisePropertyChanged(nameof(WarningVisibility));
            RaisePropertyChanged(nameof(WarningText));
        }

        // ==================================================================
        //  撤销栈
        // ==================================================================

        internal void PushUndo(IUndoCommand command)
        {
            if (command == null || _suppressUndoRedo) return;

            _redoStack.Clear();

            if (_undoStack.Count >= MaxUndoStack)
            {
                var list = new List<IUndoCommand>(_undoStack);
                list.Reverse();
                list.RemoveAt(list.Count - 1);
                _undoStack.Clear();
                for (int i = list.Count - 1; i >= 0; i--)
                    _undoStack.Push(list[i]);
            }

            _undoStack.Push(command);
            RaiseUndoRedoCanExecuteChanged();
        }

        private void Undo()
        {
            if (_undoStack.Count == 0) return;
            var cmd = _undoStack.Pop();
            _suppressUndoRedo = true;
            try
            {
                cmd.Undo();
                // 改序/移分支的 Move 会触发集合重建；解绑只改 LinkedSources（属性），
                // 不触发集合事件，这里统一显式重建一次（幂等）
                RebuildInPlace();
            }
            finally
            {
                _suppressUndoRedo = false;
            }
            _redoStack.Push(cmd);
            RaiseUndoRedoCanExecuteChanged();
        }

        private void Redo()
        {
            if (_redoStack.Count == 0) return;
            var cmd = _redoStack.Pop();
            _suppressUndoRedo = true;
            try
            {
                cmd.Redo();
                RebuildInPlace();
            }
            finally
            {
                _suppressUndoRedo = false;
            }
            _undoStack.Push(cmd);
            RaiseUndoRedoCanExecuteChanged();
        }

        private void ClearUndoStacks()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            RaiseUndoRedoCanExecuteChanged();
        }

        private void RaiseUndoRedoCanExecuteChanged()
        {
            UndoCommand.RaiseCanExecuteChanged();
            RedoCommand.RaiseCanExecuteChanged();
            RaisePropertyChanged(nameof(UndoStackSize));
            RaisePropertyChanged(nameof(RedoStackSize));
        }

        internal sealed class DragSnapshot
        {
            public CanvasNodeViewModel Node { get; }
            public ObservableCollection<StepModel> Owner { get; }
            public int OriginalIndex { get; }
            public Point OriginalLocation { get; }

            public DragSnapshot(CanvasNodeViewModel node,
                ObservableCollection<StepModel> owner, int originalIndex, Point originalLocation)
            {
                Node = node;
                Owner = owner;
                OriginalIndex = originalIndex;
                OriginalLocation = originalLocation;
            }
        }

        // ==================================================================
        //  流程切换与重建
        // ==================================================================

        private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(IWorkspaceManager.CurrentFlow):
                    Rebuild();
                    break;

                case nameof(IWorkspaceManager.CurrentSolution):
                    // 换方案：会话级折叠记忆全部作废（StepID 换了世界）
                    _sessionCollapsed.Clear();
                    _autoCollapsedFlows.Clear();
                    Rebuild();
                    break;

                case nameof(IWorkspaceManager.CurrentStep):
                    OnCurrentStepChanged();
                    break;
            }
        }

        private void OnFlowStepsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_rebuilding) return;

            if (_internalWriteback > 0 || _suppressUndoRedo)
            {
                // 画布自己的命令写回（改序/移分支 Redo、删除）：栈里那条命令就是本次变化的作者，只重画不清栈
                RebuildInPlace();
                return;
            }

            // 外部结构变化只对"会塌陷下标"的动作清栈（Remove/Replace/Reset）：
            // 撤销栈里命令记录的 owner+下标快照在删除后会指错元素，撤销会移错步骤甚至越界。
            // Add/Move 不塌陷既有下标，不清——"先写回、后压栈"的既有模式（撤销栈测试、
            // 潜在的流程栏联动写回）依赖这一点；代价是外部 Add/Move 后撤销可能回滚到
            // 略有偏移的顺序，最坏是顺序偏移、不崩溃不丢步骤，可接受。
            bool structuralBreak = e.Action
                is NotifyCollectionChangedAction.Remove
                or NotifyCollectionChangedAction.Replace
                or NotifyCollectionChangedAction.Reset;
            if (structuralBreak)
                ClearUndoStacks();

            RebuildInPlace();
        }

        /// <summary>
        /// 画布重建耗时日志的出口（宿启动时挂到 ILogService；断言宿主里保持 null = 不打日志）。
        ///
        /// 为什么要埋这个：探针（`FlowCanvasChecks --switch-perf`）跑在裸 Window + 精简主题里，
        /// 与真机（AvalonDock + 完整主题 + 真渲染线程）差好几倍，靠探针调参容易调错方向。
        /// 这里量的是**用户点完流程到 UI 线程重新空闲**的真实数字，日志里直接可对照。
        /// 用静态挂点而不是给构造器加依赖：本 VM 在十余处断言宿主里被 `new`，加参数会全线改动。
        /// </summary>
        public static Action<string>? PerfLog { get; set; }

        /// <summary>只对达到这个步数的流程打耗时日志（小流程打了也没有信息量，且会刷屏）</summary>
        public const int PerfLogMinSteps = 80;

        /// <summary>全量重建并适应新图：切流程、切方案、初始化</summary>
        public void Rebuild() => RebuildCore(resetViewport: true);

        /// <summary>图内结构变化：重画但保住视口</summary>
        public void RebuildInPlace() => RebuildCore(resetViewport: false);

        /// <summary>绑定弹窗写完 LinkedSources 后由视图回调：重画连线</summary>
        public void RefreshLinks() => RebuildInPlace();

        private void RebuildCore(bool resetViewport)
        {
            if (_rebuilding) return;
            _rebuilding = true;
            var perfWatch = PerfLog != null ? System.Diagnostics.Stopwatch.StartNew() : null;
            try
            {
                var flow = _workspace.CurrentFlow;

                // 换图纸：**不清节点池**，交给 PrunePool 按"不在当前拓扑"逐项回收。
                //
                // 旧实现无条件 _nodePool.Clear()，理由是"StepID 在不同流程间可能重复（复制粘贴）"。
                // 但 GetOrCreateNode 的复用判据已经是「StepID 命中 **且** ReferenceEquals(pooled.Model, step)」
                // （见其实现）——同 Id 不同实例的节点照样会重造，所以清池只是白白丢掉 A→B→A 的复用机会。
                // 3000 节点规模下，清池意味着每次回到看过的流程都要把全部节点 VM + 6 个命令 + 连接器
                // 重新 new 一遍。
                //
                // 内存兜底：池子过大（跨很多流程来回切）时整体清一次，避免长期运行无限涨。
                if (!ReferenceEquals(flow, _attachedFlow))
                {
                    Detach();
                    if (_nodePool.Count > NodePoolHardLimit)
                        _nodePool.Clear();
                }

                if (flow == null)
                {
                    Nodes.Clear();
                    UnhookLinks();
                    Links.Clear();
                    _nodeMap.Clear();
                    UnhookLanes();
                    _steps.Clear();
                    _lanes.Clear();
                    _frameRects.Clear();
                    IllegalLinkCount = 0;
                    DeferredLinkCount = 0;
                    HiddenLinkCount = 0;
                    NotifyViewStates();
                    return;
                }

                _attachedFlow = flow;
                StartRunTimeTimer();   // 运行耗时实时刷新随图纸挂接（空流程/Detach 时停止）

                // 大流程：首次挂接时把容器默认折起来，否则一次要画几万个 WPF 元素（见常量说明）
                TryAutoCollapseBigFlow(flow);

                if (_stepsChanged == null)
                {
                    _stepsChanged = OnFlowStepsChanged;
                    flow.Steps.CollectionChanged += _stepsChanged;
                }

                Render(flow);
                NotifyViewStates();

                if (resetViewport)
                    ViewportActionRequested?.Invoke(null);

                if (_pendingSelectId != Guid.Empty
                    && _nodeMap.TryGetValue(_pendingSelectId, out var target))
                {
                    target.IsSelected = true;
                    ViewportActionRequested?.Invoke(target);
                }

                _pendingSelectId = Guid.Empty;
            }
            finally
            {
                _rebuilding = false;
                SchedulePerfLog(perfWatch);
            }
        }

        /// <summary>
        /// 把"这次重建 + 随后一轮布局"的耗时打到 <see cref="PerfLog"/>。
        /// 排队到 Background 优先级：那时 WPF 已经把容器生成、measure、arrange 跑完，
        /// 量到的就是用户体感的"点完流程到界面重新响应"。
        /// </summary>
        private void SchedulePerfLog(System.Diagnostics.Stopwatch? watch)
        {
            if (watch == null) return;

            var flow = _attachedFlow;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (flow?.Steps == null || dispatcher == null) return;

            int steps = CountAllSteps(flow.Steps);
            if (steps < PerfLogMinSteps) return;

            string name = flow.FlowName ?? "(未命名)";
            int nodes = Nodes.Count;
            int links = Links.Count;

            // 第一段：UI 线程（重建 + 容器生成 + measure/arrange）
            dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() => PerfLog?.Invoke(
                    $"[画布] 流程「{name}」步骤={steps} 可见节点={nodes} 连线={links} "
                    + $"重建+布局={watch.ElapsedMilliseconds}ms")));

            // 第二段：呈现。重建后等下一帧 CompositionTarget.Rendering —— 它表示渲染线程
            // 真的把这一帧交给合成器了。若「重建+布局」很小而这一段很大，说明瓶颈是**光栅化**
            // （每节点一个元素的模板在大窗口里画不动），不是 UI 线程算得慢。
            //
            // 只挂一个静态处理器：Rendering 是每帧都触发的常驻事件，逐次重建都 += 会累积订阅；
            // 窗口最小化时它压根不触发，所以复用一个句柄、每次覆盖待测 stopwatch。
            _presentWatch = watch;
            _presentFlowName = name;
            if (_presentHandler == null)
            {
                _presentHandler = (_, _) =>
                {
                    var pending = _presentWatch;
                    var pendingName = _presentFlowName;
                    System.Windows.Media.CompositionTarget.Rendering -= _presentHandler;
                    _presentHandler = null;
                    _presentWatch = null;
                    if (pending != null)
                        PerfLog?.Invoke($"[画布] 流程「{pendingName}」呈现={pending.ElapsedMilliseconds}ms");
                };
                System.Windows.Media.CompositionTarget.Rendering += _presentHandler;
            }
        }

        private static System.Diagnostics.Stopwatch? _presentWatch;
        private static string? _presentFlowName;
        private static System.EventHandler? _presentHandler;

        private void Detach()
        {
            _runTimeTimer?.Stop();   // 没有挂接的图纸就没有耗时刷新
            if (_attachedFlow != null && _stepsChanged != null)
                _attachedFlow.Steps.CollectionChanged -= _stepsChanged;

            _stepsChanged = null;
            _attachedFlow = null;
            DetachLists();
            UnhookLanes();   // 最后一帧的泳道还挂在 _lanes 上，摘干净再放手
            UnhookLinks();   // 最后一帧的连线还挂在 Links 上，同上
            HideDragGuides();

            ClearUndoStacks();
            _dragStartedSnapshot = null;
        }

        /// <summary>丢弃全部旧泳道前摘掉 StepCollection 订阅（泳道 VM 每帧新建，不摘会钉住阻止 GC）</summary>
        private void UnhookLanes()
        {
            foreach (var lane in _lanes)
                lane.UnhookBranch();
        }

        /// <summary>丢弃旧连线前摘掉两端步骤的 State 订阅（连线 VM 每轮重建，同上）</summary>
        private void UnhookLinks()
        {
            foreach (var link in Links)
                link.UnhookModels();
        }

        private void DetachLists()
        {
            foreach (var list in _watchedLists)
                list.CollectionChanged -= OnFlowStepsChanged;

            _watchedLists.Clear();
        }

        /// <summary>
        /// 订阅整棵树的每个步骤集合：顶层 + 所有容器分支 + **每个容器的 Children**。
        /// 扁平画布上看得到全部深度，任何一处增删都要重画。
        ///
        /// Children 这一路是 2026-10-23 补的：此前只订 Steps，于是"加分支 / 删分支"
        /// （改的是容器 Children）画布完全收不到通知——FlowModel 自己会递增 Version，
        /// 但画布不读 Version，表现为"流程栏加了分支，画布要手动折叠再展开才出现"
        /// （用户实测口径）。分支改名走另一条路：泳道 VM 订着 StepCollection.PropertyChanged。
        /// </summary>
        private void AttachLists(ObservableCollection<StepModel> list)
        {
            Watch(list);

            foreach (var step in list)
            {
                if (step is not IContainerStep container || container.Children == null) continue;

                Watch(container.Children);

                foreach (var branch in container.Children)
                {
                    if (branch?.Steps != null) AttachLists(branch.Steps);
                }
            }
        }

        private void Watch(INotifyCollectionChanged list)
        {
            // 顶层 Steps 已由 RebuildCore 的 _stepsChanged 订阅（见那里），这里再挂一次
            // 会让同一次结构变更触发两遍全量重建（两个委托 Target+Method 相同，但 += 不去重）。
            // 顶层那一路有"切流程换图纸时整份重挂"的语义，所以保留它、跳过这里。
            if (ReferenceEquals(list, _attachedFlow?.Steps))
                return;

            _watchedLists.Add(list);
            list.CollectionChanged += OnFlowStepsChanged;
        }

        // ==================================================================
        //  渲染：扁平化整棵流程树
        // ==================================================================

        private void Render(FlowModel flow)
        {
            // 整段渲染期间抑制「节点坐标变化 → 整轮锚点重算」（见 OnNodePropertyChanged）。
            // 锚点由 RenderCore 在连线全部建好后统一算一次（BuildLinks 内一次 + 末尾顺序链后一次），
            // 这里只负责把闸门复位——异常路径也必须复位，否则之后拖动节点线不跟手。
            _suppressAnchorUpdate = true;
            try
            {
                RenderCore(flow);
            }
            finally
            {
                _suppressAnchorUpdate = false;
            }
        }

        private void RenderCore(FlowModel flow)
        {
            // 分段计时：真机上"重建+布局"可能长达数秒，只有把 RenderCore 拆开才知道钱花在哪。
            // 只在总耗时 >= 300ms 时打一行（小流程不打，避免刷屏）。
            var seg = PerfLog != null ? System.Diagnostics.Stopwatch.StartNew() : null;
            long tTopology = 0, tCollect = 0, tAutoLayout = 0, tNodes = 0, tGeometry = 0;

            DetachLists();

            _topology = FlowTopology.Build(flow);
            if (seg != null) { tTopology = seg.ElapsedMilliseconds; }

            // 1. 收集全树步骤并补齐布局（AutoLayout 递归：本层纵向、分支横向错开）
            var allSteps = new List<StepModel>();
            CollectSteps(flow.Steps, allSteps);
            if (seg != null) { tCollect = seg.ElapsedMilliseconds - tTopology; }

            if (flow.Layout.HasMissing(allSteps))
            {
                // 不传 collapseOverride：首次渲染保持既有行为——新布局的深容器按
                // AutoCollapseDepth 默认折叠，用户手动展开后坐标已在库、不会被折回去
                flow.Layout.AutoLayout(flow.Steps);
                if (seg != null) { tAutoLayout = seg.ElapsedMilliseconds - tTopology - tCollect; }
            }

            // 2. 建/复用节点（扁平，含折叠祖先内的步骤——它们进池不进画布）
            _steps.Clear();
            UnhookLanes();   // 泳道每帧新建，丢弃前摘掉分支订阅（IsBranchRunning 的挂摘成对）
            _lanes.Clear();
            _nodeMap.Clear();
            _frameRects.Clear();

            foreach (var step in allSteps)
            {
                var node = GetOrCreateNode(step);
                _steps.Add(node);
                if (!IsHiddenByCollapse(step))
                    _nodeMap[step.StepID] = node;
            }
            if (seg != null) { tNodes = seg.ElapsedMilliseconds - tTopology - tCollect - tAutoLayout; }

            // 3. 几何：自底向上算容器框与泳道矩形（展开态）
            ComputeGeometry(flow.Steps);
            if (seg != null) { tGeometry = seg.ElapsedMilliseconds - tTopology - tCollect - tAutoLayout - tNodes; }

            long tGeometryEnd = seg?.ElapsedMilliseconds ?? 0;

            // 4. 折叠角标：统计每个折叠容器藏了多少步
            foreach (var node in _steps)
            {
                if (node.Kind != CanvasNodeKind.Container || !node.IsCollapsed) continue;
                node.CollapsedChildCount = CountDescendants(node.Model!);
            }

            // 5. Z 序组装：先框、再泳道、再内容（NodifyCanvas 按集合序叠放，先入垫底）
            Nodes.Clear();
            AppendInZOrder(flow.Steps);
            long tNodesList = (seg?.ElapsedMilliseconds ?? 0) - tGeometryEnd;

            // 6. 连线（数据依赖 + 执行顺序链）
            AttachLists(flow.Steps);
            BuildLinks(flow);
            long tDataLinks = (seg?.ElapsedMilliseconds ?? 0) - tGeometryEnd - tNodesList;

            // 顶层 + 所有分支集合（CollectCollections 会把顶层本身也收进去，初始化器不能再种一份）
            var collections = new List<ObservableCollection<StepModel>>();
            CollectCollections(flow.Steps, collections);
            BuildOrderLinks(collections);
            UpdateLinkAnchors();
            long tOrderLinks = (seg?.ElapsedMilliseconds ?? 0) - tGeometryEnd - tNodesList - tDataLinks;

            PrunePool();

            if (seg != null)
            {
                long total = seg.ElapsedMilliseconds;
                if (total >= 100)
                {
                    PerfLog?.Invoke(
                        $"[画布] 分段「{flow.FlowName}」步={allSteps.Count} 节点={_steps.Count} "
                        + $"拓扑={tTopology} 收集={tCollect} 自动排位={tAutoLayout} 建点={tNodes} "
                        + $"几何={tGeometry} 装填={tNodesList} 数据线={tDataLinks} 顺序链={tOrderLinks} "
                        + $"回收={total - tGeometryEnd - tNodesList - tDataLinks - tOrderLinks} 合计={total}ms");
                }
            }
        }

        private static void CollectCollections(
            ObservableCollection<StepModel> list,
            List<ObservableCollection<StepModel>> into)
        {
            into.Add(list);
            foreach (var step in list)
            {
                if (step is not IContainerStep container || container.Children == null) continue;
                foreach (var branch in container.Children)
                {
                    if (branch?.Steps != null) CollectCollections(branch.Steps, into);
                }
            }
        }

        private static void CollectSteps(ObservableCollection<StepModel> list, List<StepModel> into)
        {
            foreach (var step in list)
            {
                if (step == null) continue;
                into.Add(step);

                if (step is not IContainerStep container || container.Children == null) continue;
                foreach (var branch in container.Children)
                {
                    if (branch?.Steps != null) CollectSteps(branch.Steps, into);
                }
            }
        }

        /// <summary>该步骤是否被某个折叠的祖先容器藏起来（含自身是折叠容器的子孙）</summary>
        private bool IsHiddenByCollapse(StepModel step)
        {
            if (!_topology.TryGet(step.StepID, out var pos)) return false;

            var current = pos;
            while (current != null)
            {
                // 祖先链上任何一层容器是折叠的，本步骤就不渲染
                var parent = current.ParentContainer;
                if (parent == null) return false;

                if (parent.Step != null
                    && _nodePool.TryGetValue(parent.StepId, out var parentNode)
                    && parentNode.IsCollapsed)
                    return true;

                current = parent;
            }

            return false;
        }

        private CanvasNodeViewModel GetOrCreateNode(StepModel step)
        {
            if (_nodePool.TryGetValue(step.StepID, out var pooled) && ReferenceEquals(pooled.Model, step))
            {
                RefreshNodeState(pooled);
                return pooled;
            }

            var node = new CanvasNodeViewModel(step);

            // 必填端口元数据：节点入池一次注入（同一 StepID 复用同一节点，元数据不变）
            node.RequiredPortNames = ResolveRequiredPortNames(step);

            // 模块级流连接器：每节点一根输入脚 + 一根输出脚（连接器锚点由 Nodify 控件自动回写）
            node.Inputs.Add(new CanvasConnectorViewModel(node, "__flow_in", typeof(object), isInput: true));
            node.Outputs.Add(new CanvasConnectorViewModel(node, "__flow_out", typeof(object), isInput: false));

            // 节点右键菜单的命令入口：菜单的 DataContext 是节点 VM（弹层够不到 UserControl 资源树），
            // 与连线 VM 的 UnbindCommand 同一模式——主 VM 在这里注入闭包命令
            node.OpenBindingCommand = new DelegateCommand(() =>
            {
                if (node.Model != null) ModuleLinkRequested?.Invoke(node.Model);
            });
            node.OpenModuleParametersCommand = new DelegateCommand(() => OpenModuleParameters(node));
            node.ToggleBreakpointCommand = new DelegateCommand(() => OnToggleBreakpoint(node));
            node.ToggleDisableCommand = new DelegateCommand(() => OnToggleDisable(node));
            node.DeleteCommand = new DelegateCommand(() => OnDeleteNode(node));
            node.CopyCommand = new DelegateCommand(() => OnCopyNode(node));

            // 重命名 / 编辑注释：与流程栏同一弹窗、同一门禁（Rename/EditComment 归结构编辑）。
            // 画布此前缺这两个入口——同一个步骤在两个编辑面上能力不一致。
            node.RenameCommand = new DelegateCommand(() => BeginRenameNode(node));
            node.EditCommentCommand = new DelegateCommand(() => BeginEditCommentNode(node));

            // 分支结构动作：与流程栏同一批处理器（FlowBranchEditor），本处只管运行锁与回执。
            // 画布侧此前完全没有这类入口（只能回流程栏点），菜单可见性由节点 VM 的三个类型判据控。
            node.AddBranchCommand = new DelegateCommand(() => OnAddBranch(node, FlowBranchKind.Parallel));
            node.AddElseIfBranchCommand = new DelegateCommand(() => OnAddBranch(node, FlowBranchKind.ElseIf));
            node.AddElseBranchCommand = new DelegateCommand(() => OnAddBranch(node, FlowBranchKind.Else));
            node.AddCaseBranchCommand = new DelegateCommand(() => OnAddBranch(node, FlowBranchKind.Case));

            node.PropertyChanged += OnNodePropertyChanged;
            node.PropertyChanged += OnNodeSelectionChanged;
            _nodePool[step.StepID] = node;
            RefreshNodeState(node);
            return node;
        }

        /// <summary>从图纸/拓扑同步节点的展示状态（每次渲染都会调，幂等）</summary>
        private void RefreshNodeState(CanvasNodeViewModel node)
        {
            var model = node.Model;
            if (model == null) return;

            node.OrderIndex = _topology.TryGet(model.StepID, out var pos) ? pos!.IndexInOwner + 1 : 0;
            // 叠放层次按拓扑深度：父框 < 父泳道 < 子框 < 子泳道 < 子内容（见 CanvasNodeViewModel.ZOrder）
            node.Depth = pos?.Depth ?? 0;

            if (TryGetLayout(model.StepID, out var x, out var y, out var collapsed))
            {
                node.IsCollapsed = collapsed;
                // 存储坐标先进来打底：步骤用它；折叠框锚在它上面；
                // 展开容器的 Location 稍后由几何阶段覆盖为框体左上角
                node.Location = new Point(x, y);
            }

            node.RefreshHeader();
            node.RefreshMissingRequired();   // 必填参数角标随渲染重算（Rebuild 时重算的口径）
        }

        /// <summary>FlowLayoutStore 读取的小包装（缺项返回 false）。
        /// 折叠态 = 落盘值 **或** 会话级自动折叠（大流程首帧，见 TryAutoCollapseBigFlow）</summary>
        private bool TryGetLayout(Guid id, out double x, out double y, out bool collapsed)
        {
            x = y = 0;
            collapsed = false;
            if (_attachedFlow == null) return false;
            if (!_attachedFlow.Layout.TryGet(id, out var layout) || layout == null) return false;
            x = layout.X;
            y = layout.Y;
            collapsed = layout.Collapsed || _sessionCollapsed.Contains(id);
            return true;
        }

        // ==================================================================
        //  几何：自底向上算泳道与容器框
        // ==================================================================

        /// <summary>
        /// 递归计算一层内容的外接矩形：直接子项（步骤盒 / 折叠框 / 展开框）的并集。
        /// 同时为每个分支铺泳道矩形、为每个展开容器铺框体。
        /// 返回本层所有可见内容（含嵌套框）的外接矩形。
        /// </summary>
        private Rect ComputeGeometry(ObservableCollection<StepModel> list)
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            bool any = false;

            foreach (var step in list)
            {
                if (step == null) continue;

                Rect rect;
                if (step is IContainerStep container && container.Children != null)
                {
                    rect = ComputeContainerGeometry(step, container);
                }
                else
                {
                    var node = _nodeMap[step.StepID];
                    rect = new Rect(node.Location.X, node.Location.Y, NodeWidth, NodeHeight);
                }

                if (!any)
                {
                    minX = maxX = rect.Left;
                    minY = maxY = rect.Top;
                    any = true;
                }

                minX = Math.Min(minX, rect.Left);
                minY = Math.Min(minY, rect.Top);
                maxX = Math.Max(maxX, rect.Right);
                maxY = Math.Max(maxY, rect.Bottom);
            }

            return any ? new Rect(minX, minY, maxX - minX, maxY - minY) : Rect.Empty;
        }

        /// <summary>计算一个容器的泳道与框体几何，返回框体矩形（含头带）</summary>
        private Rect ComputeContainerGeometry(StepModel containerStep, IContainerStep container)
        {
            var node = _nodeMap[containerStep.StepID];
            double laneBottomExtra = FlowLayoutStore.GetLaneBottomExtra(containerStep);
            double frameBottomExtra = FlowLayoutStore.GetFrameBottomExtra(containerStep);

            // 折叠态：框体就锚在存储坐标上，只剩头带
            if (node.IsCollapsed)
            {
                var collapsedRect = new Rect(node.Location.X, node.Location.Y, CollapsedFrameWidth, CollapsedFrameHeight);
                _frameRects[containerStep.StepID] = collapsedRect;
                node.LaneWidth = collapsedRect.Width;
                node.LaneHeight = collapsedRect.Height;
                return collapsedRect;
            }

            // 各分支的内容矩形（先递归嵌套容器，保证祖先能拿到子孙框的最终矩形）。
            // 泳道每帧新建：它没有任何需要跨帧保留的状态，池化反而会在折叠后吐出脏泳道
            var branchNodes = new List<CanvasNodeViewModel>();
            int branchIndex = 0;
            foreach (var branch in container.Children)
            {
                if (branch?.Steps == null) continue;
                var laneNode = CanvasNodeViewModel.CreateLane(branch, containerStep, branchIndex++);
                laneNode.Depth = node.Depth;   // 泳道与所属容器同层：父框 < 父泳道 < 子内容
                _lanes.Add(laneNode);
                branchNodes.Add(laneNode);
            }

            var contentRects = new List<Rect>();
            foreach (var branch in container.Children)
            {
                if (branch?.Steps == null) continue;
                contentRects.Add(branch.Steps.Count == 0 ? Rect.Empty : ComputeGeometry(branch.Steps));
            }

            // 畸形容器（一条分支都没有）：渲染不出泳道，框体锚在存储坐标上兜底
            if (contentRects.Count == 0)
            {
                var bare = new Rect(node.Location.X, node.Location.Y, CollapsedFrameWidth, CollapsedFrameHeight);
                node.LaneWidth = bare.Width;
                node.LaneHeight = bare.Height;
                _frameRects[containerStep.StepID] = bare;
                return bare;
            }

            var laneRects = ComputeLaneRects(contentRects, node.Location, laneBottomExtra);

            for (int i = 0; i < branchNodes.Count; i++)
            {
                var laneNode = branchNodes[i];
                laneNode.IsLaneEmpty = contentRects[i].IsEmpty;
                laneNode.Location = laneRects[i].Location;
                laneNode.LaneWidth = laneRects[i].Width;
                laneNode.LaneHeight = laneRects[i].Height;
            }

            // 框体 = 泳道并集 + 框内边距 + 头带 + 底部条带（并行汇合条）
            var laneUnionLeft = laneRects.Min(r => r.Left);
            var laneUnionTop = laneRects.Min(r => r.Top);
            var laneUnionRight = laneRects.Max(r => r.Right);
            var laneUnionBottom = laneRects.Max(r => r.Bottom);

            var frame = new Rect(
                laneUnionLeft - FramePadding,
                laneUnionTop - FramePadding - FrameHeaderHeight,
                (laneUnionRight - laneUnionLeft) + FramePadding * 2,
                (laneUnionBottom - laneUnionTop) + FramePadding * 2 + FrameHeaderHeight + frameBottomExtra);

            node.Location = frame.Location;
            node.LaneWidth = frame.Width;
            node.LaneHeight = frame.Height;
            _frameRects[containerStep.StepID] = frame;

            return frame;
        }

        /// <summary>
        /// 各分支的泳道矩形：有内容列 = 内容外接框 ± 内边距；空分支列 = 占位泳道按列序插位。
        ///
        /// 空泳道的列序规则与布局器（Core\Models\FlowLayoutStore.PlaceExpanded）同一条：
        /// 列左缘 = 上一列泳道右缘 + ColumnGap，首列 = 容器框左缘 + FramePadding。
        /// 有内容列以内容实位为准（用户拖过的坐标不会被硬拉回列起点），它前面的空列向左回推——
        /// 多条空泳道不再同址、也不再压到内容/嵌套容器框上
        ///（2026-10-09 真机"流程画布太乱"里那条半透明蓝块就是它）。
        /// </summary>
        private static List<Rect> ComputeLaneRects(List<Rect> contentRects, Point anchor, double laneBottomExtra)
        {
            var lanes = new List<Rect>(contentRects.Count);
            for (int i = 0; i < contentRects.Count; i++)
                lanes.Add(Rect.Empty);

            int firstPopulated = contentRects.FindIndex(r => !r.IsEmpty);
            if (firstPopulated < 0)
            {
                // 全空：框体锚在容器存储坐标上，空泳道自框内左上按列依次铺开
                double x = anchor.X + FramePadding;
                double y = anchor.Y + FrameHeaderHeight + FramePadding;
                for (int i = 0; i < lanes.Count; i++)
                {
                    lanes[i] = new Rect(x, y, EmptyLaneWidth, EmptyLaneHeight + laneBottomExtra);
                    x += EmptyLaneWidth + ColumnGap;
                }
                return lanes;
            }

            // 空泳道的顶（含头带）与有内容列对齐——并列/混合场景下泳道头带在同一横带
            double laneTop = contentRects.Where(r => !r.IsEmpty)
                .Min(r => r.Top - LanePadding - LaneHeaderHeight);

            // 首个有内容列定盘，向右按列距推进
            double cursorX = contentRects[firstPopulated].Left - LanePadding;
            for (int i = firstPopulated; i < lanes.Count; i++)
            {
                var content = contentRects[i];
                lanes[i] = content.IsEmpty
                    ? new Rect(cursorX, laneTop, EmptyLaneWidth, EmptyLaneHeight + laneBottomExtra)
                    : new Rect(
                        content.Left - LanePadding,
                        content.Top - LanePadding - LaneHeaderHeight,
                        Math.Max(content.Width + LanePadding * 2, MinLaneWidth),
                        content.Height + LanePadding * 2 + LaneHeaderHeight + laneBottomExtra);

                cursorX = lanes[i].Right + ColumnGap;
            }

            // 它前面的空列向左回推（都是占位宽，不与首个有内容列相交）
            for (int i = firstPopulated - 1; i >= 0; i--)
            {
                lanes[i] = new Rect(
                    lanes[i + 1].Left - ColumnGap - EmptyLaneWidth,
                    laneTop, EmptyLaneWidth, EmptyLaneHeight + laneBottomExtra);
            }

            return lanes;
        }

        /// <summary>
        /// Z 序组装：容器框 → 其各分支泳道 → 分支内容（嵌套容器递归）。
        /// 只决定集合序（无 Panel.ZIndex 时的兜底序）；视觉层次以节点 ZOrder 为准（见 Nodes 的说明）
        /// </summary>
        private void AppendInZOrder(ObservableCollection<StepModel> list)
        {
            foreach (var step in list)
            {
                if (step == null) continue;

                if (step is IContainerStep container && container.Children != null && !IsHiddenByCollapse(step))
                {
                    var frameNode = _nodeMap[step.StepID];
                    Nodes.Add(frameNode);

                    foreach (var branch in container.Children)
                    {
                        if (branch?.Steps == null) continue;
                        var lane = _lanes.FirstOrDefault(l => ReferenceEquals(l.Branch, branch));
                        if (lane != null)
                            Nodes.Add(lane);

                        AppendInZOrder(branch.Steps);
                    }

                    continue;
                }

                if (!_nodeMap.TryGetValue(step.StepID, out var node)) continue;
                if (node.Kind == CanvasNodeKind.Lane) continue;
                Nodes.Add(node);
            }
        }

        private int CountDescendants(StepModel container)
        {
            int count = 0;
            if (container is not IContainerStep ic || ic.Children == null) return 0;

            foreach (var branch in ic.Children)
            {
                if (branch?.Steps == null) continue;
                foreach (var step in branch.Steps)
                {
                    if (step == null) continue;
                    count += 1 + CountDescendants(step);
                }
            }

            return count;
        }

        // ==================================================================
        //  节点事件：坐标落库 / 选中联动
        // ==================================================================

        private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(CanvasNodeViewModel.Location)) return;
            if (sender is not CanvasNodeViewModel node || _attachedFlow == null) return;

            // 装饰节点（泳道）与容器框（几何算出）的坐标不入库
            if (node.Kind != CanvasNodeKind.Step || node.StepId == Guid.Empty) return;

            var current = node.Location;
            var existing = _attachedFlow.Layout.Find(node.StepId);
            var collapsed = existing?.Collapsed ?? false;

            _attachedFlow.Layout.Set(node.StepId, current.X, current.Y, collapsed);

            // 渲染期不许逐节点重算锚点：`RefreshNodeState` 每建一个节点就写一次 Location，
            // 而此刻 Links 里还是**上一个流程**的连线（Render 到 BuildLinks 才清），
            // 于是每个节点都对着旧线跑一遍全量 UpdateLinkAnchors —— O(新节点数 × 旧线数)，
            // 3000 节点切进来就是几万次重算、每次还发 4 条 PropertyChanged。
            // Render 末尾已有一次统一重算（Render → UpdateLinkAnchors），这里跳过即可。
            // **不能改判 _rebuilding**：OnNodePropertyChanged 故意不判它——拖动时要实时跟随，
            // 而拖动期间 _rebuilding 是 false。
            if (_suppressAnchorUpdate) return;

            UpdateLinkAnchors();
        }

        /// <summary>
        /// Render 期间抑制「节点坐标变化 → 整轮锚点重算」。只在 <see cref="RenderCore"/> 里置位，
        /// try/finally 保证出口复位；渲染末尾由 <see cref="UpdateLinkAnchors"/> 一次性算完。
        /// </summary>
        private bool _suppressAnchorUpdate;

        private void OnNodeSelectionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(CanvasNodeViewModel.IsSelected)) return;
            if (sender is not CanvasNodeViewModel node || _rebuilding || _suppressStepSync) return;
            if (!node.IsSelected || node.Model == null) return;

            // 画布点选 → 工作区当前步骤（流程栏/属性栏跟随）。多选时以最后点到的为准
            SwitchCurrentStep(node.Model);
        }

        /// <summary>
        /// 把当前步骤报给工作区。包 try/catch：WorkspaceContext.SwitchStep 对"不属于当前流程"直接抛异常，
        /// 而画布拿到的节点有可能在别的渲染周期里已被移出图纸。
        /// </summary>
        private void SwitchCurrentStep(StepModel? step)
        {
            _suppressStepSync = true;
            try
            {
                _workspace.SwitchStep(step!);
            }
            catch (InvalidOperationException)
            {
                // 图纸已变，静默放弃联动即可
            }
            finally
            {
                _suppressStepSync = false;
            }
        }

        /// <summary>外部（流程栏/属性栏）改了当前步骤 → 画布选中并滚入视野。扁平画布不需要切层</summary>
        private void OnCurrentStepChanged()
        {
            if (_suppressStepSync || _rebuilding) return;

            var step = _workspace.CurrentStep;
            if (_attachedFlow == null) return;

            if (step == null)
            {
                // 外部清空当前步骤：清掉画布选中，保持两边一致
                _suppressStepSync = true;
                try
                {
                    foreach (var selected in _nodeMap.Values.Where(n => n.IsSelected))
                        selected.IsSelected = false;
                }
                finally
                {
                    _suppressStepSync = false;
                }
                return;
            }

            if (IsHiddenByCollapse(step))
            {
                StatusHint = $"步骤「{step.StepName}」在折叠的容器里，展开容器后可见";
                return;
            }

            if (!_nodeMap.TryGetValue(step.StepID, out var node))
            {
                // 图纸刚变过而尚未重画：补一次重画再试
                RebuildInPlace();
                if (!_nodeMap.TryGetValue(step.StepID, out node)) return;
            }

            // 先清掉其它节点的选中（抑制期间不回写工作区），再点亮目标
            _suppressStepSync = true;
            try
            {
                foreach (var other in _nodeMap.Values.Where(n => n.IsSelected && !ReferenceEquals(n, node)))
                    other.IsSelected = false;
                node.IsSelected = true;
            }
            finally
            {
                _suppressStepSync = false;
            }

            ViewportActionRequested?.Invoke(node);
        }

        // ------------------------------------------------------------------
        //  必填参数角标（评审清单 B 包）：与编译期 FlowCompiler 的 [参数缺失] 同一判据的画布投影。
        //  端口元数据按 PluginTypeName 反射原型实例解析并缓存——端口名 / IsRequired 是
        //  插件类型的静态事实（PluginService.LoadPlugins 启动扫描就是这么建的），
        //  与 StepParameterDialog.ResolvePluginInstance 同一条反射先例；
        //  解析不到（假类型名桩 / 加载失败）返回 null，节点一律不亮角标。
        // ------------------------------------------------------------------

        /// <summary>插件类型 → 必填输入端口名列表（null = 解析不到）。进程级缓存：每类型只反射一次</summary>
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<
            string, IReadOnlyList<string>?> RequiredPortsCache = new();

        /// <summary>解析一个插件类型的必填输入端口名；解析失败缓存并返回 null（不反复抛）</summary>
        private static IReadOnlyList<string>? ResolveRequiredPortNames(StepModel step)
        {
            if (step is not ActionStep action || string.IsNullOrWhiteSpace(action.PluginTypeName))
                return null;

            return RequiredPortsCache.GetOrAdd(action.PluginTypeName, key =>
            {
                try
                {
                    var type = Type.GetType(key);
                    if (type == null || !typeof(VisionPluginBase).IsAssignableFrom(type))
                        return null;

                    if (Activator.CreateInstance(type) is not VisionPluginBase prototype)
                        return null;

                    return prototype.Inputs.Values
                        .Where(p => p.IsRequired)
                        .Select(p => p.Name)
                        .ToList();
                }
                catch
                {
                    // 原型构造失败（缺依赖/抛异常）= 端口元数据不可得，与"找不到类型"同口径
                    return null;
                }
            });
        }

        /// <summary>
        /// 清理池中**已被删除**的步骤节点（防止挂接阻塞 GC）。
        ///
        /// 判据是"在不在**当前方案的任何流程**里"，不是"在不在当前流程里"：
        /// 池子跨流程存活（换流程不再 Clear），拿当前拓扑当判据会把别的流程的节点全清掉，
        /// A→B→A 就永远复用不到。真正该回收的只有"步骤被删了"这一种。
        ///
        /// 方案级 StepID 集合**按方案缓存**：每帧重扫全方案在 3000 节点下是一笔白交的钱，
        /// 而"删了某条流程里的一个步骤"不会让别的流程的出图错——最坏是那个节点 VM 多留一会儿，
        /// 由 <see cref="NodePoolHardLimit"/> 兜底。缓存随 <see cref="IWorkspaceManager.CurrentSolution"/>
        /// 换引用而失效。
        /// </summary>
        private void PrunePool()
        {
            if (_nodePool.Count == 0) return;

            List<Guid>? candidates = null;
            foreach (var id in _nodePool.Keys)
            {
                if (!_topology.TryGet(id, out _))
                    (candidates ??= new List<Guid>()).Add(id);
            }

            if (candidates != null)
            {
                var alive = GetPoolScopeStepIds();
                foreach (var id in candidates)
                {
                    if (alive.Contains(id)) continue;   // 属于别的流程，留着复用
                    ReleasePooledNode(id);
                }
            }

            // 硬上限兜底：来回切过很多条流程后池子会按并集增长，超了整体清一次
            if (_nodePool.Count > NodePoolHardLimit)
                ClearPool();
        }

        private SolutionModel? _poolScopeSolution;
        private HashSet<Guid>? _poolScopeIds;

        /// <summary>当前方案里出现过的全部 StepID（按方案缓存，换方案失效）</summary>
        private HashSet<Guid> GetPoolScopeStepIds()
        {
            var solution = _workspace.CurrentSolution;
            if (ReferenceEquals(solution, _poolScopeSolution) && _poolScopeIds != null)
                return _poolScopeIds;

            _poolScopeSolution = solution;
            _poolScopeIds = CollectSolutionStepIds();
            return _poolScopeIds;
        }

        /// <summary>方案里所有流程、所有嵌套层级出现过的 StepID</summary>
        private HashSet<Guid> CollectSolutionStepIds()
        {
            var ids = new HashSet<Guid>();
            var solution = _workspace.CurrentSolution;
            if (solution?.Flows == null) return ids;

            foreach (var flow in solution.Flows)
            {
                if (flow?.Steps == null) continue;
                CollectInto(flow.Steps);
            }
            return ids;

            void CollectInto(IEnumerable<StepModel> steps)
            {
                foreach (var step in steps)
                {
                    if (step == null) continue;
                    ids.Add(step.StepID);
                    if (step is IContainerStep container && container.Children != null)
                    {
                        foreach (var branch in container.Children)
                        {
                            if (branch?.Steps != null) CollectInto(branch.Steps);
                        }
                    }
                }
            }
        }

        private void ReleasePooledNode(Guid id)
        {
            if (!_nodePool.TryGetValue(id, out var node)) return;
            node.PropertyChanged -= OnNodePropertyChanged;
            node.PropertyChanged -= OnNodeSelectionChanged;
            node.UnhookModel();
            _nodePool.Remove(id);
        }

        /// <summary>整体清池（硬上限兜底）：先逐个摘订阅，避免模型钉住已废弃的节点 VM</summary>
        private void ClearPool()
        {
            foreach (var id in _nodePool.Keys.ToList())
                ReleasePooledNode(id);
        }

        // ==================================================================
        //  模块级连线
        // ==================================================================

        /// <summary>
        /// 把各消费方 LinkedSources 聚合成模块级连线：
        /// 同一（生产方, 消费方）对的多条端口绑定合并为一根线；
        /// 全局变量/常量绑定不画线，折成消费方节点的角标；折叠子树内的绑定不画（计数留痕）。
        /// </summary>
        private void BuildLinks(FlowModel flow)
        {
            IllegalLinkCount = 0;
            DeferredLinkCount = 0;
            HiddenLinkCount = 0;

            // 一根数据线没有任何绑定拖在屏幕外也没意义；阈值取 1500：353 步流程约 350 根（正常），
            // 3542 步流程约 3500 根（此时代价已经从"可感知"变成"卡死"）
            const int DataLinkReduceThreshold = 1500;

            foreach (var node in _nodeMap.Values)
            {
                foreach (var port in node.Inputs) port.IsConnected = false;
                foreach (var port in node.Outputs) port.IsConnected = false;
                node.ResetExternalBindings();
            }

            UnhookLinks();   // 上一帧的连线（数据线 + 顺序链）整体作废，先摘 State 订阅再清
            Links.Clear();

            // 运行时变量名 → 定义节点。变量身份是名字（运行期按名取值），
            // 同名按拓扑顺序"后者覆盖前者"，与 FlowQueryHelper 的口径一致。
            // 含折叠子树里的定义节点（变量在运行期依然存在，只是画布上看不见它）
            var variableProducers = new Dictionary<string, CanvasNodeViewModel>(StringComparer.Ordinal);
            foreach (var node in _steps)
            {
                if (node.Model != null
                    && FlowQueryHelper.TryGetDefinedVariable(node.Model, out var varName, out _))
                {
                    variableProducers[varName] = node;
                }
            }

            // 先聚合、后建 VM（两遍走）。
            // 为什么必须两遍：`ShowDataLinks` 默认关闭时，普通数据线根本不画；旧实现却是
            // 「先给每对模块 new 一根 CanvasLinkViewModel + 3 个命令 + N 个绑定 DTO，
            // 合并完再逐根 UnhookModels + RemoveAt 丢掉」——纯白扔掉 90% 的分配。
            // 3000 节点、2500 根线的规模下这一段能占重建分配的一大块。
            //
            // 语义一字不改：过滤仍然**晚于**聚合与合法性判定（同一对模块的多条绑定先合并成一根线、
            // 再由"有没有非法绑定"决定这根线画不画；否则后到的非法绑定会挂在一根从未渲染过的线上，
            // 同一对模块换个绑定顺序行为就变了）。差别只是"要不要造那根线的 VM"。
            var aggregated = new Dictionary<(Guid Source, Guid Target), AggregatedLink>();

            foreach (var consumer in _nodeMap.Values)   // 只遍历渲染中的节点；折叠子树的绑定另行计数
            {
                var model = consumer.Model;
                if (model?.LinkedSources == null) continue;

                foreach (var kvp in model.LinkedSources)
                {
                    var link = kvp.Value;
                    if (link == null) continue;

                    var kind = link.NormalizeKind();
                    if (kind is not (LinkKind.StepPort or LinkKind.RuntimeVariable))
                    {
                        // 全局变量 / 常量：没有"上游模块"可画线，折成节点角标（悬停看明细）
                        var extLabel = kind == LinkKind.GlobalVariable
                            ? $"全局变量 {link.TargetPortName}"
                            : $"常量 {link.TargetPortName}";
                        consumer.AddExternalBinding(extLabel);
                        continue;
                    }

                    CanvasNodeViewModel producer;
                    string producerPort;

                    if (kind == LinkKind.StepPort)
                    {
                        if (!_nodeMap.TryGetValue(link.TargetStepId, out producer))
                        {
                            if (_topology.TryGet(link.TargetStepId, out _))
                                HiddenLinkCount++;   // 上游存在，只是被折叠藏住
                            else
                                DeferredLinkCount++; // 上游已删除：编译会报致命断连
                            continue;
                        }
                        producerPort = link.TargetPortName;
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(link.TargetPortName)
                            || !variableProducers.TryGetValue(link.TargetPortName, out producer!))
                        {
                            DeferredLinkCount++;
                            continue;
                        }

                        if (!_nodeMap.ContainsKey(producer.StepId))
                        {
                            // 定义节点在折叠子树里：变量仍然有效，只是画不出线
                            HiddenLinkCount++;
                            continue;
                        }
                        producerPort = link.TargetPortName;
                    }

                    var legality = _topology.Classify(producer.StepId, consumer.StepId);
                    var illegal = legality is LinkLegality.SameListReversed
                        or LinkLegality.CrossBranch
                        or LinkLegality.ProducerAfterEnclosingContainer;

                    if (illegal) IllegalLinkCount++;

                    var binding = new CanvasLinkBinding(
                        consumer.Model, kvp.Key,
                        $"{DescribeInput(model, kvp.Key)} ← {producer.Header}.{producerPort}")
                    {
                        IsIllegal = illegal,
                        IllegalReason = illegal ? Explain(legality) : null,
                        // 解绑撤销要用建线前的 LinkReference 快照；kvp.Value 正是当前生效的那份
                        OriginalLink = link,
                    };

                    var pairKey = (producer.StepId, consumer.StepId);
                    if (!aggregated.TryGetValue(pairKey, out var agg))
                    {
                        agg = new AggregatedLink(producer, consumer);
                        aggregated[pairKey] = agg;
                    }

                    agg.Bindings.Add(binding);
                    if (illegal)
                    {
                        agg.HasIllegal = true;
                        agg.FirstIllegalReason ??= binding.IllegalReason;
                    }
                }
            }

            // 第二遍：只为"会显示的线"造 VM。判据与旧的"建完再删"完全一致——
            // 非法线在数据线关闭时也要画（红虚线是要用户去修的错误提示，不是噪声）。
            //
            // 大流程降级：数据线全开时，3542 步的图有 3500+ 根数据线 = 3500 个 StepConnection 元素
            // （每根还带一份菜单与一次文本排版）。超过阈值只保留非法线，工具栏给出提示。
            // 这是**渲染降级**，不改任何数据；用户把「数据线」关掉再打开也不解除，
            // 因为解除的唯一合理条件是"流程变小了"（此时 aggregate 数自然落回阈值内）。
            bool reduceDataLinks = ShowDataLinks && aggregated.Count > DataLinkReduceThreshold;
            DataLinksReduced = reduceDataLinks;

            foreach (var agg in aggregated.Values)
            {
                if (!ShowDataLinks && !agg.HasIllegal) continue;
                if (reduceDataLinks && !agg.HasIllegal) continue;

                var producer = agg.Producer;
                var consumer = agg.Consumer;

                var linkVm = new CanvasLinkViewModel(producer, consumer)
                {
                    // 连线右键菜单的 DataContext 是连线 VM（弹层够不到主 VM），
                    // 命令入口在这里注入——与节点 VM 的菜单命令同一模式
                    UnbindCommand = new DelegateCommand<CanvasLinkBinding?>(OnUnbindBinding),
                };
                var unbindAllTarget = linkVm;
                linkVm.OpenBindingCommand = new DelegateCommand(() =>
                {
                    if (unbindAllTarget.Target.Model != null)
                        ModuleLinkRequested?.Invoke(unbindAllTarget.Target.Model);
                });
                linkVm.UnbindAllCommand = new DelegateCommand(() => UnbindAllBindings(unbindAllTarget));

                foreach (var binding in agg.Bindings)
                    linkVm.Bindings.Add(binding);

                linkVm.IsIllegal = agg.HasIllegal;
                linkVm.WarningText = agg.FirstIllegalReason;
                linkVm.NotifyBindingsChanged();

                Links.Add(linkVm);
                producer.Outputs[0].IsConnected = true;
                consumer.Inputs[0].IsConnected = true;
            }

            // 顺序链在 Render 里于 BuildLinks 之后统一构建（需要全量集合清单）
            UpdateLinkAnchors();
        }

        /// <summary>
        /// 聚合中的一根模块级连线（还没决定造不造 VM）。
        /// producer/consumer 用引用持有，绑定列表按 LinkedSources 的遍历顺序追加。
        /// </summary>
        private sealed class AggregatedLink
        {
            public AggregatedLink(CanvasNodeViewModel producer, CanvasNodeViewModel consumer)
            {
                Producer = producer;
                Consumer = consumer;
            }

            public CanvasNodeViewModel Producer { get; }
            public CanvasNodeViewModel Consumer { get; }
            public List<CanvasLinkBinding> Bindings { get; } = new();
            public bool HasIllegal { get; set; }
            public string? FirstIllegalReason { get; set; }
        }

        /// <summary>
        /// 执行顺序链：同一集合里相邻两步之间画一条"自上而下"的顺序线（含容器框——
        /// 容器是执行序的一员，进出都走框体；分支内部各自成链）。
        /// 折叠子树里的步骤不参与（不可见）；相邻判定只看集合下标，与数据线无关。
        /// </summary>
        private void BuildOrderLinks(IReadOnlyList<ObservableCollection<StepModel>> collections)
        {
            if (!ShowOrderLinks) return;

            foreach (var list in collections)
            {
                CanvasNodeViewModel? prev = null;
                foreach (var step in list)
                {
                    if (step == null) continue;
                    if (IsHiddenByCollapse(step)) continue;
                    if (!_nodeMap.TryGetValue(step.StepID, out var node)) continue;

                    if (prev != null)
                    {
                        Links.Add(new CanvasLinkViewModel(prev, node)
                        {
                            IsOrderLink = true,
                        });
                    }
                    prev = node;
                }
            }
        }
        /// 数据线：源 = 生产方盒子右缘中点，目标 = 消费方盒子左缘中点；
        /// 顺序链：自上而下流动——源 = 上一步盒子底缘中点，目标 = 下一步盒子顶缘中点，
        /// 但窄算子与宽容器之间的链**共享同一 X**（见 SharedVerticalX）——
        /// 容器比算子宽得多，中心对中心必然错开，正交走线被拽成"下-横-下"的拐弯，
        /// 视觉上就是"容器与上级算子做不到居中对齐"（2026-10-10 真机反馈）。
        /// 容器框用框体矩形，步骤盒用固定尺寸常量。
        /// 节点拖动（Location 变化）也会走到这里，连线实时跟随。
        /// </summary>
        private void UpdateLinkAnchors()
        {
            foreach (var link in Links)
            {
                if (link.IsOrderLink)
                {
                    var src = EdgeMidpoint(link.Source, OrderEdge.Bottom);
                    var tgt = EdgeMidpoint(link.Target, OrderEdge.Top);

                    // 就窄轴拉直：两锚点共享同一 X（优先"对方中心落在我跨度内"的那一侧），
                    // 链成为一条垂直线、与窄节点居中对齐；互不在跨度内（并排错位的手摆图）
                    // 保持中心对中心不硬掰。
                    double sharedX = SharedVerticalX(link.Source, link.Target, src.X, tgt.X);
                    if (!double.IsNaN(sharedX))
                    {
                        src.X = sharedX;
                        tgt.X = sharedX;
                    }

                    link.SourceAnchor = src;
                    link.TargetAnchor = tgt;
                    link.SourcePosition = Nodify.ConnectorPosition.Bottom;
                    link.TargetPosition = Nodify.ConnectorPosition.Top;
                }
                else
                {
                    link.SourceAnchor = EdgeMidpoint(link.Source, OrderEdge.Right);
                    link.TargetAnchor = EdgeMidpoint(link.Target, OrderEdge.Left);

                    // 进出边按「节点中心」几何选：消费方中心明显错开到生产方中心左侧
                    // （超过半个模块宽）→ 左出右入，让正交走线绕就近一侧；
                    // 垂直对齐/略偏 → 维持右出左入（右侧绕行）。
                    // 判据不能用锚点差——Right/Left 锚点天然差一个模块宽，
                    // 竖排同列会被误判成"消费方在左侧"，线从左缘出被节点盖住。
                    double sourceCenterX = link.Source.Location.X
                        + (link.Source.Kind == CanvasNodeKind.Container ? link.Source.LaneWidth : NodeWidth) / 2;
                    double targetCenterX = link.Target.Location.X
                        + (link.Target.Kind == CanvasNodeKind.Container ? link.Target.LaneWidth : NodeWidth) / 2;
                    bool targetFarLeft = targetCenterX < sourceCenterX - NodeWidth / 2;
                    link.SourcePosition = targetFarLeft
                        ? Nodify.ConnectorPosition.Left
                        : Nodify.ConnectorPosition.Right;
                    link.TargetPosition = targetFarLeft
                        ? Nodify.ConnectorPosition.Right
                        : Nodify.ConnectorPosition.Left;
                }
            }
        }

        private enum OrderEdge { Left, Right, Top, Bottom }

        /// <summary>节点的水平跨度宽（容器 = 框宽 LaneWidth，步骤 = 固定盒宽）</summary>
        private static double NodeSpanWidth(CanvasNodeViewModel node)
            => node.Kind == CanvasNodeKind.Container ? node.LaneWidth : NodeWidth;

        /// <summary>
        /// 纵向顺序链的共享 X（就窄轴拉直，2026-10-10）：
        ///  · 下游中心落在上游跨度内 → 用下游中心（箭头打在下游正中，容器→算子）；
        ///  · 否则上游中心落在下游跨度内 → 用上游中心（算子→容器：链贴着算子轴垂直下落）；
        ///  · 都不在（并排错位的手摆图，垂直线本不可能）→ NaN，调用方保持中心对中心。
        /// 结果向跨度内侧内缩 <see cref="EdgeAnchorInset"/>，避免锚点贴在框角 / 折叠钮上。
        /// </summary>
        private const double EdgeAnchorInset = 12;

        private static double SharedVerticalX(
            CanvasNodeViewModel source, CanvasNodeViewModel target,
            double sourceCenterX, double targetCenterX)
        {
            double sL = source.Location.X, sR = sL + NodeSpanWidth(source);
            double tL = target.Location.X, tR = tL + NodeSpanWidth(target);

            if (targetCenterX >= sL && targetCenterX <= sR)
                return Math.Min(Math.Max(targetCenterX, sL + EdgeAnchorInset), sR - EdgeAnchorInset);

            if (sourceCenterX >= tL && sourceCenterX <= tR)
                return Math.Min(Math.Max(sourceCenterX, tL + EdgeAnchorInset), tR - EdgeAnchorInset);

            return double.NaN;
        }

        private static Point EdgeMidpoint(CanvasNodeViewModel node, OrderEdge edge)
        {
            double w = node.Kind == CanvasNodeKind.Container ? node.LaneWidth : NodeWidth;
            double h = node.Kind == CanvasNodeKind.Container ? node.LaneHeight : NodeHeight;
            double x = node.Location.X, y = node.Location.Y;

            // 容器框的左右脚锚在头带高度（框顶 + 头带中心），而不是框缘垂直中点——
            // 框体随内容增高后，中点锚会飘到离头带/内容很远的地方（2026-10-07 真机反馈）
            double sideY = node.Kind == CanvasNodeKind.Container
                ? y + (node.IsCollapsed ? CollapsedFrameHeight : FrameHeaderHeight) / 2
                : y + h / 2;

            switch (edge)
            {
                case OrderEdge.Left: return new Point(x, sideY);
                case OrderEdge.Right: return new Point(x + w, sideY);
                case OrderEdge.Top: return new Point(x + w / 2, y);
                case OrderEdge.Bottom: return new Point(x + w / 2, y + h);
                default: return new Point(x, y);
            }
        }

        /// <summary>输入端口显示名：条件/等待节点键是变量 Guid，显示用别名；For 是循环次数；普通算子就是键名</summary>
        private static string DescribeInput(StepModel consumer, string key)
        {
            switch (consumer)
            {
                case ConditionStep condition:
                    var local = condition.LocalVariables.FirstOrDefault(v => v.Id.ToString() == key);
                    return local?.Name ?? key;
                // 等待节点不是 ConditionStep（无子分支），但条件变量的键口径与 If 完全一致
                case WaitStep wait:
                    var waitLocal = wait.LocalVariables.FirstOrDefault(v => v.Id.ToString() == key);
                    return waitLocal?.Name ?? key;
                case ForStep:
                    return key == "LoopCount" ? "循环次数" : key;
                default:
                    return key;
            }
        }

        private static string Explain(LinkLegality legality) => legality switch
        {
            LinkLegality.SameListReversed => "生产方排在消费方之后（执行顺序倒序）",
            LinkLegality.ProducerAfterEnclosingContainer => "生产方排在包住消费方的容器之后，消费时它还没执行",
            LinkLegality.CrossBranch => "跨分支取数：该分支本轮可能不执行，或取到上一轮陈旧值"
                + "（并行分支之间同样禁止互取：即使将来真并行，时序也不确定；需要共享的数据请在并行分组之前定义）",
            LinkLegality.Unknown => "连线端点不在图纸里（步骤已被删除）",
            _ => "结构非法连线",
        };

        // ==================================================================
        //  建线手势：模块 → 模块，端口对交给绑定弹窗
        // ==================================================================

        /// <summary>
        /// 结构合法性：只看身份与执行顺序（FlowTopology，与编译期同一套规则）。
        /// 端口是否存在、类型是否兼容由绑定弹窗与编译期把关——画布比编译器更严会出现
        /// "画布拒绝、编译器却允许"的矛盾。
        /// </summary>
        internal bool CanLinkSteps(CanvasNodeViewModel? source, CanvasNodeViewModel? target)
        {
            if (source == null || target == null) return false;
            if (ReferenceEquals(source, target)) return false;
            if (source.IsDecorator || target.IsDecorator) return false;
            if (source.Model == null || target.Model == null) return false;

            var legality = _topology.Classify(source.StepId, target.StepId);
            return legality is LinkLegality.SameListBefore or LinkLegality.ProducerIsAncestor;
        }

        /// <summary>
        /// 拖线源端快照。Nodify 7.3 真实时序（IL 取证）：控件在 Execute(CompletedCommand) **之前**
        /// 先置 IsVisible=false，且该 DP 默认 TwoWay——回流会触发 OnPendingConnectionChanged
        /// 把 PendingConnection.Source/Target 清空，建线时从 PendingConnection 读源端必是 null。
        /// 源端一律取本字段（OnStartConnection 时存入），绝不在 IsVisible=false 的通知里清它。
        /// </summary>
        private CanvasConnectorViewModel? _pendingSourceConnector;

        private void OnStartConnection(object? parameter)
        {
            if (parameter is CanvasConnectorViewModel connector)
            {
                _pendingSourceConnector = connector;
                PendingConnection.Source = connector;
                UpdateConnectableFeedback(connector);
            }

            PendingConnection.IsVisible = true;
        }

        /// <summary>
        /// 拖线中按拓扑把"能作为另一端"的端口亮出来、其余压灰（CanvasConnectorViewModel.IsConnectable）。
        /// 画布比编译器只严不松的口径见 CanLinkSteps 注释；结束时统一复位（见 ResetConnectableFeedback）。
        /// </summary>
        private void UpdateConnectableFeedback(CanvasConnectorViewModel source)
        {
            if (source.Owner == null) return;

            foreach (var node in _nodeMap.Values)
            {
                bool connectable = node.Model != null
                    && (source.IsInput
                        ? CanLinkSteps(node, source.Owner)      // 从输入脚起拖：候选节点是生产方
                        : CanLinkSteps(source.Owner, node));    // 从输出脚起拖：候选节点是消费方

                foreach (var input in node.Inputs)
                {
                    input.IsConnectable = connectable;
                    input.IsDragInProgress = true;
                }
                foreach (var output in node.Outputs)
                {
                    output.IsConnectable = connectable;
                    output.IsDragInProgress = true;
                }
            }
        }

        private void ResetConnectableFeedback()
        {
            foreach (var node in _nodeMap.Values)
            {
                foreach (var input in node.Inputs)
                {
                    input.IsConnectable = true;
                    input.IsDragInProgress = false;
                }
                foreach (var output in node.Outputs)
                {
                    output.IsConnectable = true;
                    output.IsDragInProgress = false;
                }
            }
        }

        private void OnCompleteConnection(object? parameter)
        {
            // Nodify 7.3 取证（PendingConnection::OnPendingConnectionCompleted 的 IL）：
            // 实参 = 目标连接器（Execute(get_Target)），且控件在 Execute 之前已置 IsVisible=false
            // （TwoWay 回流清空了 PendingConnection.Source/Target）——源端取拖线开始时的快照字段。
            var target = parameter as CanvasConnectorViewModel ?? PendingConnection.Target;
            var source = _pendingSourceConnector ?? PendingConnection.Source;

            // 收场：正常时序下控件已先置 IsVisible（此处幂等兜底），快照字段必须在这里清——
            // 不能挪进 OnPendingConnectionChanged（那里在 Execute 前就会跑，会把源端清掉）
            _pendingSourceConnector = null;
            PendingConnection.IsVisible = false;

            if (source?.Owner == null || target?.Owner == null) return;   // 拖线落空/取消：静默收场

            // 方向归一：拖线允许从任一端开始（从输入脚往回拖也是建线）
            var producer = source.Owner;
            var consumer = target.Owner;
            if (source.IsInput && !target.IsInput)
                (producer, consumer) = (target.Owner, source.Owner);

            if (!CanLinkSteps(producer, consumer))
            {
                var reason = producer.Model == null || consumer.Model == null
                    ? "端点身份不符（泳道不参与连线）"
                    : Explain(_topology.Classify(producer.StepId, consumer.StepId));
                StatusHint = $"连线被拒绝：{reason}";
                return;
            }

            StatusHint = null;

            // 端口对的选择交给绑定弹窗：它以 TargetStep 为锚构建上游候选树，
            // 写回 LinkedSources 后由视图回调 RefreshLinks 重画
            ModuleLinkRequested?.Invoke(consumer.Model);
        }

        private void OnPendingConnectionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CanvasPendingConnectionViewModel.IsVisible)
                && !PendingConnection.IsVisible)
            {
                PendingConnection.Source = null;
                PendingConnection.Target = null;
                ResetConnectableFeedback();
            }
        }

        /// <summary>断开一根聚合连线承载的全部端口绑定（连线右键菜单，整批一个撤销单元）</summary>
        private void UnbindAllBindings(CanvasLinkViewModel link)
        {
            if (_attachedFlow == null) return;

            // 运行锁：解绑是编辑动作，运行中拦下（三视角评审 P0-1）
            if (!EnsureEditable(ModuleEditAction.StructuralEdit)) return;

            var commands = new List<IUndoCommand>();
            foreach (var binding in link.Bindings)
            {
                if (binding.Consumer == null || binding.OriginalLink == null) continue;
                if (!_topology.TryGet(binding.Consumer.StepID, out _)) continue;

                binding.Consumer.RemoveLink(binding.Key);
                commands.Add(new DisconnectCommand(binding.Consumer, binding.Key, binding.OriginalLink));
            }

            if (commands.Count == 0) return;

            PushUndo(new MultiUndoCommand(commands));
            StatusHint = $"已断开 {commands.Count} 条绑定（Ctrl+Z 可整批撤销）";
            RebuildInPlace();
        }

        private void OnUnbindBinding(CanvasLinkBinding? binding)
        {
            if (binding?.Consumer == null || _attachedFlow == null) return;
            if (binding.OriginalLink == null) return;
            if (!_topology.TryGet(binding.Consumer.StepID, out _)) return;

            // 运行锁：解绑是编辑动作，运行中拦下（三视角评审 P0-1）
            if (!EnsureEditable(ModuleEditAction.StructuralEdit)) return;

            binding.Consumer.RemoveLink(binding.Key);
            PushUndo(new DisconnectCommand(binding.Consumer, binding.Key, binding.OriginalLink));
            StatusHint = $"已解绑「{binding.Display}」（Ctrl+Z 可撤销）";
            RebuildInPlace();
        }

        // ==================================================================
        //  折叠
        // ==================================================================

        private void OnToggleBreakpoint(CanvasNodeViewModel? node)
        {
            // 断点只落在普通步骤上：容器框（If/While/For）本轮不提供断点 UI
            // （数据模型已支持，口径留待后续轮）；泳道没有 Model，一并被这道闸门挡下。
            // IsBreakpoint 是 [RuntimeState]：不递增 Version、不落盘；圆点等绑定靠属性通知自行刷新，无需重画。
            if (node?.Model == null || node.Kind != CanvasNodeKind.Step) return;

            // 多选批量（评审清单 B 包交互补齐）：选中集 >1 时对每个选中的步骤节点执行，
            // 无多选维持单节点行为。断点同样不推版本、不给提示（与单节点口径一致）
            foreach (var target in CollectBatchTargets(node, stepNodesOnly: true))
                target.Model!.IsBreakpoint = !target.Model.IsBreakpoint;
        }

        private void OnToggleCollapse(CanvasNodeViewModel? node)
        {
            if (node?.Model == null || _attachedFlow == null) return;
            if (node.Kind != CanvasNodeKind.Container) return;

            bool collapsing = !node.IsCollapsed;
            _attachedFlow.Layout.ToggleCollapsed(node.StepId);

            // 用户意图覆盖"大流程自动折叠"：展开的容器从会话折叠集合里移出，
            // 否则下一次渲染又被 TryGetLayout 折回去（用户点了没反应）
            if (collapsing) _sessionCollapsed.Add(node.StepId);
            else _sessionCollapsed.Remove(node.StepId);

            // 展开/折叠改变框体占用的空间：整体重排一次，保证层级顺序始终可读——
            // 旧散乱坐标下的展开是"层级顺序乱了"的根源；手工微调可在重排后再做
            TidyLayoutCore();

            StatusHint = collapsing
                ? $"已折叠「{node.Model.StepName}」（共 {CountDescendants(node.Model)} 步）"
                : $"已展开「{node.Model.StepName}」";
            RebuildInPlace();
        }

        // ==================================================================
        //  禁用 / 删除
        // ==================================================================

        /// <summary>启用/禁用切换（节点右键菜单）。与流程栏同一写法：翻转 IsDisEnable，禁用语义由执行引擎解释。
        /// 运行锁：禁用/启用是编辑动作，运行中拦下（三视角评审 P0-1；豁免口径与流程栏 ToggleDisable 一致）。
        /// 多选批量（评审清单 B 包交互补齐）：选中集 &gt;1 时对每个选中的有模型节点执行（容器框也算——
        /// 单节点口径本就允许容器禁用），一次性给一条汇总回执</summary>
        private void OnToggleDisable(CanvasNodeViewModel? node)
        {
            if (node?.Model == null || node.IsDecorator) return;
            if (!EnsureEditable(ModuleEditAction.StructuralEdit)) return;

            var targets = CollectBatchTargets(node, stepNodesOnly: false);
            foreach (var target in targets)
            {
                // 禁用走语义 setter：FlowModel 版本链自动递增（与单节点同一条写路径）
                target.Model!.IsDisEnable = !target.Model.IsDisEnable;
                target.RefreshHeader();
            }

            if (targets.Count == 1)
            {
                var single = targets[0].Model!;
                StatusHint = single.IsDisEnable
                    ? $"已禁用「{single.StepName}」"
                    : $"已启用「{single.StepName}」";
                return;
            }

            // 批量：状态可混合（有的本来禁着），回执报清两边条数
            int disabled = targets.Count(t => t.Model!.IsDisEnable);
            StatusHint = $"已切换禁用状态 {targets.Count} 个模块（禁用 {disabled} / 启用 {targets.Count - disabled}）";
        }

        /// <summary>
        /// 批量禁用/断点的目标采集（评审清单 B 包交互补齐）：多选（选中集 &gt;1）时按
        /// DeleteSelectedNodesCore 的选中集遍历口径取目标——IsSelected、非装饰节点（泳道 Model=null 跳过）、
        /// 有模型；断点再过 Kind==Step 闸（容器框不给断点 UI 的单节点口径不变）。
        /// 无多选时维持单节点行为：返回右键命中的节点本身——菜单靶不依赖选中态，
        /// "没选中就右键"也要能切换（T3 单节点断言钉住的既有行为）。
        /// </summary>
        private List<CanvasNodeViewModel> CollectBatchTargets(CanvasNodeViewModel anchor, bool stepNodesOnly)
        {
            var selected = _nodeMap.Values
                .Where(n => n.IsSelected && !n.IsDecorator && n.Model != null
                            && (!stepNodesOnly || n.Kind == CanvasNodeKind.Step))
                .ToList();

            return selected.Count > 1 ? selected : new List<CanvasNodeViewModel> { anchor };
        }

        /// <summary>节点右键菜单「删除」：把该节点单选化后走统一删除</summary>
        private void OnDeleteNode(CanvasNodeViewModel node)
        {
            if (_attachedFlow == null || node.Model == null || node.IsDecorator) return;

            foreach (var other in _nodeMap.Values.Where(n => n.IsSelected && !ReferenceEquals(n, node)))
                other.IsSelected = false;
            node.IsSelected = true;

            OnDeleteSelection();
        }

        // ==================================================================
        //  复制 / 粘贴（2026-10-12 评审清单第三批；第一期单个节点）
        //  快照与重映射都在 FlowClipboard（Services 层纯计算），本类只管选中采集、
        //  落点判定、入图与撤销/版本推进——与拖入 DropToolAt 同一条纪律。
        // ==================================================================

        // ==================================================================
        //  重命名 / 编辑注释（2026-10-24）
        //
        //  画布此前缺这两个入口：同一个步骤在流程栏能改名字/注释、在画布不能。
        //  口径与流程栏完全一致——同一个 EasyDialog 文本输入、同一道结构编辑门禁；
        //  版本推进不用手写（StepName/Description 都在 FlowModel 的监听面里，改了就 Version++）。
        // ==================================================================

        private void BeginRenameNode(CanvasNodeViewModel node) => BeginEditText(node, rename: true);

        private void BeginEditCommentNode(CanvasNodeViewModel node) => BeginEditText(node, rename: false);

        private void BeginEditText(CanvasNodeViewModel node, bool rename)
        {
            if (_attachedFlow == null || node?.Model == null || node.IsDecorator) return;

            // 改名/改注释是结构编辑（口径同流程栏 ModuleCommandAction.Rename/EditComment）
            if (!EnsureEditable(ModuleEditAction.StructuralEdit)) return;

            _ = ApplyTextEditAsync(node.Model, rename);
        }

        /// <summary>
        /// 弹窗是异步的，这里 fire-and-forget 并把异常收在方法内落到状态栏：
        /// 画布是主编辑面，弹窗失败（无 Application 的断言宿主、Dispatcher 取不到）不该冒到 UI 线程。
        /// </summary>
        private async System.Threading.Tasks.Task ApplyTextEditAsync(StepModel model, bool rename)
        {
            try
            {
                var data = await EasyDialog.ShowTextInputAsync(
                    rename ? "步序重命名" : "注释重命名",
                    rename ? model.StepName : model.Description);

                if (!data.IsConfirmed) return;

                if (rename)
                    model.StepName = data.Value;
                else
                    model.Description = data.Value;

                StatusHint = rename ? $"已重命名为「{model.StepName}」" : "已更新注释";
            }
            catch (Exception ex)
            {
                StatusHint = (rename ? "重命名" : "编辑注释") + "失败：" + ex.Message;
            }
        }

        /// <summary>节点右键菜单「复制」：把该节点单选化后走统一复制</summary>
        private void OnCopyNode(CanvasNodeViewModel node)
        {
            if (_attachedFlow == null || node.Model == null || node.IsDecorator) return;

            foreach (var other in _nodeMap.Values.Where(n => n.IsSelected && !ReferenceEquals(n, node)))
                other.IsSelected = false;
            node.IsSelected = true;

            OnCopySelected(node);
        }

        /// <summary>复制入口的统一收口：先按参数/选中集锁定唯一目标，再交给 FlowClipboard 快照。
        /// 复制不改图纸、不受运行锁限制（与"看"同族；粘贴才是结构编辑）。</summary>
        private void OnCopySelected(object? parameter)
        {
            if (_attachedFlow == null) return;

            CanvasNodeViewModel? target = parameter as CanvasNodeViewModel;
            if (target == null)
            {
                // 快捷键路径：走既有选中机制（与 DeleteSelection 同一条采集口径）
                var selected = _nodeMap.Values
                    .Where(n => n.IsSelected && !n.IsDecorator && n.Model != null)
                    .ToList();

                if (selected.Count > 1)
                {
                    StatusHint = "复制暂只支持单个模块（多选批量复制后续版本提供）";
                    return;
                }

                target = selected.Count == 1 ? selected[0] : null;
            }

            if (target?.Model is not StepModel model) return;   // 泳道/空参/其它类型：空跑

            FlowClipboard.Copy(model);
            StatusHint = $"已复制「{model.StepName}」（Ctrl+V 或画布空白右键粘贴）";
        }

        /// <summary>粘贴：物化重映射后的子树按落点插入图纸。运行锁与拖入/删除同闸（结构编辑）；
        /// 剪贴板没货时静默空跑（菜单项常驻，不做成动态可用性）。</summary>
        private void OnPaste(object? parameter)
        {
            if (_attachedFlow == null) return;
            if (!FlowClipboard.HasPayload) return;

            // 运行锁：粘贴是结构编辑，运行中拦下（与拖入同一条三视角评审 P0-1 闸）
            if (!EnsureEditable(ModuleEditAction.StructuralEdit)) return;

            var step = FlowClipboard.Take(_attachedFlow);
            if (step == null)
            {
                StatusHint = "剪贴板内容无法识别，已忽略";
                return;
            }

            Point position = parameter is Point p ? p
                : LastPointerGraphPosition ?? new Point(0, double.MaxValue);   // 无落点信息 → 追加到顶层末尾

            var (owner, insertIndex, targetName, targetContainer) = ResolveInsertTarget(position);

            // 循环控制指令的落点闸门（与拖入同一条）：粘贴件整棵子树都要有循环归属
            var chain = StepFactory.ContainerChainOf(_attachedFlow.Steps, targetContainer);
            string? misplaced = StepFactory.FindMisplacedLoopControl(new[] { step }, chain);
            if (misplaced != null)
            {
                StatusHint = $"「{misplaced}」{StepFactory.LoopControlPlacementMessage}";
                return;
            }

            if (insertIndex < 0) insertIndex = 0;
            if (insertIndex > owner.Count) insertIndex = owner.Count;
            owner.Insert(insertIndex, step);

            // 撤销补口：粘贴与拖入同一条引用型 AddStepCommand（Undo=按引用移除，Redo=插回原位）
            PushUndo(new AddStepCommand(owner, step, insertIndex));

            // 结构变更：通知图纸重编译（口径同拖入）；集合 Add 已触发一次画布重建，这里幂等兜底
            _attachedFlow.Version++;

            // 落点防重叠：与拖入同一条（先渲染拿真实几何，再让后续兄弟腾位）
            RebuildInPlace();
            if (MakeRoomForInserted(owner, insertIndex))
                RebuildInPlace();

            StatusHint = $"已粘贴「{step.StepName}」到{targetName}";
        }

        /// <summary>Delete 键 / 右键菜单的统一删除入口：移除选中的模块节点（含容器子树）。
        /// 2026-10-11 撤销补口：删除作为引用型 DeleteStepCommand 压栈，不再清空撤销栈——
        /// LIFO 顺序下更早的改序命令只有在删除被撤销后才轮到，下标快照届时已恢复，
        /// 不存在错位回滚；外部（非命令路径）结构变化照旧清栈的保护由 OnFlowStepsChanged
        /// 的闸门承担（Add/Move 不塌陷下标不清、Remove/Replace/Reset 清）。</summary>
        private void OnDeleteSelection()
        {
            if (_attachedFlow == null) return;

            // 运行锁：删除是结构编辑，运行中拦下（三视角评审 P0-1）
            if (!EnsureEditable(ModuleEditAction.StructuralEdit)) return;

            var records = new List<DeleteStepCommand.DeleteEntry>();
            int removed = DeleteSelectedNodesCore(records);
            if (removed == 0) return;

            if (records.Count > 0)
                PushUndo(new DeleteStepCommand(records));

            StatusHint = $"已删除 {removed} 个模块（含子步骤）";
            RebuildInPlace();
        }

        /// <summary>把选中的模块（含容器子树）移出图纸。返回删除个数；
        /// 删除记录（owner + 原下标）写入 records 供撤销命令插回。期间抑制逐个 Remove 触发的重建</summary>
        private int DeleteSelectedNodesCore(List<DeleteStepCommand.DeleteEntry> records)
        {
            int removed = 0;
            var targets = _nodeMap.Values
                .Where(n => n.IsSelected && !n.IsDecorator && n.Model != null)
                .Select(n => n.Model!)
                .Where(m => _topology.TryGet(m.StepID, out _))
                .ToList();
            if (targets.Count == 0) return 0;

            // 原始下标必须先于任何删除统一记录：删除途中 IndexOf 会随前序删除移位
            foreach (var step in targets)
            {
                if (!_topology.TryGet(step.StepID, out var pos) || pos!.Owner == null) continue;

                int index = pos.Owner.IndexOf(step);
                if (index >= 0)
                    records.Add(new DeleteStepCommand.DeleteEntry(pos.Owner, step, index));
            }

            _rebuilding = true;
            try
            {
                // 按引用现查现删：不信任记录的下标（同批次前序删除会挤动同 owner 的下标）
                foreach (var record in records)
                {
                    int index = record.Owner.IndexOf(record.Step);
                    if (index >= 0)
                    {
                        record.Owner.RemoveAt(index);
                        removed++;
                    }
                }
            }
            finally
            {
                _rebuilding = false;
            }

            return removed;
        }

        // ==================================================================
        //  模块参数（画布双击节点 / 节点右键菜单）
        // ==================================================================

        /// <summary>
        /// 「模块参数」共享入口（画布双击节点 / 节点右键菜单共用）——与流程栏右键、断点命中窗
        /// 走同一个 StepParameterDialog.Open 分派（ActionStep→插件参数 / ConditionStep·ForStep→
        /// 条件编辑器 / ParallelStep→EasyDialog 并行属性面板；返回 false = 该靶没有参数面板）。
        ///
        /// 泳道没有 Model：空跑（与折叠/禁用/删除的守卫同口径）。
        /// dialogService 从 Prism 容器按需解析：视图那边也是这么拿的（FlowCanvasView.OnModuleLinkRequested）；
        /// headless 断言宿主没有容器，解析失败按"没有服务 = 打不开窗"静默空跑——这是纯 UI 动作，
        /// 不需要为它伪造失败路径。
        /// </summary>
        public void OpenModuleParameters(CanvasNodeViewModel? node)
        {
            var model = node?.Model;
            if (model == null) return;

            // 运行锁：模块参数仅"已暂停"放行、运行中拦下，口径与流程栏同一份豁免表（EditGate）
            if (!EnsureEditable(ModuleEditAction.ModuleParameters)) return;

            var dialogService = TryResolveDialogService();
            if (dialogService == null) return;

            if (!StepParameterDialog.Open(model, dialogService, _workspace))
                StatusHint = $"「{model.StepName}」没有模块参数面板";
        }

        /// <summary>按需解析宿主对话框服务；容器未初始化（断言宿主 / 设计器）返回 null</summary>
        private static IDialogService? TryResolveDialogService()
        {
            try
            {
                return ContainerLocator.Container?.Resolve<IDialogService>();
            }
            catch
            {
                // ContainerLocator 未初始化会抛：无宿主即无弹窗能力，调用方按"打不开"处理
                return null;
            }
        }

        // ==================================================================
        //  整理布局
        // ==================================================================

        /// <summary>
        /// 清空全部节点坐标后按正交规则重排。
        ///
        /// 折叠现场（每个容器的折叠标记）在**排位期**就交给布局器：清库重排后每个容器在
        /// FlowLayoutStore 眼里都是"新项"，若不声明最终态，深于 AutoCollapseDepth 的容器会按
        /// 自动折叠的 230×50 足迹参与父级行/列推进，而重排后它们要渲染成展开——框体远大于
        /// 父级预留：同级泳道互压、后续兄弟被吞进框内（2026-10-09 审查 #1，十层图式实测外层框
        /// 2092 高而排位只预留 700）。
        /// 因此这里把"重排前记录的折叠集"作为 collapseOverride 传进 AutoLayout：足迹与标记
        /// 同源（见 FlowLayoutStore.PlaceContainer），重排后不需要再逐个 SetCollapsed 恢复现场。
        /// </summary>
        private void TidyLayoutCore()
        {
            var all = new List<StepModel>();
            CollectSteps(_attachedFlow!.Steps, all);
            if (all.Count == 0) return;

            var collapsedIds = all
                .Where(s => _attachedFlow.Layout.Find(s.StepID)?.Collapsed == true)
                .Select(s => s.StepID)
                .ToHashSet();

            foreach (var step in all)
                _attachedFlow.Layout.Remove(step.StepID);

            // override 里没有的容器 = 本次应展开（含阈值内与阈值外的），
            // 排位足迹与最终折叠标记都由它一次定死，不再事后翻标记
            _attachedFlow.Layout.AutoLayout(
                _attachedFlow.Steps,
                collapseOverride: id => collapsedIds.Contains(id));
        }

        private void OnTidyLayout()
        {
            if (_attachedFlow == null) return;

            TidyLayoutCore();
            StatusHint = "已按执行顺序重新整理布局（可在「适应」后手动微调）";
            RebuildInPlace();
        }

        // ==================================================================
        //  展开 / 折叠全部（画布工具条，三视角评审第二批）
        //  只动布局库的折叠标记，不碰流程语义 → 与单个折叠钮同口径，不受运行锁限制；
        //  置完统一 RebuildInPlace（一帧重排，逐个 ToggleCollapsed + 每个都重建太浪费）
        // ==================================================================

        private void OnExpandAll() => SetAllContainersCollapsed(false);

        private void OnCollapseAll() => SetAllContainersCollapsed(true);

        /// <summary>遍历拓扑容器逐个置折叠标记，有变化才整体重排。
        /// 注意 SetCollapsed 的返回值是"置后的折叠态"而不是"是否变化"，变化判定要自己比对旧值</summary>
        private void SetAllContainersCollapsed(bool collapsed)
        {
            if (_attachedFlow == null) return;

            // 「展开/折叠全部」是显式意图，会话级自动折叠一律作废（否则展开全部没效果）
            _sessionCollapsed.Clear();

            bool changed = false;
            foreach (var container in CollectContainers(_attachedFlow.Steps))
            {
                if ((_attachedFlow.Layout.Find(container.StepID)?.Collapsed ?? false) == collapsed)
                    continue;

                _attachedFlow.Layout.SetCollapsed(container.StepID, collapsed);
                changed = true;
            }

            if (!changed)
            {
                // 落盘值已经一致，但会话折叠被清掉了 → 仍要重画一次
                RebuildInPlace();
                StatusHint = collapsed ? "已折叠全部容器" : "已展开全部容器";
                return;
            }

            RebuildInPlace();
            StatusHint = collapsed ? "已折叠全部容器" : "已展开全部容器";
        }

        /// <summary>
        /// 大流程首次挂接：把所有容器记为"会话折叠"，让首帧只画顶层。
        /// 只做一次（<see cref="_autoCollapsedFlows"/>），用户展开过的容器不会被再次折回去。
        /// </summary>
        private void TryAutoCollapseBigFlow(FlowModel flow)
        {
            if (flow?.Steps == null) return;
            if (!_autoCollapsedFlows.Add(flow)) return;   // 本流程已处理过

            int steps = CountAllSteps(flow.Steps);
            if (steps <= BigFlowAutoCollapseThreshold) return;

            int collapsed = 0;
            foreach (var container in CollectContainers(flow.Steps))
            {
                if (_sessionCollapsed.Add(container.StepID))
                    collapsed++;
            }

            if (collapsed > 0)
            {
                StatusHint = $"大流程（{steps} 步）：已自动折叠 {collapsed} 个容器，"
                    + "点击容器左上的折叠钮按需展开（也可用工具条「展开全部」）";
            }
        }

        /// <summary>全树步骤计数（含所有嵌套分支）</summary>
        private static int CountAllSteps(IEnumerable<StepModel> steps)
        {
            int count = 0;
            foreach (var step in steps)
            {
                if (step == null) continue;
                count++;
                if (step is IContainerStep container && container.Children != null)
                {
                    foreach (var branch in container.Children)
                    {
                        if (branch?.Steps != null) count += CountAllSteps(branch.Steps);
                    }
                }
            }
            return count;
        }

        /// <summary>收集全树的容器步骤（IContainerStep 口径，与拓扑/编译器同一判据）</summary>
        private static IEnumerable<StepModel> CollectContainers(ObservableCollection<StepModel> steps)
        {
            foreach (var step in steps)
            {
                if (step == null) continue;

                if (step is IContainerStep container && container.Children != null)
                {
                    yield return step;

                    foreach (var branch in container.Children)
                    {
                        if (branch?.Steps == null) continue;
                        foreach (var nested in CollectContainers(branch.Steps))
                            yield return nested;
                    }
                }
            }
        }

        // ==================================================================
        //  编译错误列表（三视角评审第二批）：ShellViewModel 编译失败时广播
        //  CompilationErrorsEvent，画布缓存错误，供工具条「错误列表」按钮
        //  打开列表、双击行定位到对应流程与步骤（切流程 + 按 StepId 找步骤模型 + 切选中，
        //  画布经既有联动选中并滚入，零新增定位通道）。
        //  评审清单 B 包交互升级：缓存从"最近一次整体替换"改为**按 FlowName 分组累计**——
        //  批量编译（DoCompileAll）对每个失败流程各发一次，用户要在一处看全所有流程的错误；
        //  同 FlowName 重新发布（重新编译该流程）替换该组。
        // ==================================================================

        // internal：CompileErrorGroup.Entries 要跨出去给断言宿主读（CS0053：成员类型不得比成员更封闭）
        internal sealed record CompileErrorEntry(string FlowName, CompilationError Error)
        {
            // 兜底显示：万一列表项没走显式绑定，也不能把 record 全名刷到屏幕上（R20 同族的病）
            public override string ToString() => Error.Message;
        }

        /// <summary>错误列表的按流程分组（评审清单 B 包交互升级）：每组头 = 流程名 + 条数。
        /// 组的先后 = 该流程错误首次出现的顺序，组内按发布顺序排列。VM 侧分组数据与弹窗
        /// 显示共用这一份，视图只排版不重组（headless 断言直接读 BuildCompileErrorGroups）。</summary>
        internal sealed class CompileErrorGroup
        {
            internal CompileErrorGroup(string flowName) { FlowName = flowName; }

            /// <summary>流程名（CompilationErrorsEvent.FlowName 快照）</summary>
            public string FlowName { get; }

            /// <summary>该流程的错误条目</summary>
            public List<CompileErrorEntry> Entries { get; } = new();
        }

        private readonly List<CompileErrorEntry> _compileErrors = new();

        /// <summary>缓存的编译错误条数（工具条「错误列表」按钮的可见性开关；分组累计口径）</summary>
        public int CompileErrorCount => _compileErrors.Count;

        /// <summary>错误列表按钮可见性：有错误才出现（不占工具条常驻位）</summary>
        public Visibility CompileErrorsVisibility
            => _compileErrors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        private void OnCompilationErrorsEvent(CompilationErrorsEvent @event)
        {
            string flowName = @event?.FlowName ?? string.Empty;

            // 按 FlowName 分组累计：同 FlowName 先整组摘掉（重新编译该流程 = 替换该组），
            // 再把本次错误追加回去；不同 FlowName 自然累积成新组
            _compileErrors.RemoveAll(entry => string.Equals(entry.FlowName, flowName, StringComparison.Ordinal));
            if (@event?.Errors != null)
            {
                foreach (var error in @event.Errors)
                    _compileErrors.Add(new CompileErrorEntry(flowName, error));
            }

            RaisePropertyChanged(nameof(CompileErrorCount));
            RaisePropertyChanged(nameof(CompileErrorsVisibility));
        }

        /// <summary>按流程分组（现算：错误量级是个位数到几十条，不必维护增量分组结构）。
        /// internal：断言宿主直调（弹窗本身在 headless 不开，分组数据结构就地可断）。</summary>
        internal List<CompileErrorGroup> BuildCompileErrorGroups()
        {
            var groups = new List<CompileErrorGroup>();
            foreach (var entry in _compileErrors)
            {
                var group = groups.Find(g => string.Equals(g.FlowName, entry.FlowName, StringComparison.Ordinal));
                if (group == null)
                {
                    group = new CompileErrorGroup(entry.FlowName);
                    groups.Add(group);
                }
                group.Entries.Add(entry);
            }
            return groups;
        }

        /// <summary>
        /// 「错误列表」弹窗：按流程分组列出编译错误（组头=流程名+条数；行=步骤/消息），双击行定位；
        /// 底部「复制全部」把多行文本写进系统剪贴板（评审清单 B 包交互升级）。
        /// EasyDialog 在 headless（无 Application）下走取消保护分支——断言宿主直接调
        /// BuildCompileErrorGroups / BuildCompileErrorsCopyText。
        /// </summary>
        private void OnShowCompileErrors()
        {
            if (_compileErrors.Count == 0) return;

            var list = new ListBox { MaxHeight = 320, MinWidth = 460 };
            foreach (var group in BuildCompileErrorGroups())
            {
                // 组头：流程名 + 条数（禁用态 = 不可点击，只做分组导航）
                list.Items.Add(new ListBoxItem
                {
                    IsEnabled = false,
                    Content = new TextBlock
                    {
                        Text = $"{group.FlowName}（{group.Entries.Count} 条）",
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 6, 0, 2),
                    },
                });
                foreach (var entry in group.Entries)
                {
                    var row = new ListBoxItem
                    {
                        Content = new TextBlock
                        {
                            Text = $"{entry.Error.StepName ?? "（流程级）"} · {entry.Error.Message}",
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            ToolTip = entry.Error.Message,
                        },
                        Tag = entry,
                    };
                    row.MouseLeftButtonDown += OnCompileErrorRowClick;
                    list.Items.Add(row);
                }
            }

            var copyButton = new Button
            {
                Content = "复制全部",
                MinWidth = 84,
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            copyButton.Click += (_, _) => CopyCompileErrorsToClipboard();

            var panel = new StackPanel();
            panel.Children.Add(list);
            panel.Children.Add(copyButton);
            panel.Children.Add(new TextBlock
            {
                Text = "双击一行定位到出错步骤（错误是编译时的快照，步骤随后可能已被删除或改名；定位失败时无动作）",
                FontSize = 11,
                Margin = new Thickness(0, 8, 0, 0),
                Opacity = 0.7,
                TextWrapping = TextWrapping.Wrap,
            });

            EasyDialog.ShowSync("编译错误列表", panel);
        }

        /// <summary>「复制全部」的文本口径：每组头一行（【流程】共 N 条）+ 每条一行（步骤 · 消息）。
        /// internal：断言宿主直调文本口径；真剪贴板写入在 headless 不测（弹窗本身标注不测）。</summary>
        internal string BuildCompileErrorsCopyText()
        {
            var lines = new List<string>();
            foreach (var group in BuildCompileErrorGroups())
            {
                lines.Add($"【{group.FlowName}】共 {group.Entries.Count} 条");
                foreach (var entry in group.Entries)
                    lines.Add($"{entry.Error.StepName ?? "（流程级）"} · {entry.Error.Message}");
            }
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>把错误文本写进系统剪贴板（照 GlobalVariableManagerViewModel 复制变量名的用法）。
        /// 无窗口站（headless）/剪贴板被其它进程长期占用时 Clipboard.SetText 会抛
        /// ExternalException——静默降级，不阻断弹窗的其余交互。</summary>
        internal void CopyCompileErrorsToClipboard()
        {
            try
            {
                Clipboard.SetText(BuildCompileErrorsCopyText());
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // headless 无剪贴板：静默降级
            }
        }

        private void OnCompileErrorRowClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2) return;
            if (sender is ListBoxItem { Tag: CompileErrorEntry entry })
                LocateCompileError(entry.FlowName, entry.Error.StepId);
        }

        /// <summary>按流程名切流程、按 StepId 找步骤模型并切选中（internal：断言宿主直调）。
        /// 找不到（流程已删 / 步骤已删）静默返回——编译错误是快照，图纸随后可能已变。</summary>
        internal void LocateCompileError(string flowName, Guid? stepId)
        {
            var flow = _workspace.CurrentSolution?.Flows.FirstOrDefault(f => f.FlowName == flowName);
            if (flow == null) return;

            if (!ReferenceEquals(_workspace.CurrentFlow, flow))
                _workspace.SwitchFlow(flow);

            if (stepId is not { } id || id == Guid.Empty) return;

            var step = FindStepById(flow.Steps, id);
            if (step == null) return;

            try
            {
                _workspace.SwitchStep(step);
            }
            catch (InvalidOperationException)
            {
                // 图纸状态边缘：静默放弃
            }
        }

        /// <summary>按 StepId 在流程树里递归找步骤模型（FlowModel 没有现成的按 Id 查找，画布侧私有递归）</summary>
        private static StepModel? FindStepById(ObservableCollection<StepModel> steps, Guid id)
        {
            foreach (var step in steps)
            {
                if (step == null) continue;

                if (step.StepID == id) return step;

                if (step is IContainerStep container && container.Children != null)
                {
                    foreach (var branch in container.Children)
                    {
                        if (branch?.Steps == null) continue;
                        var found = FindStepById(branch.Steps, id);
                        if (found != null) return found;
                    }
                }
            }

            return null;
        }

        // ==================================================================
        //  缩放
        // ==================================================================

        private void OnZoomCommand(FlowCanvasZoomCommand command)
            => ZoomCommandRequested?.Invoke(command);

        // ==================================================================
        //  拖拽改序 / 跨分支移动
        //
        //  几何规则（扁平画布版）：
        //  · 落点判定：被拖节点中心落在哪条泳道矩形内 → 目标分支；
        //    不在任何泳道/框内 → 顶层列表；落在框内但不在泳道上 → 无效（不提交）。
        //  · 改序：同一 Owner 内按中心 Y 升序算新下标，与原下标不同则提交 Move。
        //  · 折叠子树里的节点不可拖（根本没渲染），自然不参与。
        // ==================================================================

        private static List<CanvasNodeViewModel> ExtractDraggedNodes(object? parameter)
        {
            var result = new List<CanvasNodeViewModel>();
            if (parameter is not System.Collections.IEnumerable enumerable) return result;

            if (parameter is not System.Collections.IList && parameter is not CanvasNodeViewModel)
            {
                var dc = parameter.GetType().GetProperty("DataContext")?.GetValue(parameter);
                if (dc is CanvasNodeViewModel single && single.Kind != CanvasNodeKind.Lane)
                    result.Add(single);
                if (dc != null) return result;
            }

            foreach (var item in enumerable)
            {
                if (item == null) continue;
                var dc = item.GetType().GetProperty("DataContext")?.GetValue(item);
                if (dc is CanvasNodeViewModel cvm && cvm.Kind != CanvasNodeKind.Lane)
                    result.Add(cvm);
            }
            return result;
        }

        // ------------------------------------------------------------------
        //  拖动浮层（纯视觉）：画布内拖动节点时跟随指针的迷你卡片
        //
        //  与工具箱、流程栏共用 VisionMaster\Controls\DragGhostCard（设计基线 v2）。
        //  Nodify 没有公开的拖动开始事件（只有 ItemsDragStartedCommand），所以状态由
        //  OnItemsDragStarted / OnItemsDragCompleted 发布；视图只负责按鼠标位置挪 Popup，
        //  这里不参与改序/跨分支的任何判定。
        // ------------------------------------------------------------------

        /// <summary>拖动浮层是否可见（视图的 Popup IsOpen）</summary>
        public bool IsDragGhostVisible
        {
            get => field;
            private set => SetProperty(ref field, value);
        }

        /// <summary>浮层主标题（主拖动节点名）</summary>
        public string? DragGhostTitle
        {
            get => field;
            private set => SetProperty(ref field, value);
        }

        /// <summary>浮层副标题（分类名；多选时为"共 N 个模块"）</summary>
        public string? DragGhostCategory
        {
            get => field;
            private set => SetProperty(ref field, value);
        }

        /// <summary>浮层图标码点</summary>
        public string? DragGhostIcon
        {
            get => field;
            private set => SetProperty(ref field, value);
        }

        private void ShowDragGhost(IReadOnlyList<DragSnapshot> snapshot)
        {
            var primary = snapshot.Count > 0 ? snapshot[0].Node.Model : null;
            if (primary == null)
            {
                HideDragGhost();
                return;
            }

            DragGhostIcon = primary.Icon;
            DragGhostTitle = primary.StepName;
            DragGhostCategory = snapshot.Count > 1 ? $"共 {snapshot.Count} 个模块" : primary.Category;
            IsDragGhostVisible = true;
        }

        private void HideDragGhost()
        {
            if (!IsDragGhostVisible) return;
            IsDragGhostVisible = false;
        }

        private void OnItemsDragStarted(object? parameter)
        {
            _dragStartedSnapshot = null;
            HideDragGuides();   // 上一次手势若没走到 Completed（异常路径），入场先摘干净
            HideDragGhost();

            if (_attachedFlow == null) return;

            // Nodify 7.3 取证（NodifyEditor::OnItemsDragStarted/Completed 的 IL：Execute(get_DataContext)）：
            // 命令实参是编辑器自身的 DataContext（= 本 VM），不是被拖容器列表——
            // 只靠参数提取会永远拿到空快照，拖拽改序/跨分支提交随之断链。
            // 主路径改用选中集：Nodify 在拖动手势开始前已把被拖容器置选中（框选多拖时全体已选中）；
            // 参数提取保留，兼容测试直喂列表的旧形态。
            var dragged = ExtractDraggedNodes(parameter);
            if (dragged.Count == 0)
                dragged = _nodeMap.Values.Where(n => n.IsSelected && n.IsDraggable).ToList();

            if (dragged.Count == 0) return;

            _dragStartedSnapshot = new List<DragSnapshot>(dragged.Count);
            foreach (var node in dragged)
            {
                if (node.Model == null) continue;
                if (!_topology.TryGet(node.StepId, out var pos)) continue;

                _dragStartedSnapshot.Add(new DragSnapshot(
                    node, pos!.Owner, pos.IndexInOwner, node.Location));
            }

            // 对齐参考线（纯视觉附加）：盯主拖动节点的 Location（与 OnItemsDragCompleted
            // 的 primary 同一取法），拖动期间实时算与其它节点同名边的对齐
            HideDragGuides();
            if (_dragStartedSnapshot is { Count: > 0 } snapshot)
            {
                _guideDragNode = snapshot[0].Node;
                _guideDragNode.PropertyChanged += OnDragGuideNodeMoved;
                UpdateDragGuides();

                // 拖动浮层（纯视觉）：卡片内容取主拖动节点；多选时副标题显示数量
                ShowDragGhost(snapshot);
            }
        }

        private void OnItemsDragCompleted(object? parameter)
        {
            // 参考线/浮层是拖动期视觉，抬手即收——放在一切早退之前（含"被运行锁拦下"的路径）
            HideDragGuides();
            HideDragGhost();

            var snapshot = _dragStartedSnapshot;
            _dragStartedSnapshot = null;
            if (snapshot == null || snapshot.Count == 0 || _attachedFlow == null) return;

            // 运行锁：拖拽改序 / 跨分支移动是结构编辑，运行中拦下（快照已清，本次拖动只挪坐标）
            if (!EnsureEditable(ModuleEditAction.StructuralEdit)) return;

            var primary = snapshot[0];
            var primaryNode = primary.Node;
            if (primaryNode.Model == null) return;

            // 已知取舍（刻意）：多选拖动的语义提交只认首个节点（改序/换分支按它的几何落点算），
            // 其余被拖节点只随拖动挪坐标。整批换 owner 的插入顺序有歧义，留待后续轮设计。

            double centerX = primaryNode.Location.X + NodeWidth / 2;
            double centerY = primaryNode.Location.Y + NodeHeight / 2;

            // 本次拖动可能以 Move/Reorder 收尾——集合写回是画布自己的命令做的，
            // OnFlowStepsChanged 不能把它当外部变化清掉刚压栈的命令
            _internalWriteback++;
            try
            {
                // 1. 落点所属集合：泳道 > 无（框内非泳道） > 顶层
                var targetOwner = ResolveOwnerByPoint(centerX, centerY);

                if (targetOwner != null && !ReferenceEquals(targetOwner, primary.Owner))
                {
                    int toIndex = ComputeInsertIndexByY(targetOwner, centerY, primaryNode.Model);
                    int actualToIndex = Math.Min(toIndex, targetOwner.Count);

                    var cmd = new MoveBranchCommand(
                        primaryNode.Model,
                        primary.Owner, primary.OriginalIndex,
                        targetOwner, actualToIndex);
                    cmd.Redo();
                    PushUndo(cmd);
                    return;
                }

                // 2. 改序：原 Owner 内按中心 Y 算新下标
                int newIndex = ComputeInsertIndexByY(primary.Owner, centerY, primaryNode.Model);
                if (newIndex != primary.OriginalIndex
                    && newIndex >= 0 && newIndex < primary.Owner.Count)
                {
                    var cmd = new ReorderCommand(primary.Owner, primary.OriginalIndex, newIndex);
                    cmd.Redo();
                    PushUndo(cmd);
                }

                // 否则：只坐标移动，不入栈
            }
            finally
            {
                _internalWriteback--;
            }

            // 拖拽收尾：容器框/泳道几何按内容新位置贴合（拖动中不实时重算是已知取舍，抬手一次重建）；
            // 提交路径里集合变化已经触发过一次重建，这里幂等
            RebuildInPlace();
        }

        /// <summary>
        /// 拖拽落点提示（设计基线 docs/UI设计基线/drag-drop-hint v2）：只描述"会落到哪"，不改图纸。
        ///
        /// 判据与 <see cref="DropToolAt"/> 完全共用（ResolveInsertTarget + AllowsLoopControl），
        /// 不写第二套规则——否则提示与真实落点会漂移，比没有提示更坏。
        ///
        /// 四档：运行锁 → Locked；Break/Continue 落在循环外 → Illegal；
        /// 落点在折叠容器内 → Caution（放下会展开）；其余 → Valid。
        /// </summary>
        public (string Text, DragHintKind Kind) DescribeDropTarget(ToolItemModel tool, Point position)
        {
            if (_attachedFlow == null || tool == null)
                return (string.Empty, DragHintKind.Hidden);

            // 运行锁：此刻放哪儿都不许放，落点细节没有意义
            if (!_editGate.IsEditable(ModuleEditAction.StructuralEdit))
                return (DragHintText.Locked, DragHintKind.Locked);

            var (_, insertIndex, _, targetContainer) = ResolveInsertTarget(position);

            // 循环控制指令的落点闸门（与 DropToolAt 同一判据、同一文案常量）
            if (!StepFactory.AllowsLoopControl(
                    tool.ModuleTypeName,
                    StepFactory.ContainerChainOf(_attachedFlow.Steps, targetContainer)))
            {
                return (DragHintText.Illegal(StepFactory.LoopControlPlacementMessage), DragHintKind.Illegal);
            }

            // 容器内：路径 = 容器链（由远及近）+ 第一条分支名
            if (targetContainer is IContainerStep container)
            {
                var segments = StepFactory.ContainerChainOf(_attachedFlow.Steps, targetContainer)
                    .Reverse()
                    .Select(s => s.StepName)
                    .Cast<string?>()
                    .ToList();

                segments.Add(container.Children?.FirstOrDefault()?.StepName);
                string containerPath = DragHintText.Path(segments);

                // 折叠的容器：放下会先展开它（状态③）。
                // 判据必须与 TryGetLayout 完全一致——展开态 = 存储的 Collapsed 与"大流程自动折叠"
                // 两个来源都不成立；只看 Layout 会漏掉自动折叠的容器（提示说有效、落下却看不见）。
                bool collapsed = _attachedFlow.Layout.Find(targetContainer.StepID)?.Collapsed == true
                                 || _sessionCollapsed.Contains(targetContainer.StepID);

                return collapsed
                    ? (DragHintText.WillExpand(targetContainer.StepName), DragHintKind.Caution)
                    : (DragHintText.Add(tool.Name, containerPath), DragHintKind.Valid);
            }

            // 顶层：插到下一个兄弟之前，或者追加到末尾。
            // 集合里可能有 null 项（反序列化/外部改集合的残留），跳过它们再取名——
            // 否则提示这一行会抛 NRE，把拖拽整个打断。
            if (insertIndex >= 0 && insertIndex < _attachedFlow.Steps.Count)
            {
                var next = _attachedFlow.Steps.Skip(insertIndex).FirstOrDefault(s => s != null);
                if (next != null)
                {
                    return (DragHintText.Add(tool.Name, DragHintText.TopLevelPath,
                        DragHintText.InsertBeforePosition(next.StepName)), DragHintKind.Valid);
                }
            }

            return (DragHintText.Add(tool.Name, DragHintText.TopLevelPath), DragHintKind.Valid);
        }

        /// <summary>
        /// 按落点找目标步骤集合：最内层包含该点的泳道优先（取面积最小者）；
        /// 点在某个展开容器框内但不在任何泳道上 → null（框内只有泳道可接收）；
        /// 点在一切框外 → 顶层列表。
        /// </summary>
        private ObservableCollection<StepModel>? ResolveOwnerByPoint(double x, double y)
        {
            CanvasNodeViewModel? bestLane = null;
            double bestArea = double.MaxValue;

            foreach (var lane in _lanes)
            {
                if (lane.Branch?.Steps == null) continue;

                var rect = new Rect(lane.Location.X, lane.Location.Y, lane.LaneWidth, lane.LaneHeight);
                if (!rect.Contains(x, y)) continue;

                double area = rect.Width * rect.Height;
                if (area < bestArea)
                {
                    bestArea = area;
                    bestLane = lane;
                }
            }

            if (bestLane != null) return bestLane.Branch!.Steps;

            foreach (var frame in _frameRects)
            {
                if (frame.Value.Contains(x, y)) return null;
            }

            return _attachedFlow?.Steps;
        }

        // ==================================================================
        //  分支结构动作（画布节点右键）
        //
        //  2026-10-23 补：此前"加分支"只能回流程栏点，画布节点右键根本没有这类项。
        //  结构改动一律走 FlowBranchEditor（与流程栏同一份实现），本类只负责：
        //   ① 运行锁（结构编辑，与拖入/删除/粘贴同一道门禁）；
        //   ② 回执落到画布状态栏（画布是主编辑面，不该把视线拽回流程栏）；
        //   ③ 重画——Children 变化已经由 OnFlowStepsChanged 触发一次，这里幂等兜底。
        // ==================================================================

        /// <summary>画布右键菜单要加的分支种类</summary>
        private enum FlowBranchKind
        {
            Parallel,
            ElseIf,
            Else,
            Case,
        }

        private void OnAddBranch(CanvasNodeViewModel node, FlowBranchKind kind)
        {
            if (_attachedFlow == null || node.Model is not StepModel model) return;

            // 运行锁：分支增删是结构编辑（口径同流程栏 AddParallelBranchCommand）
            if (!EnsureEditable(ModuleEditAction.StructuralEdit)) return;

            var result = kind switch
            {
                FlowBranchKind.Parallel => FlowBranchEditor.AddParallelBranch(model as ParallelStep),
                FlowBranchKind.ElseIf => FlowBranchEditor.AddElseIfBranch(model as ConditionStep),
                FlowBranchKind.Else => FlowBranchEditor.AddElseBranch(model as ConditionStep),
                FlowBranchKind.Case => FlowBranchEditor.AddCaseBranch(model as CaseStep),
                _ => FlowBranchEditor.Result.Rejected(string.Empty),
            };

            // 类型不对（菜单项本不该出现）静默空跑；业务拒绝（上限/已存在）给文案
            if (result.Message.Length > 0)
                StatusHint = result.Message;

            // Children 变化已经触发过一次重画；这里幂等兜底，保证"加完立刻能看见"
            if (result.Success)
                RebuildInPlace();
        }

        // ==================================================================
        //  工具箱拖入（画布 = 主编辑面）
        // ==================================================================

        /// <summary>
        /// 按画布坐标找**最深**的容器框（点在哪个框里就取哪个；嵌套时取内层）。
        /// 判据用渲染期算好的框体矩形（_frameRects，含折叠态框）——泳道不算落点，
        /// 落进容器的步骤一律进第一条分支（与流程栏 Drop 的"落进容器 = 第一条分支末尾"同口径）。
        /// </summary>
        public CanvasNodeViewModel? FindDeepestContainerAt(Point position)
        {
            CanvasNodeViewModel? best = null;
            double bestArea = double.MaxValue;

            foreach (var node in _steps)
            {
                if (node.Kind != CanvasNodeKind.Container) continue;
                if (!_frameRects.TryGetValue(node.StepId, out var rect)) continue;
                if (!rect.Contains(position)) continue;

                // 深度优先；同深取面积更小者（框体重叠只有"祖先—子孙"一种情形，深度已够，
                // 面积是并列时的确定性兜底）
                double area = rect.Width * rect.Height;
                if (best == null || node.Depth > best.Depth
                    || (node.Depth == best.Depth && area < bestArea))
                {
                    best = node;
                    bestArea = area;
                }
            }

            return best;
        }

        /// <summary>
        /// 画布拖放：把工具箱模板造出的步骤插到落点（ToolItemModel → 模型走 StepFactory，
        /// 与流程栏 Drop 同一份工厂）。
        /// 落点规则与粘贴共用（见 <see cref="ResolveInsertTarget"/>）；
        /// 结构变更通知图纸（Version++，触发重编译）。
        /// </summary>
        public bool DropToolAt(ToolItemModel tool, Point position)
        {
            if (_attachedFlow == null || tool == null) return false;

            // 运行锁：拖入是结构编辑，运行中拦下（三视角评审 P0-1）
            if (!EnsureEditable(ModuleEditAction.StructuralEdit)) return false;

            var (owner, insertIndex, targetName, targetContainer) = ResolveInsertTarget(position);

            // 循环控制指令的落点闸门：Break/Continue 只能进 For/While（顶层与并行分支里
            // 运行期会忽略它们——允许落子等于给"看着配好了、其实没生效"的假绿）。
            if (!StepFactory.AllowsLoopControl(
                    tool.ModuleTypeName,
                    StepFactory.ContainerChainOf(_attachedFlow.Steps, targetContainer)))
            {
                StatusHint = $"「{tool.Name}」{StepFactory.LoopControlPlacementMessage}";
                return false;
            }

            string stepName = StepFactory.NextStepName(_attachedFlow, tool);
            var step = StepFactory.CreateFromTool(tool, stepName);

            if (insertIndex < 0) insertIndex = 0;
            if (insertIndex > owner.Count) insertIndex = owner.Count;
            owner.Insert(insertIndex, step);

            // 状态③的兑现：落点在折叠容器里时，提示说的是"会展开后再放入"，这里必须真的展开。
            // 只提示不展开 = 新步骤插进去了但看不见（用户以为没加成功），比不给提示更坏。
            bool expanded = ExpandIfCollapsed(targetContainer);

            // 撤销补口（2026-10-11）：拖入也入撤销栈——引用型 AddStepCommand（Undo=按引用移除，
            // Redo=按记录下标插回；owner 内容被外部动过时按引用判存在性静默降级）
            PushUndo(new AddStepCommand(owner, step, insertIndex));

            // 结构变更：通知图纸重编译（口径同流程栏 Drop）
            _attachedFlow.Version++;

            // 落点防重叠（2026-10-23）：先保证渲染过一帧——AutoLayout 给新步骤补坐标、
            // 容器算出真实框高，这之后才量得出"新节点占了多高"。不补这一帧就腾位，
            // 新插入的容器会被当成一行高度，兄弟让位的距离不够，照样叠。
            // （owner.Insert 的集合事件通常已经重建过一次，这里只兜"事件被 _rebuilding 挡掉"的路径）
            RebuildInPlace();

            if (MakeRoomForInserted(owner, insertIndex))
                RebuildInPlace();

            StatusHint = expanded
                ? $"已添加「{stepName}」到{targetName}（已展开折叠的分组）"
                : $"已添加「{stepName}」到{targetName}";
            return true;
        }

        /// <summary>
        /// 把落点所在的折叠容器展开（状态③的兑现）。两个折叠来源都要清：
        /// 存储的 <c>Layout.Collapsed</c>（用户手动折叠）与 <c>_sessionCollapsed</c>
        /// （大流程自动折叠），漏一个就会出现"提示说要展开、落下去还是折叠着"。
        /// 返回是否真的展开过（供状态栏文案区分）。
        /// </summary>
        private bool ExpandIfCollapsed(StepModel? container)
        {
            if (container == null || _attachedFlow == null) return false;

            bool wasCollapsed = _attachedFlow.Layout.Find(container.StepID)?.Collapsed == true
                                || _sessionCollapsed.Contains(container.StepID);

            if (!wasCollapsed) return false;

            _attachedFlow.Layout.ToggleCollapsed(container.StepID);
            _sessionCollapsed.Remove(container.StepID);
            return true;
        }

        /// <summary>
        /// 落点判定（画布拖入与粘贴共用一条规则，与流程栏 Drop 的可见结果对齐）：
        ///  · 容器框内（取最深）→ 该容器第一条分支末尾；
        ///  · 其余 → 顶层：插入下标 = 顶层步骤里"节点中心 Y 小于落点 Y"的条数（画布坐标）。
        /// <paramref name="TargetContainer"/> = 落点所在容器（顶层为 null），
        /// 供 Break/Continue 的循环归属闸门算容器链。
        /// </summary>
        private (ObservableCollection<StepModel> Owner, int InsertIndex, string TargetName, StepModel? TargetContainer) ResolveInsertTarget(Point position)
        {
            var container = FindDeepestContainerAt(position);

            // 畸形容器（模型层构造保证不会出现）没有分支可落：退化按顶层插入，不让落点"无声失败"
            if (container?.Model is IContainerStep target && target.Children is { Count: > 0 })
                return (target.Children[0].Steps, target.Children[0].Steps.Count, $"「{container.Model.StepName}」", container.Model);

            return (_attachedFlow!.Steps, ComputeInsertIndexByY(_attachedFlow.Steps, position.Y, null), "主流程", null);
        }

        // ==================================================================
        //  落点防重叠（2026-10-23）
        //
        //  症状：新算子插到两个已有算子中间后，卡片正好盖在下一个兄弟身上，
        //  看上去像"只加了一个算子"（用户口径）。
        //
        //  根因不是插入位置，而是坐标：FlowLayoutStore.AutoLayout 只补缺项、
        //  既有坐标一律不动（用户手工布局不能被冲掉，这条不能改）。它给新项算的
        //  位置是"上一项实际底缘 + RowGap"，而下一项还站在自己的旧 Y 上——旧布局
        //  本来就是按同一套间距排的，于是两个 Y 恰好相等，严丝合缝地重叠。
        //
        //  修法：先让 AutoLayout 把新步骤补上（渲染一帧拿真实几何），再把后续兄弟
        //  连同各自子树整体下移新节点占的那一段。只"腾位"不"重排"：用户手拖出来的
        //  间距原样保留，本来就有空隙时一个坐标都不动。
        //  为什么必须等一帧：新插入的容器高度由它的子孙外接框算出，插入前算不出来。
        // ==================================================================

        /// <summary>
        /// 新步骤插到 <paramref name="insertIndex"/> 并渲染一帧后，把后面的兄弟连同子树顺移，
        /// 保证新节点占的纵向空间不被占用。返回是否真的动过坐标（调用方据此决定要不要再渲染一帧）。
        /// </summary>
        private bool MakeRoomForInserted(ObservableCollection<StepModel> owner, int insertIndex)
        {
            if (insertIndex < 0 || insertIndex >= owner.Count - 1) return false;   // 插在末尾：后面没人要让位

            var inserted = owner[insertIndex];
            if (inserted == null) return false;

            if (!TryGetVerticalSpan(inserted, out double insertedTop, out double insertedHeight)) return false;

            // 紧邻的下一项（跳过空项）；它的顶边就是需要让到的位置
            StepModel? next = null;
            for (int i = insertIndex + 1; i < owner.Count; i++)
            {
                if (owner[i] != null) { next = owner[i]; break; }
            }

            if (next == null || !TryGetVerticalSpan(next, out double nextTop, out _)) return false;

            double required = insertedTop + insertedHeight + FlowLayoutStore.RowGap;
            double delta = required - nextTop;
            if (delta <= 0.01) return false;   // 已有空隙：尊重用户手工布局，不硬拉

            for (int i = insertIndex + 1; i < owner.Count; i++)
            {
                if (owner[i] != null)
                    ShiftSubtree(owner[i], delta);
            }

            return true;
        }

        /// <summary>
        /// 取一个步骤在画布上的纵向占位（顶边 + 高度）。
        /// 容器用框体高度（折叠时只剩头带），与兄弟的行推进口径一致。
        /// 不在 _nodeMap（被折叠祖先藏起来）时：折叠容器按折叠框高算得准，**展开容器算不准**——
        /// 它的框高由子孙外接框决定，没渲染就没有这个数。此时宁可返回 false 放弃腾位，
        /// 也不拿一个假高度去算 delta：算小了展开后照样叠，算大了把兄弟推开一个大空档。
        /// </summary>
        private bool TryGetVerticalSpan(StepModel step, out double top, out double height)
        {
            top = 0;
            height = NodeHeight;

            if (_nodeMap.TryGetValue(step.StepID, out var node))
            {
                top = node.Location.Y;
                height = node.Kind == CanvasNodeKind.Container ? node.LaneHeight : NodeHeight;
                return true;
            }

            var layout = _attachedFlow?.Layout.Find(step.StepID);
            if (layout == null) return false;

            // 隐藏的展开容器：高度未知，放弃（"不动"是保守且无害的语义）
            if (step is IContainerStep && !layout.Collapsed) return false;

            top = layout.Y;
            if (step is IContainerStep)
                height = CollapsedFrameHeight;

            return true;
        }

        /// <summary>整棵子树下移 <paramref name="delta"/>（容器的框体由内容算出，只挪它自己不动子孙等于没挪）</summary>
        private void ShiftSubtree(StepModel step, double delta)
        {
            ShiftStep(step, delta);

            if (step is not IContainerStep container || container.Children == null) return;

            foreach (var branch in container.Children)
            {
                if (branch?.Steps == null) continue;
                foreach (var child in branch.Steps)
                {
                    if (child != null) ShiftSubtree(child, delta);
                }
            }
        }

        /// <summary>单个步骤的坐标下移。只写已有布局项——缺项的留给 AutoLayout 补，别写半份</summary>
        private void ShiftStep(StepModel step, double delta)
        {
            var layout = _attachedFlow?.Layout.Find(step.StepID);
            if (layout == null) return;

            _attachedFlow!.Layout.Set(step.StepID, layout.X, layout.Y + delta, layout.Collapsed);
        }

        /// <summary>在 owner 内按中心 Y 升序排（含被拖节点），算出被拖节点应处的下标</summary>
        private int ComputeInsertIndexByY(
            ObservableCollection<StepModel> owner,
            double draggedCenterY,
            StepModel? draggedStep)
        {
            int count = 0;
            foreach (var s in owner)
            {
                if (s == null) continue;
                if (ReferenceEquals(s, draggedStep)) continue;
                if (IsHiddenByCollapse(s)) continue;

                if (_nodeMap.TryGetValue(s.StepID, out var node))
                {
                    double nodeH = node.Kind == CanvasNodeKind.Container && node.IsCollapsed
                        ? CollapsedFrameHeight
                        : node.Kind == CanvasNodeKind.Container ? node.LaneHeight : NodeHeight;

                    double otherY = node.Location.Y + nodeH / 2;
                    if (otherY < draggedCenterY)
                        count++;
                }
            }
            return count;
        }

        // ==================================================================
        //  拖动对齐参考线（评审清单 B 包，纯视觉附加——不改拖动改序/多选拖动的任何判定）
        //
        //  拖动期间盯主拖动节点的 Location：它的左/右/水平中心与其它节点**同名边**
        //  差 ≤ GuideSnapThreshold → 出竖向参考线；上/下/垂直中心对齐 → 横向参考线。
        //  多条命中取差值最小的一条（并列取先遇到的那条）。
        //  视图侧由 Adorner 把图坐标换算成屏幕坐标画 1px 虚线（GuideVisibility 控制浮层）。
        // ==================================================================

        /// <summary>对齐判定半径（图坐标 px）：主节点边缘与其它节点同名边差 ≤ 此值即吸附出线</summary>
        private const double GuideSnapThreshold = 6;

        /// <summary>竖向参考线的 X（图坐标）；无对齐时 null（视图隐藏该线）</summary>
        public double? GuideX
        {
            get => field;
            private set => SetProperty(ref field, value);
        }

        /// <summary>横向参考线的 Y（图坐标）；无对齐时 null</summary>
        public double? GuideY
        {
            get => field;
            private set => SetProperty(ref field, value);
        }

        /// <summary>参考线浮层可见性（视图 Adorner 消费；任一条在场即可见）</summary>
        public Visibility GuideVisibility
            => GuideX != null || GuideY != null ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>本手势的主拖动节点（订阅它的 Location；Completed/Detach 摘除）</summary>
        private CanvasNodeViewModel? _guideDragNode;

        private void OnDragGuideNodeMoved(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(CanvasNodeViewModel.Location)) return;
            UpdateDragGuides();
        }

        /// <summary>按当前几何重算两条参考线。候选 = 渲染中的真实节点（步骤+容器框），
        /// 排除被拖节点自己与同批被拖的伙伴（它们跟着一起动，与自己比没有意义）</summary>
        private void UpdateDragGuides()
        {
            var primary = _guideDragNode;
            if (primary == null) return;

            double? bestX = null, bestY = null;
            double bestXDiff = double.MaxValue, bestYDiff = double.MaxValue;

            double pw = NodeSpanWidth(primary);
            double ph = primary.Kind == CanvasNodeKind.Container ? primary.LaneHeight : NodeHeight;
            double pL = primary.Location.X, pCX = pL + pw / 2, pR = pL + pw;
            double pT = primary.Location.Y, pCY = pT + ph / 2, pB = pT + ph;

            foreach (var other in _nodeMap.Values)
            {
                if (ReferenceEquals(other, primary)) continue;
                if (_dragStartedSnapshot?.Any(s => ReferenceEquals(s.Node, other)) == true) continue;

                double ow = NodeSpanWidth(other);
                double oh = other.Kind == CanvasNodeKind.Container ? other.LaneHeight : NodeHeight;
                double oL = other.Location.X, oCX = oL + ow / 2, oR = oL + ow;
                double oT = other.Location.Y, oCY = oT + oh / 2, oB = oT + oh;

                TrySnapGuide(pL, oL, oL, ref bestX, ref bestXDiff);
                TrySnapGuide(pR, oR, oR, ref bestX, ref bestXDiff);
                TrySnapGuide(pCX, oCX, oCX, ref bestX, ref bestXDiff);
                TrySnapGuide(pT, oT, oT, ref bestY, ref bestYDiff);
                TrySnapGuide(pB, oB, oB, ref bestY, ref bestYDiff);
                TrySnapGuide(pCY, oCY, oCY, ref bestY, ref bestYDiff);
            }

            GuideX = bestX;
            GuideY = bestY;
            RaisePropertyChanged(nameof(GuideVisibility));
        }

        /// <summary>同名边对齐判定：差在半径内且比已命中者更近 → 记下（并列保持先到的那条）</summary>
        private static void TrySnapGuide(double primaryEdge, double otherEdge, double guidePos,
            ref double? best, ref double bestDiff)
        {
            double diff = Math.Abs(primaryEdge - otherEdge);
            if (diff > GuideSnapThreshold || diff >= bestDiff) return;
            bestDiff = diff;
            best = guidePos;
        }

        /// <summary>收参考线并摘掉主拖动节点的订阅（挂摘成对；幂等）</summary>
        private void HideDragGuides()
        {
            if (_guideDragNode != null)
            {
                _guideDragNode.PropertyChanged -= OnDragGuideNodeMoved;
                _guideDragNode = null;
            }

            GuideX = null;
            GuideY = null;
            RaisePropertyChanged(nameof(GuideVisibility));
        }
    }

    /// <summary>
    /// 模块级连线承载的一条端口绑定。
    /// Key 是 LinkedSources 的寻址键（普通算子=端口名，条件节点=变量 Guid），
    /// 解绑、撤销都靠它；Display 是给右键菜单/提示看的串。
    /// </summary>
    public sealed class CanvasLinkBinding
    {
        public CanvasLinkBinding(StepModel consumer, string key, string display)
        {
            Consumer = consumer;
            Key = key;
            Display = display;
        }

        public StepModel Consumer { get; }

        public string Key { get; }

        public string Display { get; }

        public bool IsIllegal { get; init; }

        public string? IllegalReason { get; init; }

        /// <summary>解绑前的 LinkReference 快照（撤销用）；构造连线时由主 VM 回填</summary>
        public LinkReference? OriginalLink { get; set; }

        /// <summary>右键菜单项文本：解绑动作 + 绑定描述 + 非法标记</summary>
        public string MenuHeader => (IsIllegal ? "⚠ " : string.Empty) + $"断开「{Display}」";

        /// <summary>兜底显示：万一菜单没走显式 Header 绑定，也不能把类型全名刷到屏幕上（R20 同款病）</summary>
        public override string ToString() => MenuHeader;
    }

    /// <summary>
    /// 拖线中的临时状态。属性名与 Nodify PendingConnection 的依赖属性同名。
    /// Source/Target 是连接器（每节点一根输入脚/输出脚）。
    /// </summary>
    public class CanvasPendingConnectionViewModel : BindableBase
    {
        public CanvasConnectorViewModel? Source
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        public CanvasConnectorViewModel? Target
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        public bool IsVisible
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        public Point TargetLocation
        {
            get => field;
            set => SetProperty(ref field, value);
        }
    }
}
