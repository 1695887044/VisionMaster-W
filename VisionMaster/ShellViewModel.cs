using Core.Interfaces;
using AvalonDock.Layout;
using HslCommunication.Profinet.Siemens;
using NLog;
using Prism.Common;
using Prism.Dialogs;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using UI.Attributes;
using UI.CustomControl;
using Core.Events;
using UI.Helper;
using VisionMaster.Communications;
using VisionMaster.EventModel;
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
        private readonly AdvancedCommunicationManager _communicationManager;
        private readonly NetworkVariableBridge _variableBridge;
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

        public DelegateCommand<string> SwitchCanvasCommand {  get; }

        public DelegateCommand<string> TogglePanelCommand { get; }

        #endregion

        #region 活动栏（Activity Bar）
        /// <summary>
        /// 活动栏按钮高亮状态跟随面板 IsActive（OneWay 绑定，由监听器驱动）
        /// </summary>
        public bool IsFlowListActive
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }

        public bool IsProcessActive
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }

        public bool IsToolboxActive
        {
            get { return field; }
            set { SetProperty(ref field, value); }
        }

        /// <summary>
        /// 已挂接 PropertyChanged 监听的面板（布局重载会生成新实例，需先解绑旧的）
        /// </summary>
        private readonly List<(LayoutContent Panel, PropertyChangedEventHandler Handler)> _dockWatchers = new();

        /// <summary>
        /// 活动栏开关：不可见→显示并激活；可见未激活→切到前台；已激活→收起
        /// </summary>
        private void TogglePanel(string contentId)
        {
            if (!LayoutHelper.IsPanelVisible(contentId))
            {
                LayoutHelper.ShowPanel(contentId);
            }
            else if (!LayoutHelper.IsPanelActive(contentId))
            {
                LayoutHelper.ShowPanel(contentId); // 已显示但被遮挡/失活，切到前台
            }
            else
            {
                LayoutHelper.HidePanel(contentId);
            }
            RefreshActivityBar();
        }

        /// <summary>
        /// 监听程序/工具箱面板的可见性与激活状态，驱动活动栏高亮
        /// 布局加载/重载后必须重新调用（LayoutContent 实例会被替换）
        /// </summary>
        private void AttachDockWatchers()
        {
            foreach (var (panel, handler) in _dockWatchers)
                panel.PropertyChanged -= handler;
            _dockWatchers.Clear();

            foreach (var contentId in new[] { "Panel_FlowListView", "Panel_ProcessView", "Panel_ToolView" })
            {
                // LayoutContent 基类同时兼容停靠面板与文档选项卡（工具箱为文档类型）
                if (LayoutHelper.FindPanel(contentId) is not LayoutContent panel) continue;

                PropertyChangedEventHandler handler = (_, e) =>
                {
                    // 文档类型无 IsVisible 属性，用 IsSelected（标签选中）替代
                    if (e.PropertyName is nameof(LayoutAnchorable.IsVisible) or nameof(LayoutContent.IsActive) or nameof(LayoutDocument.IsSelected))
                        RefreshActivityBar();
                };
                panel.PropertyChanged += handler;
                _dockWatchers.Add((panel, handler));
            }

            RefreshActivityBar();
        }

        private void RefreshActivityBar()
        {
            IsFlowListActive = LayoutHelper.IsPanelActive("Panel_FlowListView");
            IsProcessActive = LayoutHelper.IsPanelActive("Panel_ProcessView");
            IsToolboxActive = LayoutHelper.IsPanelActive("Panel_ToolView");
        }
        #endregion
        public ShellViewModel(
            SolutionService solutionService,
            IWorkspaceManager workspaceManager,
            IFlowEngine flowEngine,
            IExecutionContext executionContext,
            IDialogService dialogService,
            IFlowEngine flowService,
            IRuntimeManager _runtimeManager,
            FlowCompiler _flowCompiler,
            AdvancedCommunicationManager communicationManager,
            NetworkVariableBridge variableBridge
        )
        {
            StartBackgroundMonitoring();
            SolutionCommand = new(ExecuteProjectAction);
            ExecutionCommand = new DelegateCommand<ExecutionAction?>(OnExecutionAction);
            SystemCommand = new DelegateCommand<SystemAction?>(OnSystemAction);
            SwitchCanvasCommand = new DelegateCommand<string>(SwitchCanvas);
            TogglePanelCommand = new DelegateCommand<string>(TogglePanel);
            LayoutHelper.LayoutLoaded += AttachDockWatchers;
            this.solutionService = solutionService;
            this.Workspace = workspaceManager;
            this.flowEngine = flowEngine;
            this.executionContext = executionContext;
            this.dialogService = dialogService;
            this.flowService = flowService;
            this._runtimeManager = _runtimeManager;
            this._flowCompiler = _flowCompiler;
            this._communicationManager = communicationManager;
            this._variableBridge = variableBridge;

            // Core 层持久化服务的通信管理器引用（保存/加载方案时需要按协议重建地址）
            Services.ServiceLocator.CommunicationManager = communicationManager;

            // 启动时恢复上次保存的连接配置（communications.json；通信设置对话框关闭时保存）
            _ = communicationManager.LoadConfigAsync();
        }

        private void SwitchCanvas(string obj)
        {
            eViewMode viewMode = obj switch
            {
                "1" => eViewMode.One,
                "2" => eViewMode.Two,
                "3" => eViewMode.Three,
                "4" => eViewMode.Four,
                "5" => eViewMode.Five,
                "6" => eViewMode.Six,
                "7" => eViewMode.Seven,
                "8" => eViewMode.Eight,
                "9" => eViewMode.Night,
                _ => eViewMode.Night,
            };

            GlobalEventBus.Publish<ImageCanvasChangeEvent>(new ImageCanvasChangeEvent { ViewMode = viewMode });
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
                    dialogService.ShowDialog("SolutionListView");
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
                    loadResult.Data.SolutionFilePath = dialog.FileName;
                    Workspace.SwitchSolution(loadResult.Data);
                    Services.SolutionConfigApplier.Restore(loadResult.Data.Config);
                    // 按快照重建变量集合 + 重新接线网络变量（轮询镜像链路）
                    VariablePersistenceService.Restore(loadResult.Data, Workspace);
                    _variableBridge.RebindAll();
                    Notifier.ShowSuccess($"方案 [{loadResult.Data.SolutionName}] 加载成功");
                }
                else
                {
                    Notifier.ShowError(loadResult.Message);
                }
            }
        }

        /// <summary>
        /// 捕获当前界面布局到方案系统配置（保存方案前调用）
        /// </summary>
        private void CaptureLayoutToConfig()
        {
            Services.SolutionConfigApplier.Capture(Workspace.CurrentSolution);
        }

        /// <summary>
        /// 软件启动时按软件级配置（AppConfig.json）自动加载默认启动方案
        /// 无默认方案或文件不存在时不做任何事
        /// </summary>
        public async Task AutoLoadStartupSolutionAsync()
        {
            var settings = ContainerLocator.Container.Resolve<AppSettingsService>();
            var startupPath = settings.Current.StartupSolutionPath;
            if (string.IsNullOrWhiteSpace(startupPath) || !System.IO.File.Exists(startupPath)) return;

            var loadResult = await solutionService.LoadAsync(startupPath);
            if (!loadResult.Success) return;

            loadResult.Data.SolutionFilePath = startupPath;
            Workspace.SwitchSolution(loadResult.Data);
            // 自动加载不恢复方案布局：保持"上次关闭时的布局"，避免覆盖用户重置的默认布局；
            // 手动打开方案仍按方案记忆布局（Restore 默认 true）
            Services.SolutionConfigApplier.Restore(loadResult.Data.Config, restoreLayout: false);
            // 按快照重建变量集合 + 重新接线网络变量（轮询镜像链路）
            VariablePersistenceService.Restore(loadResult.Data, Workspace);
            _variableBridge.RebindAll();
            Notifier.ShowSuccess($"已自动加载方案 [{loadResult.Data.SolutionName}]");
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

            var result = dialog.ShowDialog() == true;
            if (result == true)
            {
                CaptureLayoutToConfig();
                VariablePersistenceService.Capture(Workspace.CurrentSolution, Workspace); // 变量快照随方案落盘
                // 通信配置快照：通信设置里的增删改只更新管理器内存，保存时同步进方案
                Workspace.CurrentSolution.CommunicationConfigs =
                    new System.Collections.ObjectModel.ObservableCollection<CommunicationConfig>(
                        _communicationManager.GetAllConnections());
                var saveResult = await solutionService.SaveAsync(Workspace.CurrentSolution, dialog.FileName);
                if (saveResult.Success)
                {
                    Workspace.CurrentSolution.SolutionFilePath = dialog.FileName;
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
                case SystemAction.CommSettings:
                    ShowCommunicationSettings();
                    break;
            }
        }

        private void ShowCommunicationSettings()
        {
            if (Workspace.CurrentSolution == null)
            {
                Notifier.ShowWarning("请先打开一个解决方案");
                return;
            }

            var parameters = new DialogParameters();
            parameters.Add("Configs", Workspace.CurrentSolution.CommunicationConfigs);

            dialogService.ShowDialog("CommunicationSettingsView", parameters, result =>
            {
                // 对话框关闭后持久化连接配置到 communications.json（软件重启后 LoadConfigAsync 自动恢复）
                _ = _communicationManager.SaveConfigAsync();
            });
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
        /// 递归收集步骤（含 If/For 等容器内的嵌套子步骤）
        /// 引擎按 StepID 在 Blueprints 中查找步骤回写运行状态，
        /// 只填顶层会导致嵌套步骤永不显示运行状态
        /// </summary>
        private static void CollectStepsDeep(IEnumerable<StepModel> steps, ICollection<StepModel> into)
        {
            foreach (var step in steps)
            {
                into.Add(step);
                if (step is IContainerStep container && container.Children != null)
                {
                    foreach (var branch in container.Children)
                        CollectStepsDeep(branch.Steps, into);
                }
            }
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

            // 蓝图必须填充（含嵌套步骤）：否则编译出的会话运行时步骤状态无法回写 UI
            CollectStepsDeep(Workspace.CurrentFlow.Steps, newSession.Blueprints);

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

                    CollectStepsDeep(flow.Steps, newSession.Blueprints);

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

                    CollectStepsDeep(flow.Steps, session.Blueprints);

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

                    CollectStepsDeep(flow.Steps, session.Blueprints);

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
