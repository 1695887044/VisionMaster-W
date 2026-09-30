using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Core.Interfaces;
using Prism.Commands;
using Prism.Mvvm;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 回零模式下拉的一项。
    ///
    /// 为什么包一层而不直接绑 HomeMode 枚举：
    ///   ① 枚举的 <c>ToString()</c> 出来的是成员名（<c>NegativeLimitIndex</c>），
    ///      操作员看不懂 —— 包装一层才能挂中文名与一句话说明；
    ///   ② 下拉的选中项绑到**引用类型**比绑到值类型稳：
    ///      列表被清空/重填时，值类型可能出现"选中项落不到列表上"的边界情况
    ///      （现场那张"下拉空白 + 红框"的截图就是从这个方向查起的）。
    /// 历史：原先定义在 MotionDebugViewModel 里，旧调试弹窗退役后随用途迁到本文件。
    /// </summary>
    public sealed class HomeModeOption
    {
        public HomeModeOption(HomeMode mode) => Mode = mode;

        /// <summary>对应的契约枚举值（下发命令时用这个）</summary>
        public HomeMode Mode { get; }

        /// <summary>下拉里显示的文本</summary>
        public string DisplayName => MotionEnumDisplay.Text(Mode);

        /// <summary>一句话说明（悬停提示）</summary>
        public string Hint => MotionEnumDisplay.Hint(Mode);

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// 手动调试页签里的一根轴（左栏列表行）。
    ///
    /// 数据源 = 当前卡的**启用**轴映射（差异清单 S2-1 的修复点：上一版列表绑了
    /// 原始 MotionDescriptor，界面显示的是类型名）。位置与状态标签来自驱动的
    /// 轮询快照（不额外占通信），标签语义与设计稿 4.1 对齐：
    /// 映射停用 / 报警 / 限位 / {模式}运行中(呼吸) / 已使能·已回零 / 已使能 / 未使能 / 未连接。
    /// </summary>
    public sealed class MotionDebugAxisRow : BindableBase
    {
        public MotionDebugAxisRow(AxisMapping mapping)
        {
            Mapping = mapping;
        }

        public AxisMapping Mapping { get; }

        public string LogicalName =>
            string.IsNullOrWhiteSpace(Mapping.LogicalName) ? $"轴{Mapping.PhysicalIndex}" : Mapping.LogicalName;

        /// <summary>轴名数据源被外部改了（卡设置页签行内改名），由页签 VM 调用补通知</summary>
        public void RaiseLogicalNameChanged() => RaisePropertyChanged(nameof(LogicalName));

        private double _positionMm;
        public double PositionMm
        {
            get => _positionMm;
            private set
            {
                // ★ PositionText（列表"位置"列绑的就是它）是 PositionMm 的计算属性：
                //   只通知 PositionMm 的话，绑定 PositionText 的 TextBlock 永远停在旧值 ——
                //   这正是"点动时读数条在跳、轴列表的位置列不动"的根因。
                if (SetProperty(ref _positionMm, value))
                    RaisePropertyChanged(nameof(PositionText));
            }
        }

        public string PositionText => $"{PositionMm:F3}";

        private string _stateLabel = "未连接";
        public string StateLabel
        {
            get => _stateLabel;
            private set => SetProperty(ref _stateLabel, value);
        }

        /// <summary>状态语义级别（Neutral/Accent/Success/Warning/Danger），决定圆点颜色</summary>
        private string _stateLevel = "Neutral";
        public string StateLevel
        {
            get => _stateLevel;
            private set => SetProperty(ref _stateLevel, value);
        }

        /// <summary>运动中（圆点呼吸动画）</summary>
        private bool _isMoving;
        public bool IsMoving
        {
            get => _isMoving;
            private set => SetProperty(ref _isMoving, value);
        }

        private bool _hasCritical;
        public bool HasCriticalSignal
        {
            get => _hasCritical;
            private set => SetProperty(ref _hasCritical, value);
        }

        /// <summary>
        /// 用一次快照刷新本行。<paramref name="modeText"/> 是本面板记录的"这根轴正在做什么"
        /// （点动/回零/定位，由发起动作时写入，见页签 VM）——卡只知道"在动"，模式名由界面补。
        /// </summary>
        public void Update(AxisStatus? status, bool mapEnabled, bool deviceOnline, bool deviceHomed, string modeText)
        {
            if (!mapEnabled)
            {
                StateLabel = "映射停用";
                StateLevel = "Neutral";
                IsMoving = false;
                HasCriticalSignal = false;
                return;
            }

            if (status == null)
            {
                PositionMm = 0;
                StateLabel = deviceOnline ? "无状态" : "未连接";
                StateLevel = "Neutral";
                IsMoving = false;
                HasCriticalSignal = false;
                return;
            }

            PositionMm = status.PositionMm;
            IsMoving = status.Moving;
            HasCriticalSignal = status.HasCriticalSignal;

            // 优先级 = 严重度：报警 > 限位 > 运动中 > 使能状态（最该被看到的必须显示出来）
            (StateLabel, StateLevel) =
                status.Alarm ? ("报警", "Danger")
                : status.PositiveLimit ? ("正限位", "Warning")
                : status.NegativeLimit ? ("负限位", "Warning")
                : status.Moving ? (string.IsNullOrWhiteSpace(modeText) ? "运动中" : $"{modeText}运行中", "Accent")
                : status.Enabled
                    ? (deviceHomed ? "已使能·已回零" : "已使能", "Accent")
                : ("未使能", "Neutral");
        }
    }

    /// <summary>卡上 IO 的一路（IN 只读监视 / OUT 点击翻转）</summary>
    public sealed class MotionDebugIoRow : BindableBase
    {
        public MotionDebugIoRow(int port, bool isOutput)
        {
            Port = port;
            IsOutput = isOutput;
        }

        public int Port { get; }
        public bool IsOutput { get; }

        public string Label => IsOutput ? $"OUT{Port}" : $"IN{Port}";

        private bool _isOn;
        public bool IsOn
        {
            get => _isOn;
            set => SetProperty(ref _isOn, value);
        }
    }

    /// <summary>
    /// 页签二「手动调试」（规格第 4 章）。
    ///
    /// 与旧「运动卡调试」弹窗的分工不变：这里是**操作**（使能/点动/定位/回零/IO），
    /// 配置在卡设置页签。三条安全设计与旧面板一脉相承：
    ///   ① 点动"按住才动松手即停"（视图层 HoldCommandBehavior：按下/松开/失焦四条路径）；
    ///   ② 点动心跳兜底（500ms 没有界面脉冲就自动停 —— 判断复用
    ///      <see cref="MotionJogGuard"/> 的静态纯函数，安全判断只有一份）；
    ///   ③ 关窗必停点动（外壳 OnDialogClosed 调 OnShellClosed）。
    ///
    /// 【闸门】未连接/未使能/未回零/忙 四道闸全部在本层先判（文案逐字按规格 4.2），
    /// 过闸后命令再交给设备 —— 设备自己的门禁（软限位/能力）拒绝时取它的原因上 toast。
    /// </summary>
    public class MotionBoardDebugTabViewModel : BindableBase
    {
        private readonly MotionProvider _provider;
        private readonly MotionBoardViewModel _shell;

        private DispatcherTimer? _jogWatchdog;
        private DateTime _lastJogPulseUtc = DateTime.MinValue;
        private IMotionDevice? _jogDevice;
        private string _jogAxis = string.Empty;

        /// <summary>每根轴"正在做什么"（发起动作时记录，到位后清除）——用于"正在{模式}"文案</summary>
        private readonly Dictionary<string, string> _modeByAxis = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 正在等待落定的输出点号（挡住对同一点的连点）。
        /// 不挡的话，连点两次会基于同一个旧值算出同一个目标值，白写两遍。
        /// </summary>
        private readonly HashSet<int> _pendingIoPorts = new();

        public MotionBoardDebugTabViewModel(
            MotionProvider provider, IWorkspaceManager workspace, MotionBoardViewModel shell)
        {
            _provider = provider;
            _shell = shell;

            ToggleConnectCommand = new DelegateCommand(ToggleConnect);
            ClearAlarmCommand = new DelegateCommand(ClearAlarm);
            EmergencyStopCommand = new DelegateCommand(EmergencyStop);
            EnableCommand = new DelegateCommand(() => _ = RunGuardedAsync(() => SetEnableAsync(true), "使能"));
            DisableCommand = new DelegateCommand(() => _ = RunGuardedAsync(() => SetEnableAsync(false), "失能"));
            StopAllCommand = new DelegateCommand(StopAll);
            PulseJogCommand = new DelegateCommand<string>(PulseJog);
            StopJogCommand = new DelegateCommand(StopJogFromUi);
            MoveAbsoluteCommand = new DelegateCommand(() => _ = RunGuardedAsync(() => MoveAbsoluteAsync(waitForArrival: false), "定位"));
            MoveAbsoluteAndWaitCommand = new DelegateCommand(
                () => _ = RunGuardedAsync(() => MoveAbsoluteAsync(waitForArrival: true), "定位"),
                () => !AnyMoving);
            TakeCurrentPositionCommand = new DelegateCommand(TakeCurrentPosition);
            HomeCommand = new DelegateCommand(() => _ = RunGuardedAsync(HomeAsync, "回零"));
            ToggleOutputCommand = new DelegateCommand<MotionDebugIoRow>(
                row => { if (row != null) _ = RunGuardedAsync(() => ToggleOutputAsync(row), "写输出"); });
        }

        private MotionDescriptor? _selectedDescriptor;
        public MotionDescriptor? SelectedDescriptor
        {
            get => _selectedDescriptor;
            set
            {
                if (!SetProperty(ref _selectedDescriptor, value))
                    return;

                StopJog("切换了运动卡");
                ReloadAxesAndIo();
                RaisePropertyChanged(nameof(HasCard));
                RaisePropertyChanged(nameof(CardName));
            }
        }

        public bool HasCard => _selectedDescriptor != null;
        public string CardName => _selectedDescriptor?.Caption ?? string.Empty;

        /// <summary>连接胶囊文字（只读徽标，S2-4：不做开关）</summary>
        public string CardStateText
        {
            get
            {
                var device = CurrentDevice;
                return device?.State switch
                {
                    MotionCardState.Online => "已连接",
                    MotionCardState.Alarm => "已连接",
                    MotionCardState.Connecting => "连接中",
                    MotionCardState.SafeStopped => "已安全停机",
                    _ => "未连接",
                } ?? "未连接";
            }
        }

        /// <summary>连接/断开按钮文字</summary>
        public string ConnectButtonText =>
            CurrentDevice is { State: MotionCardState.Online or MotionCardState.Alarm } ? "断开" : "连接";

        /// <summary>故障 chip 高亮（>0 整枚危险色）</summary>
        public bool HasFault => _shell.FaultCount > 0;

        /// <summary>「走到并等待到位」按钮文字（任一轴运动时禁用显示"运动中…"）</summary>
        public string WaitButtonText => AnyMoving ? "运动中…" : "走到并等待到位";

        /// <summary>「开始回零」按钮文字（回零中禁用显示"回零中…"）</summary>
        public string HomeButtonText => IsWaitingHome ? "回零中…" : "开始回零";

        public bool HasMovingHint => MovingHint.Length > 0;

        private IMotionDevice? CurrentDevice =>
            _selectedDescriptor != null && _provider.TryGetDevice(_selectedDescriptor.Id, out var device)
                ? device
                : null;

        private bool IsDeviceOnline => CurrentDevice is { State: MotionCardState.Online };

        #region 轴列表与选中轴

        public ObservableCollection<MotionDebugAxisRow> Axes { get; } = new();

        private MotionDebugAxisRow? _selectedAxis;
        public MotionDebugAxisRow? SelectedAxis
        {
            get => _selectedAxis;
            set
            {
                if (!SetProperty(ref _selectedAxis, value))
                    return;

                StopJog("切换了轴");
                RaisePropertyChanged(nameof(HasAxis));
                RaisePropertyChanged(nameof(CurrentAxisName));
                ReloadHomeModes();
                ResetTargetForAxis();
            }
        }

        public bool HasAxis => _selectedAxis != null;

        /// <summary>读数条：当前轴名（大字主色）</summary>
        public string CurrentAxisName => _selectedAxis?.LogicalName ?? "—";

        /// <summary>读数条：实际位置（等宽大字 F3）</summary>
        public string CurrentPositionText => $"{_selectedAxis?.PositionMm ?? 0:F3}";

        /// <summary>读数条右侧的该轴状态</summary>
        public string CurrentStateLabel => _selectedAxis?.StateLabel ?? "未选中轴";

        public string CurrentStateLevel => _selectedAxis?.StateLevel ?? "Neutral";

        #endregion

        #region 手动操作参数

        private string _jogSpeedText = "5";
        /// <summary>点动速度（mm/s）。0 = 使用卡内默认速度</summary>
        public string JogSpeedText
        {
            get => _jogSpeedText;
            set => SetProperty(ref _jogSpeedText, value);
        }

        private string _targetText = "0";
        public string TargetText
        {
            get => _targetText;
            set => SetProperty(ref _targetText, value);
        }

        private string _moveSpeedText = "0";
        /// <summary>定位速度（mm/s）。0 = 卡内默认</summary>
        public string MoveSpeedText
        {
            get => _moveSpeedText;
            set => SetProperty(ref _moveSpeedText, value);
        }

        private string _waitTimeoutText = "30000";
        public string WaitTimeoutText
        {
            get => _waitTimeoutText;
            set => SetProperty(ref _waitTimeoutText, value);
        }

        public ObservableCollection<HomeModeOption> HomeModes { get; } = new();

        private HomeModeOption? _selectedHomeMode;
        public HomeModeOption? SelectedHomeMode
        {
            get => _selectedHomeMode;
            set => SetProperty(ref _selectedHomeMode, value);
        }

        private bool _isWaitingHome;
        public bool IsWaitingHome
        {
            get => _isWaitingHome;
            private set => SetProperty(ref _isWaitingHome, value);
        }

        /// <summary>任一轴在动 → 「走到并等待到位」禁用显示"运动中…"</summary>
        public bool AnyMoving => Axes.Any(a => a.IsMoving);

        /// <summary>连接着且还没回零 → 顶部橙色 chip 常显</summary>
        public bool NeedHoming =>
            CurrentDevice is { State: MotionCardState.Online or MotionCardState.Alarm, IsHomed: false }
            && Axes.Count > 0;

        /// <summary>报警轴胶囊（取第一根报警轴的逻辑名）</summary>
        public string AlarmAxis =>
            Axes.FirstOrDefault(a => a.HasCriticalSignal && a.StateLabel == "报警")?.LogicalName ?? string.Empty;

        public bool HasAlarm => AlarmAxis.Length > 0;

        /// <summary>
        /// 条件提示条（规格 4.1）：「{轴} 正在{模式}中，可随时点「停止」中断」；空闲时为空（不显示）。
        /// </summary>
        public string MovingHint
        {
            get
            {
                var axis = _selectedAxis;
                if (axis == null) return string.Empty;
                _modeByAxis.TryGetValue(axis.LogicalName, out var mode);
                return string.IsNullOrWhiteSpace(mode)
                    ? string.Empty
                    : $"{axis.LogicalName} 正在{mode}中，可随时点「停止」中断";
            }
        }

        #endregion

        #region 卡上 IO

        public ObservableCollection<MotionDebugIoRow> Inputs { get; } = new();
        public ObservableCollection<MotionDebugIoRow> Outputs { get; } = new();

        /// <summary>IO 路数：连接后按能力，未连接按 8 铺出占位（界面不空白）</summary>
        private void ReloadIo()
        {
            var device = CurrentDevice;
            var inputCount = 8;
            var outputCount = 8;
            if (device is { State: MotionCardState.Online or MotionCardState.Alarm })
            {
                inputCount = Math.Max(0, device.Capabilities.DigitalInputCount);
                outputCount = Math.Max(0, device.Capabilities.DigitalOutputCount);
            }

            if (Inputs.Count != inputCount)
            {
                Inputs.Clear();
                for (var i = 0; i < inputCount; i++) Inputs.Add(new MotionDebugIoRow(i, isOutput: false));
            }

            if (Outputs.Count != outputCount)
            {
                Outputs.Clear();
                for (var i = 0; i < outputCount; i++) Outputs.Add(new MotionDebugIoRow(i, isOutput: true));
            }
        }

        #endregion

        #region 列表装载与刷新

        /// <summary>重建轴列表（选卡变化 / 连接后 / 从其它页签切回来）。数据源=启用的轴映射（S2-1 修复）
        ///
        /// ★ 必须在"切入本页签"时重扫：连接成功后「卡设置」会按机型能力把
        ///   <see cref="MotionDescriptor.Axes"/> 补齐到 32 行（EnsureAxes 只增不删），
        ///   而本页签的 Axes 是启动时的快照 —— 不重扫就会"明明连上了 32 轴，列表还是旧的/空的"。
        /// 重扫按轴名保持当前选中，不让用户选中的轴跳回第一个。</summary>
        public void ReloadAxesAndIo()
        {
            var keepName = SelectedAxis?.LogicalName;

            Axes.Clear();
            _modeByAxis.Clear();
            SelectedAxis = null;

            var descriptor = _selectedDescriptor;
            if (descriptor == null)
            {
                ReloadIo();
                return;
            }

            foreach (var mapping in descriptor.Axes.Where(a => a.Enabled))
                Axes.Add(new MotionDebugAxisRow(mapping));

            SelectedAxis = Axes.FirstOrDefault(a => a.LogicalName == keepName)
                           ?? Axes.FirstOrDefault();
            ReloadIo();
            RefreshRuntime();
        }

        /// <summary>「卡设置」里行内改了轴名：轴行 LogicalName 是 Mapping 的计算属性，补一次通知即可</summary>
        public void NotifyAxisNamesChanged()
        {
            foreach (var row in Axes)
                row.RaiseLogicalNameChanged();

            // 读数条大字轴名跟着当前选中轴走
            RaisePropertyChanged(nameof(CurrentAxisName));
        }

        /// <summary>回零方式下拉：未连接列契约全集，连接后按能力收窄（与旧面板同一策略）</summary>
        private void ReloadHomeModes()
        {
            var keep = _selectedHomeMode?.Mode ?? HomeMode.NegativeLimitIndex;
            var device = CurrentDevice;

            var modes = device is { Capabilities.SupportedHomeModes.Count: > 0 }
                ? device.Capabilities.SupportedHomeModes
                : MotionEnumDisplay.AllHomeModes;

            HomeModes.Clear();
            foreach (var mode in modes) HomeModes.Add(new HomeModeOption(mode));
            SelectedHomeMode = HomeModes.FirstOrDefault(o => o.Mode == keep) ?? HomeModes.FirstOrDefault();
        }

        /// <summary>定时器驱动的运行态刷新（200ms；只读轮询快照）</summary>
        public void RefreshRuntime()
        {
            var device = CurrentDevice;
            var online = device is { State: MotionCardState.Online or MotionCardState.Alarm };
            var homed = device?.IsHomed ?? false;

            foreach (var row in Axes)
            {
                _modeByAxis.TryGetValue(row.LogicalName, out var mode);
                var status = device?.GetAxisStatus(row.Mapping.PhysicalIndex);

                row.Update(status, row.Mapping.Enabled, online, homed, mode);

                // 到位即清"正在做什么"：卡只报 Moving，模式名靠这里收敛回空闲
                if (status is { Moving: false } && _modeByAxis.ContainsKey(row.LogicalName))
                    _modeByAxis.Remove(row.LogicalName);
            }

            if (device is { State: MotionCardState.Online or MotionCardState.Alarm })
            {
                foreach (var row in Inputs)
                    row.IsOn = device.ReadInput(row.Port);

                // 输出**故意不从设备刷新**：契约里没有 ReadOutput，刷也刷不出真值。
                // 输出行的值只由 ToggleOutputAsync 在"命令确认执行成功"后置位，
                // 所以它是"我方确认写入的值"，而不是对端子电平的断言。
            }

            RaisePropertyChanged(nameof(CurrentPositionText));
            RaisePropertyChanged(nameof(CurrentStateLabel));
            RaisePropertyChanged(nameof(CurrentStateLevel));
            RaisePropertyChanged(nameof(CurrentAxisName));
            RaisePropertyChanged(nameof(CardStateText));
            RaisePropertyChanged(nameof(ConnectButtonText));
            RaisePropertyChanged(nameof(HasFault));
            RaisePropertyChanged(nameof(WaitButtonText));
            RaisePropertyChanged(nameof(HomeButtonText));
            RaisePropertyChanged(nameof(AnyMoving));
            RaisePropertyChanged(nameof(NeedHoming));
            RaisePropertyChanged(nameof(HasAlarm));
            RaisePropertyChanged(nameof(AlarmAxis));
            RaisePropertyChanged(nameof(MovingHint));
            MoveAbsoluteAndWaitCommand.RaiseCanExecuteChanged();
        }

        /// <summary>外壳打开时启动点动看门狗</summary>
        public void OnShellOpened()
        {
            _jogWatchdog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _jogWatchdog.Tick += (_, _) => CheckJogWatchdog();
            _jogWatchdog.Start();
        }

        /// <summary>
        /// 外壳关闭：停看门狗 + 停点动 + 撤掉本面板自己发起、还没结束的命令。
        ///
        /// 点动必须停（它的终点是"松手"，关窗后不会有松手事件了）。
        /// 回零/等到位也必须撤：回零最长 120 秒，窗口关了命令还在队列里执行，
        /// 界面却已经没有地方显示它 —— 轴在没人看着的情况下走，比报错危险得多。
        ///
        /// 【为什么不是无条件 CancelAll】这张卡同时也在跑流程里的运动步骤；
        /// 无条件清空队列会把产线上正在执行的命令一起砍掉 —— 那才是真正的生产事故。
        /// 所以只撤"本面板发起且尚未结束"的那些。
        /// </summary>
        public void OnShellClosed()
        {
            _jogWatchdog?.Stop();
            _jogWatchdog = null;
            StopJog("调试面板已关闭");

            if (!IsWaitingHome && !IsWaitingArrival) return;

            var device = CurrentDevice;
            _modeByAxis.Clear();
            IsWaitingHome = false;
            IsWaitingArrival = false;
            MoveAbsoluteAndWaitCommand.RaiseCanExecuteChanged();

            try
            {
                device?.CancelAll("手动调试面板已关闭");
            }
            catch (Exception ex)
            {
                // 关窗路径上不再抛：这里唯一的后果是"没能撤掉"，日志留痕即可
                System.Diagnostics.Debug.WriteLine($"[MotionDebug] 关闭时取消命令失败：{ex.Message}");
            }
        }

        #endregion

        #region 命令

        public DelegateCommand ToggleConnectCommand { get; }
        public DelegateCommand ClearAlarmCommand { get; }
        public DelegateCommand EmergencyStopCommand { get; }
        public DelegateCommand EnableCommand { get; }
        public DelegateCommand DisableCommand { get; }
        public DelegateCommand StopAllCommand { get; }
        /// <summary>点动脉冲（HoldCommandBehavior 按住期间周期触发；参数 "+" / "-"）</summary>
        public DelegateCommand<string> PulseJogCommand { get; }
        public DelegateCommand StopJogCommand { get; }
        public DelegateCommand MoveAbsoluteCommand { get; }
        /// <summary>走到并等待到位：任一轴运动时禁用（按钮显示"运动中…"）</summary>
        public DelegateCommand MoveAbsoluteAndWaitCommand { get; }
        public DelegateCommand TakeCurrentPositionCommand { get; }
        public DelegateCommand HomeCommand { get; }
        public DelegateCommand<MotionDebugIoRow> ToggleOutputCommand { get; }

        private void ToggleConnect()
        {
            var device = CurrentDevice;
            if (device == null)
            {
                _shell.NotifyError("该卡没有运行态设备：请先到「卡设置」确认驱动已选、地址已填");
                return;
            }

            try
            {
                if (device.State is MotionCardState.Online or MotionCardState.Alarm)
                {
                    device.Disconnect();
                    _shell.NotifyOk($"「{device.Descriptor.Caption}」已断开");
                }
                else
                {
                    var ok = device.Connect();
                    if (ok)
                    {
                        // 弹"扫描到 N 个轴"（统一出口会顺带重建轴列表，下面的 ReloadAxesAndIo 幂等）
                        _shell.NotifyCardConnected(device.Descriptor.Caption, device.Capabilities.AxisCount);
                    }
                    else
                        _shell.NotifyError($"连接失败：{device.StateDetail}");
                }
            }
            catch (Exception ex)
            {
                _shell.NotifyError($"连接操作异常：{ex.Message}");
            }

            ReloadAxesAndIo();
        }

        private void EmergencyStop()
        {
            var device = CurrentDevice;
            if (device == null) return;

            var result = device.EmergencyStop();
            if (result == MotionCommandResult.Accepted)
            {
                _modeByAxis.Clear();
                _shell.NotifyOk("急停：所有轴已停止并失能");
            }
            else
            {
                _shell.NotifyError($"急停被拒绝：{device.LastFault?.Suggestion ?? device.StateDetail}");
            }
        }

        private void ClearAlarm()
        {
            var device = CurrentDevice;
            if (device == null) return;

            _shell.NotifyOk(device.ClearAlarm(out var error)
                ? "报警已清除"
                : $"清除报警失败：{error}");
        }

        private async Task SetEnableAsync(bool enable)
        {
            if (!TryGetOperatingAxis(out var device, out var axis)) return;

            var deviceGate = MotionCommandGate.CheckDevice(device);
            if (!deviceGate.Passed)
            {
                _shell.Reject(deviceGate.Reason);
                return;
            }

            var operation = enable ? "使能" : "失能";
            using var command = new MotionCommand
            {
                Kind = enable ? MotionCommandKind.Enable : MotionCommandKind.Disable,
                PhysicalAxis = axis.Mapping.PhysicalIndex,
                LogicalAxis = axis.LogicalName,
                WaitsForCompletion = true,
                Timeout = TimeSpan.FromMilliseconds(Math.Max(1000, device.Descriptor.Params.CommandTimeoutMs)),
            };

            // 绝不在 UI 线程同步等（队列里可能排着回零）：放线程池，界面保持可响应
            var result = device.Enqueue(command);
            if (result != MotionCommandResult.Accepted)
            {
                _shell.NotifyError($"{operation}命令被拒绝：{device.LastFault?.Suggestion ?? device.StateDetail}");
                return;
            }

            await Task.Run(() => command.Completion.Wait(command.Timeout));

            if (command.State != MotionCommandState.Done)
                _shell.NotifyError($"{operation}失败：{command.Error}");
        }

        private void StopAll()
        {
            var device = CurrentDevice;
            if (device == null) return;

            using var command = new MotionCommand
            {
                Kind = MotionCommandKind.Stop,
                PhysicalAxis = -1,
                StopMode = 2,   // 手动停止用减速停
                Retryable = false,
                Timeout = TimeSpan.FromMilliseconds(device.Descriptor.Params.CommandTimeoutMs),
            };

            device.Enqueue(command);
            _modeByAxis.Clear();
        }

        /// <summary>
        /// 点动脉冲：第一次按下走完整启动（四道闸），之后的重复脉冲只刷新心跳。
        /// 非空闲时的再次按下按规格"静默忽略"。
        /// </summary>
        private void PulseJog(string? direction)
        {
            var dir = direction == "-" ? -1 : 1;
            var axisName = _selectedAxis?.LogicalName ?? string.Empty;

            if (_jogDevice != null
                && string.Equals(_jogAxis, axisName, StringComparison.OrdinalIgnoreCase)
                && _jogDirection == dir)
            {
                _lastJogPulseUtc = DateTime.UtcNow;   // 按住期间的重复脉冲：只喂狗
                return;
            }

            StartJog(dir);
        }

        private int _jogDirection;

        private void StartJog(int dir)
        {
            if (!TryGetOperatingAxis(out var device, out var axis)) return;

            var status = device.GetAxisStatus(axis.Mapping.PhysicalIndex);

            // 点动与定位同一套闸门（含此前漏掉的"未回零"）；忙时按规格静默忽略
            var gate = MotionCommandGate.CheckJog(
                device, axis.Mapping, status, device.Capabilities.SupportsJog);

            if (!gate.Passed)
            {
                if (!gate.Silent) _shell.Reject(gate.Reason);
                return;
            }

            var speed = double.TryParse(JogSpeedText, out var js) && js > 0
                ? js
                : device.Descriptor.Params.DefaultVelocityMmPerS;

            _lastJogPulseUtc = DateTime.UtcNow;
            _jogDevice = device;
            _jogAxis = axis.LogicalName;
            _jogDirection = dir;
            _modeByAxis[axis.LogicalName] = "点动";

            using var command = new MotionCommand
            {
                Kind = MotionCommandKind.Jog,
                PhysicalAxis = axis.Mapping.PhysicalIndex,
                LogicalAxis = axis.LogicalName,
                JogDirection = dir,
                VelocityMmPerS = speed,
                Retryable = false,
                Timeout = TimeSpan.FromMilliseconds(device.Descriptor.Params.CommandTimeoutMs),
            };

            device.Enqueue(command);
        }

        private void StopJogFromUi() => StopJog("松开");

        /// <summary>点动结束（松开/切轴/切卡/看门狗/关窗）：全轴立即停 —— 切过轴后宁可多停</summary>
        private void StopJog(string reason)
        {
            var device = _jogDevice;
            _jogDevice = null;
            _jogAxis = string.Empty;
            _lastJogPulseUtc = DateTime.MinValue;

            if (device == null) return;

            using var command = new MotionCommand
            {
                Kind = MotionCommandKind.Stop,
                PhysicalAxis = -1,
                StopMode = 3,   // 立即停：点动速度低，立即停才停得准
                Retryable = false,
                Timeout = TimeSpan.FromMilliseconds(device.Descriptor.Params.CommandTimeoutMs),
            };

            device.Enqueue(command);

            if (_selectedAxis != null) _modeByAxis.Remove(_selectedAxis.LogicalName);
        }

        /// <summary>
        /// 点动看门狗：超过 500ms 没有界面脉冲就认为"界面已经不在按了"，自动停。
        /// 判断复用 MotionDebugViewModel.IsJogPulseTimedOut（安全逻辑只写一份）。
        /// </summary>
        private void CheckJogWatchdog()
        {
            if (_jogDevice == null) return;

            var timeoutMs = MotionJogGuard.PulseTimeoutMs;
            if (!MotionJogGuard.IsPulseTimedOut(DateTime.UtcNow, _lastJogPulseUtc, timeoutMs)) return;

            StopJog($"点动心跳超时（>{timeoutMs}ms 未收到界面脉冲）");
        }

        /// <summary>定位（wait=true 走到并等待到位 / false 走到不等待；闸门同一套）</summary>
        private async Task MoveAbsoluteAsync(bool waitForArrival)
        {
            if (!TryGetOperatingAxis(out var device, out var axis)) return;

            if (!double.TryParse(TargetText, out var target))
            {
                _shell.Reject(MotionCommandGate.InvalidTarget);
                return;
            }

            _modeByAxis.TryGetValue(axis.LogicalName, out var mode);
            var status = device.GetAxisStatus(axis.Mapping.PhysicalIndex);

            // 六道闸（未连接 → 目标非法 → 超软限位 → 未使能 → 未回零 → 忙）全部走同一实现。
            // 此前这里漏了软限位：配置界面能填软限位，运动路径却不校验，等于这道防线不存在。
            var gate = MotionCommandGate.CheckMove(device, axis.Mapping, status, target, mode);
            if (!gate.Passed)
            {
                _shell.Reject(gate.Reason);
                return;
            }

            var speed = double.TryParse(MoveSpeedText, out var ms) && ms > 0
                ? ms
                : device.Descriptor.Params.DefaultVelocityMmPerS;

            _modeByAxis[axis.LogicalName] = "定位";

            // MotionCommand 内部带 ManualResetEventSlim，必须 Dispose（下发即返回型的命令也要）
            using var command = new MotionCommand
            {
                Kind = MotionCommandKind.MoveAbsolute,
                PhysicalAxis = axis.Mapping.PhysicalIndex,
                LogicalAxis = axis.LogicalName,
                TargetMm = target,
                VelocityMmPerS = speed,
                Curve = MotionCurve.Trapezoid,
                Timeout = TimeSpan.FromMilliseconds(Math.Max(1000, device.Descriptor.Params.CommandTimeoutMs)),
            };

            var result = device.Enqueue(command);

            if (result != MotionCommandResult.Accepted)
            {
                _modeByAxis.Remove(axis.LogicalName);
                _shell.NotifyError($"定位命令被拒绝：{device.LastFault?.Suggestion ?? device.StateDetail}");
                return;
            }

            if (!int.TryParse(WaitTimeoutText, out var waitMs) || waitMs <= 0) waitMs = 30_000;

            StatusText = $"轴「{axis.LogicalName}」正在走向 {target:F3} mm …";
            if (!waitForArrival)
            {
                _shell.NotifyOk($"已下发：轴「{axis.LogicalName}」→ {target:F3} mm（未等待到位）");
                return;
            }

            IsWaitingArrival = true;
            try
            {
                var deadline = DateTime.UtcNow.AddMilliseconds(waitMs);
                while (DateTime.UtcNow < deadline)
                {
                    await Task.Delay(50);

                    var snapshot = device.GetAxisStatus(axis.Mapping.PhysicalIndex);
                    if (snapshot == null)
                    {
                        StatusText = "读不到轴状态（设备可能已断开）";
                        return;
                    }

                    if (snapshot.HasCriticalSignal)
                    {
                        StatusText = $"轴「{axis.LogicalName}」出现异常信号：{snapshot.Describe()}";
                        return;
                    }

                    if (snapshot.InPosition && !snapshot.Moving)
                    {
                        StatusText = $"轴「{axis.LogicalName}」已到位：{snapshot.PositionMm:F3} mm";
                        _modeByAxis.Remove(axis.LogicalName);
                        return;
                    }
                }

                StatusText = $"等待到位超时（>{waitMs}ms），当前位置 "
                             + $"{device.GetAxisStatus(axis.Mapping.PhysicalIndex)?.PositionMm:F3} mm";
            }
            finally
            {
                IsWaitingArrival = false;
                MoveAbsoluteAndWaitCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>把目标位置写成当前选中轴的实际位置（读不到时清空，见 <see cref="ResetTargetForAxis"/>）</summary>
        private void TakeCurrentPosition()
        {
            var status = CurrentDevice?.GetAxisStatus(_selectedAxis?.Mapping.PhysicalIndex ?? -1);
            if (status != null)
                TargetText = status.PositionMm.ToString("F3");
        }

        /// <summary>
        /// 切轴后的目标位置处理：读到就把当前位置填进去，读不到**必须清空**。
        ///
        /// 为什么不能沿用上一次的值：目标位置是"这根轴要去哪"，
        /// 沿用上一根轴的目标意味着 —— 未连接时切个轴再点「定位」，
        /// 这根轴会走向上一根轴的目标坐标。一旦连上就是一次误动作。
        /// </summary>
        private void ResetTargetForAxis()
        {
            var status = CurrentDevice?.GetAxisStatus(_selectedAxis?.Mapping.PhysicalIndex ?? -1);
            TargetText = status != null ? status.PositionMm.ToString("F3") : string.Empty;
        }

        private async Task HomeAsync()
        {
            if (!TryGetOperatingAxis(out var device, out var axis)) return;

            _modeByAxis.TryGetValue(axis.LogicalName, out var mode);
            var status = device.GetAxisStatus(axis.Mapping.PhysicalIndex);

            var gate = MotionCommandGate.CheckHome(device, axis.Mapping, status, mode);
            if (!gate.Passed)
            {
                _shell.Reject(gate.Reason);
                return;
            }

            var homeMode = SelectedHomeMode?.Mode ?? HomeMode.NegativeLimitIndex;
            if (!device.Capabilities.SupportsHomeMode(homeMode))
            {
                _shell.NotifyError($"该卡不支持回零方式「{MotionEnumDisplay.Text(homeMode)}」");
                return;
            }

            using var command = new MotionCommand
            {
                Kind = MotionCommandKind.Home,
                PhysicalAxis = axis.Mapping.PhysicalIndex,
                LogicalAxis = axis.LogicalName,
                HomeMode = homeMode,
                WaitsForCompletion = true,
                Retryable = false,
                Timeout = TimeSpan.FromMilliseconds(120_000),   // 回零慢是正常的，与「轴回零」步骤同一口径
            };

            if (device.Enqueue(command) != MotionCommandResult.Accepted)
            {
                _shell.NotifyError($"回零命令被拒绝：{device.LastFault?.Suggestion ?? device.StateDetail}");
                return;
            }

            if (homeMode == HomeMode.PresetZero)
            {
                await Task.Run(() => command.Completion.Wait(command.Timeout));
                if (command.State == MotionCommandState.Done)
                    _shell.NotifyOk($"{axis.LogicalName} 当前位置已置零");
                else
                    _shell.NotifyError($"回零失败：{command.Error}");
                return;
            }

            _modeByAxis[axis.LogicalName] = "回零";
            IsWaitingHome = true;
            StatusText = $"轴「{axis.LogicalName}」回零中（{MotionEnumDisplay.Text(homeMode)}）…";

            var completed = await Task.Run(() => command.Completion.Wait(command.Timeout));
            IsWaitingHome = false;

            if (!completed)
                StatusText = $"回零超时（>120s）";
            else if (command.State == MotionCommandState.Done)
            {
                StatusText = $"轴「{axis.LogicalName}」回零完成";
                _modeByAxis.Remove(axis.LogicalName);
            }
            else
                StatusText = $"回零失败（{command.State}）：{command.Error}";
        }

        /// <summary>
        /// 写输出。**灯只在确认写成功之后才亮**。
        ///
        /// 以前是"下发即点亮"（乐观置位），而刷新只刷输入、不刷输出，
        /// 于是那盏灯显示的永远是"我点过什么"而不是"写成功了没有" ——
        /// 端口越界、命令被拒、卡侧写失败，灯都照样亮着，而且永远不会自愈。
        /// 现在改成等命令真正执行完再按结果决定是否置位：
        /// 灯的含义从"我点过"变成"我方确认已写入"。
        ///
        /// 【它保证不了什么】外部（PLC / HMI / 卡上别的程序）改了这个输出我们不知道 ——
        /// 要知道那个必须让驱动每轮回读（<c>IMotionDevice</c> 目前只有 <c>ReadInput</c>）。
        /// 所以这盏灯表示的是"我方确认写入的值"，不是"端子的实时电平"。
        /// </summary>
        private async Task ToggleOutputAsync(MotionDebugIoRow row)
        {
            var device = CurrentDevice;
            if (device == null) return;

            if (!_pendingIoPorts.Add(row.Port)) return;   // 上一次还没落定，忽略连点

            var next = !row.IsOn;

            try
            {
                using var command = new MotionCommand
                {
                    Kind = MotionCommandKind.SetOutput,
                    IoPort = row.Port,
                    IoValue = next,
                    // 刻意**不**设 WaitsForCompletion：那会让卡的**命令队列**停下来等这条写完，
                    // 把界面上的一次点击变成对流程命令的拖累。我们自己在线程池上等就够了 ——
                    // 基类对任何命令都会 Set 完成信号（与是否 WaitsForCompletion 无关）。
                    Retryable = false,
                    Timeout = TimeSpan.FromMilliseconds(device.Descriptor.Params.CommandTimeoutMs),
                };

                var result = device.Enqueue(command);
                if (result != MotionCommandResult.Accepted)
                {
                    _shell.NotifyError($"写输出 {row.Port} 被拒绝：{device.LastFault?.Suggestion ?? device.StateDetail}");
                    return;
                }

                // 在线程池上等它真正执行完（几十毫秒）；UI 线程不等 —— 等会把界面冻住
                var completed = await Task.Run(() => command.Completion.Wait(command.Timeout));

                if (!completed)
                {
                    _shell.NotifyError($"写输出 {row.Port} 超时（>{command.Timeout.TotalSeconds:F0}s），状态未改变");
                    return;
                }

                if (command.State != MotionCommandState.Done)
                {
                    _shell.NotifyError($"写输出 {row.Port} 失败（{command.State}）：{command.Error}");
                    return;
                }

                row.IsOn = next;
                StatusText = $"输出 {row.Port} 已写入 {(next ? "ON" : "OFF")}";
            }
            finally
            {
                _pendingIoPorts.Remove(row.Port);
            }
        }

        /// <summary>操作前提：选了轴且卡在线（不满足时给出规格文案的拒绝）</summary>
        private bool TryGetOperatingAxis(out IMotionDevice device, out MotionDebugAxisRow axis)
        {
            device = CurrentDevice!;
            axis = _selectedAxis!;

            if (device == null)
            {
                _shell.Reject(MotionCommandGate.NotConnected);
                return false;
            }

            if (axis == null)
            {
                _shell.NotifyError(MotionCommandGate.NoAxis);
                return false;
            }

            if (device.State != MotionCardState.Online)
            {
                _shell.Reject(MotionCommandGate.NotConnected);
                return false;
            }

            return true;
        }

        /// <summary>async void 兜底：命令处理器里的异常必须变成状态文字，不能带崩进程</summary>
        private async Task RunGuardedAsync(Func<Task> action, string operation)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                StatusText = $"{operation}过程中出现异常：{ex.Message}";
            }
        }

        private string _statusText = "选中一根轴后即可手动操作。点动为「按住才动、松手即停」。";
        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        /// <summary>「走到并等待到位」是否处于等待期（禁用第二起点）</summary>
        private bool _isWaitingArrival;
        public bool IsWaitingArrival
        {
            get => _isWaitingArrival;
            private set => SetProperty(ref _isWaitingArrival, value);
        }

        #endregion
    }
}
