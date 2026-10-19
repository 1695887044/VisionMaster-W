using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Services.Health;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>概览页的域摘要卡。</summary>
    public sealed record HealthDomainCard(
        string Domain,
        string Title,
        string LevelKey,
        string LevelText,
        string CountText,
        string Note);

    /// <summary>概览页的异常行（跨域统一投影）。</summary>
    public sealed record HealthIssueRow(
        string Domain,
        string Target,
        string LevelKey,
        string LevelText,
        string Message,
        string Metric)
    {
        public static HealthIssueRow From(HealthIssue issue) => new(
            issue.Domain, issue.Target,
            HealthLevels.Key(issue.Level), HealthLevels.Text(issue.Level),
            issue.Message, issue.Metric);
    }

    /// <summary>
    /// 系统健康弹窗壳：5 个页签（概览 / 通讯 / 相机 / 运动 / IO）。
    ///
    /// <para><b>定时器只有一个，在这里。</b>子面板不建定时器、不实现 <c>IDialogAware</c>，
    /// 只提供 <c>Refresh/ForceRefresh/CollectIssues</c>。否则一个弹窗会有 5 个 1s 定时器、
    /// 5 份不同步的快照，关闭时还容易漏停。</para>
    ///
    /// <para><b>刷新策略</b>：概览页刷新全部四域；停在某个域页签时只刷新该域
    /// （IO 点位多时，这一条能避免每秒物化几千行）。</para>
    /// </summary>
    public sealed class SystemHealthViewModel : BindableBase, IDialogAware
    {
        private readonly CommunicationHealthPanelViewModel _comm;
        private readonly CameraHealthPanelViewModel _camera;
        private readonly MotionHealthPanelViewModel _motion;
        private readonly IoHealthViewModel _io;
        private readonly IoHealthPanelAdapter _ioPanel;
        private readonly DispatcherTimer _timer;

        private int _selectedTab;
        private bool _showOnlyIssues = true;
        private string _searchText = string.Empty;
        private string _issueSummary = "正在采集…";
        private string _lastRefreshText = string.Empty;
        private string _emptyTitle = string.Empty;
        private string _emptyHint = string.Empty;
        private string _overallLevelKey = "idle";

        public SystemHealthViewModel(
            CommunicationHealthMonitor commMonitor,
            CameraHealthMonitor cameraMonitor,
            MotionHealthMonitor motionMonitor,
            IoHealthViewModel ioPanel)
        {
            _comm = new CommunicationHealthPanelViewModel(commMonitor);
            _camera = new CameraHealthPanelViewModel(cameraMonitor);
            _motion = new MotionHealthPanelViewModel(motionMonitor);
            _io = ioPanel ?? throw new ArgumentNullException(nameof(ioPanel));

            Panels = new IHealthPanel[] { _comm, _camera, _motion, _ioPanel = new IoHealthPanelAdapter(ioPanel) };

            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += (_, _) => SafeRefresh();

            RefreshCommand = new DelegateCommand(ForceRefreshAll);
            CloseCommand = new DelegateCommand(() =>
            {
                _timer.Stop();
                RequestClose.Invoke();
            });
        }

        public string Title => "系统健康";

        public IReadOnlyList<IHealthPanel> Panels { get; }

        public CommunicationHealthPanelViewModel Comm => _comm;
        public CameraHealthPanelViewModel Camera => _camera;
        public MotionHealthPanelViewModel Motion => _motion;
        public IoHealthViewModel Io => _io;

        public ObservableCollection<HealthDomainCard> DomainCards { get; } = new();
        public ObservableCollection<HealthIssueRow> Issues { get; } = new();

        public DelegateCommand RefreshCommand { get; }
        public DelegateCommand CloseCommand { get; }

        public DialogCloseListener RequestClose { get; set; }

        /// <summary>0=概览 1=通讯 2=相机 3=运动 4=IO。</summary>
        public int SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (!SetProperty(ref _selectedTab, value)) return;

                for (int i = 0; i < Panels.Count; i++)
                    Panels[i].IsActive = value == i + 1;

                SafeRefresh();
            }
        }

        /// <summary>概览默认"仅异常"：50 台设备铺全量绿表会把唯一红点埋掉。</summary>
        public bool ShowOnlyIssues
        {
            get => _showOnlyIssues;
            set
            {
                if (SetProperty(ref _showOnlyIssues, value))
                    RebuildOverview();
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    RebuildOverview();
            }
        }

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

        public string EmptyTitle
        {
            get => _emptyTitle;
            private set => SetProperty(ref _emptyTitle, value);
        }

        public string EmptyHint
        {
            get => _emptyHint;
            private set => SetProperty(ref _emptyHint, value);
        }

        public bool CanCloseDialog() => true;

        public void OnDialogClosed()
        {
            _timer.Stop();
            foreach (var panel in Panels) panel.IsActive = false;
        }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            string? tab = parameters != null && parameters.ContainsKey("tab")
                ? parameters["tab"] as string
                : null;

            _selectedTab = tab?.ToLowerInvariant() switch
            {
                "comm" => 1,
                "camera" => 2,
                "motion" => 3,
                "io" => 4,
                _ => 0
            };
            RaisePropertyChanged(nameof(SelectedTab));

            ForceRefreshAll();
            _timer.Start();
        }

        private void ForceRefreshAll()
        {
            foreach (var panel in Panels) panel.ForceRefresh();
            RebuildOverview();
        }

        private void SafeRefresh()
        {
            try
            {
                if (_selectedTab == 0)
                {
                    foreach (var panel in Panels) panel.Refresh();
                }
                else
                {
                    Panels[_selectedTab - 1].Refresh();
                }

                RebuildOverview();
            }
            catch (Exception ex)
            {
                // DispatcherTimer.Tick 没有兜底：这里漏出去就是整个软件崩。
                _timer.Stop();
                IssueSummary = "采集失败";
                EmptyTitle = "采集失败";
                EmptyHint = $"诊断数据读取失败，已停止自动刷新：{ex.Message}";
            }
        }

        private void RebuildOverview()
        {
            DomainCards.Clear();
            DomainCards.Add(Card("通讯", _comm));
            DomainCards.Add(Card("相机", _camera));
            DomainCards.Add(Card("运动", _motion));
            DomainCards.Add(Card("IO", _ioPanel));

            var all = new List<HealthIssue>();
            foreach (var panel in Panels)
            {
                try { all.AddRange(panel.CollectIssues()); }
                catch (Exception ex)
                {
                    all.Add(new HealthIssue(panel.Domain, panel.Title, HealthLevel.Error, $"读取失败：{ex.Message}"));
                }
            }

            // 只把 Error/Warn 放进概览；Ok/Idle 不铺行（全部正常时给正向空态）。
            var problems = all.Where(i => i.Level is HealthLevel.Error or HealthLevel.Warn).ToList();
            string query = SearchText?.Trim() ?? string.Empty;
            var visible = problems
                .Where(i => query.Length == 0
                            || i.Target.Contains(query, StringComparison.OrdinalIgnoreCase)
                            || i.Message.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(i => i.Level)
                .ThenBy(i => i.Domain, StringComparer.Ordinal)
                .ThenBy(i => i.Target, StringComparer.Ordinal)
                .ToList();

            string fingerprint = string.Join("|", visible.Select(i => i.Key));
            if (fingerprint != _issuesFingerprint)
            {
                _issuesFingerprint = fingerprint;
                Issues.Clear();
                foreach (var issue in visible) Issues.Add(HealthIssueRow.From(issue));
            }

            int errors = problems.Count(i => i.Level == HealthLevel.Error);
            int warns = problems.Count(i => i.Level == HealthLevel.Warn);
            IssueSummary = errors == 0 && warns == 0
                ? "全部正常"
                : $"{errors} 故障 · {warns} 警告";

            OverallLevelKey = HealthLevels.Key(
                errors > 0 ? HealthLevel.Error : warns > 0 ? HealthLevel.Warn : HealthLevel.Ok);

            bool anyConfigured = Panels.Any(p => p.HasData);
            EmptyTitle = anyConfigured ? "全部正常" : "暂无可监控设备";
            EmptyHint = anyConfigured
                ? $"{Panels.Sum(p => p.HasData ? 1 : 0)} 个域已接入 · 最近检查 {DateTime.Now:HH:mm:ss}"
                : "当前方案没有配置通讯连接、相机、运动卡或 IO 设备。";

            LastRefreshText = $"更新 {DateTime.Now:HH:mm:ss}";
        }

        private string _issuesFingerprint = string.Empty;

        private static HealthDomainCard Card(string title, IHealthPanel panel) => new(
            panel.Domain,
            title,
            panel.LevelKey,
            HealthLevels.Text(panel.Level),
            panel.HasData ? panel.Summary : "未配置",
            string.IsNullOrWhiteSpace(panel.Detail) ? panel.Summary : panel.Detail);

        /// <summary>
        /// 把现有 <see cref="IoHealthViewModel"/> 适配成 <see cref="IHealthPanel"/>。
        ///
        /// <para>为什么用适配器而不是让 IO 面板继承基类：IO 面板已经有自己的 4 页签、审计筛选
        /// 与 <c>SafeRefresh</c> 语义（失败即停表），重写收益为零、风险覆盖选中/滚动/审计四条
        /// 已修复过的坑。适配器只做"把它的状态翻译成统一口径"。</para>
        /// </summary>
        private sealed class IoHealthPanelAdapter : HealthPanelViewModelBase
        {
            private readonly IoHealthViewModel _inner;
            private IReadOnlyList<HealthIssue> _issues = Array.Empty<HealthIssue>();

            public IoHealthPanelAdapter(IoHealthViewModel inner) => _inner = inner;

            public override string Domain => "IO";
            public override string Title => "IO";

            protected override void RefreshCore()
            {
                _inner.Refresh();

                Summary = _inner.Summary;
                Detail = _inner.Hint;
                HasData = _inner.HasData;

                var devices = _inner.Devices;
                int errors = devices.Count(d => string.Equals(d.StateLevel, "error", StringComparison.Ordinal));
                int warns = devices.Count(d => string.Equals(d.StateLevel, "warn", StringComparison.Ordinal));

                var issues = new List<HealthIssue>();
                foreach (var device in devices)
                {
                    if (!string.Equals(device.StateLevel, "error", StringComparison.Ordinal)
                        && !string.Equals(device.StateLevel, "warn", StringComparison.Ordinal)) continue;

                    issues.Add(new HealthIssue(
                        Domain, device.DeviceId,
                        string.Equals(device.StateLevel, "error", StringComparison.Ordinal)
                            ? HealthLevel.Error : HealthLevel.Warn,
                        device.StateDetail,
                        device.CountersText));
                }

                _issues = issues;
                Level = errors > 0 ? HealthLevel.Error
                    : warns > 0 ? HealthLevel.Warn
                    : HasData ? HealthLevel.Ok : HealthLevel.Idle;
                IssueCount = errors + warns;
            }

            protected override IReadOnlyList<HealthIssue> CollectIssuesCore() => _issues;
        }
    }
}
