using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Core.Database;
using Core.Interfaces.Operations;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Scada;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 操作记录弹窗（只读）。数据源是**软件级**库 `<程序目录>\Data\operations.db`。
    ///
    /// 与资产页「审计」页签的分工：那边读方案内 `Audit\*.csv`（跟着方案走，答"这个资产被谁动过"），
    /// 这边读软件级库（跟着机器走，答"这台机器上发生过什么"）。两者不能合并。
    /// </summary>
    public sealed class OperationsLogViewModel : BindableBase, IDialogAware
    {
        private const int PageSize = 200;

        /// <summary>CSV 表头（列序与列表一致；导出给现场看，列名用中文）。</summary>
        private static readonly string[] CsvHeader =
        {
            "时间", "分类", "动作", "结果", "操作者", "对象", "明细", "方案", "机器", "版本",
        };

        /// <summary>UTF-8 BOM：不带 BOM 的中文 CSV 用 Excel 打开必乱码（同 ScadaAlarmHistoryWriter 口径）。</summary>
        private static readonly byte[] CsvBom = { 0xEF, 0xBB, 0xBF };

        /// <summary>CSV 需要转义的字符（RFC 4180：含逗号/引号/换行才加引号）。</summary>
        private static readonly char[] CsvMustQuote = { ',', '"', '\r', '\n' };

        public string Title => "操作记录";

        public bool CanCloseDialog() => true;

        public void OnDialogClosed()
        {
            _debounceTimer.Stop();
            _skeletonTimer.Stop();
            _debouncePending = false;
            _loadVersion++;   // 在途查询作废（回写被版本号挡掉）
        }

        public void OnDialogOpened(IDialogParameters parameters) => StartReload();

        private readonly SqliteSoftwareOperationLog? _log;
        private readonly ScadaAccessPolicy _policy;

        private readonly Dispatcher _dispatcher;
        private readonly DispatcherTimer _debounceTimer;
        private readonly DispatcherTimer _skeletonTimer;
        private bool _debouncePending;
        private int _loadVersion;
        private bool _isBusy;
        private bool _isLoading;

        public OperationsLogViewModel(ScadaAccessPolicy policy, SqliteSoftwareOperationLog? log = null)
        {
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            _log = log;

            // 防抖与骨架门限都跑在弹窗所在的 UI Dispatcher 上（弹窗与探针都是 STA + Dispatcher）。
            _dispatcher = Dispatcher.CurrentDispatcher;
            _debounceTimer = new DispatcherTimer(DispatcherPriority.Background);
            _debounceTimer.Tick += (_, _) =>
            {
                if (_debouncePending) StartReload();
            };
            _skeletonTimer = new DispatcherTimer(DispatcherPriority.Background);
            _skeletonTimer.Tick += (_, _) =>
            {
                _skeletonTimer.Stop();
                // 设计稿 §5：查询超过 SkeletonDelay 仍未完成才显示骨架（快查询不闪）。
                if (_isBusy) IsLoading = true;
            };

            CategoryOptions = new ObservableCollection<string> { "全部", "Identity", "Permission", "Solution", "Settings", "Safety", "Lifecycle" };
            RangeOptions = new ObservableCollection<RangeOption>
            {
                new("近 7 天", TimeSpan.FromDays(7)),
                new("近 30 天", TimeSpan.FromDays(30)),
                new("近 90 天", TimeSpan.FromDays(90)),
                new("全部", null),
            };

            _selectedCategory = "全部";
            _selectedActor = "全部";
            _selectedRange = RangeOptions[0];

            RefreshCommand = new DelegateCommand(StartReload);
            // 边界禁用（设计稿 §5 禁用态）：首页「上一页」、末页「下一页」置灰，判据看 CanExecute。
            PrevPageCommand = new DelegateCommand(() => ChangePage(-1), () => _page > 1);
            NextPageCommand = new DelegateCommand(() => ChangePage(1), () => _page < PageCount);
            ClearFiltersCommand = new DelegateCommand(ClearFilters);
            CloseCommand = new DelegateCommand(() => RequestClose.Invoke(new DialogParameters(), ButtonResult.Cancel));
            ExportCommand = new DelegateCommand(Export);

            StartReload();
        }

        public DialogCloseListener RequestClose { get; set; }

        public ObservableCollection<OperationRow> Rows { get; } = new();
        public ObservableCollection<string> CategoryOptions { get; }

        /// <summary>操作者筛选候选：首项「全部」，其余来自库里真实出现过的操作者（次数降序）。</summary>
        public ObservableCollection<string> ActorOptions { get; } = new() { "全部" };

        public ObservableCollection<RangeOption> RangeOptions { get; }

        public DelegateCommand RefreshCommand { get; }
        public DelegateCommand PrevPageCommand { get; }
        public DelegateCommand NextPageCommand { get; }
        public DelegateCommand ClearFiltersCommand { get; }
        public DelegateCommand ExportCommand { get; }
        public DelegateCommand CloseCommand { get; }

        /// <summary>是否有查看权限：操作记录含登录失败与权限变更，按「用户管理」权限收口。</summary>
        public bool HasAccess => _policy.Can(ScadaPermissionKeys.UserAdministration, out _);

        public bool IsDenied => !HasAccess;

        private string _selectedCategory;
        public string SelectedCategory
        {
            get => _selectedCategory;
            set { if (SetProperty(ref _selectedCategory, value ?? "全部")) ScheduleReload(FilterDebounce); }
        }

        private string _selectedActor;
        private bool _suppressReload;

        /// <summary>操作者筛选：首项「全部」= 不筛。候选见 <see cref="ActorOptions"/>。</summary>
        public string SelectedActor
        {
            get => _selectedActor;
            set
            {
                if (!SetProperty(ref _selectedActor, value ?? "全部")) return;
                if (!_suppressReload) ScheduleReload(FilterDebounce);
            }
        }

        private RangeOption _selectedRange;
        public RangeOption SelectedRange
        {
            get => _selectedRange;
            set { if (SetProperty(ref _selectedRange, value)) ScheduleReload(FilterDebounce); }
        }

        private string _keyword = string.Empty;
        public string Keyword
        {
            get => _keyword;
            set { if (SetProperty(ref _keyword, value ?? string.Empty)) ScheduleReload(KeywordDebounce); }
        }

        private OperationRow? _selectedRow;
        public OperationRow? SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (!SetProperty(ref _selectedRow, value)) return;
                Feedback = string.Empty;
                RaisePropertyChanged(nameof(HasSelection));
                RaisePropertyChanged(nameof(DetailText));
                RaisePropertyChanged(nameof(DetailMeta));
            }
        }

        public bool HasSelection => SelectedRow != null;
        public string DetailText => SelectedRow == null ? "选中一条记录查看明细" : SelectedRow.Detail;
        public string DetailMeta => SelectedRow == null
            ? string.Empty
            : $"方案：{SelectedRow.SolutionName}   机器：{SelectedRow.Machine}   版本：{SelectedRow.AppVersion}   记录 Id：{SelectedRow.RecordId}";

        private string _feedback = string.Empty;

        /// <summary>页脚瞬时提示（已复制 / 已导出 / 复制失败）。空串 = 不显示。</summary>
        public string Feedback
        {
            get => _feedback;
            private set
            {
                if (SetProperty(ref _feedback, value)) RaisePropertyChanged(nameof(HasFeedback));
            }
        }

        public bool HasFeedback => _feedback.Length > 0;

        private int _page = 1;
        private int _total;
        public string PageText => $"共 {_total} 条 · 第 {_page} / {PageCount} 页";
        public int PageCount => Math.Max(1, (int)Math.Ceiling(_total / (double)PageSize));
        public bool IsEmpty => _total == 0 && HasAccess && !IsLoading;
        public bool HasRows => _total > 0 && HasAccess;

        private string _message = string.Empty;
        public string Message
        {
            get => _message;
            private set => SetProperty(ref _message, value);
        }

        /// <summary>筛选（分类 / 操作者 / 时间）防抖，设计稿 §6：250ms。</summary>
        public TimeSpan FilterDebounce { get; set; } = TimeSpan.FromMilliseconds(250);

        /// <summary>搜索防抖，设计稿 §6：300ms。</summary>
        public TimeSpan KeywordDebounce { get; set; } = TimeSpan.FromMilliseconds(300);

        /// <summary>骨架门限，设计稿 §5：查询超过该时长才显示骨架（默认 300ms，避免快查询闪烁）。</summary>
        public TimeSpan SkeletonDelay { get; set; } = TimeSpan.FromMilliseconds(300);

        /// <summary>查询进行中（含骨架尚未显示的那一段）。</summary>
        public bool IsBusy
        {
            get => _isBusy;
            private set => SetProperty(ref _isBusy, value);
        }

        /// <summary>加载骨架可见（查询已超过 <see cref="SkeletonDelay"/> 仍未完成）。</summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (!SetProperty(ref _isLoading, value)) return;
                RaisePropertyChanged(nameof(IsEmpty));
                RaisePropertyChanged(nameof(HasRows));
            }
        }

        private void ChangePage(int delta)
        {
            var next = Math.Clamp(_page + delta, 1, PageCount);
            if (next == _page) return;
            _page = next;
            StartReload();
        }

        private void ClearFilters()
        {
            _selectedCategory = "全部";
            _selectedActor = "全部";
            _selectedRange = RangeOptions[0];
            _keyword = string.Empty;
            _page = 1;
            RaisePropertyChanged(nameof(SelectedCategory));
            RaisePropertyChanged(nameof(SelectedActor));
            RaisePropertyChanged(nameof(SelectedRange));
            RaisePropertyChanged(nameof(Keyword));
            StartReload();
        }

        /// <summary>筛选类改动：防抖后重查（设计稿 §6：筛选 250ms / 搜索 300ms）。</summary>
        private void ScheduleReload(TimeSpan delay)
        {
            if (delay <= TimeSpan.Zero)
            {
                StartReload();
                return;
            }

            _debounceTimer.Stop();
            _debounceTimer.Interval = delay;
            _debounceTimer.Start();
            _debouncePending = true;
        }

        /// <summary>立即执行挂起的防抖重查（探针 / 程序化场景用；无挂起时无副作用）。</summary>
        public void FlushPendingReload()
        {
            if (!_debouncePending) return;
            StartReload();
        }

        /// <summary>
        /// 发起一次查询：清空当前结果 → 后台查 SQLite → 回 UI 线程应用。
        /// 骨架门限也在这一层：超过 <see cref="SkeletonDelay"/> 仍未回来才显示（设计稿 §5，快查询不闪）。
        /// </summary>
        private void StartReload()
        {
            _debounceTimer.Stop();
            _debouncePending = false;
            var version = ++_loadVersion;

            Rows.Clear();
            SelectedRow = null;
            _total = 0;
            Feedback = string.Empty;
            Message = string.Empty;
            IsLoading = false;

            if (!HasAccess)
            {
                IsBusy = false;
                RaiseAll();
                return;
            }

            if (_log == null)
            {
                Message = "当前没有可用的操作记录库（Data\\operations.db）";
                IsBusy = false;
                RaiseAll();
                return;
            }

            var from = _selectedRange?.Window == null ? (DateTime?)null : DateTime.UtcNow - _selectedRange.Window.Value;
            var category = _selectedCategory == "全部" ? null : _selectedCategory;
            var actor = _selectedActor == "全部" ? null : _selectedActor;
            var keyword = _keyword;
            var offset = (_page - 1) * PageSize;
            var log = _log;

            IsBusy = true;
            RaiseAll();

            if (SkeletonDelay > TimeSpan.Zero)
            {
                _skeletonTimer.Stop();
                _skeletonTimer.Interval = SkeletonDelay;
                _skeletonTimer.Start();
            }
            else
            {
                IsLoading = true;
            }

            Task.Run(() =>
            {
                var actors = log.QueryActors();
                var actorToUse = actor;
                if (actorToUse != null && !actors.Contains(actorToUse, StringComparer.Ordinal))
                    actorToUse = null;   // 选中的操作者已不在库里：按「全部」查，回 UI 后把选中项复位

                var rows = log.QueryPage(
                    category, actorToUse, solutionId: null,
                    fromUtc: from, toUtc: null,
                    keyword: keyword, offset: offset, limit: PageSize,
                    out var total);
                return (Actors: actors, Rows: rows, Total: total);
            }).ContinueWith(task =>
            {
                if (_dispatcher.HasShutdownStarted) return;
                _dispatcher.BeginInvoke(new Action(() => CompleteReload(version, task)));
            }, TaskScheduler.Default);
        }

        private void CompleteReload(
            int version,
            Task<(IReadOnlyList<string> Actors, IReadOnlyList<SoftwareOperationEntry> Rows, int Total)> task)
        {
            if (version != _loadVersion) return;   // 已被更新的查询取代

            _skeletonTimer.Stop();
            IsBusy = false;
            IsLoading = false;

            if (task.Status != TaskStatus.RanToCompletion)
            {
                Message = "读取操作记录失败：" + (task.Exception?.GetBaseException().Message ?? "未知错误");
                RaiseAll();
                return;
            }

            ApplyActors(task.Result.Actors);
            foreach (var entry in task.Result.Rows)
                Rows.Add(new OperationRow(entry));
            _total = task.Result.Total;
            Message = _total == 0 ? "没有符合条件的记录" : string.Empty;
            RaiseAll();
        }

        /// <summary>
        /// 重建操作者候选（后台已查好，这里只做 UI 侧应用；出现次数降序）。
        /// 只在列表真的变化时才动集合：ComboBox 的 ItemsSource 被清空时会把 SelectedItem 推成 null
        /// （TwoWay 回写），无条件重建会把用户当前选中的操作者洗掉，还会多触发一次重查。
        /// </summary>
        private void ApplyActors(IReadOnlyList<string> actors)
        {
            var items = new List<string> { "全部" };
            items.AddRange(actors);
            if (items.SequenceEqual(ActorOptions)) return;

            var previous = _selectedActor;
            _suppressReload = true;
            try
            {
                ActorOptions.Clear();
                foreach (var item in items)
                    ActorOptions.Add(item);
            }
            finally
            {
                _suppressReload = false;
            }

            // 选中的操作者可能已从库里消失（被清理/换库）：回到「全部」，不留一个空选。
            var restored = items.Contains(previous) ? previous : "全部";
            if (!string.Equals(_selectedActor, restored, StringComparison.Ordinal))
            {
                _selectedActor = restored;
                RaisePropertyChanged(nameof(SelectedActor));
            }
        }

        private void RaiseAll()
        {
            RaisePropertyChanged(nameof(PageText));
            RaisePropertyChanged(nameof(PageCount));
            RaisePropertyChanged(nameof(IsEmpty));
            RaisePropertyChanged(nameof(HasRows));
            RaisePropertyChanged(nameof(HasAccess));
            RaisePropertyChanged(nameof(IsDenied));
            PrevPageCommand.RaiseCanExecuteChanged();
            NextPageCommand.RaiseCanExecuteChanged();
        }

        /// <summary>导出按钮：先弹保存框选路径（与项目资产导出的做法一致），落盘走 <see cref="TryExportCsv"/>。</summary>
        private void Export()
        {
            if (!HasAccess) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "导出操作记录",
                Filter = "CSV 文件 (*.csv)|*.csv",
                FileName = "操作记录-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv",
                DefaultExt = ".csv",
                AddExtension = true,
                OverwritePrompt = true,
            };
            if (dialog.ShowDialog() != true) return;

            Feedback = TryExportCsv(dialog.FileName, out var error)
                ? "已导出当前筛选结果 → " + dialog.FileName
                : error;
        }

        /// <summary>
        /// 把**当前筛选结果**（不是当前页）写成 CSV：UTF-8 带 BOM + RFC 4180 转义 + 含表头。
        /// 供命令与断言共用——断言在无窗口环境里直调本方法，绕开保存对话框（对话框只在 <see cref="Export"/> 里）。
        /// 失败不抛（同 ScadaAlarmHistoryWriter 口径）：原因从 <paramref name="error"/> 交出去，由界面提示。
        /// </summary>
        public bool TryExportCsv(string path, out string error)
        {
            error = string.Empty;
            if (!HasAccess) { error = "没有查看权限"; return false; }
            if (_log == null) { error = "当前没有可用的操作记录库（Data\\operations.db）"; return false; }
            if (string.IsNullOrWhiteSpace(path)) { error = "导出路径为空"; return false; }

            var from = _selectedRange?.Window == null ? (DateTime?)null : DateTime.UtcNow - _selectedRange.Window.Value;
            var category = _selectedCategory == "全部" ? null : _selectedCategory;
            var actor = _selectedActor == "全部" ? null : _selectedActor;

            try
            {
                var rows = _log.QueryPage(
                    category, actor, solutionId: null,
                    fromUtc: from, toUtc: null,
                    keyword: _keyword,
                    offset: 0, limit: int.MaxValue, out _);

                var sb = new StringBuilder();
                sb.AppendLine(string.Join(",", CsvHeader));
                foreach (var entry in rows)
                {
                    sb.AppendLine(string.Join(",",
                        Csv(entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),
                        Csv(OperationRow.CategoryLabelOf(entry.Category)),
                        Csv(entry.Action),
                        Csv(entry.Outcome),
                        Csv(entry.Actor),
                        Csv(entry.Subject),
                        Csv(entry.Detail),
                        Csv(entry.SolutionName),
                        Csv(entry.Machine),
                        Csv(entry.AppVersion)));
                }

                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                // FileShare.Read：导出中允许别人读（Excel 可能正开着上一份），但不许并发写。
                using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                stream.Write(CsvBom, 0, CsvBom.Length);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
                writer.Write(sb.ToString());
                return true;
            }
            catch (Exception ex)
            {
                error = "导出失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>RFC 4180 转义：含逗号/引号/换行的字段加双引号、内部引号翻倍（与 ScadaAuditWriter 同口径）。</summary>
        private static string Csv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.IndexOfAny(CsvMustQuote) >= 0
                ? "\"" + value.Replace("\"", "\"\"") + "\""
                : value;
        }

        /// <summary>
        /// 复制选中行明细到剪贴板（设计稿 §6：双击行复制，页脚提示「已复制」）。
        /// 剪贴板被其它进程短暂占用是常态：短重试；最终失败只提示、不抛（同 FlowCanvasViewModel 口径）。
        /// </summary>
        public bool CopySelectedRowDetail()
        {
            var row = SelectedRow;
            if (row == null)
            {
                Feedback = "没有选中记录";
                return false;
            }

            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    Clipboard.SetText(row.Detail);
                    Feedback = "已复制";
                    return true;
                }
                catch (Exception ex)
                {
                    if (attempt == 2)
                    {
                        Feedback = "复制失败：" + ex.Message;
                        return false;
                    }

                    System.Threading.Thread.Sleep(30);
                }
            }

            return false;
        }
    }

    /// <summary>一行操作记录（界面只读展示）。</summary>
    public sealed class OperationRow
    {
        public OperationRow(SoftwareOperationEntry entry)
        {
            Time = entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            RecordId = entry.Id > 0 ? entry.Id.ToString() : "—";
            Category = entry.Category;
            CategoryLabel = CategoryLabelOf(entry.Category);
            Action = entry.Action;
            Outcome = entry.Outcome;
            Actor = string.IsNullOrWhiteSpace(entry.Actor) ? "—" : entry.Actor;
            Subject = string.IsNullOrWhiteSpace(entry.Subject) ? "—" : entry.Subject;
            Detail = string.IsNullOrWhiteSpace(entry.Detail) ? "—" : entry.Detail;
            Machine = entry.Machine;
            AppVersion = entry.AppVersion;
            SolutionName = string.IsNullOrWhiteSpace(entry.SolutionName) ? "—" : entry.SolutionName;
        }

        public string Time { get; }
        public string RecordId { get; }
        public string Category { get; }
        public string CategoryLabel { get; }
        public string Action { get; }
        public string Outcome { get; }
        public string Actor { get; }
        public string Subject { get; }
        public string Detail { get; }
        public string Machine { get; }
        public string AppVersion { get; }
        public string SolutionName { get; }

        public bool IsSuccess => Outcome == "成功";
        public bool IsDeniedOutcome => Outcome == "拒绝";
        public bool IsFailed => Outcome == "失败";

        /// <summary>分类的中文展示名（列表与 CSV 导出共用一份映射）。</summary>
        public static string CategoryLabelOf(string category) => category switch
        {
            "Identity" => "身份",
            "Permission" => "权限",
            "Solution" => "方案",
            "Settings" => "系统参数",
            "Safety" => "安全动作",
            "Lifecycle" => "启动",
            _ => category,
        };
    }

    public sealed class RangeOption
    {
        public RangeOption(string label, TimeSpan? window)
        {
            Label = label;
            Window = window;
        }

        public string Label { get; }
        public TimeSpan? Window { get; }

        /// <summary>ComboBox 选中项显示用（与资产页 FilterOption 同一原因：自定义模板不吃 DisplayMemberPath）。</summary>
        public override string ToString() => Label;
    }
}
