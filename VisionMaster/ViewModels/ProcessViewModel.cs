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
        /// 运行状态镜像：由 ShellViewModel 经 GlobalEventBus 广播同步。
        /// 运行中锁住流程编辑（增删/改名/参数/拖放），防止"边跑边换轮胎"。
        /// </summary>
        private MainRunState _runState = MainRunState.NotStarted;
        private bool IsRunLocked => _runState != MainRunState.NotStarted;

        /// <summary>运行锁拦截的提示文案（ModuleActionCommand / Drop / 三个分支结构命令共用一份，别各写各的）</summary>
        private const string RunLockedMessage = "流程运行中，禁止编辑；如需修改请先点击“停止”";

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
            AddParallelBranchCommand = new(AddParallelBranch);
            RemoveParallelBranchCommand = new(RemoveParallelBranch);
            RenameParallelBranchCommand = new(RenameParallelBranch);
            RenameParallelGroupCommand = new(RenameParallelGroup);

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

            if (_workspaceChanged != null)
                _workspaceChanged.PropertyChanged -= OnWorkspacePropertyChanged;
        }

        private void OnMainRunStateChanged(MainRunState state) => _runState = state;

        private void OnRunTimeTimerTick(object? sender, EventArgs e)
            => TickRunningTimes(Workspace.CurrentFlow?.Steps);

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
        /// 递归刷新运行中步骤的实时耗时（含容器内嵌套步骤）
        /// 只更新 IsRunningFocus 且 State==Running 的步骤：
        /// 完成步骤的耗时已在引擎侧冻结为最终值，此处不覆盖
        /// </summary>
        private static void TickRunningTimes(IEnumerable<StepModel> steps)
        {
            if (steps == null) return;

            foreach (var step in steps)
            {
                if (step.IsRunningFocus
                    && step.State == StepState.Running
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
        /// 运行锁豁免表（DWV 第 1 期）。豁免只放宽下列动作，其余编辑动作维持原锁。
        ///
        /// 【为什么「切换断点」任何运行态都放行】断点标记是 [RuntimeState] 运行期设施——
        /// 不落盘、不递增 Version、不改流程语义（见 StepModel.IsBreakpoint 注释）；
        /// 而"停在断点上给后续步骤加减断点"恰恰是调试的日常动作，锁住它等于调试时不能调断点。
        ///
        /// 【为什么「模块参数」只在"已暂停"放行】暂停中查看/调整参数是调试的核心诉求
        /// （引擎停在节点执行前，此刻看参数不与被执行中的插件状态打架）；
        /// 运行中（非暂停）仍锁死，防止"边跑边换轮胎"。
        /// 注意：豁免只影响本命令的运行锁守卫；参数写回仍走既有通道（Version 递增照旧），
        /// 是否对下一轮生效由引擎的编译版本检查决定，本命令不做额外处理。
        /// </summary>
        private bool IsRunLockExempt(ModuleCommandAction action)
        {
            if (action == ModuleCommandAction.ToggleBreakpoint)
                return true;

            if (action == ModuleCommandAction.ModuleParameters && _runState == MainRunState.Paused)
                return true;

            return false;
        }

        private async Task ModuleActionAsync(ModuleCommandAction? action)
        {
            // 运行锁：双击卡片、右键菜单（重命名/删除/禁用/模块参数…）都汇聚到本命令，单点拦截。
            // 删除/改名/参数等全部菜单动作都是编辑动作，运行中一律拦截（原 Copy/ShowAll 放行已随空壳项移除）。
            // DWV 第 1 期两处豁免见 IsRunLockExempt：切换断点任何运行态放行；模块参数仅"已暂停"放行。
            if (action.HasValue && IsRunLocked && !IsRunLockExempt(action.Value))
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
                    break;
                case ModuleCommandAction.ToggleDisable:
                    if (CurrentSelectedStepModel == null)
                        break;
                    CurrentSelectedStepModel.IsDisEnable = !CurrentSelectedStepModel.IsDisEnable;
                    RaisePropertyChanged(nameof(DisableToggleHeader));
                    break;
                case ModuleCommandAction.Delete:
                    if (CurrentSelectedStepModel == null || Workspace?.CurrentFlow == null)
                        break;
                    if (
                        RemoveStepRecursively(Workspace.CurrentFlow.Steps, CurrentSelectedStepModel)
                    )
                    {
                        CurrentSelectedStepModel = null;
                    }

                    break;
                case ModuleCommandAction.AddElseIf:
                {
                    if (SelectStep is ConditionStep ifNode)
                    {
                        int insertIndex = ifNode.Children.Count;
                        var lastBranch = ifNode.Children.LastOrDefault();

                        if (lastBranch != null && lastBranch.BranchType == BranchType.Else)
                        {
                            insertIndex = ifNode.Children.Count - 1;
                        }

                        // 3. 插入数据
                        ifNode.Children.Insert(
                            insertIndex,
                            new StepCollection
                            {
                                BranchType = BranchType.ElseIf,
                                StepName = "ElseIf 分支",
                            }
                        );
                    }
                    break;
                }
                case ModuleCommandAction.AddElse:
                {
                    if (SelectStep is ConditionStep ifNode)
                    {
                        if (ifNode.Children.Any(c => c.BranchType == BranchType.Else))
                        {
                            Notifier.ShowWarning("该算子已经包含了 Else 分支！");
                            break;
                        }
                        ifNode.Children.Add(
                            new StepCollection
                            {
                                BranchType = BranchType.Else,
                                StepName = "Else 分支",
                            }
                        );
                    }
                    break;
                }
                case ModuleCommandAction.AddCase:
                {
                    // 分支匹配（Case）容器专用：插一条 Case 分支，位置在兜底分支（Else）之前——
                    // 兜底分支之后的分支编译器会按"不可达分支"报错，插在前面才是有意义的顺序。
                    // 结构变更只动 Children → FlowModel 版本链自动递增（同 AddElseIf，无需手动 Version++）。
                    if (SelectStep is CaseStep caseNode)
                    {
                        int insertIndex = caseNode.Children.Count;
                        var lastBranch = caseNode.Children.LastOrDefault();

                        if (lastBranch != null && lastBranch.BranchType == BranchType.Else)
                        {
                            insertIndex = caseNode.Children.Count - 1;
                        }

                        caseNode.Children.Insert(
                            insertIndex,
                            new StepCollection
                            {
                                BranchType = BranchType.Case,
                                StepName = caseNode.NextCaseBranchName(),
                            }
                        );
                    }
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

        /// <summary>
        /// 三个结构命令的反馈出口：message + 是否"成功"档（成功=绿泡，否则=黄警示）。
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
        /// 名字在"分支 N"里挑第一个未占用的 N：用户可能已经改过名（"左工位"），
        /// 按"当前条数 + 1"命名会撞出两条"分支 3"。
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

            if (group.Children.Count >= ParallelStep.RecommendedMaxBranches)
            {
                ShowFeedback(
                    $"并行分支已达上限 {ParallelStep.RecommendedMaxBranches} 条（并列泳道再宽一屏就放不下）",
                    false);
                return;
            }

            string name = NextBranchName(group);
            group.Children.Add(new StepCollection { BranchType = BranchType.Default, StepName = name });

            ShowFeedback($"已添加「{name}」到「{group.StepName}」（可在分支上右键改名）", true);
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
        /// 在"分支 N"里挑第一个未被占用的 N（分支数有上限、名字空间无限，循环必然找到空位）。
        /// </summary>
        private static string NextBranchName(ParallelStep group)
        {
            for (int n = 1; ; n++)
            {
                string candidate = $"分支 {n}";
                if (!group.Children.Any(c => c.StepName == candidate))
                    return candidate;
            }
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
                return;
            }

            // 2. 核心判定：拖的是什么？(直接决定了是复制还是移动)
            bool isFromToolbox = dropInfo.Data is ToolItemModel; // 从左侧工具箱拖来的图纸模板
            bool isFromCanvas = dropInfo.Data is StepModel; // 从画布上拖起来的旧算子

            if (!isFromToolbox && !isFromCanvas)
            {
                dropInfo.Effects = DragDropEffects.None;
                return;
            }
            // 3. 智能推断“落地点名称”和“UI 框选效果”
            string destinationName = "主流程";
            bool isHoveringContainer = false;

            if (dropInfo.TargetItem is StepCollection branch)
            {
                destinationName = $"分支: {branch.StepName}";
                isHoveringContainer = true;
            }
            else if (dropInfo.TargetItem is ConditionStep condition) // 💡 自动包含 If 和 While
            {
                destinationName = $"容器: {condition.StepName}";
                isHoveringContainer = true;
            }
            else if (dropInfo.TargetItem is ForStep forStep) // 🌟 新增：识别 For 循环容器
            {
                destinationName = $"循环: {forStep.StepName}";
                isHoveringContainer = true;
            }
            else if (dropInfo.TargetItem is ParallelStep parallel) // 并行分组：与 If/For 同款（落进第一条分支）
            {
                destinationName = $"并行: {parallel.StepName}";
                isHoveringContainer = true;
            }

            // 4. 设置 UI 样式：悬停在容器头上显示高亮框，悬停在算子之间显示插入线条
            dropInfo.DropTargetAdorner = isHoveringContainer
                ? DropTargetAdorners.Highlight
                : DropTargetAdorners.Insert;

            // 5. 根据来源设置终极动作
            if (isFromToolbox)
            {
                // 场景 A：从工具箱拖来的 -> 永远是添加 (Copy)
                dropInfo.Effects = DragDropEffects.Copy;
                dropInfo.EffectText = "添加算子";
                dropInfo.DestinationText = destinationName;
            }
            else if (isFromCanvas)
            {
                // 场景 B：在画布内部拖的 -> 无论是同级排序还是跨分支，永远是移动 (Move)

                // 🛡️ 防御性编程：防止用户把一个大容器拖进自己的肚子里造成无限死循环死锁
                if (dropInfo.Data == dropInfo.TargetItem)
                {
                    dropInfo.Effects = DragDropEffects.None;
                    return;
                }

                dropInfo.Effects = DragDropEffects.Move;
                dropInfo.EffectText = "移动算子";
                dropInfo.DestinationText = destinationName;
            }
        }

        public void Drop(IDropInfo args)
        {
            if (Workspace.CurrentFlow == null)
            {
                Notifier.ShowError("请先加载流程");
                return;
            }

            // 运行锁：从工具箱拖入、卡片间拖拽排序，都会走到这里，一并拦下
            if (IsRunLocked)
            {
                Notifier.ShowWarning(RunLockedMessage);
                return;
            }

            if (args.Effects != DragDropEffects.Copy && args.Effects != DragDropEffects.Move)
                return;

            // ==========================================
            // 第一步：智能解析要放进哪个“口袋”
            // ==========================================
            IList targetList = args.TargetCollection as IList;
            int insertIndex = args.InsertIndex;

            if (targetList == null || !(targetList is IEnumerable<StepModel>))
            {
                switch (args.TargetItem)
                {
                    case StepCollection branch:
                        targetList = branch.Steps;
                        insertIndex = branch.Steps.Count;
                        break;

                    case ConditionStep condition when condition.Children.Count > 0: // 💡 包含 If 和 While
                        targetList = condition.Children[0].Steps;
                        insertIndex = condition.Children[0].Steps.Count;
                        break;

                    case ForStep forStep when forStep.Children.Count > 0: // 🌟 新增：把算子丢进 For 循环肚子里
                        targetList = forStep.Children[0].Steps;
                        insertIndex = forStep.Children[0].Steps.Count;
                        break;

                    // 并行分组：与 If/For 同款落进第一条分支（分支卡片本身是独立落点；
                    // 2026-10-09 前这里没有 Parallel 分支 → 拖到组头上会静默掉到主流程末尾）
                    case ParallelStep parallel when parallel.Children.Count > 0:
                        targetList = parallel.Children[0].Steps;
                        insertIndex = parallel.Children[0].Steps.Count;
                        break;

                    default:
                        targetList = Workspace.CurrentFlow.Steps;
                        insertIndex = Workspace.CurrentFlow.Steps.Count;
                        break;
                }
            }

            if (insertIndex < 0) insertIndex = 0;
            if (insertIndex > targetList.Count) insertIndex = targetList.Count;

            // ==========================================
            // 第二步：场景 A - 从工具箱【新增】算子 (Copy)
            // ==========================================
            if (args.Effects == DragDropEffects.Copy && args.Data is ToolItemModel node)
            {
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
