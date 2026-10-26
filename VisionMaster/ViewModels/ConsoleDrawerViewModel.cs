using System.Collections.Specialized;
using System.Linq;
using UI.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 底部控制台抽屉条（收起态 32px）的视图模型。
    ///
    /// 为什么抽屉条不放在 LogView 里：
    /// ① AvalonDock 4.74 的 <c>LayoutAnchorablePane</c> 没有 <c>ShowHeader</c>
    ///    （只有 <c>LayoutDocumentPane</c> 有），只要面板还在停靠树里，库就一定画自己的页签条，
    ///    抽屉头塞进视图内容只会被它挤到看不见；要藏只能改库模板。
    /// ② 抽屉条要同时显示日志条数与监控项数，这两份数据分属两个视图；
    ///    由 Shell 层的一个 VM 聚合，比让某个视图去拿另一个视图的 VM 干净。
    /// ③ "收起"是布局属性（<c>DockHeight</c>），只有主窗口层的 DockingManager 能改。
    ///
    /// 数据来源是**共用同一份** <see cref="LogViewModel"/> / <see cref="MonitorViewModel"/>
    /// （App 里注册成单例）：若自己再 new 一个 LogViewModel，它会在
    /// <c>LogPanelMaxRows</c> 上限处与日志页的「全部 N」分叉。
    /// </summary>
    public class ConsoleDrawerViewModel : BindableBase
    {
        /// <summary>抽屉条页签 → 面板 ContentId（与 Shell.xaml 的 ContentId 一致）。</summary>
        public const string LogPanelId = "Panel_LogView";
        public const string MonitorPanelId = "Panel_MonitorView";

        private readonly LogViewModel _log;
        private readonly MonitorViewModel _monitor;

        public ConsoleDrawerViewModel(LogViewModel log, MonitorViewModel monitor)
        {
            _log = log;
            _monitor = monitor;

            // 订阅集合变化只为了刷"计数 / 最后一条"；日志正文本身仍由日志页展示。
            _log.SystemLogs.CollectionChanged += OnLogsChanged;
            _monitor.DisplayPorts.CollectionChanged += OnWatchItemsChanged;

            ToggleCommand = new DelegateCommand(LayoutHelper.ToggleConsole);
            ExpandLogCommand = new DelegateCommand(() => OpenPanel(LogPanelId));
            ExpandMonitorCommand = new DelegateCommand(() => OpenPanel(MonitorPanelId));
        }

        /// <summary>展开 / 收起（与状态栏按钮、命令面板同一条底层命令）。</summary>
        public DelegateCommand ToggleCommand { get; }

        /// <summary>点「控制台」页签：展开并切到日志页。</summary>
        public DelegateCommand ExpandLogCommand { get; }

        /// <summary>点「监控栏」页签：展开并切到监视页。</summary>
        public DelegateCommand ExpandMonitorCommand { get; }

        public int LogCount => _log.SystemLogs.Count;

        public int WatchCount => _monitor.DisplayPorts.Count;

        /// <summary>错误条数（与日志页「错误」筹码同一个计数源）。</summary>
        public int ErrorCount =>
            _log.LevelOptions.FirstOrDefault(o => o.Value == LogLevel.Error)?.Count ?? 0;

        public bool HasError => ErrorCount > 0;

        public bool HasLastLog => _log.SystemLogs.Count > 0;

        public LogLevel? LastLevel => HasLastLog ? _log.SystemLogs[_log.SystemLogs.Count - 1].Level : null;

        public string LastSource => HasLastLog ? _log.SystemLogs[_log.SystemLogs.Count - 1].Source ?? string.Empty : string.Empty;

        public string LastText =>
            HasLastLog
                ? _log.SystemLogs[_log.SystemLogs.Count - 1].Message ?? string.Empty
                : "暂无日志";

        /// <summary>抽屉条状态（由 <see cref="Services.LayoutHelper"/> 的状态事件驱动，不读 DockHeight）。</summary>
        public ConsolePaneState State { get; private set; } = ConsolePaneState.Expanded;

        /// <summary>收起态才显示抽屉条（展开态由面板自己的页签条接管；组态模式两者都不显示）。</summary>
        public bool IsDrawerVisible => State == ConsolePaneState.Collapsed;

        /// <summary>箭头方向（展开时朝下）。</summary>
        public bool IsExpanded => State == ConsolePaneState.Expanded;

        /// <summary>收起态高亮哪一页（与展开态面板的选中页一致；取不到时按日志页）。</summary>
        public string ActivePanelId { get; private set; } = LogPanelId;

        public void ApplyState(ConsolePaneState state)
        {
            // 先同步"当前页"：收起的一瞬间面板的选中项就是用户最后看的那页。
            var active = LayoutHelper.GetConsoleSelectedPanelId();
            if (!string.IsNullOrEmpty(active) && active != ActivePanelId)
            {
                ActivePanelId = active;
                RaisePropertyChanged(nameof(ActivePanelId));
            }

            if (State == state) return;

            State = state;
            RaisePropertyChanged(nameof(State));
            RaisePropertyChanged(nameof(IsDrawerVisible));
            RaisePropertyChanged(nameof(IsExpanded));
        }

        /// <summary>
        /// 页签点击：先展开，**等一次布局之后再切页签**。
        ///
        /// 两个顺序都不能反：
        /// ① 展开必须在切页之前——展开会"还原收起前那一页"（<see cref="LayoutHelper.AttachConsolePane"/>），
        ///    先切会被这次还原顶回去；
        /// ② 切页必须排在布局之后——面板是刚从缓存插回停靠树的，同步写 <c>IsSelected</c> 会被
        ///    AvalonDock 自己的"选中项回填"覆盖（2026-10-26 真机实测：点「监控栏」展开后仍停在日志页）。
        /// </summary>
        private void OpenPanel(string contentId)
        {
            LayoutHelper.ExpandConsole();

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                LayoutHelper.ShowPanel(contentId);   // 无 Application 的宿主：直接切
                return;
            }

            dispatcher.BeginInvoke(
                new Action(() => LayoutHelper.ShowPanel(contentId)),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RaisePropertyChanged(nameof(LogCount));
            RaisePropertyChanged(nameof(ErrorCount));
            RaisePropertyChanged(nameof(HasError));
            RaisePropertyChanged(nameof(HasLastLog));
            RaisePropertyChanged(nameof(LastLevel));
            RaisePropertyChanged(nameof(LastSource));
            RaisePropertyChanged(nameof(LastText));
        }

        private void OnWatchItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
            => RaisePropertyChanged(nameof(WatchCount));
    }
}
