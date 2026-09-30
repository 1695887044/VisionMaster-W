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
        /// <summary>
        /// <paramref name="owner"/> 是这一行所属的卡；<paramref name="registry"/> 是全局轴名注册表。
        /// 两个都必须传：本地合法性（名字是否为空、本卡轴号是否撞）只需 owner，
        /// 而**全局唯一性**与"改名要级联哪些东西"只有注册表知道。
        /// </summary>
        public MotionAxisMapRow(AxisMapping mapping, MotionDescriptor owner, MotionAxisRegistry registry)
        {
            Mapping = mapping;
            Owner = owner;
            _registry = registry;
            DeleteCommand = new DelegateCommand(() => DeleteRequested?.Invoke(this));
        }

        private readonly MotionAxisRegistry _registry;

        public AxisMapping Mapping { get; }

        /// <summary>本行所属的卡（本地校验、以及改轴号时要告诉注册表"改的是哪张卡"）</summary>
        public MotionDescriptor Owner { get; }

        public event Action<MotionAxisMapRow>? DeleteRequested;

        /// <summary>
        /// 逻辑轴名改成功了，携带 (旧名, 新名, 注册表给的说明)。
        ///
        /// 说明由注册表生成（它才知道级联了多少点位与凸轮引用），这里不重编措辞 ——
        /// 两处各写一遍必然某一处先失真。
        /// </summary>
        public event Action<string, string, string>? LogicalNameChanged;

        /// <summary>编辑被校验拦下（参数是可直接上 toast 的中文原因）</summary>
        public event Action<string>? InvalidInput;

        public DelegateCommand DeleteCommand { get; }

        /// <summary>校验不通过时回滚本次编辑：不改任何数据，补一次通知让界面回到原值</summary>
        private void Reject(string reason)
        {
            InvalidInput?.Invoke(reason);
            RaisePropertyChanged();
        }

        /// <summary>
        /// 确认注册表里"这个名字"解析到的确实是本行。
        ///
        /// 历史方案里可能存在跨卡重名（注册表按先到者解析，后来的被影子化）。
        /// 少了这道核对，编辑"被影子的那一行"改到的会是**先到那根轴** ——
        /// 现场现象是"我改的是这根，怎么动的是那根"。
        /// </summary>
        private bool IsRegistryOwner()
            => !_registry.TryResolve(Mapping.LogicalName, out var binding)
               || ReferenceEquals(binding.Mapping, Mapping);

        private static readonly string DuplicatedNameHint =
            "：它与其它卡上的轴重名，注册表按先到的那根解析 —— 请先把重名解开，再改这一行";

        /// <summary>逻辑轴名（流程唯一引用的名字）</summary>
        public string LogicalName
        {
            get => Mapping.LogicalName;
            set
            {
                var next = value ?? string.Empty;
                if (Mapping.LogicalName == next) return;

                var localError = MotionAxisValidator.ValidateLogicalName(Owner, Mapping, next);
                if (localError != null)
                {
                    Reject(localError);
                    return;
                }

                if (!IsRegistryOwner())
                {
                    Reject($"轴名「{Mapping.LogicalName}」{DuplicatedNameHint}");
                    return;
                }

                // ★ 改名必须走注册表：全局唯一性判断 + 点位外键级联 + 凸轮标签级联都在那里。
                //   若像以前那样直接写 Mapping.LogicalName，这三件事会一次性全漏掉。
                var trimmed = next.Trim();
                var result = _registry.Rename(Mapping.LogicalName, trimmed);
                if (!result.Success)
                {
                    Reject(result.Message);
                    return;
                }

                var old = Mapping.LogicalName;
                RaisePropertyChanged();
                LogicalNameChanged?.Invoke(old, trimmed, result.Message);
            }
        }

        public int PhysicalIndex
        {
            get => Mapping.PhysicalIndex;
            set
            {
                if (Mapping.PhysicalIndex == value) return;

                var localError = MotionAxisValidator.ValidatePhysicalIndex(Owner, Mapping, value);
                if (localError != null)
                {
                    Reject(localError);
                    return;
                }

                if (!IsRegistryOwner())
                {
                    Reject($"轴名「{Mapping.LogicalName}」{DuplicatedNameHint}");
                    return;
                }

                // 换物理轴号同样走注册表：它会重排 (卡, 轴号) 槽位索引，
                // 否则两根逻辑轴会指向同一个物理轴（运动时互抢）
                var result = _registry.Update(Mapping.LogicalName, Owner.Id, value);
                if (!result.Success)
                {
                    Reject(result.Message);
                    return;
                }

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
        private readonly MotionAxisRegistry _axes;

        public MotionBoardSettingsTabViewModel(
            MotionProvider provider, IWorkspaceManager workspace, MotionBoardViewModel shell,
            MotionAxisRegistry axes)
        {
            _provider = provider;
            _workspace = workspace;
            _shell = shell;
            _axes = axes;

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
            EnsureAxisRowCoverage();

            foreach (var row in AxisRows)
            {
                row.DeleteRequested -= OnRowDeleteRequested;
                row.LogicalNameChanged -= OnRowLogicalNameChanged;
                row.InvalidInput -= OnRowInvalidInput;
            }

            AxisRows.Clear();
            if (_selectedDescriptor == null) return;

            // 行要以"注册表已就位"为前提构造：它们改名/改轴号会立刻回调注册表，
            // 若拿到的还是旧索引，就会用过期数据判全局唯一性 —— 明明能放行的名字被拒。
            _axes.Reload();

            foreach (var mapping in _selectedDescriptor.Axes)
            {
                var row = new MotionAxisMapRow(mapping, _selectedDescriptor, _axes);
                row.DeleteRequested += OnRowDeleteRequested;
                row.LogicalNameChanged += OnRowLogicalNameChanged;
                row.InvalidInput += OnRowInvalidInput;
                AxisRows.Add(row);
            }
        }

        /// <summary>
        /// 铺够轴行：连上卡按机型能力补齐，没连卡也至少给出 <see cref="MotionAxisValidator.MinimumAxisRows"/> 行。
        ///
        /// 少了它就会出现两个"表是空的"现场事故（两个都是实测反馈过的问题）：
        ///   ① 未连接时按能力铺不出轴，表格一片空白，用户连"脉冲当量填在哪儿"都不知道；
        ///   ② 连上 32 轴卡之后不补齐，表格永远停在配置里的那几行。
        /// <c>EnsureAxes</c> 只增不删，已填的值不会被清掉。
        /// </summary>
        private void EnsureAxisRowCoverage()
        {
            var descriptor = _selectedDescriptor;
            if (descriptor == null) return;

            var device = CurrentDevice;
            var byCapability = device is { State: MotionCardState.Online or MotionCardState.Alarm }
                ? device.Capabilities.AxisCount
                : 0;

            // 生成默认名时必须避开**全局**已占用的名字：两张卡各自铺一遍 A0/A1/A2，
            // 在注册表里就是三对重名，其中一半的轴永远解析不到。
            descriptor.EnsureAxes(
                Math.Max(Math.Max(byCapability, descriptor.Axes.Count),
                         MotionAxisValidator.MinimumAxisRows),
                isNameTaken: name => !_axes.IsNameAvailable(name));
        }

        /// <summary>行内编辑被校验拦下：给一条红色 toast（编辑已被回滚）</summary>
        private void OnRowInvalidInput(string reason) => _shell.NotifyError(reason);

        /// <summary>
        /// 行内改了轴名：先做级联（点位表外键 + 凸轮表引用），再让其它页签跟着刷新。
        /// 顺序不能反 —— 点位与凸轮都按**旧名**找，改完名就找不到了。
        /// </summary>
        private void OnRowLogicalNameChanged(string oldName, string newName, string detail)
            => _shell.OnAxisLogicalNameChanged(oldName, newName, detail);

        private void OnRowDeleteRequested(MotionAxisMapRow row)
        {
            _shell.RequestConfirm(
                "删除轴",
                $"确定删除轴「{row.LogicalName}」吗？该轴的 16 行点位会一并删除。",
                () =>
                {
                    if (_selectedDescriptor == null) return;

                    // 删轴走注册表：它负责摘映射 + 清该轴 16 行点位 + 摘掉凸轮表对它的引用。
                    // 少做后面两步的结果是：下次再建一根同名轴，老点位会"复活"到新轴上。
                    var name = row.LogicalName;
                    var result = _axes.Remove(name);

                    ReloadAxisRows();
                    _shell.NotifyOk(result.Success
                        ? $"轴「{name}」已删除（点位与凸轮引用已清理）"
                        : $"轴「{name}」已从映射表移除，但清理时提示：{result.Message}");
                });
        }

        /// <summary>
        /// 添加轴。逻辑名/物理轴号都取"下一个可用"的值而不是"当前行数" ——
        /// 用户改过名或删过轴之后，行数与占用情况不再对应，按行数取名会直接撞名。
        /// </summary>
        private void AddAxis()
        {
            var descriptor = _selectedDescriptor;
            if (descriptor == null) return;

            // 走注册表注册：名字与轴号都由它给"下一个可用的"，并且一次判完全局唯一性。
            // 自己算"下一个"等于绕开了唯一的裁决者 —— 撞名时不会有人拦。
            var result = _axes.Register(
                _axes.NextAvailableName(), descriptor.Id, _axes.NextAvailableAxisIndex(descriptor.Id));

            if (!result.Success)
            {
                _shell.NotifyError(result.Message);
                return;
            }

            ReloadAxisRows();
            _shell.NotifyOk(result.Message);
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
                    {
                        // 弹"扫描到 N 个轴" + 同步重建手动调试页签的轴列表（统一出口）
                        _shell.NotifyCardConnected(descriptor.Caption, device.Capabilities.AxisCount);
                    }
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
                // 级联（凸轮轴标签清理 + 断开物理连接 + 重载）只有外壳里那一份，
                // 这里不再抄一遍 —— 抄一份迟早少清一样东西
                () => _shell.RemoveCard(descriptor));
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
