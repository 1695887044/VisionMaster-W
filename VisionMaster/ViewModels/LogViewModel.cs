using Core.Interfaces;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using UI.Models;
using VisionMaster.Binding;
using VisionMaster.Helpers;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 等级筛选筹码：Text 给界面看，Value 给过滤用（null = 全部），Count 是这一级当前有多少条。
    /// 用对象而不是裸枚举，是为了"全部"这一项能显示成中文，并且每项能各自带着计数一起刷新。
    /// </summary>
    public class LogLevelFilterOption : BindableBase
    {
        public string Text { get; set; } = string.Empty;
        public LogLevel? Value { get; set; }

        private int _count;
        /// <summary>该等级当前条数（"全部"那项是总条数）；随日志增删与面板裁剪实时变</summary>
        public int Count
        {
            get => _count;
            set => SetProperty(ref _count, value);
        }
    }

    public class LogViewModel : BindableBase
    {
        public ObservableCollection<LogItem> SystemLogs { get; set; } = new();

        /// <summary>日志面板的关键字过滤（绑 LogConsole.SearchText；控件侧有 200ms 防抖）</summary>
        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                    RaiseFilterCounters();   // 底栏计数与"无结果"态都跟着搜索词变
            }
        }

        /// <summary>等级筹码（顺序即显示顺序）；第一项是"全部"</summary>
        public IReadOnlyList<LogLevelFilterOption> LevelOptions { get; } = new List<LogLevelFilterOption>
        {
            new LogLevelFilterOption { Text = "全部", Value = null },
            new LogLevelFilterOption { Text = "信息", Value = LogLevel.Info },
            new LogLevelFilterOption { Text = "成功", Value = LogLevel.Success },
            new LogLevelFilterOption { Text = "警告", Value = LogLevel.Warning },
            new LogLevelFilterOption { Text = "错误", Value = LogLevel.Error },
        };

        private readonly LogLevelFilterOption _allOption;

        private LogLevelFilterOption _levelOption;
        public LogLevelFilterOption LevelOption
        {
            get => _levelOption;
            set
            {
                // 派生属性 FilterLevel 要跟着变，否则控件那边的依赖属性不会更新
                if (SetProperty(ref _levelOption, value))
                {
                    RaisePropertyChanged(nameof(FilterLevel));
                    RaiseFilterCounters();
                }
            }
        }

        /// <summary>当前等级过滤（"全部" = 不限等级），绑 LogConsole.FilterLevel</summary>
        public LogLevel? FilterLevel => _levelOption?.Value;

        /// <summary>是否自动跟随最新日志，绑 LogConsole.AutoScroll</summary>
        private bool _autoScroll = true;
        public bool AutoScroll
        {
            get => _autoScroll;
            set => SetProperty(ref _autoScroll, value);
        }

        /// <summary>
        /// 清空筛选条件（搜索词 + 等级回到「全部」）。
        ///
        /// 为什么放在 VM 而不是视图的 code-behind：无结果态的出口按钮要同时改
        /// <see cref="SearchText"/> 与 <see cref="LevelOption"/> 两个属性，其中后者还要
        /// 带动 <see cref="FilterLevel"/> 的通知链；写在视图里就得手写两条赋值 + 一次通知，
        /// 而这里复用 setter 已有的通知逻辑（设计基线 log-console §4b ①）。
        /// </summary>
        public DelegateCommand ClearFiltersCommand { get; }

        /// <summary>
        /// 当前筛选命中条数（底栏「显示 N / 共 M」里的 N）。
        ///
        /// 为什么放在 VM：筛选本身挂在 LogConsole 内部的集合默认视图上（控件私有），
        /// 视图拿不到；而 VM 手里有完整的 <see cref="SystemLogs"/> 与两个筛选条件，
        /// 复算一遍的代价可以忽略（面板上限几千条）。判定口径必须与
        /// LogConsole.FilterLogic 逐字一致：等级精确匹配 + 关键字忽略大小写匹配正文或来源。
        /// </summary>
        public int FilteredCount => CountFiltered();

        private int CountFiltered()
        {
            var level = FilterLevel;
            var keyword = _searchText;
            bool hasKeyword = !string.IsNullOrWhiteSpace(keyword);
            if (hasKeyword) keyword = keyword.Trim();

            if (!level.HasValue && !hasKeyword) return SystemLogs.Count;

            int n = 0;
            foreach (var log in SystemLogs)
            {
                if (level.HasValue && log.Level != level.Value) continue;
                if (hasKeyword)
                {
                    bool matchMsg = log.Message?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
                    bool matchSrc = log.Source?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!matchMsg && !matchSrc) continue;
                }
                n++;
            }
            return n;
        }

        /// <summary>筛选条件是否生效（用于「无结果」与「空态」二选一，绑视图的显隐）</summary>
        public bool HasFilter => FilterLevel.HasValue || !string.IsNullOrWhiteSpace(_searchText);

        /// <summary>无结果：有筛选条件、且一条都没命中（区别于"真的没有日志"）</summary>
        public bool IsFilterEmpty => HasFilter && FilteredCount == 0;

        /// <summary>真的没有日志（刚启动 / 已清空），与"筛没了"分开</summary>
        public bool IsEmpty => SystemLogs.Count == 0;

        private void RaiseFilterCounters()
        {
            RaisePropertyChanged(nameof(FilteredCount));
            RaisePropertyChanged(nameof(HasFilter));
            RaisePropertyChanged(nameof(IsFilterEmpty));
            RaisePropertyChanged(nameof(IsEmpty));
        }

        public LogViewModel(ILogService _logService, AppSettingsService? settings = null)
        {
            _settings = settings;
            _allOption = LevelOptions[0];
            _levelOption = _allOption;   // 默认「全部」
            ClearFiltersCommand = new DelegateCommand(ExecuteClearFilters);
            SystemLogs.CollectionChanged += OnSystemLogsChanged;

            if (_logService is LogService logService)
            {
                // 异步投递（fire-and-forget）：
                // ① 同步 Invoke 让每条日志的产出线程（流程/引擎）干等 UI，关闭软件时
                //    Dispatcher 取消挂起操作 → TaskCanceledException 沿调用栈抛回插件层崩溃；
                // ② SafeDispatch 在 Dispatcher 关门阶段静默丢弃——日志不丢（文件通道独立），只是不再刷 UI
                logService.OnLogReceived += (log) => SafeDispatch.BeginInvoke(() => AppendWithCap(log));
                _logService.Success("软件加载成功");
            }

        }

        private readonly AppSettingsService? _settings;

        /// <summary>
        /// 收一条日志并维持面板上限（<see cref="AppConfigModel.LogPanelMaxRows"/>，0/负数 = 不限制）。
        ///
        /// 为什么要有上限：这个集合在加这一项之前只增不减，连续跑几天就是几十万条
        /// （每条都带着消息文本），面板本身会吃掉可观内存，而屏幕上最多只看得到几十行。
        /// 超过上限按最旧丢弃——日志文件的完整历史不受影响（文件通道是独立的）。
        /// </summary>
        private void AppendWithCap(LogItem log)
            => AppendWithCap(
                SystemLogs,
                log,
                _settings?.Current?.LogPanelMaxRows ?? AppConfigModel.DefaultLogPanelMaxRows);

        /// <summary>
        /// 上限裁剪的**纯函数**形态（公开是为了让断言能直接钉住它）：
        /// 追加一条后把超出 <paramref name="maxRows"/> 的最旧项丢掉。
        /// </summary>
        public static void AppendWithCap(ObservableCollection<LogItem>? target, LogItem? log, int maxRows)
        {
            if (target == null || log == null) return;

            target.Add(log);

            if (maxRows <= 0) return;   // 0 / 负数 = 不限制

            while (target.Count > maxRows)
                target.RemoveAt(0);
        }

        /// <summary>
        /// 维护每级条数。增量更新（Add/Remove 各只动一格），不然日志一多，
        /// 每条日志都全量重数一遍就是 O(n²)。换源/清空（Reset）才走全量重算。
        /// </summary>
        private void OnSystemLogsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    BumpCounts(e.NewItems, +1);
                    break;
                case NotifyCollectionChangedAction.Remove:
                    BumpCounts(e.OldItems, -1);
                    break;
                case NotifyCollectionChangedAction.Reset:
                    RecountAll();
                    break;
                default:
                    // Replace / Move 很少见，不值得为它们写增量
                    RecountAll();
                    break;
            }

            // 底栏「显示 N」与「无结果/空」三态都依赖集合内容
            RaiseFilterCounters();
        }

        /// <summary>清空筛选条件：搜索词置空 + 等级回到「全部」</summary>
        private void ExecuteClearFilters()
        {
            SearchText = string.Empty;
            LevelOption = _allOption;
        }

        private void BumpCounts(IList items, int delta)
        {
            if (items == null) return;
            foreach (var item in items)
            {
                if (item is not LogItem log) continue;
                var option = FindOption(log.Level);
                if (option != null) option.Count += delta;
                _allOption.Count += delta;
            }
        }

        private void RecountAll()
        {
            foreach (var option in LevelOptions) option.Count = 0;
            foreach (var log in SystemLogs)
            {
                var option = FindOption(log.Level);
                if (option != null) option.Count++;
            }
            _allOption.Count = SystemLogs.Count;
        }

        private LogLevelFilterOption FindOption(LogLevel level)
            => LevelOptions.FirstOrDefault(o => o.Value == level);

    }
}
