using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Core.Interfaces;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 「运动卡设置」里的一行（一张卡）。
    ///
    /// 与相机的 CameraRowViewModel 同职责：把"配置（MotionDescriptor）+ 运行态（IMotionDevice）"
    /// 合成界面能用的一行。界面只读它的属性，改配置一律走 VM 的命令。
    /// </summary>
    public sealed class MotionCardRowViewModel : BindableBase
    {
        private readonly MotionProvider _provider;

        public MotionCardRowViewModel(MotionProvider provider, MotionDescriptor descriptor)
        {
            _provider = provider;
            Descriptor = descriptor;
        }

        /// <summary>配置对象（界面上的编辑最终写回它，再由 VM 落盘）</summary>
        public MotionDescriptor Descriptor { get; }

        public Guid Id => Descriptor.Id;

        /// <summary>卡显示名（可编辑）</summary>
        public string DisplayName
        {
            get => Descriptor.DisplayName;
            set { if (Descriptor.DisplayName != value) { Descriptor.DisplayName = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(Caption)); } }
        }

        /// <summary>连接地址（如 192.168.0.11）</summary>
        public string Address
        {
            get => Descriptor.Address;
            set { if (Descriptor.Address != value) { Descriptor.Address = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(Caption)); } }
        }

        /// <summary>机型（决定轴数/IO 数，如 ECI3428；留空则由驱动自己问卡）</summary>
        public string CardModel
        {
            get => Descriptor.CardModel;
            set { if (Descriptor.CardModel != value) { Descriptor.CardModel = value; RaisePropertyChanged(); } }
        }

        public string Caption => Descriptor.Caption;

        /// <summary>驱动显示名（配置里存的是类型键，这里换成给人看的名字）</summary>
        public string DriverDisplayName
        {
            get
            {
                var key = Descriptor.DriverTypeKey;
                return _provider.AvailableDrivers.FirstOrDefault(d => d.TypeKey == key)?.DisplayName
                       ?? (string.IsNullOrWhiteSpace(key) ? "(未选择驱动)" : key.Split(',')[0]);
            }
        }

        /// <summary>运行态设备（未连接或未对齐时为 null）</summary>
        public IMotionDevice? Device
        {
            get
            {
                _provider.TryGetDevice(Descriptor.Id, out var device);
                return device;
            }
        }

        public MotionCardState State => Device?.State ?? MotionCardState.Closed;

        public string StateText => State switch
        {
            MotionCardState.Closed => "未连接",
            MotionCardState.Connecting => "连接中",
            MotionCardState.Online => "在线",
            MotionCardState.Alarm => "报警",
            MotionCardState.SafeStopped => "已安全停机",
            _ => State.ToString(),
        };

        public string StateDetail => Device?.StateDetail ?? "尚未创建设备实例（检查驱动是否可用）";

        public bool IsConnected => State is MotionCardState.Online or MotionCardState.Alarm;

        public long ExecutedCommandCount => Device?.ExecutedCommandCount ?? 0;

        public long RejectedCommandCount => Device?.RejectedCommandCount ?? 0;

        public long FaultCount => Device?.FaultCount ?? 0;

        public int AxisCount => Device?.Capabilities.AxisCount ?? Descriptor.Axes.Count;

        /// <summary>是否需要回零（增量式编码器在失联/上电后位置不可信）</summary>
        public bool RequiresHoming => Device is { IsHomed: false };

        /// <summary>最近一次故障的说明（含建议）</summary>
        public string LastFaultText
        {
            get
            {
                var fault = Device?.LastFault;
                if (fault == null) return string.Empty;
                return string.IsNullOrWhiteSpace(fault.Suggestion)
                    ? fault.Message
                    : $"{fault.Message}（建议：{fault.Suggestion}）";
            }
        }

        /// <summary>轴映射（界面表格绑定它；编辑后由 VM 调 MarkConfigDirty 生效）</summary>
        public ObservableCollection<AxisMapping> Axes { get; } = new();

        /// <summary>
        /// 未连卡时也铺出的最少轴行数。
        /// 没有它，"轴映射"在连接之前是一片空白 —— 用户连字段填在哪儿都不知道。
        /// 取 4 是因为它覆盖了绝大多数小型卡；大卡连上后会自动补齐。
        /// </summary>
        private const int MinimumAxisRows = 4;

        /// <summary>把配置里的轴映射与设备能力对齐后再铺进界面</summary>
        public void ReloadAxes()
        {
            // 下限 MinimumAxisRows：**还没连卡时也要给出可编辑的行**。
            // 未连接时 AxisCount 退到 Descriptor.Axes.Count（新方案是 0），表格会是一片空白 ——
            // 用户因此连"脉冲当量填在哪儿"都无从下手（实测反馈：轴映射是空的）。
            // 连上卡之后再按机型能力补齐（EnsureAxes 只增不删，已填的值不会被清掉）。
            Descriptor.EnsureAxes(Math.Max(Math.Max(AxisCount, Descriptor.Axes.Count), MinimumAxisRows));
            Axes.Clear();
            foreach (var axis in Descriptor.Axes) Axes.Add(axis);
        }

        /// <summary>刷新运行态相关显示（由 VM 的定时器/事件调用）</summary>
        public void RefreshRuntime()
        {
            RaisePropertyChanged(nameof(Device));
            RaisePropertyChanged(nameof(State));
            RaisePropertyChanged(nameof(StateText));
            RaisePropertyChanged(nameof(StateDetail));
            RaisePropertyChanged(nameof(IsConnected));
            RaisePropertyChanged(nameof(ExecutedCommandCount));
            RaisePropertyChanged(nameof(RejectedCommandCount));
            RaisePropertyChanged(nameof(FaultCount));
            RaisePropertyChanged(nameof(AxisCount));
            RaisePropertyChanged(nameof(RequiresHoming));
            RaisePropertyChanged(nameof(LastFaultText));
        }
    }

    /// <summary>
    /// 运动卡设置（方案级运动卡资源管理）。
    ///
    /// 与 <see cref="CameraSettingsViewModel"/> 同范式：本界面只管"配置怎么写 + 设备怎么连"，
    /// 配置落到 <c>SolutionModel.MotionCards</c>（随 .vms 落盘），运行态设备由 MotionProvider 另存。
    /// 界面**不直接调 SyncFromSolution**（编辑是连续动作，每次都重建会把连接断掉），
    /// 一律是"改配置 → MarkConfigDirty → 下一次取用时对齐"。
    /// </summary>
    public class MotionSettingsViewModel : BindableBase, IDialogAware
    {
        private readonly MotionProvider _provider;
        private readonly IWorkspaceManager _workspace;

        public MotionSettingsViewModel(MotionProvider provider, IWorkspaceManager workspace)
        {
            _provider = provider;
            _workspace = workspace;

            AddCommand = new DelegateCommand(() => { IsAdding = true; NewDriver = AvailableDrivers.FirstOrDefault(); });
            CancelAddCommand = new DelegateCommand(() => IsAdding = false);
            ConfirmAddCommand = new DelegateCommand(ConfirmAdd);
            DeleteCommand = new DelegateCommand<MotionCardRowViewModel>(DeleteCard);
            ToggleConnectCommand = new DelegateCommand(ToggleConnect);
            ClearAlarmCommand = new DelegateCommand(ClearAlarm);
            ApplyParamsCommand = new DelegateCommand(ApplyParams);
            CloseCommand = new DelegateCommand(() => RequestClose.Invoke(new DialogResult(ButtonResult.OK)));
        }

        public string Title => "运动卡设置";

        public DialogCloseListener RequestClose { get; set; }

        public bool CanCloseDialog() => true;

        public void OnDialogOpened(IDialogParameters parameters)
        {
            _provider.EnsureSynced();
            _provider.DevicesChanged += OnDevicesChangedHandler;
            ReloadRows();
        }

        public void OnDialogClosed()
        {
            _provider.DevicesChanged -= OnDevicesChangedHandler;
            // 关闭时把配置脏位落下并同步一次：让"界面里刚做的连接/断开"与配置状态一致
            _provider.MarkConfigDirty();
            _provider.EnsureSynced();
        }

        #region 列表与选择

        public ObservableCollection<MotionCardRowViewModel> Cards { get; } = new();

        private MotionCardRowViewModel? _selectedCard;
        public MotionCardRowViewModel? SelectedCard
        {
            get => _selectedCard;
            set
            {
                if (!SetProperty(ref _selectedCard, value)) return;
                RaisePropertyChanged(nameof(HasSelection));
                RaisePropertyChanged(nameof(IsEmpty));
                LoadParamsFromSelection();
                value?.ReloadAxes();
            }
        }

        public bool HasSelection => _selectedCard != null;

        public bool IsEmpty => Cards.Count == 0;

        /// <summary>可用驱动（来自 Plugins 扫描到的运动插件表）</summary>
        public IReadOnlyList<MotionDriverInfo> AvailableDrivers => _provider.AvailableDrivers;

        #region 新增表单

        private bool _isAdding;
        public bool IsAdding
        {
            get => _isAdding;
            set => SetProperty(ref _isAdding, value);
        }

        private MotionDriverInfo? _newDriver;
        public MotionDriverInfo? NewDriver
        {
            get => _newDriver;
            set => SetProperty(ref _newDriver, value);
        }

        private string _newAddress = "192.168.0.11";
        public string NewAddress
        {
            get => _newAddress;
            set => SetProperty(ref _newAddress, value);
        }

        private string _newDisplayName = string.Empty;
        public string NewDisplayName
        {
            get => _newDisplayName;
            set => SetProperty(ref _newDisplayName, value);
        }

        private string _newCardModel = string.Empty;
        public string NewCardModel
        {
            get => _newCardModel;
            set => SetProperty(ref _newCardModel, value);
        }

        #endregion

        #endregion

        #region 参数编辑（跟随选中卡）

        private double _defaultVelocity;
        public double DefaultVelocity
        {
            get => _defaultVelocity;
            set => SetProperty(ref _defaultVelocity, value);
        }

        private double _defaultAccel;
        public double DefaultAccel
        {
            get => _defaultAccel;
            set => SetProperty(ref _defaultAccel, value);
        }

        private int _pollIntervalMs;
        public int PollIntervalMs
        {
            get => _pollIntervalMs;
            set => SetProperty(ref _pollIntervalMs, value);
        }

        private int _commandTimeoutMs;
        public int CommandTimeoutMs
        {
            get => _commandTimeoutMs;
            set => SetProperty(ref _commandTimeoutMs, value);
        }

        private int _watchdogTimeoutMs;
        public int WatchdogTimeoutMs
        {
            get => _watchdogTimeoutMs;
            set => SetProperty(ref _watchdogTimeoutMs, value);
        }

        private bool _autoConnect;
        public bool AutoConnect
        {
            get => _autoConnect;
            set => SetProperty(ref _autoConnect, value);
        }

        private string _statusHint = string.Empty;
        public string StatusHint
        {
            get => _statusHint;
            set => SetProperty(ref _statusHint, value);
        }

        private void LoadParamsFromSelection()
        {
            var parameters = _selectedCard?.Descriptor.Params;
            DefaultVelocity = parameters?.DefaultVelocityMmPerS ?? 50;
            DefaultAccel = parameters?.DefaultAccelMmPerS2 ?? 500;
            PollIntervalMs = parameters?.PollIntervalMs ?? 10;
            CommandTimeoutMs = parameters?.CommandTimeoutMs ?? 30_000;
            WatchdogTimeoutMs = parameters?.WatchdogTimeoutMs ?? 3_000;
            AutoConnect = _selectedCard?.Descriptor.AutoConnect ?? false;
        }

        #endregion

        #region 命令

        public DelegateCommand AddCommand { get; }
        public DelegateCommand CancelAddCommand { get; }
        public DelegateCommand ConfirmAddCommand { get; }
        public DelegateCommand<MotionCardRowViewModel> DeleteCommand { get; }
        public DelegateCommand ToggleConnectCommand { get; }
        public DelegateCommand ClearAlarmCommand { get; }
        public DelegateCommand ApplyParamsCommand { get; }
        public DelegateCommand CloseCommand { get; }

        private void ReloadRows()
        {
            Cards.Clear();
            foreach (var descriptor in _workspace.CurrentSolution?.MotionCards ?? new ObservableCollection<MotionDescriptor>())
                Cards.Add(new MotionCardRowViewModel(_provider, descriptor));

            foreach (var row in Cards) row.ReloadAxes();

            SelectedCard = Cards.FirstOrDefault();
            RaisePropertyChanged(nameof(IsEmpty));
        }

        private void ConfirmAdd()
        {
            if (NewDriver == null)
            {
                StatusHint = "请先选择驱动类型";
                return;
            }

            var address = (NewAddress ?? string.Empty).Trim();
            if (address.Length == 0)
            {
                StatusHint = "请填写连接地址（正运动为卡 IP，如 192.168.0.11）";
                return;
            }

            // 地址是流程引用设备的键，重复会让"命令发给 A 卡、动的是 B 卡"，必须提前拦
            if (!_provider.IsAddressAvailable(address, Guid.Empty))
            {
                StatusHint = $"地址「{address}」已被另一张卡占用，请换一个（流程按地址寻址，必须唯一）";
                return;
            }

            var descriptor = new MotionDescriptor
            {
                DriverTypeKey = NewDriver.TypeKey,
                Address = address,
                DisplayName = string.IsNullOrWhiteSpace(NewDisplayName) ? NewDriver.DisplayName : NewDisplayName.Trim(),
                CardModel = (NewCardModel ?? string.Empty).Trim(),
                AutoConnect = false,
            };

            var solution = _workspace.CurrentSolution;
            if (solution?.MotionCards == null)
            {
                StatusHint = "当前没有打开的方案，无法保存运动卡配置";
                return;
            }

            solution.MotionCards.Add(descriptor);
            _provider.MarkConfigDirty();
            _provider.EnsureSynced();

            IsAdding = false;
            NewDisplayName = string.Empty;
            StatusHint = $"已添加「{descriptor.Caption}」；轴映射会自动按机型铺开，请核对脉冲当量与软限位后再连接";

            ReloadRows();
            SelectedCard = Cards.FirstOrDefault(r => r.Id == descriptor.Id);
        }

        private void DeleteCard(MotionCardRowViewModel? row)
        {
            if (row == null) return;

            var solution = _workspace.CurrentSolution;
            if (solution?.MotionCards == null) return;

            var target = solution.MotionCards.FirstOrDefault(c => c.Id == row.Id);
            if (target != null) solution.MotionCards.Remove(target);

            _provider.MarkConfigDirty();
            _provider.EnsureSynced();   // 删卡必须立刻断开物理连接，等下次取用太久

            StatusHint = $"已删除「{row.Caption}」（物理连接已断开）";
            ReloadRows();
        }

        private void ToggleConnect()
        {
            var row = _selectedCard;
            if (row == null) return;

            var device = row.Device;
            if (device == null)
            {
                StatusHint = "该卡没有运行态设备实例：请确认驱动插件已加载（驱动名显示为未选择时尤其要查）";
                return;
            }

            try
            {
                if (row.IsConnected)
                {
                    device.Disconnect();
                    StatusHint = $"已断开「{row.Caption}」";
                }
                else
                {
                    if (!device.Connect())
                    {
                        StatusHint = $"连接「{row.Caption}」失败：{device.StateDetail}";
                    }
                    else if (!device.Capabilities.SupportsAbsoluteEncoder)
                    {
                        StatusHint = $"已连接「{row.Caption}」：增量式编码器，位置不可信 —— "
                                      + "请在流程首步放「轴回零」，否则运动坐标会是错的";
                    }
                    else
                    {
                        StatusHint = $"已连接「{row.Caption}」";
                    }
                }
            }
            catch (Exception ex)
            {
                StatusHint = $"操作失败：{ex.Message}";
            }

            // ★ 连接/断开之后必须重建轴映射表：
            //   面板打开时卡往往还没连，此时按"能力"铺不出轴（AxisCount 退到配置里的条数），
            //   表格是空的；连上之后机型能力才有值（如 VPLC532R = 32 轴），
            //   不重建的话用户面对的就是一张永远空着的表（实测反馈：轴映射是空的）。
            row.ReloadAxes();

            row.RefreshRuntime();
        }

        private void ClearAlarm()
        {
            var row = _selectedCard;
            var device = row?.Device;
            if (row == null || device == null) return;

            StatusHint = device.ClearAlarm(out var error)
                ? $"「{row.Caption}」报警已清除"
                : $"清除报警失败：{error}";

            row.RefreshRuntime();
        }

        private void ApplyParams()
        {
            var row = _selectedCard;
            if (row == null) return;

            var parameters = row.Descriptor.Params;
            parameters.DefaultVelocityMmPerS = Math.Max(0, DefaultVelocity);
            parameters.DefaultAccelMmPerS2 = Math.Max(0, DefaultAccel);
            parameters.PollIntervalMs = Math.Clamp(PollIntervalMs, 1, 1000);
            parameters.CommandTimeoutMs = Math.Max(100, CommandTimeoutMs);
            parameters.WatchdogTimeoutMs = Math.Max(200, WatchdogTimeoutMs);
            row.Descriptor.AutoConnect = AutoConnect;

            // 参数是"从 Descriptor 现读"的，标脏 + 对齐即可生效（不需要重建设备）
            _provider.MarkConfigDirty();
            _provider.EnsureSynced();

            StatusHint = $"「{row.Caption}」的参数已应用（轮询 {parameters.PollIntervalMs}ms，"
                         + $"看门狗 {parameters.WatchdogTimeoutMs}ms，自动连接 {(AutoConnect ? "开" : "关")}）";
        }

        private void OnDevicesChangedHandler(object? sender, EventArgs e)
        {
            foreach (var row in Cards) row.RefreshRuntime();
        }

        #endregion
    }
}
