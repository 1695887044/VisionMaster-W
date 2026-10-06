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
        /// <summary>
        /// 运行态权限会话（S12）。注入的是<b>具体类型</b>而不是 <c>IScadaAccessPolicy</c>：
        /// 状态栏要读 <see cref="ScadaAccessPolicy.LoginStateText"/>、超时要订阅
        /// <see cref="ScadaAccessPolicy.AutoLoggedOut"/>，这两个都不在接口上
        /// （接口只回答"够不够格"，见 IScadaAccessPolicy 的契约③）。
        /// 两者在 App 里注册的是<b>同一个单例</b>，所以这里读到的一定是权限判定用的那一份。
        /// </summary>
        private readonly ScadaAccessPolicy _accessPolicy;
        private readonly IRuntimeManager _runtimeManager;

        /// <summary>
        /// 具体类型注入（容器把具体类型与 IFlowEngine 映射到同一单例，先例见 FlowEngineModule 注册注释）：
        /// SessionStateChanged 事件不在 IFlowEngine 契约上，必须拿具体类型才能订阅。
        /// </summary>
        private readonly FlowEngineService _flowEngineService;

        // ================= DWV 第 1 期：调试暂停 / 命中窗 =================

        /// <summary>暂停前的运行态（进入 Paused 时记住，最后一个暂停会话恢复时还原）</summary>
        private MainRunState _prePauseRunState = MainRunState.NotStarted;

        /// <summary>「本次运行不再提示」：断点 / 单步照常停（引擎侧不受影响），只是不再弹命中窗</summary>
        private bool _suppressHitThisRun;

        /// <summary>命中窗（单实例，懒建；点 X 只隐藏，下次命中再现）</summary>
        private DebugHitWindow _hitWindow;

        /// <summary>命中窗的展示 VM</summary>
        private DebugHitViewModel _debugHitVm;

        /// <summary>当前命中步骤（「打开模块参数」的目标；未定位到时为 null）</summary>
        private StepModel _hitTargetStep;

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
        #region 登录会话（状态栏显示）
        /// <summary>
        /// 状态栏要显示的那份会话。<b>直接把它暴露出去，而不是在这里抄一份 <c>LoginStateText</c></b>：
        /// 抄一份就要再写一遍"会话变了 → 通知我这一份也跟着变"的转发代码，
        /// 而转发一旦漏掉某个属性，现场表现是"登录成功了状态栏还写着未登录"——很难往"少写了一行通知"上想。
        /// 这里让 WPF 顺着 <c>AccessPolicy.LoginStateText</c> 这条路径直接绑到单例上，
        /// 单例自己是 <c>BindableBase</c>，变更通知天然到位。
        ///
        /// 与状态栏里"当前方案"绑 <c>Workspace.CurrentSolution</c> 是同一个手法。
        /// 视图拿到的是一份可写引用，但状态栏只读它——权限的写入口（登录/登出）刻意留在弹窗里。
        /// </summary>
        public ScadaAccessPolicy AccessPolicy => _accessPolicy;
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
                // 捕获旧值：命中窗"新一轮开始才复位抑制"要区分
                // NotStarted→Running*（新运行）与 Paused→Running*（暂停恢复，抑制保留到本轮结束）
                var previous = field;
                if (SetProperty(ref field, value))
                {
                    ExecutionCommand?.RaiseCanExecuteChanged();
                    // 方案菜单同步互锁：新建/打开/浏览运行中置灰（保存不受限）
                    SolutionCommand?.RaiseCanExecuteChanged();
                    RaisePropertyChanged(nameof(RunStateText));
                    RaisePropertyChanged(nameof(RunStateBrush));

                    // DWV 第 1 期：命中窗与"本次运行不再提示"的生命周期挂在运行态转换上——
                    //  · 回到"未启动"：关窗 + 复位抑制（上一次运行的"不再提示"不跨运行泄漏）；
                    //  · 新一轮运行开始（从未启动进入 Running*）：同样复位 —— 抑制只作用于"本次运行"。
                    //    注意：暂停恢复（Paused → Running*）不算新一轮、不复位，
                    //    否则用户勾的"不再提示"在点继续后就失效（口径：断点/单步照常停，只是不弹窗）。
                    if (value == MainRunState.NotStarted)
                    {
                        _prePauseRunState = MainRunState.NotStarted;
                        ResetHitSuppression();
                        _hitWindow?.Hide();
                    }
                    else if (previous == MainRunState.NotStarted
                             && (value == MainRunState.RunningOnce || value == MainRunState.RunningContinuous))
                    {
                        ResetHitSuppression();
                    }

                    // 向全应用广播运行状态（流程栏编辑锁等消费方经 GlobalEventBus 订阅）；
                    // setter 必在 UI 线程调用，总线同步派发，订阅者拿不到脏线程上下文
                    GlobalEventBus.Publish(value);
                }
            }
        }

        /// <summary>状态栏文字：未启动 / 启动中 / 循环运行中 / 已暂停</summary>
        public string RunStateText => RunState switch
        {
            MainRunState.RunningOnce => "启动中",
            MainRunState.RunningContinuous => "循环运行中",
            MainRunState.Paused => "已暂停",
            _ => "未启动",
        };

        /// <summary>暂停态灯色 #409EFF（评审建议色；静态复用，避免每次取属性都新建画刷）</summary>
        private static readonly Brush PausedBrush = new SolidColorBrush(Color.FromRgb(0x40, 0x9E, 0xFF));

        /// <summary>状态指示灯颜色：灰=未启动，橙=单次运行，绿=循环运行，蓝=已暂停</summary>
        public Brush RunStateBrush => RunState switch
        {
            MainRunState.RunningOnce => Brushes.DarkOrange,
            MainRunState.RunningContinuous => Brushes.ForestGreen,
            MainRunState.Paused => PausedBrush,
            _ => Brushes.Gray,
        };
        #endregion
        #region Commands
        public AsyncDelegateCommand<SolutionAction?> SolutionCommand { get; }
        public DelegateCommand<ExecutionAction?> ExecutionCommand { get; }

        /// <summary>F9：切换当前步骤断点（无选中则 no-op；与流程栏「切换断点」同口径）</summary>
        public DelegateCommand ToggleBreakpointOnCurrentStepCommand { get; }
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

        public bool IsScadaLayerActive
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

            foreach (var contentId in new[] { "Panel_FlowListView", "Panel_ProcessView", "Panel_ToolView", "Panel_ScadaToolboxView", "Panel_ScadaLayerView" })
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
            IsScadaLayerActive = LayoutHelper.IsPanelActive("Panel_ScadaLayerView");
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
            NetworkVariableBridge variableBridge,
            ScadaAccessPolicy accessPolicy,
            FlowEngineService flowEngineService
        )
        {
            StartBackgroundMonitoring();
            SolutionCommand = new(ExecuteProjectAction, CanExecuteSolution);
            ExecutionCommand = new DelegateCommand<ExecutionAction?>(OnExecutionAction, CanExecuteExecution);
            ToggleBreakpointOnCurrentStepCommand = new DelegateCommand(ToggleBreakpointOnCurrentStep);
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

            // DWV 第 1 期：调试会话状态订阅（生命周期 = Shell 全程，无需退订）。
            // 调试态由各运行入口（RunAllEnabledOnce/Continuous）以 debugSession: true 参数交给
            // 引擎置位（抢锁后），这里负责把"暂停 / 恢复"镜像到 RunState、按暂停原因驱动命中窗。
            this._flowEngineService = flowEngineService;
            _flowEngineService.SessionStateChanged += OnSessionStateChanged;

            // 空闲超时登出要"说一声"。挂在这里（而不是弹窗里）是因为主窗口是唯一
            // 从头到尾开着的那一个——超时那一刻弹窗多半关着（见 ScadaLoginViewModel 注释①）。
            // Shell 是应用级单例，订阅一次即终身有效，无需解订阅。
            this._accessPolicy = accessPolicy;
            _accessPolicy.AutoLoggedOut += OnAutoLoggedOut;

            // Core 层持久化服务的通信管理器引用（保存/加载方案时需要按协议重建地址）
            Services.ServiceLocator.CommunicationManager = communicationManager;

            // 连接配置的恢复与自动连接已由 CommunicationModule 在启动装配阶段完成
            // （必须早于通讯自检与 Shell 构造，见 CommunicationModule.Initialize），此处不再重复加载，
            // 否则会出现"两处都能加载配置"的真相源分裂。
        }

        /// <summary>
        /// 切换画布布局。
        ///
        /// 每一格都是"上行图 + 下行走马灯"的完整画布，格号 ↔ 插件的显示窗口号。
        /// 参数认不出来（菜单改过、老快捷键）时回落单画面 —— 比"点了没反应"好排查。
        /// </summary>
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
                _ => eViewMode.One,
            };

            GlobalEventBus.Publish<ImageCanvasChangeEvent>(new ImageCanvasChangeEvent { ViewMode = viewMode });
        }

        /// <summary>
        /// 方案操作互锁：保存永远可用；新建/打开/浏览列表都会丢弃当前运行中的会话，运行中一律禁用。
        /// 判据除 MainRunState 外还要看"是否真有流程在跑"：单流程运行 / HTTP 触发都不写 MainRunState，
        /// 只认它会让"边跑边换方案"从这两个入口溜进来（会话被 ClearAll 丢掉 = 静默拆台）。
        /// </summary>
        private bool CanExecuteSolution(SolutionAction? action)
            => action == SolutionAction.Save
               || (RunState == MainRunState.NotStarted && !AnyFlowRunning());

        /// <summary>
        /// 是否**有任意流程**处在运行或暂停（会话级镜像；涵盖界面运行 / 单流程运行 / HTTP 触发）。
        /// 编译类与方案切换类操作的前置判据：RegisterSession / ClearAll 都会先停掉运行中的会话，
        /// 而 MainRunState 只反映"界面发起的整体运行"，单流程与 HTTP 触发不在其中。
        /// </summary>
        private bool AnyFlowRunning()
        {
            var flows = Workspace?.CurrentSolution?.Flows;
            if (flows == null) return false;

            foreach (var flow in flows)
            {
                if (flow != null && flow.RunState != FlowRunState.Stopped)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 对于解决方案的操作
        /// </summary>
        /// <param name="action"></param>
        private async Task ExecuteProjectAction(SolutionAction? action)
        {
            // 防御纵深：菜单/工具栏正常已被 CanExecute 置灰，这里兜住快捷键等旁路调用
            if (!CanExecuteSolution(action))
            {
                Notifier.ShowWarning("流程运行中，禁止新建/打开/切换方案或修改报警配置；如需操作请先点击“停止”");
                return;
            }

            switch (action)
            {
                case SolutionAction.Create:
                    // 传进去的是**草稿**：PropertyGrid 是就地编辑（取消不回滚），
                    // 所以必须"先 new 一份、确认后才落库"，不能直接把当前方案交出去改。
                    var newSolution = new SolutionModel();
                    var isConfirmed = await EasyDialog.ShowPropertyGridAsync(
                        "创建新解决方案",
                        newSolution
                    );
                    if (!isConfirmed) break;

                    // 弹窗本身不做校验（[Required] 只是数据注解，PropertyGrid 不读它），
                    // 所以"空名 / 重名"必须在这里拦：确认即创建，拦不住就会建出一个没有名字的方案。
                    newSolution.SolutionName = (newSolution.SolutionName ?? string.Empty).Trim();
                    if (newSolution.SolutionName.Length == 0)
                    {
                        Notifier.ShowWarning("方案名称不能为空，请重新创建");
                        break;
                    }

                    if (solutionService.SolutionModels.Any(s =>
                            s != newSolution &&
                            string.Equals(s.SolutionName, newSolution.SolutionName, StringComparison.OrdinalIgnoreCase)))
                    {
                        Notifier.ShowWarning($"已存在同名方案「{newSolution.SolutionName}」，请换一个名称");
                        break;
                    }

                    try
                    {
                        var createResult = solutionService.Create(newSolution);
                        if (!createResult.Success)
                        {
                            Notifier.ShowError($"创建方案失败：{createResult.Message}");
                            break;
                        }

                        Workspace.SwitchSolution(newSolution);
                        Notifier.ShowSuccess($"方案 [{newSolution.SolutionName}] 已创建");
                    }
                    catch (Exception ex)
                    {
                        // AsyncDelegateCommand 会吞掉异常：不接住的话失败在界面上完全无声
                        Notifier.ShowError($"创建方案异常：{ex.Message}");
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
                case SolutionAction.AlarmConfig:
                    // 报警配置是方案级内容（跟着 .vms 走、进撤销栈），所以与新建/打开同级走这里的互锁：
                    // 运行中改报警定义会让"正在判的那批条件"中途换一套，属于该被拦下的动作。
                    dialogService.ShowDialog("ScadaAlarmConfigDialogView");
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
        /// 运行按钮互锁矩阵（DWV 第 1 期扩了"已暂停"一行）：
        ///  · 未启动：编译 / 运行一次 / 循环运行；
        ///  · 运行中：停止 / 暂停；
        ///  · 已暂停：停止 / 继续；单步仅当"恰好一个会话暂停"（多会话时步谁说不清，宁缺毋滥）；
        ///  · 其余（暂停中点编译 / 运行等）一律灰。
        /// WPF 按钮在 CanExecute=false 时自动置灰，无需在 View 里写任何状态判断。
        /// </summary>
        private bool CanExecuteExecution(ExecutionAction? action)
        {
            switch (action)
            {
                case ExecutionAction.Compile:
                case ExecutionAction.RunOnce:
                case ExecutionAction.RunContinuous:
                    return RunState == MainRunState.NotStarted;

                case ExecutionAction.Stop:
                    return RunState != MainRunState.NotStarted;

                case ExecutionAction.Pause:
                    return RunState == MainRunState.RunningOnce || RunState == MainRunState.RunningContinuous;

                case ExecutionAction.Resume:
                    return RunState == MainRunState.Paused;

                case ExecutionAction.StepOnce:
                    // 快照现取：暂停会话数随会话事件变化，缓存会过期
                    return RunState == MainRunState.Paused && PausedSessions().Count == 1;

                default:
                    return false;
            }
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
                case ExecutionAction.Pause:
                    PauseAllDebugSessions();
                    break;
                case ExecutionAction.Resume:
                    ResumeAllPausedSessions();
                    break;
                case ExecutionAction.StepOnce:
                    StepSinglePausedSession();
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
                case SystemAction.UserLogin:
                    // 登录、登出、改密、用户管理四个入口都收在这一个弹窗里：
                    // 它们共用同一份会话状态，拆成四个菜单项只会让"我现在是谁"这件事更难看明白。
                    dialogService.ShowDialog("ScadaLoginView");
                    break;
                case SystemAction.CameraSettings:
                    ShowCameraSettings();
                    break;
                case SystemAction.MotionBoard:
                    ShowMotionBoard();
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
                case SystemAction.SystemParameters:
                    // 空闲自动登出时长、审计 / 报警历史保留天数（软件级，存 AppConfig.json）。
                    // 同运行窗口设置：弹窗自己读当前值、自己写盘，这里不传参数也不看返回值——
                    // 这三项的使用方都是"每次现读配置"，所以"改完生效"不需要在这里同步任何东西。
                    dialogService.ShowDialog("ScadaSystemParametersView");
                    break;
                case SystemAction.VariableEvents:
                    // 按变量配置值驱动的规则（更改数值 / 值为真 / 值为假 / 上限 / 下限）。
                    // 与上面两个不同，它改的是方案内容（落 .vms、进撤销栈），所以：
                    // ① 弹窗自己从当前方案里取"变量事件表"，这里不传参数（传了反而要在这里判方案有没有打开）；
                    // ② 它需要"有打开的方案"才有内容——没有方案时弹窗内是空清单并给出提示，
                    //    而不是在这里拦一道"请先打开方案"（拦一道的结果是菜单点下去毫无反应）。
                    dialogService.ShowDialog("ScadaVariableEventDialogView");
                    break;
            }
        }

        /// <summary>
        /// 空闲超时被自动登出 → 提示一句。
        ///
        /// <b>不吭声地退出登录</b>在现场会被理解成"软件坏了"：操作员回来点按钮，
        /// 发现没反应（权限已经掉回操作员），反复重登、反复被踢，最后报故障。
        /// 说清"是因为多久没操作"，他才知道是设计如此、动一下鼠标就没事。
        ///
        /// 时序：本方法由 <c>ScadaAccessPolicy</c> 在 <b>已登出之后</b> 调用
        /// （先 Logout 再抛事件，见 OnIdleTick），所以此刻读到的用户名是"未登录"，
        /// 被踢的那位只能从参数里拿——这也正是这个事件带参数的原因。
        ///
        /// 线程：来自 UI 线程的 DispatcherTimer，直接弹提示安全。
        /// </summary>
        private void OnAutoLoggedOut(string userName)
        {
            var minutes = _accessPolicy.IdleTimeout.TotalMinutes;
            Notifier.ShowWarning(
                $"账号「{userName}」已因 {minutes:0.#} 分钟无操作自动退出登录，权限回到「操作员」。如需继续，请重新登录。");
        }

        /// <summary>
        /// 打开「相机设置」。
        ///
        /// 相机的**配置**直接挂在当前方案上（<see cref="SolutionModel.CameraConfigs"/>），
        /// 运行态设备由 CameraProvider 按方案对齐，所以：
        ///   ① 没有方案时无处可写，必须先拦一道并说明原因——否则点下去只会得到一个
        ///      "改了也不知道存哪"的界面；
        ///   ② 不给弹窗传任何参数：相机清单与运行态一律由 CameraProvider 从"当前方案"现取。
        ///      传一份快照反而会出现"界面里那份和方案里那份不是同一个对象"，改一份丢一份。
        /// </summary>
        private void ShowCameraSettings()
        {
            if (Workspace.CurrentSolution == null)
            {
                Notifier.ShowWarning("请先打开一个解决方案");
                return;
            }

            dialogService.ShowDialog("CameraSettingsView");
        }

        /// <summary>
        /// 打开运动板卡（合并窗口：卡设置 / 手动调试 / 点位列表 / 电子凸轮）。
        /// 取代原来两个独立入口（运动设置 / 手动调试弹窗已退役）——
        /// 参数、调试、点位在一个窗口里切换，现场不用来回找菜单。
        /// 与 <see cref="ShowCameraSettings"/> 同口径：没有方案就拦下来提示（运动卡是方案级资源，
        /// 没有方案时无处可存），且**不传任何参数** —— 卡片清单与运行态一律由 MotionProvider
        /// 从"当前方案"现取；传一份快照反而会出现"界面里那份和方案里那份不是同一个对象"，
        /// 改一份丢一份。
        /// </summary>
        private void ShowMotionBoard()
        {
            if (Workspace.CurrentSolution == null)
            {
                Notifier.ShowWarning("请先打开一个解决方案");
                return;
            }

            dialogService.ShowDialog("MotionBoardView");
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
            newSession.AddBlueprintsDeep(Workspace.CurrentFlow.Steps);

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

            // 编译 = 重建会话（RegisterSession 会先停掉同名运行会话，最长等 3s）：
            // 只要**有任意流程在跑**就先拦下，否则"编译完成"的提示背后是一条流程被静默杀掉
            if (AnyFlowRunning())
            {
                Notifier.ShowWarning("有流程正在运行，禁止编译（编译会停掉同名运行会话）；如需编译请先停止");
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

                    newSession.AddBlueprintsDeep(flow.Steps);

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

                    session.AddBlueprintsDeep(flow.Steps);

                    _runtimeManager.RegisterSession(session);
                }

                if (session != null && !session.IsRunning)
                {
                    // DWV 第 1 期：界面发起的运行 = 调试会话（装调试门，支持断点 / 单步 / 暂停）。
                    // 调试态不在这里预置：由引擎抢到会话锁后按此参数置位——预置的话，本次若被 HTTP
                    // 抢到锁而拒绝，true 会残留给那次非界面运行（P2）；per-run 清回 false 在引擎收尾
                    tasks.Add(flowEngine.RunSessionOnceAsync(session, debugSession: true));
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

                    session.AddBlueprintsDeep(flow.Steps);

                    _runtimeManager.RegisterSession(session);
                }

                if (session != null && !session.IsRunning)
                {
                    // DWV 第 1 期：界面发起的运行 = 调试会话（装调试门，支持断点 / 单步 / 暂停）。
                    // 调试态不在这里预置（理由同单次运行入口：抢锁后才置位，防 P2 泄漏）
                    // 循环会话的任务只有被"停止"取消后才会结束，因此这里绝不能 await 它
                    tasks.Add(flowEngine.RunSessionAsync(session, debugSession: true));
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

        // ================= DWV 第 1 期：调试暂停 / 继续 / 单步 / 命中窗 =================

        /// <summary>
        /// 暂停所有运行中的调试会话（「暂停 / 继续 = 全部运行中会话」，与 StopAll 同口径）。
        ///
        /// 【为什么只暂停 DebugEnabled 的会话】调试门只在调试会话上装（HTTP 触发 / 试运行从不置位
        /// DebugEnabled），非调试会话没有可等待的门；按钮的语义就是"暂停本次界面发起的运行"，
        /// 跳过非调试会话是刻意的（别去卡外部触发的产线链路）。
        /// 【为什么走快照】ActiveSessions 是运行期可变集合（HTTP 线程会增删），裸枚举会撞"集合已修改"。
        /// </summary>
        private void PauseAllDebugSessions()
        {
            foreach (var session in _runtimeManager.SnapshotSessions())
            {
                if (session.State == SessionState.Running && session.DebugEnabled)
                    flowEngine.PauseSession(session);
            }
        }

        /// <summary>继续：对所有"已暂停"的会话放行（断点 / 单步 / 用户暂停三种暂停统一适用，评审结论 9）</summary>
        private void ResumeAllPausedSessions()
        {
            foreach (var session in _runtimeManager.SnapshotSessions())
            {
                if (session.State == SessionState.Paused)
                    flowEngine.ResumeSession(session);
            }
        }

        /// <summary>单步：仅当"恰好一个会话暂停"时可用（多会话同时暂停时步哪个说不清，宁缺毋滥）</summary>
        private void StepSinglePausedSession()
        {
            var paused = PausedSessions();
            if (paused.Count == 1)
                flowEngine.StepSession(paused[0]);
        }

        /// <summary>暂停中的会话快照（"已无暂停会话" / "恰一个暂停"两处判定共用的唯一来源，防口径漂移）</summary>
        private List<FlowSession> PausedSessions()
            => _runtimeManager.SnapshotSessions().Where(s => s.State == SessionState.Paused).ToList();

        /// <summary>复位命中窗抑制（同步命中窗 VM 的勾选态；VM 回调仅回写同一字段，幂等）</summary>
        private void ResetHitSuppression()
        {
            _suppressHitThisRun = false;
            if (_debugHitVm != null)
                _debugHitVm.SuppressThisRun = false;
        }

        /// <summary>
        /// 会话状态变更（DWV 第 1 期）。
        ///
        /// 事件可能来自执行线程（调试门命中 → 引擎在运行线程上 NotifyStateChanged），
        /// 先经 UiDispatcher 切回 UI 线程，再动 RunState / 命中窗。
        ///
        /// 两件事：
        ///  · 运行态镜像：有会话进入 Paused → 整机切"已暂停"（记住暂停前的运行态）；
        ///    最后一个暂停会话回到 Running 且"已无暂停会话" → 还原暂停前的运行态。
        ///  · 命中窗：仅"断点命中 / 单步停住"弹窗（用户手动暂停不弹）；被"本次运行不再提示"抑制时不弹；
        ///    暂停会话清零（继续 / 停止都会走到）自动隐藏。
        /// </summary>
        private void OnSessionStateChanged(object sender, SessionStateChangedEventArgs e)
            => UiDispatcher.Post(() => HandleSessionStateChanged(e));

        private void HandleSessionStateChanged(SessionStateChangedEventArgs e)
        {
            if (e.NewState == SessionState.Paused)
            {
                if (RunState != MainRunState.Paused)
                {
                    _prePauseRunState = RunState;
                    RunState = MainRunState.Paused;
                }

                // 命中窗按暂停原因分流：只有"断点 / 单步"才弹（用户手动暂停不弹）；
                // 用 SessionId 反查会话拿 PauseReason（通知只捎带 Id，不带会话本体）
                var session = _runtimeManager.GetSessionById(e.SessionId);
                if (session != null
                    && !_suppressHitThisRun
                    && (session.PauseReason == SessionPauseReason.Breakpoint
                        || session.PauseReason == SessionPauseReason.Step))
                {
                    ShowDebugHit(session);
                }
            }
            else if (e.NewState == SessionState.Running
                     && RunState == MainRunState.Paused
                     && PausedSessions().Count == 0)
            {
                // 最后一个暂停会话已放行：还原暂停前的运行态（正常路径下必为 RunningOnce / Continuous）
                RunState = _prePauseRunState == MainRunState.NotStarted
                    ? MainRunState.RunningContinuous
                    : _prePauseRunState;
            }

            // 暂停会话清零 → 命中窗自动隐藏（继续 / 停止后都会走到这里）
            if (_hitWindow != null && _hitWindow.IsVisible && PausedSessions().Count == 0)
                _hitWindow.Hide();

            // 流程级运行状态镜像（FlowModel.RunState）：方案里 4 处"运行中禁止改配置"的守卫
            // 读的都是它，此前没人赋值 = 守卫形同虚设（本次收口，含 HTTP / 手动运行等非界面触发）
            SyncFlowRunState(e);

            // 会话状态一变，"单步"可用性（暂停会话数）可能就变了：让按钮重新查询；
            // 方案的 New/Open 互锁同时依赖"是否真有流程在跑"，一并重查
            ExecutionCommand.RaiseCanExecuteChanged();
            SolutionCommand?.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// 会话状态 → 流程级运行状态（<see cref="FlowModel.RunState"/>）的单向镜像。
        ///
        /// 【为什么按名字找流程而不是按会话】FlowModel 跟随 .vms 活、FlowSession 随编译/停止增删，
        /// 两者唯一的稳定纽带就是流程名（与引擎补编译、命中窗定位同口径：Ordinal）。
        /// 找不到流程（方案已切换 / 流程已删除）就静默返回——镜像只是守卫与列表显示的输入，
        /// 不该反过来报错打断执行。
        /// </summary>
        private void SyncFlowRunState(SessionStateChangedEventArgs e)
        {
            var flows = Workspace.CurrentSolution?.Flows;
            if (flows == null) return;

            foreach (var flow in flows)
            {
                if (flow == null || !string.Equals(flow.FlowName, e.FlowName, StringComparison.Ordinal))
                    continue;

                bool wasRunning = flow.RunState == FlowRunState.Running;

                flow.RunState = e.NewState switch
                {
                    SessionState.Running => FlowRunState.Running,
                    SessionState.Paused => FlowRunState.Paused,
                    // Stopped / Faulted（以及将来新增的终止态）一律回"停止"：
                    // 列表上挂着"运行中"而实际没跑，比不显示状态危害大得多
                    _ => FlowRunState.Stopped,
                };

                // 开始时间只在"由非运行进入运行"那一刻刷新：连续运行每轮都会发 Running 事件，
                // 每轮都刷新会让"已运行时长"永远从 0 开始（它是给人看这一轮跑了多久的）
                if (e.NewState == SessionState.Running && !wasRunning)
                    flow.StartRunTime = DateTime.Now;

                return;
            }
        }

        /// <summary>
        /// 弹出 / 刷新命中窗（单实例）。窗口仍以 Shell 命令为唯一执行入口
        /// （继续 / 单步 / 停止直接绑 ExecutionCommand；「打开模块参数」走共享帮助类）。
        /// </summary>
        private void ShowDebugHit(FlowSession session)
        {
            _hitTargetStep = FindDebugStoppedStep(session);

            EnsureHitWindow();

            _debugHitVm.Update(
                session.PauseReason == SessionPauseReason.Step ? "单步暂停" : "断点命中",
                session.FlowName,
                _hitTargetStep?.StepName ?? "（未能定位命中步骤）",
                _hitTargetStep != null);

            if (!_hitWindow.IsVisible)
                _hitWindow.Show();
        }

        /// <summary>懒建命中窗（首命中才建，避免 Shell 构造期就碰 Window 对象）</summary>
        private void EnsureHitWindow()
        {
            if (_hitWindow != null) return;

            _debugHitVm = new DebugHitViewModel(
                ExecutionCommand,
                openParameters: OpenHitStepParameters,
                suppressChanged: v => _suppressHitThisRun = v);

            _hitWindow = new DebugHitWindow { DataContext = _debugHitVm };

            // 挂主窗口为属主：随主窗口最小化 / 不退到主窗口后面；不设 Topmost，避免压住其它应用
            if (Application.Current?.MainWindow is { } main && !ReferenceEquals(main, _hitWindow))
                _hitWindow.Owner = main;
        }

        /// <summary>命中窗「打开模块参数」：复用流程栏同一帮助类（口径只有一份）</summary>
        private void OpenHitStepParameters()
        {
            if (_hitTargetStep == null) return;
            StepParameterDialog.Open(_hitTargetStep, dialogService);
        }

        /// <summary>
        /// 在方案里按流程名找到命中流程，再递归找 IsDebugStopped==true 的步骤（含容器分支内嵌套步骤）。
        /// 用 IsDebugStopped 而不是按 PauseReason 猜步骤：它是调试门在"停住期间"置的权威停点标记
        /// （放行 / 收尾时清除），与引擎停点零漂移。
        /// </summary>
        private StepModel FindDebugStoppedStep(FlowSession session)
        {
            var flows = Workspace.CurrentSolution?.Flows;
            if (flows == null) return null;

            foreach (var flow in flows)
            {
                if (flow == null || flow.FlowName != session.FlowName) continue;
                return FindDebugStoppedStep(flow.Steps);
            }

            return null;
        }

        private static StepModel FindDebugStoppedStep(IEnumerable<StepModel> steps)
        {
            if (steps == null) return null;

            foreach (var step in steps)
            {
                if (step == null) continue;
                if (step.IsDebugStopped) return step;

                if (step is IContainerStep container && container.Children != null)
                {
                    foreach (var branch in container.Children)
                    {
                        var hit = FindDebugStoppedStep(branch?.Steps);
                        if (hit != null) return hit;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// F9：取反当前选中步骤的断点标记。无选中 no-op（评审：F9 纳入第 1 期）。
        /// 纯运行期标记：运行 / 暂停中同样可用（与流程栏「切换断点」的运行锁豁免同一理由）。
        /// </summary>
        private void ToggleBreakpointOnCurrentStep()
        {
            var step = Workspace.CurrentStep;
            if (step == null) return;

            step.IsBreakpoint = !step.IsBreakpoint;
        }

        #endregion
    }
}
