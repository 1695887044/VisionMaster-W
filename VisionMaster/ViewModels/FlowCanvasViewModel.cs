using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Windows;
using Core.Interfaces;
using VisionMaster.Helpers;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 流程节点画布（M2-1：子画布 + 分支泳道 + 结构告警）。
    ///
    /// 职责边界：本类只做「图纸 ⇄ 图视图」的双向映射，不含任何编译或执行逻辑。
    ///   读：StepModel.LinkedSources / OutputPortDefinitions / IPluginProvider 静态端口 → 节点与连线
    ///   写：拉线 → StepModel.SetLink；断线 → StepModel.RemoveLink；拖动 → FlowLayoutStore.Set
    ///
    /// 五条刻意的设计约束：
    /// 1. 坐标只进 FlowLayoutStore，绝不写 StepModel → 拖动不会递增 Version、不会触发重编译；
    /// 2. 连线只写 LinkedSources，Version 递增由既有的统一写路径负责，画布不自己维护脏标记；
    /// 3. 只订阅步骤集合的增删，不订阅步骤属性变更 → 改名/改参数不会重建画布（避免与写回形成回环）；
    /// 4. 「谁先执行、能不能取数」一律问 <see cref="FlowTopology"/>，画布不自己写下标比较——
    ///    与 FlowCompiler 共用同一真相源，才不会出现「画布放行、编译报错」的劈叉；
    /// 5. 泳道与层根是 ItemsSource 里的**装饰节点**，不是真实步骤：坐标由本层内容算出、不入库，
    ///    因此它们的 StepId 为 Guid.Empty 或祖先 Id，任何写回路径都要先排除装饰身份。
    /// </summary>
    public class FlowCanvasViewModel : BindableBase
    {
        /// <summary>泳道外接框估算用的节点标称尺寸（真实容器尺寸随主题/字号浮动，此处只做「背景比节点大一圈」）</summary>
        private const double NodeWidth = 220;
        private const double NodeHeight = 95;
        private const double LanePadding = 30;
        private const double LaneHeaderHeight = 32;

        /// <summary>层根列与本层主列的水平间距</summary>
        private const double LayerRootGap = 300;

        /// <summary>层根节点（祖先链）纵向堆叠间距</summary>
        private const double LayerRootRow = 130;

        private readonly IWorkspaceManager _workspace;
        private readonly IPluginProvider _pluginProvider;

        /// <summary>
        /// 构造时 Workspace 的 INotifyPropertyChanged 引用，Dispose 时用于解绑。
        /// _workspace 是业务接口 IWorkspaceManager，不继承 INotifyPropertyChanged。
        /// </summary>
        private readonly INotifyPropertyChanged? _workspaceChanged;

        /// <summary>本层可见节点：StepID → 节点，供连线解析与后续运行态高亮反查</summary>
        private readonly Dictionary<Guid, CanvasNodeViewModel> _nodeMap = new();

        /// <summary>
        /// 步骤节点池：跨「增量重建 / 切层」复用同一批 VM。
        /// 复用的目的是保住 IsSelected——ItemsSource 每次渲染都 Clear 重填，
        /// 若 VM 也一起丢，选中态就随渲染蒸发（确认清单第 8 项明令要避免的行为）。
        /// </summary>
        private readonly Dictionary<Guid, CanvasNodeViewModel> _nodePool = new();

        /// <summary>本层可见的「真实步骤」节点（层根与泳道不算），连线以它们为消费方</summary>
        private readonly List<CanvasNodeViewModel> _visibleSteps = new();

        /// <summary>当前层的主列表：顶层是 FlowModel.Steps，子层是某个 StepCollection.Steps</summary>
        private ObservableCollection<StepModel> _layerList = new();

        /// <summary>
        /// 层路径（由外到内的 (祖先容器, 通往本层的分支) 链）。
        /// 面包屑、层根锚点、回跳都从这里取，避免再去拓扑里反查。
        /// </summary>
        private readonly List<(StepModel Container, StepCollection Branch)> _layerTrail = new();

        /// <summary>层路径的分段键，与 _layerTrail 一一对应，拼 _layerPath 用</summary>
        private readonly List<string> _layerSegments = new();

        /// <summary>本层渲染期间订阅过的步骤列表（主列表 + 各泳道列表），渲染前统一摘除</summary>
        private readonly List<INotifyCollectionChanged> _watchedLists = new();

        /// <summary>结构拓扑快照。快照不跟踪图纸，所以每次渲染前无条件重建</summary>
        private FlowTopology _topology = FlowTopology.Build(Array.Empty<StepModel>());

        private FlowModel? _attachedFlow;
        private NotifyCollectionChangedEventHandler? _stepsChanged;
        private string _layerPath = string.Empty;
        private Guid _pendingSelectId;

        /// <summary>正在重建：抑制重建过程自身引发的重入</summary>
        private bool _rebuilding;

        /// <summary>
        /// 画布主动切当前步骤时的回环闸门。
        /// WorkspaceContext.SwitchStep 是同步派发 PropertyChanged 的，
        /// 所以只要在调用前后置位/复位就足以挡住自己收到的回调（联动方案 A）。
        /// </summary>
        private bool _suppressStepSync;

        // ---- 段3：撤销栈 + 拖拽改序 ----

        /// <summary>
        /// 撤销栈。栈深上限 <see cref="MaxUndoStack"/>，超出丢最早项。
        /// 不快照、不撤销坐标：坐标写回只进 FlowLayoutStore、不递增 Version，
        /// 撤销它会让用户拖一下就被回滚，体验上是反效果。
        /// </summary>
        private readonly Stack<IUndoCommand> _undoStack = new();

        /// <summary>重做栈。新操作压栈时清空（单栈双向）</summary>
        private readonly Stack<IUndoCommand> _redoStack = new();

        private const int MaxUndoStack = 50;

        /// <summary>
        /// 撤销/重做执行期间置位：逆操作本身会改 Steps 集合/LinkedSources，
        /// 这些写回不应再压栈（否则会无限循环——Undo 产生新条目又被 Undo）。
        /// </summary>
        private bool _suppressUndoRedo;

        /// <summary>
        /// 拖拽开始快照：每个被拖 Step 节点的 (Owner, IndexInOwner, OriginalLocation)。
        /// 抬起时按几何位置判改序/移分支，对照此快照提交命令。
        /// </summary>
        private List<DragSnapshot>? _dragStartedSnapshot;

        /// <summary>插入指示线 Y 坐标（项目主列纵向流，水平线在 Y 插入点）；&lt;0 表示不显示</summary>
        private double _dropIndicatorY = -1;

        /// <summary>插入指示线目标泳道 Id（跨分支拖拽时高亮泳道边框）；Guid.Empty 表示无</summary>
        private Guid _dropHighlightLaneId = Guid.Empty;

        public FlowCanvasViewModel(IWorkspaceManager workspace, IPluginProvider pluginProvider)
        {
            _workspace = workspace;
            _pluginProvider = pluginProvider;

            // 命令参数用 object 承接 Nodify 传入的 DataContext，再在内部转型，
            // 避免视图模型与控件泛型签名强耦合
            StartConnectionCommand = new DelegateCommand<object>(OnStartConnection);
            CompleteConnectionCommand = new DelegateCommand<object>(OnCompleteConnection);
            DisconnectConnectorCommand = new DelegateCommand<object>(OnDisconnectConnector);
            DrillDownCommand = new DelegateCommand<CanvasNodeViewModel?>(OnDrillDown);
            NavigateCrumbCommand = new DelegateCommand<CanvasCrumbViewModel?>(OnNavigateCrumb);

            // 段3：撤销/重做 + 拖拽改序
            // CanExecute 直接读栈非空，命令被 Invoke 时也立即 RaiseCanExecuteChanged，
            // 让 InputBinding 的 Ctrl+Z/Y 在栈空时不会响铃
            UndoCommand = new DelegateCommand(Undo, () => _undoStack.Count > 0);
            RedoCommand = new DelegateCommand(Redo, () => _redoStack.Count > 0);
            ItemsDragStartedCommand = new DelegateCommand<object>(OnItemsDragStarted);
            ItemsDragCompletedCommand = new DelegateCommand<object>(OnItemsDragCompleted);

            // 拖线过程中要把「按当前起点算下来不可连」的端口画灰，
            // 端点/可见性一变就要重算，故订阅自身状态而不是让视图去推
            // PendingConnection 是本 VM 自己的成员，随 VM 一起被 GC，无需在 Deactivate 中摘除
            PendingConnection.PropertyChanged += OnPendingConnectionChanged;

            // 流程/方案/当前步骤的通知来自 WorkspaceContext（BindableBase），不是 FlowModel——
            // FlowModel 只会通知自己的 Version/FlowName 等属性，挂错对象会导致切换流程时画布不刷新。
            _workspaceChanged = _workspace as INotifyPropertyChanged;

            // 订阅与首次渲染统一由 Activate 负责；View 的 Loaded / Unloaded 成对调用 Activate / Deactivate。
            // 构造即视为"已入树"，所以这里先挂一次（含 Rebuild）。
            Activate();
        }

        private bool _subscribed;

        /// <summary>
        /// 挂接 Workspace 通知并渲染（幂等）。
        ///
        /// 为什么不做成一次性 Dispose：AvalonDock 切换标签页、隐藏面板都会让 View 触发 Unloaded
        /// （之后不会自动 Loaded），一次性清理会让面板恢复后画布不再响应流程变化。
        /// 故设计为可逆的挂/摘——离树时摘干净让旧 VM 可被 GC，入树时重新挂上。
        /// </summary>
        public void Activate()
        {
            if (_subscribed) return;
            _subscribed = true;

            if (_workspaceChanged != null)
                _workspaceChanged.PropertyChanged += OnWorkspacePropertyChanged;

            Rebuild();
        }

        /// <summary>
        /// 摘除 Workspace 通知与流程/各泳道集合订阅（幂等）。
        /// 摘干净后本 VM 不再被 Workspace 单例引用，且 Detach 会清掉撤销栈里对旧图纸的引用。
        /// </summary>
        public void Deactivate()
        {
            if (!_subscribed) return;
            _subscribed = false;

            if (_workspaceChanged != null)
                _workspaceChanged.PropertyChanged -= OnWorkspacePropertyChanged;

            Detach();
        }

        public ObservableCollection<CanvasNodeViewModel> Nodes { get; } = new();

        public ObservableCollection<CanvasConnectionViewModel> Connections { get; } = new();

        /// <summary>面包屑：从流程名到当前层，点击任一级回跳</summary>
        public ObservableCollection<CanvasCrumbViewModel> Breadcrumb { get; } = new();

        /// <summary>
        /// 拖线过程中的临时连线状态。属性名与 Nodify PendingConnection 的 DP 同名，
        /// 因此可直接交给默认模板渲染，无需自写 PendingConnectionTemplate
        /// </summary>
        public CanvasPendingConnectionViewModel PendingConnection { get; } = new();

        /// <summary>拖线开始：让待完成的连线可见（Nodify 把起点端口的 DataContext 写入 PendingConnection.Source）</summary>
        public DelegateCommand<object> StartConnectionCommand { get; }

        public DelegateCommand<object> CompleteConnectionCommand { get; }

        public DelegateCommand<object> DisconnectConnectorCommand { get; }

        /// <summary>双击容器节点/泳道下钻</summary>
        public DelegateCommand<CanvasNodeViewModel?> DrillDownCommand { get; }

        /// <summary>点击面包屑回跳</summary>
        public DelegateCommand<CanvasCrumbViewModel?> NavigateCrumbCommand { get; }

        // ---- 段3 命令 ----

        /// <summary>撤销（Ctrl+Z）：弹栈顶到 redo，执行其 Undo</summary>
        public DelegateCommand UndoCommand { get; }

        /// <summary>重做（Ctrl+Y）：弹 redo 栈顶回 undo，执行其 Redo</summary>
        public DelegateCommand RedoCommand { get; }

        /// <summary>
        /// 拖拽开始：记录被拖 Step 节点的原 owner / 原下标 / 原坐标。
        /// 参数是 Nodify 传入的 IEnumerable&lt;ItemContainer&gt;，本类用反射读 DataContext 提取节点。
        /// </summary>
        public DelegateCommand<object> ItemsDragStartedCommand { get; }

        /// <summary>拖拽抬起：按几何位置判定改序/移分支并提交命令</summary>
        public DelegateCommand<object> ItemsDragCompletedCommand { get; }

        /// <summary>插入指示线 Y 坐标；&lt;0 表示不显示。视图据此画一条水平线 + 端点三角</summary>
        public double DropIndicatorY
        {
            get => _dropIndicatorY;
            private set => SetProperty(ref _dropIndicatorY, value);
        }

        /// <summary>跨分支拖拽时高亮目标泳道的容器 StepId；Guid.Empty 表示无</summary>
        public Guid DropHighlightLaneId
        {
            get => _dropHighlightLaneId;
            private set => SetProperty(ref _dropHighlightLaneId, value);
        }

        /// <summary>撤销栈深度（供断言与状态显示）</summary>
        public int UndoStackSize => _undoStack.Count;

        /// <summary>重做栈深度</summary>
        public int RedoStackSize => _redoStack.Count;

        /// <summary>
        /// 视口动作请求。参数 null = 适应整层（切层/切流程后），非 null = 把该节点滚入视野（外部选中联动）。
        /// 必须由视图执行：ViewportLocation / FitToScreen 是 NodifyEditor 的东西，视图模型够不着。
        /// </summary>
        public event Action<CanvasNodeViewModel?>? ViewportActionRequested;

        /// <summary>当前是否有可显示的流程（只看真实步骤，装饰节点不计）</summary>
        public bool HasFlow => _attachedFlow != null && _visibleSteps.Count > 0;

        /// <summary>空状态提示的可见性。直接由视图模型给出，避免为一条提示引入转换器资源依赖</summary>
        public Visibility EmptyHintVisibility => HasFlow ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>是否位于子层（非顶层），决定面包屑与「返回顶层」是否可用</summary>
        public bool IsNested => !string.IsNullOrEmpty(_layerPath);

        /// <summary>因跨层/非步骤来源/端口失效而无法画成实线的连线数</summary>
        public int DeferredLinkCount { get; private set; }

        /// <summary>结构非法（倒序、跨分支、外层晚于容器）的连线数——编译期会是致命错</summary>
        public int IllegalLinkCount { get; private set; }

        /// <summary>告警条是否显示。降级连线是常态（全局变量绑定很多），不报警；只有非法连线才报警</summary>
        public Visibility WarningVisibility => IllegalLinkCount > 0 ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>告警条文案</summary>
        public string WarningText => IllegalLinkCount > 0
            ? $"⚠ {IllegalLinkCount} 条连线结构非法（编译会报致命错），已按灰色虚线显示；另有 {DeferredLinkCount} 条跨层/非步骤连线未在本层绘制"
            : string.Empty;

        /// <summary>状态栏提示：拒绝连线的原因等一次性文案；无提示时为 null</summary>
        public string? StatusHint
        {
            get => field;
            set => SetProperty(ref field, value);
        }

        /// <summary>统一刷新依赖集合内容的派生状态</summary>
        private void NotifyViewStates()
        {
            RaisePropertyChanged(nameof(HasFlow));
            RaisePropertyChanged(nameof(EmptyHintVisibility));
            RaisePropertyChanged(nameof(IsNested));
            RaisePropertyChanged(nameof(DeferredLinkCount));
            RaisePropertyChanged(nameof(IllegalLinkCount));
            RaisePropertyChanged(nameof(WarningVisibility));
            RaisePropertyChanged(nameof(WarningText));
            RaisePropertyChanged(nameof(StatusHint));
        }

        // ==================================================================
        //  段3：撤销栈
        // ==================================================================

        /// <summary>
        /// 压栈：新操作产生时调用。规则：
        /// 1. <see cref="_suppressUndoRedo"/> 期间不压（Undo/Redo 自身的写回不入栈）；
        /// 2. 压栈即清空 redo 栈（单栈双向）；
        /// 3. 超过 <see cref="MaxUndoStack"/> 丢最早项（Stack 不支持从底删，转 List 处理）。
        /// </summary>
        internal void PushUndo(IUndoCommand command)
        {
            if (command == null || _suppressUndoRedo) return;

            _redoStack.Clear();

            if (_undoStack.Count >= MaxUndoStack)
            {
                // 转 List 翻转、丢最后一项（最旧）、再翻回 Stack
                var list = new List<IUndoCommand>(_undoStack);
                list.Reverse();                  // 栈顶在前
                list.RemoveAt(list.Count - 1);   // 丢最旧（栈底）
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
                // 设计约束 3：画布只订阅 Steps 集合的增删，不订阅 StepModel 属性变更。
                // 连线/断线类 Undo 只改 LinkedSources（属性变更），不触发 OnFlowStepsChanged →
                // 画布的 Connections 集合不会自动重建。这里显式调一次 RebuildInPlace，
                // 让 BuildConnections 基于 LinkedSources 重新生成 Connections。
                // ReorderCommand/MoveBranchCommand 已通过 Move/Remove+Insert 触发过一次重建，
                // 这里再调一次是冗余但幂等的——RebuildCore 本身可重入安全，二次重建结果与首次一致。
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
                // 同 Undo：连线/断线类 Redo 不会触发 Steps 集合变化，需显式重建 Connections。
                RebuildInPlace();
            }
            finally
            {
                _suppressUndoRedo = false;
            }
            _undoStack.Push(cmd);
            RaiseUndoRedoCanExecuteChanged();
        }

        /// <summary>清空两个栈。切流程 / Detach 时调用——避免引用旧图纸的 StepModel 阻塞 GC</summary>
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

        /// <summary>
        /// 段3 拖拽快照条目。记录被拖 Step 节点在拖拽开始时的：
        /// - 节点引用（Location 已在拖拽中变化）
        /// - Owner：原所在步骤集合（顶层或某分支 Steps）
        /// - OriginalIndex：原下标
        /// - OriginalLocation：原坐标（仅记录用于多次 Undo 重放时保住视觉位置）
        /// </summary>
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

            // 结构变化会牵动节点集合、泳道外接框与连线合法性。
            // 因为坐标来自 FlowLayoutStore（按 StepID 全局寻址）、选中态来自节点池，
            // 重画一遍不会丢用户状态，所以这里走「全量重画 + 保住视口」而不是增量打补丁。
            RebuildInPlace();
        }

        /// <summary>
        /// 全量重建并适应新层：切流程、切方案、初始化。
        /// </summary>
        public void Rebuild() => RebuildCore(resetViewport: true, dropLayer: true);

        /// <summary>切层：重画 + 适应视口</summary>
        private void RebuildLayer() => RebuildCore(resetViewport: true, dropLayer: false);

        /// <summary>本层内的结构变化：重画但保住视口（确认清单第 8 项）</summary>
        private void RebuildInPlace() => RebuildCore(resetViewport: false, dropLayer: false);

        /// <summary>
        /// 依据当前流程重建节点与连线。可重复调用（幂等）。
        /// </summary>
        private void RebuildCore(bool resetViewport, bool dropLayer)
        {
            if (_rebuilding) return;
            _rebuilding = true;
            try
            {
                var flow = _workspace.CurrentFlow;

                // 换图纸：节点池、层路径一律作废——StepID 在不同流程间可能重复（复制粘贴），
                // 复用旧池会把 A 流程的节点摆到 B 流程的画布上
                if (!ReferenceEquals(flow, _attachedFlow))
                {
                    Detach();
                    _nodePool.Clear();
                    _layerPath = string.Empty;
                    _layerSegments.Clear();
                    _layerTrail.Clear();
                }
                else if (dropLayer)
                {
                    _layerPath = string.Empty;
                    _layerSegments.Clear();
                    _layerTrail.Clear();
                }

                if (flow == null)
                {
                    DetachLists();
                    Nodes.Clear();
                    Connections.Clear();
                    Breadcrumb.Clear();
                    _nodeMap.Clear();
                    _visibleSteps.Clear();
                    DeferredLinkCount = 0;
                    IllegalLinkCount = 0;
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
                BuildConnections(flow);
                UpdatePortAvailability();
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
            {
                _attachedFlow.Steps.CollectionChanged -= _stepsChanged;
            }

            _stepsChanged = null;
            _attachedFlow = null;
            DetachLists();

            // 段3：切流程时清撤销栈——栈里的 StepModel 引用属于旧图纸，
            // 不清会阻塞 GC，且 Undo 会把旧图纸的改序重放到新流程上
            ClearUndoStacks();
            _dragStartedSnapshot = null;
            DropIndicatorY = -1;
            DropHighlightLaneId = Guid.Empty;
        }

        private void DetachLists()
        {
            foreach (var list in _watchedLists)
                list.CollectionChanged -= OnFlowStepsChanged;

            _watchedLists.Clear();
        }

        /// <summary>
        /// 订阅本层渲染到的每个列表：主列表 + 各泳道分支。
        /// 只订阅 flow.Steps 的老做法在子画布里不够用——分支里加一步，画布不会动。
        /// </summary>
        private void AttachLists(FlowModel flow)
        {
            // 顶层列表已由 _stepsChanged 订阅，重复挂会在同一次变更上重建两遍
            if (!ReferenceEquals(_layerList, flow.Steps))
                Watch(_layerList);

            foreach (var step in _layerList)
            {
                if (step is not IContainerStep container || container.Children == null) continue;

                foreach (var branch in container.Children)
                {
                    if (branch?.Steps != null) Watch(branch.Steps);
                }
            }
        }

        private void Watch(ObservableCollection<StepModel> list)
        {
            _watchedLists.Add(list);
            list.CollectionChanged += OnFlowStepsChanged;
        }

        // ==================================================================
        //  层路径：解析与生成
        // ==================================================================

        /// <summary>
        /// 把层路径键解析成主列表，同时填好 _layerTrail / _layerSegments。
        /// 路径键形如 <c>{容器StepID}#{分支下标}/{容器StepID}#{分支下标}</c>。
        ///
        /// 为什么用「Id + 分支下标」而不是集合引用：引用存不住（图纸反序列化后换了实例）、
        /// 分支名可重复且可改；Id+下标既能跨渲染稳定，又能在分支被删时干净地解析失败并退回顶层。
        /// </summary>
        private ObservableCollection<StepModel>? ResolveTrail(FlowModel flow, string pathKey)
        {
            _layerTrail.Clear();
            _layerSegments.Clear();

            var list = flow.Steps;
            if (string.IsNullOrEmpty(pathKey))
                return list;

            foreach (var segment in pathKey.Split('/'))
            {
                int hash = segment.IndexOf('#');
                if (hash <= 0) return null;
                if (!Guid.TryParse(segment.Substring(0, hash), out var containerId)) return null;
                if (!int.TryParse(segment.Substring(hash + 1), out var branchIndex)) return null;

                var container = list.FirstOrDefault(s => s != null && s.StepID == containerId);
                if (container is not IContainerStep ic) return null;
                if (ic.Children == null || branchIndex < 0 || branchIndex >= ic.Children.Count) return null;

                var branch = ic.Children[branchIndex];
                if (branch?.Steps == null) return null;

                _layerTrail.Add((container, branch));
                _layerSegments.Add(segment);
                list = branch.Steps;
            }

            return list;
        }

        /// <summary>
        /// 反推某个步骤所在层的路径键（沿 Branch/ParentContainer 一路向外，最后倒序拼接）。
        /// </summary>
        private string BuildPathKey(StepPosition position)
        {
            var segments = new List<string>();
            var current = position;

            while (current != null && current.Branch != null && current.ParentContainer != null)
            {
                var container = current.ParentContainer;
                if (container.Step is not IContainerStep ic)
                    break;

                int index = IndexOfBranch(ic, current.Branch);
                if (index < 0) break;

                segments.Add($"{container.StepId}#{index}");
                current = container;
            }

            segments.Reverse();
            return string.Join("/", segments);
        }

        private static int IndexOfBranch(IContainerStep container, StepCollection branch)
        {
            var children = container.Children;
            if (children == null) return -1;

            for (int i = 0; i < children.Count; i++)
            {
                if (ReferenceEquals(children[i], branch)) return i;
            }

            return -1;
        }

        private static string MakeSegment(StepModel container, IContainerStep ic, StepCollection branch)
            => $"{container.StepID}#{IndexOfBranch(ic, branch)}";

        // ==================================================================
        //  渲染
        // ==================================================================

        /// <summary>
        /// 渲染当前层：一层全展开——主列是本层步骤，每个容器步骤的每条分支铺一条泳道，
        /// 祖先容器投影成左侧的层根锚点（只有输出端口、不可拖、坐标不入库）。
        /// </summary>
        private void Render(FlowModel flow)
        {
            DetachLists();

            // 快照不跟踪图纸：Build 之后不订阅任何东西，所以每次渲染前都得重建一遍，
            // 否则改序、移动分支后拿旧快照比下标，会得出「看着非法其实合法」的假结论
            _topology = FlowTopology.Build(flow);

            var list = ResolveTrail(flow, _layerPath);
            if (list == null)
            {
                // 路径失效（分支或容器被删）：退回顶层，而不是抛异常打断渲染
                list = ResolveTrail(flow, string.Empty)!;
                _layerPath = string.Empty;
            }
            else
            {
                _layerPath = string.Join("/", _layerSegments);
            }

            _layerList = list;
            BuildBreadcrumb(flow);
            BuildNodes(flow);
            AttachLists(flow);
        }

        private void BuildBreadcrumb(FlowModel flow)
        {
            Breadcrumb.Clear();
            Breadcrumb.Add(new CanvasCrumbViewModel(flow.FlowName ?? "流程", string.Empty));

            var keys = new StringBuilder();
            for (int i = 0; i < _layerTrail.Count; i++)
            {
                if (i > 0) keys.Append('/');
                keys.Append(_layerSegments[i]);

                var (container, branch) = _layerTrail[i];
                Breadcrumb.Add(new CanvasCrumbViewModel($"{container.StepName} · {branch.DisplayName}", keys.ToString()));
            }

            for (int i = 0; i < Breadcrumb.Count; i++)
                Breadcrumb[i].IsCurrent = i == Breadcrumb.Count - 1;
        }

        private void BuildNodes(FlowModel flow)
        {
            Nodes.Clear();
            Connections.Clear();
            _nodeMap.Clear();
            _visibleSteps.Clear();

            // 第一遍：把本层要显示的真实步骤收集齐（主列 + 每条泳道一层）
            var branchGroups = new List<StepCollection>();
            foreach (var step in _layerList)
            {
                if (step is IContainerStep container && container.Children != null)
                {
                    foreach (var branch in container.Children)
                    {
                        if (branch?.Steps != null) branchGroups.Add(branch);
                    }
                }
            }

            var mainSteps = _layerList.Where(s => s != null).ToList();
            var allSteps = new List<StepModel>(mainSteps);
            foreach (var branch in branchGroups)
                allSteps.AddRange(branch.Steps.Where(s => s != null));

            EnsureLayout(flow, allSteps);

            // 装饰节点先入集合：NodifyCanvas 按子元素顺序排 Z 序，先进来的天然垫底
            var layerRoots = new List<CanvasNodeViewModel>();
            foreach (var (container, _) in _layerTrail)
            {
                var root = CanvasNodeViewModel.CreateLayerRoot(container, $"层根 · {container.StepName}");
                ConfigurePorts(root, inputs: false);
                layerRoots.Add(root);
                _nodeMap[root.StepId] = root;
            }

            var lanes = new List<(CanvasNodeViewModel Lane, StepCollection Branch)>();
            foreach (var step in mainSteps)
            {
                if (step is not IContainerStep container || container.Children == null) continue;

                foreach (var branch in container.Children)
                {
                    if (branch?.Steps == null) continue;
                    var lane = CanvasNodeViewModel.CreateLane(branch, branch.DisplayName);
                    lanes.Add((lane, branch));
                }
            }

            foreach (var root in layerRoots) Nodes.Add(root);
            foreach (var (lane, _) in lanes) Nodes.Add(lane);

            var mainNodes = new List<CanvasNodeViewModel>();
            foreach (var step in mainSteps)
            {
                var node = GetOrCreateStepNode(step, flow);
                Nodes.Add(node);
                _visibleSteps.Add(node);
                mainNodes.Add(node);
            }

            foreach (var (lane, branch) in lanes)
            {
                foreach (var step in branch.Steps)
                {
                    if (step == null) continue;
                    var node = GetOrCreateStepNode(step, flow);
                    Nodes.Add(node);
                    _visibleSteps.Add(node);
                }
            }

            UpdateLayerRootPositions(layerRoots, mainNodes);
            UpdateLaneGeometry(lanes);
            PrunePool();
        }

        /// <summary>
        /// 缺坐标时兜一次自动布局。AutoLayout 只补缺项、不动已有坐标，
        /// 所以整层调用一次即可，不必逐节点判空（逐节点调用会让每次重画都白跑一遍全图）。
        /// </summary>
        private void EnsureLayout(FlowModel flow, IReadOnlyCollection<StepModel> visible)
        {
            if (!flow.Layout.HasMissing(visible)) return;
            flow.Layout.AutoLayout(flow.Steps);
        }

        /// <summary>取/造本层步骤节点。同 StepID 但模型实例不同（复制粘贴产生的重复 Id）时不复用</summary>
        private CanvasNodeViewModel GetOrCreateStepNode(StepModel step, FlowModel flow)
        {
            CanvasNodeViewModel node;
            if (_nodePool.TryGetValue(step.StepID, out var pooled) && ReferenceEquals(pooled.Model, step))
            {
                node = pooled;
            }
            else
            {
                node = new CanvasNodeViewModel(step);
                // 拖动回写布局。注意这里走 FlowLayoutStore，不碰 StepModel，
                // 因此不会递增 Version —— 这是 M1-7 隔离设计的落点
                node.PropertyChanged += OnNodePropertyChanged;
                if (!_nodePool.ContainsKey(step.StepID))
                    _nodePool[step.StepID] = node;
            }

            node.IsContainer = step is IContainerStep;
            node.IsNested = !string.IsNullOrEmpty(_layerPath);
            node.OrderIndex = _topology.TryGet(step.StepID, out var pos) ? pos.IndexInOwner + 1 : 0;

            ConfigurePorts(node, inputs: true);

            if (flow.Layout.TryGet(step.StepID, out var layout) && layout != null)
                node.Location = new Point(layout.X, layout.Y);

            _nodeMap[step.StepID] = node;
            return node;
        }

        /// <summary>
        /// 重挂端口。每次渲染都重建：算子换版本、动态输出增减都会改变端口集合，
        /// 而画布刻意不订阅步骤属性变更（约束 3），所以只在渲染时全量重挂一次，
        /// 宁可多建几个轻量 VM，也不留「节点上还有端口、图纸里早已没有」的陈旧状态。
        /// </summary>
        private void ConfigurePorts(CanvasNodeViewModel node, bool inputs)
        {
            node.Inputs.Clear();
            node.Outputs.Clear();

            var model = node.Model;
            if (model == null) return;

            if (inputs)
            {
                foreach (var (key, label, type) in GetInputPorts(model))
                {
                    node.Inputs.Add(new CanvasConnectorViewModel(node, key, type, isInput: true)
                    {
                        // 显示名与寻址键分离：条件节点的键是变量 Guid，显示要用别名
                        DisplayLabel = label,
                    });
                }
            }

            foreach (var (name, type, isVariablePort) in GetOutputPorts(model))
                node.Outputs.Add(new CanvasConnectorViewModel(node, name, type, isInput: false)
                {
                    IsRuntimeVariablePort = isVariablePort,
                });
        }

        /// <summary>
        /// 层根锚点摆在本层主列左侧，按祖先由外到内纵向堆叠。
        /// 主列节点由调用方直接给（而不是从 _visibleSteps 里筛），因为「属于本层主列表」这件事
        /// BuildNodes 在收集 mainSteps 时就已经确定了，再回来判一次模型归属只会多一个出错点。
        /// </summary>
        private void UpdateLayerRootPositions(
            List<CanvasNodeViewModel> layerRoots,
            List<CanvasNodeViewModel> mainNodes)
        {
            if (layerRoots.Count == 0) return;

            var main = mainNodes.Select(n => n.Location).ToList();

            // 主列坐标由用户拖动决定，层根只能跟着外接框走：
            // 它自己的坐标不入库（每次进层重算），存下来反而与本层手工布局打架
            double x = main.Count > 0 ? main.Min(p => p.X) - LayerRootGap : 0;
            double y = main.Count > 0 ? main.Min(p => p.Y) : 0;

            for (int i = 0; i < layerRoots.Count; i++)
                layerRoots[i].Location = new Point(x, y + i * LayerRootRow);
        }

        /// <summary>泳道几何 = 该分支所有节点坐标的外接框 + 内边距（不量真实容器，避免布局回环）</summary>
        private void UpdateLaneGeometry(List<(CanvasNodeViewModel Lane, StepCollection Branch)> lanes)
        {
            foreach (var (lane, branch) in lanes)
            {
                var points = branch.Steps
                    .Where(s => s != null && _nodeMap.TryGetValue(s.StepID, out var n) && n.Kind == CanvasNodeKind.Step)
                    .Select(s => _nodeMap[s.StepID].Location)
                    .ToList();

                lane.IsLaneEmpty = points.Count == 0;

                double x = points.Count > 0 ? points.Min(p => p.X) : lane.Location.X;
                double y = points.Count > 0 ? points.Min(p => p.Y) : lane.Location.Y;
                double right = points.Count > 0 ? points.Max(p => p.X) + NodeWidth : x + NodeWidth;
                double bottom = points.Count > 0 ? points.Max(p => p.Y) + NodeHeight : y + NodeHeight;

                lane.Location = new Point(x - LanePadding, y - LanePadding - LaneHeaderHeight);
                lane.LaneWidth = right - x + LanePadding * 2;
                lane.LaneHeight = bottom - y + LanePadding * 2 + LaneHeaderHeight;
            }
        }

        /// <summary>
        /// 清理池中已不在图纸里的步骤节点。图纸被删掉的步骤若一直留在池中，
        /// 它的 PropertyChanged 挂接会连带整个 VM 活下去（画布 VM 与面板同生命周期，池不会自动缩）。
        /// </summary>
        private void PrunePool()
        {
            if (_nodePool.Count == 0) return;

            var dead = _nodePool.Keys.Where(id => !_topology.TryGet(id, out _)).ToList();
            foreach (var id in dead)
            {
                var node = _nodePool[id];
                node.PropertyChanged -= OnNodePropertyChanged;
                _nodePool.Remove(id);
            }
        }

        private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(CanvasNodeViewModel.Location)) return;
            if (sender is not CanvasNodeViewModel node || _attachedFlow == null) return;

            // 装饰节点（泳道/层根）的坐标是算出来的，落库就会污染 FlowLayoutStore
            if (node.Kind != CanvasNodeKind.Step || node.StepId == Guid.Empty) return;

            var current = node.Location;
            var existing = _attachedFlow.Layout.Find(node.StepId);
            var collapsed = existing?.Collapsed ?? false;

            _attachedFlow.Layout.Set(node.StepId, current.X, current.Y, collapsed);
        }

        /// <summary>
        /// 把本层步骤 LinkedSources 里能落地的连线画成实线，结构非法的画成灰虚线并计数。
        /// 可还原的两类：StepPort（上游在本层）与 RuntimeVariable（变量定义节点在本层，按变量名回找）；
        /// 全局变量/常量、上游在本层之外（更深的子层、祖先层、已删除）、端口已失效的计入 DeferredLinkCount。
        /// </summary>
        private void BuildConnections(FlowModel flow)
        {
            DeferredLinkCount = 0;
            IllegalLinkCount = 0;

            // 端口是每次重挂的，IsConnected 必须先从两端清零：
            // 否则上一轮的连线会把已失效端口一直标成"连着"，Nodify 也就不再更新其 Anchor
            foreach (var node in _nodeMap.Values)
            {
                foreach (var port in node.Inputs) port.IsConnected = false;
                foreach (var port in node.Outputs) port.IsConnected = false;
            }

            Connections.Clear();

            // 变量名 → 本层的值输出脚。运行时变量线的连线里不存定义步骤 Id（只有变量名），
            // 所以还原时只能按名回找；同名变量按本层枚举顺序"后者覆盖前者"，
            // 与 FlowQueryHelper 的"以最后一次定义为准"同一口径（运行期也是覆盖语义）。
            var variableProducers = new Dictionary<string, CanvasConnectorViewModel>(StringComparer.Ordinal);
            foreach (var producerNode in _visibleSteps)
                foreach (var port in producerNode.Outputs)
                    if (port.IsRuntimeVariablePort)
                        variableProducers[port.PortName] = port;

            foreach (var consumer in _visibleSteps)
            {
                var model = consumer.Model;
                if (model?.LinkedSources == null) continue;

                foreach (var kvp in model.LinkedSources)
                {
                    var link = kvp.Value;
                    if (link == null) continue;

                    var kind = link.NormalizeKind();
                    if (kind != LinkKind.StepPort && kind != LinkKind.RuntimeVariable)
                    {
                        DeferredLinkCount++;
                        continue;
                    }

                    CanvasConnectorViewModel output;

                    if (kind == LinkKind.StepPort)
                    {
                        // 上游不在本层：藏在更深的子层里（本层不展开），或上游步骤已被删除
                        // （后者会在编译期报"致命断连"）
                        if (!_nodeMap.TryGetValue(link.TargetStepId, out var producer))
                        {
                            DeferredLinkCount++;
                            continue;
                        }

                        output = producer.Outputs.FirstOrDefault(o => o.PortName == link.TargetPortName);
                    }
                    else
                    {
                        // 定义在本层 → 画实线；定义在别的层或变量名已失效 → 降级到弹窗里看
                        if (string.IsNullOrEmpty(link.TargetPortName)
                            || !variableProducers.TryGetValue(link.TargetPortName, out output))
                        {
                            DeferredLinkCount++;
                            continue;
                        }
                    }

                    var input = consumer.Inputs.FirstOrDefault(i => i.PortName == kvp.Key);

                    if (input == null || output == null)
                    {
                        // 端口已不存在（算子换版本或动态端口收缩后残留），计入待降级
                        DeferredLinkCount++;
                        continue;
                    }

                    var legality = _topology.Classify(output.Owner.StepId, consumer.StepId);
                    var illegal = legality is LinkLegality.SameListReversed
                        or LinkLegality.CrossBranch
                        or LinkLegality.ProducerAfterEnclosingContainer;

                    if (illegal) IllegalLinkCount++;

                    input.IsConnected = true;
                    output.IsConnected = true;
                    Connections.Add(new CanvasConnectionViewModel(input, output)
                    {
                        IsIllegal = illegal,
                        WarningText = illegal ? Explain(legality) : null,
                    });
                }
            }
        }

        private static string Explain(LinkLegality legality) => legality switch
        {
            LinkLegality.SameListReversed => "生产方排在消费方之后（执行顺序倒序）",
            LinkLegality.ProducerAfterEnclosingContainer => "生产方排在本层所属容器之后，消费时它还没执行",
            LinkLegality.CrossBranch => "跨分支取数：该分支本轮可能不执行，或取到上一轮陈旧值",
            LinkLegality.Unknown => "连线端点不在图纸里（步骤已被删除）",
            _ => "结构非法连线",
        };

        // ==================================================================
        //  端口枚举：三种步骤的输入端口来源各不相同
        // ==================================================================

        /// <summary>
        /// 输入端口。
        /// 条件节点：每个局部变量是一个端口，寻址键是变量 Guid（编译器按 Guid.TryParse 解析），
        ///           显示名是用户起的别名 —— 与 VariableBindingViewModel 的 bindKey 规则一致；
        /// For 节点：只有一个隐藏的 LoopCount 输入（不在 InputValues 里）；
        /// 普通算子：InputValues 的键。
        /// </summary>
        private static IEnumerable<(string Key, string Label, Type Type)> GetInputPorts(StepModel model)
        {
            switch (model)
            {
                case ConditionStep condition:
                    foreach (var local in condition.LocalVariables)
                        yield return (local.Id.ToString(), local.Name, ResolveType(local.DataTypeName));
                    break;

                case ForStep:
                    yield return ("LoopCount", "循环次数", typeof(int));
                    break;

                default:
                    foreach (var key in model.InputValues.Keys)
                        yield return (key, key, typeof(object));
                    break;
            }
        }

        /// <summary>
        /// 输出端口 = 插件静态定义 + 图纸里的动态端口快照（IDynamicOutputProvider 重建后回写的部分）
        /// + 变量定义节点的「值输出脚」。
        /// 与 FlowQueryHelper 给绑定弹窗构造候选树的口径保持一致，避免"画布能连但绑定弹窗选不到"。
        /// 第三项标记该脚是否为运行时变量值脚（建线时要据此写 LinkKind.RuntimeVariable）。
        /// </summary>
        private IEnumerable<(string Name, Type Type, bool IsVariablePort)> GetOutputPorts(StepModel model)
        {
            var result = new List<(string, Type, bool)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            if (_pluginProvider?.ModulePlugins != null
                && model.PluginTypeName != null
                && _pluginProvider.ModulePlugins.TryGetValue(model.PluginTypeName, out var definition)
                && definition?.OutputDefinitions != null)
            {
                foreach (var port in definition.OutputDefinitions)
                {
                    if (string.IsNullOrEmpty(port?.Name) || !seen.Add(port.Name)) continue;
                    result.Add((port.Name, ResolveType(port.DataTypeName), false));
                }
            }

            foreach (var dynamicPort in model.OutputPortDefinitions ?? new List<DynamicPortInfo>())
            {
                if (dynamicPort == null || string.IsNullOrEmpty(dynamicPort.Name)) continue;
                if (!seen.Add(dynamicPort.Name)) continue;
                result.Add((dynamicPort.Name, ResolveType(dynamicPort.DataTypeName), false));
            }

            // For 节点额外暴露一个编译期注入的隐藏输出：循环索引 Index（CompiledForNode.IndexPort）
            if (model is ForStep)
                result.Add(("Index", typeof(int), false));

            // 变量定义节点动态长出一个「值输出脚」：端口名 = 变量名。
            // 插件本身只有 Name/Type/InitialValue/Overwrite 四个输入和 Success/ErrorMessage 两个输出，
            // 变量值是运行期写进 context.LocalVariables 的，画布上想表达「取这个变量」就只能靠这一根动态脚。
            // 识别口径复用 FlowQueryHelper（与绑定弹窗同一份），变量名与插件输出口同名时以输出口为准（不重复长脚）。
            if (FlowQueryHelper.TryGetDefinedVariable(model, out var varName, out var varType) && seen.Add(varName))
                result.Add((varName, varType, true));

            return result;
        }

        /// <summary>
        /// 类型解析失败一律退化为 object：画布只做展示与基本校验，
        /// 真正的类型兼容判定仍以编译器为准（避免画布比编译器更严导致连不上线）
        /// </summary>
        private static Type ResolveType(string? dataTypeName)
        {
            if (string.IsNullOrWhiteSpace(dataTypeName)) return typeof(object);

            try
            {
                return Type.GetType(dataTypeName, throwOnError: false) ?? typeof(object);
            }
            catch
            {
                return typeof(object);
            }
        }

        // ==================================================================
        //  连线：建线与断线
        // ==================================================================

        /// <summary>
        /// 可连性判定：结构合法性交给 FlowTopology（与编译期同一套规则），本函数只管身份与方向。
        /// 刻意不做类型兼容检查：画布上输入端口类型多为 object，
        /// 比编译器更严会出现"画布拒绝、编译器却允许"的矛盾。类型校验交给编译错误红框（M2）。
        /// </summary>
        internal static bool CanConnect(
            CanvasConnectorViewModel? source,
            CanvasConnectorViewModel? target,
            FlowTopology topology)
        {
            if (source == null || target == null) return false;
            if (ReferenceEquals(source, target)) return false;
            if (ReferenceEquals(source.Owner, target.Owner)) return false;   // 禁自连
            if (source.IsInput == target.IsInput) return false;              // 必须一进一出

            var input = source.IsInput ? source : target;
            var output = source.IsInput ? target : source;

            // 消费方必须是本层真实步骤：层根只对外提供产出，泳道根本没有端口。
            // 让祖先当消费方会画出一条"容器取它自己子孙的输出"的线，运行时永远取不到值。
            if (input.Owner.Kind != CanvasNodeKind.Step) return false;
            if (input.Owner.Model == null || output.Owner.Model == null) return false;

            var legality = topology.Classify(output.Owner.StepId, input.Owner.StepId);
            return legality is LinkLegality.SameListBefore or LinkLegality.ProducerIsAncestor;
        }

        /// <summary>
        /// 重算所有端口的可连状态。预览变灰与真正建线走的是同一个 CanConnect，
        /// 所以不会出现「看着能连实际连不上」或反过来的两套规则漂移。
        /// </summary>
        private void UpdatePortAvailability()
        {
            var source = PendingConnection.Source;
            var dragging = PendingConnection.IsVisible && source != null;

            foreach (var node in Nodes)
            {
                foreach (var port in node.Inputs.Concat(node.Outputs))
                {
                    if (dragging)
                    {
                        port.IsConnectable = CanConnect(source, port, _topology);
                        port.UnconnectableReason = port.IsConnectable ? null : "与当前拖出的连线在结构上不相容";
                    }
                    else
                    {
                        // 没拖线时只标出「本层永远连不上」的端口：祖先层根的输入口（已被 ConfigurePorts 排除）
                        port.IsConnectable = node.Kind != CanvasNodeKind.LayerRoot || !port.IsInput;
                        port.UnconnectableReason = port.IsConnectable ? null : "层根锚点只向外提供产出，不能被本层步骤回填";
                    }
                }
            }
        }

        private void OnPendingConnectionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CanvasPendingConnectionViewModel.IsVisible)
                || e.PropertyName == nameof(CanvasPendingConnectionViewModel.Source))
            {
                UpdatePortAvailability();
            }
        }

        private void OnStartConnection(object? parameter)
        {
            if (parameter is CanvasConnectorViewModel connector)
                PendingConnection.Source = connector;

            PendingConnection.IsVisible = true;
        }

        private void OnCompleteConnection(object? parameter)
        {
            PendingConnection.IsVisible = false;

            // 兼容两种挂法：挂在 NodifyEditor.ConnectionCompletedCommand 上时参数是 Tuple<Source,Target>；
            // 挂在 PendingConnection.CompletedCommand 上时参数不保证，此时直接读视图模型里的两端
            var source = PendingConnection.Source;
            var target = PendingConnection.Target;

            if (parameter is Tuple<object, object> tuple)
            {
                source = tuple.Item1 as CanvasConnectorViewModel;
                target = tuple.Item2 as CanvasConnectorViewModel;
            }

            CreateConnection(source, target);
        }

        private void CreateConnection(CanvasConnectorViewModel? source, CanvasConnectorViewModel? target)
        {
            if (!CanConnect(source, target, _topology))
            {
                if (source != null && target != null)
                {
                    var consumer = source.IsInput ? source : target;
                    var producer = source.IsInput ? target : source;
                    var reason = consumer.Owner.Model == null || producer.Owner.Model == null
                        ? "端点身份不符（层根/泳道不参与连线）"
                        : Explain(_topology.Classify(producer.Owner.StepId, consumer.Owner.StepId));
                    StatusHint = $"连线被拒绝：{reason}";
                }
                return;
            }

            StatusHint = null;
            var input = source!.IsInput ? source : target!;
            var output = source.IsInput ? target! : source;

            // 一个输入端口只允许一条连线：先断开旧线，再建新线
            RemoveExistingConnection(input);

            // 段3：记录旧 LinkReference 以支持撤销（断线前的 LinkedSources 取一份快照）
            // LinkedSources 已存在意味着这是「换线」，Undo 应当把旧线恢复回去；
            // 不存在就是首次建线，Undo 时 RemoveLink 即可。
            LinkReference? oldLink = null;
            if (input.Owner.Model!.LinkedSources.TryGetValue(input.PortName, out var existing) && existing != null)
                oldLink = existing;

            // 源是「值输出脚」→ 这是一条运行时变量引用线，与绑定弹窗写出的完全同构：
            // TargetStepId 用协议 marker（不存定义步骤的 Id），寻址键只有变量名。
            // 为什么不存定义步骤 Id：变量的身份是名字（运行期按名从 LocalVariables 取），
            // 存了 Id 反而多一套"定义步骤被删/改名"的失效判定，而编译器本来就不看它。
            var newLink = output.IsRuntimeVariablePort
                ? new LinkReference(
                    LinkKind.RuntimeVariable,
                    LinkProtocol.RuntimeVariableMarkerGuid,
                    output.PortName,
                    $"Runtime.{output.PortName}")
                : new LinkReference(
                    LinkKind.StepPort,
                    output.Owner.StepId,
                    output.PortName,
                    $"{output.Owner.Header}.{output.PortName}");
            input.Owner.Model!.SetLink(input.PortName, newLink);

            input.IsConnected = true;
            output.IsConnected = true;
            Connections.Add(new CanvasConnectionViewModel(input, output));

            PushUndo(new ConnectCommand(input.Owner.Model!, input.PortName, newLink, oldLink));

            // SetLink 已递增 FlowModel.Version，重编译由既有的运行前版本检查接管，画布不自己触发
        }

        private void OnDisconnectConnector(object? parameter)
        {
            if (parameter is not CanvasConnectorViewModel connector) return;

            // 从输出侧断开没有语义（一个输出可喂多个输入），只处理输入侧
            if (!connector.IsInput) return;
            if (connector.Owner.Model == null) return;

            // 段3：记录旧 LinkReference 以支持撤销
            LinkReference? oldLink = null;
            if (connector.Owner.Model.LinkedSources.TryGetValue(connector.PortName, out var existing) && existing != null)
                oldLink = existing;

            RemoveExistingConnection(connector);
            connector.Owner.Model.RemoveLink(connector.PortName);

            // 只在确实断开了一条线时才入栈：空断线（端口本就没连）压栈会让 Undo 重连一条不存在的线
            if (oldLink != null)
                PushUndo(new DisconnectCommand(connector.Owner.Model, connector.PortName, oldLink));
        }

        private void RemoveExistingConnection(CanvasConnectorViewModel input)
        {
            var stale = Connections.Where(c => c.Input == input).ToList();
            if (stale.Count == 0) return;

            foreach (var connection in stale)
                Connections.Remove(connection);

            input.IsConnected = false;

            // 输出端口可以喂多个输入，只有彻底没有连线了才置回未连接
            foreach (var output in stale.Select(c => c.Output).Distinct())
            {
                if (!Connections.Any(c => c.Output == output))
                    output.IsConnected = false;
            }
        }

        // ==================================================================
        //  段3：拖拽改序 / 跨分支移分支
        //
        //  几何规则（项目主列纵向流）：
        //  · 改序：抬起时按被拖节点中心 Y 与同分支其他节点中心 Y 升序排，
        //    被拖节点在排序后的位置即新下标；与原下标不同则提交 Move。
        //  · 跨分支：按被拖节点中心 X 落在哪条泳道判定目标 Owner；
        //    目标 Owner != 原 Owner 且都在本层可见范围内 → 同层跨分支移动。
        //  · 多选拖动只对「主导节点」（被拖列表第一个 Step 节点）判定改序/移分支，
        //    其余节点跟随——它们与主导节点的相对位置由 Nodify 自动保持。
        //  · 拖拽过程不触发 RebuildInPlace（坐标写回只进 FlowLayoutStore），
        //    抬起时若提交 Move / Remove+Insert 才触发 CollectionChanged → Version++ → 重画。
        // ==================================================================

        /// <summary>
        /// 从 Nodify 传入的拖拽参数里提取所有被拖 Step 节点。
        /// 参数实际是 IEnumerable&lt;ItemContainer&gt;——ItemContainer 是 Nodify 类型，
        /// 视图模型不该直接引用它，故用非泛型 IEnumerable + 反射读 DataContext 提取节点。
        /// </summary>
        private static List<CanvasNodeViewModel> ExtractDraggedStepNodes(object? parameter)
        {
            var result = new List<CanvasNodeViewModel>();
            if (parameter is not System.Collections.IEnumerable enumerable) return result;

            // 单个对象被 Nodify 包成 IEnumerable 的情况：兜底直接读 DataContext
            if (parameter is not System.Collections.IList && parameter is not CanvasNodeViewModel)
            {
                var dc = parameter.GetType().GetProperty("DataContext")?.GetValue(parameter);
                if (dc is CanvasNodeViewModel single && single.Kind == CanvasNodeKind.Step)
                    result.Add(single);
                if (dc != null) return result;
            }

            foreach (var item in enumerable)
            {
                if (item == null) continue;
                var dc = item.GetType().GetProperty("DataContext")?.GetValue(item);
                if (dc is CanvasNodeViewModel cvm && cvm.Kind == CanvasNodeKind.Step)
                    result.Add(cvm);
            }
            return result;
        }

        private void OnItemsDragStarted(object? parameter)
        {
            // 清掉上一轮未结清的快照（异常路径里 DragStarted 后没收到 Completed 的兜底）
            _dragStartedSnapshot = null;
            DropIndicatorY = -1;
            DropHighlightLaneId = Guid.Empty;

            if (_attachedFlow == null) return;

            var dragged = ExtractDraggedStepNodes(parameter);
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
            // 先把指示线收起来——无论是否提交了改序/移分支，抬起即结束拖拽
            DropIndicatorY = -1;
            DropHighlightLaneId = Guid.Empty;

            var snapshot = _dragStartedSnapshot;
            _dragStartedSnapshot = null;
            if (snapshot == null || snapshot.Count == 0 || _attachedFlow == null) return;

            // 多选拖动只对主导节点判定改序/移分支
            var primary = snapshot[0];
            var primaryNode = primary.Node;
            if (primaryNode.Model == null) return;

            // 拖拽期间拓扑不变（Steps 集合没动），所以 _topology 还是 DragStarted 时的那份
            // 取主导节点当前中心 X / Y
            double centerX = primaryNode.Location.X + NodeWidth / 2;
            double centerY = primaryNode.Location.Y + NodeHeight / 2;

            // 1. 跨分支判定：找中心 X 落在哪条泳道
            var targetOwner = ResolveOwnerByX(primary, centerX);

            if (targetOwner != null && !ReferenceEquals(targetOwner, primary.Owner))
            {
                // 同层跨分支移动：Remove + Insert
                // 但只允许移到本层可见的目标 Owner（已由 ResolveOwnerByX 限定为本层集合）
                int toIndex = ComputeInsertIndexByY(targetOwner, centerY, primaryNode.Model);
                int actualToIndex = Math.Min(toIndex, targetOwner.Count);

                var cmd = new MoveBranchCommand(
                    primaryNode.Model,
                    primary.Owner, primary.OriginalIndex,
                    targetOwner, actualToIndex);
                // 先执行（Remove + Insert 触发 CollectionChanged → Version++ → RebuildInPlace）
                // 再入栈——PushUndo 的语义是「记录已发生操作」，调用方负责实际写回
                cmd.Redo();
                PushUndo(cmd);
                return;
            }

            // 2. 改序判定：在原 Owner 中按中心 Y 算新下标
            int newIndex = ComputeInsertIndexByY(primary.Owner, centerY, primaryNode.Model);
            if (newIndex != primary.OriginalIndex
                && newIndex >= 0 && newIndex < primary.Owner.Count)
            {
                var cmd = new ReorderCommand(primary.Owner, primary.OriginalIndex, newIndex);
                cmd.Redo();   // Move(from, to) → CollectionChanged → Version++ → RebuildInPlace
                PushUndo(cmd);
            }

            // 否则：只坐标移动，不入栈
        }

        /// <summary>
        /// 按中心 X 找出主导节点应落入的本层步骤集合。
        /// 主列区域（不在任何泳道内）→ <see cref="_layerList"/>；
        /// 落在某泳道内 → 该泳道 Branch.Steps；
        /// 都不在 → null（视为无效拖拽，不提交跨分支移动）。
        /// </summary>
        private ObservableCollection<StepModel>? ResolveOwnerByX(DragSnapshot primary, double centerX)
        {
            // 主列 = _layerList；它在顶层是 FlowModel.Steps，在子层是某分支的 Steps。
            // 主列节点的外接框：取所有主列节点的 X 范围 + NodeWidth
            var mainNodes = _visibleSteps
                .Where(n => n.Kind == CanvasNodeKind.Step
                            && _topology.TryGet(n.StepId, out var pos)
                            && ReferenceEquals(pos!.Owner, _layerList))
                .ToList();

            double mainLeft = mainNodes.Count > 0
                ? mainNodes.Min(n => n.Location.X) - LanePadding
                : double.MinValue;
            double mainRight = mainNodes.Count > 0
                ? mainNodes.Max(n => n.Location.X) + NodeWidth + LanePadding
                : double.MaxValue;

            if (centerX >= mainLeft && centerX <= mainRight)
                return _layerList;

            // 泳道：找中心 X 落在 [lane.Location.X, lane.Location.X + LaneWidth] 内的
            foreach (var lane in Nodes.Where(n => n.Kind == CanvasNodeKind.Lane))
            {
                if (lane.Branch?.Steps == null) continue;
                double laneLeft = lane.Location.X;
                double laneRight = lane.Location.X + lane.LaneWidth;
                if (centerX >= laneLeft && centerX <= laneRight)
                    return lane.Branch.Steps;
            }

            return null;
        }

        /// <summary>
        /// 在 owner 中按中心 Y 升序排（含被拖节点），算出被拖节点应处的下标。
        /// 实现思路：把 owner 中其他节点按当前 Location.Y 升序排（用 _nodeMap 反查坐标），
        /// 数出有几个节点的中心 Y 小于被拖节点的中心 Y，那即是被拖节点应处的下标。
        /// </summary>
        private int ComputeInsertIndexByY(
            ObservableCollection<StepModel> owner,
            double draggedCenterY,
            StepModel draggedStep)
        {
            int count = 0;
            foreach (var s in owner)
            {
                if (s == null) continue;
                if (ReferenceEquals(s, draggedStep)) continue;

                if (_nodeMap.TryGetValue(s.StepID, out var node))
                {
                    double otherY = node.Location.Y + NodeHeight / 2;
                    if (otherY < draggedCenterY)
                        count++;
                }
            }
            return count;
        }

        /// <summary>返回 owner 在本层 _layerList/泳道序列里的序号，仅供跨分支判定边界用</summary>
        private int IndexInLayerOrder(ObservableCollection<StepModel> owner)
        {
            if (ReferenceEquals(owner, _layerList)) return 0;
            int i = 1;
            foreach (var lane in Nodes.Where(n => n.Kind == CanvasNodeKind.Lane))
            {
                if (lane.Branch?.Steps != null && ReferenceEquals(lane.Branch.Steps, owner))
                    return i;
                i++;
            }
            return -1;
        }

        // ==================================================================
        //  下钻 / 回跳 / 与流程栏联动
        // ==================================================================

        /// <summary>
        /// 双击下钻：双击容器步骤进入它的第一条分支，双击泳道直接进入该分支。
        /// 之所以给「整条祖先链」而不是只给父级：循环体里取 For.Index 这类连线，
        /// 生产方是祖先，必须让它在本层可见才能连，光有父级在嵌套三层时就不够用了。
        /// </summary>
        private void OnDrillDown(CanvasNodeViewModel? node)
        {
            if (_rebuilding || _attachedFlow == null) return;

            StepCollection? branch = null;
            StepModel? container = null;

            switch (node?.Kind)
            {
                case CanvasNodeKind.Lane:
                    branch = node.Branch;
                    break;

                case CanvasNodeKind.Step when node.Model is IContainerStep ic
                    && ic.Children != null && ic.Children.Count > 0:
                    container = node.Model;
                    branch = ic.Children[0];
                    break;
            }

            if (branch?.Steps == null) return;

            if (container == null && !TryFindOwnerOfBranch(branch, out container)) return;
            if (container is not IContainerStep containerStep) return;

            var segment = MakeSegment(container, containerStep, branch);
            var path = _layerPath.Length == 0 ? segment : $"{_layerPath}/{segment}";

            SwitchCurrentStep(container);
            NavigateToPath(path, Guid.Empty);
        }

        /// <summary>在本层主列里找某条分支的宿主容器（泳道只记分支引用，不记宿主）</summary>
        private bool TryFindOwnerOfBranch(StepCollection branch, out StepModel? container)
        {
            container = null;
            foreach (var step in _layerList)
            {
                if (step is not IContainerStep ic || ic.Children == null) continue;
                if (IndexOfBranch(ic, branch) >= 0)
                {
                    container = step;
                    return true;
                }
            }
            return false;
        }

        private void OnNavigateCrumb(CanvasCrumbViewModel? crumb)
        {
            if (crumb == null || crumb.IsCurrent) return;
            if (string.Equals(crumb.PathKey, _layerPath, StringComparison.Ordinal)) return;

            NavigateToPath(crumb.PathKey, Guid.Empty);

            // 回跳后把「本层所属容器」报成当前步骤；回顶层则清空
            var owner = _layerTrail.Count > 0 ? _layerTrail[^1].Container : null;
            SwitchCurrentStep(owner);
        }

        /// <summary>
        /// 切到指定层路径。路径解析失败时退回顶层（不抛异常）——
        /// 图纸在别处被改（分支删除、容器搬走）是正常事件，画布不能因此卡住。
        /// </summary>
        private void NavigateToPath(string pathKey, Guid selectStepId)
        {
            var flow = _attachedFlow;
            if (flow == null) return;

            var probe = ResolveTrail(flow, pathKey);
            if (probe == null)
            {
                pathKey = string.Empty;
                probe = ResolveTrail(flow, pathKey);
            }

            _layerList = probe ?? flow.Steps;
            _layerPath = string.Join("/", _layerSegments);
            _pendingSelectId = selectStepId;

            RebuildLayer();
        }

        /// <summary>
        /// 把当前步骤报给工作区（流程栏 TreeView 会据此选中并展开）。
        /// 包 try/catch 是必要的：WorkspaceContext.SwitchStep 对"不属于当前流程"直接抛异常，
        /// 而画布拿到的祖先容器有可能在别的渲染周期里已被移出图纸。
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

        /// <summary>
        /// 外部（TreeView/属性栏）改了当前步骤 → 画布跟着切层并选中。
        /// 联动方案 A：本方向只读 Workspace 的 CurrentStep，不新增任何总线事件通道。
        /// </summary>
        private void OnCurrentStepChanged()
        {
            if (_suppressStepSync || _rebuilding) return;

            var flow = _workspace.CurrentFlow;
            var step = _workspace.CurrentStep;
            if (flow == null || step == null) return;

            if (!_topology.TryGet(step.StepID, out var position))
            {
                // 图纸刚变过而本层尚未重画，快照里没有它：补建一次拓扑再试
                _topology = FlowTopology.Build(flow);
                if (!_topology.TryGet(step.StepID, out position)) return;
            }

            var target = BuildPathKey(position);
            if (string.Equals(target, _layerPath, StringComparison.Ordinal))
            {
                _pendingSelectId = step.StepID;
                RebuildInPlace();
                return;
            }

            NavigateToPath(target, step.StepID);
        }
    }

    /// <summary>
    /// 面包屑的一级。Label 给人看，PathKey 给导航用（"" 表示顶层）。
    /// </summary>
    public class CanvasCrumbViewModel : BindableBase
    {
        public CanvasCrumbViewModel(string label, string pathKey)
        {
            Label = label;
            PathKey = pathKey;
        }

        public string Label { get; }

        public string PathKey { get; }

        /// <summary>是否当前层：当前级不可点（点了没意义），模板据此去下划线、变灰</summary>
        public bool IsCurrent
        {
            get => field;
            set => SetProperty(ref field, value);
        }
    }

    /// <summary>
    /// 拖线中的临时状态。属性名与 Nodify PendingConnection 的依赖属性同名，
    /// 以便直接套用控件默认模板。
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
