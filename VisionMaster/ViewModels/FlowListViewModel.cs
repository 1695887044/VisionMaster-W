using Core.Events;
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
            IRuntimeManager runtimeManager)
        {
            Workspace = workspace;
            this.dialogService = dialogService;
            _compiler = compiler;
            _runtimeManager = runtimeManager;
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
            // 其余列表操作（增删改名）本轮不加锁，行为保持原样
            if (action == FlowAction.Compile && IsRunLocked)
            {
                Notifier.ShowWarning("流程运行中，禁止编译；如需编译请先点击“停止”");
                return;
            }

            switch (action)
            {
                case FlowAction.Create:
                    Workspace.CurrentSolution.Flows.Insert(Workspace.CurrentSolution.Flows.IndexOf(SelectFlow) + 1, new FlowModel() { FlowName = "新建流程" });
                    break;
                case FlowAction.Delete:
                    Workspace.CurrentSolution.Flows.Remove(SelectFlow);
                    break;
                case FlowAction.Rename:
                    var data = await EasyDialog.ShowTextInputAsync("流程重命名", SelectFlow.FlowName);
                    if (data.IsConfirmed) SelectFlow.FlowName = data.Value;
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
