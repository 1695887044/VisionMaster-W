using Core.Interfaces;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using UI.Models;
using VisionMaster.Helpers;
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
            set => SetProperty(ref _searchText, value);
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
                    RaisePropertyChanged(nameof(FilterLevel));
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

        public LogViewModel(ILogService _logService)
        {
            _allOption = LevelOptions[0];
            _levelOption = _allOption;   // 默认「全部」
            SystemLogs.CollectionChanged += OnSystemLogsChanged;

            if (_logService is LogService logService)
            {
                // 异步投递（fire-and-forget）：
                // ① 同步 Invoke 让每条日志的产出线程（流程/引擎）干等 UI，关闭软件时
                //    Dispatcher 取消挂起操作 → TaskCanceledException 沿调用栈抛回插件层崩溃；
                // ② SafeDispatch 在 Dispatcher 关门阶段静默丢弃——日志不丢（文件通道独立），只是不再刷 UI
                logService.OnLogReceived += (log) => SafeDispatch.BeginInvoke(() => SystemLogs.Add(log));
                _logService.Success("软件加载成功");
            }

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
