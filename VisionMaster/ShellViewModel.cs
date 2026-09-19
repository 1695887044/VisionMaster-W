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
using System.Windows.Media;
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
        #region 运行状态（按钮互锁 + 状态栏显示的唯一事实来源）
        /// <summary>
        /// 运行控制状态。变化时同时刷新：①ExecutionCommand 的 CanExecute（按钮互锁）
        /// ②状态栏文字 RunStateText ③指示灯颜色 RunStateBrush。
        /// </summary>
        public MainRunState RunState
        {
            get { return field; }
            set
            {
                if (SetProperty(ref field, value))
                {
                    ExecutionCommand?.RaiseCanExecuteChanged();
                    // 方案菜单同步互锁：新建/打开/浏览运行中置灰（保存不受限）
                    SolutionCommand?.RaiseCanExecuteChanged();
                    RaisePropertyChanged(nameof(RunStateText));
                    RaisePropertyChanged(nameof(RunStateBrush));
                    // 向全应用广播运行状态（流程栏编辑锁等消费方经 GlobalEventBus 订阅）；
                    // setter 必在 UI 线程调用，总线同步派发，订阅者拿不到脏线程上下文
                    GlobalEventBus.Publish(value);
                }
            }
        }

        /// <summary>状态栏文字：未启动 / 启动中 / 循环运行中</summary>
        public string RunStateText => RunState switch
        {
            MainRunState.RunningOnce => "启动中",
            MainRunState.RunningContinuous => "循环运行中",
            _ => "未启动",
        };

        /// <summary>状态指示灯颜色：灰=未启动，橙=单次运行，绿=循环运行</summary>
        public Brush RunStateBrush => RunState switch
        {
            MainRunState.RunningOnce => Brushes.DarkOrange,
            MainRunState.RunningContinuous => Brushes.ForestGreen,
            _ => Brushes.Gray,
        };
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

        public bool IsScadaToolboxActive
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

            foreach (var contentId in new[] { "Panel_FlowListView", "Panel_ProcessView", "Panel_ToolView", "Panel_ScadaToolboxView" })
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
            IsScadaToolboxActive = LayoutHelper.IsPanelActive("Panel_ScadaToolboxView");
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
            SolutionCommand = new(ExecuteProjectAction, CanExecuteSolution);
            ExecutionCommand = new DelegateCommand<ExecutionAction?>(OnExecutionAction, CanExecuteExecution);
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

            // 连接配置的恢复与自动连接已由 CommunicationModule 在启动装配阶段完成
            // （必须早于通讯自检与 Shell 构造，见 CommunicationModule.Initialize），此处不再重复加载，
            // 否则会出现"两处都能加载配置"的真相源分裂。
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
        /// 方案操作互锁：保存永远可用；新建/打开/浏览列表都会丢弃当前运行中的会话，运行中一律禁用
        /// </summary>
        private bool CanExecuteSolution(SolutionAction? action)
            => action == SolutionAction.Save || RunState == MainRunState.NotStarted;

        /// <summary>
        /// 对于解决方案的操作
        /// </summary>
        /// <param name="action"></param>
        private async Task ExecuteProjectAction(SolutionAction? action)
        {
            // 防御纵深：菜单/工具栏正常已被 CanExecute 置灰，这里兜住快捷键等旁路调用
            if (!CanExecuteSolution(action))
            {
                Notifier.ShowWarning("流程运行中，禁止新建/打开/切换方案；如需切换请先点击“停止”");
                return;
            }

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

        /// <summary>
        /// 运行按钮互锁规则：未运行时可点"编译/启动/循环"；运行中只留"停止"可点。
        /// WPF 按钮在 CanExecute=false 时自动置灰，无需在 View 里写任何状态判断。
        /// </summary>
        private bool CanExecuteExecution(ExecutionAction? action)
        {
            return action == ExecutionAction.Stop
                ? RunState != MainRunState.NotStarted
                : RunState == MainRunState.NotStarted;
        }

        /// <summary>
        /// 等待本轮启动的所有会话任务结束（单次跑完 / 循环被取消），再把状态复位为"未启动"。
        /// 从 UI 线程 await，续体自动回到 UI 线程，可安全赋值绑定属性。
        /// </summary>
        private async Task TrackRunCompletionAsync(List<Task> tasks)
        {
            try
            {
                await Task.WhenAll(tasks);
            }
            catch
            {
                // 引擎内部异常/取消已由各自的错误提示与日志反映，这里只负责状态复位
            }
            RunState = MainRunState.NotStarted;
        }

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
                case SystemAction.RuntimeWindowSettings:
                    // 软件级偏好（依附主窗口 / 独立窗口），存 AppConfig.json。
                    // 弹窗自己负责读当前值、写盘，这里不传参数、也不看返回值——
                    // 运行窗口的形态是宿主在 Start 时现读配置决定的（见 ScadaRuntimeHost），
                    // 所以"改完生效"不需要在这里做任何同步动作。
                    dialogService.ShowDialog("ScadaRunWindowSettingsView");
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
                    Notifier.ShowError(item.Message);
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

            // 进入运行态：立即互锁"编译/启动/循环"，放开"停止"
            RunState = MainRunState.RunningOnce;
            int runCount = 0;
            var tasks = new List<Task>();

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
                    tasks.Add(flowEngine.RunSessionOnceAsync(session));
                    runCount++;
                }
            }

            if (runCount > 0)
            {
                Notifier.ShowSuccess($"已启动 {runCount} 个流程的单次运行");
                // 所有会话跑完后自动回到"未启动"，按钮互锁随之解除
                _ = TrackRunCompletionAsync(tasks);
            }
            else
            {
                // 一个都没启动成功：状态立刻回退，避免按钮被永久锁死
                RunState = MainRunState.NotStarted;
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

            // 进入运行态：立即互锁"编译/启动/循环"，放开"停止"
            RunState = MainRunState.RunningContinuous;
            int runCount = 0;
            var tasks = new List<Task>();

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
                    // 循环会话的任务只有被"停止"取消后才会结束，因此这里绝不能 await 它
                    tasks.Add(flowEngine.RunSessionAsync(session));
                    runCount++;
                }
            }

            if (runCount > 0)
            {
                Notifier.ShowSuccess($"已启动 {runCount} 个流程的连续运行");
                // 点"停止"→ 引擎取消令牌 → 循环任务结束 → 状态自动回到"未启动"
                _ = TrackRunCompletionAsync(tasks);
            }
            else
            {
                // 一个都没启动成功：状态立刻回退，避免按钮被永久锁死
                RunState = MainRunState.NotStarted;
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
