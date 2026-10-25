using Core.Interfaces;
using GongSolutions.Wpf.DragDrop;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using UI.CustomControl;
using Core.Events;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.Views.DialogViews;   // AutoPortConfigView：无自定义视图插件的框架参数层

namespace VisionMaster.ViewModels
{
    public class ProcessViewModel : BindableBase, IDropTarget
    {
        private readonly IDialogService dialogService;
        // 判据统一收在 ConditionStep.IsIfLike，避免这里再写一份 Contains 造成口径漂移
        public bool IsIfNodeSelected => SelectStep is ConditionStep step && step.IsIfLike;

        /// <summary>
        /// 右键菜单「添加 Case 分支」的可见性判据：当前选中的是不是分支匹配容器。
        /// 用具体类型而不是"含 Case 分支"的内容嗅探——CaseStep 的身份由类本身定，与 IsIfLike 同一口径。
        /// 刷新点：选中步骤变化（SelectStep setter）。
        /// </summary>
        public bool IsCaseNodeSelected => SelectStep is CaseStep;

        /// <summary>右键菜单「禁用/启用」的标题：跟随当前选中步骤的禁用状态</summary>
        public string DisableToggleHeader => CurrentSelectedStepModel?.IsDisEnable == true ? "启用" : "禁用";

        /// <summary>
        /// 右键菜单「切换断点」的标题：跟随当前选中步骤的断点状态（照 DisableToggleHeader 先例）。
        /// 刷新点：选中步骤变化（SelectStep setter）与经本命令取反之后。
        /// </summary>
        public string BreakpointToggleHeader => CurrentSelectedStepModel?.IsBreakpoint == true ? "取消断点" : "设置断点";

        /// <summary>
        /// 右键菜单「断点」项的勾选态（菜单 C 方案的 ✓ 标记）。
        /// 与 BreakpointToggleHeader 同源、同刷新点：选中步骤变化 + 本命令取反之后。
        /// </summary>
        public bool IsBreakpointSet => CurrentSelectedStepModel?.IsBreakpoint == true;

        public IWorkspaceManager Workspace { get; init; }

        /// <summary>
        /// 构造时的 Workspace INotifyPropertyChanged 引用，Dispose 时用于解绑。
        /// IWorkspaceManager 是业务接口不继承 INotifyPropertyChanged，只有运行时实例才是 INPC。
        /// </summary>
        private readonly INotifyPropertyChanged? _workspaceChanged;

        public AsyncDelegateCommand<ModuleCommandAction?> ModuleActionCommand { get; init; }

        // ------------------------------------------------------------------
        //  并行分组：分支结构动作（增 / 删 / 改名）
        //  2026-10-09 用户裁决：分支结构编辑从参数面板下沉到流程栏右键菜单，就地单步完成。
        //  三个命令刻意**不进 ModuleActionCommand** 那条"按当前选中项取参"的链：
        //  分支胶囊不是算子（R25：不可选中、左键被拦、双击不派发），命令靶由 CommandParameter
        //  明确给出——组头菜单带 SelectStep，胶囊菜单带"命中的那条分支"。
        // ------------------------------------------------------------------

        /// <summary>组头右键「添加分支」的命令靶 = 当前选中的并行分组（选中组头时才出该项）。
        /// 刻意用 object 而不是 ParallelStep：CommandParameter 绑 SelectStep（object），
        /// WPF 的 CanExecute 会把任何当前选中对象（ActionStep / ConditionStep / null）塞进来——
        /// 强类型泛型命令当场 InvalidCastException（2026-10-10 真机：选中算子时炸在
        /// SelectStep setter 的属性通知链里），判型收在处理器里。</summary>
        public DelegateCommand<object?> AddParallelBranchCommand { get; init; }

        /// <summary>分支胶囊右键「删除本条分支」的命令靶 = 命中的那条分支（不是当前选中项）。
        /// 同上用 object：胶囊菜单的 CommandParameter 绑 PlacementTarget.DataContext，
        /// XAML 编译器不检查类型，运行期才判。</summary>
        public DelegateCommand<object?> RemoveParallelBranchCommand { get; init; }

        /// <summary>分支胶囊右键「重命名本条分支」的命令靶 = 命中的那条分支（不是当前选中项）</summary>
        public DelegateCommand<object?> RenameParallelBranchCommand { get; init; }

        /// <summary>组头右键「重命名分组」的命令靶 = 当前选中的并行分组（2026-10-09：随参数面板收编 FlatPropertyGrid，分组名改走右键）。同 AddParallelBranchCommand 用 object。</summary>
        public DelegateCommand<object?> RenameParallelGroupCommand { get; init; }

        /// <summary>组头菜单「添加分支」的可见性判据（刷新点同 IsIfNodeSelected：SelectStep setter）</summary>
        public bool IsParallelNodeSelected => SelectStep is ParallelStep;

        /// <summary>Case 分支胶囊右键「重命名本条分支」的命令靶 = 命中的那条 Case 分支。
        /// 同并行三命令用 object：胶囊菜单的 CommandParameter 绑 PlacementTarget.DataContext，
        /// XAML 编译器不检查类型，运行期才判（R27）。</summary>
        public DelegateCommand<object?> RenameCaseBranchCommand { get; init; }

        /// <summary>Case 分支胶囊右键「删除本条分支」的命令靶 = 命中的那条 Case 分支。
        /// 默认分支（Else）不可删、至少保留一条 Case 分支（编译期同样守）。</summary>
        public DelegateCommand<object?> RemoveCaseBranchCommand { get; init; }

        /// <summary>
        /// 流程栏右键「编辑绑定…」：打开变量绑定弹窗，锚点是当前选中步骤。
        ///
        /// 与画布右键走同一个弹窗、同一份参数（画布侧见 FlowCanvasView.OnModuleLinkRequested，
        /// 传的也是 <c>TargetStep</c>）——弹窗自己按 TargetStep 构建上游候选树、写回 LinkedSources，
        /// 所以这里不再重复"哪些端口可绑"的判断。
        ///
        /// 不做运行锁：与画布入口一致（绑定的写回在弹窗里，运行期改绑定影响的是下一轮编译）。
        /// </summary>
        public DelegateCommand OpenBindingCommand { get; }

        /// <summary>
        /// 流程栏右键「复制」：把当前选中步骤快照进 FlowClipboard（与画布共用同一份剪贴板）。
        ///
        /// 复制不改图纸、不受运行锁限制——口径与画布 <c>OnCopySelected</c> 一致
        /// （"看"同族；粘贴才是结构编辑）。
        /// </summary>
        public DelegateCommand CopyStepCommand { get; }

        public object SelectStep
        {
            get => field;
            set
            {
                // 分支卡片（StepCollection：分支 1/分支 2/If/Else/循环体）不是算子——它没有自己的参数，
                // 条件在容器上配置、名字改不改由容器面板负责。这里**清空选中**而不是保留旧值：
                // 保留旧值的后果是"界面上看着选中了分支，模块参数/删除等命令却打在旧算子上"（错靶）。
                // 视图侧还有一道拦截（ProcessView 预览鼠标事件），两道合起来保证分支卡片进不了命令链。
                if (value is StepCollection)
                {
                    SelectStep = null;
                    return;
                }

                SetProperty(ref field, value);
                CurrentSelectedStepModel = value as StepModel;
                RaisePropertyChanged(nameof(IsIfNodeSelected));
                RaisePropertyChanged(nameof(IsCaseNodeSelected));
                RaisePropertyChanged(nameof(IsParallelNodeSelected));
                RaisePropertyChanged(nameof(DisableToggleHeader));
                RaisePropertyChanged(nameof(BreakpointToggleHeader));
                RaisePropertyChanged(nameof(IsBreakpointSet));

                // 反向推送来的值本来就是 Workspace 自己发出来的，再 SwitchStep 一次纯属回环
                if (!_syncingFromWorkspace)
                    Workspace.SwitchStep(CurrentSelectedStepModel);
            }
        }
        StepModel CurrentSelectedStepModel;

        /// <summary>
        /// true = 正在把 Workspace.CurrentStep 推给 SelectStep。
        /// 联动只有一条总线：Workspace.CurrentStep。画布（或任何编辑端）改当前步骤 → 这里收到通知
        /// → 更新 SelectStep → TreeViewBehavior 的双向绑定把流程树选中并逐级展开过去。
        /// 反向（流程树点选 → SwitchStep）走 SelectStep 的 setter，两条路径靠这个标志互相隔断。
        /// </summary>
        private bool _syncingFromWorkspace;

        /// <summary>
        /// 画布编辑门禁（2026-10-11 收口为单一出口）：运行态镜像与豁免表都在 EditGate 里，
        /// 流程栏与画布共用同一份判据。本 VM 只保留"新一轮运行开始 → 失败跳转基线清零"的
        /// 状态转换监听（见 OnMainRunStateChanged）。
        /// </summary>
        private readonly EditGate _editGate = new();

        /// <summary>运行锁镜像（门禁的只读投影：ModuleActionCommand / Drop / 结构命令共用）</summary>
        private bool IsRunLocked => _editGate.IsRunLocked;

        /// <summary>运行锁拦截的提示文案（ModuleActionCommand / Drop / 分支结构命令共用一份，与门禁同源）</summary>
        private const string RunLockedMessage = EditGate.RunLockedMessage;

        /// <summary>
        /// 运行时间实时刷新定时器：
        /// 引擎只记录步骤起始时间戳（LastRunStartTimestamp），运行中耗时由 UI 定时器计算写入 CurrentRunTimeMs，
        /// 否则毫秒级步骤的耗时显示永远停在初始值 0
        /// </summary>
        private readonly System.Windows.Threading.DispatcherTimer _runTimeTimer;

        public ProcessViewModel(IWorkspaceManager workspace, IDialogService dialogService)
        {
            this.Workspace = workspace;
            this.dialogService = dialogService;
            ModuleActionCommand = new(ModuleActionAsync);
            SearchNextCommand = new(SearchNext);
            SearchPreviousCommand = new(SearchPrevious);
            AddParallelBranchCommand = new(AddParallelBranch);
            RemoveParallelBranchCommand = new(RemoveParallelBranch);
            RenameParallelBranchCommand = new(RenameParallelBranch);
            RenameParallelGroupCommand = new(RenameParallelGroup);
            RenameCaseBranchCommand = new(RenameCaseBranch);
            RemoveCaseBranchCommand = new(RemoveCaseBranch);
            OpenBindingCommand = new(OpenBindingForSelectedStep);
            CopyStepCommand = new(CopySelectedStep);

            _workspaceChanged = workspace as INotifyPropertyChanged;

            _runTimeTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(200),
            };
            _runTimeTimer.Tick += OnRunTimeTimerTick;

            // 订阅与计时器统一由 Activate 挂接；View 的 Loaded / Unloaded 会成对调用
            // Activate / Deactivate。构造即视为"已入树"，所以这里先挂一次。
            Activate();
        }

        private bool _subscribed;

        /// <summary>
        /// 挂接全局订阅与运行耗时计时器（幂等）。
        ///
        /// 为什么不做成一次性的 Dispose：AvalonDock 切换标签页、隐藏面板都会让 View 触发
        /// Unloaded（之后不会自动 Loaded），一次性清理会让面板恢复显示后失去流程联动。
        /// 因此清理必须是**可逆**的挂/摘——离树时摘干净让旧 VM 可被 GC，入树时重新挂上。
        /// </summary>
        public void Activate()
        {
            if (_subscribed) return;
            _subscribed = true;

            GlobalEventBus.Subscribe<LinkPathEvent>(OnLinkPathEvent);
            // 用命名方法而非 lambda：GlobalEventBus.Unsubscribe 依赖委托的 Target+Method 匹配
            GlobalEventBus.Subscribe<MainRunState>(OnMainRunStateChanged);

            // 编辑门禁的运行态订阅与本体同挂同摘（离树时一并可逆清理）
            _editGate.Activate();

            if (_workspaceChanged != null)
                _workspaceChanged.PropertyChanged += OnWorkspacePropertyChanged;

            _runTimeTimer.Start();
        }

        /// <summary>
        /// 摘除全部长生命周期订阅并停表（幂等）。
        /// 摘干净后本 VM 不再被 GlobalEventBus 静态表 / Workspace 单例 / 计时器泵引用，可被 GC。
        /// </summary>
        public void Deactivate()
        {
            if (!_subscribed) return;
            _subscribed = false;

            _runTimeTimer.Stop();

            GlobalEventBus.Unsubscribe<LinkPathEvent>(OnLinkPathEvent);
            GlobalEventBus.Unsubscribe<MainRunState>(OnMainRunStateChanged);

            _editGate.Deactivate();

            if (_workspaceChanged != null)
                _workspaceChanged.PropertyChanged -= OnWorkspacePropertyChanged;
        }

        private void OnMainRunStateChanged(MainRunState state)
        {
            // 新一轮运行开始（从未启动进入任一运行/暂停态）：清空失败自动跳转的基线，
            // 上一轮的失败不再拦住本轮的自动定位（"同一失败不重复跳"的口径见 DetectNewFailures）
            if (_lastObservedRunState == MainRunState.NotStarted && state != MainRunState.NotStarted)
                _failedJumpBaseline.Clear();

            _lastObservedRunState = state;
        }

        /// <summary>上一轮观察到的运行态（失败跳转基线重置的转换判据）</summary>
        private MainRunState _lastObservedRunState = MainRunState.NotStarted;

        private void OnRunTimeTimerTick(object? sender, EventArgs e)
        {
            TickRunningTimes(Workspace.CurrentFlow?.Steps);

            // 执行统计两行（评审清单 B 包交互补齐）顺带刷新：tick 本就每 200ms 过一遍，
            // 统计读时现算，这里只负责发通知，不另起扫描
            RaisePropertyChanged(nameof(StepCountText));
            RaisePropertyChanged(nameof(RunTimeText));

            // 失败自动跳转（三视角评审第二批）：发现新出现的失败步骤就切过去并给一次性提示——
            // 每轮只跳扫描序里的第一个新失败，同一失败不重复跳（基线口径见 DetectNewFailures）
            var failed = DetectNewFailures(Workspace.CurrentFlow?.Steps, _failedJumpBaseline);
            if (failed == null) return;

            // 开关关闭：扫描照跑（上面的基线照记，重新打开后历史失败不补跳），只是不切选中
            if (!AutoJumpToFailure) return;

            try
            {
                Workspace.SwitchStep(failed);
            }
            catch (InvalidOperationException)
            {
                return;   // 图纸刚切换等边缘场景：静默放弃，下一轮再说
            }

            ShowFeedback($"发现失败步骤「{failed.StepName}」，已自动定位", false);
        }

        /// <summary>定时器一轮的实际动作（internal：断言宿主直接驱动同一逻辑，不必等 Dispatcher 泵）</summary>
        internal void RunRuntimeTick() => OnRunTimeTimerTick(this, EventArgs.Empty);

        private void OnWorkspacePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(IWorkspaceManager.CurrentStep)) return;

            var step = Workspace.CurrentStep;
            if (ReferenceEquals(step, SelectStep)) return;

            _syncingFromWorkspace = true;
            try
            {
                SelectStep = step;
            }
            finally
            {
                _syncingFromWorkspace = false;
            }
        }

        /// <summary>
        /// 递归刷新运行中步骤的实时耗时（含容器内嵌套步骤）。
        /// 判据是 State==Running（2026-10-10 前只认 IsRunningFocus）：并行分支线程走
        /// UpdateStepRuntimeState 的分支快捷路径——只写 State+计时、刻意不碰 FocusedStep
        /// （并发不变量 1：多分支并发读写单指针必然撕裂），分支步骤永远没有 IsRunningFocus，
        /// 旧判据下真并行会话里分支内步骤的实时耗时永远停在 0、跑完才一次性蹦终值。
        /// 终态（Success/Failed）不满足条件：引擎冻结的最终值不会被本方法覆盖。
        /// internal static：纯计算，便于断言程序直接调用（与 FlowCompiler.CheckLinkOrder 同一理由）。
        /// </summary>
        internal static void TickRunningTimes(IEnumerable<StepModel> steps)
        {
            if (steps == null) return;

            foreach (var step in steps)
            {
                if (step.State == StepState.Running
                    && step.LastRunStartTimestamp.HasValue)
                {
                    step.CurrentRunTimeMs = step.LiveElapsedMs();
                }

                if (step is IContainerStep container && container.Children != null)
                {
                    foreach (var branch in container.Children)
                        TickRunningTimes(branch.Steps);
                }
            }
        }

        /// <summary>
        /// 失败自动跳转的探测：从步骤树里找出"新出现"的 State==Failed 步骤。
        /// 基线集合记录已处理过的失败（含本轮没跳到的——每轮只跳第一个，其余不迟到补跳，
        /// 免得一次多点失败把用户来回拽）；基线由调用方持有，在新一轮运行开始时清空
        /// （OnMainRunStateChanged）。返回本轮应跳转的那一个（扫描序第一个新失败；没有则 null）。
        /// internal static：纯计算 + 显式基线入参，断言宿主直调（与 TickRunningTimes 同一理由）。
        /// </summary>
        internal static StepModel? DetectNewFailures(IEnumerable<StepModel>? steps, HashSet<Guid> baseline)
        {
            if (steps == null) return null;

            StepModel? first = null;
            Walk(steps);
            return first;

            void Walk(IEnumerable<StepModel> list)
            {
                foreach (var step in list)
                {
                    if (step == null) continue;

                    // Add=false = 基线里已有（此前已处理过），不算新失败
                    if (step.State == StepState.Failed && baseline.Add(step.StepID) && first == null)
                        first = step;

                    if (step is IContainerStep container && container.Children != null)
                    {
                        foreach (var branch in container.Children)
                        {
                            if (branch?.Steps != null)
                                Walk(branch.Steps);
                        }
                    }
                }
            }
        }

        // ==================================================================
        //  流程栏执行统计（评审清单 B 包交互补齐）：底部两行只读信息
        //  第 1 行「共 N 步 · 最深 d 层」；第 2 行「本轮总耗时 X ms · 最慢：步骤名 Y ms」，
        //  无任何运行数据（LastRunTimeMs 全 0）时第二行显示"尚未运行"。
        //  递归口径与 TickRunningTimes / CollectSearchMatches 同一条：容器经
        //  IContainerStep.Children 逐分支下钻；步骤数含容器本身，顶层 = 第 1 层。
        // ==================================================================

        /// <summary>执行统计快照：步骤总数 / 最大深度 / 总耗时 / 最慢步骤（纯数据载体）</summary>
        internal sealed record FlowRunStats(
            int TotalSteps, int MaxDepth, double TotalMs, double SlowestMs, string? SlowestName);

        /// <summary>第 1 行：步骤规模。读时现算（流程树的量级遍历代价可忽略），刷新由 200ms tick 顺带通知。</summary>
        public string StepCountText
        {
            get
            {
                var stats = ComputeRunStats(Workspace?.CurrentFlow?.Steps);
                return $"共 {stats.TotalSteps} 步 · 最深 {stats.MaxDepth} 层";
            }
        }

        /// <summary>第 2 行：耗时汇总。LastRunTimeMs 是 [RuntimeState] 的冻结终值，总和 ≤ 0 = 尚未运行。
        /// 口径：总耗时 = Sum(LastRunTimeMs)；最慢 = Max 及其步骤名（与流程树卡片同用 "0.#" 格式）。</summary>
        public string RunTimeText
        {
            get
            {
                var stats = ComputeRunStats(Workspace?.CurrentFlow?.Steps);
                if (stats.TotalMs <= 0)
                    return "尚未运行";
                return $"本轮总耗时 {stats.TotalMs.ToString("0.#")} ms · 最慢：{stats.SlowestName ?? "—"} {stats.SlowestMs.ToString("0.#")} ms";
            }
        }

        /// <summary>
        /// 递归聚合整棵步骤树的执行统计（顶层 = 第 1 层，每下钻一层分支 +1）。
        /// internal static：纯计算 + 显式入参，断言宿主直调（与 DetectNewFailures 同一理由）。
        /// </summary>
        internal static FlowRunStats ComputeRunStats(IEnumerable<StepModel>? steps) => ComputeRunStats(steps, 1);

        private static FlowRunStats ComputeRunStats(IEnumerable<StepModel>? steps, int depth)
        {
            int total = 0, maxDepth = 0;
            double totalMs = 0, slowestMs = 0;
            string? slowestName = null;

            if (steps != null)
            {
                foreach (var step in steps)
                {
                    if (step == null) continue;

                    total++;
                    if (depth > maxDepth) maxDepth = depth;

                    totalMs += step.LastRunTimeMs;
                    if (step.LastRunTimeMs > slowestMs)
                    {
                        slowestMs = step.LastRunTimeMs;
                        slowestName = step.StepName;
                    }

                    if (step is IContainerStep container && container.Children != null)
                    {
                        foreach (var branch in container.Children)
                        {
                            if (branch?.Steps == null) continue;

                            var nested = ComputeRunStats(branch.Steps, depth + 1);
                            total += nested.TotalSteps;
                            if (nested.MaxDepth > maxDepth) maxDepth = nested.MaxDepth;
                            totalMs += nested.TotalMs;
                            if (nested.SlowestMs > slowestMs)
                            {
                                slowestMs = nested.SlowestMs;
                                slowestName = nested.SlowestName;
                            }
                        }
                    }
                }
            }

            return new FlowRunStats(total, maxDepth, totalMs, slowestMs, slowestName);
        }

        // ==================================================================
        //  流程栏搜索定位（三视角评审第二批）
        // ==================================================================

        /// <summary>搜索框文本（按步骤名 / 注释匹配，回车或「下一个」触发定位）</summary>
        public string SearchText
        {
            get => field;
            set => SetProperty(ref field, value);
        } = string.Empty;

        /// <summary>「下一个」：循环定位下一个匹配步骤。DelegateCommand&lt;object?&gt;（R27：绑定无参，判型收在处理器）</summary>
        public DelegateCommand<object?> SearchNextCommand { get; }

        /// <summary>「上一个」（评审清单 B 包交互补齐）：同一次匹配列表反向回绕。与 SearchNextCommand 同用 object（R27）</summary>
        public DelegateCommand<object?> SearchPreviousCommand { get; }

        /// <summary>失败自动跳转的基线：已处理过的失败步骤 Id（新一轮运行开始时清空）</summary>
        private readonly HashSet<Guid> _failedJumpBaseline = new();

        /// <summary>
        /// 失败自动跳转开关（评审清单 B 包交互补齐，默认开）：关闭时扫描照跑——
        /// DetectNewFailures 照常执行、基线照记，只是不再切选中；重新打开后从"下一个新失败"
        /// 恢复跳转，关闭期间记入基线的失败不补跳（避免打开瞬间整批旧失败把用户来回拽）。
        /// </summary>
        public bool AutoJumpToFailure
        {
            get => field;
            set => SetProperty(ref field, value);
        } = true;

        /// <summary>
        /// 搜索定位：遍历当前流程（容器递归走 IContainerStep.Children，跳过 StepCollection
        /// 自身——分支卡片进不了 SelectStep，定位它只会落空），按 StepName / Description 的
        /// Contains（OrdinalIgnoreCase）匹配。命中即 Workspace.SwitchStep——树展开定位与画布
        /// 滚入由既有联动总线完成，这里零新增联动；再按一次跳下一个（到尾回绕）；
        /// 无命中给提示、不换选中。
        /// </summary>
        private void SearchNext(object? parameter) => SearchLocate(forward: true);

        /// <summary>「上一个」（评审清单 B 包交互补齐）：同一次匹配列表反向回绕——
        /// 匹配列表每次现算（口径与「下一个」一致，等价于 SearchText 变化时重算），
        /// 反向在头回绕到最后一个；当前不在命中列表时从最后一个开始。</summary>
        private void SearchPrevious(object? parameter) => SearchLocate(forward: false);

        /// <summary>搜索定位公共核（+1 = 下一个 / -1 = 上一个），命中列表与回绕口径见 SearchNext 注释</summary>
        private void SearchLocate(bool forward)
        {
            var flow = Workspace?.CurrentFlow;
            if (flow == null) return;

            string query = (SearchText ?? string.Empty).Trim();
            if (query.Length == 0) return;

            var matches = new List<StepModel>();
            CollectSearchMatches(flow.Steps, query, matches);
            if (matches.Count == 0)
            {
                ShowFeedback($"没有匹配「{query}」的步骤", false);
                return;
            }

            // 命中列表里已有当前选中 → 按方向走一步（尾/头回绕）；不在 → 正向从第一个、反向从最后一个开始
            int index = matches.FindIndex(s => ReferenceEquals(s, Workspace.CurrentStep));
            int target = forward
                ? (index + 1) % matches.Count
                : (index <= 0 ? matches.Count - 1 : index - 1);
            Workspace.SwitchStep(matches[target]);
        }

        private static void CollectSearchMatches(IEnumerable<StepModel> steps, string query, List<StepModel> into)
        {
            foreach (var step in steps)
            {
                if (step == null) continue;

                if ((step.StepName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (step.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    into.Add(step);
                }

                // 分支（StepCollection）不在 Steps 列表里、也进不了 SelectStep；
                // 容器的分支内步骤经 Children 递归照常搜
                if (step is IContainerStep container && container.Children != null)
                {
                    foreach (var branch in container.Children)
                    {
                        if (branch?.Steps != null)
                            CollectSearchMatches(branch.Steps, query, into);
                    }
                }
            }
        }

        private void OnLinkPathEvent(LinkPathEvent @event)
        {
            // 未携带目标端口：兼容旧行为，打开完整绑定视图
            if (string.IsNullOrEmpty(@event?.InputPortName))
            {
                dialogService.ShowDialog("DataBindView");
                return;
            }

            // 单绑定模式：弹窗左侧目标端口 = 事件携带的插件输入端口
            var p = new DialogParameters
            {
                { "IsSingleBindMode", true },
                { "TargetPortName", @event.InputPortName },
                { "AllowGlobals", @event.AllowGlobals },
            };
            if (@event.TargetType != null)
                p.Add("TargetTypeName", @event.TargetType.AssemblyQualifiedName);

            dialogService.ShowDialog("DataBindView", p, result =>
            {
                if (result.Result != ButtonResult.OK)
                    return;
                if (result.Parameters.TryGetValue<LinkReference>("BoundLink", out var link))
                    @event.OnBound?.Invoke(link);
            });
        }

        /// <summary>
        /// 流程栏右键「编辑绑定…」的处理器：以当前选中步骤为锚打开变量绑定弹窗。
        /// 拿不到选中步骤就什么都不做（菜单项本就只在选中算子时有意义，这里防御一次，
        /// 与 Rename/EditComment 的"空选中直接 break"同一写法）。
        /// </summary>
        private void OpenBindingForSelectedStep()
        {
            var target = CurrentSelectedStepModel;
            if (target == null)
                return;

            dialogService.ShowDialog("DataBindView", new DialogParameters
            {
                { "TargetStep", target },
            });
        }

        /// <summary>流程栏右键「复制」：无选中给回执（不静默空跑），有选中就进共享剪贴板。</summary>
        private void CopySelectedStep()
        {
            var target = CurrentSelectedStepModel;
            if (target == null)
            {
                ShowFeedback("请先选中一个步骤再复制", false);
                return;
            }

            FlowClipboard.Copy(target);
            ShowFeedback($"已复制「{target.StepName}」（在画布空白处右键粘贴）", true);
        }

        /// <summary>UI 命令动作 → 门禁编辑类别的归一（豁免口径全在 EditGate.IsEditable，别再私写判据）</summary>
        private static ModuleEditAction ToEditAction(ModuleCommandAction action) => action switch
        {
            ModuleCommandAction.ToggleBreakpoint => ModuleEditAction.ToggleBreakpoint,
            ModuleCommandAction.ModuleParameters => ModuleEditAction.ModuleParameters,
            _ => ModuleEditAction.StructuralEdit,
        };

        private async Task ModuleActionAsync(ModuleCommandAction? action)
        {
            // 运行锁：双击卡片、右键菜单（重命名/删除/禁用/模块参数…）都汇聚到本命令，单点拦截。
            // 删除/改名/参数等全部菜单动作都是编辑动作，运行中一律拦截（原 Copy/ShowAll 放行已随空壳项移除）。
            // DWV 第 1 期两处豁免已收口进 EditGate.IsEditable：切换断点任何运行态放行；模块参数仅"已暂停"放行。
            if (action.HasValue && !_editGate.IsEditable(ToEditAction(action.Value)))
            {
                Notifier.ShowWarning(RunLockedMessage);
                return;
            }

            switch (action)
            {
                case ModuleCommandAction.Rename:
                {
                    // 拿不到算子（未选中，或选中项不是 StepModel）就什么都不做——
                    // 这条命令是 async 的，空引用冒出去就是 UI 线程上的未处理异常
                    if (CurrentSelectedStepModel == null)
                        break;
                    var data = await EasyDialog.ShowTextInputAsync(
                        "步序重命名",
                        CurrentSelectedStepModel.StepName
                    );
                    if (data.IsConfirmed)
                        CurrentSelectedStepModel.StepName = data.Value;
                    break;
                }
                case ModuleCommandAction.EditComment:
                {
                    if (CurrentSelectedStepModel == null)
                        break;
                    var data1 = await EasyDialog.ShowTextInputAsync(
                        "注释重命名",
                        CurrentSelectedStepModel.Description
                    );
                    if (data1.IsConfirmed)
                        CurrentSelectedStepModel.Description = data1.Value;
                    break;
                }
                case ModuleCommandAction.ModuleParameters:
                    // 打开逻辑原样搬到 StepParameterDialog（DWV 第 1 期）：
                    // 命中窗「打开模块参数」与流程栏右键共用同一实现，杜绝第二份口径漂移。
                    // 2026-10-09：返回值 false = 该对象没有参数面板（分支卡片），不再兜底弹"空白条件编辑器"；
                    // 并行分组的参数面板是 EasyDialog 属性网格（静态弹窗），分派同样在那一处，这里不再给提示。
                    // workspace 传下去：并行面板保存成功后推进 CurrentFlow.Version（口径同旧面板 OnSave）。
                    StepParameterDialog.Open(SelectStep, dialogService, Workspace);
                    break;
                case ModuleCommandAction.ToggleBreakpoint:
                    // 纯运行期标记：取反 IsBreakpoint（不落盘、不递增 Version）。
                    // 菜单标题靠 BreakpointToggleHeader 通知刷新；红点/画布圆点各自绑定 IsBreakpoint，
                    // 属性通知由 StepModel.SetRuntimeState 自动发出。
                    if (CurrentSelectedStepModel == null)
                        break;
                    CurrentSelectedStepModel.IsBreakpoint = !CurrentSelectedStepModel.IsBreakpoint;
                    RaisePropertyChanged(nameof(BreakpointToggleHeader));
                    RaisePropertyChanged(nameof(IsBreakpointSet));
                    break;
                case ModuleCommandAction.ToggleDisable:
                    if (CurrentSelectedStepModel == null)
                        break;
                    CurrentSelectedStepModel.IsDisEnable = !CurrentSelectedStepModel.IsDisEnable;
                    RaisePropertyChanged(nameof(DisableToggleHeader));
                    break;
                case ModuleCommandAction.Delete:
                {
                    // 删除会用掉"这一次"的命令靶，但靶还要用来判断下面该不该清选中——先记住它。
                    var target = CurrentSelectedStepModel;
                    if (target == null || Workspace?.CurrentFlow == null)
                        break;

                    if (!RemoveStepRecursively(Workspace.CurrentFlow.Steps, target))
                        break;

                    // 2026-10-16 真机：删掉一个算子后再删剩下那个"点了没反应"（尤其只剩最后一步时）。
                    // 根因是这里的清空**无条件**执行——删除引起的集合变更会让流程树自动改选邻近行
                    //（WPF ItemsSource 变更后 Selector 保选），那次改选经 BindableSelectedItem 写回
                    // SelectStep，命令靶已跟着变成"幸存的那一步"；这里再写 null 就把它抹掉了，
                    // 于是界面高亮着某一行、删除/改名/禁用却全部静默空跑。更阴的是"点同一行"救不回来：
                    // 那一行本来就选着，不会再触发 SelectedItemChanged。
                    // 只在"删除期间没人改过选中项"时清空——此时命令靶还停在刚被移出图纸的那一步，
                    // 留着它就是命令链继续指着已删除对象（改名/禁用会打到孤儿对象上）。
                    // 清空走 SelectStep 而不是只写字段：顺带把 Workspace.CurrentStep 与菜单可见性
                    // 通知一起归位，公开属性与命令靶不再各说各话。
                    if (ReferenceEquals(CurrentSelectedStepModel, target))
                        SelectStep = null;

                    break;
                }
                case ModuleCommandAction.AddElseIf:
                {
                    // 结构动作的实现在 FlowBranchEditor（画布右键走同一份）：
                    // 本处只负责"命令靶 = 当前选中项"与反馈出口，判型/插入位置不在这里写第二遍。
                    // Result 统一接住：今天 AddElseIf 没有失败分支，但"一个入口说话、另一个不说"
                    // 在下次加校验时会立刻变成静默空跑——三个动作走同一条反馈口径。
                    if (SelectStep is ConditionStep ifNode)
                        ReportBranchResult(FlowBranchEditor.AddElseIfBranch(ifNode));

                    break;
                }
                case ModuleCommandAction.AddElse:
                {
                    if (SelectStep is ConditionStep ifNode)
                        ReportBranchResult(FlowBranchEditor.AddElseBranch(ifNode));

                    break;
                }
                case ModuleCommandAction.AddCase:
                {
                    // 分支匹配（Case）容器专用：插一条 Case 分支，位置在兜底分支（Else）之前——
                    // 兜底分支之后的分支编译器会按"不可达分支"报错，插在前面才是有意义的顺序。
                    // 结构变更只动 Children → FlowModel 版本链自动递增（同 AddElseIf，无需手动 Version++）。
                    if (SelectStep is CaseStep caseNode)
                        ReportBranchResult(FlowBranchEditor.AddCaseBranch(caseNode));

                    break;
                }
            }
        }

        #region 并行分组：分支结构动作（增 / 删 / 改名）
        //  2026-10-09：这三个动作原来在「并行分组配置」面板里（草稿 → 校验 → 写回）。
        //  用户裁决"右键直接添加"后下沉到流程栏右键菜单：就地生效、单步，面板只留参数与只读总览。
        //  版本号纪律：增/删分支**不用手动 Version++**——FlowModel 盯着每个容器的 Children 集合
        //  （CollectSubscriptions 会订阅它），结构变更自己会递增；只有"分支改名"必须手动推进，
        //  因为 StepCollection.StepName 不在 FlowModel 的监听面里（它盯的是集合结构与 StepModel 的属性）。

        /// <summary>三个结构命令的反馈出口：message + 是否"成功"档（成功=绿泡，否则=黄警示）。
        /// 默认实现走 UI 库弹泡；离屏断言宿主没有 WPF Application（Notifier.Show 内部直接取
        /// Application.Current.Dispatcher，headless 里会 NRE），所以默认实现先过一道空判，
        /// 断言宿主注入收集器就能读回文案（与 ConfirmDeleteParallelBranch 同一"可注入出口"手法）。
        /// </summary>
        public Action<string, bool> ShowFeedback { get; set; } = (message, success) =>
        {
            if (Application.Current == null)
                return;

            if (success)
                Notifier.ShowSuccess(message);
            else
                Notifier.ShowWarning(message);
        };

        /// <summary>
        /// 分支结构动作的统一反馈：空文案（= 命令靶类型不对、菜单本不该出现）静默，
        /// 其余一律走 <see cref="ShowFeedback"/>。四个动作（并行/ElseIf/Else/Case）共用，
        /// 免得"某个入口说话了、另一个不说"演变成静默空跑。
        /// </summary>
        private void ReportBranchResult(FlowBranchEditor.Result result)
        {
            if (result.Message.Length == 0)
                return;

            ShowFeedback(result.Message, result.Success);
        }

        /// <summary>
        /// 删除分支的二次确认出口。默认走 EasyDialog；断言宿主注入 (t, m) =&gt; false/true 就不弹真窗
        /// （与 StepParameterDialog 的"可注入出口"同一手法）。
        /// </summary>
        public Func<string, string, bool> ConfirmDeleteParallelBranch { get; set; } =
            (title, message) => EasyDialog.ShowSync(title, message);

        /// <summary>
        /// 分支改名的输入出口。默认走 EasyDialog 的**同步**版：ShowTextInputAsync() 在 UI 线程上
        /// 直接 GetAwaiter().GetResult() 会死锁——弹窗创建被排到 DispatcherPriority.Background，
        /// 阻塞的线程等不到它；ShowTextInputSync 内部压 DispatcherFrame，弹窗期间消息循环照常推动。
        /// 断言宿主注入固定值即可（(true, "新名字") / (false, "")）。
        /// </summary>
        public Func<string, string, (bool IsConfirmed, string Value)> ConfirmRenameParallelBranch { get; set; } =
            (title, defaultValue) => EasyDialog.ShowTextInputSync(title, defaultValue);

        /// <summary>
        /// 分组改名的输入出口（组头右键「重命名分组」）。与 ConfirmRenameParallelBranch 同一手法：
        /// 默认走 EasyDialog 的**同步**版（Async 版在 UI 线程上 GetAwaiter().GetResult() 会死锁），
        /// 断言宿主注入固定值即可（(true, "新名字") / (false, "")）。
        /// </summary>
        public Func<string, string, (bool IsConfirmed, string Value)> ConfirmRenameParallelGroup { get; set; } =
            (title, defaultValue) => EasyDialog.ShowTextInputSync(title, defaultValue);

        /// <summary>
        /// 添加一条并行分支（流程栏组头右键「添加分支」）。
        /// 结构动作与自动命名在 <see cref="FlowBranchEditor.AddParallelBranch"/>（画布右键同一份），
        /// 本处只管运行锁与反馈出口。
        /// </summary>
        private void AddParallelBranch(object? target)
        {
            // 命令靶是绑定给过来的：没选中组头 / 选中的不是并行分组时什么都不做
            if (target is not ParallelStep group)
                return;

            if (IsRunLocked)
            {
                ShowFeedback(RunLockedMessage, false);
                return;
            }

            var result = FlowBranchEditor.AddParallelBranch(group);
            if (result.Message.Length == 0)
                return;

            ShowFeedback(result.Message, result.Success);
        }

        /// <summary>
        /// 删除一条并行分支（流程栏分支胶囊右键「删除本条分支」）。
        /// 分支只有一个属主：递归找到所属容器就定稿，找不到（或不属于并行分组）什么都不做。
        /// </summary>
        private void RemoveParallelBranch(object? target)
        {
            if (target is not StepCollection branch)
                return;

            if (IsRunLocked)
            {
                ShowFeedback(RunLockedMessage, false);
                return;
            }

            var group = FindOwningParallelGroup(Workspace?.CurrentFlow?.Steps, branch);
            if (group == null || group.Children.Count <= 1)
            {
                ShowFeedback("至少保留一条分支（只有一条分支时：等价顺序执行）", false);
                return;
            }

            // 有算子的分支删掉会连带丢掉分支内的步骤，先二次确认；取消即什么都不做
            if (branch.Steps.Count > 0)
            {
                bool confirmed = ConfirmDeleteParallelBranch?.Invoke(
                    "删除分支",
                    $"分支「{branch.StepName}」内还有 {branch.Steps.Count} 个算子，删除分支会一并移除，确定？") ?? false;
                if (!confirmed)
                    return;
            }

            group.Children.Remove(branch);
        }

        /// <summary>
        /// 重命名一条并行分支（流程栏分支胶囊右键「重命名本条分支」）。
        /// 写 StepName 后必须手动 Version++：StepCollection.StepName 不在 FlowModel 的监听面里
        /// （它盯的是集合结构与 StepModel 属性），漏了就是"试运行对、正式跑错"。
        /// </summary>
        private void RenameParallelBranch(object? target)
        {
            if (target is not StepCollection branch)
                return;

            if (IsRunLocked)
            {
                ShowFeedback(RunLockedMessage, false);
                return;
            }

            var data = ConfirmRenameParallelBranch("分支重命名", branch.StepName);
            if (!data.IsConfirmed)
                return;

            string name = (data.Value ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                ShowFeedback("分支名不能为空", false);
                return;
            }

            // 名字没变就别白推一次版本号（口径同面板 TryApplyToModel 的"只写不同值"）
            if (name == branch.StepName)
                return;

            branch.StepName = name;
            if (Workspace?.CurrentFlow != null)
                Workspace.CurrentFlow.Version++;
        }

        /// <summary>
        /// 重命名并行分组（流程栏组头右键「重命名分组」，与分支重命名同款体验）。
        /// 与 RenameParallelBranch 的**版本号口径不同**：分组名写的是
        /// ParallelStep.StepName —— 它是 StepModel 的语义属性，setter 自带通知，
        /// FlowModel 版本链会自己递增，**不需要手动 Version++**（分支名 StepCollection.StepName
        /// 不在监听面里才要手动推，别把两处搞混，W5 断言对照着守）。
        /// </summary>
        private void RenameParallelGroup(object? target)
        {
            if (target is not ParallelStep group)
                return;

            if (IsRunLocked)
            {
                ShowFeedback(RunLockedMessage, false);
                return;
            }

            var data = ConfirmRenameParallelGroup("分组重命名", group.StepName);
            if (!data.IsConfirmed)
                return;

            string name = (data.Value ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                ShowFeedback("分组名不能为空", false);
                return;
            }

            // 名字没变就什么都不做（语义属性 setter 通知也不发，避免白刷一次版本链）
            if (name == group.StepName)
                return;

            // 走 setter：ParallelStep.StepName 是语义属性，Version 由 FlowModel 版本链自动递增
            group.StepName = name;
        }

        /// <summary>
        /// 从流程树里找某条分支的所属并行分组（找不到 / 不属于并行分组都返回 null，不抛）。
        /// 递归口径统一走 IContainerStep（与 RemoveStepRecursively / StepFactory.CountStepsDeep 一致）：
        /// 分支可能挂在并行分组下，也可能在别处的 If/For 里——只有属主是并行分组时才认，
        /// 免得"删并行分组的分支"误伤别处同名分支。
        /// </summary>
        private static ParallelStep FindOwningParallelGroup(IEnumerable<StepModel>? steps, StepCollection branch)
        {
            if (steps == null || branch == null)
                return null;

            foreach (var step in steps)
            {
                if (step is not IContainerStep container || container.Children == null)
                    continue;

                foreach (var child in container.Children)
                {
                    // 分支只有一个属主：命中即定稿（不是并行分组的分支 → 返回 null）
                    if (ReferenceEquals(child, branch))
                        return container as ParallelStep;

                    var nested = FindOwningParallelGroup(child.Steps, branch);
                    if (nested != null)
                        return nested;
                }
            }

            return null;
        }
        #endregion

        #region 分支匹配（Case）：分支结构动作（改名 / 删除）
        //  2026-10-11：Case 分支此前只有编译语义没有结构入口——加分支走 ModuleCommandAction.AddCase，
        //  改名/删除缺席（"Case 1/2/3"想改成"型号A"做不到）。照并行分支胶囊先例补齐：
        //  判型收 object（R27）、运行锁同一份文案、三个可注入出口（ShowFeedback /
        //  ConfirmDeleteParallelBranch / ConfirmRenameParallelBranch）**与并行共用同一份**——
        //  出口各写一份必然漂移。删除/改名打在命中的那条分支上（菜单靶 = PlacementTarget.DataContext），
        //  与"当前选中项"无关，不违反 R25 的分支卡片不可选中闸。

        /// <summary>
        /// 重命名一条 Case 分支（流程栏分支胶囊右键）。写 StepName 后手动 Version++：
        /// StepCollection.StepName 不在 FlowModel 监听面里（口径与并行分支改名一致）。
        /// </summary>
        private void RenameCaseBranch(object? target)
        {
            if (target is not StepCollection branch)
                return;

            if (FindOwningCaseStep(Workspace?.CurrentFlow?.Steps, branch) == null)
                return;

            if (IsRunLocked)
            {
                ShowFeedback(RunLockedMessage, false);
                return;
            }

            var data = ConfirmRenameParallelBranch("Case 分支重命名", branch.StepName);
            if (!data.IsConfirmed)
                return;

            string name = (data.Value ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                ShowFeedback("分支名不能为空", false);
                return;
            }

            if (name == branch.StepName)
                return;

            branch.StepName = name;
            if (Workspace?.CurrentFlow != null)
                Workspace.CurrentFlow.Version++;
        }

        /// <summary>
        /// 删除一条 Case 分支（流程栏分支胶囊右键）。两条业务闸：
        /// 默认分支（Else）不可删——未命中任何匹配值时靠它收尾；至少保留一条 Case 分支
        /// （编译期"没有可用的 Case 分支"也会拦，UI 先说人话）。删有算子的分支先确认。
        /// 从 Children 移除由 FlowModel 集合监听自动递增版本，不手动 Version++。
        /// </summary>
        private void RemoveCaseBranch(object? target)
        {
            if (target is not StepCollection branch)
                return;

            var owner = FindOwningCaseStep(Workspace?.CurrentFlow?.Steps, branch);
            if (owner == null)
                return;

            if (IsRunLocked)
            {
                ShowFeedback(RunLockedMessage, false);
                return;
            }

            if (branch.BranchType != BranchType.Case)
            {
                ShowFeedback("默认分支不可删除（所有匹配值都不命中时靠它收尾）", false);
                return;
            }

            if (owner.Children.Count(c => c.BranchType == BranchType.Case) <= 1)
            {
                ShowFeedback("至少保留一条 Case 分支", false);
                return;
            }

            if (branch.Steps.Count > 0)
            {
                bool confirmed = ConfirmDeleteParallelBranch?.Invoke(
                    "删除 Case 分支",
                    $"分支「{branch.StepName}」内还有 {branch.Steps.Count} 个算子，删除分支会一并移除，确定？") ?? false;
                if (!confirmed)
                    return;
            }

            owner.Children.Remove(branch);
        }

        /// <summary>
        /// 从流程树里找某条分支的所属分支匹配容器（找不到 / 属主不是 CaseStep 都返回 null，不抛）。
        /// 递归口径与 FindOwningParallelGroup 逐字同款：分支可能嵌在别的容器里，
        /// 只有属主是 CaseStep 时才认，免得"删 Case 分支"误伤别处同名分支。
        /// </summary>
        private static CaseStep? FindOwningCaseStep(IEnumerable<StepModel>? steps, StepCollection branch)
        {
            if (steps == null || branch == null)
                return null;

            foreach (var step in steps)
            {
                if (step is not IContainerStep container || container.Children == null)
                    continue;

                foreach (var child in container.Children)
                {
                    if (ReferenceEquals(child, branch))
                        return container as CaseStep;

                    var nested = FindOwningCaseStep(child.Steps, branch);
                    if (nested != null)
                        return nested;
                }
            }

            return null;
        }
        #endregion

        #region 控件拖拽
        /// <summary>
        /// 控件拖动
        /// </summary>
        /// <param name="args"></param>
        public void DragOver(IDropInfo dropInfo)
        {
            // 1. 基础防线：没有抓到数据，或者没加载流程，直接拒绝
            if (dropInfo.Data == null || Workspace?.CurrentFlow == null)
            {
                dropInfo.Effects = DragDropEffects.None;
                DragHintPresenter.Hide(DragHintPresenter.ProcessTreeOwner);
                return;
            }

            // 2. 核心判定：拖的是什么？(直接决定了是复制还是移动)
            bool isFromToolbox = dropInfo.Data is ToolItemModel; // 从左侧工具箱拖来的图纸模板
            bool isFromCanvas = dropInfo.Data is StepModel; // 从画布上拖起来的旧算子

            if (!isFromToolbox && !isFromCanvas)
            {
                dropInfo.Effects = DragDropEffects.None;
                DragHintPresenter.Hide(DragHintPresenter.ProcessTreeOwner);
                return;
            }

            // 3. 运行锁优先：此刻放哪儿都不许放，落点细节没有意义（与画布侧同一顺序）
            if (IsRunLocked)
            {
                dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
                DragHintPresenter.Show(DragHintPresenter.ProcessTreeOwner, DragHintText.Locked, DragHintKind.Locked);
                return;
            }

            // 4. 拖进自己或自己的子孙 → 结构成环（Drop 侧同样拦，这里只负责先说清楚）
            if (IsDraggingIntoSelf(dropInfo.Data, dropInfo.TargetItem))
            {
                dropInfo.Effects = DragDropEffects.None;
                DragHintPresenter.Show(DragHintPresenter.ProcessTreeOwner,
                    DragHintText.Illegal("不能放进自己里面"), DragHintKind.Illegal);
                return;
            }

            // 5. 落点解析：与 Drop 共用同一份（否则会出现"提示说 A、实际落 B"）
            var resolved = ResolveProcessDrop(dropInfo);
            string path = DragHintText.Path(resolved.Path);

            // 6. 设置 UI 样式：悬停在容器头上显示高亮框，悬停在算子之间显示插入线条
            dropInfo.DropTargetAdorner = resolved.IsContainerHover
                ? DropTargetAdorners.Highlight
                : DropTargetAdorners.Insert;

            string draggedName = dropInfo.Data is ToolItemModel tool
                ? tool.Name
                : (dropInfo.Data as StepModel)?.StepName ?? string.Empty;

            // 7. 循环控制指令的落点闸门（与 Drop 的 Copy 分支同一判据、同一文案）
            if (isFromToolbox
                && !StepFactory.AllowsLoopControl(
                    (dropInfo.Data as ToolItemModel)?.ModuleTypeName,
                    StepFactory.ContainerChainOf(Workspace.CurrentFlow.Steps, dropInfo.TargetItem)))
            {
                dropInfo.Effects = DragDropEffects.None;
                DragHintPresenter.Show(DragHintPresenter.ProcessTreeOwner,
                    DragHintText.Illegal(StepFactory.LoopControlPlacementMessage), DragHintKind.Illegal);
                return;
            }

            // 8. 根据来源设置终极动作 + 提示文案
            if (isFromToolbox)
            {
                // 场景 A：从工具箱拖来的 -> 永远是添加 (Copy)
                dropInfo.Effects = DragDropEffects.Copy;
                dropInfo.EffectText = "添加算子";
                dropInfo.DestinationText = path;

                DragHintPresenter.Show(DragHintPresenter.ProcessTreeOwner,
                    DragHintText.Add(draggedName, path, resolved.Position), DragHintKind.Valid);
            }
            else
            {
                // 场景 B：在画布内部拖的 -> 无论是同级排序还是跨分支，永远是移动 (Move)
                dropInfo.Effects = DragDropEffects.Move;
                dropInfo.EffectText = "移动算子";
                dropInfo.DestinationText = path;

                DragHintPresenter.Show(DragHintPresenter.ProcessTreeOwner,
                    DragHintText.Move(draggedName, path, resolved.Position), DragHintKind.Valid);
            }
        }

        /// <summary>
        /// 拖出流程栏：只撤自己写的那条提示。
        /// 不能无条件 Hide——gong 的 DragLeave 是 Dispatcher 延迟派发的，从流程栏移到画布时
        /// 它可能排在画布的 DragOver 之后，无条件 Hide 会把画布刚写的提示抹掉。
        /// </summary>
        public void DragLeave(IDropInfo dropInfo) => DragHintPresenter.Hide(DragHintPresenter.ProcessTreeOwner);

        /// <summary>
        /// 流程栏落点解析（DragOver 与 Drop 共用一份，保证"提示说的"就是"实际落的"）。
        ///
        /// gong 在 TreeViewItem 上按鼠标纵向位置分三档：上/下 25% 把 TargetCollection 设为父集合
        /// （插到容器旁边）、中间 50% 把 TargetCollection 设为该条目自己的空 Items（TargetItemCenter）。
        /// 因此"落在容器上"与"落在容器旁"是两种不同结果，必须按 TargetCollection 判，
        /// 不能只看 TargetItem 的类型——这正是此前提示会撒谎的地方。
        /// </summary>
        private ProcessDropTarget ResolveProcessDrop(IDropInfo info)
        {
            var flow = Workspace?.CurrentFlow;
            IList? targetList = info.TargetCollection as IList;
            int insertIndex = info.InsertIndex;
            bool isContainerHover = false;
            StepCollection? owningBranch = null;

            if (targetList == null || !(targetList is IEnumerable<StepModel>))
            {
                switch (info.TargetItem)
                {
                    case StepCollection branch:
                        targetList = branch.Steps;
                        insertIndex = branch.Steps.Count;
                        isContainerHover = true;
                        owningBranch = branch;
                        break;

                    case ConditionStep condition when condition.Children.Count > 0: // 💡 包含 If 和 While
                        targetList = condition.Children[0].Steps;
                        insertIndex = condition.Children[0].Steps.Count;
                        isContainerHover = true;
                        owningBranch = condition.Children[0];
                        break;

                    case ForStep forStep when forStep.Children.Count > 0: // 🌟 For 循环肚子里
                        targetList = forStep.Children[0].Steps;
                        insertIndex = forStep.Children[0].Steps.Count;
                        isContainerHover = true;
                        owningBranch = forStep.Children[0];
                        break;

                    // 并行分组：与 If/For 同款落进第一条分支（分支卡片本身是独立落点）
                    case ParallelStep parallel when parallel.Children.Count > 0:
                        targetList = parallel.Children[0].Steps;
                        insertIndex = parallel.Children[0].Steps.Count;
                        isContainerHover = true;
                        owningBranch = parallel.Children[0];
                        break;

                    default:
                        targetList = flow?.Steps;
                        insertIndex = targetList?.Count ?? 0;
                        break;
                }
            }
            else
            {
                // 直接落在某个 Steps 集合上（兄弟之间 / 容器上下边缘）：反查它属于哪条分支
                owningBranch = FindOwningBranch(flow?.Steps, targetList);
            }

            if (targetList == null)
                targetList = new ObservableCollection<StepModel>();

            if (insertIndex < 0) insertIndex = 0;
            if (insertIndex > targetList.Count) insertIndex = targetList.Count;

            // 路径：容器链（由远及近）+ 分支名
            var segments = new List<string?>();
            if (owningBranch != null)
            {
                foreach (var ancestor in StepFactory.ContainerChainOf(flow?.Steps, owningBranch).Reverse())
                    segments.Add(ancestor.StepName);

                segments.Add(owningBranch.StepName);
            }
            else if (info.TargetItem is StepModel targetStep)
            {
                foreach (var ancestor in StepFactory.ContainerChainOf(flow?.Steps, targetStep).Reverse())
                    segments.Add(ancestor.StepName);
            }

            // 位置：插到下一个兄弟之前，或追加到末尾
            string position = DragHintText.PositionEnd;
            if (!isContainerHover && insertIndex < targetList.Count)
            {
                if (targetList[insertIndex] is StepModel next)
                    position = DragHintText.InsertBeforePosition(next.StepName);
            }

            return new ProcessDropTarget(targetList, insertIndex, segments, position, isContainerHover);
        }

        /// <summary>
        /// 反查一个 Steps 集合属于哪条分支（用于把路径补成「容器 / 分支」）。
        /// 找不到（顶层 Steps / 畸形结构）返回 null。
        /// </summary>
        private static StepCollection? FindOwningBranch(
            ObservableCollection<StepModel>? roots,
            IList? steps)
        {
            if (roots == null || steps == null)
                return null;

            foreach (var step in roots)
            {
                if (step is not IContainerStep container || container.Children == null)
                    continue;

                foreach (var branch in container.Children)
                {
                    if (branch?.Steps == null) continue;

                    if (ReferenceEquals(branch.Steps, steps))
                        return branch;

                    var nested = FindOwningBranch(branch.Steps, steps);
                    if (nested != null)
                        return nested;
                }
            }

            return null;
        }

        /// <summary>
        /// 拖拽目标是否落在被拖步骤自己（或自己的子孙）里——命中会形成结构环，
        /// 之后编译/遍历都会无限递归。DragOver 与 Drop 共用。
        /// </summary>
        private bool IsDraggingIntoSelf(object? dragged, object? target)
        {
            if (dragged is not StepModel step || target == null)
                return false;

            if (ReferenceEquals(dragged, target))
                return true;

            // 目标的容器链里出现"正在拖的那个容器" → 目标在它的子树里
            return StepFactory.ContainerChainOf(Workspace?.CurrentFlow?.Steps, target)
                .Any(ancestor => ReferenceEquals(ancestor, step));
        }

        /// <summary>流程栏落点解析结果（DragOver 与 Drop 共用）</summary>
        private sealed record ProcessDropTarget(
            IList TargetList,
            int InsertIndex,
            IReadOnlyList<string?> Path,
            string Position,
            bool IsContainerHover);

        public void Drop(IDropInfo args)
        {
            if (Workspace.CurrentFlow == null)
            {
                Notifier.ShowError("请先加载流程");
                return;
            }

            // 拖放结束：先收起落点提示条（Drop 之后不会再有 DragOver，不收就永远挂着）
            DragHintPresenter.Hide();

            // 运行锁：从工具箱拖入、卡片间拖拽排序，都会走到这里，一并拦下
            if (IsRunLocked)
            {
                Notifier.ShowWarning(RunLockedMessage);
                return;
            }

            if (args.Effects != DragDropEffects.Copy && args.Effects != DragDropEffects.Move)
                return;

            // 拖进自己或自己的子孙 → 结构成环（DragOver 已提前说过，这里兜住实际落子）
            if (IsDraggingIntoSelf(args.Data, args.TargetItem))
            {
                ShowFeedback("不能放进自己里面", false);
                return;
            }

            // ==========================================
            // 第一步：落点解析（与 DragOver 共用一份，保证"提示说的"就是"实际落的"）
            // ==========================================
            var resolved = ResolveProcessDrop(args);
            IList targetList = resolved.TargetList;
            int insertIndex = resolved.InsertIndex;

            // ==========================================
            // 第二步：场景 A - 从工具箱【新增】算子 (Copy)
            // ==========================================
            if (args.Effects == DragDropEffects.Copy && args.Data is ToolItemModel node)
            {
                // 循环控制指令的落点闸门（与画布 DropToolAt 同一条）：Break/Continue 只能进
                // For/While；顶层与并行分支里运行期会忽略它们，允许落子等于制造"看着配好了、
                // 其实没生效"的假绿。判据在 StepFactory，两处入口共用。
                if (!StepFactory.AllowsLoopControl(
                        node.ModuleTypeName,
                        StepFactory.ContainerChainOf(Workspace.CurrentFlow?.Steps, args.TargetItem)))
                {
                    ShowFeedback($"「{node.Name}」{StepFactory.LoopControlPlacementMessage}", false);
                    return;
                }

                // 造模型（类型分派）与自动命名统一走 StepFactory —— 画布 Drop 用同一份，
                // 改口径只改一处；落点命中 / 运行锁 / 版本推进留在本方法（两处落点规则不同）
                string stepName = StepFactory.NextStepName(Workspace.CurrentFlow, node);
                StepModel newStep = StepFactory.CreateFromTool(node, stepName);

                targetList.Insert(insertIndex, newStep);

                // 🌟 别忘了通知图纸：结构改变了，需要重新编译！
                Workspace.CurrentFlow.Version++;
                return;
            }
            // ==========================================
            // 第三步：场景 B - 在画布内部【拖拽移动】 (Move)
            // ==========================================
            if (args.Effects == DragDropEffects.Move && args.Data is StepModel sourceItem)
            {
                // 🚨 致命 Bug 修复处：必须从 DragInfo 里拿数据的来源集合！
                IList sourceList = args.DragInfo?.SourceCollection as IList;

                if (sourceList != null && targetList != null)
                {
                    int oldIndex = sourceList.IndexOf(sourceItem);
                    int newIndex = insertIndex;

                    if (oldIndex == -1)
                        return; // 防御性编程

                    // 【情况 B-1】：同容器内移动（上下排序）
                    if (sourceList == targetList)
                    {
                        // 如果位置没变，直接跳过
                        if (oldIndex == newIndex || oldIndex == newIndex - 1)
                            return;

                        // 核心算法：因为元素被移除后，后面的元素会整体往前挤1位，所以新索引需要修正
                        if (newIndex > oldIndex)
                            newIndex--;

                        sourceList.RemoveAt(oldIndex);
                        sourceList.Insert(newIndex, sourceItem);
                    }
                    // 【情况 B-2】：跨容器移动（主流程 <-> 逻辑分支，或者 分支A <-> 分支B）
                    else
                    {
                        // 1. 先从老口袋里拿出来
                        sourceList.RemoveAt(oldIndex);

                        // 2. 重新校验目标口袋的容量上限 (因为拿出来一个，总数可能变了)
                        if (newIndex > targetList.Count)
                            newIndex = targetList.Count;

                        // 3. 塞进新口袋
                        targetList.Insert(newIndex, sourceItem);
                    }
                }
            }
        }

        private bool RemoveStepRecursively(
            ObservableCollection<StepModel> steps,
            StepModel targetToRemove
        )
        {
            if (steps == null || steps.Count == 0 || targetToRemove == null)
                return false;

            // 1. 第一层拦截：如果这个节点就在当前集合里，直接斩杀！
            if (steps.Contains(targetToRemove))
            {
                steps.Remove(targetToRemove);
                return true;
            }

            // 2. 如果不在当前层，遍历当前层里的所有“容器算子”（If/While/For/并行分组）。
            //    判据统一走 IContainerStep：2026-10-09 前只认 ConditionStep，
            //    For 与并行分支里的算子"删除模块"静默无效（命令看着像没反应）。
            foreach (var step in steps)
            {
                if (step is IContainerStep container)
                {
                    foreach (var branch in container.Children)
                    {
                        // 递归调用！如果在深层找到了并删除了，立刻顺着调用栈返回 true 终止搜索
                        if (RemoveStepRecursively(branch.Steps, targetToRemove))
                        {
                            return true;
                        }
                    }
                }
            }

            // 到底了都没找到
            return false;
        }
        #endregion
    }
}
