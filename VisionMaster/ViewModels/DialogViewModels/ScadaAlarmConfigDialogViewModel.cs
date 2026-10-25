using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Threading;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Scada;
using VisionMaster.Services;
using VisionMaster.Services.Hardware;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 组态「报警配置」弹窗：把方案里的报警定义集中到一张<b>平铺表</b>上增删改。
    ///
    /// 报警是方案级内容（跟着 .vms 走、进撤销栈、由 <c>ScadaAlarmEngine</c> 逐条求值），
    /// 没有归属图元，所以它需要一个弹窗，而不是塞进属性面板。
    ///
    /// 三条与本类强相关的口径：
    /// 1. <b>即时写回</b>——没有「确定」，每格编辑当场落进模型并包一层作用域；
    /// 2. <b>弹窗只是投影</b>——计数、筛选、规则句全部从行集合与模型现算，不另存状态；
    /// 3. <b>反馈要贴住出错的那一格</b>——校验失败只标该格 + 行下一条原因，不再用顶部红带。
    ///
    /// 为什么数值列用字符串接输入框：直接绑 <c>double</c> 时用户敲进字母会让绑定静默失效、
    /// 界面看不出异常。收字符串、失焦统一解析，解析不了回灌原值并给中文原因。
    /// </summary>
    public class ScadaAlarmConfigDialogViewModel : BindableBase, IDialogAware
    {
        /// <summary>审计"事件"列写类别名：这一列答的是"哪一类事"，"具体哪一件"在动作列</summary>
        private const string AuditEventText = "报警配置";

        private const string AuditActionCreate = "新建报警";
        private const string AuditActionRemove = "删除报警";
        private const string AuditActionRename = "报警改名";
        private const string AuditActionField = "修改报警";
        private const string AuditActionBind = "选择变量";

        /// <summary>取不到被操作对象名时的审计对象列兜底（正常路径下用不上）</summary>
        private const string AuditSubjectFallback = "报警表";

        /// <summary>组筛选里代表"不筛"的那一项；组名本身是自由文本，不会与它撞车</summary>
        public const string AllGroupsLabel = "全部";

        /// <summary>删除回执自动消失的秒数（设计基线 §7.1 画面 6）</summary>
        private const int ReceiptSeconds = 6;

        private const string FieldName = "Name";
        private const string FieldPrimaryValue = "PrimaryValue";
        private const string FieldDeadband = "Deadband";
        private const string FieldDelay = "Delay";

        private readonly IWorkspaceManager _workspace;
        private readonly IScadaVariablePicker _picker;
        private readonly ScadaAuditWriter? _audit;
        private readonly Func<string?>? _actorProvider;
        private readonly IFlowRunState? _flowRunState;
        private readonly DispatcherTimer? _statusTimer;

        /// <summary>平铺表的全部行，一行 = 一条报警定义，顺序即 <see cref="ScadaDocument.Alarms"/> 的顺序</summary>
        private readonly ObservableCollection<ScadaAlarmConfigRow> _rows = new();

        private readonly List<string> _knownGroupNames = new();
        private ScadaDocument? _subscribedDocument;
        private bool _historySubscribed;
        private int _syncDepth;

        private ScadaAlarmConfigRow? _selectedRow;
        private string? _errorMessage;
        private string _searchText = string.Empty;
        private string _groupFilter = AllGroupsLabel;
        private bool _isRunning;

        private string? _receiptAlarmName;
        private string? _receiptUndoLabel;
        private Guid _receiptAlarmId;
        private int _receiptSecondsLeft;

        /// <param name="workspace">只用来取"当前方案的组态文档"</param>
        /// <param name="picker">「选变量」入口：为某条报警选它要监视的工程变量</param>
        /// <param name="audit">操作审计落盘端；null = 不记（单机调试 / 断言里直接 new 的路径）</param>
        /// <param name="actorProvider">"此刻登录着谁"；取不到时 <see cref="ScadaAuditEntry"/> 自己回落"未登录"</param>
        /// <param name="flowRunState">
        /// 流程是否在运行。只用于标题栏的「运行中」chip 与底栏后果提示；null = 一律按未运行显示。
        /// 不要拿 <c>ScadaMonitoringHost.IsActive</c> 代它：后者是"方案级监视已挂上"，
        /// 打开方案即为真，chip 会永远亮着。
        /// </param>
        public ScadaAlarmConfigDialogViewModel(
            IWorkspaceManager workspace,
            IScadaVariablePicker picker,
            ScadaAuditWriter? audit = null,
            Func<string?>? actorProvider = null,
            IFlowRunState? flowRunState = null)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _picker = picker ?? throw new ArgumentNullException(nameof(picker));
            _audit = audit;
            _actorProvider = actorProvider;
            _flowRunState = flowRunState;

            RowsView = CollectionViewSource.GetDefaultView(_rows);
            RowsView.Filter = MatchesFilter;

            AddCommand = new DelegateCommand(OnAdd);
            RemoveCommand = new DelegateCommand(OnRemove, () => _selectedRow != null);
            PickVariableForRowCommand = new DelegateCommand<object?>(OnPickVariableForRow);
            UndoCommand = new DelegateCommand(OnUndo, () => ScadaEditHistory.CanUndo && !HasReceipt);
            ReceiptUndoCommand = new DelegateCommand(OnReceiptUndo, () => CanUndoReceipt);
            ClearFilterCommand = new DelegateCommand(ClearFilter, () => HasFilter);
            SetGroupFilterCommand = new DelegateCommand<object?>(OnSetGroupFilter);
            CloseCommand = new DelegateCommand(() => RequestClose.Invoke(new DialogParameters(), ButtonResult.OK));

            if (flowRunState != null)
            {
                _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _statusTimer.Tick += (_, _) => TickStatus();
            }
        }

        public DialogCloseListener RequestClose { get; set; }

        public string Title => "报警配置";

        /// <summary>平铺表的行集合</summary>
        public ObservableCollection<ScadaAlarmConfigRow> Rows => _rows;

        /// <summary>筛过的视图（搜索 + 组筛选叠加），DataGrid 绑它</summary>
        public ICollectionView RowsView { get; }

        /// <summary>当前选中的行（选中即展开详情块）</summary>
        public ScadaAlarmConfigRow? SelectedRow
        {
            get => _selectedRow;
            set
            {
                var previous = _selectedRow;
                if (!SetProperty(ref _selectedRow, value))
                    return;

                // 「新增未完成」是瞬态提示：离开这一行就收起，不跟着行漂到别的选中项上
                if (previous != null && !ReferenceEquals(previous, value))
                    previous.IsNewlyAdded = false;

                RemoveCommand.RaiseCanExecuteChanged();
                RaisePropertyChanged(nameof(HasSelection));
            }
        }

        /// <summary>报警条数（含停用；标题栏「共 N 条」）</summary>
        public int AlarmCount => _rows.Count;

        /// <summary>已勾选启用的条数</summary>
        public int EnabledCount => _rows.Count(r => r.IsEnabled);

        /// <summary>已启用且等级为「严重」的条数</summary>
        public int CriticalCount => _rows.Count(r => r.IsEnabled && r.Severity == ScadaAlarmSeverity.Critical);

        /// <summary>已启用但还没选监视变量：引擎对这类槽直接空转（标题栏「缺变量 N」）</summary>
        public int MissingVariableCount => _rows.Count(r => r.IsIncomplete);

        public string TotalHint => $"共 {AlarmCount} 条";
        public string EnabledHint => $"启用 {EnabledCount}";
        public string CriticalHint => $"严重 {CriticalCount}";
        public string MissingHint => $"缺变量 {MissingVariableCount}";

        /// <summary>表里有没有内容</summary>
        public bool HasRows => _rows.Count > 0;

        /// <summary>存在"已启用但没绑变量"的行</summary>
        public bool HasIncomplete => MissingVariableCount > 0;

        /// <summary>启用 / 严重 / 缺变量这三段只在有行时出现（空态只留「共 0 条」）</summary>
        public bool ShowBreakdown => HasRows;

        /// <summary>空方案（一条报警都没有）——与"搜索没匹配"是两个分支</summary>
        public bool ShowEmptyState => !HasRows;

        /// <summary>有没有选中行</summary>
        public bool HasSelection => _selectedRow != null;

        public int VisibleRowCount => RowsView.Cast<object>().Count();

        public int GroupAlarmCount => HasGroupFilter
            ? _rows.Count(r => string.Equals(r.AlarmGroup, _groupFilter, StringComparison.OrdinalIgnoreCase))
            : AlarmCount;

        public string ShownHint => $"显示 {VisibleRowCount} / {AlarmCount}";

        public bool ShowShownHint => HasFilter;

        public bool HasSearch => !string.IsNullOrWhiteSpace(_searchText);

        public bool HasGroupFilter => !string.Equals(_groupFilter, AllGroupsLabel, StringComparison.Ordinal);

        public bool HasFilter => HasSearch || HasGroupFilter;

        /// <summary>有行、有筛选、但一条都没匹配上</summary>
        public bool ShowNoResult => HasRows && HasFilter && VisibleRowCount == 0;

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (!SetProperty(ref _searchText, value ?? string.Empty))
                    return;

                RaiseFilterState();
            }
        }

        /// <summary>组筛选的当前值（<see cref="AllGroupsLabel"/> = 不筛）</summary>
        public string GroupFilter
        {
            get => _groupFilter;
            set
            {
                var target = string.IsNullOrWhiteSpace(value) ? AllGroupsLabel : value.Trim();
                if (string.Equals(_groupFilter, target, StringComparison.Ordinal))
                    return;

                _groupFilter = target;
                RaisePropertyChanged();
                RaiseFilterState();
            }
        }

        /// <summary>组筛选的候选：全部 + 本方案已用过的组名（与报警组历史值同源）</summary>
        public ObservableCollection<string> GroupFilterOptions { get; } = new();

        public string ClearFilterButtonText => HasSearch ? "清除搜索" : "清除筛选";

        public string NoResultTitle => HasSearch
            ? $"没有匹配「{SearchText.Trim()}」的报警"
            : "当前筛选下没有报警";

        public string NoResultDescription
        {
            get
            {
                if (HasSearch)
                {
                    return HasGroupFilter
                        ? $"搜索会同时匹配名称、监视变量与报警组。当前组筛选是「{_groupFilter}」，所以这条搜索只扫该组内的 {GroupAlarmCount} 条。"
                        : $"搜索会同时匹配名称、监视变量与报警组。当前组筛选是「全部」，所以这条搜索扫的是全部 {AlarmCount} 条。";
                }

                return $"当前组筛选是「{_groupFilter}」，这 {AlarmCount} 条报警里没有属于这一组的。";
            }
        }

        /// <summary>没有文档时才会用到的全局错误（行内校验失败走行自己的错误行）</summary>
        public string? ErrorMessage
        {
            get => _errorMessage;
            private set
            {
                if (SetProperty(ref _errorMessage, value))
                    RaisePropertyChanged(nameof(HasError));
            }
        }

        public bool HasError => !string.IsNullOrEmpty(_errorMessage);

        /// <summary>流程是否在运行（每秒刷新；只影响提示，不锁编辑——设计基线 §12 P2）</summary>
        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (!SetProperty(ref _isRunning, value))
                    return;

                RaisePropertyChanged(nameof(ShowRunningChip));
                RaisePropertyChanged(nameof(StatusHint));
                RaisePropertyChanged(nameof(StatusHintIsWarning));
            }
        }

        public bool ShowRunningChip => IsRunning;

        public string StatusHint => IsRunning
            ? "流程运行中：改动下一拍生效，无需重启画面。"
            : "改动即时写入方案、进撤销栈；关窗不回滚。";

        public bool StatusHintIsWarning => IsRunning;

        public bool HasReceipt => _receiptUndoLabel != null;

        public string ReceiptText => _receiptAlarmName == null ? string.Empty : $"已删除报警 [{_receiptAlarmName}]";

        /// <summary>删除回执的就地撤销可用：必须是"删除"还在栈顶，否则会误撤后面的编辑</summary>
        public bool CanUndoReceipt => HasReceipt && string.Equals(ScadaEditHistory.NextUndoLabel, _receiptUndoLabel, StringComparison.Ordinal);

        /// <summary>有回执时全局撤销按钮让位，避免出现两个撤销入口</summary>
        public bool ShowUndo => HasRows && ScadaEditHistory.CanUndo && !HasReceipt;

        public string UndoLabel => ScadaEditHistory.NextUndoLabel ?? string.Empty;

        public string UndoButtonText => UndoLabel.Length == 0 ? string.Empty : $"撤销「{UndoLabel}」";

        /// <summary>
        /// 条件种类的全部候选（下拉数据源）。直接取枚举全量 + <c>DisplayName()</c> 出中文，
        /// 不手写一份平行清单。
        /// </summary>
        public IReadOnlyList<ScadaAlarmKindOption> KindOptions { get; } =
            Enum.GetValues<ScadaAlarmKind>().Select(k => new ScadaAlarmKindOption(k)).ToArray();

        /// <summary>严重度的全部候选（下拉数据源）</summary>
        public IReadOnlyList<ScadaAlarmSeverityOption> SeverityOptions { get; } =
            Enum.GetValues<ScadaAlarmSeverity>().Select(s => new ScadaAlarmSeverityOption(s)).ToArray();

        /// <summary>「报警组」的历史值下拉数据源（本方案已用过的组名，去重 + 排序）</summary>
        public ObservableCollection<string> AlarmGroupSuggestions { get; } = new();

        /// <summary>新增成功后请求视图滚到新行并聚焦名称格（视图做不到就落一条日志，不静默）</summary>
        public event Action<ScadaAlarmConfigRow>? RowAdded;

        public DelegateCommand AddCommand { get; }
        public DelegateCommand RemoveCommand { get; }
        public DelegateCommand<object?> PickVariableForRowCommand { get; }
        public DelegateCommand UndoCommand { get; }
        public DelegateCommand ReceiptUndoCommand { get; }
        public DelegateCommand ClearFilterCommand { get; }
        public DelegateCommand<object?> SetGroupFilterCommand { get; }
        public DelegateCommand CloseCommand { get; }

        /// <summary>当前方案的组态文档；没有打开的方案时为 <c>null</c></summary>
        internal ScadaDocument? Document => _workspace.CurrentSolution?.Scada;

        internal void ReportError(string? message) => ErrorMessage = message;

        internal void AuditField(ScadaAlarmDefinition alarm, string detail)
            => Audit(AuditActionField, true, detail, null, alarm.Name);

        internal void AuditRename(ScadaAlarmDefinition alarm, string detail)
            => Audit(AuditActionRename, true, detail, null, alarm.Name);

        /// <summary>
        /// 包住一次本地模型写：写的过程中 <see cref="ScadaEditHistory.Changed"/> 与
        /// <c>Alarms.CollectionChanged</c> 不触发整表对账（否则 OnAdd 会先被回调加一遍行、
        /// 自己再补一遍，出现重复行）。退出时补刷撤销按钮文案。
        /// </summary>
        internal IDisposable SuspendSync()
        {
            _syncDepth++;
            return new SyncScope(this);
        }

        /// <summary>行内成功写入后通知宿主：计数与筛选结果都要重算</summary>
        internal void NotifyRowMutated(ScadaAlarmConfigRow row)
        {
            if (!_rows.Contains(row))
                return;

            RaiseDerivedState();

            // 行属性变化可能发生在 DataGrid 的编辑事务里，此时同步 Refresh 会被框架拒绝；
            // 筛选结果晚一拍刷新即可（计数是同步的）。
            if (HasFilter)
                ScheduleFilterRefresh();
        }

        /// <summary>重建「报警组」历史值 + 组筛选项（组名集合没变就不动，避免每次敲字都重建菜单）</summary>
        internal void RefreshAlarmGroups()
        {
            var names = Document?.Alarms
                .Select(a => a.AlarmGroup)
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
                .ToArray()
                ?? Array.Empty<string>();

            // 首次打开且方案里一个组都没有时，上面两条集合都是空的，但「全部」这一项仍必须补上，
            // 否则组筛选菜单是空的（只靠名字集合相等就 return 会漏掉这次初始化）。
            if (names.SequenceEqual(_knownGroupNames, StringComparer.Ordinal) && GroupFilterOptions.Count > 0)
                return;

            _knownGroupNames.Clear();
            _knownGroupNames.AddRange(names);

            AlarmGroupSuggestions.Clear();
            foreach (var name in names)
                AlarmGroupSuggestions.Add(name);

            GroupFilterOptions.Clear();
            GroupFilterOptions.Add(AllGroupsLabel);
            foreach (var name in names)
                GroupFilterOptions.Add(name);

            if (!GroupFilterOptions.Contains(_groupFilter))
            {
                _groupFilter = AllGroupsLabel;
                RaisePropertyChanged(nameof(GroupFilter));
                RaiseFilterState();
            }
        }

        private void OnAdd()
        {
            var document = Document;
            if (document == null)
            {
                ReportError("当前没有打开的方案，无法新建报警。");
                Audit(AuditActionCreate, false, null, ErrorMessage);
                return;
            }

            ClearFilter();

            ScadaAlarmDefinition alarm;
            // AddAlarm 内部已包 BeginEdit，这里不要再包一层（会把两次撤销并成一次）
            using (SuspendSync())
            {
                alarm = document.AddAlarm();
            }

            var row = new ScadaAlarmConfigRow(this, alarm) { IsNewlyAdded = true };
            _rows.Add(row);
            RefreshAlarmGroups();
            SelectedRow = row;

            ClearReceipt();
            ReportError(null);
            RaiseDerivedState();
            RaiseHistoryState();
            Audit(AuditActionCreate, true, $"新建报警 [{alarm.Name}]", null, alarm.Name);

            RowAdded?.Invoke(row);
        }

        private void OnRemove()
        {
            var row = _selectedRow;
            if (row == null)
                return;

            var document = Document;
            if (document == null)
            {
                ReportError("当前没有打开的方案，无法删除报警。");
                Audit(AuditActionRemove, false, null, ErrorMessage, row.Name);
                return;
            }

            var name = row.Name;
            var alarmId = row.AlarmId;

            bool removed;
            string error;
            // TryRemoveAlarm 内部已包 BeginEdit
            using (SuspendSync())
            {
                removed = document.TryRemoveAlarm(row.Model, out error);
            }

            if (!removed)
            {
                ReportError(error);
                Audit(AuditActionRemove, false, null, error, name);
                return;
            }

            ReportError(null);
            Audit(AuditActionRemove, true, $"删除报警 [{name}]", null, name);

            row.Detach();
            _rows.Remove(row);
            if (ReferenceEquals(_selectedRow, row))
                SelectedRow = null;

            RefreshAlarmGroups();
            SelectedRow = RowsView.Cast<ScadaAlarmConfigRow>().FirstOrDefault() ?? _rows.FirstOrDefault();

            ShowReceipt(alarmId, name);
            RaiseDerivedState();
            RaiseHistoryState();
        }

        private void OnPickVariableForRow(object? parameter)
        {
            // 参数来自 XAML 的对象型绑定：必须判型，拿错类型只允许静默空跑（R27）
            if (parameter is not ScadaAlarmConfigRow row || !_rows.Contains(row))
                return;

            var document = Document;
            if (document == null)
            {
                ReportError("当前没有打开的方案，无法绑定变量。");
                return;
            }

            _picker.Pick(row.Model.VariableId, row.VariableName, row.Model.ArrayIndex,
                (id, name, arrayIndex) =>
                {
                    var currentDocument = Document;
                    if (currentDocument == null)
                    {
                        ReportError("当前没有打开的方案，无法绑定变量。");
                        return;
                    }

                    string label = new ScadaVariableRef(id, name, arrayIndex).DisplayText;

                    // Bind 是三次属性写（Id + 名字 + 下标），必须包作用域，否则撤销只回退一半
                    using (SuspendSync())
                    using (currentDocument.BeginEdit($"报警 [{row.Name}] 选择变量 [{label}]"))
                    {
                        row.Model.Bind(id, name, arrayIndex);
                    }

                    row.NotifyVariableChanged();
                    ReportError(null);
                    Audit(AuditActionBind, true, $"报警 [{row.Name}] 监视变量 → {label}", null, row.Name);
                });
        }

        private void OnUndo()
        {
            if (!ScadaEditHistory.CanUndo)
                return;

            ClearReceipt();
            ScadaEditHistory.Undo();
        }

        private void OnReceiptUndo()
        {
            if (!CanUndoReceipt)
            {
                ClearReceipt();
                return;
            }

            var alarmId = _receiptAlarmId;
            ClearReceipt();
            ScadaEditHistory.Undo();
            ReconcileRows(alarmId);
        }

        private void OnSetGroupFilter(object? parameter)
        {
            if (parameter is string group)
                GroupFilter = group;
        }

        private void ClearFilter()
        {
            var changed = false;
            if (_searchText.Length > 0)
            {
                _searchText = string.Empty;
                RaisePropertyChanged(nameof(SearchText));
                changed = true;
            }

            if (HasGroupFilter)
            {
                _groupFilter = AllGroupsLabel;
                RaisePropertyChanged(nameof(GroupFilter));
                changed = true;
            }

            if (changed)
                RaiseFilterState();
        }

        private bool MatchesFilter(object item)
        {
            if (item is not ScadaAlarmConfigRow row)
                return false;

            if (HasGroupFilter && !string.Equals(row.AlarmGroup, _groupFilter, StringComparison.OrdinalIgnoreCase))
                return false;

            var query = _searchText.Trim();
            if (query.Length == 0)
                return true;

            return row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || row.VariableDisplay.Contains(query, StringComparison.OrdinalIgnoreCase)
                || row.AlarmGroup.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        private void RaiseFilterState()
        {
            RaisePropertyChanged(nameof(HasSearch));
            RaisePropertyChanged(nameof(HasGroupFilter));
            RaisePropertyChanged(nameof(HasFilter));
            RaisePropertyChanged(nameof(ShowShownHint));
            RaisePropertyChanged(nameof(ClearFilterButtonText));
            ClearFilterCommand.RaiseCanExecuteChanged();
            RefreshFilterNow();
        }

        private void RefreshFilterNow()
        {
            RowsView.Refresh();
            RaisePropertyChanged(nameof(VisibleRowCount));
            RaisePropertyChanged(nameof(GroupAlarmCount));
            RaisePropertyChanged(nameof(ShownHint));
            RaisePropertyChanged(nameof(ShowNoResult));
            RaisePropertyChanged(nameof(NoResultTitle));
            RaisePropertyChanged(nameof(NoResultDescription));
        }

        private bool _filterRefreshPending;

        private void ScheduleFilterRefresh()
        {
            if (_filterRefreshPending)
                return;

            _filterRefreshPending = true;
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _filterRefreshPending = false;
                RefreshFilterNow();
            }));
        }

        private void RaiseDerivedState()
        {
            RaisePropertyChanged(nameof(AlarmCount));
            RaisePropertyChanged(nameof(EnabledCount));
            RaisePropertyChanged(nameof(CriticalCount));
            RaisePropertyChanged(nameof(MissingVariableCount));
            RaisePropertyChanged(nameof(TotalHint));
            RaisePropertyChanged(nameof(EnabledHint));
            RaisePropertyChanged(nameof(CriticalHint));
            RaisePropertyChanged(nameof(MissingHint));
            RaisePropertyChanged(nameof(HasRows));
            RaisePropertyChanged(nameof(HasIncomplete));
            RaisePropertyChanged(nameof(ShowBreakdown));
            RaisePropertyChanged(nameof(ShowEmptyState));
            RaisePropertyChanged(nameof(GroupAlarmCount));
            RaisePropertyChanged(nameof(ShowUndo));
            UndoCommand.RaiseCanExecuteChanged();
        }

        private void RaiseHistoryState()
        {
            if (HasReceipt && !CanUndoReceipt)
                ClearReceipt();

            RaisePropertyChanged(nameof(UndoLabel));
            RaisePropertyChanged(nameof(UndoButtonText));
            RaisePropertyChanged(nameof(ShowUndo));
            RaisePropertyChanged(nameof(CanUndoReceipt));
            UndoCommand.RaiseCanExecuteChanged();
            ReceiptUndoCommand.RaiseCanExecuteChanged();
        }

        private void ShowReceipt(Guid alarmId, string alarmName)
        {
            _receiptAlarmId = alarmId;
            _receiptAlarmName = alarmName;
            _receiptUndoLabel = $"删除报警 [{alarmName}]";
            _receiptSecondsLeft = ReceiptSeconds;

            RaisePropertyChanged(nameof(HasReceipt));
            RaisePropertyChanged(nameof(ReceiptText));
            RaisePropertyChanged(nameof(CanUndoReceipt));
            RaisePropertyChanged(nameof(ShowUndo));
            UndoCommand.RaiseCanExecuteChanged();
            ReceiptUndoCommand.RaiseCanExecuteChanged();
        }

        private void ClearReceipt()
        {
            if (!HasReceipt)
                return;

            _receiptAlarmName = null;
            _receiptUndoLabel = null;
            _receiptAlarmId = Guid.Empty;
            _receiptSecondsLeft = 0;

            RaisePropertyChanged(nameof(HasReceipt));
            RaisePropertyChanged(nameof(ReceiptText));
            RaisePropertyChanged(nameof(CanUndoReceipt));
            RaisePropertyChanged(nameof(ShowUndo));
            UndoCommand.RaiseCanExecuteChanged();
            ReceiptUndoCommand.RaiseCanExecuteChanged();
        }

        /// <summary>每秒刷新运行态 + 删除回执倒计时；断言里直接调它，不依赖消息泵</summary>
        internal void TickStatus()
        {
            IsRunning = _flowRunState?.AnyFlowRunning ?? false;

            if (!HasReceipt)
                return;

            _receiptSecondsLeft--;
            if (_receiptSecondsLeft <= 0)
                ClearReceipt();
        }

        internal int ReceiptSecondsRemaining => _receiptSecondsLeft;

        /// <summary>
        /// 把行集合与文档对齐：删掉已不在文档里的行、按文档顺序插入/移动其余行。
        /// 撤销/重做会直接增删 <c>Alarms</c>，不同步就会留下"表里写着它、模型里却没有"的幽灵行。
        /// </summary>
        private void ReconcileRows(Guid keepAlarmId)
        {
            var alarms = Document?.Alarms;
            if (alarms == null)
            {
                if (_rows.Count > 0)
                {
                    foreach (var row in _rows)
                        row.Detach();
                    _rows.Clear();
                }

                SelectedRow = null;
                RefreshAlarmGroups();
                RaiseDerivedState();
                RefreshFilterNow();
                return;
            }

            for (var i = _rows.Count - 1; i >= 0; i--)
            {
                var row = _rows[i];
                if (alarms.Any(a => ReferenceEquals(a, row.Model)))
                    continue;

                row.Detach();
                _rows.RemoveAt(i);
            }

            for (var i = 0; i < alarms.Count; i++)
            {
                var alarm = alarms[i];
                var row = _rows.FirstOrDefault(r => ReferenceEquals(r.Model, alarm));
                if (row == null)
                {
                    _rows.Insert(Math.Min(i, _rows.Count), new ScadaAlarmConfigRow(this, alarm));
                    continue;
                }

                var current = _rows.IndexOf(row);
                if (current != i)
                    _rows.Move(current, i);
            }

            var keep = keepAlarmId != Guid.Empty ? keepAlarmId : _selectedRow?.AlarmId ?? Guid.Empty;
            var next = _rows.FirstOrDefault(r => r.AlarmId == keep);
            if (next == null && _selectedRow != null && _rows.Contains(_selectedRow))
                next = _selectedRow;
            next ??= _rows.FirstOrDefault();
            SelectedRow = next;

            RefreshAlarmGroups();
            RaiseDerivedState();
            RefreshFilterNow();
        }

        private void SubscribeToDocument(ScadaDocument? document)
        {
            if (!_historySubscribed)
            {
                ScadaEditHistory.Changed += OnHistoryChanged;
                _historySubscribed = true;
            }

            if (ReferenceEquals(_subscribedDocument, document))
                return;

            if (_subscribedDocument != null)
                _subscribedDocument.Alarms.CollectionChanged -= OnAlarmsChanged;

            _subscribedDocument = document;
            if (document != null)
                document.Alarms.CollectionChanged += OnAlarmsChanged;
        }

        private void UnsubscribeFromDocument()
        {
            if (_historySubscribed)
            {
                ScadaEditHistory.Changed -= OnHistoryChanged;
                _historySubscribed = false;
            }

            if (_subscribedDocument != null)
            {
                _subscribedDocument.Alarms.CollectionChanged -= OnAlarmsChanged;
                _subscribedDocument = null;
            }
        }

        private void OnHistoryChanged(object? sender, EventArgs e)
        {
            if (_syncDepth > 0)
                return;

            ReconcileRows(_selectedRow?.AlarmId ?? Guid.Empty);
            RaiseHistoryState();
        }

        private void OnAlarmsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_syncDepth > 0)
                return;

            ReconcileRows(_selectedRow?.AlarmId ?? Guid.Empty);
        }

        private void Audit(string action, bool ok, string? detail, string? error, string? subject = null)
            => _audit?.Append(new ScadaAuditEntry(
                _actorProvider?.Invoke(),
                subject ?? _selectedRow?.Name ?? AuditSubjectFallback,
                AuditEventText,
                action,
                ok ? ScadaAuditOutcome.Success : ScadaAuditOutcome.Failed,
                ok ? detail : error));

        private sealed class SyncScope : IDisposable
        {
            private readonly ScadaAlarmConfigDialogViewModel _owner;
            private bool _disposed;

            internal SyncScope(ScadaAlarmConfigDialogViewModel owner) => _owner = owner;

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _owner._syncDepth--;
                if (_owner._syncDepth == 0)
                    _owner.RaiseHistoryState();
            }
        }

        #region IDialogAware

        public bool CanCloseDialog() => true;

        public void OnDialogClosed()
        {
            UnsubscribeFromDocument();
            _statusTimer?.Stop();

            foreach (var row in _rows)
                row.Detach();
            _rows.Clear();

            SelectedRow = null;
            ClearReceipt();
            ErrorMessage = null;
            RaiseDerivedState();
            RefreshFilterNow();
        }

        /// <summary>
        /// 打开时现读整张报警表。每次打开都重建，是因为两次打开之间用户可能已经改过方案
        /// （撤销、加载别的方案），缓存跨弹窗复用会给出幽灵条目。
        /// </summary>
        public void OnDialogOpened(IDialogParameters parameters)
        {
            var document = Document;
            SubscribeToDocument(document);
            RefreshAlarmGroups();
            ReconcileRows(Guid.Empty);
            ErrorMessage = null;

            IsRunning = _flowRunState?.AnyFlowRunning ?? false;
            _statusTimer?.Start();
            RaiseHistoryState();
        }

        #endregion
    }

    /// <summary>
    /// 平铺表里的一行：一条报警定义，<b>可就地编辑</b>。
    ///
    /// 行持有宿主视图模型，因为每次写入都要包一层 <see cref="ScadaDocument.BeginEdit"/>、
    /// 失败时要把原因摆到"这一行这一格"上、还要记审计——原料都在宿主手上。
    /// </summary>
    public sealed class ScadaAlarmConfigRow : BindableBase
    {
        private readonly ScadaAlarmConfigDialogViewModel _host;
        private readonly ScadaAlarmDefinition _model;

        private string? _inlineError;
        private string? _inlineErrorField;
        private bool _isNewlyAdded;

        internal ScadaAlarmConfigRow(ScadaAlarmConfigDialogViewModel host, ScadaAlarmDefinition model)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _model.PropertyChanged += OnModelPropertyChanged;

            ApplyGroupCommand = new DelegateCommand<string>(group => AlarmGroup = group ?? string.Empty);
        }

        /// <summary>底层报警定义（删除 / 选变量时要把它交给文档）</summary>
        public ScadaAlarmDefinition Model => _model;

        /// <summary>稳定身份（重建后按它把选中落回同一条）</summary>
        public Guid AlarmId => _model.AlarmId;

        /// <summary>「报警组」历史值候选（直接转发宿主的集合，不复制快照）</summary>
        public ObservableCollection<string> GroupSuggestions => _host.AlarmGroupSuggestions;

        public DelegateCommand<string> ApplyGroupCommand { get; }

        /// <summary>刚新增、尚未离开这一行（瞬态标记，不入模型、不进撤销栈）</summary>
        public bool IsNewlyAdded
        {
            get => _isNewlyAdded;
            internal set
            {
                if (SetProperty(ref _isNewlyAdded, value))
                    RaisePropertyChanged(nameof(ShowSetupHint));
            }
        }

        /// <summary>详情块里的「待完成」提示只在刚新增且尚未离开该行时出现（设计基线 P5）</summary>
        public bool ShowSetupHint => _isNewlyAdded;

        public bool HasInlineError => !string.IsNullOrEmpty(_inlineError);

        public string InlineError => _inlineError ?? string.Empty;

        public bool NameHasError => _inlineErrorField == "Name";

        public bool PrimaryValueHasError => _inlineErrorField == "PrimaryValue";

        public bool DeadbandHasError => _inlineErrorField == "Deadband";

        public bool DelaySecondsHasError => _inlineErrorField == "Delay";

        /// <summary>报警名（就地改名；查重与空名拒绝由文档负责）</summary>
        public string Name
        {
            get => _model.Name;
            set
            {
                var target = (value ?? string.Empty).Trim();
                var before = _model.Name;
                if (string.Equals(before, target, StringComparison.Ordinal))
                    return;

                var document = _host.Document;
                if (document == null)
                {
                    _host.ReportError("当前没有打开的方案，无法改名。");
                    RaisePropertyChanged();
                    return;
                }

                bool renamed;
                string error;
                // TryRenameAlarm 内部已包 BeginEdit，并带空名校验 + 查重
                using (_host.SuspendSync())
                {
                    renamed = document.TryRenameAlarm(_model, target, out error);
                }

                if (!renamed)
                {
                    SetInlineError("Name", $"{error}（已恢复为原名）");
                    RaisePropertyChanged();
                    return;
                }

                ClearInlineError();
                _host.AuditRename(_model, $"报警改名：{before} → {target}");
                _host.ReportError(null);
                RaisePropertyChanged();
                _host.NotifyRowMutated(this);
            }
        }

        public ScadaAlarmKind Kind
        {
            get => _model.Kind;
            set
            {
                if (_model.Kind == value)
                    return;

                if (!TryWrite($"修改报警 [{_model.Name}] 的条件种类", () => _model.Kind = value))
                    return;

                // 领域层刻意不联动严重度（用户特意调过的严重度不该被静默推翻），这里也不补
                _host.AuditField(_model, $"条件种类：{value.DisplayName()}");
            }
        }

        public ScadaAlarmSeverity Severity
        {
            get => _model.Severity;
            set
            {
                if (_model.Severity == value)
                    return;

                if (!TryWrite($"修改报警 [{_model.Name}] 的报警等级", () => _model.Severity = value))
                    return;

                _host.AuditField(_model, $"报警等级：{value.DisplayName()}");
            }
        }

        /// <summary>阈值（字符串接输入框；失焦提交，解析不了回灌原值）</summary>
        public string ThresholdText
        {
            get => Format(_model.Threshold);
            set => CommitNumber(value, nameof(ThresholdText), "阈值", "PrimaryValue",
                () => _model.Threshold, v => _model.Threshold = v);
        }

        /// <summary>回差（小于 0 会被领域层夹到 0）</summary>
        public string DeadbandText
        {
            get => Format(_model.Deadband);
            set => CommitNumber(value, nameof(DeadbandText), "回差", "Deadband",
                () => _model.Deadband, v => _model.Deadband = v);
        }

        /// <summary>延时（秒）（小于 0 会被领域层夹到 0；断线种类不展示这一格）</summary>
        public string DelaySecondsText
        {
            get => Format(_model.DelaySeconds);
            set => CommitNumber(value, nameof(DelaySecondsText), "延时", "Delay",
                () => _model.DelaySeconds, v => _model.DelaySeconds = v);
        }

        /// <summary>断线判定秒数（小于 1 会被领域层夹到 1）</summary>
        public string StaleSecondsText
        {
            get => Format(_model.StaleSeconds);
            set => CommitNumber(value, nameof(StaleSecondsText), "断线判定", "PrimaryValue",
                () => _model.StaleSeconds, v => _model.StaleSeconds = v);
        }

        /// <summary>
        /// 主表「阈值」列绑的那一格：四限绑 Threshold、断线绑 StaleSeconds、布尔不展示。
        /// 判定值必须出现在表格里（设计基线 D2/F2），否则扫表时看不见断线的 5 秒。
        /// </summary>
        public string PrimaryValueText
        {
            get => IsStaleKind ? StaleSecondsText : IsLimitKind ? ThresholdText : string.Empty;
            set
            {
                if (IsStaleKind)
                    StaleSecondsText = value;
                else if (IsLimitKind)
                    ThresholdText = value;
            }
        }

        public bool ShowsPrimaryValueInput => IsLimitKind || IsStaleKind;

        /// <summary>断线那一格右侧的单位（四限/布尔不占单位槽）</summary>
        public string ThresholdUnit => IsStaleKind ? "s" : string.Empty;

        public bool HasThresholdUnit => IsStaleKind;

        /// <summary>启用开关（停用后运行态不求值这条报警，但配置保留）</summary>
        public bool IsEnabled
        {
            get => _model.IsEnabled;
            set
            {
                if (_model.IsEnabled == value)
                    return;

                if (!TryWrite(value ? $"启用报警 [{_model.Name}]" : $"停用报警 [{_model.Name}]", () => _model.IsEnabled = value))
                    return;

                _host.AuditField(_model, value ? "启用" : "停用");
            }
        }

        /// <summary>已启用但未绑变量：引擎跳过求值，标题栏「缺变量 N」按它计数</summary>
        public bool IsIncomplete => _model.IsEnabled && !HasVariable;

        /// <summary>报警组（自由文本 + 历史值下拉；留空表示不分组）</summary>
        public string AlarmGroup
        {
            get => _model.AlarmGroup ?? string.Empty;
            set
            {
                var target = (value ?? string.Empty).Trim();
                var current = _model.AlarmGroup ?? string.Empty;
                if (string.Equals(current, target, StringComparison.Ordinal))
                    return;

                if (!TryWrite($"修改报警 [{_model.Name}] 的报警组",
                        () => _model.AlarmGroup = target.Length == 0 ? null : target))
                    return;

                _host.AuditField(_model, $"报警组：{Show(current)} → {Show(target)}");
                _host.RefreshAlarmGroups();
                _host.NotifyRowMutated(this);
            }
        }

        public string Message
        {
            get => _model.Message ?? string.Empty;
            set => CommitText(value, nameof(Message), "信息文本", () => _model.Message, v => _model.Message = v);
        }

        public string Cause
        {
            get => _model.Cause ?? string.Empty;
            set => CommitText(value, nameof(Cause), "故障原因", () => _model.Cause, v => _model.Cause = v);
        }

        public string Remedy
        {
            get => _model.Remedy ?? string.Empty;
            set => CommitText(value, nameof(Remedy), "解决措施", () => _model.Remedy, v => _model.Remedy = v);
        }

        public string ExtraInfo
        {
            get => _model.ExtraInfo ?? string.Empty;
            set => CommitText(value, nameof(ExtraInfo), "附加信息", () => _model.ExtraInfo, v => _model.ExtraInfo = v);
        }

        public string? VariableName => _model.VariableName;

        public string VariableDisplay
            => string.IsNullOrWhiteSpace(_model.VariableName) ? "未选变量" : _model.ToReference().DisplayText;

        public bool HasVariable => !string.IsNullOrWhiteSpace(_model.VariableName);

        public string KindText => _model.Kind.DisplayName();

        public string SeverityText => _model.Severity.DisplayName();

        public bool IsLimitKind => _model.Kind.IsLimit();

        public bool IsBooleanKind => _model.Kind.IsBoolean();

        public bool IsStaleKind => _model.Kind == ScadaAlarmKind.Stale;

        private bool IsHighLimit
            => _model.Kind == ScadaAlarmKind.High || _model.Kind == ScadaAlarmKind.HighHigh;

        public bool ShowDeadband => IsLimitKind;

        public bool ShowDelay => IsLimitKind || IsBooleanKind;

        public bool ShowStaleNote => IsStaleKind;

        // ---- 规则句（按引擎口径生成；数值单独成 Run 以便加粗） ----

        public string RuleSubject => HasVariable ? VariableDisplay : "（未选变量）";

        public string RulePredicate
        {
            get
            {
                if (IsStaleKind) return " 连续 ";
                if (IsBooleanKind) return _model.Kind == ScadaAlarmKind.BoolOn ? " 为真" : " 为假";
                return IsHighLimit ? " > " : " < ";
            }
        }

        public string RuleValue => IsStaleKind
            ? Format(_model.StaleSeconds)
            : IsLimitKind ? Format(_model.Threshold) : string.Empty;

        public string RuleAfterValue
        {
            get
            {
                if (IsStaleKind)
                    return string.Empty;

                if (IsBooleanKind)
                    return _model.DelaySeconds > 0 ? " 持续 " : string.Empty;

                if (_model.DelaySeconds > 0)
                    return " 持续 ";

                return IsHighLimit ? " 立即报警；回落到 " : " 立即报警；回升到 ";
            }
        }

        public string RuleDelay => _model.DelaySeconds > 0 && (IsLimitKind || IsBooleanKind)
            ? Format(_model.DelaySeconds)
            : string.Empty;

        public string RuleBeforeRecovery
        {
            get
            {
                if (IsStaleKind)
                    return " 秒没有收到新值即报警；收到新值即恢复。";

                if (IsBooleanKind)
                {
                    var back = _model.Kind == ScadaAlarmKind.BoolOn ? "假" : "真";
                    return _model.DelaySeconds > 0
                        ? $" 秒即报警；回到{back}即恢复。"
                        : $" 即报警；回到{back}即恢复。";
                }

                // 四限的回退方向由高低决定：高限「回落到」，低限「回升到」。
                // 延时为 0 时前一句已经写成「立即报警；回落/回升到」，这里必须空着，
                // 否则会拼出「立即报警；回落到 报警；回落到 ≤ …」。
                if (_model.DelaySeconds <= 0)
                    return string.Empty;

                return IsHighLimit ? " 秒即报警；回落到 " : " 秒即报警；回升到 ";
            }
        }

        public string RuleRecovery
        {
            get
            {
                if (!IsLimitKind)
                    return string.Empty;

                var recovery = IsHighLimit
                    ? _model.Threshold - _model.Deadband
                    : _model.Threshold + _model.Deadband;
                return $"{(IsHighLimit ? "≤" : "≥")} {Format(recovery)}";
            }
        }

        public string RuleTail => IsLimitKind ? " 才恢复。" : string.Empty;

        /// <summary>断线不显示回差/延时，用一句话说明"超时本身就是延时"（设计基线 D3）</summary>
        public string StaleNote
            => $"断线种类不使用回差与延时（超时本身就是延时），所以这两格在本行不出现——右上「阈值 {Format(_model.StaleSeconds)} s」就是它的判定值。";

        /// <summary>新增行的「待完成」提示标题（随绑定状态换口径：没绑变量是第一步，绑了变量是核对判定值）</summary>
        public string SetupHintTitle => HasVariable ? "检查判定值" : "先选监视变量";

        /// <summary>新增行的「待完成」提示正文（随实际种类/绑定状态变化，避免文案说谎）</summary>
        public string SetupHintBody
        {
            get
            {
                if (!HasVariable)
                {
                    var tail = IsLimitKind && _model.Threshold == 0
                        ? "新建默认是「高限 > 0」，选好变量后请先把阈值改成工艺值——否则真实值一出现就会立即报警。"
                        : $"当前是「{KindText}」，选好变量后请确认判定值与报警等级。";
                    return "这条报警已经启用，但未绑定时引擎直接跳过求值，运行态永远不会报。" + tail;
                }

                return IsLimitKind && _model.Threshold == 0
                    ? "变量已选好，但阈值还是默认 0：真实值一出现就会立即报警，请先改成工艺值。"
                    : "变量已选好，这条报警已经参与运行态求值；请确认判定值与报警等级。";
            }
        }

        /// <summary>取消对模型的订阅（行被移除时必须调，否则模型活着、行也活着）</summary>
        internal void Detach()
        {
            _model.PropertyChanged -= OnModelPropertyChanged;
            IsNewlyAdded = false;
            ClearInlineError();
        }

        /// <summary>选完变量后刷新变量列与规则句（Id / 名字是模型属性，订阅会兜住，这里显式再保一次）</summary>
        internal void NotifyVariableChanged()
        {
            RaisePropertyChanged(nameof(VariableName));
            RaisePropertyChanged(nameof(VariableDisplay));
            RaisePropertyChanged(nameof(HasVariable));
            RaisePropertyChanged(nameof(IsIncomplete));
            RaisePropertyChanged(nameof(RuleSubject));
            RaisePropertyChanged(nameof(SetupHintTitle));
            RaisePropertyChanged(nameof(SetupHintBody));
            ClearInlineError();
            _host.NotifyRowMutated(this);
        }

        private void SetInlineError(string field, string message)
        {
            _inlineErrorField = field;
            _inlineError = message;
            RaiseInlineErrorState();
        }

        private void ClearInlineError()
        {
            if (_inlineError == null && _inlineErrorField == null)
                return;

            _inlineError = null;
            _inlineErrorField = null;
            RaiseInlineErrorState();
        }

        private void RaiseInlineErrorState()
        {
            RaisePropertyChanged(nameof(HasInlineError));
            RaisePropertyChanged(nameof(InlineError));
            RaisePropertyChanged(nameof(NameHasError));
            RaisePropertyChanged(nameof(PrimaryValueHasError));
            RaisePropertyChanged(nameof(DeadbandHasError));
            RaisePropertyChanged(nameof(DelaySecondsHasError));
        }

        /// <summary>
        /// 包一层作用域写模型。拿不到文档（没有打开的方案）时不写、报全局错误、并把界面回灌成原值。
        /// 返回 false 表示没写成功，调用方不该接着记审计。
        /// </summary>
        private bool TryWrite(string label, Action write)
        {
            var document = _host.Document;
            if (document == null)
            {
                _host.ReportError("当前没有打开的方案，无法修改报警。");
                RaisePropertyChanged(null);
                return false;
            }

            using (_host.SuspendSync())
            using (document.BeginEdit(label))
            {
                write();
            }

            _host.ReportError(null);
            return true;
        }

        /// <summary>
        /// 数值列的统一提交口径：空串与解析失败都回灌原值 + 在该格给中文原因，
        /// 等值不占撤销位。解析成功后按模型的实际落值记审计（回差 / 延时 / 断线都会被夹下限）。
        /// </summary>
        private void CommitNumber(string? text, string propertyName, string label, string field,
            Func<double> read, Action<double> write)
        {
            var before = read();
            var raw = (text ?? string.Empty).Trim();

            if (raw.Length == 0)
            {
                SetInlineError(field, $"「{label}」不能为空；已恢复成 {Format(before)}。");
                RaisePropertyChanged(propertyName);
                RaisePropertyChanged(nameof(PrimaryValueText));
                return;
            }

            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                SetInlineError(field, $"「{label}」请填数字，当前填的是「{raw}」；已恢复成 {Format(before)}。");
                RaisePropertyChanged(propertyName);
                RaisePropertyChanged(nameof(PrimaryValueText));
                return;
            }

            if (parsed.Equals(before))
            {
                ClearInlineError();
                return;
            }

            if (!TryWrite($"修改报警 [{_model.Name}] 的{label}", () => write(parsed)))
                return;

            var after = read();
            ClearInlineError();
            _host.AuditField(_model, $"{label}：{Format(before)} → {Format(after)}");
            RaisePropertyChanged(propertyName);
            RaisePropertyChanged(nameof(PrimaryValueText));
        }

        /// <summary>长文本列的统一提交口径：等值不占撤销位，空串按"未填"落成 null。</summary>
        private void CommitText(string? value, string propertyName, string label, Func<string?> read, Action<string?> write)
        {
            var before = read() ?? string.Empty;
            var after = value ?? string.Empty;
            if (string.Equals(before, after, StringComparison.Ordinal))
                return;

            if (!TryWrite($"修改报警 [{_model.Name}] 的{label}",
                    () => write(after.Length == 0 ? null : after)))
                return;

            ClearInlineError();
            _host.AuditField(_model, $"{label}已更新");
            RaisePropertyChanged(propertyName);
        }

        private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            RaisePropertyChanged(null);
            _host.NotifyRowMutated(this);
        }

        /// <summary>数值的显示 / 解析口径（不变文化，避免"小数点变成逗号"随语言环境漂移）</summary>
        private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private static string Show(string text) => text.Length == 0 ? "（空）" : text;
    }

    /// <summary>
    /// 「条件种类」下拉框的一项：把"给人看的中文名"和"落盘的枚举值"配成一对，
    /// 让 ComboBox 用 <c>SelectedValuePath</c> + <c>SelectedValue</c> 直接绑。
    /// </summary>
    public sealed class ScadaAlarmKindOption
    {
        public ScadaAlarmKindOption(ScadaAlarmKind kind)
        {
            Kind = kind;
            DisplayName = kind.DisplayName();
        }

        /// <summary>写回模型的值</summary>
        public ScadaAlarmKind Kind { get; }

        /// <summary>下拉里显示的名字</summary>
        public string DisplayName { get; }
    }

    /// <summary>「报警等级」下拉框的一项（手法同 <see cref="ScadaAlarmKindOption"/>）</summary>
    public sealed class ScadaAlarmSeverityOption
    {
        public ScadaAlarmSeverityOption(ScadaAlarmSeverity severity)
        {
            Severity = severity;
            DisplayName = severity.DisplayName();
        }

        /// <summary>写回模型的值</summary>
        public ScadaAlarmSeverity Severity { get; }

        /// <summary>下拉里显示的名字</summary>
        public string DisplayName { get; }
    }
}
