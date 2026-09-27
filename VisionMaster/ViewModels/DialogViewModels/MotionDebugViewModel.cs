using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using Core.Interfaces;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 调试面板里的一个轴。
    ///
    /// 运动卡都是**一带多**（一张卡 4/8 根轴），所以调试界面的基本单位是"轴"而不是"卡"：
    /// 同一屏里要把每根轴的位置与状态并排显示出来，才能一眼看出哪根轴报警、哪根还在动。
    /// </summary>
    public sealed class MotionDebugAxisRow : BindableBase
    {
        public MotionDebugAxisRow(AxisMapping mapping)
        {
            PhysicalIndex = mapping.PhysicalIndex;
            LogicalName = string.IsNullOrWhiteSpace(mapping.LogicalName)
                ? $"轴{mapping.PhysicalIndex}"
                : mapping.LogicalName;
            SoftLimitMinMm = mapping.SoftLimitMinMm;
            SoftLimitMaxMm = mapping.SoftLimitMaxMm;
        }

        /// <summary>卡内物理轴号（下发命令用）</summary>
        public int PhysicalIndex { get; }

        /// <summary>逻辑轴名（人在流程与界面上看到的名字）</summary>
        public string LogicalName { get; }

        public double SoftLimitMinMm { get; }
        public double SoftLimitMaxMm { get; }

        private double _positionMm;
        public double PositionMm
        {
            get => _positionMm;
            private set => SetProperty(ref _positionMm, value);
        }

        public string PositionText => $"{PositionMm:F3} mm";

        private bool _enabled;
        public bool Enabled { get => _enabled; private set => SetProperty(ref _enabled, value); }

        private bool _moving;
        public bool Moving { get => _moving; private set => SetProperty(ref _moving, value); }

        private bool _inPosition;
        public bool InPosition { get => _inPosition; private set => SetProperty(ref _inPosition, value); }

        private bool _alarm;
        public bool Alarm { get => _alarm; private set => SetProperty(ref _alarm, value); }

        private bool _positiveLimit;
        public bool PositiveLimit { get => _positiveLimit; private set => SetProperty(ref _positiveLimit, value); }

        private bool _negativeLimit;
        public bool NegativeLimit { get => _negativeLimit; private set => SetProperty(ref _negativeLimit, value); }

        private string _stateText = "—";
        public string StateText { get => _stateText; private set => SetProperty(ref _stateText, value); }

        private string _stateLabel = "未知";
        /// <summary>状态的短标签（列表里显示；<see cref="StateText"/> 是详细描述，留给悬停提示）</summary>
        public string StateLabel { get => _stateLabel; private set => SetProperty(ref _stateLabel, value); }

        private string _stateLevel = "Neutral";
        /// <summary>
        /// 状态的语义级别：Neutral / Accent / Success / Warning / Danger。
        ///
        /// 为什么把"级别"和"文字"分开给：颜色必须由**语义**决定，
        /// 而不是让视图去判断"报警该用什么色"—— 那样每加一种状态就要改视图，
        /// 且容易出现"同一个报警在两处颜色不同"。
        /// </summary>
        public string StateLevel { get => _stateLevel; private set => SetProperty(ref _stateLevel, value); }

        /// <summary>有需要立刻停下并人工处理的信号（报警/限位）</summary>
        public bool HasCriticalSignal => Alarm || PositiveLimit || NegativeLimit;

        /// <summary>用一次状态快照刷新本行（快照来自驱动轮询，不额外占用通信）</summary>
        public void Update(AxisStatus? status)
        {
            if (status == null)
            {
                StateText = "无状态（设备未连接）";
                return;
            }

            PositionMm = status.PositionMm;
            Enabled = status.Enabled;
            Moving = status.Moving;
            InPosition = status.InPosition;
            Alarm = status.Alarm;
            PositiveLimit = status.PositiveLimit;
            NegativeLimit = status.NegativeLimit;
            StateText = status.Describe();

            // 优先级 = 严重度（报警 > 限位 > 运动中 > 未使能 > 到位）：列表里只能显示一个标签，
            // 必须显示"最该被看到"的那个，否则报警会被"运动中"盖掉
            (StateLabel, StateLevel) = Alarm ? ("报警", "Danger")
                : PositiveLimit ? ("正限位", "Warning")
                : NegativeLimit ? ("负限位", "Warning")
                : Moving ? ("运动中", "Accent")
                : !Enabled ? ("未使能", "Neutral")
                : InPosition ? ("到位", "Success")
                : ("待机", "Neutral");

            RaisePropertyChanged(nameof(PositionText));
            RaisePropertyChanged(nameof(HasCriticalSignal));
        }
    }

    /// <summary>
    /// 回零方式的下拉项。
    ///
    /// 为什么不直接把枚举塞进 ItemsSource：
    ///   ① 枚举的 <c>ToString()</c> 出来的是成员名（<c>NegativeLimitIndex</c>），
    ///      操作员看不懂 —— 包装一层才能挂中文名与一句话说明；
    ///   ② 下拉的选中项绑到**引用类型**比绑到值类型稳：
    ///      列表被清空/重填时，值类型可能出现"选中项落不到列表上"的边界情况
    ///      （现场那张"下拉空白 + 红框"的截图就是从这个方向查起的）。
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

    /// <summary>调试面板里的一个 IO 点</summary>
    public sealed class MotionDebugIoRow : BindableBase
    {
        public MotionDebugIoRow(int port, bool isOutput)
        {
            Port = port;
            IsOutput = isOutput;
        }

        public int Port { get; }

        public bool IsOutput { get; }

        public string Label => $"{(IsOutput ? "输出" : "输入")} {Port}";

        private bool _isOn;
        public bool IsOn { get => _isOn; set => SetProperty(ref _isOn, value); }
    }

    /// <summary>可选的卡（下拉项）</summary>
    public sealed class MotionCardOption
    {
        public MotionCardOption(MotionDescriptor descriptor) => Descriptor = descriptor;

        public MotionDescriptor Descriptor { get; }

        public Guid Id => Descriptor.Id;

        public string Caption => Descriptor.Caption;

        public override string ToString() => Caption;
    }

    /// <summary>
    /// 运动卡调试面板（手动操作）。
    ///
    /// 与「运动卡设置」的分工：
    ///   · 设置弹窗 = 配置（驱动/地址/轴映射/参数），低频、改动要谨慎；
    ///   · 本面板  = **操作**（使能 / 点动 / 定位 / 回零 / IO），高频、现场天天用。
    /// 分开的理由很实际：调试时要盯着位置与状态，屏幕上塞满配置项会碍事；
    /// 而配置时也不希望手一抖点到"急停"旁边的东西。工业软件普遍这么分。
    ///
    /// **本面板的三条安全设计**（都是"手动操作"特有的风险）：
    ///   ① 点动"按住才动"：由 UI 的 HoldCommandBehavior 负责按下/松开，见其注释；
    ///   ② **点动心跳兜底**：万一界面事件丢了（失焦、卡顿、行为失效），
    ///      本 VM 的看门狗发现超过 <see cref="JogPulseTimeoutMs"/> 没有脉冲就自动停 ——
    ///      安全不能只赌界面事件一定会到达；
    ///   ③ 关窗必停点动：点动的终点是"松手"，而"关了面板"意味着不会有松手事件了，
    ///      所以必须在这里显式停掉（定位类运动有终点，不必停，让它走完）。
    /// </summary>
    public class MotionDebugViewModel : BindableBase, IDialogAware
    {
        /// <summary>点动心跳超时（ms）：按住期间 UI 每 150ms 会重复一次，这里给 3 倍余量</summary>
        public const int JogPulseTimeoutMs = 500;

        private readonly MotionProvider _provider;
        private readonly IWorkspaceManager _workspace;

        private DispatcherTimer? _refreshTimer;
        private DispatcherTimer? _jogWatchdog;

        /// <summary>最近一次点动脉冲时刻（UI 按住期间持续刷新）</summary>
        private DateTime _lastJogPulseUtc = DateTime.MinValue;

        /// <summary>当前正在点动的卡（看门狗据此决定停谁）</summary>
        private IMotionDevice? _jogDevice;

        public MotionDebugViewModel(MotionProvider provider, IWorkspaceManager workspace)
        {
            _provider = provider;
            _workspace = workspace;

            ConnectCommand = new DelegateCommand(ToggleConnect);
            EmergencyStopCommand = new DelegateCommand(EmergencyStop);
            ClearAlarmCommand = new DelegateCommand(ClearAlarm);
            EnableCommand = new DelegateCommand(() => SetEnable(true));
            DisableCommand = new DelegateCommand(() => SetEnable(false));
            GoToCommand = new DelegateCommand(() => _ = RunGuardedAsync(() => GoToAsync(waitForArrival: false), "定位"));
            GoToAndWaitCommand = new DelegateCommand(async () => await RunGuardedAsync(() => GoToAsync(waitForArrival: true), "定位"));
            HomeCommand = new DelegateCommand(async () => await RunGuardedAsync(HomeAsync, "回零"));
            StopCommand = new DelegateCommand(() => StopAxis("手动停止"));
            StartJogCommand = new DelegateCommand<object?>(StartJog);
            StopJogCommand = new DelegateCommand(() => StopJog("松开"));
            UseCurrentPositionCommand = new DelegateCommand(UseCurrentPosition);
            ToggleOutputCommand = new DelegateCommand<MotionDebugIoRow>(ToggleOutput);
            CloseCommand = new DelegateCommand(() => RequestClose.Invoke(new DialogResult(ButtonResult.OK)));
        }

        #region 对话框契约

        public string Title => "运动卡调试";

        public DialogCloseListener RequestClose { get; set; }

        public bool CanCloseDialog() => true;

        public void OnDialogOpened(IDialogParameters parameters)
        {
            _provider.EnsureSynced();
            ReloadCards();

            // 刷新用 200ms：读的是驱动的**轮询快照**，不额外占用通信，
            // 所以界面刷新频率与卡的通信负担无关（这一点在多点位调试时很关键）
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _refreshTimer.Tick += (_, _) => RefreshRuntime();
            _refreshTimer.Start();

            _jogWatchdog = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _jogWatchdog.Tick += (_, _) => CheckJogWatchdog();
            _jogWatchdog.Start();
        }

        public void OnDialogClosed()
        {
            // 关窗时点动必须停：点动的终点是"松手"，而面板都关了就不会再有松手事件
            StopJog("调试面板已关闭");

            _refreshTimer?.Stop();
            _jogWatchdog?.Stop();
            _refreshTimer = null;
            _jogWatchdog = null;
        }

        #endregion

        #region 卡与轴

        public ObservableCollection<MotionCardOption> Cards { get; } = new();

        private MotionCardOption? _selectedCard;
        public MotionCardOption? SelectedCard
        {
            get => _selectedCard;
            set
            {
                if (!SetProperty(ref _selectedCard, value)) return;
                StopJog("切换了运动卡");
                ReloadAxesAndIo();
                RaisePropertyChanged(nameof(HasCard));
                RaisePropertyChanged(nameof(CardStateText));
            }
        }

        public bool HasCard => _selectedCard != null;

        public ObservableCollection<MotionDebugAxisRow> Axes { get; } = new();

        private MotionDebugAxisRow? _selectedAxis;
        public MotionDebugAxisRow? SelectedAxis
        {
            get => _selectedAxis;
            set
            {
                if (!SetProperty(ref _selectedAxis, value)) return;
                StopJog("切换了轴");
                RaisePropertyChanged(nameof(HasAxis));
                RaisePropertyChanged(nameof(CanJog));
                UseCurrentPosition();
                ReloadHomeModes();
            }
        }

        public bool HasAxis => _selectedAxis != null;

        /// <summary>能否点动（卡上报了 Jog 能力、且轴已选中）</summary>
        public bool CanJog => HasAxis && CurrentDevice?.Capabilities.SupportsJog == true;

        public ObservableCollection<MotionDebugIoRow> Inputs { get; } = new();

        public ObservableCollection<MotionDebugIoRow> Outputs { get; } = new();

        private IMotionDevice? CurrentDevice
        {
            get
            {
                if (_selectedCard == null) return null;
                return _provider.TryGetDevice(_selectedCard.Id, out var device) ? device : null;
            }
        }

        private string _cardStateText = "未选择运动卡";
        /// <summary>完整说明（卡名 + 驱动的连接详情），给工具条信息的悬停提示用</summary>
        public string CardStateText
        {
            get => _cardStateText;
            private set => SetProperty(ref _cardStateText, value);
        }

        // ── 工具条上的信息 chips ──
        // 拆成几个结构化属性而不是拼一大段文字：界面上是四个并排的标签，
        // 拼成一串的话换行位置不可控、状态色也没法单独给。

        private string _cardModelText = "—";
        /// <summary>机型（如 VPLC532R）</summary>
        public string CardModelText { get => _cardModelText; private set => SetProperty(ref _cardModelText, value); }

        private string _cardStateLabel = "无设备";
        /// <summary>连接状态短标签</summary>
        public string CardStateLabel { get => _cardStateLabel; private set => SetProperty(ref _cardStateLabel, value); }

        private string _cardStateLevel = "Neutral";
        /// <summary>连接状态的语义级别（Neutral/Accent/Success/Warning/Danger，决定 chip 配色）</summary>
        public string CardStateLevel { get => _cardStateLevel; private set => SetProperty(ref _cardStateLevel, value); }

        private string _cardAxisText = string.Empty;
        /// <summary>轴数（如 "32 轴"）</summary>
        public string CardAxisText { get => _cardAxisText; private set => SetProperty(ref _cardAxisText, value); }

        private string _cardIoText = string.Empty;
        /// <summary>IO 点数（如 "IO 24 入 / 16 出"）</summary>
        public string CardIoText { get => _cardIoText; private set => SetProperty(ref _cardIoText, value); }

        private bool _isIoCountApproximate;
        /// <summary>IO 点数是显示上限而非核实值（界面要如实标注，别让它看起来像确定值）</summary>
        public bool IsIoCountApproximate { get => _isIoCountApproximate; private set => SetProperty(ref _isIoCountApproximate, value); }

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            private set => SetProperty(ref _isConnected, value);
        }

        #endregion

        #region 手动操作参数

        private double _jogVelocity = 5;
        /// <summary>点动速度（mm/s）。默认给得很慢：点动是"对位"，快了点不准还危险</summary>
        public double JogVelocity
        {
            get => _jogVelocity;
            set => SetProperty(ref _jogVelocity, value);
        }

        private double _targetPosition;
        public double TargetPosition
        {
            get => _targetPosition;
            set => SetProperty(ref _targetPosition, value);
        }

        private double _moveVelocity;
        /// <summary>定位速度（mm/s）。0 = 用卡级默认速度</summary>
        public double MoveVelocity
        {
            get => _moveVelocity;
            set => SetProperty(ref _moveVelocity, value);
        }

        private int _moveTimeoutMs = 30_000;
        public int MoveTimeoutMs
        {
            get => _moveTimeoutMs;
            set => SetProperty(ref _moveTimeoutMs, value);
        }

        public ObservableCollection<HomeModeOption> HomeModes { get; } = new();

        private HomeModeOption? _selectedHomeMode;
        public HomeModeOption? SelectedHomeMode
        {
            get => _selectedHomeMode;
            set => SetProperty(ref _selectedHomeMode, value);
        }

        private string _statusHint = "选中一张卡与一根轴后即可手动操作。点动为「按住才动、松手即停」。";
        public string StatusHint
        {
            get => _statusHint;
            private set => SetProperty(ref _statusHint, value);
        }

        #endregion

        #region 命令

        public DelegateCommand ConnectCommand { get; }
        public DelegateCommand EmergencyStopCommand { get; }
        public DelegateCommand ClearAlarmCommand { get; }
        public DelegateCommand EnableCommand { get; }
        public DelegateCommand DisableCommand { get; }
        public DelegateCommand GoToCommand { get; }
        public DelegateCommand GoToAndWaitCommand { get; }
        public DelegateCommand HomeCommand { get; }
        public DelegateCommand StopCommand { get; }
        public DelegateCommand<object?> StartJogCommand { get; }
        public DelegateCommand StopJogCommand { get; }
        public DelegateCommand UseCurrentPositionCommand { get; }
        public DelegateCommand<MotionDebugIoRow> ToggleOutputCommand { get; }
        public DelegateCommand CloseCommand { get; }

        #endregion

        #region 列表装载与刷新

        /// <summary>重建卡下拉集合（公开：外壳合并窗口桥接时先调它，理由见 MotionSettingsViewModel.ReloadRows）</summary>
        public void ReloadCards()
        {
            var keep = _selectedCard?.Id;
            Cards.Clear();
            foreach (var descriptor in _workspace.CurrentSolution?.MotionCards ?? new ObservableCollection<MotionDescriptor>())
                Cards.Add(new MotionCardOption(descriptor));

            SelectedCard = Cards.FirstOrDefault(c => c.Id == keep) ?? Cards.FirstOrDefault();
        }

        private void ReloadAxesAndIo()
        {
            Axes.Clear();
            Inputs.Clear();
            Outputs.Clear();
            SelectedAxis = null;

            var device = CurrentDevice;
            if (device == null)
            {
                CardStateText = "该卡没有运行态设备（驱动未加载或配置未对齐）";
                IsConnected = false;
                return;
            }

            foreach (var mapping in device.Descriptor.Axes.Where(a => a.Enabled))
                Axes.Add(new MotionDebugAxisRow(mapping));

            var capabilities = device.Capabilities;
            for (var i = 0; i < Math.Max(0, capabilities.DigitalInputCount); i++) Inputs.Add(new MotionDebugIoRow(i, false));
            for (var i = 0; i < Math.Max(0, capabilities.DigitalOutputCount); i++) Outputs.Add(new MotionDebugIoRow(i, true));

            SelectedAxis = Axes.FirstOrDefault();
            RefreshRuntime();
        }

        private void RefreshRuntime()
        {
            var device = CurrentDevice;
            if (device == null)
            {
                IsConnected = false;
                CardModelText = "—";
                CardStateLabel = "无设备";
                CardStateLevel = "Neutral";
                CardAxisText = string.Empty;
                CardIoText = string.Empty;
                IsIoCountApproximate = false;
                CardStateText = "未选择运动卡 / 该卡没有运行态设备（驱动未加载或配置未对齐）";

                foreach (var row in Axes) row.Update(null);
                return;
            }

            var capabilities = device.Capabilities;

            IsConnected = device.State is MotionCardState.Online or MotionCardState.Alarm;
            CardModelText = string.IsNullOrWhiteSpace(device.ModelName) ? "未知机型" : device.ModelName;
            CardStateLabel = device.StateTextOf();
            CardStateLevel = device.State switch
            {
                MotionCardState.Online => "Success",
                MotionCardState.Alarm => "Danger",
                MotionCardState.Connecting => "Accent",
                MotionCardState.SafeStopped => "Warning",
                _ => "Neutral",
            };
            CardAxisText = capabilities.AxisCount > 0 ? $"{capabilities.AxisCount} 轴" : string.Empty;
            CardIoText = capabilities.DigitalInputCount > 0 || capabilities.DigitalOutputCount > 0
                ? $"IO {capabilities.DigitalInputCount} 入 / {capabilities.DigitalOutputCount} 出"
                : string.Empty;
            IsIoCountApproximate = capabilities.IsIoCountApproximate;
            CardStateText = $"{device.Descriptor.Caption}\n{device.StateDetail}";

            foreach (var row in Axes) row.Update(device.GetAxisStatus(row.PhysicalIndex));
            foreach (var row in Inputs) row.IsOn = device.ReadInput(row.Port);

            RaisePropertyChanged(nameof(CanJog));
        }

        /// <summary>
        /// 重建回零方式下拉。
        ///
        /// 未连接时列**契约里的全部方式**，而不是留空：
        ///   ① 空白下拉会让人以为"这功能不可用"，而实际只是还没连卡；
        ///   ② 连上之后按能力收窄（只留卡上报支持的），原先的选择若被剔除会自动落到第一项。
        /// </summary>
        private void ReloadHomeModes()
        {
            var keep = _selectedHomeMode?.Mode ?? HomeMode.NegativeLimitIndex;
            var device = CurrentDevice;

            var modes = device is { Capabilities.SupportedHomeModes.Count: > 0 }
                ? device.Capabilities.SupportedHomeModes
                : MotionEnumDisplay.AllHomeModes;

            HomeModes.Clear();
            foreach (var mode in modes) HomeModes.Add(new HomeModeOption(mode));

            // 选中项永远落在列表里（列表本身也不会为空），避免出现"下拉空白"的中间态
            SelectedHomeMode = HomeModes.FirstOrDefault(o => o.Mode == keep) ?? HomeModes.FirstOrDefault();
        }

        #endregion

        #region 手动操作实现

        private void ToggleConnect()
        {
            var device = CurrentDevice;
            if (device == null)
            {
                StatusHint = "该卡没有运行态设备：请先到「运动卡设置」确认驱动已选、地址已填，且已保存到方案";
                return;
            }

            try
            {
                if (device.State is MotionCardState.Online or MotionCardState.Alarm)
                {
                    device.Disconnect();
                    StatusHint = $"已断开「{device.Descriptor.Caption}」";
                }
                else
                {
                    StatusHint = device.Connect()
                        ? $"已连接「{device.Descriptor.Caption}」"
                        : $"连接失败：{device.StateDetail}";
                }
            }
            catch (Exception ex)
            {
                StatusHint = $"连接操作异常：{ex.Message}";
            }

            RefreshRuntime();
            ReloadAxesAndIo();
        }

        private void EmergencyStop()
        {
            var device = CurrentDevice;
            if (device == null) return;

            var result = device.EmergencyStop();
            StatusHint = result == MotionCommandResult.Accepted
                ? "已下发急停（清空待执行命令并立即停止全部轴）——注意软件急停不能替代硬件安全回路"
                : $"急停被拒绝（{result}）：{device.StateDetail}";
        }

        private void ClearAlarm()
        {
            var device = CurrentDevice;
            if (device == null) return;

            StatusHint = device.ClearAlarm(out var error)
                ? "已清除报警。若伺服需要重新使能，请再点「使能」"
                : $"清除报警失败：{error}";
        }

        /// <summary>
        /// 异常兜底包装。
        ///
        /// 【为什么必须有】这些命令处理器是 <c>async void</c>（Prism 的 DelegateCommand 不会 await 它），
        /// 而 **async void 里抛出的异常没有接收者，会直接把进程带走**。
        /// 手动操作面板最怕这个：一次意外的通信/IO 异常就能让整个程序消失，
        /// 现场只会看到"点了一下就没了"（连异常都来不及显示）。
        /// </summary>
        private async Task RunGuardedAsync(Func<Task> action, string operation)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                StatusHint = $"{operation}过程中出现异常：{ex.Message}";
            }
        }

        private async void SetEnable(bool enable)
        {
            if (!TryGetAxis(out var device, out var axis)) return;

            var operation = enable ? "使能" : "失能";

            using var command = new MotionCommand
            {
                Kind = enable ? MotionCommandKind.Enable : MotionCommandKind.Disable,
                PhysicalAxis = axis.PhysicalIndex,
                LogicalAxis = axis.LogicalName,
                WaitsForCompletion = true,
                Timeout = TimeSpan.FromMilliseconds(Math.Max(1000, device.Descriptor.Params.CommandTimeoutMs)),
            };

            StatusHint = $"{operation}中…（若前方有回零等长命令，会先把它打断）";

            // ★ 绝不能在这里同步 Wait：
            //   使能/失能要等命令队列，而队列里可能正排着一个回零（最长 120 秒）——
            //   在 UI 线程上 Wait 会把界面冻住那么久，用户看到的就是"点一下整个界面卡死"（实测反馈）。
            //   放到线程池上等，界面保持可响应（用户至少还能看到状态提示、还能点急停）。
            try
            {
                StatusHint = await Task.Run(() => SendAndWait(device, command, operation));
            }
            catch (Exception ex)
            {
                // async void 的异常会直接崩进程，这里必须兜住
                StatusHint = $"{operation}过程中出现异常：{ex.Message}";
            }
        }

        private void GoTo(bool waitForArrival) => _ = GoToAsync(waitForArrival);

        /// <summary>
        /// 定位（走到指定位置）。
        /// <paramref name="waitForArrival"/> = true 时等到位再返回（调试时更常用：
        /// 点一下就能看到"它到底有没有走到"，而不是下发了就没了下文）。
        /// </summary>
        private async Task GoToAsync(bool waitForArrival)
        {
            if (!TryGetAxis(out var device, out var axis)) return;

            var target = TargetPosition;
            using var command = new MotionCommand
            {
                Kind = MotionCommandKind.MoveAbsolute,
                PhysicalAxis = axis.PhysicalIndex,
                LogicalAxis = axis.LogicalName,
                TargetMm = target,
                VelocityMmPerS = MoveVelocity,
                Timeout = TimeSpan.FromMilliseconds(Math.Max(1000, device.Descriptor.Params.CommandTimeoutMs)),
            };

            var result = device.Enqueue(command);
            if (result != MotionCommandResult.Accepted)
            {
                StatusHint = $"定位命令被拒绝（{result}）："
                             + (device.LastFault?.Suggestion ?? device.StateDetail);
                return;
            }

            if (!waitForArrival)
            {
                StatusHint = $"已下发：轴「{axis.LogicalName}」→ {target:F3} mm"
                             + "（未等待到位；要等它走完请用「走到并等待」）";
                return;
            }

            StatusHint = $"轴「{axis.LogicalName}」正在走向 {target:F3} mm …";
            var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(500, MoveTimeoutMs));

            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);

                var status = device.GetAxisStatus(axis.PhysicalIndex);
                if (status == null)
                {
                    StatusHint = "读不到轴状态（设备可能已断开）";
                    return;
                }

                if (status.HasCriticalSignal)
                {
                    StatusHint = $"轴「{axis.LogicalName}」出现异常信号：{status.Describe()}"
                                 + (string.IsNullOrWhiteSpace(device.LastFault?.Suggestion)
                                     ? string.Empty
                                     : $"（建议：{device.LastFault!.Suggestion}）");
                    return;
                }

                if (status.InPosition && !status.Moving)
                {
                    StatusHint = $"轴「{axis.LogicalName}」已到位：{status.PositionMm:F3} mm";
                    return;
                }
            }

            StatusHint = $"等待到位超时（>{MoveTimeoutMs}ms），当前位置 "
                         + $"{device.GetAxisStatus(axis.PhysicalIndex)?.PositionMm:F3} mm";
        }

        private async Task HomeAsync()
        {
            if (!TryGetAxis(out var device, out var axis)) return;

            var homeMode = SelectedHomeMode?.Mode ?? HomeMode.NegativeLimitIndex;

            if (!device.Capabilities.SupportsHomeMode(homeMode))
            {
                StatusHint = $"该卡不支持回零方式「{MotionEnumDisplay.Text(homeMode)}」";
                return;
            }

            using var command = new MotionCommand
            {
                Kind = MotionCommandKind.Home,
                PhysicalAxis = axis.PhysicalIndex,
                LogicalAxis = axis.LogicalName,
                HomeMode = homeMode,
                WaitsForCompletion = true,
                Retryable = false,
                // 回零慢是正常的，给足时间（与流程里的「轴回零」步骤同一口径）
                Timeout = TimeSpan.FromMilliseconds(120_000),
            };

            if (device.Enqueue(command) != MotionCommandResult.Accepted)
            {
                StatusHint = $"回零命令被拒绝：{device.LastFault?.Suggestion ?? device.StateDetail}";
                return;
            }

            StatusHint = $"轴「{axis.LogicalName}」回零中（{MotionEnumDisplay.Text(homeMode)}）…";
            var completed = await Task.Run(() => command.Completion.Wait(command.Timeout));

            StatusHint = !completed
                ? $"回零超时（>{command.Timeout.TotalSeconds:F0}s）"
                : command.State == MotionCommandState.Done
                    ? $"轴「{axis.LogicalName}」回零完成"
                    : $"回零失败（{command.State}）：{command.Error}";
        }

        private void StopAxis(string reason)
        {
            if (!TryGetAxis(out var device, out var axis)) return;

            using var command = new MotionCommand
            {
                Kind = MotionCommandKind.Stop,
                PhysicalAxis = axis.PhysicalIndex,
                LogicalAxis = axis.LogicalName,
                StopMode = 2,   // 手动停止用减速停
                WaitsForCompletion = false,
                Retryable = false,
                Timeout = TimeSpan.FromMilliseconds(device.Descriptor.Params.CommandTimeoutMs),
            };

            var result = device.Enqueue(command);
            StatusHint = result == MotionCommandResult.Accepted
                ? $"已下发停止（{reason}）"
                : $"停止命令被拒绝（{result}）";
        }

        /// <summary>
        /// 点动开始（由 UI 的按住行为触发，按住期间每 150ms 重复一次）。
        /// 每次调用都刷新心跳 —— 上层的 <see cref="CheckJogWatchdog"/> 据此判断界面还在不在按。
        /// </summary>
        private void StartJog(object? parameter)
        {
            if (!TryGetAxis(out var device, out var axis)) return;

            if (!device.Capabilities.SupportsJog)
            {
                StatusHint = "该卡未上报点动能力（SupportsJog=false），请改用「定位」逐点对位";
                return;
            }

            var direction = 1;
            if (parameter is string text && text.StartsWith("-", StringComparison.Ordinal)) direction = -1;
            else if (parameter is int raw) direction = raw >= 0 ? 1 : -1;

            _lastJogPulseUtc = DateTime.UtcNow;
            _jogDevice = device;

            using var command = new MotionCommand
            {
                Kind = MotionCommandKind.Jog,
                PhysicalAxis = axis.PhysicalIndex,
                LogicalAxis = axis.LogicalName,
                JogDirection = direction,
                VelocityMmPerS = JogVelocity,
                WaitsForCompletion = false,
                Retryable = false,
                Timeout = TimeSpan.FromMilliseconds(device.Descriptor.Params.CommandTimeoutMs),
            };

            var result = device.Enqueue(command);
            StatusHint = result == MotionCommandResult.Accepted
                ? $"轴「{axis.LogicalName}」点动中（{JogVelocity:F2} mm/s，{(direction >= 0 ? "正" : "负")}向）——松开即停"
                : $"点动被拒绝（{result}）：{device.LastFault?.Suggestion ?? device.StateDetail}";
        }

        /// <summary>点动结束（松开、切换轴/卡、关窗、看门狗触发）</summary>
        private void StopJog(string reason)
        {
            var device = _jogDevice;
            _jogDevice = null;
            _lastJogPulseUtc = DateTime.MinValue;

            if (device == null) return;

            // 停点动用立即停（mode=3）：点动速度通常很低，"立即停"能最快停在想要的位置；
            // 若用减速停，手一松还会多走一点，对位就白对了
            using var command = new MotionCommand
            {
                Kind = MotionCommandKind.Stop,
                PhysicalAxis = -1,      // 全部轴：点动可能因为切轴而记不清哪根在动，宁可多停
                StopMode = 3,
                WaitsForCompletion = false,
                Retryable = false,
                Timeout = TimeSpan.FromMilliseconds(device.Descriptor.Params.CommandTimeoutMs),
            };

            device.Enqueue(command);
            StatusHint = $"点动已停止（{reason}）";
        }

        /// <summary>
        /// 点动看门狗：按住期间 UI 会持续刷新心跳；超过阈值没收到就认为"界面已经不在按了"
        /// （失焦、卡顿、行为失效……），自动停。
        ///
        /// 为什么必须有它：点动没有终点，"松手即停"完全依赖界面事件一定送达。
        /// 而在真实产线上，一次丢事件就意味着机器一直走。安全不能建立在"事件不会丢"的假设上。
        /// </summary>
        private void CheckJogWatchdog()
        {
            if (_jogDevice == null) return;

            if (!IsJogPulseTimedOut(DateTime.UtcNow, _lastJogPulseUtc, JogPulseTimeoutMs)) return;

            StopJog($"点动心跳超时（>{JogPulseTimeoutMs}ms 未收到界面脉冲）");
        }

        /// <summary>
        /// 点动心跳是否已超时。
        ///
        /// 抽成静态纯函数是为了**能被断言**：定时器依赖 WPF 消息泵，在无消息循环的测试宿主里
        /// 根本不会 tick —— 而这条判断恰恰是"松手事件丢了设备也不能一直动"的最后一道闸。
        /// 判断逻辑必须离开计时器本身才测得到（计时器只负责"什么时候问"，不负责"怎么判"）。
        /// </summary>
        public static bool IsJogPulseTimedOut(DateTime nowUtc, DateTime lastPulseUtc, int timeoutMs)
            => lastPulseUtc != DateTime.MinValue
               && (nowUtc - lastPulseUtc).TotalMilliseconds > Math.Max(1, timeoutMs);

        private void UseCurrentPosition()
        {
            var device = CurrentDevice;
            var axis = _selectedAxis;
            if (device == null || axis == null) return;

            var status = device.GetAxisStatus(axis.PhysicalIndex);
            if (status != null) TargetPosition = Math.Round(status.PositionMm, 3);
        }

        private void ToggleOutput(MotionDebugIoRow? row)
        {
            if (row == null || !row.IsOutput) return;

            var device = CurrentDevice;
            if (device == null) return;

            var next = !row.IsOn;
            using var command = new MotionCommand
            {
                Kind = MotionCommandKind.SetOutput,
                IoPort = row.Port,
                IoValue = next,
                WaitsForCompletion = false,
                Timeout = TimeSpan.FromMilliseconds(device.Descriptor.Params.CommandTimeoutMs),
            };

            var result = device.Enqueue(command);
            if (result != MotionCommandResult.Accepted)
            {
                StatusHint = $"写输出 {row.Port} 被拒绝（{result}）：{device.LastFault?.Suggestion ?? device.StateDetail}";
                return;
            }

            // 界面先乐观置位，随后由刷新（读输出/命令完成）收敛 —— 但**不**在失败时谎报成功
            row.IsOn = next;
            StatusHint = $"已下发：输出 {row.Port} ← {(next ? "ON" : "OFF")}";
        }

        private bool TryGetAxis(out IMotionDevice device, out MotionDebugAxisRow axis)
        {
            device = CurrentDevice!;
            axis = _selectedAxis!;

            if (device == null)
            {
                StatusHint = "请先选择一张运动卡并确保它已连接";
                return false;
            }

            if (axis == null)
            {
                StatusHint = "请先在左侧选择一根轴";
                return false;
            }

            if (device.State != MotionCardState.Online)
            {
                StatusHint = $"该卡当前不可操作：{device.StateDetail}";
                return false;
            }

            return true;
        }

        /// <summary>下发"要等结果"的命令并同步等它结束（使能/失能这类必须当场知道成败）</summary>
        private static string SendAndWait(IMotionDevice device, MotionCommand command, string operation)
        {
            var result = device.Enqueue(command);
            if (result != MotionCommandResult.Accepted)
                return $"{operation}命令被拒绝（{result}）：{device.LastFault?.Suggestion ?? device.StateDetail}";

            var timeout = command.Timeout > TimeSpan.Zero
                ? command.Timeout
                : TimeSpan.FromMilliseconds(device.Descriptor.Params.CommandTimeoutMs);

            if (!command.Completion.Wait(timeout)) return $"{operation}超时（>{timeout.TotalSeconds:F0}s）";

            return command.State == MotionCommandState.Done
                ? $"{operation}完成"
                : $"{operation}失败（{command.State}）：{command.Error}";
        }

        #endregion
    }

    /// <summary>
    /// 小扩展：把设备状态转成界面文案。
    /// 文案本身收在 <see cref="MotionEnumDisplay"/>（驱动与界面共用同一份中文），这里只做转发 ——
    /// 免得"在线 / 报警"这类词在几个界面里各写一遍、各差一个字。
    /// </summary>
    internal static class MotionDeviceStateText
    {
        public static string StateTextOf(this IMotionDevice device) => MotionEnumDisplay.Text(device.State);
    }
}
