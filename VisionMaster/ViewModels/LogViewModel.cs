using Core.Interfaces;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
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

        /// <summary>
        /// 面板保留条数（绑 <c>LogConsole.MaxItems</c>）。
        ///
        /// 为什么要有这个属性：视图里原先硬编码 <c>MaxItems="2000"</c>，而
        /// <see cref="AppConfigModel.LogPanelMaxRows"/> 默认 5000 且「系统 → 系统参数设置」可改——
        /// 两个上限并存，"改了配置没生效"（实际卡在 2000）而且没有任何提示。
        /// 现在两处用**同一个来源**：本属性直接读配置，<see cref="AppendBatch"/> 用它裁剪，
        /// 视图也绑它，不再有第二个数字。
        ///
        /// 口径：0 或负数 = 不限制（<see cref="AppendBatchWithCap"/> 与
        /// <c>LogConsole.TrimToCapacity</c> 都按这个语义处理）。
        /// </summary>
        public int PanelMaxRows => _settings?.Current?.LogPanelMaxRows ?? AppConfigModel.DefaultLogPanelMaxRows;

        /// <summary>
        /// 配置里的面板保留条数被改过之后调一次：通知 <see cref="PanelMaxRows"/> 重读。
        /// 通知到视图后，<c>LogConsole.MaxItems</c> 的变更回调会立刻按新上限裁一次
        /// （调小上限时不必等新日志到达）。宿主在系统设置「确定」后的生效动作里调用。
        /// </summary>
        public void RefreshPanelMaxRows() => RaisePropertyChanged(nameof(PanelMaxRows));

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

            // 批量刷新节拍（10Hz）。必须在本类构造线程（UI 线程）上创建：DispatcherTimer 有线程亲和性，
            // 日志产线在流程/引擎线程，只能在那边"请求起表"，起表动作由 SafeDispatch 送回来。
            _flushTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(FlushIntervalMs),
            };
            _flushTimer.Tick += OnFlushTimerTick;

            if (_logService is LogService logService)
            {
                // 异步投递（fire-and-forget）：
                // ① 同步 Invoke 让每条日志的产出线程（流程/引擎）干等 UI，关闭软件时
                //    Dispatcher 取消挂起操作 → TaskCanceledException 沿调用栈抛回插件层崩溃；
                // ② SafeDispatch 在 Dispatcher 关门阶段静默丢弃——日志不丢（文件通道独立），只是不再刷 UI
                //
                // 【批量投递】旧写法是"每条日志一个 BeginInvoke(AppendWithCap)"：循环运行时日志实测
                // 约 1000 条/秒，于是 UI 线程每秒要做 1000 次集合增删 + 1000 次 CollectionChanged，
                // 而 BeginInvoke 走 Normal 优先级（高于 Render/Input）——渲染与输入被日志搬运饿死，
                // 界面表现为"点什么都没反应"（2026-10-27 真机：UI 线程 73% 单核，日志 141 KB/s）。
                // 现在改为"先进待刷队列，10Hz 批量追加"：单次 flush 只发一次计数通知，
                // 队列排空即停表；渲染与输入因此总能插进来。
                logService.OnLogReceived += EnqueueLog;
                _logService.Success("软件加载成功");
            }

        }

        private readonly AppSettingsService? _settings;

        // ── 批量投递（见构造函数里的说明）──

        /// <summary>批量刷新节拍：100ms = 10Hz。日志洪峰（循环运行约 1000 条/秒）下把 UI 的
        /// 集合变更次数从 1000 次/秒压到 10 次/秒，代价是最多 100ms 的显示延迟。</summary>
        private const int FlushIntervalMs = 100;

        /// <summary>单次 flush 最多追加多少条：积压很多时分多次排空，别让一次 flush
        /// 长时间占住 UI 线程（一次 flush 就是一次"渲染/输入能插进来"的机会）。</summary>
        private const int MaxBatchPerFlush = 500;

        /// <summary>待刷队列上限：UI 长时间跟不上（例如主线程被别的重活占住）时丢最旧，
        /// 只保证面板显示最近的内容——文件通道是独立的，完整历史一条不少。</summary>
        private const int PendingLogLimit = 20000;

        private readonly DispatcherTimer _flushTimer;
        private readonly object _pendingGate = new();
        private readonly Queue<LogItem> _pendingLogs = new();

        /// <summary>已有一个 flush 在路上或定时器在跑（0/1）。队列排空时复位。</summary>
        private int _flushScheduled;

        /// <summary>批量追加深度：&gt;0 时集合事件只做增量计数，收尾统一通知一次，
        /// 否则每条日志都会触发一次 O(n) 的筛选计数重算（面板 5000 行时是实打实的卡顿源）。</summary>
        private int _batchDepth;

        /// <summary>队列超上限被丢掉的条数（只在队列排空时补一条汇总，不边丢边报）</summary>
        private long _droppedLogs;

        /// <summary>日志产线线程调用：入待刷队列并请求起表。绝不在这里碰 UI 对象。</summary>
        private void EnqueueLog(LogItem log)
        {
            if (log == null) return;

            lock (_pendingGate)
            {
                _pendingLogs.Enqueue(log);
                while (_pendingLogs.Count > PendingLogLimit)
                {
                    _pendingLogs.Dequeue();
                    _droppedLogs++;
                }
            }

            ScheduleFlush();
        }

        /// <summary>
        /// 请求一次批量刷新（任意线程可调）。同一时刻只挂一个请求：<see cref="_flushScheduled"/>
        /// 在"定时器跑起来"到"队列排空"之间保持 1，所以 1000 条/秒的洪峰只会产生 1 次 Dispatcher 投递。
        /// </summary>
        private void ScheduleFlush()
        {
            if (Interlocked.Exchange(ref _flushScheduled, 1) == 1) return;

            // 无 WPF 应用上下文（断言宿主 / 设计器）：SafeDispatch 在没有 Dispatcher 时会**直接丢弃**
            // 投递，等它转一圈等于日志一条都进不了面板。这里自己同步排空——与
            // ImageCollectionService.RunOnUi 的 headless 口径一致（无消息循环 = 调用线程上直接执行）。
            if (Application.Current?.Dispatcher == null)
            {
                FlushPending();
                return;
            }

            SafeDispatch.BeginInvoke(StartFlushTimerOnUi);
        }

        /// <summary>UI 线程：把定时器跑起来（已跑则不重复起）。</summary>
        private void StartFlushTimerOnUi()
        {
            if (Application.Current?.Dispatcher == null)
            {
                // 投递与执行之间应用被关掉：按 headless 口径同步排空，别把日志留在队列里
                FlushPending();
                return;
            }

            if (!_flushTimer.IsEnabled)
                _flushTimer.Start();
        }

        private void OnFlushTimerTick(object? sender, EventArgs e) => FlushPending();

        /// <summary>UI 线程：把待刷队列取一批写进面板。</summary>
        private void FlushPending()
        {
            List<LogItem>? batch = null;

            lock (_pendingGate)
            {
                if (_pendingLogs.Count == 0)
                {
                    // 停表与复位标志在同一把锁里：入队方要么已在我们之前（count &gt; 0，走不到这里），
                    // 要么在之后（看到标志 0 会重新请求起表）——不存在"日志留在队列里没人刷"的丢唤醒
                    if (_flushTimer.IsEnabled) _flushTimer.Stop();
                    Volatile.Write(ref _flushScheduled, 0);
                    return;
                }

                int take = Math.Min(_pendingLogs.Count, MaxBatchPerFlush);
                batch = new List<LogItem>(take);
                for (int i = 0; i < take; i++)
                    batch.Add(_pendingLogs.Dequeue());
            }

            AppendBatch(batch);
            ReportDroppedIfDrained();
        }

        /// <summary>批量追加（UI 线程）：整批只发一次计数通知。</summary>
        private void AppendBatch(List<LogItem> batch)
        {
            var maxRows = PanelMaxRows;

            _batchDepth++;
            try
            {
                AppendBatchWithCap(SystemLogs, batch, maxRows);
            }
            finally
            {
                _batchDepth--;
                if (_batchDepth == 0)
                    RaiseFilterCounters();
            }
        }

        /// <summary>
        /// 丢条汇总：只在队列排空时补一条，且**直接写集合、不经 LogService**
        /// （经 LogService 会再进一次队列，等于"报告洪水"本身又制造一条洪水）。
        /// </summary>
        private void ReportDroppedIfDrained()
        {
            long dropped;
            lock (_pendingGate)
            {
                if (_pendingLogs.Count != 0) return;
                dropped = _droppedLogs;
                _droppedLogs = 0;
            }

            if (dropped <= 0) return;

            AppendBatch(new List<LogItem>
            {
                new LogItem(LogLevel.Warning,
                    $"日志面板投递落后，已丢弃 {dropped} 条（日志文件完整，不受影响）",
                    "日志面板"),
            });
        }

        /// <summary>
        /// 上限裁剪的**纯函数**形态（公开是为了让断言能直接钉住它）：
        /// 追加一条后把超出 <paramref name="maxRows"/> 的最旧项丢掉。
        ///
        /// 为什么要有上限：这个集合在加这一项之前只增不减，连续跑几天就是几十万条
        /// （每条都带着消息文本），面板本身会吃掉可观内存，而屏幕上最多只看得到几十行。
        /// 超过上限按最旧丢弃——日志文件的完整历史不受影响（文件通道是独立的）。
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
        /// 批量版的同一语义：先全部追加，再**一次性**裁到上限。
        ///
        /// 与"循环调用 <see cref="AppendWithCap"/>"逐项等价（最终内容一致），差别只在裁剪次数：
        /// 逐条裁剪要搬 n 遍 5000 行，批量只搬一遍——这正是洪峰下省下来的那部分 UI 时间。
        /// </summary>
        public static void AppendBatchWithCap(
            ObservableCollection<LogItem>? target,
            IReadOnlyList<LogItem>? logs,
            int maxRows)
        {
            if (target == null || logs == null) return;

            for (int i = 0; i < logs.Count; i++)
            {
                if (logs[i] != null)
                    target.Add(logs[i]);
            }

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

            // 底栏「显示 N」与「无结果/空」三态都依赖集合内容。
            // 批量追加期间只做上面的增量计数，收尾由 AppendBatch 统一通知一次：
            // 否则一批 500 条就要重算 500 遍筛选计数（每遍 O(面板行数)）。
            if (_batchDepth > 0) return;
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
