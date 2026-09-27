using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Core.Interfaces;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 左栏卡列表里的一行：配置（MotionDescriptor）+ 实时在线态。
    ///
    /// 设计稿 3.3 的行结构：呼吸圆点（在线主色闪/离线灰）+ 卡名 + 「在线/离线」pill
    /// + 第二行「地址 | 驱动」等宽灰字。在线态每次刷新时从 MotionProvider 现取
    /// （设备实例的有无与状态只属于运行态，不进配置对象）。
    /// </summary>
    public sealed class MotionBoardCardRow : BindableBase
    {
        private readonly MotionProvider _provider;

        public MotionBoardCardRow(MotionProvider provider, MotionDescriptor descriptor)
        {
            _provider = provider;
            Descriptor = descriptor;
        }

        /// <summary>配置对象（纯配置、随方案落盘；界面编辑一律写回它）</summary>
        public MotionDescriptor Descriptor { get; }

        public Guid Id => Descriptor.Id;
        public string Caption => Descriptor.Caption;
        public string Address => Descriptor.Address;

        /// <summary>驱动显示名（配置里存类型键，这里换成给人看的名字）</summary>
        public string DriverName =>
            _provider.AvailableDrivers.FirstOrDefault(d => d.TypeKey == Descriptor.DriverTypeKey)?.DisplayName
            ?? (string.IsNullOrWhiteSpace(Descriptor.DriverTypeKey) ? "未选择驱动" : Descriptor.DriverTypeKey.Split(',')[0]);

        private bool _isOnline;
        /// <summary>在线 = 设备实例存在且状态为在线/报警（报警也是"连着的"）</summary>
        public bool IsOnline
        {
            get => _isOnline;
            private set => SetProperty(ref _isOnline, value);
        }

        public string OnlineText => IsOnline ? "在线" : "离线";

        /// <summary>刷新运行态（左栏呼吸圆点与 pill 由它驱动；定时器周期调用）</summary>
        public void RefreshRuntime()
        {
            var online = _provider.TryGetDevice(Descriptor.Id, out var device)
                         && device.State is MotionCardState.Online or MotionCardState.Alarm;
            IsOnline = online;
        }
    }

    /// <summary>
    /// 运动板卡（合并窗口）外壳 —— 左栏选卡 + 四页签（卡设置 / 手动调试 / 点位列表 / 电子凸轮）。
    ///
    /// 【与上一版骨架的本质区别】上一版把旧的弹窗级视图嵌进页签、靠 View 在 Loaded 后
    /// 把子 DataContext"桥接"回外壳 —— 实测层次混乱且桥接脆弱（StackOverflow 事故）。
    /// 这一版页签是**一等公民**：四个页签 VM 由本外壳构造时直接创建（构造注入同一个
    /// MotionProvider / IWorkspaceManager），"当前选中卡"只有本外壳一个事实来源，
    /// 选中变化时直接写给子 VM —— 没有 ViewModelLocator 时序问题，也没有桥接环。
    ///
    /// 【toast / 确认框 / 命令统计】都收在外壳这一层（设计稿 1.5 / 2.2）：
    ///   · 右下角 toast 单实例，成功 ok / 失败 err 两类，3.5s 自动淡出；
    ///   · 删除卡 / 删除轴走居中确认遮罩（标题与文案逐字按规格）；
    ///   · 已执行 / 已拒绝 / 故障 聚合**所有**运动卡设备的计数器
    ///     （它们由命令门禁自动累计），本外壳自己拦下的拒绝（未使能/未回零/忙…）
    ///     不经过设备，另记 _sessionRejected —— 两路相加才是完整口径。
    /// </summary>
    public class MotionBoardViewModel : BindableBase, IDialogAware
    {
        private readonly MotionProvider _provider;
        private readonly IWorkspaceManager _workspace;

        private DispatcherTimer? _refreshTimer;
        private DispatcherTimer? _noticeTimer;

        public MotionBoardViewModel(MotionProvider provider, IWorkspaceManager workspace)
        {
            _provider = provider;
            _workspace = workspace;

            Settings = new MotionBoardSettingsTabViewModel(provider, workspace, this);
            Debug = new MotionBoardDebugTabViewModel(provider, workspace, this);
            Points = new MotionBoardPointsTabViewModel(provider, workspace, this);
            Cam = new MotionBoardCamTabViewModel(provider, workspace, this);

            CloseCommand = new DelegateCommand(() => RequestClose.Invoke(ButtonResult.Cancel));
            OpenAddCardCommand = new DelegateCommand(OpenAddCard);
            CancelAddCardCommand = new DelegateCommand(CancelAddCard);
            ConfirmAddCardCommand = new DelegateCommand(ConfirmAddCard);
            ConfirmOkCommand = new DelegateCommand(() =>
            {
                var action = _confirmAction;
                CloseConfirm();
                action?.Invoke();
            });
            CancelConfirmCommand = new DelegateCommand(CloseConfirm);
        }

        /// <summary>四个页签 VM（构造时创建，生命周期与外壳一致）</summary>
        public MotionBoardSettingsTabViewModel Settings { get; }
        public MotionBoardDebugTabViewModel Debug { get; }
        public MotionBoardPointsTabViewModel Points { get; }
        public MotionBoardCamTabViewModel Cam { get; }

        public DelegateCommand CloseCommand { get; }

        #region 对话框契约

        public DialogCloseListener RequestClose { get; }

        public bool CanCloseDialog() => true;

        public void OnDialogOpened(IDialogParameters parameters)
        {
            _provider.EnsureSynced();
            _provider.DevicesChanged += OnDevicesChanged;

            Reload();

            // 空方案给一张默认卡，让首次打开不是一片空白（规格 2.1 空库 seed）
            var solution = _workspace.CurrentSolution;
            if (solution?.MotionCards != null && solution.MotionCards.Count == 0)
            {
                var driver = _provider.AvailableDrivers.FirstOrDefault();
                if (driver != null)
                {
                    var descriptor = new MotionDescriptor
                    {
                        DriverTypeKey = driver.TypeKey,
                        Address = "127.0.0.1",
                        DisplayName = driver.DisplayName,
                    };
                    solution.MotionCards.Add(descriptor);
                    _provider.MarkConfigDirty();
                    _provider.EnsureSynced();
                    Reload();
                }
            }

            // 电子凸轮表空库 seed（规格 6.4：一张五拐点默认表）
            if (solution != null && solution.CamTables.Count == 0)
                solution.CamTables.Add(MotionCamMath.CreateSeedTable());
            Cam.LoadTables();

            // 菜单「轴点位表」入口 → 直接落在点位列表页签
            if (parameters.TryGetValue("tab", out string tab))
                SelectedTabIndex = tab switch
                {
                    "debug" => 1,
                    "points" => 2,
                    "cam" => 3,
                    _ => 0,
                };

            // 200ms 刷新：读的是驱动轮询快照，不额外占通信（与旧调试面板同一口径）
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _refreshTimer.Tick += (_, _) => RefreshRuntime();
            _refreshTimer.Start();

            Debug.OnShellOpened();
        }

        public void OnDialogClosed()
        {
            _provider.DevicesChanged -= OnDevicesChanged;
            _refreshTimer?.Stop();
            _refreshTimer = null;
            _noticeTimer?.Stop();

            // 关窗必停点动（点动的终点是"松手"，窗都关了不会再有松手事件）
            Debug.OnShellClosed();
            Cam.OnShellClosed();
        }

        #endregion

        #region 左栏卡列表与选中卡

        public ObservableCollection<MotionBoardCardRow> Cards { get; } = new();

        private MotionBoardCardRow? _selectedCard;
        public MotionBoardCardRow? SelectedCard
        {
            get => _selectedCard;
            set
            {
                if (!SetProperty(ref _selectedCard, value)) return;

                // 选中卡只有一个事实来源：直接推给四个页签（它们只管跟着显示）
                var descriptor = value?.Descriptor;
                Settings.SelectedDescriptor = descriptor;
                Debug.SelectedDescriptor = descriptor;
                Points.SelectedDescriptor = descriptor;
                Cam.RefreshAxisOptions();
            }
        }

        /// <summary>重建卡列表（打开时与方案变化后）。选中尽量按 Id 保持</summary>
        public void Reload()
        {
            var keep = _selectedCard?.Id;
            Cards.Clear();
            foreach (var descriptor in _workspace.CurrentSolution?.MotionCards
                                            ?? new ObservableCollection<MotionDescriptor>())
            {
                var row = new MotionBoardCardRow(_provider, descriptor);
                row.RefreshRuntime();
                Cards.Add(row);
            }

            SelectedCard = Cards.FirstOrDefault(c => c.Id == keep) ?? Cards.FirstOrDefault();
            Settings.ReloadDriverOptions();
        }

        private void OnDevicesChanged(object? sender, EventArgs e)
        {
            // 设备事件在非 UI 线程触发（命令/轮询线程），必须调度回 UI 线程再碰集合
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;
            dispatcher.BeginInvoke(() =>
            {
                foreach (var row in Cards) row.RefreshRuntime();
            });
        }

        #endregion

        #region 页签

        /// <summary>页签标题（顺序即规格 1.1：卡设置 / 手动调试 / 点位列表 / 电子凸轮）</summary>
        public string[] TabLabels { get; } = { "卡设置", "手动调试", "点位列表", "电子凸轮" };

        private int _selectedTabIndex;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => SetProperty(ref _selectedTabIndex, value);
        }

        #endregion

        #region toast（右下角，规格 1.5）

        private string _noticeText = string.Empty;
        public string NoticeText
        {
            get => _noticeText;
            private set => SetProperty(ref _noticeText, value);
        }

        private bool _noticeIsError;
        public bool NoticeIsError
        {
            get => _noticeIsError;
            private set => SetProperty(ref _noticeIsError, value);
        }

        public bool HasNotice => NoticeText.Length > 0;

        /// <summary>成功类 toast（对勾 + 深字描边卡）</summary>
        public void NotifyOk(string text) => ShowNotice(text, isError: false);

        /// <summary>失败类 toast（警告图标 + 危险色文字）</summary>
        public void NotifyError(string text) => ShowNotice(text, isError: true);

        /// <summary>
        /// 闸门拒绝的统一出口：已拒绝 +1 + 红色 toast，文案固定「命令被拒绝：{原因}」。
        /// 原因字符串由各页签按规格闸门表逐字传入。
        /// </summary>
        public void Reject(string reason)
        {
            _sessionRejected++;
            ShowNotice($"命令被拒绝：{reason}", isError: true);
        }

        /// <summary>凸轮这类不经过设备的成功动作也计入"已执行"（与 Web 版口径一致）</summary>
        public void CountExecuted() => _sessionExecuted++;

        private void ShowNotice(string text, bool isError)
        {
            NoticeText = text;
            NoticeIsError = isError;
            RaisePropertyChanged(nameof(HasNotice));

            // 同屏只保留最新一条：重启计时器即可（旧 toast 被直接顶掉）
            _noticeTimer?.Stop();
            _noticeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3500) };
            _noticeTimer.Tick += (_, _) =>
            {
                _noticeTimer.Stop();
                NoticeText = string.Empty;
                RaisePropertyChanged(nameof(HasNotice));
            };
            _noticeTimer.Start();
        }

        #endregion

        #region 命令统计（聚合所有卡设备计数器 + 本会话本地计数）

        private long _sessionExecuted;
        private long _sessionRejected;

        private long _executedCount;
        public long ExecutedCount
        {
            get => _executedCount;
            private set => SetProperty(ref _executedCount, value);
        }

        private long _rejectedCount;
        public long RejectedCount
        {
            get => _rejectedCount;
            private set => SetProperty(ref _rejectedCount, value);
        }

        private long _faultCount;
        public long FaultCount
        {
            get => _faultCount;
            private set => SetProperty(ref _faultCount, value);
        }

        private void RefreshStats()
        {
            long executed = _sessionExecuted, rejected = _sessionRejected, fault = 0;
            foreach (var card in _workspace.CurrentSolution?.MotionCards
                                 ?? new ObservableCollection<MotionDescriptor>())
            {
                if (!_provider.TryGetDevice(card.Id, out var device)) continue;
                executed += device.ExecutedCommandCount;
                rejected += device.RejectedCommandCount;
                fault += device.FaultCount;
            }

            ExecutedCount = executed;
            RejectedCount = rejected;
            FaultCount = fault;
        }

        #endregion

        #region 居中确认遮罩（删除卡 / 删除轴）

        private string _confirmTitle = string.Empty;
        public string ConfirmTitle
        {
            get => _confirmTitle;
            private set => SetProperty(ref _confirmTitle, value);
        }

        private string _confirmMessage = string.Empty;
        public string ConfirmMessage
        {
            get => _confirmMessage;
            private set => SetProperty(ref _confirmMessage, value);
        }

        public bool HasConfirm => ConfirmTitle.Length > 0;

        private Action? _confirmAction;

        public DelegateCommand ConfirmOkCommand { get; }
        public DelegateCommand CancelConfirmCommand { get; }

        /// <summary>弹确认遮罩；点「确定」后执行 <paramref name="onOk"/>（点遮罩/取消则丢弃）</summary>
        public void RequestConfirm(string title, string message, Action onOk)
        {
            ConfirmTitle = title;
            ConfirmMessage = message;
            _confirmAction = onOk;
            RaisePropertyChanged(nameof(HasConfirm));
        }

        private void CloseConfirm()
        {
            ConfirmTitle = string.Empty;
            ConfirmMessage = string.Empty;
            _confirmAction = null;
            RaisePropertyChanged(nameof(HasConfirm));
        }

        #endregion

        #region 运行态刷新

        private void RefreshRuntime()
        {
            foreach (var row in Cards) row.RefreshRuntime();

            Settings.RefreshRuntime();
            Debug.RefreshRuntime();
            Points.RefreshRuntime();

            RefreshStats();
        }

        #endregion

        #region 添加运动卡（居中模态，规格 3.1/S1-3）

        private IReadOnlyList<MotionDriverInfo>? _availableDrivers;

        /// <summary>
        /// 驱动候选。**必须缓存同一份列表实例**：
        /// MotionProvider.AvailableDrivers 每次访问都新建 MotionDriverInfo 对象，
        /// 若 ItemsSource 与 SelectedItem 各取一次，两个"同名驱动"是不同实例 ——
        /// ComboBox 认为选中项不在列表里，显示框就是空的（值其实有效，纯显示 bug）。
        /// </summary>
        public IReadOnlyList<MotionDriverInfo> AvailableDrivers =>
            _availableDrivers ??= _provider.AvailableDrivers;

        private bool _isAddCardOpen;
        public bool IsAddCardOpen
        {
            get => _isAddCardOpen;
            private set => SetProperty(ref _isAddCardOpen, value);
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

        public DelegateCommand OpenAddCardCommand { get; }
        public DelegateCommand CancelAddCardCommand { get; }
        public DelegateCommand ConfirmAddCardCommand { get; }

        /// <summary>打开添加模态（表单重置为默认草稿，规格 3.2）</summary>
        public void OpenAddCard()
        {
            NewDriver = _provider.AvailableDrivers.FirstOrDefault();
            NewAddress = "192.168.0.11";
            NewDisplayName = string.Empty;
            NewCardModel = string.Empty;
            IsAddCardOpen = true;
        }

        public void CancelAddCard() => IsAddCardOpen = false;

        /// <summary>
        /// 确定添加：驱动必选 → 地址必填且全表唯一（流程按地址寻址，重复会让
        /// "命令发给 A 卡、动的是 B 卡"）→ 成功 toast + 选中新卡。
        /// </summary>
        public void ConfirmAddCard()
        {
            if (NewDriver == null)
            {
                NotifyError("请先选择驱动类型");
                return;
            }

            var address = (NewAddress ?? string.Empty).Trim();
            if (address.Length == 0)
            {
                NotifyError("请填写连接地址（正运动为卡 IP，如 192.168.0.11）");
                return;
            }

            if (!_provider.IsAddressAvailable(address, Guid.Empty))
            {
                NotifyError($"地址「{address}」已被另一张卡占用，请换一个（流程按地址寻址，必须唯一）");
                return;
            }

            var solution = _workspace.CurrentSolution;
            if (solution?.MotionCards == null)
            {
                NotifyError("当前没有打开的方案，无法保存运动卡配置");
                return;
            }

            var descriptor = new MotionDescriptor
            {
                DriverTypeKey = NewDriver.TypeKey,
                Address = address,
                DisplayName = string.IsNullOrWhiteSpace(NewDisplayName) ? NewDriver.DisplayName : NewDisplayName.Trim(),
                CardModel = (NewCardModel ?? string.Empty).Trim(),
            };
            solution.MotionCards.Add(descriptor);
            _provider.MarkConfigDirty();
            _provider.EnsureSynced();

            IsAddCardOpen = false;
            NotifyOk($"运动卡「{descriptor.Caption}」已添加");
            Reload();
            SelectedCard = Cards.FirstOrDefault(c => c.Id == descriptor.Id);
        }

        #endregion
    }
}
