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
            private set => SetProperty(ref _positionMm, value);
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
    ///   ② 点动心跳兜底（500ms 没有界面脉冲就自动停 —— 判断复用 MotionDebugViewModel
    ///      的静态纯函数，安全判断只有一份）；
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
            ToggleOutputCommand = new DelegateCommand<MotionDebugIoRow>(ToggleOutput);
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
                TakeCurrentPosition();
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

        /// <summary>重建轴列表（选卡变化 / 连接断开后）。数据源=启用的轴映射（S2-1 修复）</summary>
        public void ReloadAxesAndIo()
        {
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

            SelectedAxis = Axes.FirstOrDefault();
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

        /// <summary>外壳关闭：停看门狗 + 停点动（点动的终点是"松手"，关窗后不会有松手事件）</summary>
        public void OnShellClosed()
        {
            _jogWatchdog?.Stop();
            _jogWatchdog = null;
            StopJog("调试面板已关闭");
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
                    _shell.NotifyOk(device.Connect()
                        ? $"「{device.Descriptor.Caption}」已连接"
                        : $"连接失败：{device.StateDetail}");
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

            device.Enqueue(new MotionCommand
            {
                Kind = MotionCommandKind.Stop,
                PhysicalAxis = -1,
                StopMode = 2,   // 手动停止用减速停
                Retryable = false,
                Timeout = TimeSpan.FromMilliseconds(device.Descriptor.Params.CommandTimeoutMs),
            });
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
            if (status == null) return;
            if (!status.Enabled)
            {
                _shell.Reject($"{axis.LogicalName} 未使能，请先伺服使能");
                return;
            }

            if (status.Moving)
            {
                // 规格闸门表：非空闲 → 静默忽略
                return;
            }

            if (!device.Capabilities.SupportsJog)
            {
                _shell.NotifyError("该卡未上报点动能力，请改用「定位」逐点对位");
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

            device.Enqueue(new MotionCommand
            {
                Kind = MotionCommandKind.Jog,
                PhysicalAxis = axis.Mapping.PhysicalIndex,
                LogicalAxis = axis.LogicalName,
                JogDirection = dir,
                VelocityMmPerS = speed,
                Retryable = false,
                Timeout = TimeSpan.FromMilliseconds(device.Descriptor.Params.CommandTimeoutMs),
            });
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

            device.Enqueue(new MotionCommand
            {
                Kind = MotionCommandKind.Stop,
                PhysicalAxis = -1,
                StopMode = 3,   // 立即停：点动速度低，立即停才停得准
                Retryable = false,
                Timeout = TimeSpan.FromMilliseconds(device.Descriptor.Params.CommandTimeoutMs),
            });

            if (_selectedAxis != null) _modeByAxis.Remove(_selectedAxis.LogicalName);
        }

        /// <summary>
        /// 点动看门狗：超过 500ms 没有界面脉冲就认为"界面已经不在按了"，自动停。
        /// 判断复用 MotionDebugViewModel.IsJogPulseTimedOut（安全逻辑只写一份）。
        /// </summary>
        private void CheckJogWatchdog()
        {
            if (_jogDevice == null) return;

            var timeoutMs = MotionDebugViewModel.JogPulseTimeoutMs;
            if (!MotionDebugViewModel.IsJogPulseTimedOut(DateTime.UtcNow, _lastJogPulseUtc, timeoutMs)) return;

            StopJog($"点动心跳超时（>{timeoutMs}ms 未收到界面脉冲）");
        }

        /// <summary>定位（wait=true 走到并等待到位 / false 走到不等待；闸门同一套）</summary>
        private async Task MoveAbsoluteAsync(bool waitForArrival)
        {
            if (!TryGetOperatingAxis(out var device, out var axis)) return;

            // 闸门顺序逐字按规格 4.2：未连接 → 目标非法 → 未使能 → 未回零 → 忙
            if (!double.TryParse(TargetText, out var target) || !IsFinite(target))
            {
                _shell.Reject("目标坐标不是有效数字");
                return;
            }

            var status = device.GetAxisStatus(axis.Mapping.PhysicalIndex);
            if (status == null)
            {
                _shell.Reject("运动卡未连接");
                return;
            }

            if (!status.Enabled)
            {
                _shell.Reject($"{axis.LogicalName} 未使能，请先伺服使能");
                return;
            }

            if (!device.IsHomed)
            {
                _shell.Reject($"{axis.LogicalName} 需要回零后才能运动");
                return;
            }

            if (status.Moving)
            {
                _modeByAxis.TryGetValue(axis.LogicalName, out var mode);
                _shell.Reject($"{axis.LogicalName} 正在{(string.IsNullOrWhiteSpace(mode) ? "运动" : mode)}，请等待完成");
                return;
            }

            var speed = double.TryParse(MoveSpeedText, out var ms) && ms > 0
                ? ms
                : device.Descriptor.Params.DefaultVelocityMmPerS;

            _modeByAxis[axis.LogicalName] = "定位";
            var result = device.Enqueue(new MotionCommand
            {
                Kind = MotionCommandKind.MoveAbsolute,
                PhysicalAxis = axis.Mapping.PhysicalIndex,
                LogicalAxis = axis.LogicalName,
                TargetMm = target,
                VelocityMmPerS = speed,
                Curve = MotionCurve.Trapezoid,
                Timeout = TimeSpan.FromMilliseconds(Math.Max(1000, device.Descriptor.Params.CommandTimeoutMs)),
            });

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

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private void TakeCurrentPosition()
        {
            var status = CurrentDevice?.GetAxisStatus(_selectedAxis?.Mapping.PhysicalIndex ?? -1);
            if (status != null)
                TargetText = status.PositionMm.ToString("F3");
        }

        private async Task HomeAsync()
        {
            if (!TryGetOperatingAxis(out var device, out var axis)) return;

            var status = device.GetAxisStatus(axis.Mapping.PhysicalIndex);
            if (status is { Moving: true })
            {
                _modeByAxis.TryGetValue(axis.LogicalName, out var mode);
                _shell.Reject($"{axis.LogicalName} 正在{(string.IsNullOrWhiteSpace(mode) ? "运动" : mode)}，请等待完成");
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

        private void ToggleOutput(MotionDebugIoRow? row)
        {
            if (row == null || !row.IsOutput) return;

            var device = CurrentDevice;
            if (device == null) return;

            var next = !row.IsOn;
            var result = device.Enqueue(new MotionCommand
            {
                Kind = MotionCommandKind.SetOutput,
                IoPort = row.Port,
                IoValue = next,
                Retryable = false,
                Timeout = TimeSpan.FromMilliseconds(device.Descriptor.Params.CommandTimeoutMs),
            });

            if (result != MotionCommandResult.Accepted)
            {
                _shell.NotifyError($"写输出 {row.Port} 被拒绝：{device.LastFault?.Suggestion ?? device.StateDetail}");
                return;
            }

            row.IsOn = next;   // 乐观置位，随后由刷新收敛
        }

        /// <summary>操作前提：选了轴且卡在线（不满足时给出规格文案的拒绝）</summary>
        private bool TryGetOperatingAxis(out IMotionDevice device, out MotionDebugAxisRow axis)
        {
            device = CurrentDevice!;
            axis = _selectedAxis!;

            if (device == null)
            {
                _shell.Reject("运动卡未连接");
                return false;
            }

            if (axis == null)
            {
                _shell.NotifyError("请先在左侧选择一根轴");
                return false;
            }

            if (device.State != MotionCardState.Online)
            {
                _shell.Reject("运动卡未连接");
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
