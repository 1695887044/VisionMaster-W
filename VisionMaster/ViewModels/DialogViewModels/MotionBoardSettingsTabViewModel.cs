using System;
using System.Collections.ObjectModel;
using System.Linq;
using Core.Interfaces;
using Prism.Commands;
using Prism.Mvvm;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 轴映射表里的一行：包一层 INPC，编辑直接写回 <see cref="AxisMapping"/>（方案对象，落盘走工作区）。
    /// 行尾删除按钮通过事件上抛给页签 VM。
    /// </summary>
    public sealed class MotionAxisMapRow : BindableBase
    {
        public MotionAxisMapRow(AxisMapping mapping)
        {
            Mapping = mapping;
            DeleteCommand = new DelegateCommand(() => DeleteRequested?.Invoke(this));
        }

        public AxisMapping Mapping { get; }

        public event Action<MotionAxisMapRow>? DeleteRequested;

        public DelegateCommand DeleteCommand { get; }

        /// <summary>逻辑轴名（流程唯一引用的名字）</summary>
        public string LogicalName
        {
            get => Mapping.LogicalName;
            set
            {
                if (Mapping.LogicalName == value) return;
                Mapping.LogicalName = value;
                RaisePropertyChanged();
            }
        }

        public int PhysicalIndex
        {
            get => Mapping.PhysicalIndex;
            set
            {
                if (Mapping.PhysicalIndex == value) return;
                Mapping.PhysicalIndex = value;
                RaisePropertyChanged();
            }
        }

        /// <summary>脉冲当量（走 1mm 需要多少脉冲）</summary>
        public double UnitsPerMm
        {
            get => Mapping.UnitsPerMm;
            set
            {
                if (Mapping.UnitsPerMm.Equals(value)) return;
                Mapping.UnitsPerMm = value;
                RaisePropertyChanged();
            }
        }

        public double SoftLimitMinMm
        {
            get => Mapping.SoftLimitMinMm;
            set
            {
                if (Mapping.SoftLimitMinMm.Equals(value)) return;
                Mapping.SoftLimitMinMm = value;
                RaisePropertyChanged();
            }
        }

        public double SoftLimitMaxMm
        {
            get => Mapping.SoftLimitMaxMm;
            set
            {
                if (Mapping.SoftLimitMaxMm.Equals(value)) return;
                Mapping.SoftLimitMaxMm = value;
                RaisePropertyChanged();
            }
        }

        public bool Enabled
        {
            get => Mapping.Enabled;
            set
            {
                if (Mapping.Enabled == value) return;
                Mapping.Enabled = value;
                RaisePropertyChanged();
            }
        }
    }

    /// <summary>
    /// 页签一「卡设置」（规格第 3 章）：
    /// 卡头部（渐变图标 + 名称 + 连接徽标 + 驱动/机型 pill + 清除报警/连接/删除）
    /// → 设备标识 → 运动参数（6 字段 + 自动连接勾选卡）→ 轴映射表（行内编辑 + ⚙/🗑）→ 贴底保存条。
    ///
    /// 添加运动卡 / 删除确认 / 轴配置弹窗都走外壳的模态遮罩（本 VM 只发请求）。
    /// 编辑写回的都是方案对象：参数要等「应用」才对运行态生效（MarkConfigDirty → EnsureSynced），
    /// 轴映射行内编辑即时生效（设备描述符与方案共享同一份 Axes 列表）。
    /// </summary>
    public class MotionBoardSettingsTabViewModel : BindableBase
    {
        private readonly MotionProvider _provider;
        private readonly IWorkspaceManager _workspace;
        private readonly MotionBoardViewModel _shell;

        public MotionBoardSettingsTabViewModel(
            MotionProvider provider, IWorkspaceManager workspace, MotionBoardViewModel shell)
        {
            _provider = provider;
            _workspace = workspace;
            _shell = shell;

            ApplyParamsCommand = new DelegateCommand(ApplyParams);
            ToggleConnectCommand = new DelegateCommand(ToggleConnect);
            ClearAlarmCommand = new DelegateCommand(ClearAlarm);
            DeleteCardCommand = new DelegateCommand(DeleteCard);
            AddAxisCommand = new DelegateCommand(AddAxis);
        }

        private MotionDescriptor? _selectedDescriptor;
        /// <summary>当前卡（由外壳推过来；null = 未选卡，视图显示空态）</summary>
        public MotionDescriptor? SelectedDescriptor
        {
            get => _selectedDescriptor;
            set
            {
                if (!SetProperty(ref _selectedDescriptor, value))
                    return;

                RaisePropertyChanged(nameof(HasSelection));
                LoadParamsFromSelection();
                ReloadAxisRows();
                RefreshRuntime();
            }
        }

        public bool HasSelection => _selectedDescriptor != null;

        #region 卡头部

        /// <summary>设备名（可留空：留空则显示地址）——落 Remarks，头部第二行用它</summary>
        public string DeviceName
        {
            get => _selectedDescriptor?.Remarks ?? string.Empty;
            set
            {
                if (_selectedDescriptor == null || _selectedDescriptor.Remarks == value) return;
                _selectedDescriptor.Remarks = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(HeaderDetail));
            }
        }

        public string HeaderName => _selectedDescriptor?.Caption ?? string.Empty;

        public string HeaderDetail
        {
            get
            {
                var d = _selectedDescriptor;
                if (d == null) return string.Empty;
                var name = string.IsNullOrWhiteSpace(d.Remarks) ? d.Caption : d.Remarks;
                return $"{name} · {d.Address}";
            }
        }

        /// <summary>驱动 pill（给人看的驱动名）</summary>
        public string DriverText =>
            _selectedDescriptor == null
                ? string.Empty
                : _provider.AvailableDrivers.FirstOrDefault(d => d.TypeKey == _selectedDescriptor.DriverTypeKey)?.DisplayName
                  ?? (string.IsNullOrWhiteSpace(_selectedDescriptor.DriverTypeKey)
                      ? "(未选择驱动)"
                      : _selectedDescriptor.DriverTypeKey.Split(',')[0]);

        public string ModelText => _selectedDescriptor?.CardModel ?? string.Empty;
        public bool HasModel => ModelText.Length > 0;

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (SetProperty(ref _isConnected, value))
                    RaisePropertyChanged(nameof(ConnectButtonText));
            }
        }

        public string ConnectButtonText => IsConnected ? "断开" : "连接";

        /// <summary>头部连接徽标文字（只读状态：已连接/未连接，与按钮文字分开）</summary>
        public string ConnectionStateText => IsConnected ? "已连接" : "未连接";

        private bool _isConnecting;
        public bool IsConnecting
        {
            get => _isConnecting;
            private set => SetProperty(ref _isConnecting, value);
        }

        private IMotionDevice? CurrentDevice =>
            _selectedDescriptor != null && _provider.TryGetDevice(_selectedDescriptor.Id, out var device)
                ? device
                : null;

        #endregion

        #region 运动参数（跟随选中卡加载，点「应用」才生效）

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

        private int _commandTimeoutMs;
        public int CommandTimeoutMs
        {
            get => _commandTimeoutMs;
            set => SetProperty(ref _commandTimeoutMs, value);
        }

        private int _pollMs;
        public int PollMs
        {
            get => _pollMs;
            set => SetProperty(ref _pollMs, value);
        }

        private int _watchdogMs;
        public int WatchdogMs
        {
            get => _watchdogMs;
            set => SetProperty(ref _watchdogMs, value);
        }

        private bool _autoConnect;
        public bool AutoConnect
        {
            get => _autoConnect;
            set => SetProperty(ref _autoConnect, value);
        }

        private void LoadParamsFromSelection()
        {
            var p = _selectedDescriptor?.Params;
            DefaultVelocity = p?.DefaultVelocityMmPerS ?? 50;
            DefaultAccel = p?.DefaultAccelMmPerS2 ?? 500;
            CommandTimeoutMs = p?.CommandTimeoutMs ?? 30_000;
            PollMs = p?.PollIntervalMs ?? 10;
            WatchdogMs = p?.WatchdogTimeoutMs ?? 3_000;
            AutoConnect = _selectedDescriptor?.AutoConnect ?? false;
        }

        #endregion

        #region 轴映射表

        public ObservableCollection<MotionAxisMapRow> AxisRows { get; } = new();

        /// <summary>重建轴映射行（选卡变化 / 连接后轴数变多）</summary>
        public void ReloadAxisRows()
        {
            foreach (var row in AxisRows)
                row.DeleteRequested -= OnRowDeleteRequested;

            AxisRows.Clear();
            if (_selectedDescriptor == null) return;

            foreach (var mapping in _selectedDescriptor.Axes)
            {
                var row = new MotionAxisMapRow(mapping);
                row.DeleteRequested += OnRowDeleteRequested;
                AxisRows.Add(row);
            }
        }

        private void OnRowDeleteRequested(MotionAxisMapRow row)
        {
            _shell.RequestConfirm(
                "删除轴",
                $"确定删除轴「{row.LogicalName}」吗？该轴的 16 行点位会一并删除。",
                () =>
                {
                    if (_selectedDescriptor == null) return;
                    _selectedDescriptor.Axes.Remove(row.Mapping);
                    _selectedDescriptor.RemoveAxisPoints(row.LogicalName);
                    ReloadAxisRows();
                    _shell.NotifyOk($"轴「{row.LogicalName}」已删除");
                });
        }

        /// <summary>添加轴：逻辑名 A{序号}、物理轴号取当前行数（与 Web 版同规则）</summary>
        private void AddAxis()
        {
            if (_selectedDescriptor == null) return;

            var index = _selectedDescriptor.Axes.Count;
            _selectedDescriptor.Axes.Add(new AxisMapping
            {
                LogicalName = $"A{index}",
                PhysicalIndex = index,
            });
            ReloadAxisRows();
        }

        #endregion

        #region 命令

        public DelegateCommand ApplyParamsCommand { get; }
        public DelegateCommand ToggleConnectCommand { get; }
        public DelegateCommand ClearAlarmCommand { get; }
        public DelegateCommand DeleteCardCommand { get; }
        public DelegateCommand AddAxisCommand { get; }

        private void ApplyParams()
        {
            var descriptor = _selectedDescriptor;
            if (descriptor == null) return;

            try
            {
                var p = descriptor.Params;
                p.DefaultVelocityMmPerS = Math.Max(0, DefaultVelocity);
                p.DefaultAccelMmPerS2 = Math.Max(0, DefaultAccel);
                p.PollIntervalMs = Math.Clamp(PollMs, 1, 1000);
                p.CommandTimeoutMs = Math.Max(100, CommandTimeoutMs);
                p.WatchdogTimeoutMs = Math.Max(200, WatchdogMs);
                descriptor.AutoConnect = AutoConnect;

                _provider.MarkConfigDirty();
                _provider.EnsureSynced();
                _shell.NotifyOk("参数与轴映射已应用");
            }
            catch (Exception ex)
            {
                _shell.NotifyError("应用失败，请重试（" + ex.Message + "）");
            }
        }

        private void ToggleConnect()
        {
            var descriptor = _selectedDescriptor;
            var device = CurrentDevice;
            if (descriptor == null || device == null)
            {
                _shell.NotifyError("该卡没有运行态设备实例：请确认驱动插件已加载（驱动名显示为未选择时尤其要查）");
                return;
            }

            try
            {
                if (IsConnected)
                {
                    device.Disconnect();
                    _shell.NotifyOk($"「{descriptor.Caption}」已断开");
                }
                else
                {
                    IsConnecting = true;
                    var ok = device.Connect();
                    IsConnecting = false;
                    if (ok)
                        _shell.NotifyOk($"「{descriptor.Caption}」已连接");
                    else
                        _shell.NotifyError($"连接「{descriptor.Caption}」失败：{device.StateDetail}");
                }
            }
            catch (Exception ex)
            {
                IsConnecting = false;
                _shell.NotifyError($"连接操作异常：{ex.Message}");
            }

            RefreshRuntime();
            ReloadAxisRows();
        }

        private void ClearAlarm()
        {
            var descriptor = _selectedDescriptor;
            var device = CurrentDevice;
            if (descriptor == null || device == null) return;

            _shell.NotifyOk(device.ClearAlarm(out var error)
                ? $"「{descriptor.Caption}」报警已清除"
                : $"清除报警失败：{error}");
            RefreshRuntime();
        }

        private void DeleteCard()
        {
            var descriptor = _selectedDescriptor;
            if (descriptor == null) return;

            _shell.RequestConfirm(
                "删除运动卡",
                $"确定删除「{descriptor.Caption}」及其全部轴映射吗？",
                () =>
                {
                    var solution = _workspace.CurrentSolution;
                    var target = solution?.MotionCards?.FirstOrDefault(c => c.Id == descriptor.Id);
                    if (target == null) return;

                    solution.MotionCards.Remove(target);
                    _provider.MarkConfigDirty();
                    _provider.EnsureSynced();   // 删卡必须立刻断开物理连接

                    _shell.NotifyOk("运动卡及其轴映射已删除");
                    _shell.Reload();
                });
        }

        #endregion

        #region 驱动选项与运行态刷新

        public ObservableCollection<MotionDriverInfo> DriverOptions { get; } = new();

        /// <summary>驱动下拉候选（外壳打开时与方案变化后刷新）</summary>
        public void ReloadDriverOptions()
        {
            DriverOptions.Clear();
            foreach (var driver in _provider.AvailableDrivers) DriverOptions.Add(driver);
        }

        /// <summary>刷新头部运行态（连接徽标）；轴数可能随连接变多，轴行由连接路径主动重建</summary>
        public void RefreshRuntime()
        {
            IsConnected = CurrentDevice is { State: MotionCardState.Online or MotionCardState.Alarm };
            RaisePropertyChanged(nameof(HeaderName));
            RaisePropertyChanged(nameof(HeaderDetail));
            RaisePropertyChanged(nameof(DriverText));
            RaisePropertyChanged(nameof(ModelText));
            RaisePropertyChanged(nameof(HasModel));
            RaisePropertyChanged(nameof(DeviceName));
            RaisePropertyChanged(nameof(ConnectionStateText));
        }

        #endregion
    }
}
