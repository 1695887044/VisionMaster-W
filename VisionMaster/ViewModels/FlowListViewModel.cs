using Core.Events;
using Core.Interfaces;
using Prism.Dialogs;
using Prism.Mvvm;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UI.CustomControl;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.ViewModels.DialogViewModels;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 程序面板（FlowListView）ViewModel：
    /// 流程列表的显示、选中切换（驱动流程栏）与右键管理操作
    /// </summary>
    public class FlowListViewModel : BindableBase
    {
        private readonly IDialogService dialogService;
        private readonly FlowCompiler _compiler;
        private readonly IRuntimeManager _runtimeManager;
        private readonly IFlowEngine _flowEngine;
        private readonly ILogService _log;
        private readonly ScadaAccessPolicy _accessPolicy;
        private readonly ScadaPermissionGate _permissionGate;
        private readonly SolutionSessionCoordinator _sessionCoordinator;

        public IWorkspaceManager Workspace { get; init; }

        /// <summary>
        /// 选中的流程：切换时驱动流程栏（Workspace.SwitchFlow）。
        /// 同时只订阅当前选中流程的 PropertyChanged：列表项视觉由绑定自更新，
        /// 但右键菜单标题不在流程对象的绑定链上，只能靠这个通知转发刷新。
        /// 订阅只在挂接状态（_subscribed）下持有：面板离树后旧 VM 必须能被回收，
        /// 未挂接时不订阅，由 Activate 统一补挂
        /// </summary>
        public FlowModel SelectFlow
        {
            get { return field; }
            set
            {
                if (field != null)
                    field.PropertyChanged -= OnSelectFlowPropertyChanged;
                field = value;
                if (field != null && _subscribed)
                    field.PropertyChanged += OnSelectFlowPropertyChanged;

                RefreshFlowMenuState();
                Workspace.SwitchFlow(value);
            }
        }

        /// <summary>右键菜单「启用/禁用」的标题：跟随当前选中流程的启用状态</summary>
        public string FlowToggleHeader => SelectFlow?.IsEnabled == true ? "禁用" : "启用";

        // ==================================================================
        //  右键菜单的只读展示面（设计基线 docs/UI设计基线/flow-context-menu v1）
        //
        //  【为什么是 VM 属性而不是 XAML 里的转换器】
        //  "某项能不能点 / 为什么不能点"是同一份判据，必须只有一处来源；
        //  转换器散在 XAML 里既测不到，也会和命令体的拦截文案各说一套。
        //  这里只做**展示**：命令体里的拦截（FlowCommandExecute）一个都不动，
        //  所以这些属性即使漏刷新，行为仍然安全（最多是灰/亮不同步）。
        //
        //  【刷新时机】选中流程变化、流程的 IsEnabled / RunState / FlowName /
        //  StepsEncrypted 变化、全局 MainRunState 变化——全部收在 RefreshFlowMenuState()。
        // ==================================================================

        /// <summary>行尾状态徽标：「当前：已启用 / 已禁用」</summary>
        public string FlowStateBadge =>
            SelectFlow == null ? string.Empty : (SelectFlow.IsEnabled ? "当前：已启用" : "当前：已禁用");

        /// <summary>启用/禁用项的图标：已启用 → toggle-off（点了会禁用）；已禁用 → toggle-on</summary>
        public string FlowToggleIcon => SelectFlow?.IsEnabled == true ? "\uf204" : "\uf205";

        /// <summary>菜单页脚：写明这条菜单作用于哪条流程（锚在具体胶囊上，不该靠猜）</summary>
        public string FlowMenuFooter =>
            SelectFlow == null ? string.Empty : $"仅作用于「{SelectFlow.FlowName}」";

        /// <summary>删除可用：骨架流程（Home/Main/End）不允许删除</summary>
        public bool CanDeleteFlow => SelectFlow != null && !MandatoryFlows.IsMandatory(SelectFlow);

        /// <summary>编译可用：流程启用、没有整体运行、且本流程不在跑</summary>
        public bool CanCompileFlow =>
            SelectFlow != null
            && SelectFlow.IsEnabled
            && !IsRunLocked
            && SelectFlow.RunState == FlowRunState.Stopped;

        /// <summary>
        /// 运行可用：流程启用、步序未加密、本流程不在跑。
        ///
        /// 【不再判整体运行态（2026-10-28）】工位与主流程是平级的：整线跑着的时候，
        /// 某工位被停掉后必须能单独把它重新拉起来，不能要求"先停整线"——那等于一有故障
        /// 就整线停。互斥只按**本流程**的会话状态判（同一条不许跑两份），
        /// 这与 SCADA「启动流程」的幂等口径一致（已在运行就跳过，不拒绝其它流程）。
        /// </summary>
        public bool CanRunFlow =>
            SelectFlow != null
            && SelectFlow.IsEnabled
            && !SelectFlow.StepsEncrypted
            && SelectFlow.RunState == FlowRunState.Stopped;

        /// <summary>停止可用：本流程确实在跑（未运行时不给可点的空操作）</summary>
        public bool CanStopFlow => SelectFlow != null && SelectFlow.RunState != FlowRunState.Stopped;

        /// <summary>
        /// 批量设置显示窗口可用：整体运行中（非暂停）拦截。
        /// 口径与 <c>EditGate.ModuleParameters</c> 一致——暂停中允许调参，是调试的核心诉求。
        /// </summary>
        public bool CanBatchDisplayWindow =>
            SelectFlow != null
            && _runState is not (MainRunState.RunningOnce or MainRunState.RunningContinuous);

        public string DeleteFlowToolTip =>
            CanDeleteFlow ? "删除这条流程（连同步骤）" : "骨架流程不允许删除";

        public string CompileFlowToolTip =>
            SelectFlow == null ? "按当前图纸重新编译，供运行使用"
            : !SelectFlow.IsEnabled ? "流程已禁用，编译会被跳过"
            : (IsRunLocked || SelectFlow.RunState != FlowRunState.Stopped)
                ? "流程运行中，禁止编译；如需编译请先停止"
                : "按当前图纸重新编译，供运行使用";

        public string RunFlowToolTip =>
            SelectFlow == null ? "执行这条流程"
            : !SelectFlow.IsEnabled ? "流程已禁用，请先启用"
            : SelectFlow.StepsEncrypted ? "步序已加密，无法运行"
            : SelectFlow.RunState != FlowRunState.Stopped
                ? "这条流程正在运行，请先停止它"
                : "执行这条流程（不影响其它正在运行的流程）";

        public string BatchDisplayWindowToolTip =>
            _runState is MainRunState.RunningOnce or MainRunState.RunningContinuous
                ? "流程运行中，禁止编辑；如需修改请先停止"
                : "把本流程所有出图算子的显示窗口一次设好";

        public string StopFlowToolTip => CanStopFlow ? "停止这条流程的运行会话" : "当前未运行";

        /// <summary>一次性刷新右键菜单的全部展示属性（时机见上方注释块）。</summary>
        private void RefreshFlowMenuState()
        {
            RaisePropertyChanged(nameof(FlowToggleHeader));
            RaisePropertyChanged(nameof(FlowStateBadge));
            RaisePropertyChanged(nameof(FlowToggleIcon));
            RaisePropertyChanged(nameof(FlowMenuFooter));
            RaisePropertyChanged(nameof(CanDeleteFlow));
            RaisePropertyChanged(nameof(CanCompileFlow));
            RaisePropertyChanged(nameof(CanRunFlow));
            RaisePropertyChanged(nameof(CanStopFlow));
            RaisePropertyChanged(nameof(CanBatchDisplayWindow));
            RaisePropertyChanged(nameof(DeleteFlowToolTip));
            RaisePropertyChanged(nameof(CompileFlowToolTip));
            RaisePropertyChanged(nameof(RunFlowToolTip));
            RaisePropertyChanged(nameof(BatchDisplayWindowToolTip));
            RaisePropertyChanged(nameof(StopFlowToolTip));
        }

        public AsyncDelegateCommand<FlowAction?> FlowCommand { get; }

        /// <summary>
        /// 运行状态镜像：由 ShellViewModel 经 GlobalEventBus 广播同步。
        /// 运行中编译会被 RegisterSession 静默停掉同名会话（边跑边拆台），故编译单点拦截。
        /// </summary>
        private MainRunState _runState = MainRunState.NotStarted;
        private bool IsRunLocked => _runState != MainRunState.NotStarted;

        /// <summary>
        /// 正在准备启动（编译已挪到后台线程）。期间重复点击直接忽略——否则双击会排两次编译，
        /// 而两次编译最终只有一次能抢到会话锁，另一次只是白烧 CPU 并弹一条"未启动"。
        /// </summary>
        private bool _starting;

        public FlowListViewModel(
            IWorkspaceManager workspace,
            IDialogService dialogService,
            FlowCompiler compiler,
            IRuntimeManager runtimeManager,
            IFlowEngine flowEngine,
            ILogService logService,
            ScadaAccessPolicy accessPolicy,
            ScadaPermissionGate permissionGate,
            SolutionSessionCoordinator sessionCoordinator)
        {
            Workspace = workspace;
            this.dialogService = dialogService;
            _compiler = compiler;
            _runtimeManager = runtimeManager;
            _flowEngine = flowEngine;
            _log = logService;
            _accessPolicy = accessPolicy;
            _permissionGate = permissionGate;
            _sessionCoordinator = sessionCoordinator;
            FlowCommand = new AsyncDelegateCommand<FlowAction?>(FlowCommandExecute, CanExecuteFlow);
        }

        /// <summary>流程栏 13 项统一接 gate；只判权限，不判运行态（运行中的 Stop 必须仍可用）。</summary>
        public bool CanExecuteFlow(FlowAction? action)
            => action != null
               && _permissionGate.Can(ScadaPermissionGate.PermissionFor(action.Value), out _);

        private bool _subscribed;

        /// <summary>
        /// 挂接全局运行状态订阅（幂等）。
        /// 为什么必须成对挂/摘：AvalonDock 切换标签页、隐藏面板都会让 View 触发 Unloaded，
        /// 一次性清理会让面板恢复显示后拿不到运行状态；摘干净则旧 VM 不再被静态事件表引用，可被 GC。
        /// </summary>
        public void Activate()
        {
            if (_subscribed) return;
            _subscribed = true;

            // 用命名方法而非 lambda：GlobalEventBus.Unsubscribe 依赖委托的 Target+Method 匹配
            GlobalEventBus.Subscribe<MainRunState>(OnMainRunStateChanged);
            _accessPolicy.PropertyChanged += OnAccessPolicyPropertyChanged;
            _sessionCoordinator.SessionBoundaryChanged += OnSessionBoundaryChanged;

            // 面板重见时补挂当前选中流的属性转发（与 Deactivate 的摘除配对，见 SelectFlow 注释）
            if (SelectFlow != null)
                SelectFlow.PropertyChanged += OnSelectFlowPropertyChanged;
        }

        /// <summary>
        /// 摘除全局运行状态订阅与选中流订阅（幂等）
        /// </summary>
        public void Deactivate()
        {
            if (!_subscribed) return;
            _subscribed = false;

            GlobalEventBus.Unsubscribe<MainRunState>(OnMainRunStateChanged);
            _accessPolicy.PropertyChanged -= OnAccessPolicyPropertyChanged;
            _sessionCoordinator.SessionBoundaryChanged -= OnSessionBoundaryChanged;

            if (SelectFlow != null)
                SelectFlow.PropertyChanged -= OnSelectFlowPropertyChanged;
        }

        private void OnSessionBoundaryChanged() => FlowCommand.RaiseCanExecuteChanged();

        private void OnAccessPolicyPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(ScadaAccessPolicy.IsLoggedIn)
                or nameof(ScadaAccessPolicy.CurrentGroupId)
                or nameof(ScadaAccessPolicy.Permissions))
                FlowCommand.RaiseCanExecuteChanged();
        }

        private void OnMainRunStateChanged(MainRunState state)
        {
            _runState = state;
            RefreshFlowMenuState();
        }

        private void OnSelectFlowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 菜单的标题 / 徽标 / 禁用态 / 页脚全部由流程状态派生，四类属性任一变化都要重算
            if (e.PropertyName is nameof(FlowModel.IsEnabled)
                or nameof(FlowModel.RunState)
                or nameof(FlowModel.FlowName)
                or nameof(FlowModel.StepsEncrypted))
                RefreshFlowMenuState();
        }

        private async Task FlowCommandExecute(FlowAction? action)
        {
            if (action == null) return;
            if (!_permissionGate.Can(ScadaPermissionGate.PermissionFor(action.Value), out var denyReason))
            {
                Notifier.ShowWarning(ScadaPermissionDenial.Message(
                    ScadaPermissionGate.Describe(action.Value), denyReason));
                return;
            }

            // 运行中编译：RegisterSession 会静默停掉同名运行会话，等于“边跑边拆台”，直接拦下。
            // 判据两条并用：MainRunState（界面整体运行）+ 本条流程自己的 RunState（单流程运行 / HTTP 触发
            // 都不写 MainRunState，只认前者会让"单跑时编译本流程"把那条流程静默杀掉）
            if (action == FlowAction.Compile
                && (IsRunLocked || (SelectFlow != null && SelectFlow.RunState != FlowRunState.Stopped)))
            {
                Notifier.ShowWarning("流程运行中，禁止编译；如需编译请先点击“停止”");
                return;
            }

            switch (action)
            {
                case FlowAction.Create:
                {
                    // 流程名是运行标识（会话 / 运行状态镜像 / HTTP 路由都按名找），必须唯一：
                    // 建重名流程会让"哪条在跑"不可判，这里顺手生成一个不冲突的默认名
                    var flows = Workspace.CurrentSolution.Flows;
                    var newName = "新建流程";
                    int suffix = 2;
                    while (flows.Any(f => f != null && string.Equals(f.FlowName, newName, StringComparison.Ordinal)))
                        newName = $"新建流程{suffix++}";

                    flows.Insert(flows.IndexOf(SelectFlow) + 1, new FlowModel { FlowName = newName });
                    break;
                }
                case FlowAction.Delete:
                    if (MandatoryFlows.IsMandatory(SelectFlow))
                    {
                        Notifier.ShowWarning($"流程 [{SelectFlow.FlowName}] 是强制流程（{SelectFlow.Role}），不允许删除");
                        break;
                    }
                    // 运行中禁止删除：会话 / 状态镜像 / HTTP 路由 / 子程序调用全按流程名寻址，
                    // 删掉之后那条仍在跑的流程就成了"停止也够不着"的孤儿（旧会话与插件实例永不回收）
                    if (SelectFlow.RunState != FlowRunState.Stopped
                        || _runtimeManager.GetSessionByName(SelectFlow.FlowName)?.IsRunning == true)
                    {
                        Notifier.ShowWarning($"流程 [{SelectFlow.FlowName}] 正在运行，禁止删除；请先「停止本流程」");
                        break;
                    }
                    Workspace.CurrentSolution.Flows.Remove(SelectFlow);
                    break;
                case FlowAction.Rename:
                    // 运行中禁止改名（同删除）：改名后旧名字下的会话按名寻址不到，
                    // 正在跑的那条流程连"停止本流程"都找不到它
                    if (SelectFlow.RunState != FlowRunState.Stopped
                        || _runtimeManager.GetSessionByName(SelectFlow.FlowName)?.IsRunning == true)
                    {
                        Notifier.ShowWarning($"流程 [{SelectFlow.FlowName}] 正在运行，禁止改名；请先「停止本流程」");
                        break;
                    }

                    var data = await EasyDialog.ShowTextInputAsync("流程重命名", SelectFlow.FlowName);
                    if (data.IsConfirmed)
                    {
                        var newName = (data.Value ?? string.Empty).Trim();
                        if (newName.Length == 0)
                        {
                            Notifier.ShowWarning("流程名不能为空");
                            break;
                        }
                        // 改名同样要防重：流程名是运行标识，重名会让按名找会话 / 状态镜像错配
                        if (Workspace.CurrentSolution.Flows.Any(f =>
                                f != null && !ReferenceEquals(f, SelectFlow)
                                && string.Equals(f.FlowName, newName, StringComparison.Ordinal)))
                        {
                            Notifier.ShowWarning($"已存在名为「{newName}」的流程（流程名是运行标识，必须唯一）");
                            break;
                        }
                        var oldName = SelectFlow.FlowName;
                        SelectFlow.FlowName = newName;

                        // 旧名字下的会话（若存在）已"谁都不是"：没在跑就注销掉（释放插件实例/HImage），
                        // 下次运行按新名字重新编译注册；在跑的情况已被上面的守卫拦住
                        var stale = _runtimeManager.GetSessionByName(oldName);
                        if (stale != null && !stale.IsRunning)
                            _runtimeManager.UnregisterSession(stale.SessionID);
                    }
                    break;
                case FlowAction.EditComment:
                    var data1 = await EasyDialog.ShowTextInputAsync("流程注释修改", SelectFlow.Description);
                    if (data1.IsConfirmed) SelectFlow.Description = data1.Value;
                    break;
                case FlowAction.ToggleEnabled:
                    if (SelectFlow == null)
                        break;
                    // 不弹提示：列表项禁用态与菜单标题都会随即刷新
                    SelectFlow.IsEnabled = !SelectFlow.IsEnabled;
                    break;
                case FlowAction.BatchDisplayWindow:
                {
                    if (SelectFlow == null)
                        break;

                    // 运行锁口径与 EditGate.ModuleParameters 一致：运行中（非暂停）拦截、暂停放行。
                    // 本 VM 只镜像 MainRunState（单流程运行 / HTTP 触发不广播它，与 Compile 的既有判据同源）；
                    // 批量改的是参数而不是结构，暂停中调参正是调试的核心诉求，不按结构编辑一刀切。
                    if (_runState is MainRunState.RunningOnce or MainRunState.RunningContinuous)
                    {
                        Notifier.ShowWarning(EditGate.RunLockedMessage);
                        break;
                    }

                    // 先扫后写：弹窗里的"影响范围"来自这次扫描，用户取消时一个字节都不动。
                    var plan = BatchDisplayWindowService.BuildPlan(SelectFlow);
                    if (plan.Targets.Count == 0)
                    {
                        Notifier.ShowInfo(plan.LinkedSkipped.Count > 0
                            ? "本流程的显示窗口全部已链接到变量（固定值不生效），没有可批量设置的步骤"
                            : "本流程没有带「显示窗口」的算子步骤");
                        break;
                    }

                    var edit = new BatchDisplayWindowEditModel(plan);
                    if (!EasyDialog.ShowPropertyGridSync("批量设置显示窗口", edit))
                        break;   // 取消 = 草稿丢弃，不写流程

                    // 越界不写：RangeValidation 只是行内红字，用户仍可能带着越界值点确认
                    if (!BatchDisplayWindowService.IsValidValue(edit.DisplayViewIndex))
                    {
                        Notifier.ShowWarning("显示窗口号必须是 0~9（0 = 不显示）");
                        break;
                    }

                    var result = BatchDisplayWindowService.Apply(plan, edit.DisplayViewIndex);
                    Notifier.ShowSuccess(result.BuildMessage());
                    break;
                }
                case FlowAction.Compile:
                    if (SelectFlow == null)
                        break;
                    if (!SelectFlow.IsEnabled)
                    {
                        Notifier.ShowInfo($"流程 [{SelectFlow.FlowName}] 已禁用，跳过编译");
                        break;
                    }

                    var compileResult = _compiler.Compile(SelectFlow.Steps, SelectFlow.FlowName);
                    if (!compileResult.Success)
                    {
                        foreach (var err in compileResult.Errors)
                            Notifier.ShowError($"[{SelectFlow.FlowName}] {err}");
                        break;
                    }

                    var session = new FlowSession
                    {
                        FlowName = SelectFlow.FlowName,
                        ExecutionEngine = compileResult.Data,
                        CompiledVersion = SelectFlow.Version,
                        CompiledFlowId = SelectFlow.FlowID ?? string.Empty,
                    };
                    // 蓝图必须填充（含嵌套步骤）：否则编译出的会话运行时步骤状态无法回写 UI
                    session.AddBlueprintsDeep(SelectFlow.Steps);
                    _runtimeManager.RegisterSession(session);

                    Notifier.ShowSuccess($"流程 [{SelectFlow.FlowName}] 编译完成，版本：{session.CompiledVersion}");
                    break;
                case FlowAction.Manager:
                    ShowFlowManager();
                    break;
                case FlowAction.RunOnce:
                    await RunSingleFlowAsync(continuous: false);
                    break;
                case FlowAction.RunContinuous:
                    await RunSingleFlowAsync(continuous: true);
                    break;
                case FlowAction.Stop:
                    StopSingleFlow();
                    break;
                case FlowAction.VariableEvents:
                    // 方案级变量事件：入口在流程栏，但配置不归属单条流程
                    // （ScadaDocument.VariableEvents 是一张方案级表）。
                    dialogService.ShowDialog("ScadaVariableEventDialogView");
                    break;
                case FlowAction.ResultSource:
                    ShowResultSourceDialog();
                    break;
            }
        }

        /// <summary>
        /// 「运行统计 · 结果源」：只对当前选中的那条流程开窗。
        /// 传 Id 为主、名字兜底——与运行态解析同一顺序，避免"改完发现改的是同名的另一条"。
        /// </summary>
        private void ShowResultSourceDialog()
        {
            var flow = SelectFlow;
            if (flow == null) return;

            var parameters = new DialogParameters
            {
                { FlowResultSourceDialogViewModel.FlowIdKey, flow.FlowID ?? string.Empty },
                { FlowResultSourceDialogViewModel.FlowNameKey, flow.FlowName ?? string.Empty },
            };
            dialogService.ShowDialog(FlowResultSourceDialogViewModel.DialogName, parameters);
        }

        /// <summary>
        /// 运行单条流程（程序栏右键入口）。
        ///
        /// 走**非调试**路径（不传 debugSession）：与 HTTP 触发 / 定时触发同口径，断点不生效；
        /// 需要断点调试请用主界面的运行按钮（那里装调试门）。
        /// 会话准备与"编译本流程"同款：优先复用运行管理器里未过期的会话，
        /// 缺了或图纸改过（Version 涨了）才重编译——否则拿旧图纸跑新图，结果对不上用户看到的设计。
        ///
        /// 【编译挪出 UI 线程（2026-10-27）】
        /// <see cref="FlowSessionFactory.TryEnsureSession"/> 里含 <c>FlowCompiler.Compile</c>：
        /// 大流程几百毫秒到数秒。旧实现在 UI 线程同步跑，点"循环运行"那一下界面会顿住。
        /// 而定时触发（<c>FlowAutoRunner</c>）与 HTTP 触发本来就是**在后台线程**编译的——
        /// 手动入口没有理由特殊。竞态口径与它们一致：编译期间用户仍可编辑图纸，
        /// 会话里记的是"编译前版本号"，图纸改过下一轮判过期自动重编译；真正互斥仍由会话锁保证。
        /// 编译期间新增的两条竞态由这里复核：流程被删/禁用则放弃启动。
        /// </summary>
        private async Task RunSingleFlowAsync(bool continuous)
        {
            var flow = SelectFlow;
            if (flow == null) return;

            if (!flow.IsEnabled)
            {
                Notifier.ShowWarning($"流程 [{flow.FlowName}] 已禁用，请先启用再运行");
                return;
            }

            if (flow.StepsEncrypted)
            {
                Notifier.ShowWarning($"流程 [{flow.FlowName}] 步序已加密，无法编译运行");
                return;
            }

            // 已在运行就先停再跑：**必须在重编译之前判**——编译走 RegisterSession，
            // 它会先把同名运行会话停掉，等编译完再看 IsRunning 已经晚了（老会话被换掉了）
            if (_runtimeManager.GetSessionByName(flow.FlowName)?.IsRunning == true
                || flow.RunState != FlowRunState.Stopped)
            {
                Notifier.ShowWarning($"流程 [{flow.FlowName}] 已在运行中；如需重跑请先「停止本流程」");
                return;
            }

            // 编译是异步的：不挡的话双击会排两次编译（命令的 CanExecute 不判"正在启动"）
            if (_starting) return;
            _starting = true;
            try
            {
                // 会话准备：单点收口（查会话 → 判新鲜度[流程身份 FlowID + 版本] → 必要时重编译 → 注册）
                // 放后台线程：这一句里最贵的是编译。await 之后回到 UI 线程（命令在 UI 线程发起）。
                var (prepared, session, sessionError) = await Task.Run(() =>
                {
                    var ok = FlowSessionFactory.TryEnsureSession(
                        _runtimeManager, _compiler, flow, out var preparedSession, out var error, _log);
                    return (ok, preparedSession, error);
                });

                if (!prepared || session == null)
                {
                    Notifier.ShowError($"[{flow.FlowName}] {sessionError}");
                    return;
                }

                // 编译期间界面仍可操作：流程可能被删除/禁用。启动前复核一次，
                // 避免"点了运行、编译完却启动了一条已经不在方案里的流程"。
                if (Workspace?.CurrentSolution?.Flows == null
                    || !Workspace.CurrentSolution.Flows.Contains(flow)
                    || !flow.IsEnabled)
                {
                    Notifier.ShowWarning($"流程 [{flow.FlowName}] 在编译期间已被删除或禁用，本次未启动");
                    return;
                }

                // 到这里 session 必定"没在跑"（上面已拦）；编译分支会把会话换成新实例，
                // 复用的分支沿用原实例——两条路的 IsRunning 都是 false
                if (continuous)
                {
                    var continuousTask = _flowEngine.RunSessionAsync(session);
                    // 连续运行的"抢不到锁"没有返回值可判：引擎的抢锁与 IsRunning 置位在首个 await 之前
                    // **同步**完成，所以调用返回后取快照是准的（不猜时序）
                    bool startedNow = session.IsRunning;
                    _ = TrackSingleRunAsync(continuousTask, flow.FlowName);
                    Notifier.ShowSuccess(startedNow
                        ? $"流程 [{flow.FlowName}] 已开始循环运行"
                        : $"流程 [{flow.FlowName}] 未启动：会话正被其它触发占用（HTTP / 定时 / 手动）");
                }
                else
                {
                    _ = TrackSingleRunAsync(_flowEngine.TryRunSessionOnceAsync(session), flow.FlowName);
                    Notifier.ShowSuccess($"流程 [{flow.FlowName}] 单次运行已启动");
                }
            }
            finally
            {
                _starting = false;
            }
        }

        /// <summary>停止单条流程（按流程名找会话；引擎侧"没在跑"只是 Warn，这里先给用户一句人话）</summary>
        private void StopSingleFlow()
        {
            var flow = SelectFlow;
            if (flow == null) return;

            var session = _runtimeManager.GetSessionByName(flow.FlowName);
            if (session == null || !session.IsRunning)
            {
                Notifier.ShowInfo($"流程 [{flow.FlowName}] 当前未在运行");
                return;
            }

            _flowEngine.StopSession(session);
        }

        /// <summary>
        /// 观察单流程运行任务的结局：消化异常（不消化会变成"未观察异常"，GC 时可能把进程打挂），
        /// 兜底记一条 Warn；单次执行时若引擎回报"没跑上"（false），补一条如实提示，
        /// 不让界面刚宣布的"已启动"变成一句空话
        /// </summary>
        private async Task TrackSingleRunAsync(Task<bool> runTask, string flowName)
        {
            try
            {
                if (!await runTask)
                    Notifier.ShowWarning($"流程 [{flowName}] 未启动：会话正被其它触发占用（HTTP / 定时 / 手动）");
            }
            catch (Exception ex)
            {
                _log?.Warn($"[{flowName}] 单流程运行任务异常结束（引擎侧失败原因见运行日志）：{ex.Message}");
            }
        }

        /// <summary>连续运行的跟随观察（无返回值可判，只消化异常）</summary>
        private async Task TrackSingleRunAsync(Task runTask, string flowName)
        {
            try
            {
                await runTask;
            }
            catch (Exception ex)
            {
                _log?.Warn($"[{flowName}] 单流程运行任务异常结束（引擎侧失败原因见运行日志）：{ex.Message}");
            }
        }

        /// <summary>
        /// 显示流程管理对话框
        /// </summary>
        private void ShowFlowManager()
        {
            if (Workspace.CurrentSolution == null) return;

            var parameters = new DialogParameters();
            parameters.Add("Flows", Workspace.CurrentSolution.Flows);

            dialogService.ShowDialog("FlowManagerView", parameters, result =>
            {
                // 可以在这里处理对话框关闭后的逻辑
            });
        }
    }
}
