using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;
using Core.Interfaces;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Ioc;
using Prism.Mvvm;
using VisionMaster.Communications;
using VisionMaster.Services;
using VisionMaster.Services.Hardware;
using VisionMaster.Services.Health;
using VisionMaster.Views.DialogViews;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 硬件管理外壳（批次 C）：左栏域导航 + 只读概览 + 三个<b>嵌入</b>页面。
    ///
    /// 嵌入不是"把 UserControl 塞进 TabControl"这么简单：三个域 VM 都是 IDialogAware，
    /// 定时器、设备事件订阅、关闭持久化都绑在 OnDialogOpened/OnDialogClosed 上。
    /// 本类通过 <see cref="IEmbeddedDialogPage"/> 把生命周期拆成：
    /// <list type="bullet">
    /// <item>懒创建时 Opened 一次（转调 OnDialogOpened）；</item>
    /// <item>切页时 Activated/Deactivated 多次（只停/启本页 UI 定时器与点动看门狗）；</item>
    /// <item>宿主关闭时 Closed 恰好一次（转调 OnDialogClosed）。</item>
    /// </list>
    /// 三个域 VM 的 RequestClose 一律不碰（运动那个还是 get-only）；关闭由本宿主 footer 负责。
    ///
    /// 权限：宿主把 <see cref="IHardwareEditGate"/> 的结果推给三个页面；
    /// 页面只读属性负责禁用 TwoWay 配置区，运行操作由外层容器的 IsEnabled 拦住。
    /// </summary>
    public sealed class HardwareManagerViewModel : BindableBase, IDialogAware
    {
        private readonly IDialogService _dialogs;
        private readonly IWorkspaceManager _workspace;
        private readonly IUserNotifier _notifier;
        private readonly AdvancedCommunicationManager _communicationManager;
        private readonly IHardwareEditGate _gate;
        private readonly IContainerProvider _container;

        private readonly IHealthPanel _cameraPanel;
        private readonly IHealthPanel _commPanel;
        private readonly IHealthPanel _motionPanel;

        private readonly DispatcherTimer _timer;
        private string _issueSignature = string.Empty;

        private int _selectedPage;
        private int _issueCount;
        private string _issueSummary = "正在读取设备状态…";
        private string _lastRefreshText = string.Empty;
        private string _overallLevelKey = "idle";

        private bool _isConfigEditable = true;
        private bool _isRuntimeOperable = true;
        private string? _permissionHint;

        private EmbeddedPage? _cameraPage;
        private EmbeddedPage? _commPage;
        private EmbeddedPage? _motionPage;
        private EmbeddedPage? _databasePage;
        private string? _pendingMotionTab;
        private bool _isClosing;
        private bool _closeSaveInProgress;

        public HardwareManagerViewModel(
            ICameraProvider cameraProvider,
            AdvancedCommunicationManager communicationManager,
            IMotionProvider motionProvider,
            IWorkspaceManager workspace,
            IDialogService dialogs,
            IUserNotifier notifier,
            IHardwareEditGate gate,
            IContainerProvider container)
        {
            _communicationManager = communicationManager ?? throw new ArgumentNullException(nameof(communicationManager));
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
            _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
            _container = container ?? throw new ArgumentNullException(nameof(container));

            // 独立 Monitor 实例：SystemHealthView 用的是容器里的单例，两处共用会让
            // "两次采样之间新增帧数"的基线互相覆盖，帧率/告警会随机跳。
            _cameraPanel = new CameraHealthPanelViewModel(new CameraHealthMonitor(cameraProvider));
            _commPanel = new CommunicationHealthPanelViewModel(new CommunicationHealthMonitor(communicationManager));
            _motionPanel = new MotionHealthPanelViewModel(new MotionHealthMonitor(motionProvider));

            OpenCameraCommand = new DelegateCommand(OpenCamera);
            OpenCommunicationCommand = new DelegateCommand(OpenCommunication);
            OpenMotionCommand = new DelegateCommand(OpenMotion);
            OpenSystemHealthCommand = new DelegateCommand(OpenSystemHealth);
            MotionEmergencyStopCommand = new DelegateCommand(
                EmergencyStopMotion,
                () => _motionPage is { Opened: true, Closed: false });
            RefreshCommand = new DelegateCommand(() => RefreshAll(force: true));
            CloseCommand = new DelegateCommand(async () => await CloseHostAsync());

            CameraCard = new HardwareDomainCard("相机", "\uf030", 1, OpenCameraCommand);
            CommunicationCard = new HardwareDomainCard("通讯", "\uf1e6", 2, OpenCommunicationCommand);
            MotionCard = new HardwareDomainCard("运动控制", "\uf085", 3, OpenMotionCommand);

            NavItems.Add(new HardwareNavItem("概览", "\uf00b", 0));
            NavItems.Add(new HardwareNavItem("相机", "\uf030", 1));
            NavItems.Add(new HardwareNavItem("通讯", "\uf1e6", 2));
            NavItems.Add(new HardwareNavItem("运动控制", "\uf085", 3));
            NavItems.Add(new HardwareNavItem("数据库", "\uf1c0", 4));

            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += (_, _) => RefreshAll(force: false);
        }

        public string Title => "硬件管理";

        public DialogCloseListener RequestClose { get; set; }

        public ObservableCollection<HardwareNavItem> NavItems { get; } = new();

        public ObservableCollection<HardwareIssueRow> Issues { get; } = new();

        public HardwareDomainCard CameraCard { get; }
        public HardwareDomainCard CommunicationCard { get; }
        public HardwareDomainCard MotionCard { get; }

        public DelegateCommand OpenCameraCommand { get; }
        public DelegateCommand OpenCommunicationCommand { get; }
        public DelegateCommand OpenMotionCommand { get; }
        public DelegateCommand OpenSystemHealthCommand { get; }
        public DelegateCommand MotionEmergencyStopCommand { get; }
        public DelegateCommand RefreshCommand { get; }
        public DelegateCommand CloseCommand { get; }

        public UserControl? CameraPageView => _cameraPage?.View;
        public UserControl? CommunicationPageView => _commPage?.View;
        public UserControl? MotionPageView => _motionPage?.View;
        public UserControl? DatabasePageView => _databasePage?.View;

        public int SelectedPage
        {
            get => _selectedPage;
            set
            {
                var clamped = Math.Max(0, Math.Min(4, value));
                if (!SetProperty(ref _selectedPage, clamped)) return;
                OnSelectedPageChanged();
            }
        }

        public int IssueCount
        {
            get => _issueCount;
            private set
            {
                if (!SetProperty(ref _issueCount, value)) return;
                RaisePropertyChanged(nameof(HasIssues));
                RaisePropertyChanged(nameof(IsHealthy));
            }
        }

        public bool HasIssues => IssueCount > 0;

        public bool IsHealthy => IssueCount == 0;

        public string IssueSummary
        {
            get => _issueSummary;
            private set => SetProperty(ref _issueSummary, value);
        }

        public string LastRefreshText
        {
            get => _lastRefreshText;
            private set => SetProperty(ref _lastRefreshText, value);
        }

        public string OverallLevelKey
        {
            get => _overallLevelKey;
            private set => SetProperty(ref _overallLevelKey, value);
        }

        public string SolutionName => _workspace.CurrentSolution?.SolutionName ?? "未加载方案";

        public bool IsSolutionMissing => _workspace.CurrentSolution == null;

        public bool IsConfigEditable
        {
            get => _isConfigEditable;
            private set => SetProperty(ref _isConfigEditable, value);
        }

        public bool IsRuntimeOperable
        {
            get => _isRuntimeOperable;
            private set => SetProperty(ref _isRuntimeOperable, value);
        }

        public string? PermissionHint
        {
            get => _permissionHint;
            private set
            {
                if (!SetProperty(ref _permissionHint, value)) return;
                RaisePropertyChanged(nameof(HasPermissionHint));
            }
        }

        public bool HasPermissionHint => !string.IsNullOrWhiteSpace(PermissionHint);

        public bool CanCloseDialog()
        {
            if (_isClosing) return true;
            if (!CanClosePages()) return false;

            // 通讯脏时不允许直接关：先异步保存，成功且期间无新改动后再真正关闭。
            // 返回 false 会取消本次关闭；SaveAndCloseAsync 完成后重新触发 RequestClose。
            if (_commPage?.Page is CommunicationSettingsViewModel comm && comm.IsCommunicationDirty)
            {
                if (_closeSaveInProgress) return false;
                _closeSaveInProgress = true;
                _ = SaveAndCloseAsync(comm);
                return false;
            }

            return true;
        }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            if (parameters != null)
            {
                if (parameters.TryGetValue("tab", out string tab) && !string.IsNullOrWhiteSpace(tab))
                    _pendingMotionTab = tab;

                if (parameters.TryGetValue("page", out string page))
                {
                    SelectedPage = page switch
                    {
                        "camera" => 1,
                        "comm" => 2,
                        "motion" => 3,
                        _ => 0,
                    };
                }
                else if (_pendingMotionTab != null)
                {
                    SelectedPage = 3;
                }
            }

            RefreshAll(force: false);
            _timer.Start();
        }

        public void OnDialogClosed()
        {
            _timer.Stop();

            // 正常路径（CloseCommand / CanCloseDialog）已经保存过；这里是 Alt+F4 等旁路的最后兜底。
            // 保存进行中就不再起第二路：两路同时写同一个文件只会互相覆盖。
            if (!_closeSaveInProgress
                && _commPage?.Page is CommunicationSettingsViewModel comm
                && comm.IsCommunicationDirty)
            {
                _ = SaveCommunicationAsync(comm);
            }

            ClosePages();
            ResetEmbeddedPages();
        }

        /// <summary>
        /// 清空页面引用，保证二次打开一定重新建页。
        ///
        /// 宿主 VM 的生命周期由容器的 RegisterDialog 决定（当前是瞬态，但这不是本类能假定的）：
        /// 若哪天变成单例，已 Closed 的页面记录会被 EnsurePage 当成"已存在"直接返回，
        /// 新内容永远不创建，界面停在空白页。
        /// </summary>
        private void ResetEmbeddedPages()
        {
            _cameraPage = null;
            _commPage = null;
            _motionPage = null;
            _databasePage = null;
            _pendingMotionTab = null;
            _isClosing = false;
            _closeSaveInProgress = false;
            SelectedPage = 0;

            RaisePropertyChanged(nameof(CameraPageView));
            RaisePropertyChanged(nameof(CommunicationPageView));
            RaisePropertyChanged(nameof(MotionPageView));
            RaisePropertyChanged(nameof(DatabasePageView));
            MotionEmergencyStopCommand.RaiseCanExecuteChanged();
        }

        private void OnSelectedPageChanged()
        {
            if (SelectedPage == 0)
            {
                DeactivateAllPages();
                return;
            }

            var page = EnsurePage(SelectedPage);
            if (page == null) return;

            ActivatePage(page);
        }

        private EmbeddedPage? EnsurePage(int index)
        {
            var existing = index switch
            {
                1 => _cameraPage,
                2 => _commPage,
                3 => _motionPage,
                4 => _databasePage,
                _ => null,
            };
            if (existing != null) return existing;

            UserControl view = index switch
            {
                1 => new CameraSettingsView(),
                2 => new CommunicationSettingsView(),
                3 => new MotionBoardView(),
                4 => new DatabasePageView(),
                _ => throw new ArgumentOutOfRangeException(nameof(index)),
            };

            EmbeddedMode.SetIsEmbedded(view, true);

            // 优先用 Prism AutoWire 已经装好的 VM；取不到再显式 Resolve。
            // 后者是兜底：正常路径下 view.DataContext 已经是目标 VM，不会再造第二份。
            var page = view.DataContext as IEmbeddedDialogPage;
            if (page == null)
            {
                var vmType = index switch
                {
                    1 => typeof(CameraSettingsViewModel),
                    2 => typeof(CommunicationSettingsViewModel),
                    3 => typeof(MotionBoardViewModel),
                    4 => typeof(DatabasePageViewModel),
                    _ => throw new ArgumentOutOfRangeException(nameof(index)),
                };
                var vm = _container.Resolve(vmType);
                view.DataContext = vm;
                page = vm as IEmbeddedDialogPage;
            }

            if (page == null)
            {
                _notifier.ShowError("硬件页面缺少嵌入生命周期实现，已取消打开该页。");
                return null;
            }

            var host = new EmbeddedPage(view, page, index);
            try
            {
                ApplyGateToPage(page);
                page.OnEmbeddedOpened(BuildPageParameters(index));
                host.Opened = true;
                host.Active = true;
            }
            catch (Exception ex)
            {
                // 初始化半途失败也要把 Closed 走到，避免订阅/定时器留在单例上。
                try { page.OnEmbeddedClosed(); } catch { /* 已在失败路径，关闭异常不再外抛 */ }
                _notifier.ShowError($"打开硬件页面失败：{ex.Message}");
                return null;
            }

            switch (index)
            {
                case 1: _cameraPage = host; break;
                case 2: _commPage = host; break;
                case 3: _motionPage = host; break;
                case 4: _databasePage = host; break;
            }

            RaisePropertyChanged(nameof(CameraPageView));
            RaisePropertyChanged(nameof(CommunicationPageView));
            RaisePropertyChanged(nameof(MotionPageView));
            RaisePropertyChanged(nameof(DatabasePageView));
            MotionEmergencyStopCommand.RaiseCanExecuteChanged();
            return host;
        }

        private DialogParameters BuildPageParameters(int index)
        {
            var parameters = new DialogParameters();
            if (index == 3 && !string.IsNullOrWhiteSpace(_pendingMotionTab))
            {
                parameters.Add("tab", _pendingMotionTab);
                _pendingMotionTab = null;
            }
            return parameters;
        }

        private void ActivatePage(EmbeddedPage page)
        {
            foreach (var other in AllPages())
            {
                if (other == null || other == page || !other.Opened || !other.Active) continue;
                other.Page.OnEmbeddedDeactivated();
                other.Active = false;
            }

            if (page.Opened && !page.Active && !page.Closed)
            {
                page.Page.OnEmbeddedActivated();
                page.Active = true;
            }
        }

        private void DeactivateAllPages()
        {
            foreach (var page in AllPages())
            {
                if (page == null || !page.Opened || !page.Active) continue;
                page.Page.OnEmbeddedDeactivated();
                page.Active = false;
            }
        }

        private void ClosePages()
        {
            foreach (var page in AllPages())
            {
                if (page == null || !page.Opened || page.Closed) continue;
                try
                {
                    page.Page.OnEmbeddedClosed();
                }
                catch (Exception ex)
                {
                    _notifier.ShowError($"关闭硬件页面失败：{ex.Message}");
                }
                finally
                {
                    page.Closed = true;
                    page.Active = false;
                }
            }
        }

        private IEnumerable<EmbeddedPage?> AllPages()
        {
            yield return _cameraPage;
            yield return _commPage;
            yield return _motionPage;
            yield return _databasePage;
        }

        private bool CanClosePages()
        {
            foreach (var page in AllPages())
            {
                if (page == null || !page.Opened || page.Closed) continue;
                try
                {
                    if (!page.Page.CanCloseEmbedded(out var reason))
                    {
                        _notifier.ShowWarn(reason ?? "当前页面不允许关闭");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    _notifier.ShowError($"关闭前检查失败：{ex.Message}");
                    return false;
                }
            }
            return true;
        }

        private async Task CloseHostAsync()
        {
            if (_isClosing || _closeSaveInProgress) return;
            if (!CanClosePages()) return;

            if (_commPage?.Page is CommunicationSettingsViewModel comm && comm.IsCommunicationDirty)
            {
                _closeSaveInProgress = true;
                var version = comm.DirtyVersion;
                try
                {
                    await _communicationManager.SaveConfigAsync();
                    comm.MarkCommunicationClean(version);
                }
                catch (Exception ex)
                {
                    _notifier.ShowError($"保存通讯配置失败：{ex.Message}");
                    _closeSaveInProgress = false;
                    return; // 保存失败不关宿主，用户还有机会重试
                }
                _closeSaveInProgress = false;

                if (comm.IsCommunicationDirty)
                {
                    _notifier.ShowWarn("保存期间通讯配置又有改动，请再点一次关闭。");
                    return;
                }
            }

            _isClosing = true;
            try
            {
                ClosePages();
                RequestClose.Invoke(new DialogParameters(), ButtonResult.Cancel);
            }
            catch (Exception ex)
            {
                _notifier.ShowError($"关闭硬件管理失败：{ex.Message}");
            }
            finally
            {
                _isClosing = false;
            }
        }

        /// <summary>
        /// 窗口级关闭（标题栏 X / Alt+F4）的保存后重关路径。
        /// CanCloseDialog 返回 false 取消本次关闭；这里保存成功后重新触发 RequestClose。
        /// </summary>
        private async Task SaveAndCloseAsync(CommunicationSettingsViewModel comm)
        {
            var version = comm.DirtyVersion;
            try
            {
                await _communicationManager.SaveConfigAsync();
                comm.MarkCommunicationClean(version);
            }
            catch (Exception ex)
            {
                _notifier.ShowError($"保存通讯配置失败：{ex.Message}");
                _closeSaveInProgress = false;
                return;
            }

            _closeSaveInProgress = false;
            if (comm.IsCommunicationDirty)
            {
                _notifier.ShowWarn("保存期间通讯配置又有改动，请再点一次关闭。");
                return;
            }

            _isClosing = true;
            try
            {
                ClosePages();
                RequestClose.Invoke(new DialogParameters(), ButtonResult.Cancel);
            }
            catch (Exception ex)
            {
                _notifier.ShowError($"关闭硬件管理失败：{ex.Message}");
            }
            finally
            {
                _isClosing = false;
            }
        }

        private async Task SaveCommunicationAsync(CommunicationSettingsViewModel comm)
        {
            var version = comm.DirtyVersion;
            try
            {
                await _communicationManager.SaveConfigAsync();
                comm.MarkCommunicationClean(version);
            }
            catch (Exception ex)
            {
                _notifier.ShowError($"保存通讯配置失败：{ex.Message}");
            }
        }

        private void RefreshAll(bool force)
        {
            if (force)
            {
                _cameraPanel.ForceRefresh();
                _commPanel.ForceRefresh();
                _motionPanel.ForceRefresh();
            }
            else
            {
                _cameraPanel.Refresh();
                _commPanel.Refresh();
                _motionPanel.Refresh();
            }

            CameraCard.Update(_cameraPanel);
            CommunicationCard.Update(_commPanel);
            MotionCard.Update(_motionPanel);

            var issues = new List<HealthIssue>();
            issues.AddRange(_cameraPanel.CollectIssues());
            issues.AddRange(_commPanel.CollectIssues());
            issues.AddRange(_motionPanel.CollectIssues());

            var ordered = issues
                .OrderByDescending(i => (int)i.Level)
                .ThenBy(i => i.Domain, StringComparer.Ordinal)
                .ThenBy(i => i.Target, StringComparer.Ordinal)
                .Take(12)
                .ToList();

            IssueCount = issues.Count(i => i.Level is HealthLevel.Warn or HealthLevel.Error);

            // 稳定键对齐：没有变化时不动集合，避免每秒清空重建导致滚动位置/选中态丢失。
            var signature = string.Join("|", ordered.Select(i => i.Key))
                            + "#" + _cameraPanel.LevelKey + _commPanel.LevelKey + _motionPanel.LevelKey
                            + "#" + _cameraPanel.IssueCount + _commPanel.IssueCount + _motionPanel.IssueCount;
            if (!string.Equals(signature, _issueSignature, StringComparison.Ordinal))
            {
                _issueSignature = signature;
                Issues.Clear();
                var now = DateTime.Now.ToString("HH:mm:ss");
                foreach (var issue in ordered)
                    Issues.Add(new HardwareIssueRow(issue.Domain, issue.Target, issue.Message, issue.Level, now));
            }

            OverallLevelKey = HealthLevels.Key(HealthLevels.Aggregate(
                new[] { _cameraPanel.Level, _commPanel.Level, _motionPanel.Level }));
            IssueSummary = IssueCount == 0 ? "全部设备状态正常" : $"{IssueCount} 项待处理";
            LastRefreshText = $"更新 {DateTime.Now:HH:mm:ss}";

            NavItems[0].IssueCount = IssueCount;
            NavItems[1].IssueCount = _cameraPanel.IssueCount;
            NavItems[2].IssueCount = _commPanel.IssueCount;
            NavItems[3].IssueCount = _motionPanel.IssueCount;

            // 权限门禁每拍重算：空闲超时登出 / 流程启停都会在这里翻面。
            IsConfigEditable = _gate.CanEditConfig;
            IsRuntimeOperable = _gate.CanOperateRuntime;
            // 两个原因都上屏：操作员既不能运行操作、也不能改配置；只显示配置原因会误导成"能连设备"。
            PermissionHint = string.Join("；", new[] { _gate.RuntimeReason, _gate.ConfigReason }
                .Where(r => !string.IsNullOrWhiteSpace(r)));
            ApplyGateToPages();

            RaisePropertyChanged(nameof(SolutionName));
            RaisePropertyChanged(nameof(IsSolutionMissing));
        }

        private void ApplyGateToPages()
        {
            foreach (var page in AllPages())
            {
                if (page == null || page.Closed) continue;
                ApplyGateToPage(page.Page);
            }
        }

        private void ApplyGateToPage(IEmbeddedDialogPage page)
        {
            if (page is IHardwareEditGateAware aware)
                aware.ApplyEditGate(_gate.CanEditConfig, _gate.CanOperateRuntime, _gate.ConfigReason, _gate.RuntimeReason);
        }

        private bool EnsureSolution()
        {
            if (_workspace.CurrentSolution != null) return true;
            _notifier.ShowWarn("请先打开一个解决方案");
            return false;
        }

        private void OpenCamera()
        {
            if (!EnsureSolution()) return;
            SelectedPage = 1;
        }

        private void OpenCommunication()
        {
            if (!EnsureSolution()) return;
            SelectedPage = 2;
        }

        private void OpenMotion()
        {
            if (!EnsureSolution()) return;
            SelectedPage = 3;
        }

        private void OpenSystemHealth() => _dialogs.ShowDialog("SystemHealthView");

        private void EmergencyStopMotion()
        {
            if (_motionPage?.Page is MotionBoardViewModel motion)
                motion.Debug.EmergencyStopCommand.Execute();
        }

        /// <summary>一个已创建页面的宿主记录；Opened/Closed 由外壳保证各一次。</summary>
        private sealed class EmbeddedPage
        {
            public EmbeddedPage(UserControl view, IEmbeddedDialogPage page, int index)
            {
                View = view;
                Page = page;
                Index = index;
            }

            public UserControl View { get; }
            public IEmbeddedDialogPage Page { get; }
            public int Index { get; }
            public bool Opened { get; set; }
            public bool Active { get; set; }
            public bool Closed { get; set; }
        }
    }

    /// <summary>左栏导航项。Count 只显示"异常数"，0 时整块隐藏。</summary>
    public sealed class HardwareNavItem : BindableBase
    {
        private int _issueCount;

        public HardwareNavItem(string title, string icon, int pageIndex)
        {
            Title = title;
            Icon = icon;
            PageIndex = pageIndex;
        }

        public string Title { get; }
        public string Icon { get; }
        public int PageIndex { get; }

        public int IssueCount
        {
            get => _issueCount;
            set
            {
                if (!SetProperty(ref _issueCount, value)) return;
                RaisePropertyChanged(nameof(CountText));
                RaisePropertyChanged(nameof(HasCount));
            }
        }

        public string CountText => IssueCount > 0 ? IssueCount.ToString() : string.Empty;

        public bool HasCount => IssueCount > 0;
    }

    /// <summary>概览三张域卡。LevelKey 供 XAML 的 ok/warn/error/idle 触发器使用。</summary>
    public sealed class HardwareDomainCard : BindableBase
    {
        private string _summary = "读取中…";
        private string _detail = string.Empty;
        private string _levelKey = "idle";
        private string _levelText = "未启用";
        private bool _hasData;
        private int _issueCount;

        public HardwareDomainCard(string title, string icon, int pageIndex, DelegateCommand openCommand)
        {
            Title = title;
            Icon = icon;
            PageIndex = pageIndex;
            OpenCommand = openCommand;
        }

        public string Title { get; }
        public string Icon { get; }
        public int PageIndex { get; }
        public DelegateCommand OpenCommand { get; }

        public string Summary
        {
            get => _summary;
            private set => SetProperty(ref _summary, value);
        }

        public string Detail
        {
            get => _detail;
            private set => SetProperty(ref _detail, value);
        }

        public string LevelKey
        {
            get => _levelKey;
            private set => SetProperty(ref _levelKey, value);
        }

        public string LevelText
        {
            get => _levelText;
            private set => SetProperty(ref _levelText, value);
        }

        public bool HasData
        {
            get => _hasData;
            private set
            {
                if (!SetProperty(ref _hasData, value)) return;
                RaisePropertyChanged(nameof(IssueCountText));
            }
        }

        public int IssueCount
        {
            get => _issueCount;
            private set
            {
                if (!SetProperty(ref _issueCount, value)) return;
                RaisePropertyChanged(nameof(IssueCountText));
                RaisePropertyChanged(nameof(HasIssues));
            }
        }

        public string IssueCountText => !HasData
            ? "未配置"
            : IssueCount > 0 ? $"{IssueCount} 项异常" : "无异常";

        public bool HasIssues => IssueCount > 0;

        public void Update(IHealthPanel panel)
        {
            HasData = panel.HasData;
            Summary = panel.Summary;
            // 未配置时 Summary 已经说明“当前方案未配置…”，Detail 再写一遍只是重复。
            Detail = panel.HasData ? panel.Detail : string.Empty;
            LevelKey = panel.LevelKey;
            LevelText = HealthLevels.Text(panel.Level);
            IssueCount = panel.IssueCount;
        }
    }

    /// <summary>概览"当前异常"的一行。Key 由 HealthIssue 的稳定键派生，不参与显示。</summary>
    public sealed class HardwareIssueRow
    {
        public HardwareIssueRow(string domain, string target, string message, HealthLevel level, string timeText)
        {
            Domain = domain;
            Target = target;
            Message = message;
            Level = level;
            TimeText = timeText;
        }

        public string Domain { get; }
        public string Target { get; }
        public string Message { get; }
        public HealthLevel Level { get; }
        public string TimeText { get; }

        public string Title => $"{Domain} · {Target}";
        public string LevelKey => HealthLevels.Key(Level);
        public string LevelText => HealthLevels.Text(Level);
    }
}
