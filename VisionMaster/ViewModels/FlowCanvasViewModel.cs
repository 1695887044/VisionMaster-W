using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Core.Interfaces;
using Prism.Dialogs;
using Prism.Ioc;
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

        /// <summary>模块盒标称尺寸（真实渲染随内容浮动，此处用于几何估算与锚点兜底）</summary>
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

        private readonly IWorkspaceManager _workspace;
        private readonly IPluginProvider _pluginProvider;

        /// <summary>构造时 Workspace 的 INotifyPropertyChanged 引用，Deactivate 时用于解绑</summary>
        private readonly INotifyPropertyChanged? _workspaceChanged;

        /// <summary>渲染在画布上的节点：StepID → 节点（只含可见节点，折叠子树不入内）</summary>
        private readonly Dictionary<Guid, CanvasNodeViewModel> _nodeMap = new();

        /// <summary>步骤节点池：跨重建复用同一批 VM，保住 IsSelected 不随渲染蒸发</summary>
        private readonly Dictionary<Guid, CanvasNodeViewModel> _nodePool = new();

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

        public FlowCanvasViewModel(IWorkspaceManager workspace, IPluginProvider pluginProvider)
        {
            _workspace = workspace;
            _pluginProvider = pluginProvider;

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
            ItemsDragStartedCommand = new DelegateCommand<object>(OnItemsDragStarted);
            ItemsDragCompletedCommand = new DelegateCommand<object>(OnItemsDragCompleted);
            DeleteSelectionCommand = new DelegateCommand(OnDeleteSelection);

            PendingConnection.PropertyChanged += OnPendingConnectionChanged;

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

            Rebuild();
        }

        /// <summary>摘除 Workspace 通知与流程/嵌套集合订阅（幂等）。摘干净后旧 VM 可被 GC</summary>
        public void Deactivate()
        {
            if (!_subscribed) return;
            _subscribed = false;

            if (_workspaceChanged != null)
                _workspaceChanged.PropertyChanged -= OnWorkspacePropertyChanged;

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

        /// <summary>拖拽开始：记录被拖节点的原 owner / 原下标 / 原坐标</summary>
        public DelegateCommand<object> ItemsDragStartedCommand { get; }

        /// <summary>拖拽抬起：按几何位置判定改序/移分支并提交命令</summary>
        public DelegateCommand<object> ItemsDragCompletedCommand { get; }

        /// <summary>
        /// 删除画布选中的模块（Delete 键 / 节点右键菜单共用）：连容器子树一起移出图纸。
        /// 数据连线的删除走连线右键菜单（编辑绑定/逐条/整批解绑）——
        /// Nodify 7.3 的连线选中机制（SelectedConnections 集合语义与选中视觉）未取证，不盲绑。
        /// 删除会使撤销栈里改序/移分支命令的下标快照失效，统一清栈——
        /// 宁可失去撤销能力，也不给出错乱的回滚。
        /// </summary>
        public DelegateCommand DeleteSelectionCommand { get; }

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

        /// <summary>是否显示执行顺序链（工具条开关）</summary>
        public bool ShowOrderLinks
        {
            get => field;
            set
            {
                if (SetProperty(ref field, value))
                    RebuildInPlace();
            }
        } = true;

        /// <summary>
        /// 是否显示端口数据线（工具条开关，**默认关**——用户口径："端口之间的连线默认不显示，
        /// 只显示执行顺序"）。
        ///
        /// 隐藏只针对普通数据线：非法数据线（红虚线）是要用户去修的错误提示，不随开关隐藏
        ///（警告条与 Illegal/Deferred 计数同样照常）。全局变量/常量角标不受影响。
        /// </summary>
        public bool ShowDataLinks
        {
            get => field;
            set
            {
                if (SetProperty(ref field, value))
                    RebuildInPlace();
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
                case nameof(IWorkspaceManager.CurrentSolution):
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
            try
            {
                var flow = _workspace.CurrentFlow;

                // 换图纸：节点池作废——StepID 在不同流程间可能重复（复制粘贴），
                // 复用旧池会把 A 流程的节点摆到 B 流程的画布上
                if (!ReferenceEquals(flow, _attachedFlow))
                {
                    Detach();
                    _nodePool.Clear();
                }

                if (flow == null)
                {
                    Nodes.Clear();
                    Links.Clear();
                    _nodeMap.Clear();
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
            }
        }

        private void Detach()
        {
            if (_attachedFlow != null && _stepsChanged != null)
                _attachedFlow.Steps.CollectionChanged -= _stepsChanged;

            _stepsChanged = null;
            _attachedFlow = null;
            DetachLists();

            ClearUndoStacks();
            _dragStartedSnapshot = null;
        }

        private void DetachLists()
        {
            foreach (var list in _watchedLists)
                list.CollectionChanged -= OnFlowStepsChanged;

            _watchedLists.Clear();
        }

        /// <summary>
        /// 订阅整棵树的每个步骤集合：顶层 + 所有容器分支。
        /// 扁平画布上看得到全部深度，任何一处增删都要重画。
        /// </summary>
        private void AttachLists(ObservableCollection<StepModel> list)
        {
            Watch(list);

            foreach (var step in list)
            {
                if (step is not IContainerStep container || container.Children == null) continue;
                foreach (var branch in container.Children)
                {
                    if (branch?.Steps != null) AttachLists(branch.Steps);
                }
            }
        }

        private void Watch(ObservableCollection<StepModel> list)
        {
            _watchedLists.Add(list);
            list.CollectionChanged += OnFlowStepsChanged;
        }

        // ==================================================================
        //  渲染：扁平化整棵流程树
        // ==================================================================

        private void Render(FlowModel flow)
        {
            DetachLists();

            _topology = FlowTopology.Build(flow);

            // 1. 收集全树步骤并补齐布局（AutoLayout 递归：本层纵向、分支横向错开）
            var allSteps = new List<StepModel>();
            CollectSteps(flow.Steps, allSteps);

            if (flow.Layout.HasMissing(allSteps))
                // 不传 collapseOverride：首次渲染保持既有行为——新布局的深容器按
                // AutoCollapseDepth 默认折叠，用户手动展开后坐标已在库、不会被折回去
                flow.Layout.AutoLayout(flow.Steps);

            // 2. 建/复用节点（扁平，含折叠祖先内的步骤——它们进池不进画布）
            _steps.Clear();
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

            // 3. 几何：自底向上算容器框与泳道矩形（展开态）
            ComputeGeometry(flow.Steps);

            // 4. 折叠角标：统计每个折叠容器藏了多少步
            foreach (var node in _steps)
            {
                if (node.Kind != CanvasNodeKind.Container || !node.IsCollapsed) continue;
                node.CollapsedChildCount = CountDescendants(node.Model!);
            }

            // 5. Z 序组装：先框、再泳道、再内容（NodifyCanvas 按集合序叠放，先入垫底）
            Nodes.Clear();
            AppendInZOrder(flow.Steps);

            // 6. 连线（数据依赖 + 执行顺序链）
            AttachLists(flow.Steps);
            BuildLinks(flow);

            // 顶层 + 所有分支集合（CollectCollections 会把顶层本身也收进去，初始化器不能再种一份）
            var collections = new List<ObservableCollection<StepModel>>();
            CollectCollections(flow.Steps, collections);
            BuildOrderLinks(collections);
            UpdateLinkAnchors();

            PrunePool();
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
        }

        /// <summary>FlowLayoutStore 读取的小包装（缺项返回 false）</summary>
        private bool TryGetLayout(Guid id, out double x, out double y, out bool collapsed)
        {
            x = y = 0;
            collapsed = false;
            if (_attachedFlow == null) return false;
            if (!_attachedFlow.Layout.TryGet(id, out var layout) || layout == null) return false;            x = layout.X;
            y = layout.Y;
            collapsed = layout.Collapsed;
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

            // 折叠态：框体就锚在存储坐标上，等高一枚模块盒
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
            foreach (var branch in container.Children)
            {
                if (branch?.Steps == null) continue;
                var laneNode = CanvasNodeViewModel.CreateLane(branch);
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

            var laneRects = ComputeLaneRects(contentRects, node.Location);

            for (int i = 0; i < branchNodes.Count; i++)
            {
                var laneNode = branchNodes[i];
                laneNode.IsLaneEmpty = contentRects[i].IsEmpty;
                laneNode.Location = laneRects[i].Location;
                laneNode.LaneWidth = laneRects[i].Width;
                laneNode.LaneHeight = laneRects[i].Height;
            }

            // 框体 = 泳道并集 + 框内边距 + 头带
            var laneUnionLeft = laneRects.Min(r => r.Left);
            var laneUnionTop = laneRects.Min(r => r.Top);
            var laneUnionRight = laneRects.Max(r => r.Right);
            var laneUnionBottom = laneRects.Max(r => r.Bottom);

            var frame = new Rect(
                laneUnionLeft - FramePadding,
                laneUnionTop - FramePadding - FrameHeaderHeight,
                (laneUnionRight - laneUnionLeft) + FramePadding * 2,
                (laneUnionBottom - laneUnionTop) + FramePadding * 2 + FrameHeaderHeight);

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
        private static List<Rect> ComputeLaneRects(List<Rect> contentRects, Point anchor)
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
                    lanes[i] = new Rect(x, y, EmptyLaneWidth, EmptyLaneHeight);
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
                    ? new Rect(cursorX, laneTop, EmptyLaneWidth, EmptyLaneHeight)
                    : new Rect(
                        content.Left - LanePadding,
                        content.Top - LanePadding - LaneHeaderHeight,
                        content.Width + LanePadding * 2,
                        content.Height + LanePadding * 2 + LaneHeaderHeight);

                cursorX = lanes[i].Right + ColumnGap;
            }

            // 它前面的空列向左回推（都是占位宽，不与首个有内容列相交）
            for (int i = firstPopulated - 1; i >= 0; i--)
            {
                lanes[i] = new Rect(
                    lanes[i + 1].Left - ColumnGap - EmptyLaneWidth,
                    laneTop, EmptyLaneWidth, EmptyLaneHeight);
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
            UpdateLinkAnchors();
        }

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

        /// <summary>清理池中已不在图纸里的步骤节点（防止挂接阻塞 GC）</summary>
        private void PrunePool()
        {
            if (_nodePool.Count == 0) return;

            var dead = _nodePool.Keys.Where(id => !_topology.TryGet(id, out _)).ToList();
            foreach (var id in dead)
            {
                var node = _nodePool[id];
                node.PropertyChanged -= OnNodePropertyChanged;
                node.PropertyChanged -= OnNodeSelectionChanged;
                node.UnhookModel();
                _nodePool.Remove(id);
            }
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

            foreach (var node in _nodeMap.Values)
            {
                foreach (var port in node.Inputs) port.IsConnected = false;
                foreach (var port in node.Outputs) port.IsConnected = false;
                node.ResetExternalBindings();
            }

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

            var byPair = new Dictionary<(Guid Source, Guid Target), CanvasLinkViewModel>();

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
                    if (!byPair.TryGetValue(pairKey, out var linkVm))
                    {
                        linkVm = new CanvasLinkViewModel(producer, consumer)
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
                        byPair[pairKey] = linkVm;
                        Links.Add(linkVm);
                        producer.Outputs[0].IsConnected = true;
                        consumer.Inputs[0].IsConnected = true;
                    }

                    linkVm.Bindings.Add(binding);
                }
            }

            foreach (var linkVm in Links)
            {
                linkVm.IsIllegal = linkVm.Bindings.Any(b => b.IsIllegal);
                linkVm.WarningText = linkVm.Bindings.FirstOrDefault(b => b.IsIllegal)?.IllegalReason;
                linkVm.NotifyBindingsChanged();
            }

            // 数据线隐藏（默认态）：聚合完成后**按整条线**定去留——普通线不渲染，
            // 非法线（红虚线）照常渲染（它是要用户去修的错误提示，不是噪声）。
            // 过滤必须晚于聚合与合法性判定：同一对模块的多条绑定先合并成一根线、再由
            // "有没有非法绑定"决定这根线画不画；若在"加进 Links"的地方提前过滤，
            // 后到的非法绑定会挂在一根从未渲染过的线上——同一对模块换个绑定顺序行为就变了。
            if (!ShowDataLinks)
            {
                for (int i = Links.Count - 1; i >= 0; i--)
                {
                    if (!Links[i].IsIllegal)
                        Links.RemoveAt(i);
                }
            }

            // 顺序链在 Render 里于 BuildLinks 之后统一构建（需要全量集合清单）
            UpdateLinkAnchors();
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
        /// 顺序链：自上而下流动——源 = 上一步盒子底缘中点，目标 = 下一步盒子顶缘中点。
        /// 容器框用框体矩形，步骤盒用固定尺寸常量。
        /// 节点拖动（Location 变化）也会走到这里，连线实时跟随。
        /// </summary>
        private void UpdateLinkAnchors()
        {
            foreach (var link in Links)
            {
                if (link.IsOrderLink)
                {
                    link.SourceAnchor = EdgeMidpoint(link.Source, OrderEdge.Bottom);
                    link.TargetAnchor = EdgeMidpoint(link.Target, OrderEdge.Top);
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

        /// <summary>输入端口显示名：条件节点键是变量 Guid，显示用别名；For 是循环次数；普通算子就是键名</summary>
        private static string DescribeInput(StepModel consumer, string key)
        {
            switch (consumer)
            {
                case ConditionStep condition:
                    var local = condition.LocalVariables.FirstOrDefault(v => v.Id.ToString() == key);
                    return local?.Name ?? key;
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

            node.Model.IsBreakpoint = !node.Model.IsBreakpoint;
        }

        private void OnToggleCollapse(CanvasNodeViewModel? node)
        {
            if (node?.Model == null || _attachedFlow == null) return;
            if (node.Kind != CanvasNodeKind.Container) return;

            bool collapsing = !node.IsCollapsed;
            _attachedFlow.Layout.ToggleCollapsed(node.StepId);

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

        /// <summary>启用/禁用切换（节点右键菜单）。与流程栏同一写法：翻转 IsDisEnable，禁用语义由执行引擎解释</summary>
        private void OnToggleDisable(CanvasNodeViewModel? node)
        {
            if (node?.Model == null || node.IsDecorator) return;

            node.Model.IsDisEnable = !node.Model.IsDisEnable;
            node.RefreshHeader();
            StatusHint = node.Model.IsDisEnable
                ? $"已禁用「{node.Model.StepName}」"
                : $"已启用「{node.Model.StepName}」";
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

        /// <summary>Delete 键 / 右键菜单的统一删除入口：移除选中的模块节点（含容器子树）</summary>
        private void OnDeleteSelection()
        {
            if (_attachedFlow == null) return;

            int removed = DeleteSelectedNodesCore();
            if (removed == 0) return;

            // 删除改变了集合构成，撤销栈里改序/移分支命令记录的下标快照已不可信
            ClearUndoStacks();
            StatusHint = $"已删除 {removed} 个模块（含子步骤）";
            RebuildInPlace();
        }

        /// <summary>把选中的模块（含容器子树）移出图纸。返回删除个数。期间抑制逐个 Remove 触发的重建</summary>
        private int DeleteSelectedNodesCore()
        {
            int removed = 0;
            var targets = _nodeMap.Values
                .Where(n => n.IsSelected && !n.IsDecorator && n.Model != null)
                .Select(n => n.Model!)
                .Where(m => _topology.TryGet(m.StepID, out _))
                .ToList();
            if (targets.Count == 0) return 0;

            _rebuilding = true;
            try
            {
                foreach (var step in targets)
                {
                    if (!_topology.TryGet(step.StepID, out var pos) || pos!.Owner == null) continue;

                    // IndexOf 按引用现查：快照里的 IndexInOwner 可能已被同批次删除挤动
                    int index = pos.Owner.IndexOf(step);
                    if (index >= 0)
                    {
                        pos.Owner.RemoveAt(index);
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
        /// 自动折叠的 230×64 足迹参与父级行/列推进，而重排后它们要渲染成展开——框体远大于
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

        private void OnItemsDragStarted(object? parameter)
        {
            _dragStartedSnapshot = null;

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
        }

        private void OnItemsDragCompleted(object? parameter)
        {
            var snapshot = _dragStartedSnapshot;
            _dragStartedSnapshot = null;
            if (snapshot == null || snapshot.Count == 0 || _attachedFlow == null) return;

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
        /// 落点规则（与流程栏 Drop 的可见结果对齐）：
        ///  · 容器框内（取最深）→ 该容器第一条分支末尾；
        ///  · 其余 → 顶层：插入下标 = 顶层步骤里"节点中心 Y 小于落点 Y"的条数（画布坐标）。
        /// 结构变更通知图纸（Version++，触发重编译）；不做撤销（与流程栏 Drop 同口径）。
        /// </summary>
        public bool DropToolAt(ToolItemModel tool, Point position)
        {
            if (_attachedFlow == null || tool == null) return false;

            var container = FindDeepestContainerAt(position);
            ObservableCollection<StepModel> owner;
            int insertIndex;
            string targetName;

            // 畸形容器（模型层构造保证不会出现）没有分支可落：退化按顶层插入，不让拖放"无声失败"
            if (container?.Model is IContainerStep target && target.Children is { Count: > 0 })
            {
                owner = target.Children[0].Steps;
                insertIndex = owner.Count;   // 容器内一律追加到第一条分支末尾
                targetName = $"「{container.Model.StepName}」";
            }
            else
            {
                owner = _attachedFlow.Steps;
                insertIndex = ComputeInsertIndexByY(owner, position.Y, null);
                targetName = "主流程";
            }

            string stepName = StepFactory.NextStepName(_attachedFlow, tool);
            var step = StepFactory.CreateFromTool(tool, stepName);

            if (insertIndex < 0) insertIndex = 0;
            if (insertIndex > owner.Count) insertIndex = owner.Count;
            owner.Insert(insertIndex, step);

            // 结构变更：通知图纸重编译（口径同流程栏 Drop）；集合 Add 已触发一次画布重建，这里幂等兜底
            _attachedFlow.Version++;
            RebuildInPlace();
            StatusHint = $"已添加「{stepName}」到{targetName}";
            return true;
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
