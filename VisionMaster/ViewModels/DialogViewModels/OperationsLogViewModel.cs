using System;
using System.Collections.ObjectModel;
using System.Linq;
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

        public string Title => "操作记录";

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() { }

        public void OnDialogOpened(IDialogParameters parameters) => Reload();

        private readonly SqliteSoftwareOperationLog? _log;
        private readonly ScadaAccessPolicy _policy;

        public OperationsLogViewModel(ScadaAccessPolicy policy, SqliteSoftwareOperationLog? log = null)
        {
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            _log = log;

            CategoryOptions = new ObservableCollection<string> { "全部", "Identity", "Permission", "Solution", "Settings", "Safety", "Lifecycle" };
            RangeOptions = new ObservableCollection<RangeOption>
            {
                new("近 7 天", TimeSpan.FromDays(7)),
                new("近 30 天", TimeSpan.FromDays(30)),
                new("近 90 天", TimeSpan.FromDays(90)),
                new("全部", null),
            };

            _selectedCategory = "全部";
            _selectedRange = RangeOptions[0];

            RefreshCommand = new DelegateCommand(Reload);
            PrevPageCommand = new DelegateCommand(() => ChangePage(-1));
            NextPageCommand = new DelegateCommand(() => ChangePage(1));
            ClearFiltersCommand = new DelegateCommand(ClearFilters);
            CloseCommand = new DelegateCommand(() => RequestClose.Invoke(new DialogParameters(), ButtonResult.Cancel));
            ExportCommand = new DelegateCommand(Export);

            Reload();
        }

        public DialogCloseListener RequestClose { get; set; }

        public ObservableCollection<OperationRow> Rows { get; } = new();
        public ObservableCollection<string> CategoryOptions { get; }
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
            set { if (SetProperty(ref _selectedCategory, value ?? "全部")) Reload(); }
        }

        private RangeOption _selectedRange;
        public RangeOption SelectedRange
        {
            get => _selectedRange;
            set { if (SetProperty(ref _selectedRange, value)) Reload(); }
        }

        private string _keyword = string.Empty;
        public string Keyword
        {
            get => _keyword;
            set { if (SetProperty(ref _keyword, value ?? string.Empty)) Reload(); }
        }

        private OperationRow? _selectedRow;
        public OperationRow? SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (!SetProperty(ref _selectedRow, value)) return;
                RaisePropertyChanged(nameof(HasSelection));
                RaisePropertyChanged(nameof(DetailText));
                RaisePropertyChanged(nameof(DetailMeta));
            }
        }

        public bool HasSelection => SelectedRow != null;
        public string DetailText => SelectedRow == null ? "选中一条记录查看明细" : SelectedRow.Detail;
        public string DetailMeta => SelectedRow == null
            ? string.Empty
            : $"方案：{SelectedRow.SolutionName}   机器：{SelectedRow.Machine}   版本：{SelectedRow.AppVersion}";

        private int _page = 1;
        private int _total;
        public string PageText => $"共 {_total} 条 · 第 {_page} / {PageCount} 页";
        public int PageCount => Math.Max(1, (int)Math.Ceiling(_total / (double)PageSize));
        public bool IsEmpty => _total == 0 && HasAccess;
        public bool HasRows => _total > 0 && HasAccess;

        private string _message = string.Empty;
        public string Message
        {
            get => _message;
            private set => SetProperty(ref _message, value);
        }

        private void ChangePage(int delta)
        {
            var next = Math.Clamp(_page + delta, 1, PageCount);
            if (next == _page) return;
            _page = next;
            Reload();
        }

        private void ClearFilters()
        {
            _selectedCategory = "全部";
            _selectedRange = RangeOptions[0];
            _keyword = string.Empty;
            _page = 1;
            RaisePropertyChanged(nameof(SelectedCategory));
            RaisePropertyChanged(nameof(SelectedRange));
            RaisePropertyChanged(nameof(Keyword));
            Reload();
        }

        private void Reload()
        {
            Rows.Clear();
            SelectedRow = null;
            _total = 0;

            if (!HasAccess)
            {
                RaiseAll();
                return;
            }

            if (_log == null)
            {
                Message = "当前没有可用的操作记录库（Data\\operations.db）";
                RaiseAll();
                return;
            }

            var from = _selectedRange?.Window == null ? (DateTime?)null : DateTime.UtcNow - _selectedRange.Window.Value;
            var category = _selectedCategory == "全部" ? null : _selectedCategory;

            var rows = _log.QueryPage(
                category, actor: null, solutionId: null,
                fromUtc: from, toUtc: null,
                keyword: _keyword,
                offset: (_page - 1) * PageSize, limit: PageSize,
                out _total);

            foreach (var entry in rows)
                Rows.Add(new OperationRow(entry));

            Message = _total == 0 ? "没有符合条件的记录" : string.Empty;
            RaiseAll();
        }

        private void RaiseAll()
        {
            RaisePropertyChanged(nameof(PageText));
            RaisePropertyChanged(nameof(PageCount));
            RaisePropertyChanged(nameof(IsEmpty));
            RaisePropertyChanged(nameof(HasRows));
            RaisePropertyChanged(nameof(HasAccess));
            RaisePropertyChanged(nameof(IsDenied));
        }

        private void Export()
        {
            Message = "导出功能待接：当前只做查看（设计稿 §6 的导出按筛选结果）";
        }
    }

    /// <summary>一行操作记录（界面只读展示）。</summary>
    public sealed class OperationRow
    {
        public OperationRow(SoftwareOperationEntry entry)
        {
            Time = entry.TimestampUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            Category = entry.Category;
            CategoryLabel = entry.Category switch
            {
                "Identity" => "身份",
                "Permission" => "权限",
                "Solution" => "方案",
                "Settings" => "系统参数",
                "Safety" => "安全动作",
                "Lifecycle" => "启动",
                _ => entry.Category,
            };
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
