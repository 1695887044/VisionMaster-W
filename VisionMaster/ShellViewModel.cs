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
                    await OpenSolutionAsync();
                    break;
                case SolutionAction.Save:
                    await SaveSolutionAsync();
                    break;
                case SolutionAction.BrowseList:
                    break;
            }
        }

        /// <summary>
        /// 打开解决方案
        /// </summary>
        private async Task OpenSolutionAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "VisionMaster方案 (*.vms)|*.vms|所有文件 (*.*)|*.*",
                Title = "打开解决方案",
                DefaultExt = ".vms",
                CheckFileExists = true
            };

            var result = dialog.ShowDialog();
            if (result == true)
            {
                var loadResult = await solutionService.LoadAsync(dialog.FileName);
                if (loadResult.Success)
                {
                    Workspace.SwitchSolution(loadResult.Data);
                    Notifier.ShowSuccess($"方案 [{loadResult.Data.SolutionName}] 加载成功");
                }
                else
                {
                    Notifier.ShowError(loadResult.Message);
                }
            }
        }

        /// <summary>
        /// 保存解决方案
        /// </summary>
        private async Task SaveSolutionAsync()
        {
            if (Workspace.CurrentSolution == null)
            {
                Notifier.ShowWarning("当前没有打开的解决方案");
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "VisionMaster方案 (*.vms)|*.vms|所有文件 (*.*)|*.*",
                Title = "保存解决方案",
                DefaultExt = ".vms",
                FileName = $"{Workspace.CurrentSolution.SolutionName}.vms"
            };

            var result = dialog.ShowDialog();
            if (result == true)
            {
                var saveResult = await solutionService.SaveAsync(Workspace.CurrentSolution, dialog.FileName);
                if (saveResult.Success)
                {
                    Notifier.ShowSuccess($"方案 [{Workspace.CurrentSolution.SolutionName}] 保存成功");
                }
                else
                {
                    Notifier.ShowError(saveResult.Message);
                }
            }
        }

        private FlowSession _currentSession;

        private void OnExecutionAction(ExecutionAction? action)
        {
            switch (action)
            {
                case ExecutionAction.Compile:
                    DoCompileAll();
                    break;
                case ExecutionAction.RunOnce:
                    RunAllEnabledOnce();
                    break;
                case ExecutionAction.RunContinuous:
                    RunAllEnabledContinuous();
                    break;
                case ExecutionAction.Stop:
                    StopAllRunning();
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

        /// <summary>
        /// 编译当前单个流程（保留原有方法用于兼容性）
        /// </summary>
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

            var newSession = new FlowSession
            {
                FlowName = CurrentFlowName,
                ExecutionEngine = result.Data,
                CompiledVersion = Workspace.CurrentFlow.Version,
            };

            _runtimeManager.RegisterSession(newSession);

            Notifier.ShowSuccess(
                $"流程 [{CurrentFlowName}] 编译完成，版本：{newSession.CompiledVersion}"
            );
        }

        /// <summary>
        /// 批量编译所有流程
        /// </summary>
        private void DoCompileAll()
        {
            if (Workspace.CurrentSolution == null || Workspace.CurrentSolution.Flows.Count == 0)
            {
                Notifier.ShowWarning("当前没有可编译的流程");
                return;
            }

            int successCount = 0;
            int skipCount = 0;
            int failCount = 0;

            foreach (var flow in Workspace.CurrentSolution.Flows)
            {
                if (!flow.IsEnabled)
                {
                    Notifier.ShowInfo($"流程 [{flow.FlowName}] 已禁用，跳过编译");
                    skipCount++;
                    continue;
                }

                var result = _flowCompiler.Compile(flow.Steps, flow.FlowName);
                if (result.Success)
                {
                    var newSession = new FlowSession
                    {
                        FlowName = flow.FlowName,
                        ExecutionEngine = result.Data,
                        CompiledVersion = flow.Version,
                    };

                    foreach (var step in flow.Steps)
                    {
                        newSession.Blueprints.Add(step);
                    }

                    _runtimeManager.RegisterSession(newSession);
                    successCount++;
                }
                else
                {
                    foreach (var item in result.Errors)
                    {
                        Notifier.ShowError($"[{flow.FlowName}] {item}");
                    }
                    failCount++;
                }
            }

            if (skipCount > 0)
            {
                Notifier.ShowSuccess(
                    $"批量编译完成：成功 {successCount} 个，跳过 {skipCount} 个，失败 {failCount} 个"
                );
            }
            else
            {
                Notifier.ShowSuccess(
                    $"批量编译完成：成功 {successCount} 个，失败 {failCount} 个"
                );
            }
        }

        /// <summary>
        /// 批量运行所有已启用流程（单次运行）
        /// </summary>
        private void RunAllEnabledOnce()
        {
            if (Workspace.CurrentSolution == null || Workspace.CurrentSolution.Flows.Count == 0)
            {
                Notifier.ShowWarning("当前没有可运行的流程");
                return;
            }

            int runCount = 0;

            foreach (var flow in Workspace.CurrentSolution.Flows)
            {
                if (!flow.IsEnabled)
                    continue;

                var session = _runtimeManager.GetSessionByName(flow.FlowName);
                
                // 检查是否需要重新编译
                if (session == null || flow.Version > session.CompiledVersion)
                {
                    var result = _flowCompiler.Compile(flow.Steps, flow.FlowName);
                    if (!result.Success)
                    {
                        foreach (var item in result.Errors)
                        {
                            Notifier.ShowError($"[{flow.FlowName}] {item}");
                        }
                        continue;
                    }

                    session = new FlowSession
                    {
                        FlowName = flow.FlowName,
                        ExecutionEngine = result.Data,
                        CompiledVersion = flow.Version,
                    };

                    foreach (var step in flow.Steps)
                    {
                        session.Blueprints.Add(step);
                    }

                    _runtimeManager.RegisterSession(session);
                }

                if (session != null && !session.IsRunning)
                {
                    _ = flowEngine.RunSessionOnceAsync(session);
                    runCount++;
                }
            }

            if (runCount > 0)
            {
                Notifier.ShowSuccess($"已启动 {runCount} 个流程的单次运行");
            }
            else
            {
                Notifier.ShowWarning("没有可运行的流程（请确保流程已启用且未加密）");
            }
        }

        /// <summary>
        /// 批量运行所有已启用流程（连续运行）
        /// </summary>
        private void RunAllEnabledContinuous()
        {
            if (Workspace.CurrentSolution == null || Workspace.CurrentSolution.Flows.Count == 0)
            {
                Notifier.ShowWarning("当前没有可运行的流程");
                return;
            }

            int runCount = 0;

            foreach (var flow in Workspace.CurrentSolution.Flows)
            {
                if (!flow.IsEnabled)
                    continue;

                var session = _runtimeManager.GetSessionByName(flow.FlowName);
                
                // 检查是否需要重新编译
                if (session == null || flow.Version > session.CompiledVersion)
                {
                    var result = _flowCompiler.Compile(flow.Steps, flow.FlowName);
                    if (!result.Success)
                    {
                        foreach (var item in result.Errors)
                        {
                            Notifier.ShowError($"[{flow.FlowName}] {item}");
                        }
                        continue;
                    }

                    session = new FlowSession
                    {
                        FlowName = flow.FlowName,
                        ExecutionEngine = result.Data,
                        CompiledVersion = flow.Version,
                    };

                    foreach (var step in flow.Steps)
                    {
                        session.Blueprints.Add(step);
                    }

                    _runtimeManager.RegisterSession(session);
                }

                if (session != null && !session.IsRunning)
                {
                    _ = flowEngine.RunSessionAsync(session);
                    runCount++;
                }
            }

            if (runCount > 0)
            {
                Notifier.ShowSuccess($"已启动 {runCount} 个流程的连续运行");
            }
            else
            {
                Notifier.ShowWarning("没有可运行的流程（请确保流程已启用且未加密）");
            }
        }

        /// <summary>
        /// 停止所有正在运行的流程
        /// </summary>
        private void StopAllRunning()
        {
            flowEngine.StopAll();
            Notifier.ShowSuccess("已停止所有运行中的流程");
        }

        #endregion
    }
}
