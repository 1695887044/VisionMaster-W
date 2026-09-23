using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Threading;
using Prism.Commands;
using Prism.Mvvm;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;
using VisionMaster.Services;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 报警历史面板的视图模型：把本次运行产生的报警流水摆成一张能搜、能筛、能确认、能导出的表。
    ///
    /// 数据从哪来
    /// ---------
    /// <see cref="ScadaRuntimeContext.Alarms"/>——本轮运行那个活着的 <see cref="ScadaAlarmEngine"/>。
    /// 面板不自己存历史：引擎的 <c>_history</c> 才是唯一真相（它同时喂着落盘端），
    /// 面板再存一份就必然出现"两份历史对不上"的问题。
    /// 取的是 <see cref="ScadaAlarmEngine.RecentRecords"/>（<b>最新在前</b>），
    /// 现场翻开面板第一眼要看到的是"刚刚又报了什么"，不是三小时前那条。
    ///
    /// 为什么整表重建，而不是增量维护
    /// ---------
    /// 与报警条同一条理由：报警是<b>低频</b>事件（分钟级），一次重读最多两千行；
    /// 而增量要维护"哪一行对应哪条记录"，配置热重载（引擎自动重挂、旧记录成批消失）时
    /// 最容易留下幽灵行。重建之后不存在"行忘了跟着记录刷新"这种陈旧显示。
    ///
    /// 为什么重建要<b>合并</b>（<see cref="RequestRefresh"/>）
    /// ---------
    /// 一次初判可能连着抛十几条报警，每条都重建一次表就是十几次两千行的重建 +
    /// 十几次 <c>CollectionChanged</c> 风暴，界面会卡住。合并成"一轮消息循环里只重建一次"，
    /// 成本与报警条数无关。
    ///
    /// 为什么只有持续时长要单独刷
    /// ---------
    /// 其余字段都是<b>定值</b>（激活时刻、名字、严重度），只有"还没恢复的那条"的时长会随钟走。
    /// 为它整表重建不值当，所以挂节拍、每满一秒调一次 <see cref="ScadaAlarmHistoryRow.RefreshDuration"/>——
    /// 只改一个属性，不重建集合、不动选中。
    ///
    /// 线程
    /// ---------
    /// 引擎的三个事件可能在变量轮询线程上抛（见 <see cref="ScadaAlarmEngine"/> 的线程约定），
    /// 所以事件入口只做一件事：请求一次重建（<see cref="Dispatcher.BeginInvoke(DispatcherPriority, Delegate)"/>
    /// 本身是线程安全的）。真正改集合永远在 UI 线程上。节拍本来就在 UI 线程上。
    /// </summary>
    public sealed class ScadaAlarmHistoryViewModel : BindableBase
    {
        /// <summary>一次最多摆多少条。与引擎的内存历史上限（2000）对齐——摆不下的本来也取不到</summary>
        private const int MaxRows = 2000;

        /// <summary>持续时长的刷新周期。1 秒足够（时长本身只显示到秒），又不会每秒重画两千行</summary>
        private static readonly TimeSpan DurationRefreshInterval = TimeSpan.FromSeconds(1);

        /// <summary>提示语（导出结果之类）在面板上停留多久</summary>
        private static readonly TimeSpan NoticeLifetime = TimeSpan.FromSeconds(8);

        private readonly Dispatcher _dispatcher;

        /// <summary>二次确认口（"确认当前列表里的 12 条报警？"）。视图层注入，见构造函数</summary>
        private readonly Func<string, bool> _confirm;

        /// <summary>本次运行的全部记录（已按最新在前）；<see cref="Rows"/> 是它在筛选下的投影</summary>
        private readonly List<ScadaAlarmHistoryRow> _all = new();

        private ScadaRuntimeContext? _context;

        /// <summary>已经排了一次重建、还没执行。合并多次事件用</summary>
        private bool _refreshPending;

        private TimeSpan _lastDurationRefresh;
        private TimeSpan _noticeStartedAt;

        private string _keyword = string.Empty;
        private ScadaAlarmHistoryFilter _stateFilter = StateFilters[0];
        private ScadaAlarmHistoryFilter _severityFilter = SeverityFilters[0];
        private ScadaAlarmHistoryRow? _selectedRow;
        private string _noticeText = string.Empty;

        /// <param name="dispatcher">面板所在的 UI 线程调度器。必须注入而不是取 <c>Application.Current</c>：
        /// 断言宿主里根本没有 <c>Application</c>，而节拍与重建都必须落在这个线程上</param>
        /// <param name="confirm">"全部确认"的二次确认口，参数是要问的那句话，返回用户是否同意。
        /// 由视图层注入（弹 <c>MessageBox</c>）；断言不传 = 不打断、直接执行</param>
        public ScadaAlarmHistoryViewModel(Dispatcher dispatcher, Func<string, bool>? confirm = null)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _confirm = confirm ?? (_ => true);

            AcknowledgeCommand = new DelegateCommand<ScadaAlarmHistoryRow>(OnAcknowledge);
            AcknowledgeAllCommand = new DelegateCommand(OnAcknowledgeAll, () => Rows.Any(r => r.CanAcknowledge));
            ResetFilterCommand = new DelegateCommand(OnResetFilter);
        }

        /// <summary>当前可见的行（最新在前，已过滤）</summary>
        public ObservableCollection<ScadaAlarmHistoryRow> Rows { get; } = new();

        /// <summary>状态筛选项（静态共享：它们只是谓词，没有实例状态）</summary>
        public static IReadOnlyList<ScadaAlarmHistoryFilter> StateFilters { get; } = new[]
        {
            ScadaAlarmHistoryFilter.All("全部状态"),
            new ScadaAlarmHistoryFilter("未确认", r => r.CanAcknowledge),
            new ScadaAlarmHistoryFilter("报警中", r => r.Record.IsActive),
            new ScadaAlarmHistoryFilter("已恢复", r => r.State == ScadaAlarmState.Recovered),
            new ScadaAlarmHistoryFilter("已清除", r => r.State == ScadaAlarmState.Normal),
        };

        /// <summary>严重度筛选项</summary>
        public static IReadOnlyList<ScadaAlarmHistoryFilter> SeverityFilters { get; } = new[]
        {
            ScadaAlarmHistoryFilter.All("全部级别"),
            new ScadaAlarmHistoryFilter("严重", r => r.Severity == ScadaAlarmSeverity.Critical),
            new ScadaAlarmHistoryFilter("警告", r => r.Severity == ScadaAlarmSeverity.Warning),
            new ScadaAlarmHistoryFilter("提示", r => r.Severity == ScadaAlarmSeverity.Info),
        };

        /// <summary>关键字：匹配 报警名 / 报警文本 / 变量名 / 条件描述，大小写不敏感</summary>
        public string Keyword
        {
            get => _keyword;
            set
            {
                if (SetProperty(ref _keyword, value ?? string.Empty))
                    ApplyFilter();
            }
        }

        public ScadaAlarmHistoryFilter StateFilter
        {
            get => _stateFilter;
            set
            {
                if (value != null && SetProperty(ref _stateFilter, value))
                    ApplyFilter();
            }
        }

        public ScadaAlarmHistoryFilter SeverityFilter
        {
            get => _severityFilter;
            set
            {
                if (value != null && SetProperty(ref _severityFilter, value))
                    ApplyFilter();
            }
        }

        /// <summary>当前选中行（确认单条用不到它——每行自带按钮；留给将来的详情区与断言）</summary>
        public ScadaAlarmHistoryRow? SelectedRow
        {
            get => _selectedRow;
            set => SetProperty(ref _selectedRow, value);
        }

        /// <summary>底部计数：<c>"共 42 条 · 未确认 3 · 显示 12"</c></summary>
        public string SummaryText =>
            $"共 {_all.Count} 条 · 未确认 {UnacknowledgedCount} · 显示 {Rows.Count}";

        /// <summary>还没确认的条数（顶条徽标与底部计数共用；面板收起时也照常刷新）</summary>
        public int UnacknowledgedCount => _all.Count(r => r.CanAcknowledge);

        /// <summary>顶条徽标要不要出现</summary>
        public bool HasUnacknowledged => UnacknowledgedCount > 0;

        /// <summary>
        /// 徽标上的字。超过 99 收成 <c>"99+"</c>——徽标是给"有没有"看的，
        /// 让它跟着数字变宽会把顶条挤歪，而三位数以上的精确值在面板里一眼就有。
        /// </summary>
        public string UnacknowledgedText =>
            UnacknowledgedCount > 99 ? "99+" : UnacknowledgedCount.ToString(CultureInfo.InvariantCulture);

        /// <summary>提示语（导出结果 / 确认反馈）；空串时模板把它收掉</summary>
        public string NoticeText => _noticeText;

        /// <summary>列表里有没有内容</summary>
        public bool HasRows => Rows.Count > 0;

        /// <summary>空态提示（有内容时为空串）</summary>
        public string EmptyHint =>
            _all.Count == 0
                ? "本次运行还没有报警记录。"
                : Rows.Count == 0
                    ? "没有符合当前筛选条件的报警。"
                    : string.Empty;

        /// <summary>确认某一条（行上的按钮）</summary>
        public DelegateCommand<ScadaAlarmHistoryRow> AcknowledgeCommand { get; }

        /// <summary>确认<b>当前列表里</b>所有待确认的（筛选后可见的那些——所见即所确认）</summary>
        public DelegateCommand AcknowledgeAllCommand { get; }

        /// <summary>把关键字与两个筛选清回默认</summary>
        public DelegateCommand ResetFilterCommand { get; }

        /// <summary>
        /// 换挂运行态上下文。<paramref name="context"/> 为 null = 退挂（窗口关闭、运行收场）。
        ///
        /// 为什么换挂要整体做：引擎与节拍同生共死（见 <see cref="ScadaRuntimeContext"/>），
        /// 所以"挂上"和"摘掉"永远是一组动作，不可能只来一半。
        /// </summary>
        public void Attach(ScadaRuntimeContext? context)
        {
            if (ReferenceEquals(_context, context))
                return;

            Detach();

            _context = context;

            if (context == null)
            {
                _all.Clear();
                ApplyFilter();
                return;
            }

            context.Alarms.AlarmRaised += OnAlarmChanged;
            context.Alarms.AlarmChanged += OnAlarmChanged;
            context.Alarms.AlarmCleared += OnAlarmChanged;
            context.Beat.Beat += OnBeat;

            _lastDurationRefresh = TimeSpan.Zero;
            RebuildRows();
        }

        /// <summary>重建整表（打开面板、引擎报事件、用户点刷新都走这里）</summary>
        public void RebuildRows()
        {
            _all.Clear();

            var records = _context?.Alarms.RecentRecords(MaxRows);
            if (records != null)
            {
                foreach (var record in records)
                {
                    if (record != null)
                        _all.Add(new ScadaAlarmHistoryRow(record));
                }
            }

            ApplyFilter();
        }

        /// <summary>
        /// 把当前可见的行导出成 CSV。返回是否成功；成败都会在面板底部留一句提示。
        ///
        /// 导出的<b>不是</b>全量而是当前列表：现场最常见的用法是"筛出这个班次的严重报警，导出交班"，
        /// 导出全量再让他在 Excel 里筛，等于把筛子推给了别人。
        /// </summary>
        /// <param name="filePath">目标文件全路径（由保存对话框给出，所以本类不碰对话框）</param>
        public bool Export(string filePath)
        {
            var records = Rows.Select(r => r.Record).ToList();

            if (ScadaAlarmHistoryWriter.Export(filePath, records, out var error))
            {
                SetNotice($"已导出 {records.Count} 条 → {filePath}");
                return true;
            }

            SetNotice($"导出失败：{error}");
            return false;
        }

        /// <summary>确认当前列表里所有待确认的。返回真正确认掉的条数</summary>
        public int AcknowledgeAll()
        {
            var engine = _context?.Alarms;
            if (engine == null)
                return 0;

            // 先快照再遍历：确认会抛事件、事件会请求重建，虽然重建被推迟到下一轮消息循环，
            // 但依赖"恰好还没执行"是脆的，快照一行成本。
            int changed = 0;
            foreach (var row in Rows.ToList())
            {
                if (row.CanAcknowledge && engine.Acknowledge(row.Record.AlarmId) > 0)
                    changed++;
            }

            SetNotice(changed > 0 ? $"已确认 {changed} 条报警" : "当前列表里没有待确认的报警");
            RequestRefresh();
            return changed;
        }

        /// <summary>请求一次重建（合并：一轮消息循环里最多重建一次）。可在任意线程调用</summary>
        private void RequestRefresh()
        {
            if (_refreshPending || _dispatcher.HasShutdownStarted)
                return;

            _refreshPending = true;

            // Background：重建是"把数据摆上屏"，不该插到输入与渲染前面——操作员正在点按钮时
            // 列表晚 10ms 刷新没人看得出来，而抢在输入前面会让手感变涩。
            _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _refreshPending = false;
                RebuildRows();
            }));
        }

        /// <summary>
        /// 重建可见行。筛选从 setter 的副作用里解耦出来单独一步：
        /// 首次打开时 <see cref="Keyword"/> 本来就是空串，靠 setter 触发刷新会因"值没变"而直接返回，
        /// 表现为"有记录却显示空态"（画面选择器踩过同一个坑）。
        /// </summary>
        private void ApplyFilter()
        {
            var keepId = _selectedRow?.RecordId ?? Guid.Empty;

            Rows.Clear();
            foreach (var row in _all)
            {
                if (_stateFilter.Matches(row) && _severityFilter.Matches(row) && row.Matches(_keyword))
                    Rows.Add(row);
            }

            // 选中项按 RecordId 找回：整表重建后行对象全是新的，按引用找必然丢选中。
            SelectedRow = keepId == Guid.Empty ? null : Rows.FirstOrDefault(r => r.RecordId == keepId);

            RaisePropertyChanged(nameof(SummaryText));
            RaisePropertyChanged(nameof(HasRows));
            RaisePropertyChanged(nameof(EmptyHint));
            // 顶条徽标绑的就是这三个：面板收起时也靠它们亮起来，所以必须跟着筛选一起重算。
            RaisePropertyChanged(nameof(UnacknowledgedCount));
            RaisePropertyChanged(nameof(HasUnacknowledged));
            RaisePropertyChanged(nameof(UnacknowledgedText));
            AcknowledgeAllCommand.RaiseCanExecuteChanged();
        }

        private void OnResetFilter()
        {
            _keyword = string.Empty;
            _stateFilter = StateFilters[0];
            _severityFilter = SeverityFilters[0];

            RaisePropertyChanged(nameof(Keyword));
            RaisePropertyChanged(nameof(StateFilter));
            RaisePropertyChanged(nameof(SeverityFilter));

            ApplyFilter();
        }

        private void OnAcknowledge(ScadaAlarmHistoryRow? row)
        {
            var engine = _context?.Alarms;
            if (row == null || engine == null || !row.CanAcknowledge)
                return;

            // 按 AlarmId（<b>定义</b>身份）而不是 RecordId：引擎的 Acknowledge 就是按"哪条定义"
            // 找它<b>当前</b>那条记录的（见 ScadaAlarmEngine.Acknowledge）。而 CanAcknowledge 为真的行
            // （激活中 / 已恢复未确认）必定还是槽里的当前记录，所以找回去命中的正是操作员点的那一行。
            // 已清除的旧记录不给按钮，就是为了不出现"点一条三个月前的记录，确认掉的是刚刚那条"。
            var changed = engine.Acknowledge(row.Record.AlarmId);

            SetNotice(changed > 0 ? $"已确认「{row.Name}」" : "这条报警的状态刚刚变了，列表马上刷新");
            RequestRefresh();
        }

        private void OnAcknowledgeAll()
        {
            int pending = Rows.Count(r => r.CanAcknowledge);
            if (pending == 0)
                return;

            // 二次确认：一次确认掉十几条是<b>不可撤销</b>的动作（确认时间戳一落就没有"撤销确认"这条路），
            // 而按钮就在"导出"旁边——手快的人会点错。问一句的成本远低于让操作员去解释"我没看过就确认了"。
            if (!_confirm($"确认当前列表里的 {pending} 条报警？\n\n已确认的报警会从「待确认」里消失，此操作不可撤销。"))
                return;

            AcknowledgeAll();
        }

        private void Detach()
        {
            var context = _context;
            _context = null;

            if (context == null)
                return;

            context.Alarms.AlarmRaised -= OnAlarmChanged;
            context.Alarms.AlarmChanged -= OnAlarmChanged;
            context.Alarms.AlarmCleared -= OnAlarmChanged;
            context.Beat.Beat -= OnBeat;
        }

        /// <summary>三个事件共用一个处理函数：既然整表重读，就不必知道"是哪一条怎么变了"</summary>
        private void OnAlarmChanged(ScadaAlarmRecord record) => RequestRefresh();

        /// <summary>节拍：只干两件与钟有关的小事（刷时长、收提示），不重建表</summary>
        private void OnBeat(TimeSpan elapsed)
        {
            if (elapsed - _lastDurationRefresh >= DurationRefreshInterval)
            {
                _lastDurationRefresh = elapsed;

                foreach (var row in _all)
                    row.RefreshDuration();
            }

            if (_noticeText.Length != 0 && elapsed - _noticeStartedAt >= NoticeLifetime)
            {
                _noticeText = string.Empty;
                RaisePropertyChanged(nameof(NoticeText));
            }
        }

        private void SetNotice(string text)
        {
            _noticeText = text ?? string.Empty;
            _noticeStartedAt = _context?.Beat.Elapsed ?? TimeSpan.Zero;
            RaisePropertyChanged(nameof(NoticeText));
        }
    }

    /// <summary>
    /// 一个筛选档位：显示文本 + 谓词。
    ///
    /// 为什么不用"枚举 + switch"：那样每加一档都要同时改枚举、改显示名、改判定三处，
    /// 漏一处就是"选了没反应"。这里一档就是一行，文本与判定写在一起，加档只加一行。
    /// 谓词而不是"取字段值"：状态筛选要表达的是"未确认"（跨 Active / Recovered 两态），
    /// 那不是任何单一字段的等值比较。
    /// </summary>
    public sealed class ScadaAlarmHistoryFilter
    {
        public ScadaAlarmHistoryFilter(string text, Func<ScadaAlarmHistoryRow, bool> match)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            Match = match ?? throw new ArgumentNullException(nameof(match));
        }

        /// <summary>下拉里显示的字</summary>
        public string Text { get; }

        /// <summary>判定谓词</summary>
        public Func<ScadaAlarmHistoryRow, bool> Match { get; }

        /// <summary>"全部"那一档：恒真</summary>
        public static ScadaAlarmHistoryFilter All(string text) => new(text, _ => true);

        public bool Matches(ScadaAlarmHistoryRow row) => Match(row);
    }
}
