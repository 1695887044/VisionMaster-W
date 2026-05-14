using System.Reflection.Metadata;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Core.Interfaces;
using NLog;
using Prism.Common;
using Prism.Dialogs;
using UI.Attributes;
using UI.CustomControl;
using UI.Helper;
using VisionMaster.Helpers;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.ViewModels;
using VisionMaster.Views;
using VisionMaster.Views.DialogViews;

namespace VisionMaster
{
    public class ShellViewModel : BindableBase
    {
        private readonly FlowCompiler _flowCompiler;
        private readonly IFlowEngine flowEngine;
        private readonly IExecutionContext executionContext;
        private readonly IDialogService dialogService;
        private readonly IFlowEngine flowService;
        private readonly IRuntimeManager _runtimeManager;
        private CancellationTokenSource _cts;
        private Task _monitorTask;
        public IWorkspaceManager Workspace { get; }
        private string CurrentFlowName => Workspace.CurrentFlow?.FlowName;
        public SolutionService solutionService { get; }

        #region 系统状态
        public string HandleCountStr
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }
        public string ThreadCountStr
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }
        public string CpuUsageStr
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }
        public string MemoryUsageStr
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }
        public string CurrentTimeText
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }
        #endregion
        #region Commands
        public AsyncDelegateCommand<SolutionAction?> SolutionCommand { get; }
        public DelegateCommand<ExecutionAction?> ExecutionCommand { get; }
        public DelegateCommand<SystemAction?> SystemCommand { get; }

        #endregion
        public ShellViewModel(
            SolutionService solutionService,
            IWorkspaceManager workspaceManager,
            IFlowEngine flowEngine,
            IExecutionContext executionContext,
            IDialogService dialogService,
            IFlowEngine flowService,
            IRuntimeManager _runtimeManager,
            FlowCompiler _flowCompiler
        )
        {
            StartBackgroundMonitoring();
            SolutionCommand = new(ExecuteProjectAction);
            ExecutionCommand = new DelegateCommand<ExecutionAction?>(OnExecutionAction);
            SystemCommand = new DelegateCommand<SystemAction?>(OnSystemAction);
            this.solutionService = solutionService;
            this.Workspace = workspaceManager;
            this.flowEngine = flowEngine;
            this.executionContext = executionContext;
            this.dialogService = dialogService;
            this.flowService = flowService;
            this._runtimeManager = _runtimeManager;
            this._flowCompiler = _flowCompiler;
        }

        /// <summary>
        /// 对于解决方案的操作
        /// </summary>
        /// <param name="action"></param>
        private async Task ExecuteProjectAction(SolutionAction? action)
        {
            switch (action)
            {
                case SolutionAction.Create:
                    var newSolution = new SolutionModel();
                    var isConfirmed = await EasyDialog.ShowPropertyGridAsync(
                        "创建新解决方案",
                        newSolution
                    );
                    if (isConfirmed)
                    {
                        solutionService.Create(newSolution);
                        Workspace.SwitchSolution(newSolution);
                    }
                    break;
                case SolutionAction.Open:
                    await EasyDialog.ShowCustomAsync("", new ConditionEditorView());
                    break;
                case SolutionAction.Save:
                    break;
                case SolutionAction.BrowseList:
                    break;
            }
        }

        private FlowSession _currentSession;

        private void OnExecutionAction(ExecutionAction? action)
        {
            switch (action)
            {
                case ExecutionAction.Compile:
                    DoCompile();
                    break;
                case ExecutionAction.RunOnce:
                    if (!PreRunCheck())
                        return;
                    var sessionOnce = _runtimeManager.GetSessionByName(CurrentFlowName);
                    if (sessionOnce != null)
                    {
                        _ = flowEngine.RunSessionOnceAsync(sessionOnce);
                    }
                    break;
                case ExecutionAction.RunContinuous:
                    if (!PreRunCheck())
                        return;
                    // 从仓库拿机器，丢给引擎跑死循环
                    var sessionCont = _runtimeManager.GetSessionByName(CurrentFlowName);
                    if (sessionCont != null)
                    {
                        _ = flowEngine.RunSessionAsync(sessionCont);
                    }
                    break;
                case ExecutionAction.Stop:
                    var sessionToStop = _runtimeManager.GetSessionByName(CurrentFlowName);
                    if (sessionToStop != null)
                    {
                        flowEngine.StopSession(sessionToStop);
                    }
                    break;
            }
        }

        private void OnSystemAction(SystemAction? action)
        {
            switch (action)
            {
                case SystemAction.GlobalVariables:
                    dialogService.ShowDialog("GlobalVariable");
                    break;
                case SystemAction.CameraSettings:
                    // TODO: 弹出相机配置 Dialog
                    break;
            }
        }

        private void StartBackgroundMonitoring()
        {
            _cts = new CancellationTokenSource();
            _monitorTask = Task.Run(
                async () =>
                {
                    while (!_cts.Token.IsCancellationRequested)
                    {
                        try
                        {
                            var metrics = SystemMonitor.Instance.GetAllMetrics();

                            CpuUsageStr = $"CPU: {metrics.CpuUsagePercent:F1}%";
                            MemoryUsageStr = $"MEM: {metrics.PrivateMemoryMB:F0} MB";
                            ThreadCountStr = $"THD: {metrics.ThreadCount}";
                            HandleCountStr = $"HDL: {metrics.HandleCount}";
                            CurrentTimeText = DateTime.Now.ToString(" HH:mm:ss");
                            //CurrentTimeText = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            Notifier.ShowError(ex.Message);
                        }

                        await Task.Delay(TimeSpan.FromSeconds(1), _cts.Token);
                    }
                },
                _cts.Token
            );
        }

        #region Methods
        private bool PreRunCheck()
        {
            if (Workspace.CurrentFlow == null)
                return false;

            // 从仓库中寻找当前流程的运行实例
            var session = _runtimeManager.GetSessionByName(CurrentFlowName);

            // 1. 是否从来没编译过？(仓库里找不到)
            if (session == null)
            {
                var result = EasyDialog.ShowSync("提示", "当前流程尚未编译，是否立即编译并运行？");
                if (result)
                {
                    DoCompile();
                    return true;
                }
                return false;
            }

            // 2. 🌟 脏检查：图纸版本是否大于已编译的物理机版本？
            if (Workspace.CurrentFlow.Version > session.CompiledVersion)
            {
                var result = EasyDialog.ShowSync(
                    "配置已更改",
                    "检测到流程图纸已修改，当前的运行逻辑已过期。\n是否重新编译？"
                );
                if (result)
                {
                    DoCompile();
                    return true;
                }
                return false;
            }

            return true;
        }

        private void DoCompile()
        {
            if (Workspace.CurrentFlow == null)
                return;

            var result = _flowCompiler.Compile(Workspace.CurrentFlow.Steps);
            if (!result.Success)
            {
                foreach (var item in result.Errors)
                {
                    Notifier.ShowError(item);
                }
                return;
            }

            // 1. 造出新的物理机器
            var newSession = new FlowSession
            {
                FlowName = CurrentFlowName,
                ExecutionEngine = result.Data,
                CompiledVersion = Workspace.CurrentFlow.Version, // 🌟 同步版本号
            };

            // 2. 将机器推入仓库，完成登记！(如果之前有同名机器在跑，内部逻辑会处理)
            _runtimeManager.RegisterSession(newSession);

            Notifier.ShowSuccess(
                $"流程 [{CurrentFlowName}] 编译完成，版本：{newSession.CompiledVersion}"
            );
        }

        #endregion
    }
}
