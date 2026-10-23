using Core.Interfaces;
using AvalonDock.Layout;
using System.Collections.ObjectModel;
using HslCommunication.Profinet.Siemens;
using NLog;
using Prism.Common;
using Prism.Dialogs;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
    /// <summary>
    /// Shell 工作模式：调试 / 运行 / 组态 / 自由。
    /// 模式决定文档标签、左右面板、底部面板与状态栏的上下文，不只是“布局预设”。
    /// </summary>
    public enum ShellMode
    {
        Debug,
        Run,
        Config,
        Free,
    }

    /// <summary>
    /// 命令面板的一行。动作用委托携带，避免为每种入口再写一个命令类型；
    /// 面板每次打开时重建，流程/视图变化不会留下过期引用。
    /// </summary>
    public sealed class CommandPaletteItem
    {
        public string Title { get; init; } = string.Empty;
        public string Subtitle { get; init; } = string.Empty;
        public string Glyph { get; init; } = "\uf0c9";
        public Action? Invoke { get; init; }
    }

    public class ShellViewModel : BindableBase
    {
        private readonly FlowCompiler _flowCompiler;
        private readonly IFlowEngine flowEngine;
        private readonly AdvancedCommunicationManager _communicationManager;
        private readonly NetworkVariableBridge _variableBridge;

        /// <summary>通讯配置的方案级装配（打开/新建/保存方案三处共用）。</summary>
        public Services.CommunicationSolutionBinder CommunicationBinder { get; }

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

        /// <summary>标题栏配方 chip 的展示与入口（切换/编辑都进管理器）。</summary>
        public RecipeViewModel Recipe { get; }

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

        // 生产 KPI 的显示位先留给状态栏；数据源（节拍 / OK / NG / 告警）尚未接入时
        // 明确显示占位符，不用 0 冒充真实计数。接入后只需在这四个属性里换源。
        public string CycleTimeText => "—";
        public string OkCountText => "—";
        public string NgCountText => "—";
        public string AlarmCountText => "—";
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
        /// <summary>当前工作模式（调试 / 运行 / 组态 / 自由）</summary>
        public ShellMode CurrentMode
        {
            get { return field; }
            private set { SetProperty(ref field, value); }
        }

        /// <summary>切换工作模式；参数为 Debug / Run / Config / Free。</summary>
        public DelegateCommand<string> SetShellModeCommand { get; }

        public AsyncDelegateCommand<SolutionAction?> SolutionCommand { get; }
        public DelegateCommand<ExecutionAction?> ExecutionCommand { get; }

        /// <summary>F9：切换当前步骤断点（无选中则 no-op；与流程栏「切换断点」同口径）</summary>
        public DelegateCommand ToggleBreakpointOnCurrentStepCommand { get; }
        public DelegateCommand<SystemAction?> SystemCommand { get; }

        public DelegateCommand<string> SwitchCanvasCommand {  get; }

        public DelegateCommand<string> TogglePanelCommand { get; }

        /// <summary>命令面板（Ctrl+K）：打开 / 关闭 / 执行当前项。</summary>
        public DelegateCommand OpenCommandPaletteCommand { get; }
        public DelegateCommand CloseCommandPaletteCommand { get; }
        public DelegateCommand ExecuteCommandPaletteItemCommand { get; }
        public DelegateCommand ToggleConsoleCommand { get; }

        private readonly List<CommandPaletteItem> _allPaletteItems = new();
        private CommandPaletteItem? _selectedCommandPaletteItem;

        public ObservableCollection<CommandPaletteItem> CommandPaletteItems { get; } = new();

        public bool IsCommandPaletteOpen
        {
            get => field;
            private set
            {
                if (SetProperty(ref field, value))
                    RaisePropertyChanged(nameof(CommandPaletteEmptyVisibility));
            }
        }

        public string CommandPaletteQuery
        {
            get => field;
            set
            {
                if (SetProperty(ref field, value))
                    FilterCommandPalette();
            }
        }

        public CommandPaletteItem? SelectedCommandPaletteItem
        {
            get => _selectedCommandPaletteItem;
            set => SetProperty(ref _selectedCommandPaletteItem, value);
        }

        public Visibility CommandPaletteEmptyVisibility
            => IsCommandPaletteOpen && CommandPaletteItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        #endregion

        #region 活动栏（Activity Bar）
        /// <summary>
        /// 活动栏按钮高亮状态跟随面板 IsActive（OneWay 绑定，由监听器驱动）。
        /// 注意：左侧活动栏已下线、右侧活动栏也只留「工具箱」一个开关，所以当前
        /// <b>只有 <see cref="IsToolboxActive"/> 被 Shell.xaml 绑定</b>；其余四个仍由
        /// <see cref="RefreshActivityBar"/> 照常维护（判据已在手），将来重新加回开关即可直接用。
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
        /// 当前是否处于「组态界面设计」场景（= 中央文档区里选中的是「组态画面」标签）。
        /// 判据用文档的 IsSelected 而非 IsActive，理由见 <see cref="LayoutHelper.IsDocumentSelected"/>。
        /// 该属性驱动 <see cref="ApplyPanelMode"/> 收敛右侧三面板；
        /// 2026-10-15 活动栏整条下线后它暂无 XAML 消费者（保留作模式状态，
        /// 将来要在界面上显示"设计中"之类提示可直接绑）。
        /// </summary>
        public bool IsScadaDesignMode
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
        /// 布局加载/重载后必须重新调用（LayoutContent 实例会被替换）。
        /// 两条调用路径：① LayoutLoaded 事件（成功恢复布局后）；② Shell.OnContentRendered
        /// 里无条件补挂一次——恢复失败时事件不触发，缺这一路就是"面板在、活动栏不亮"。
        /// 实现先解绑再挂，因此重复调用是幂等的。
        /// </summary>
        public void AttachDockWatchers()
        {
            foreach (var (panel, handler) in _dockWatchers)
                panel.PropertyChanged -= handler;
            _dockWatchers.Clear();

            // 监听清单：前 5 个驱动活动栏高亮；最后一个是"当前是否在组态界面设计"的判据来源。
            foreach (var contentId in new[] { "Panel_ProcessView", "Panel_ToolView", "Panel_ScadaToolboxView", "Panel_ScadaLayerView", "Panel_ScadaEditorView" })
            {
                // LayoutContent 基类同时兼容停靠面板与文档选项卡（工具箱为文档类型）
                if (LayoutHelper.FindPanel(contentId) is not LayoutContent panel) continue;

                bool isScadaEditor = contentId == "Panel_ScadaEditorView";
                PropertyChangedEventHandler handler = (_, e) =>
                {
                    // 文档类型无 IsVisible 属性，用 IsSelected（标签选中）替代
                    if (e.PropertyName is nameof(LayoutAnchorable.IsVisible) or nameof(LayoutContent.IsActive) or nameof(LayoutDocument.IsSelected))
                        RefreshActivityBar();

                    // R56 第三处触发点：中央文档区切标签后重收敛。
                    // 只在组态模式生效：其它模式下重新 Show 面板会抢右侧标签（Free 尤其明显），
                    // 而“文档可见性”已由模式切换本身保证，不需要这里再收敛一次。
                    if (isScadaEditor && CurrentMode == ShellMode.Config
                        && e.PropertyName == nameof(LayoutDocument.IsSelected))
                        ApplyPanelMode();
                };
                panel.PropertyChanged += handler;
                _dockWatchers.Add((panel, handler));
            }

            RefreshActivityBar();
        }

        /// <summary>
        /// 切换工作模式：先切文档上下文，再收敛左右面板与底部面板。
        /// 文档切换会触发 ApplyPanelMode，必须用 _applyingShellMode 防止递归/互相覆盖。
        /// </summary>
        public void SetShellMode(string modeText)
        {
            if (!Enum.TryParse<ShellMode>(modeText, ignoreCase: true, out var mode)) return;
            if (CurrentMode == mode)
            {
                ApplyShellMode(mode);
                return;
            }

            var previous = CurrentMode;
            CurrentMode = mode;
            try
            {
                ApplyShellMode(mode);
            }
            catch (Exception ex)
            {
                // 模式收敛失败不许把界面留在“半个模式”：回滚模式标记并重收敛一次。
                Console.WriteLine($"切换工作模式失败，已回滚到 {previous}：{ex.Message}");
                CurrentMode = previous;
                try { ApplyShellMode(previous); } catch { /* 回滚失败只记日志，不覆盖原始异常 */ }
            }
        }

        private bool _applyingShellMode;

        private void ApplyShellMode(ShellMode mode)
        {
            if (_applyingShellMode) return;
            _applyingShellMode = true;
            try
            {
                LayoutHelper.ApplyShellMode(mode);
                IsScadaDesignMode = mode == ShellMode.Config;
                RefreshActivityBar();
            }
            finally
            {
                _applyingShellMode = false;
            }
        }

        /// <summary>
        /// 按当前工作模式收敛面板组（幂等）。触发点：启动、布局加载/重载、中央文档区切标签。
        /// </summary>
        public void ApplyPanelMode()
        {
            if (_applyingShellMode || LayoutHelper.IsApplyingShellMode) return;

            // 调试 / 运行 / 组态由模式直接决定；自由模式始终保留全部面板，
            // 不跟随“组态文档选中”再收起算子工具箱（否则“全部面板”名不副实）。
            if (CurrentMode != ShellMode.Free)
            {
                ApplyShellMode(CurrentMode);
                return;
            }

            IsScadaDesignMode = false;
            ApplyShellMode(ShellMode.Free);
        }

        /// <summary>
        /// 布局加载/重载完成：先重挂监听（反序列化会换掉 LayoutContent 实例），再按模式收敛面板组。
        /// </summary>
        private void OnLayoutLoaded()
        {
            AttachDockWatchers();
            ApplyPanelMode();
        }

        private void RefreshActivityBar()
        {
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
            FlowEngineService flowEngineService,
            Services.CommunicationSolutionBinder communicationBinder
        )
        {
            StartBackgroundMonitoring();
            SolutionCommand = new(ExecuteProjectAction, CanExecuteSolution);
            SetShellModeCommand = new DelegateCommand<string>(SetShellMode);
            ExecutionCommand = new DelegateCommand<ExecutionAction?>(OnExecutionAction, CanExecuteExecution);
            ToggleBreakpointOnCurrentStepCommand = new DelegateCommand(ToggleBreakpointOnCurrentStep);
            SystemCommand = new DelegateCommand<SystemAction?>(OnSystemAction);
            SwitchCanvasCommand = new DelegateCommand<string>(SwitchCanvas);
            TogglePanelCommand = new DelegateCommand<string>(TogglePanel);
            OpenCommandPaletteCommand = new DelegateCommand(OpenCommandPalette);
            CloseCommandPaletteCommand = new DelegateCommand(CloseCommandPalette);
            ExecuteCommandPaletteItemCommand = new DelegateCommand(ExecuteSelectedPaletteItem);
            ToggleConsoleCommand = new DelegateCommand(LayoutHelper.ToggleConsole);
            // 布局加载/重载后：重挂监听 + 按模式收敛面板组（不能只挂 AttachDockWatchers，
            // 否则磁盘 Layout.xml 恢复出来的旧可见性会盖掉本次改造）
            LayoutHelper.LayoutLoaded += OnLayoutLoaded;
            this.solutionService = solutionService;
            this.Workspace = workspaceManager;
            this.Recipe = ContainerLocator.Container.Resolve<RecipeViewModel>();
            this.flowEngine = flowEngine;
            this.executionContext = executionContext;
            this.dialogService = dialogService;
            this.flowService = flowService;
            this._runtimeManager = _runtimeManager;
            this._flowCompiler = _flowCompiler;
            this._communicationManager = communicationManager;
            this._variableBridge = variableBridge;
            CommunicationBinder = communicationBinder;

            // DWV 第 1 期：调试会话状态订阅（生命周期 = Shell 全程，无需退订）。
            // 调试态由各运行入口（RunAllEnabledOnce/Continuous）以 debugSession 参数交给
            // 引擎置位（抢锁后）：普通运行=false（并行组真并发），流程含断点时=true（自动调试），
            // 这里负责把"暂停 / 恢复"镜像到 RunState、按暂停原因驱动命中窗。
            this._flowEngineService = flowEngineService;
            _flowEngineService.SessionStateChanged += OnSessionStateChanged;

            // 空闲超时登出要"说一声"。挂在这里（而不是弹窗里）是因为主窗口是唯一
            // 从头到尾开着的那一个——超时那一刻弹窗多半关着（见 ScadaLoginViewModel 注释①）。
            // Shell 是应用级单例，订阅一次即终身有效，无需解订阅。
            this._accessPolicy = accessPolicy;
            _accessPolicy.AutoLoggedOut += OnAutoLoggedOut;

            // 运行条文案在"没有流程在跑"时回落到当前流程，所以选中项一变也要重算
            // （有流程在跑时显示的是"在跑的那几条"，与选中无关）
            if (workspaceManager is INotifyPropertyChanged workspaceChanges)
                workspaceChanges.PropertyChanged += OnWorkspacePropertyChanged;

            // Core 层持久化服务的通信管理器引用（保存/加载方案时需要按协议重建地址）
            Services.ServiceLocator.CommunicationManager = communicationManager;

            // 连接配置的恢复与自动连接已由 CommunicationModule 在启动装配阶段完成
            // （必须早于通讯自检与 Shell 构造，见 CommunicationModule.Initialize），此处不再重复加载，
            // 否则会出现"两处都能加载配置"的真相源分裂。
        }

        private void OpenCommandPalette()
        {
            BuildCommandPalette();
            CommandPaletteQuery = string.Empty;
            IsCommandPaletteOpen = true;
            SelectedCommandPaletteItem = CommandPaletteItems.FirstOrDefault();
        }

        private void CloseCommandPalette()
        {
            IsCommandPaletteOpen = false;
            CommandPaletteQuery = string.Empty;
        }

        private void ExecuteSelectedPaletteItem()
        {
            var item = SelectedCommandPaletteItem;
            if (item == null) return;

            CloseCommandPalette();
            item.Invoke?.Invoke();
        }

        private void BuildCommandPalette()
        {
            _allPaletteItems.Clear();

            void Add(string title, string subtitle, string glyph, Action action)
                => _allPaletteItems.Add(new CommandPaletteItem
                {
                    Title = title,
                    Subtitle = subtitle,
                    Glyph = glyph,
                    Invoke = action,
                });

            Add("调试", "切换工作模式", "\uf1c0", () => SetShellMode("Debug"));
            Add("运行", "切换工作模式", "\uf04b", () => SetShellMode("Run"));
            Add("组态", "切换工作模式", "\uf1b3", () => SetShellMode("Config"));
            Add("自由", "切换工作模式", "\uf2d2", () => SetShellMode("Free"));

            Add("视觉图像", "视图 · 调试工作区", "\uf030", () => SetShellMode("Debug"));
            Add("流程画布", "视图 · 调试工作区", "\uf542", () => SetShellMode("Debug"));
            Add("组态画面", "视图 · 组态工作区", "\uf1b3", () => SetShellMode("Config"));
            Add("运行驾驶舱", "视图 · 运行工作区", "\uf0e7", () => SetShellMode("Run"));
            Add("流程导航", "视图 · 左侧面板", "\uf0e8", () => LayoutHelper.ShowPanel("Panel_ProcessView"));
            Add("工具箱", "视图 · 右侧面板", "\uf552", () => LayoutHelper.ShowPanel("Panel_ToolView"));
            Add("检查器", "视图 · 属性 / 图层", "\uf1de", () => LayoutHelper.ShowPanel("Panel_ScadaPropertyView"));
            Add("控制台", "视图 · 展开 / 收起底部面板", "\uf120", LayoutHelper.ToggleConsole);

            Add("全局变量", "命令", "\uf121", () => OnSystemAction(SystemAction.GlobalVariables));
            Add("硬件管理", "命令", "\uf2db", () => OnSystemAction(SystemAction.HardwareSettings));

            if (Workspace.CurrentSolution?.Flows != null)
            {
                foreach (var flow in Workspace.CurrentSolution.Flows)
                {
                    var target = flow;
                    Add(target.FlowName, "切换流程", "\uf0e8", () => Workspace.SwitchFlow(target));
                }
            }

            FilterCommandPalette();
        }

        private void FilterCommandPalette()
        {
            var query = CommandPaletteQuery?.Trim() ?? string.Empty;
            var matches = string.IsNullOrEmpty(query)
                ? _allPaletteItems
                : _allPaletteItems.Where(item =>
                    item.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || item.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

            CommandPaletteItems.Clear();
            foreach (var item in matches)
                CommandPaletteItems.Add(item);

            SelectedCommandPaletteItem = CommandPaletteItems.FirstOrDefault();
            RaisePropertyChanged(nameof(CommandPaletteEmptyVisibility));
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
        /// 方案操作互锁：保存永远可用；新建/打开/另存为/浏览列表都会丢弃或改变当前运行上下文，运行中一律禁用。
        ///
        /// 另存为也必须拦：它改的是同一个 <see cref="SolutionModel.SolutionFilePath"/>，而本次运行的
        /// 报警历史与运行窗口图片根是在 Start 时按旧目录解析好的——运行中另存为会让“日志写新目录、
        /// 报警写旧目录”劈成两半。停掉再存。
        ///
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
        /// 运行条上"在跑的是谁"。
        /// 数据源与 <see cref="AnyFlowRunning"/> 同一份（<see cref="FlowModel.RunState"/> 镜像），
        /// 所以条上的说法与流程栏的「运行中」标签永远一致。
        /// </summary>
        public string RunBarFlowText => FormatRunBarFlowText(RunningFlowNames(), CurrentFlowName);

        /// <summary>多条在跑时把完整名单放进悬停提示（条上只放得下「A 等 N 个流程」）</summary>
        public string RunBarFlowToolTip => FormatRunBarFlowToolTip(RunningFlowNames(), CurrentFlowName);

        /// <summary>运行条文案的纯函数（断言直接调它，不必凑一个 Shell）</summary>
        public static string FormatRunBarFlowText(IReadOnlyList<string> running, string? currentFlow)
        {
            if (running.Count == 0)
                return string.IsNullOrEmpty(currentFlow) ? "未选择流程" : currentFlow!;

            return running.Count == 1 ? running[0] : $"{running[0]} 等 {running.Count} 个流程";
        }

        /// <summary>运行条悬停提示的纯函数：只有真的多条时才列名单</summary>
        public static string FormatRunBarFlowToolTip(IReadOnlyList<string> running, string? currentFlow)
            => running.Count > 1
                ? "正在运行：" + string.Join("、", running)
                : FormatRunBarFlowText(running, currentFlow);

        /// <summary>当前在跑（含暂停）的流程名，按方案里的流程次序</summary>
        private List<string> RunningFlowNames()
        {
            var names = new List<string>();

            var flows = Workspace?.CurrentSolution?.Flows;
            if (flows == null) return names;

            foreach (var flow in flows)
            {
                if (flow == null) continue;
                if (flow.RunState is FlowRunState.Running or FlowRunState.Paused)
                    names.Add(flow.FlowName);
            }

            return names;
        }

        /// <summary>选中流程变了：运行条在"没流程在跑"时要跟着换名字（其余情况显示的是在跑的那条）</summary>
        private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is null or nameof(IReadOnlyWorkspaceContext.CurrentFlow))
            {
                RaisePropertyChanged(nameof(RunBarFlowText));
                RaisePropertyChanged(nameof(RunBarFlowToolTip));
            }
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
                Notifier.ShowWarning("流程运行中，禁止新建/打开/另存为/切换方案或修改报警配置；如需操作请先点击“停止”");
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
                        // 新方案没有通讯配置 → 清空当前连接（方案即唯一事实源）
                        CommunicationBinder.Apply(newSolution, autoStart: false);
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
                case SolutionAction.SaveAs:
                    await SaveSolutionAsAsync();
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

                    // 换方案 = 上一份方案的会话全部作废（会话按流程名索引，同名不同源会串车）；
                    // 顺带把插件实例/HImage 释放掉，别让上一份方案常驻
                    _runtimeManager.ClearAll();
                    Services.SolutionConfigApplier.Restore(loadResult.Data.Config);
                    // 按快照重建变量集合 + 重新接线网络变量（轮询镜像链路）
                    VariablePersistenceService.Restore(loadResult.Data, Workspace);
                    _variableBridge.RebindAll();
                    // 通讯配置随方案走：用方案里的连接替换管理器集合（先停后换，逐条隔离）
                    CommunicationBinder.Apply(loadResult.Data, autoStart: true);
                    ConnectMotionAutoStart();
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
        /// 软件启动时按软件级配置（AppConfig.json）自动加载默认启动方案。
        ///
        /// N3：两种失败都必须"说一声"（日志 + 右下角非模态提示），不许静默 return ——
        /// 静默的后果是用户只看到"方案没加载"，既不知道为什么、也不知道去哪里改：
        ///  · 文件不存在（配置指向了被删/被移走/网络盘没连上的文件）→ Warn；
        ///  · 文件在但解析失败（版本不兼容 / 手工改坏 / 半截写入）→ Error，文案带 LoadAsync 的原因。
        /// **不自动清 StartupSolutionPath**：盘没挂上、文件暂时被移走都是可恢复的，
        /// 替用户删配置等于"下次插上盘也不会再自动加载"，比多一条提示更难查。
        /// </summary>
        public async Task AutoLoadStartupSolutionAsync()
        {
            var settings = ContainerLocator.Container.Resolve<AppSettingsService>();
            var startupPath = settings.Current.StartupSolutionPath;
            if (string.IsNullOrWhiteSpace(startupPath)) return;

            var log = TryResolveLogService();

            if (!System.IO.File.Exists(startupPath))
            {
                log?.Warn($"[启动方案] 文件不存在：{startupPath}（已跳过自动加载；配置保留，不自动清除）");
                Notifier.ShowWarning($"启动方案文件不存在，已跳过自动加载：{startupPath}");
                return;
            }

            var loadResult = await solutionService.LoadAsync(startupPath);
            if (!loadResult.Success)
            {
                // 解析失败 = 文件在但内容不可用。Error 级别（这是"配置/文件坏了"，不是"没配"），
                // 原因直接用 LoadAsync 拼好的文案（含异常消息），界面与日志保持同一句话。
                log?.Error($"[启动方案] 解析失败：{startupPath}：{loadResult.Message}");
                Notifier.ShowError($"启动方案解析失败：{loadResult.Message}");
                return;
            }

            loadResult.Data.SolutionFilePath = startupPath;
            Workspace.SwitchSolution(loadResult.Data);
            // 自动加载不恢复方案布局：保持"上次关闭时的布局"，避免覆盖用户重置的默认布局；
            // 手动打开方案仍按方案记忆布局（Restore 默认 true）
            Services.SolutionConfigApplier.Restore(loadResult.Data.Config, restoreLayout: false);
            // 按快照重建变量集合 + 重新接线网络变量（轮询镜像链路）
            VariablePersistenceService.Restore(loadResult.Data, Workspace);
            _variableBridge.RebindAll();
            CommunicationBinder.Apply(loadResult.Data, autoStart: true);
            ConnectMotionAutoStart();
            Notifier.ShowSuccess($"已自动加载方案 [{loadResult.Data.SolutionName}]");
        }

        /// <summary>
        /// 取日志服务（N3 启动方案失败留痕用）。
        ///
        /// 为什么不在构造函数里注入：Shell 是应用级单例，而日志服务在 RegisterTypes 之后才可用
        /// （App.xaml.cs 的启动自检链同样用 Resolve 拿它）。解析不到就返回 null ——
        /// "启动方案加载失败"本身绝不能因为日志不可用再抛一次（提示仍然会走 Notifier）。
        /// </summary>
        private static ILogService? TryResolveLogService()
        {
            try { return ContainerLocator.Container.Resolve<ILogService>(); }
            catch { return null; }
        }

        /// <summary>
        /// 方案加载后补一次运动卡自动连接。
        ///
        /// 为什么需要：MotionModule 只在**启动时**调一次 ConnectAutoStart，而方案是在启动之后才加载的，
        /// 那一刻方案里还没有运动卡 → 自动连接无事可做；此后 EnsureSynced 只建运行态设备，不会连。
        /// 结果就是"AutoConnect=true 却一直是 Closed"，用户必须手动去运动卡设置里点连接。
        /// 放在方案加载之后补一次，与通讯的 CommunicationBinder.Apply 同一时机、同一语义。
        /// </summary>
        private static void ConnectMotionAutoStart()
        {
            try
            {
                var provider = ContainerLocator.Container.Resolve<global::Core.Interfaces.IMotionProvider>() as Services.MotionProvider;
                provider?.EnsureSynced();
                provider?.ConnectAutoStart();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[Motion] 方案加载后自动连接失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 保存解决方案：已落过盘的直接写回原文件；从未保存过的走“另存为”。
        ///
        /// 一个方案 = 一个以方案命名的文件夹（目录约定见 <see cref="SolutionPaths"/>）：
        /// <c>&lt;保存位置&gt;\方案A\方案A.vms</c>，旁边是 Image / Alarms / Logs。
        /// 因此“保存”不再让用户挑 .vms 文件，只有“另存为”才选保存位置。
        /// </summary>
        private async Task SaveSolutionAsync()
        {
            var solution = Workspace.CurrentSolution;
            if (solution == null)
            {
                Notifier.ShowWarning("当前没有打开的解决方案");
                return;
            }

            // 从未保存过，或当前文件不符合“<位置>\<方案名>\<方案名>.vms”约定
            // （散装老方案 / 用户改过方案名）→ 走另存为，由用户选位置落成规范文件夹。
            // 读侧不受影响：散装老方案照样能打开，只是第一次保存会搬进文件夹。
            if (string.IsNullOrWhiteSpace(solution.SolutionFilePath)
                || !SolutionPaths.IsFolderLayout(solution.SolutionFilePath, solution.SolutionName))
            {
                await SaveSolutionAsAsync();
                return;
            }

            await PersistSolutionAsync(solution, solution.SolutionFilePath);
        }

        /// <summary>
        /// 另存为：选一个保存位置，在其中建「方案名」文件夹，配置与资产一起搬过去。
        /// 选中的是**父目录**而不是文件——文件夹名必须等于方案名，否则“一个方案一个文件夹”这条约定
        /// 从第一次保存就破了（用户可以把方案A.vms 放进“新建文件夹(3)”里）。
        /// </summary>
        private async Task SaveSolutionAsAsync()
        {
            var solution = Workspace.CurrentSolution;
            if (solution == null)
            {
                Notifier.ShowWarning("当前没有打开的解决方案");
                return;
            }

            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择方案保存位置（将在其中创建以方案命名的文件夹）",
                Multiselect = false
            };

            // 默认定位到当前方案文件夹的上一级：连续另存为时不用每次从头找盘。
            var currentRoot = SolutionPaths.GetSolutionRoot(solution.SolutionFilePath);
            var currentParent = string.IsNullOrWhiteSpace(currentRoot) ? null : Path.GetDirectoryName(currentRoot);
            if (!string.IsNullOrWhiteSpace(currentParent) && Directory.Exists(currentParent))
                dialog.InitialDirectory = currentParent;

            if (dialog.ShowDialog() != true) return;

            if (!SolutionPaths.TryValidateSaveTarget(
                    dialog.FolderName, solution.SolutionName,
                    out var solutionFolder, out var configFilePath, out var error))
            {
                Notifier.ShowError(error);
                return;
            }

            await PersistSolutionAsync(solution, configFilePath, solutionFolder);
        }

        /// <summary>
        /// 落盘主链（保存与另存为共用）：Capture → 原子写 → 建目录骨架 → 搬资产。
        ///
        /// 顺序不能换：SaveAsync 内部先序列化再建目录、写失败会把本次新建的空目录删掉，
        /// 所以“配置没写成功”不会在磁盘上留下一个像方案的空壳。
        /// 目录骨架（Image / Alarms / Logs）建失败只提示不回滚——配置已经落盘，
        /// 资产目录等到第一次用时也会被各自的写端建出来。
        /// </summary>
        private async Task PersistSolutionAsync(SolutionModel solution, string filePath, string? solutionFolder = null)
        {
            var previousPath = solution.SolutionFilePath;

            CaptureLayoutToConfig();
            VariablePersistenceService.Capture(solution, Workspace); // 变量快照随方案落盘
            // 通信配置快照：通信设置里的增删改只更新管理器内存，保存时经绑定器同步进方案
            // （与"打开方案时 Apply"配对，保存走同一个类，避免两处各写一遍集合替换逻辑）
            CommunicationBinder.Capture(solution);

            var saveResult = await solutionService.SaveAsync(solution, filePath);
            if (!saveResult.Success)
            {
                Notifier.ShowError(saveResult.Message);
                return;
            }

            solution.SolutionFilePath = filePath;

            // 另存为/搬进方案文件夹后，软件级配置里指向旧路径的两处（默认启动方案、方案清单）一起改掉。
            // 不改的话，散装老方案搬进文件夹后下次启动仍加载旧副本、清单点开的也还是旧文件。
            SyncSolutionReferences(previousPath, filePath);

            if (!SolutionPaths.EnsureStructure(filePath, out var structureError))
                Notifier.ShowWarning("方案已保存，但目录结构未建全：" + structureError);

            // 另存为：把旧方案目录下的 Image / Alarms / Logs 一起带到新目录。
            // 用户复制的是“这个方案”，不是只复制一个 .vms 文件；失败只提示、不回滚保存。
            var assetFailures = SolutionAssetFolders.CopyAlongside(previousPath, filePath);
            if (assetFailures.Length > 0)
                Notifier.ShowWarning("方案已保存，但部分资产未复制：" + string.Join("；", assetFailures));

            var folder = solutionFolder ?? SolutionPaths.GetSolutionRoot(filePath) ?? filePath;
            Notifier.ShowSuccess($"方案 [{solution.SolutionName}] 已保存到 {folder}");
        }

        /// <summary>
        /// 另存为/搬进方案文件夹后，把软件级配置里仍指向旧路径的两处一起改掉：
        /// 「默认启动方案」与「方案清单」。不改的话，散装老方案搬进文件夹后，
        /// 下次启动仍加载旧副本，清单里点开的也还是那份不会再更新的 .vms。
        ///
        /// 失败只记调试痕迹：保存本身已经成功，配置同步不该把结果翻成失败。
        /// </summary>
        private static void SyncSolutionReferences(string? previousPath, string newPath)
        {
            if (string.IsNullOrWhiteSpace(previousPath)
                || string.Equals(previousPath, newPath, StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                var settings = ContainerLocator.Container.Resolve<AppSettingsService>();
                var changed = false;

                if (string.Equals(settings.Current.StartupSolutionPath, previousPath, StringComparison.OrdinalIgnoreCase))
                {
                    settings.Current.StartupSolutionPath = newPath;
                    changed = true;
                }

                foreach (var entry in settings.Current.Solutions ?? new System.Collections.Generic.List<AppSolutionEntry>())
                {
                    if (entry == null || !string.Equals(entry.Path, previousPath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    entry.Path = newPath;
                    changed = true;
                }

                if (changed) settings.Save();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[方案保存] 同步方案清单/启动路径失败（已忽略）：{ex.Message}");
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
                    // S6：单流程运行 / HTTP 触发都不写 MainRunState（只认它 = 停止按钮灰着、
                    // 而流程明明在跑）。判据补"是否真有流程在跑"，与方案互锁（CanExecuteSolution）同一口径。
                    return RunState != MainRunState.NotStarted || AnyFlowRunning();

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

        /// <summary>
        /// 观察"单次运行是否真的启动"：引擎抢不到会话锁时返回 false（本次没跑），
        /// 这里补一条"未启动"的提示——否则界面刚宣布"已启动"，用户却在等一个永远不会来的结果。
        /// </summary>
        private async Task TrackStartRejectedAsync(Task<bool> runTask, string flowName)
        {
            try
            {
                if (!await runTask)
                    Notifier.ShowWarning($"流程 [{flowName}] 未启动：会话正被其它触发占用（HTTP / 定时 / 手动）");
            }
            catch
            {
                // 结局由引擎侧反映（日志 / 状态事件），这里只负责如实提示"启动与否"
            }
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
                case SystemAction.ReturnToZero:
                    // 「回零」是运动调试里的一个动作，不是独立配置页：深链到运动板卡的手动调试页签。
                    // 旧菜单/脚本仍按 ReturnToZero 触发，枚举值保留；菜单项仍在，但不再是无处理分支。
                    ShowMotionBoard("debug");
                    break;
                case SystemAction.CommSettings:
                    ShowCommunicationSettings();
                    break;
                case SystemAction.HardwareSettings:
                    // 硬件管理（批次 C）：统一入口 + 概览 + 嵌入三个域页面。
                    // 旧菜单入口（相机/通讯/运动/IO/回零）全部深链到同一个外壳，
                    // 保证权限门禁与通讯保存语义不会被旁路。
                    ShowHardwareManager();
                    break;
                case SystemAction.IoSettings:
                    ShowIoSettings();
                    break;
                case SystemAction.SystemSettings:
                    // 合并后的唯一入口（运行窗口 / 安全与留档 / 画布三个页签）。
                    // 只传"落在哪一页"，不传配置快照——弹窗打开时从 AppSettingsService 现读，
                    // 传快照会出现"界面里那份和配置里那份不是同一个对象"，改一份丢一份。
                    ShowSystemSettings(SystemSettingsDraft.GroupRunWindow);
                    break;
                case SystemAction.RuntimeWindowSettings:
                    // 旧入口：枚举值可能已落进 Layout.xml / 旧配置，不能删。
                    // 合并后转发到「系统设置」的「运行窗口」页签（范式同 ReturnToZero → 运动板卡调试页）。
                    ShowSystemSettings(SystemSettingsDraft.GroupRunWindow);
                    break;
                case SystemAction.SystemParameters:
                    // 旧入口：转发到「系统设置」的「安全与留档」页签。
                    ShowSystemSettings(SystemSettingsDraft.GroupSafety);
                    break;
                case SystemAction.VariableEvents:
                    // 按变量配置值驱动的规则（更改数值 / 值为真 / 值为假 / 上限 / 下限）。
                    // 与上面两个不同，它改的是方案内容（落 .vms、进撤销栈），所以：
                    // ① 弹窗自己从当前方案里取"变量事件表"，这里不传参数（传了反而要在这里判方案有没有打开）；
                    // ② 它需要"有打开的方案"才有内容——没有方案时弹窗内是空清单并给出提示，
                    //    而不是在这里拦一道"请先打开方案"（拦一道的结果是菜单点下去毫无反应）。
                    dialogService.ShowDialog("ScadaVariableEventDialogView");
                    break;
                case SystemAction.Help:
                    // 帮助手册：内容由启动自检链合并好（宿主自带 + 各插件自带 + 自动生成页），
                    // 这里只负责开窗——弹窗自己从 HelpCatalogService 现取目录，宿主不传快照
                    //（不传参数的理由与「变量事件」同款：传了就要在这里判"合没合过"）。
                    dialogService.ShowDialog("HelpView");
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
        /// 打开硬件管理外壳，并落在指定域/页签。
        ///
        /// 批次 C 起所有硬件入口（相机 / 通讯 / 运动板卡 / IO / 回零）都深链到这里，
        /// 不再直接开三个独立弹窗——否则权限门禁与"通讯只在脏时保存"都会被旁路。
        /// 只传"落在哪一页"，不传任何设备/配置快照；数据一律由各页从当前方案现取。
        /// </summary>
        private void ShowHardwareManager(string? page = null, string? tab = null)
        {
            if (Workspace.CurrentSolution == null)
            {
                Notifier.ShowWarning("请先打开一个解决方案");
                return;
            }

            var parameters = new DialogParameters();
            if (!string.IsNullOrEmpty(page)) parameters.Add("page", page);
            if (!string.IsNullOrEmpty(tab)) parameters.Add("tab", tab);

            if (parameters.Count == 0)
                dialogService.ShowDialog("HardwareManagerView");
            else
                dialogService.ShowDialog("HardwareManagerView", parameters, _ => { });
        }

        /// <summary>
        /// 打开「相机设置」（深链到硬件管理的相机页）。
        ///
        /// 相机的**配置**直接挂在当前方案上（<see cref="SolutionModel.CameraConfigs"/>），
        /// 运行态设备由 CameraProvider 按方案对齐，所以：
        ///   ① 没有方案时无处可写，必须先拦一道并说明原因；
        ///   ② 不给弹窗传任何参数：相机清单与运行态一律由 CameraProvider 从"当前方案"现取。
        /// </summary>
        private void ShowCameraSettings() => ShowHardwareManager("camera");

        /// <summary>
        /// 打开运动板卡（合并窗口：卡设置 / 手动调试 / 点位列表 / 电子凸轮）。
        /// 取代原来两个独立入口（运动设置 / 手动调试弹窗已退役）——
        /// 参数、调试、点位在一个窗口里切换，现场不用来回找菜单。
        /// 与 <see cref="ShowCameraSettings"/> 同口径：没有方案就拦下来提示（运动卡是方案级资源，
        /// 没有方案时无处可存），且**不传快照参数** —— 卡片清单与运行态一律由 MotionProvider
        /// 从"当前方案"现取；传一份快照反而会出现"界面里那份和方案里那份不是同一个对象"，
        /// 改一份丢一份。
        ///
        /// <paramref name="tab"/> 只传"落在哪个页签"（debug / points / cam），不传任何设备数据。
        /// 「回零」菜单走 debug 深链；不传 = 默认卡设置页。
        /// </summary>
        private void ShowMotionBoard(string? tab = null) => ShowHardwareManager("motion", tab);


        /// <summary>
        /// IO 设置入口的转发壳：IO 接入层已下线，IO 点位现在就是通讯连接下的网络变量，
        /// 所以旧菜单项/快捷键统一落到「通讯设置」。保留方法是为了不让旧布局与脚本里的
        /// <see cref="SystemAction.IoSettings"/> 失效（枚举只能追加，不能删值）。
        /// </summary>
        private void ShowIoSettings() => ShowCommunicationSettings();

        /// <summary>
        /// 打开「系统设置」并落在指定页签。
        ///
        /// 只传页签名，不传任何配置快照：弹窗打开时从 <see cref="AppSettingsService"/> 现读，
        /// 这样"两次打开之间配置被别处改过"也能显示当前真值。
        /// 旧的两个入口（<see cref="SystemAction.RuntimeWindowSettings"/> /
        /// <see cref="SystemAction.SystemParameters"/>）走同一个方法，只是页签不同。
        /// </summary>
        private void ShowSystemSettings(string group)
        {
            var parameters = new DialogParameters();
            parameters.Add(ViewModels.DialogViewModels.ScadaSystemSettingsViewModel.GroupParameterKey, group);
            dialogService.ShowDialog(
                ViewModels.DialogViewModels.ScadaSystemSettingsViewModel.DialogName, parameters, _ => { });
        }

        private void ShowCommunicationSettings() => ShowHardwareManager("comm");

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

                // 编译错误列表广播（三视角评审第二批）：保留逐条弹泡，画布的「错误列表」按钮
                // 据此缓存最近一次错误并支持双击定位到出错步骤
                GlobalEventBus.Publish(new CompilationErrorsEvent
                {
                    FlowName = CurrentFlowName,
                    Errors = result.Errors,
                });
                return;
            }

            var newSession = new FlowSession
            {
                FlowName = CurrentFlowName,
                ExecutionEngine = result.Data,
                CompiledVersion = Workspace.CurrentFlow.Version,
                CompiledFlowId = Workspace.CurrentFlow?.FlowID ?? string.Empty,
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
                        CompiledFlowId = flow.FlowID ?? string.Empty,
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

                    // 每个失败流程各广播一次（照 DoCompile 的通道，评审清单 B 包交互补齐）：
                    // 画布按 FlowName 分组累计，批量编译的错误才能在一处看全
                    // （口径见 FlowCanvasViewModel.OnCompilationErrorsEvent）
                    GlobalEventBus.Publish(new CompilationErrorsEvent
                    {
                        FlowName = flow.FlowName,
                        Errors = result.Errors,
                    });
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

            var selection = FlowBatchSelector.Select(Workspace.CurrentSolution);
            if (selection.Selected.Count == 0)
            {
                RunState = MainRunState.NotStarted;
                Notifier.ShowWarning(selection.Warning ?? "没有可运行的主流程");
                return;
            }

            if (selection.EnabledButExcluded.Count > 0)
            {
                Notifier.ShowInfo(
                    "以下启用流程不参与批量运行（非主流程）："
                    + string.Join("、", selection.EnabledButExcluded.Select(f => f.FlowName)));
            }

            // 进入运行态：立即互锁"编译/启动/循环"，放开"停止"
            RunState = MainRunState.RunningOnce;
            int runCount = 0;
            var tasks = new List<Task>();

            foreach (var flow in selection.Selected)
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
                        CompiledFlowId = flow.FlowID ?? string.Empty,
                    };

                    session.AddBlueprintsDeep(flow.Steps);

                    _runtimeManager.RegisterSession(session);
                }

                if (session != null && !session.IsRunning)
                {
                    // 方案 A（2026-10-10）：界面运行默认**普通运行**——并行组真并发；
                    // 只有流程里存在断点时才自动按调试会话启动（装调试门，断点/单步可用，
                    // 代价是并行组就地退化顺序执行，见 FlowSession.DebugDegradedParallel）。
                    // 调试态不在这里预置：由引擎抢到会话锁后按此参数置位——预置的话，本次若被 HTTP
                    // 抢到锁而拒绝，true 会残留给那次非界面运行（P2）；per-run 清回 false 在引擎收尾。
                    // 用 Try 版本：抢不到锁（HTTP / 定时恰好占着）时引擎返回 false，
                    // 由跟随任务立刻给一条"未启动"的提示——旧写法会把"没跑上"静默包装成"已启动"。
                    bool debugRun = flow.HasBreakpoint();
                    if (debugRun)
                        Notifier.ShowInfo($"流程 [{flow.FlowName}] 检测到断点，本次按调试运行（断点/单步生效，并行组将按顺序执行）");
                    tasks.Add(TrackStartRejectedAsync(flowEngine.TryRunSessionOnceAsync(session, debugSession: debugRun), flow.FlowName));
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

            var selection = FlowBatchSelector.Select(Workspace.CurrentSolution);
            if (selection.Selected.Count == 0)
            {
                RunState = MainRunState.NotStarted;
                Notifier.ShowWarning(selection.Warning ?? "没有可运行的主流程");
                return;
            }

            if (selection.EnabledButExcluded.Count > 0)
            {
                Notifier.ShowInfo(
                    "以下启用流程不参与批量运行（非主流程）："
                    + string.Join("、", selection.EnabledButExcluded.Select(f => f.FlowName)));
            }

            // 进入运行态：立即互锁"编译/启动/循环"，放开"停止"
            RunState = MainRunState.RunningContinuous;
            int runCount = 0;
            var tasks = new List<Task>();

            foreach (var flow in selection.Selected)
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
                        CompiledFlowId = flow.FlowID ?? string.Empty,
                    };

                    session.AddBlueprintsDeep(flow.Steps);

                    _runtimeManager.RegisterSession(session);
                }

                if (session != null && !session.IsRunning)
                {
                    // 方案 A（2026-10-10）：连续运行默认**普通运行**——并行组真并发；
                    // 只有流程里存在断点时才自动按调试会话启动（装调试门，断点/单步可用，
                    // 代价是并行组就地退化顺序执行，见 FlowSession.DebugDegradedParallel）。
                    // 调试态不在这里预置（理由同单次运行入口：抢锁后才置位，防 P2 泄漏）。
                    // 循环会话的任务只有被"停止"取消后才会结束，因此这里绝不能 await 它。
                    bool debugRun = flow.HasBreakpoint();
                    if (debugRun)
                        Notifier.ShowInfo($"流程 [{flow.FlowName}] 检测到断点，本次按调试运行（断点/单步生效，并行组将按顺序执行）");
                    var continuousTask = flowEngine.RunSessionAsync(session, debugSession: debugRun);
                    // 连续运行的"抢不到锁"没有返回值可判：引擎的抢锁与 IsRunning 置位都在首个 await
                    // 之前**同步**完成，所以调用返回后立刻取快照就是准的（不猜时序）
                    bool startedNow = session.IsRunning;
                    if (startedNow)
                    {
                        tasks.Add(continuousTask);
                        runCount++;
                    }
                    else
                    {
                        Notifier.ShowWarning($"流程 [{flow.FlowName}] 未启动：会话正被其它触发占用（HTTP / 定时 / 手动）");
                    }
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
        /// 停止所有正在运行的流程。
        ///
        /// S6：提示必须按**实际停了几条**来说 —— 文案不许暗示"只停当前流程"
        /// （停止按钮的语义一直是 StopAll：界面发起的整体运行、单流程运行、HTTP 触发一起停）。
        /// 0 条时给一条 Info 而不是 Success：按钮现在会因"有流程在跑"而可点，
        /// 但真到点下去那一刻可能已经跑完了，"已停止所有运行中的流程"就成了假话。
        /// </summary>
        private void StopAllRunning()
        {
            int stopped = _flowEngineService.StopAll();
            if (stopped > 0)
                Notifier.ShowSuccess($"已请求停止 {stopped} 个运行中的流程");
            else
                Notifier.ShowInfo("当前没有运行中的流程（停止请求未发出）");
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

            // 谁在跑变了：运行条文案跟着重算（多条并跑时条上要报"等 N 个流程"）
            RaisePropertyChanged(nameof(RunBarFlowText));
            RaisePropertyChanged(nameof(RunBarFlowToolTip));

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

        /// <summary>命中窗「打开模块参数」：复用流程栏同一帮助类（口径只有一份）。
        /// workspace 传下去：命中目标是并行分组时，面板保存成功后推进 CurrentFlow.Version（与流程栏同一口径）。</summary>
        private void OpenHitStepParameters()
        {
            if (_hitTargetStep == null) return;
            StepParameterDialog.Open(_hitTargetStep, dialogService, Workspace);
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
