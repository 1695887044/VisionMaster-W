using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
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
            private set
            {
                if (SetProperty(ref _isOnline, value))
                    RaisePropertyChanged(nameof(OnlineText));
            }
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

        /// <summary>全局轴名注册表（轴名 → 卡+轴号 的唯一权威）</summary>
        private readonly MotionAxisRegistry _axes;

        private DispatcherTimer? _refreshTimer;
        private DispatcherTimer? _noticeTimer;

        public MotionBoardViewModel(MotionProvider provider, IWorkspaceManager workspace, MotionAxisRegistry axes)
        {
            _provider = provider;
            _workspace = workspace;
            _axes = axes;

            Settings = new MotionBoardSettingsTabViewModel(provider, workspace, this, axes);
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

            // ★ 空库**不再自动补一张卡**（原规格 2.1 的空库 seed 已废弃）。
            //
            // 它带来的问题比解决的问题大：
            //   ① 用户删光所有卡之后，关掉面板再打开，面板又悄悄补一张 ——
            //      "我想清空运动卡"这个操作根本做不到，且没有任何提示告诉他为什么又冒出来一张；
            //   ② 补出来的卡会随方案落盘，成为"用户从没主动创建过"的脏数据
            //      （早期那张默认虚拟卡就是这么进方案文件的，后面花了两轮才清掉）。
            // 空态本身是完整的：左栏有"添加"按钮，设置页签提示"请在左侧选择一张运动卡"。
            // 需要卡就点「添加」——由用户主动创建，而不是替他做决定。
            var solution = _workspace.CurrentSolution;

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
            UiDispatcher.Post(() =>
            {
                foreach (var row in Cards) row.RefreshRuntime();
            });
        }

        #endregion

        #region 页签

        /// <summary>页签标题（顺序即规格 1.1：卡设置 / 手动调试 / 点位列表 / 电子凸轮）</summary>
        public string[] TabLabels { get; } = { "卡设置", "手动调试", "点位列表", "电子凸轮" };

        /// <summary>
        /// 「卡设置」里行内改了轴名 —— 先做**级联**，再让三个消费方跟一步：
        ///   ① 点位表以轴名为外键，改名必须带着点位一起走（<see cref="MotionDescriptor.RenameAxis"/>）；
        ///   ② 凸轮表的主/从轴是「卡名 · 轴名」标签，改名即失效，必须同步改过去；
        ///   ③ 调试/点位页签的轴行是 Mapping 的包装（同一引用），只需补一次属性通知；
        ///   ④ 凸轮页签的下拉候选直接重建。
        ///
        /// 顺序不能颠倒：① ② 都按**旧名**定位，等名字改完了就找不到了 ——
        /// 那正是"改个轴名，教好的点位全没了"的根因。
        /// </summary>
        public void OnAxisLogicalNameChanged(string oldName, string newName, string detail)
        {
            // ★ 级联（点位外键 / 凸轮标签 / 索引重排）已经在
            //   MotionAxisRegistry.Rename 里一次性做完了。
            //   这里只负责"让跟着显示的界面跟上"——级联逻辑必须只有一份，
            //   否则迟早出现"这条路径忘了级联"的第三种写法。
            Debug.NotifyAxisNamesChanged();
            Points.NotifyAxisNamesChanged();
            Cam.RefreshAxisOptions();

            if (!string.IsNullOrWhiteSpace(detail))
                NotifyOk(detail);
        }

        private int _selectedTabIndex;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (!SetProperty(ref _selectedTabIndex, value))
                    return;

                // 切入手动调试页签时重扫轴列表：轴映射可能在「卡设置」里刚被
                // 连接后的能力铺齐（32 轴）或改名过，不重扫就会显示旧快照/空列表
                if (value == 1)
                    Debug.ReloadAxesAndIo();
            }
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

        /// <summary>
        /// 「轴集合发生变化」（新增轴 / 删除轴）的统一出口。
        ///
        /// 与 <see cref="OnAxisLogicalNameChanged"/> 的区别必须分清：
        ///   改名 = 同一批轴换个名字 → 各页签补一次通知就够（NotifyAxisNamesChanged）；
        ///   增删 = 轴的行数变了 → 所有页签的轴列表都得**整表重建**，只补通知不会有新行。
        /// 少了这一步的表现就是"在卡设置里加了轴，点位列表/手动调试里看不见"。
        /// </summary>
        public void NotifyAxisSetChanged(string detail)
        {
            Points.ReloadAxisRows();
            Debug.ReloadAxesAndIo();
            Cam.RefreshAxisOptions();

            if (!string.IsNullOrWhiteSpace(detail))
                NotifyOk(detail);
        }

        /// <summary>
        /// 连接成功的统一出口（「卡设置」与「手动调试」两个页签的连接按钮都走这里）：
        /// ① toast 报告扫描到的轴数（连上 32 轴卡必须让用户第一眼知道，而不是静默铺表）；
        /// ② 立即重建手动调试页签的轴列表 —— 连接后轴映射可能刚按能力补齐，
        ///    不重建的话调试页签还停在连接前的旧快照。
        /// </summary>
        public void NotifyCardConnected(string caption, int axisCount)
        {
            NotifyOk(axisCount > 0
                ? $"「{caption}」已连接，扫描到 {axisCount} 个轴"
                : $"「{caption}」已连接");

            Debug.ReloadAxesAndIo();
        }

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

        #region 删卡（唯一一份级联）与出厂默认仿真卡识别

        /// <summary>
        /// 删卡。**级联只有这一份**（凸轮表的「卡名 · 轴名」标签必须跟着清，
        /// 否则留下一批指向不存在轴的悬垂引用），「卡设置」页签的删除按钮也走这里。
        /// </summary>
        public void RemoveCard(MotionDescriptor descriptor)
        {
            var solution = _workspace.CurrentSolution;
            var target = solution?.MotionCards?.FirstOrDefault(c => c.Id == descriptor.Id);
            if (solution == null || target == null) return;

            // 注册表是派生索引，先刷新一次再取快照，否则可能按旧索引清错标签
            _axes.Reload();
            foreach (var binding in _axes.Snapshot().Where(b => b.CardId == descriptor.Id))
            {
                MotionCamAxisRefs.ClearAxisLabels(
                    solution.CamTables, MotionCamAxisRefs.Label(descriptor.Caption, binding.Name));
            }

            solution.MotionCards.Remove(target);
            _provider.MarkConfigDirty();
            _provider.EnsureSynced();   // 删卡必须立刻断开物理连接

            NotifyOk($"运动卡「{descriptor.Caption}」已删除");
            Reload();
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

        /// <summary>
        /// 默认驱动：**第一个真实驱动**（仿真驱动排在最后，见 MotionProvider.AvailableDrivers 的排序）。
        /// 一个真实驱动都没有时返回 null —— 此时"添加卡"要求用户显式选驱动，
        /// 空方案也不再 seed（不给默认虚拟卡）。
        ///
        /// ★ 必须是 AvailableDrivers **列表里的那个实例**：下拉的 ItemsSource 与 SelectedItem
        ///   若取自两次不同的枚举（MotionDriverInfo 每次都新建），ComboBox 认为选中项不在列表里，
        ///   显示框会空掉，用户"以为选好了"的驱动和实际提交的可能不是同一个。
        /// </summary>
        private MotionDriverInfo? DefaultDriver =>
            AvailableDrivers.FirstOrDefault(d => !d.IsSimulated);

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
            // 默认驱动取真实驱动（仿真驱动要用户显式选择），
            // 且必须取列表里那个实例（否则下拉显示为空，见 DefaultDriver 的说明）
            NewDriver = DefaultDriver;
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
                // 把"被谁占了"说出来：只说"已被占用"时，用户不知道该去左栏删哪一张
                var owner = _workspace.CurrentSolution?.MotionCards?.FirstOrDefault(c =>
                    string.Equals((c.Address ?? string.Empty).Trim(), address, StringComparison.OrdinalIgnoreCase));

                NotifyError(owner == null
                    ? $"地址「{address}」已被另一张卡占用，请换一个（流程按地址寻址，必须唯一）"
                    : $"地址「{address}」已被运动卡「{owner.Caption}」占用：请换一个地址，或先在左栏删除那张卡");
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
            // toast 里带上驱动名：选了什么驱动必须让用户当场能核对，
            // 否则"我选的是正运动，怎么加出来是虚拟卡"只能靠猜
            NotifyOk($"运动卡「{descriptor.Caption}」（{NewDriver!.DisplayName}）已添加");
            Reload();
            SelectedCard = Cards.FirstOrDefault(c => c.Id == descriptor.Id);
        }

        #endregion
    }
}
