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

                RaisePropertyChanged(nameof(FlowToggleHeader));
                Workspace.SwitchFlow(value);
            }
        }

        /// <summary>右键菜单「启用/禁用」的标题：跟随当前选中流程的启用状态</summary>
        public string FlowToggleHeader => SelectFlow?.IsEnabled == true ? "禁用" : "启用";

        public AsyncDelegateCommand<FlowAction?> FlowCommand { get; }

        /// <summary>
        /// 运行状态镜像：由 ShellViewModel 经 GlobalEventBus 广播同步。
        /// 运行中编译会被 RegisterSession 静默停掉同名会话（边跑边拆台），故编译单点拦截。
        /// </summary>
        private MainRunState _runState = MainRunState.NotStarted;
        private bool IsRunLocked => _runState != MainRunState.NotStarted;

        public FlowListViewModel(
            IWorkspaceManager workspace,
            IDialogService dialogService,
            FlowCompiler compiler,
            IRuntimeManager runtimeManager,
            IFlowEngine flowEngine,
            ILogService logService)
        {
            Workspace = workspace;
            this.dialogService = dialogService;
            _compiler = compiler;
            _runtimeManager = runtimeManager;
            _flowEngine = flowEngine;
            _log = logService;
            FlowCommand = new AsyncDelegateCommand<FlowAction?>(FlowCommandExecute);
        }

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

            if (SelectFlow != null)
                SelectFlow.PropertyChanged -= OnSelectFlowPropertyChanged;
        }

        private void OnMainRunStateChanged(MainRunState state) => _runState = state;

        private void OnSelectFlowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FlowModel.IsEnabled))
                RaisePropertyChanged(nameof(FlowToggleHeader));
        }

        private async Task FlowCommandExecute(FlowAction? action)
        {
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
                    Workspace.CurrentSolution.Flows.Remove(SelectFlow);
                    break;
                case FlowAction.Rename:
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
                        SelectFlow.FlowName = newName;
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
            }
        }

        /// <summary>
        /// 运行单条流程（程序栏右键入口）。
        ///
        /// 走**非调试**路径（不传 debugSession）：与 HTTP 触发 / 定时触发同口径，断点不生效；
        /// 需要断点调试请用主界面的运行按钮（那里装调试门）。
        /// 会话准备与"编译本流程"同款：优先复用运行管理器里未过期的会话，
        /// 缺了或图纸改过（Version 涨了）才重编译——否则拿旧图纸跑新图，结果对不上用户看到的设计。
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
            var session = _runtimeManager.GetSessionByName(flow.FlowName);
            if (session?.IsRunning == true || flow.RunState != FlowRunState.Stopped)
            {
                Notifier.ShowWarning($"流程 [{flow.FlowName}] 已在运行中；如需重跑请先「停止本流程」");
                return;
            }

            if (session == null || flow.Version > session.CompiledVersion)
            {
                var compileResult = _compiler.Compile(flow.Steps, flow.FlowName);
                if (!compileResult.Success)
                {
                    foreach (var err in compileResult.Errors)
                        Notifier.ShowError($"[{flow.FlowName}] {err}");
                    return;
                }

                session = new FlowSession
                {
                    FlowName = flow.FlowName,
                    ExecutionEngine = compileResult.Data,
                    CompiledVersion = flow.Version,
                };
                session.AddBlueprintsDeep(flow.Steps);
                _runtimeManager.RegisterSession(session);
            }

            // 到这里 session 必定"没在跑"（上面已拦）；编译分支会把会话换成新实例，
            // 复用的分支沿用原实例——两条路的 IsRunning 都是 false
            if (continuous)
            {
                _ = TrackSingleRunAsync(_flowEngine.RunSessionAsync(session), flow.FlowName);
                Notifier.ShowSuccess($"流程 [{flow.FlowName}] 已开始循环运行");
            }
            else
            {
                _ = TrackSingleRunAsync(_flowEngine.RunSessionOnceAsync(session), flow.FlowName);
                Notifier.ShowSuccess($"流程 [{flow.FlowName}] 单次运行已启动");
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
        /// 并兜底记一条 Warn —— 引擎只保证把**自己的**失败写进日志，任务之外的异常（取消竞态等）
        /// 若这里也一声不吭，现场就只剩"流程莫名停了"这一条线索
        /// </summary>
        private async Task TrackSingleRunAsync(Task task, string flowName)
        {
            try
            {
                await task;
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
