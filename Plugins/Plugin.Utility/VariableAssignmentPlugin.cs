using Core.Commands;
using Core.Interfaces;
using Plugin.Util.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Input;

namespace VisionMaster.Plugins.Util
{
    /// <summary>
    /// 变量赋值插件（v3：一个节点写多个变量）。
    ///
    /// 兼容策略：
    /// - 旧四端口 Name / Value / Scope / CreateIfNotExists **保留**：旧 .vms 的键与旧连线仍能解析；
    ///   旧配置（Rows 为空）首次打开/编译时迁移成 1 行，值仍走旧 Value 端口。
    /// - 新行用 <c>Value_&lt;RowId&gt;</c> 动态输入端口；端口名只由 RowId 决定，改名不断线。
    /// - Rows 非空时以 Rows 为权威；旧四端口只用于回退旧版本与旧连线兼容。
    /// </summary>
    [Display(
        Name = "变量赋值",
        GroupName = "变量操作",
        Description = "为已定义的本地变量赋值（一个节点可写多个变量）",
        ShortName = "\uf044"
    )]
    [ParallelSafe] // 二期真并行：Runtime 写走记账字典; Global 写由编辑器检查
    public class VariableAssignmentPlugin : VisionPluginBase, IPluginCustomViewProvider, IPluginConfigContextProvider
    {
        #region 旧端口（兼容，不可删）

        /// <summary>
        /// 变量名称输入端口（旧版单值模型；新视图不直接绑定它）
        /// </summary>
        /// 这三个端口都是"界面上手填"的参数，不是"从上游接线"的参数。
        /// 必须显式写 IsRequired = false —— InputPort.IsRequired 默认就是 true，
        /// 而编译期的必填检查是「IsRequired 且没有连线来源」：只看有没有连线，不看有没有填值。
        /// 于是默认值下，只要用户没用连线喂这三个参数（也就是正常用法），编译必然报
        /// 「必填参数 Name/Value/CreateIfNotExists 未配置」，这个节点等于用不了。
        public InputPort<string> VariableName { get; } = new InputPort<string>("Name", "", "目标变量名称") { IsRequired = false };

        /// <summary>
        /// 赋值内容输入端口（旧版单值模型；迁移行继续用它承载旧连线/旧常量）
        /// </summary>
        public InputPort<object> Value { get; } = new InputPort<object>("Value", null, "要赋的值") { IsRequired = false };

        /// <summary>
        /// 是否创建不存在的变量输入端口（旧版单值模型）
        /// </summary>
        public InputPort<bool> CreateIfNotExists { get; } = new InputPort<bool>("CreateIfNotExists", false, "变量不存在时是否自动创建（仅作用域=运行时有效）") { IsRequired = false };

        /// <summary>作用域取值：运行时变量（默认）</summary>
        public const string ScopeRuntime = "Runtime";

        /// <summary>作用域取值：全局变量</summary>
        public const string ScopeGlobal = "Global";

        /// <summary>行数软上限：每行一个动态端口，32 行约 100+ 控件。</summary>
        public const int MaxRows = 32;

        /// <summary>
        /// 作用域（旧版单值模型）：写运行时变量还是全局变量。
        ///
        /// 为什么是 string 而不是枚举 —— 踩过：
        /// InputPort&lt;T&gt;.Value 的 setter 对枚举只做硬转换，不做"从数字/文本解析"。
        /// 而本参数要经 InputValues 存盘（枚举会序列化成 int），读回来赋给端口时
        /// 直接抛 InvalidCastException: Invalid cast from 'System.Int32' to '枚举类型'，
        /// 方案一打开就炸。string 两端都能无损往返，另外取值做白名单校验（见 RunAlgorithm）。
        /// </summary>
        public InputPort<string> Scope { get; } =
            new InputPort<string>("Scope", ScopeRuntime, "写到哪里：Runtime=运行时变量 / Global=全局变量")
            { IsRequired = false };

        #endregion

        #region 行列表（v3）

        private ObservableCollection<AssignmentRow> _rows = new();
        private bool _loading;

        /// <summary>
        /// 赋值行（随方案持久化）。非空时以它为权威；旧四端口只做兼容。
        /// </summary>
        [StepConfig]
        public ObservableCollection<AssignmentRow> Rows
        {
            get => _rows;
            set
            {
                var next = value ?? new ObservableCollection<AssignmentRow>();
                if (ReferenceEquals(_rows, next)) return;
                UnhookRows(_rows);
                _rows = next;
                HookRows(_rows);
                OnPropertyChanged(nameof(Rows));
                if (!_loading)
                {
                    RebuildRowVms();
                    RefreshStatus();
                }
            }
        }

        /// <summary>行列表的界面投影（视图 DataTemplate 的 DataContext）。</summary>
        public ObservableCollection<AssignmentRowVm> RowVms { get; } = new();

        private AssignmentRowVm? _selectedRow;

        /// <summary>当前选中行（右侧详情卡绑定它）。</summary>
        public AssignmentRowVm? SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (SetProperty(ref _selectedRow, value))
                {
                    OnPropertyChanged(nameof(HasSelectedRow));
                    OnPropertyChanged(nameof(PendingDeleteText));
                }
            }
        }

        public bool HasSelectedRow => SelectedRow != null;
        public bool HasRows => RowVms.Count > 0;

        private bool _isNarrow;

        /// <summary>640 窄档：由视图 SizeChanged 写入，用于切换两行式行模板。</summary>
        public bool IsNarrow
        {
            get => _isNarrow;
            set => SetProperty(ref _isNarrow, value);
        }

        private string _statusText = "0 行 · 未配置";
        public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

        private string _statusLevel = "None";
        public string StatusLevel { get => _statusLevel; private set => SetProperty(ref _statusLevel, value); }

        private string _rowCountText = "0 行";
        public string RowCountText { get => _rowCountText; private set => SetProperty(ref _rowCountText, value); }

        private string _limitText = "上限 32 行 · 当前 0 行 · 行顺序即执行顺序";
        public string LimitText { get => _limitText; private set => SetProperty(ref _limitText, value); }

        private int _errorCount;
        public int ErrorCount { get => _errorCount; private set => SetProperty(ref _errorCount, value); }

        private int _warnCount;
        public int WarnCount { get => _warnCount; private set => SetProperty(ref _warnCount, value); }

        public bool HasErrors => ErrorCount > 0;
        public bool HasWarnings => WarnCount > 0;
        public bool CanAddRow => Rows.Count < MaxRows;

        private string _errorSummary = string.Empty;
        public string ErrorSummary { get => _errorSummary; private set => SetProperty(ref _errorSummary, value); }

        private bool _showMigrationBanner;
        public bool ShowMigrationBanner { get => _showMigrationBanner; private set => SetProperty(ref _showMigrationBanner, value); }

        private string _migrationText = string.Empty;
        public string MigrationText { get => _migrationText; private set => SetProperty(ref _migrationText, value); }

        private AssignmentRowVm? _pendingDelete;
        public AssignmentRowVm? PendingDelete
        {
            get => _pendingDelete;
            private set
            {
                if (SetProperty(ref _pendingDelete, value))
                {
                    OnPropertyChanged(nameof(HasPendingDelete));
                    OnPropertyChanged(nameof(PendingDeleteText));
                }
            }
        }

        public bool HasPendingDelete => PendingDelete != null;

        public string PendingDeleteText
        {
            get
            {
                if (PendingDelete == null) return string.Empty;
                var index = RowVms.IndexOf(PendingDelete) + 1;
                var address = PendingDelete.StepData?.GetLinkedAddress(PendingDelete.Row.ValuePortName);
                var suffix = string.IsNullOrWhiteSpace(address)
                    ? "该行来自旧版单条配置，删除后不可恢复。"
                    : $"该行的「值」来自上游（{address}），删除会一并解除链接。";
                return $"删除第 {index} 行？{suffix}";
            }
        }

        #endregion

        #region 命令

        private ICommand? _addRowCommand;
        private ICommand? _deleteRowCommand;
        private ICommand? _confirmDeleteCommand;
        private ICommand? _cancelDeleteCommand;
        private ICommand? _duplicateRowCommand;
        private ICommand? _moveUpCommand;
        private ICommand? _moveDownCommand;
        private ICommand? _dismissMigrationCommand;
        private ICommand? _goToErrorCommand;

        public ICommand AddRowCommand => _addRowCommand ??= new RelayCommand(_ => AddRow(), _ => CanAddRow);
        public ICommand DeleteRowCommand => _deleteRowCommand ??= new RelayCommand(p => { if (p is AssignmentRowVm vm) RequestDelete(vm); });
        public ICommand ConfirmDeleteCommand => _confirmDeleteCommand ??= new RelayCommand(_ => ConfirmDelete());
        public ICommand CancelDeleteCommand => _cancelDeleteCommand ??= new RelayCommand(_ => PendingDelete = null);
        public ICommand DuplicateRowCommand => _duplicateRowCommand ??= new RelayCommand(p => { if (p is AssignmentRowVm vm) DuplicateRow(vm); }, _ => CanAddRow);
        public ICommand MoveUpCommand => _moveUpCommand ??= new RelayCommand(p => { if (p is AssignmentRowVm vm) MoveRow(vm, -1); }, p => p is AssignmentRowVm vm && CanMove(vm, -1));
        public ICommand MoveDownCommand => _moveDownCommand ??= new RelayCommand(p => { if (p is AssignmentRowVm vm) MoveRow(vm, 1); }, p => p is AssignmentRowVm vm && CanMove(vm, 1));
        public ICommand DismissMigrationCommand => _dismissMigrationCommand ??= new RelayCommand(_ => ShowMigrationBanner = false);
        public ICommand GoToErrorCommand => _goToErrorCommand ??= new RelayCommand(_ =>
        {
            var first = RowVms.FirstOrDefault(v => string.Equals(v.HintLevel, "Error", StringComparison.Ordinal));
            if (first != null) SelectedRow = first;
        }, _ => RowVms.Any(v => string.Equals(v.HintLevel, "Error", StringComparison.Ordinal)));

        #endregion

        #region 旧版候选与提示（兼容旧视图/断言；新视图按行生成）

        private PluginConfigContext? _configContext;
        private bool _configEventsHooked;
        private string _legacyNameLink = string.Empty;

        public ObservableCollection<string> NameCandidates { get; } = new();
        public ObservableCollection<NameCandidateItem> NameCandidateItems { get; } = new();
        public sealed record NameCandidateItem(string Name, string TypeName, string Note);

        private string _nameHint = "「变量名」填要写入的变量名，也可点右侧箭头从列表里选。";
        public string NameHint
        {
            get => _nameHint;
            private set => SetProperty(ref _nameHint, value);
        }

        public void SetConfigContext(PluginConfigContext context)
        {
            _configContext = context;
            RefreshNameCandidates();
            RefreshNameHint();
            RefreshAllRows();
        }

        public override void Initialize(IStepConfigData stepData)
        {
            base.Initialize(stepData); // 内部会调 ApplyConfigValues（迁移/建端口）
            _legacyNameLink = stepData?.GetLinkedAddress(VariableName.Name) ?? string.Empty;

            if (!_configEventsHooked)
            {
                _configEventsHooked = true;
                Scope.ValueChanged += (_, __) =>
                {
                    RefreshNameCandidates();
                    RefreshNameHint();
                };
            }

            RefreshNameCandidates();
            RefreshNameHint();
            RefreshAllRows();
        }

        /// <summary>旧视图的「用户动了名字」上报（保留给旧断言；新视图用 NotifyRowNameEditedByUser）。</summary>
        public void NotifyNameEditedByUser()
        {
            if (StepData != null && StepData.IsLinked(VariableName.Name))
            {
                StepData.RemoveLink(VariableName.Name);
                _legacyNameLink = string.Empty;
            }

            RefreshNameHint();
        }

        /// <summary>
        /// 新视图：用户在某一行真实输入/选择了变量名。
        /// 只解除迁移行残留的旧 Name 连线；程序灌值不会走到这里（R35）。
        /// </summary>
        public void NotifyRowNameEditedByUser(string rowId)
        {
            var row = Rows.FirstOrDefault(r => string.Equals(r.RowId, rowId, StringComparison.Ordinal));
            if (row == null) return;

            if ((row.UsesLegacyValuePort || !string.IsNullOrEmpty(row.LegacyNameLinkAddress))
                && StepData != null && StepData.IsLinked(VariableName.Name))
            {
                StepData.RemoveLink(VariableName.Name);
                _legacyNameLink = string.Empty;
            }

            row.LegacyNameLinkAddress = string.Empty;
            var vm = RowVms.FirstOrDefault(v => ReferenceEquals(v.Row, row));
            vm?.RefreshHint();
            RefreshStatus();
            RefreshNameHint();
        }

        private void RefreshNameCandidates()
        {
            var scope = CurrentScope;

            // 重算前先记住当前名字，重算后顶回去（可编辑 ComboBox 清空 ItemsSource 会推空串）。
            var keep = VariableName.GetTypedValue();

            NameCandidates.Clear();
            NameCandidateItems.Clear();
            if (_configContext?.Variables != null)
            {
                foreach (var option in _configContext.Variables)
                {
                    if (option == null || string.IsNullOrWhiteSpace(option.Name))
                        continue;
                    if (!string.Equals(option.Scope, scope, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (NameCandidates.Contains(option.Name))
                        continue;

                    NameCandidates.Add(option.Name);
                    NameCandidateItems.Add(new NameCandidateItem(
                        option.Name,
                        option.TypeName ?? string.Empty,
                        option.Description ?? string.Empty));
                }
            }

            if (!string.IsNullOrEmpty(keep)
                && !string.Equals(VariableName.GetTypedValue(), keep, StringComparison.Ordinal))
            {
                VariableName.Value = keep;
            }
        }

        private void RefreshNameHint()
        {
            var scope = CurrentScope;
            bool isGlobal = string.Equals(scope, ScopeGlobal, StringComparison.OrdinalIgnoreCase);
            var name = VariableName.GetTypedValue();

            if (scope.Length == 0)
            {
                var rawScope = Scope.GetTypedValue();
                NameHint = string.IsNullOrWhiteSpace(rawScope)
                    ? "还没选择作用域：请选择「运行时变量」或「全局变量」。"
                    : $"作用域取值无法识别：「{rawScope}」（应为 {ScopeRuntime} 或 {ScopeGlobal}）；运行期会明确报错。";
                return;
            }

            var match = FindOption(name, scope);
            var otherScopeMatch = match == null ? FindOption(name, isGlobal ? ScopeRuntime : ScopeGlobal) : null;

            var text = !string.IsNullOrEmpty(_legacyNameLink)
                ? $"该行原由上游提供（{_legacyNameLink}）—— 在本行选/输一个变量名，即可改为手填。"
                : string.Empty;

            if (string.IsNullOrWhiteSpace(name))
            {
                NameHint = text + (isGlobal
                    ? "还没填名字。作用域=全局变量时，名字要来自变量管理（点右侧箭头选）。"
                    : "还没填名字。作用域=运行时变量时，名字通常来自上游「变量定义」节点。");
                return;
            }

            if (match != null)
            {
                NameHint = text + (isGlobal
                    ? $"写入全局变量「{match.Name}」（{match.TypeName}）——「值」按它的类型解析（如 Int32 填 21）。"
                    : $"写入运行时变量「{match.Name}」（{match.TypeName}）· {match.Description}。");
                return;
            }

            if (otherScopeMatch != null)
            {
                NameHint = text + (isGlobal
                    ? $"「{name}」是运行时变量（{otherScopeMatch.TypeName}），不在变量管理里 —— 要写它请把作用域改成「运行时变量」。"
                    : $"「{name}」是全局变量（{otherScopeMatch.TypeName}），不是上游声明的运行时变量 —— 要写它请把作用域改成「全局变量」。");
                return;
            }

            NameHint = text + (isGlobal
                ? $"变量管理里没有「{name}」：运行期会明确报「找不到全局变量」，不会静默新建。"
                : $"「{name}」不在上游的变量定义里：需要它运行时已存在，或勾上「不存在时自动创建」。");
        }

        private VariableOption? FindOption(string name, string scope)
            => _configContext?.Variables?.FirstOrDefault(o =>
                o != null
                && string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(o.Scope, scope, StringComparison.OrdinalIgnoreCase));

        private string CurrentScope
        {
            get
            {
                var scope = Scope.GetTypedValue();
                if (string.Equals(scope, ScopeGlobal, StringComparison.OrdinalIgnoreCase))
                    return ScopeGlobal;
                if (string.Equals(scope, ScopeRuntime, StringComparison.OrdinalIgnoreCase))
                    return ScopeRuntime;
                return string.Empty;
            }
        }

        #endregion

        #region 按行候选 / 提示 / 状态

        public VariableOption? FindVariable(string? name, string? scope)
        {
            var n = (name ?? string.Empty).Trim();
            if (n.Length == 0) return null;
            return _configContext?.Variables?.FirstOrDefault(o =>
                o != null
                && string.Equals(o.Name, n, StringComparison.OrdinalIgnoreCase)
                && string.Equals(o.Scope, scope ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 按行解析目标变量：**Id 优先、名字兜底**（与运行期同一口径）。
        ///
        /// 界面上的类型/提示/失效徽标都走这里：多设备同名点位之后，只按名字查会把
        /// "绑定的是 L2 那个"显示成 L1 的类型；变量改名后按名字也查不到，会误报"已失效"。
        /// </summary>
        public VariableOption? FindVariable(AssignmentRow row)
        {
            if (row == null) return null;

            var options = _configContext?.Variables;
            var scope = string.Equals(row.Scope, ScopeGlobal, StringComparison.OrdinalIgnoreCase)
                ? ScopeGlobal
                : ScopeRuntime;

            if (row.VariableId != Guid.Empty && options != null)
            {
                var byId = options.FirstOrDefault(o =>
                    o != null
                    && o.VariableId == row.VariableId
                    && string.Equals(o.Scope, scope, StringComparison.OrdinalIgnoreCase));
                if (byId != null) return byId;
            }

            return FindVariable(row.VariableName, row.Scope);
        }

        public List<NameCandidate> BuildCandidates(AssignmentRow row)
        {
            var result = new List<NameCandidate>();
            var options = _configContext?.Variables;
            if (row == null || options == null) return result;

            var scope = string.Equals(row.Scope, ScopeGlobal, StringComparison.OrdinalIgnoreCase) ? ScopeGlobal : ScopeRuntime;
            var currentName = (row.VariableName ?? string.Empty).Trim();
            var currentFound = false;

            // 去重键：Id 优先（有身份的候选天然唯一）；没有 Id 的（运行时变量）按名字 + 设备。
            // 只按名字去重会让多设备的同名点位**在候选里消失**——用户以为没建，其实是被吞了。
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var option in options)
            {
                if (option == null || string.IsNullOrWhiteSpace(option.Name)) continue;
                if (!string.Equals(option.Scope, scope, StringComparison.OrdinalIgnoreCase)) continue;

                if (!currentFound
                    && (row.VariableId != Guid.Empty && option.VariableId == row.VariableId
                        || string.Equals(option.Name, currentName, StringComparison.OrdinalIgnoreCase)))
                {
                    currentFound = true;
                }

                var key = option.VariableId != Guid.Empty
                    ? "id:" + option.VariableId.ToString("N")
                    : "name:" + option.Name + "\n" + (option.Device ?? string.Empty);
                if (!seen.Add(key)) continue;

                result.Add(new NameCandidate(
                    option.Name,
                    option.TypeName ?? string.Empty,
                    option.Description ?? string.Empty,
                    isInvalid: false,
                    variableId: option.VariableId,
                    device: option.Device ?? string.Empty));
            }

            if (string.Equals(scope, ScopeGlobal, StringComparison.OrdinalIgnoreCase)
                && currentName.Length > 0
                && !currentFound)
            {
                result.Insert(0, new NameCandidate(
                    currentName, "已失效", "变量管理里没有这个名字", true, row.VariableId));
            }

            return result;
        }

        public (string Text, string Level) BuildHint(AssignmentRow row)
        {
            var name = (row?.VariableName ?? string.Empty).Trim();
            bool isGlobal = string.Equals(row?.Scope, ScopeGlobal, StringComparison.OrdinalIgnoreCase);
            var prefix = string.IsNullOrWhiteSpace(row?.LegacyNameLinkAddress)
                ? string.Empty
                : $"该行原由上游提供（{row.LegacyNameLinkAddress}）—— 在本行选/输一个变量名，即可改为手填。";

            if (name.Length == 0)
            {
                return (prefix + (isGlobal
                    ? "还没填名字。作用域=全局变量时，名字要来自变量管理（点右侧箭头选）。"
                    : "还没填名字。作用域=运行时变量时，名字通常来自上游「变量定义」节点。"), "Error");
            }

            var current = FindVariable(row!);
            if (current != null)
            {
                // 多设备同名点位：提示里必须带设备，否则用户看到"写入全局变量「A」"也无从判断是哪一台
                var typeText = string.IsNullOrWhiteSpace(current.Device)
                    ? current.TypeName
                    : $"{current.TypeName} · 设备 {current.Device}";

                return (prefix + (isGlobal
                    ? $"写入全局变量「{current.Name}」（{typeText}）——「值」按它的类型解析（如 Int32 填 21）。"
                    : $"写入运行时变量「{current.Name}」（{typeText}）· {current.Description}。"), "Info");
            }

            var other = FindVariable(name, isGlobal ? ScopeRuntime : ScopeGlobal);
            if (other != null)
            {
                return (prefix + (isGlobal
                    ? $"「{name}」是运行时变量（{other.TypeName}），不在变量管理里 —— 全局作用域写不了它；请改选全局变量，或把作用域改成「运行时变量」。"
                    : $"「{name}」是全局变量（{other.TypeName}），不是上游声明的运行时变量 —— 要写它请把作用域改成「全局变量」。"),
                    isGlobal ? "Error" : "Warn");
            }

            if (isGlobal)
            {
                return (prefix + $"变量管理里没有「{name}」：运行期会明确报「找不到全局变量」，不会静默新建。", "Error");
            }

            if (row.CreateIfNotExists)
            {
                return (prefix + $"「{name}」不在上游的变量定义里：已勾选「自动创建」，运行期会新建它（类型由值决定）；名字由你负责，拼错会新建另一个变量。", "Info");
            }

            return (prefix + $"「{name}」不在上游的变量定义里：运行期会报「本地变量 {name} 不存在」；请在上游「变量定义」声明，或勾选「自动创建」。", "Warn");
        }

        public string GetTypeText(AssignmentRow row)
        {
            var current = FindVariable(row);
            if (current != null) return current.TypeName ?? string.Empty;
            if (string.Equals(row?.Scope, ScopeGlobal, StringComparison.OrdinalIgnoreCase)) return "已失效";
            return string.IsNullOrWhiteSpace(row?.VariableName) ? "类型未知" : "手填";
        }

        public string GetValueSourceText(AssignmentRow row)
        {
            var port = row?.ValuePortName;
            if (!string.IsNullOrWhiteSpace(port) && StepData != null && StepData.IsLinked(port))
                return "值=上游 · " + (StepData.GetLinkedAddress(port) ?? port);
            return "值=常量";
        }

        public string GetRowIndexText(AssignmentRowVm vm)
        {
            var index = RowVms.IndexOf(vm);
            return index < 0 ? string.Empty : $"第 {index + 1} 行 / 共 {RowVms.Count} 行";
        }

        public bool CanMove(AssignmentRowVm vm, int delta)
        {
            if (vm?.Row == null) return false;
            var index = Rows.IndexOf(vm.Row);
            var target = index + delta;
            return index >= 0 && target >= 0 && target < Rows.Count;
        }

        internal void OnRowPropertyChanged(AssignmentRowVm vm, PropertyChangedEventArgs e)
        {
            if (vm == null) return;

            if (string.Equals(e.PropertyName, nameof(AssignmentRow.Scope), StringComparison.Ordinal))
            {
                if (vm.IsGlobal) vm.Row.CreateIfNotExists = false;
                vm.RefreshCandidates();
            }
            else if (string.Equals(e.PropertyName, nameof(AssignmentRow.VariableName), StringComparison.Ordinal))
            {
                if (vm.IsGlobal && !vm.IsRefreshingCandidates) vm.RefreshCandidates();
            }
            else if (string.Equals(e.PropertyName, nameof(AssignmentRow.ValuePortName), StringComparison.Ordinal))
            {
                RebuildDynamicInputs();
            }

            vm.RefreshHint();
            RefreshStatus();
        }

        private void RefreshAllRows()
        {
            foreach (var vm in RowVms)
            {
                vm.RefreshCandidates();
                vm.RefreshAll();
            }
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            var errors = RowVms.Count(v => string.Equals(v.HintLevel, "Error", StringComparison.Ordinal));
            var warns = RowVms.Count(v => string.Equals(v.HintLevel, "Warn", StringComparison.Ordinal));

            ErrorCount = errors;
            WarnCount = warns;
            StatusLevel = errors > 0 ? "Error" : warns > 0 ? "Warn" : "None";
            StatusText = RowVms.Count == 0
                ? "0 行 · 未配置"
                : errors > 0
                    ? $"{RowVms.Count} 行 · {errors} 行有问题"
                    : warns > 0
                        ? $"{RowVms.Count} 行 · {warns} 行需确认"
                        : $"{RowVms.Count} 行 · 待确认";
            RowCountText = $"{RowVms.Count} 行";
            LimitText = Rows.Count >= MaxRows
                ? "已满 32 行"
                : $"上限 32 行 · 当前 {Rows.Count} 行 · 行顺序即执行顺序";
            ErrorSummary = $"有 {errors} 行配置有问题";

            OnPropertyChanged(nameof(HasRows));
            OnPropertyChanged(nameof(HasErrors));
            OnPropertyChanged(nameof(HasWarnings));
            OnPropertyChanged(nameof(CanAddRow));
        }

        #endregion

        #region 行操作

        private void AddRow()
        {
            if (Rows.Count >= MaxRows) return;
            var row = new AssignmentRow { Scope = ScopeRuntime };
            EnsureRowShape(row);
            Rows.Add(row);
            var vm = RowVms.FirstOrDefault(v => ReferenceEquals(v.Row, row));
            if (vm != null) SelectedRow = vm;
        }

        private void RequestDelete(AssignmentRowVm vm)
        {
            if (vm == null) return;
            if (StepData != null && StepData.IsLinked(vm.Row.ValuePortName))
            {
                PendingDelete = vm;
                return;
            }
            RemoveRow(vm);
        }

        private void ConfirmDelete()
        {
            var vm = PendingDelete;
            PendingDelete = null;
            if (vm != null) RemoveRow(vm);
        }

        private void RemoveRow(AssignmentRowVm vm)
        {
            var index = RowVms.IndexOf(vm);
            Rows.Remove(vm.Row);
            PendingDelete = null;
            if (RowVms.Count == 0) SelectedRow = null;
            else SelectedRow = RowVms[Math.Min(Math.Max(index, 0), RowVms.Count - 1)];
        }

        private void DuplicateRow(AssignmentRowVm vm)
        {
            if (vm == null || Rows.Count >= MaxRows) return;

            var source = vm.Row;
            var oldPort = source.ValuePortName;
            var link = StepData?.GetLink(oldPort);
            var value = GetRowValue(source);
            var index = Rows.IndexOf(source);

            var copy = source.CloneWithNewIdentity();
            EnsureRowShape(copy);
            Rows.Insert(index + 1, copy);

            var copyVm = RowVms.FirstOrDefault(v => ReferenceEquals(v.Row, copy));
            if (copyVm == null) return;

            if (link != null && StepData != null && !string.IsNullOrWhiteSpace(copy.ValuePortName))
                StepData.SetLink(copy.ValuePortName, link);
            else if (value != null && StepData != null && !string.IsNullOrWhiteSpace(copy.ValuePortName))
                StepData.SetInputValue(copy.ValuePortName, value);

            copyVm.RefreshAll();
            SelectedRow = copyVm;
        }

        private void MoveRow(AssignmentRowVm vm, int delta)
        {
            if (vm?.Row == null) return;
            var row = vm.Row;
            var index = Rows.IndexOf(row);
            var target = index + delta;
            if (index < 0 || target < 0 || target >= Rows.Count) return;
            Rows.Move(index, target);
            var moved = RowVms.FirstOrDefault(v => ReferenceEquals(v.Row, row));
            if (moved != null) SelectedRow = moved;
        }

        #endregion

        #region 生命周期 / 端口 / 迁移

        public override void ApplyConfigValues(IStepConfigData stepData)
        {
            _ = Inputs; // 触发端口名发现（FlowCompiler 直接调用本方法时不走 Initialize）
            _loading = true;
            try
            {
                base.ApplyConfigValues(stepData);
                MigrateLegacy(stepData);
                RebuildRowVms();
                RebuildDynamicInputs();
                RefillRowConstants(stepData);
            }
            finally
            {
                _loading = false;
            }

            RefreshAllRows();
        }

        public override void OnConfirm(IStepConfigData stepData)
        {
            CaptureVariableIds();
            RebuildDynamicInputs();
            base.OnConfirm(stepData); // 写端口 + Rows JSON 快照
            DropOrphanLinks(stepData);
            StripNonConstantValues(stepData);
            RemoveLegacyNameLink(stepData);
            RefreshAllRows();
        }

        /// <summary>
        /// 把「全局变量」行的目标 Id 落进配置（Id 优先、名字兜底）。
        ///
        /// 为什么要捕获：多设备允许同名点位之后（同一设备内唯一），名字不再能唯一定位一个变量；
        /// 运行期只有拿到 Id 才能在变量改名后仍然打到同一个变量上。
        /// 规则（与运行期解析同序）：
        /// - 已有 Id 且能在候选里找到 → 保留（变量改名后行里的旧名字不动，Id 仍然有效）；
        /// - Id 找不到（变量被删）或为空 → 按名字在候选里**唯一**命中时补上；
        ///   重名时留空，运行期会明确报歧义，而不是替用户选一台设备。
        /// </summary>
        private void CaptureVariableIds()
        {
            var options = _configContext?.Variables;
            if (options == null) return;

            foreach (var row in Rows)
            {
                if (row == null) continue;

                if (!string.Equals(row.Scope, ScopeGlobal, StringComparison.OrdinalIgnoreCase))
                {
                    // 运行时变量每轮由「变量定义」重新创建，没有稳定身份
                    row.VariableId = Guid.Empty;
                    continue;
                }

                row.VariableId = VariableOptionResolver.CaptureId(
                    options, row.VariableId, row.VariableName, ScopeGlobal);
            }
        }

        public override void OnCancel()
        {
        }

        public override void Dispose()
        {
            foreach (var vm in RowVms) vm.Detach();
            RowVms.Clear();
            UnhookRows(_rows);
            DisposeDynamicInputs();
            base.Dispose();
        }

        private void MigrateLegacy(IStepConfigData? stepData)
        {
            if (Rows.Count > 0) return;

            var name = VariableName.GetTypedValue();
            var scope = Scope.GetTypedValue();
            var value = Value.GetTypedValue();
            var create = CreateIfNotExists.GetTypedValue();
            var hasLegacy = !string.IsNullOrWhiteSpace(name)
                || value != null
                || (stepData?.InputValues?.ContainsKey(Value.Name) ?? false)
                || (stepData?.IsLinked(Value.Name) ?? false)
                || (stepData?.IsLinked(VariableName.Name) ?? false);
            if (!hasLegacy) return;

            var row = new AssignmentRow
            {
                VariableName = name ?? string.Empty,
                Scope = string.Equals(scope, ScopeGlobal, StringComparison.OrdinalIgnoreCase) ? ScopeGlobal : ScopeRuntime,
                CreateIfNotExists = create,
                ValuePortName = Value.Name,
                UsesLegacyValuePort = true,
                LegacyNameLinkAddress = stepData?.GetLinkedAddress(VariableName.Name) ?? string.Empty,
            };
            Rows.Add(row);
            ShowMigrationBanner = true;
            MigrationText = "已把原来的单条赋值转成第 1 行；原「值」的连线已保留；原「名字」的连线将在确认时解除。";
            RefreshStatus();
        }

        private void EnsureRowShape(AssignmentRow row)
        {
            if (row == null) return;
            if (string.IsNullOrWhiteSpace(row.RowId)) row.RowId = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(row.ValuePortName)) row.ValuePortName = "Value_" + row.RowId;
            row.UsesLegacyValuePort = string.Equals(row.ValuePortName, Value.Name, StringComparison.Ordinal);

            if (!string.Equals(row.Scope, ScopeGlobal, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(row.Scope, ScopeRuntime, StringComparison.OrdinalIgnoreCase))
                row.Scope = ScopeRuntime;

            if (string.Equals(row.Scope, ScopeGlobal, StringComparison.OrdinalIgnoreCase))
                row.CreateIfNotExists = false;
        }

        private readonly List<string> _dynamicInputNames = new();

        private void RebuildDynamicInputs()
        {
            var preserved = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var name in _dynamicInputNames)
            {
                if (Inputs.TryGetValue(name, out var oldPort))
                    preserved[name] = oldPort.GetActualValue();
            }

            DisposeDynamicInputs();

            foreach (var row in Rows)
            {
                if (row == null) continue;
                EnsureRowShape(row);

                if (row.UsesLegacyValuePort)
                {
                    SetRowPort(row, Inputs.TryGetValue(Value.Name, out var legacyPort) ? legacyPort : null);
                    continue;
                }

                var name = row.ValuePortName;
                if (string.IsNullOrWhiteSpace(name)) continue;

                if (!Inputs.TryGetValue(name, out var port))
                {
                    var newPort = new InputPort<object>(name, null, $"第 {Rows.IndexOf(row) + 1} 行：{row.VariableName} 的值")
                    {
                        IsRequired = false
                    };
                    AddDynamicInput(newPort);
                    _dynamicInputNames.Add(name);
                    port = newPort;
                }

                if (preserved.TryGetValue(name, out var pv) && pv != null)
                {
                    try { port.Value = pv; } catch { /* 类型不匹配保留默认值 */ }
                }
                else if (StepData?.InputValues != null && StepData.InputValues.TryGetValue(name, out var iv))
                {
                    try { port.Value = iv; } catch { /* 旧数据类型不匹配保留默认值 */ }
                }

                SetRowPort(row, port);
            }
        }

        private void DisposeDynamicInputs()
        {
            foreach (var name in _dynamicInputNames)
            {
                if (Inputs.TryGetValue(name, out var oldPort) && oldPort is IDisposable disposable)
                {
                    try { disposable.Dispose(); } catch { /* 退订失败不挡重建 */ }
                }
                RemoveDynamicInput(name);
            }
            _dynamicInputNames.Clear();
        }

        private void SetRowPort(AssignmentRow row, IInputPort? port)
        {
            var vm = RowVms.FirstOrDefault(v => ReferenceEquals(v.Row, row));
            vm?.SetPort(port);
        }

        private void RefillRowConstants(IStepConfigData? stepData)
        {
            if (stepData?.InputValues == null) return;
            foreach (var row in Rows)
            {
                if (row == null || row.UsesLegacyValuePort) continue;
                var name = row.ValuePortName;
                if (string.IsNullOrWhiteSpace(name) || stepData.IsLinked(name)) continue;
                if (!stepData.InputValues.TryGetValue(name, out var value)) continue;
                if (!Inputs.TryGetValue(name, out var port)) continue;
                try { port.Value = value; } catch { /* 旧数据类型不匹配保留默认值 */ }
            }
        }

        private void DropOrphanLinks(IStepConfigData? stepData)
        {
            if (stepData == null) return;
            var keep = new HashSet<string>(
                Rows.Where(r => r != null && !string.IsNullOrWhiteSpace(r.ValuePortName)).Select(r => r.ValuePortName),
                StringComparer.Ordinal);

            foreach (var name in GetLinkedPortNames(stepData))
            {
                if (!name.StartsWith("Value_", StringComparison.Ordinal)) continue;
                if (keep.Contains(name)) continue;
                stepData.RemoveLink(name);
                stepData.RemoveInputValue(name);
            }
        }

        /// <summary>
        /// IStepConfigData 没有暴露「全部连线键」；具体实现 StepModel 有公开的 GetLinkedPortNames()。
        /// 这里只读反射调用那个公开方法（不碰私有状态），拿不到就跳过清理，由编译器报孤儿键。
        /// </summary>
        private static IEnumerable<string> GetLinkedPortNames(IStepConfigData stepData)
        {
            if (stepData == null) yield break;

            System.Reflection.MethodInfo? method = null;
            try { method = stepData.GetType().GetMethod("GetLinkedPortNames", Type.EmptyTypes); }
            catch { /* 拿不到就跳过 */ }

            object? result = null;
            if (method != null)
            {
                try { result = method.Invoke(stepData, null); }
                catch { /* 反射失败不挡保存 */ }
            }

            if (result is IEnumerable<string> names)
            {
                foreach (var name in names)
                    yield return name;
            }
        }

        private static bool IsConstantable(object? value)
            => value == null || value is string || value is bool || value is Enum
               || value is double || value is float || value is decimal
               || value is int || value is long || value is short || value is byte
               || value is uint || value is ulong || value is ushort || value is sbyte;

        private void StripNonConstantValues(IStepConfigData? stepData)
        {
            if (stepData?.InputValues == null) return;
            foreach (var port in Inputs.Values)
            {
                if (!stepData.InputValues.TryGetValue(port.Name, out var value)) continue;
                if (IsConstantable(value)) continue;
                stepData.RemoveInputValue(port.Name);
            }
        }

        private void RemoveLegacyNameLink(IStepConfigData? stepData)
        {
            if (stepData == null || Rows.Count == 0) return;
            if (stepData.IsLinked(VariableName.Name)) stepData.RemoveLink(VariableName.Name);
        }

        private void RebuildRowVms()
        {
            var selectedId = SelectedRow?.Row.RowId;

            foreach (var vm in RowVms) vm.Detach();
            RowVms.Clear();

            foreach (var row in Rows)
            {
                if (row == null) continue;
                EnsureRowShape(row);
                var vm = new AssignmentRowVm(this, row);
                vm.RefreshCandidates();
                RowVms.Add(vm);
            }

            foreach (var vm in RowVms) vm.RefreshAll();

            SelectedRow = RowVms.FirstOrDefault(v => string.Equals(v.Row.RowId, selectedId, StringComparison.Ordinal))
                ?? RowVms.FirstOrDefault();
            RebuildDynamicInputs();
            RefreshStatus();
        }

        private void HookRows(ObservableCollection<AssignmentRow> rows)
        {
            if (rows == null) return;
            rows.CollectionChanged += OnRowsCollectionChanged;
        }

        private void UnhookRows(ObservableCollection<AssignmentRow> rows)
        {
            if (rows == null) return;
            rows.CollectionChanged -= OnRowsCollectionChanged;
        }

        private void OnRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (_loading) return;
            RebuildRowVms();
        }

        public VariableAssignmentPlugin()
        {
            HookRows(_rows);
        }

        #endregion

        #region 执行

        public override void RunAlgorithm(IExecutionContext context)
        {
            var enabled = Rows.Where(r => r != null && r.Enabled).ToList();
            if (enabled.Count == 0)
            {
                RunLegacy(context);
                return;
            }

            RunRows(context, enabled);
        }

        private void RunRows(IExecutionContext context, List<AssignmentRow> rows)
        {
            var failures = new List<string>();
            var successCount = 0;

            foreach (var row in rows)
            {
                var index = Rows.IndexOf(row) + 1;
                var name = (row.VariableName ?? string.Empty).Trim();
                var scope = row.Scope;
                var create = row.CreateIfNotExists;
                var value = GetRowValue(row);
                var existed = context.LocalVariables.ContainsKey(name);
                var label = string.IsNullOrWhiteSpace(name) ? $"第{index}行" : $"第{index}行「{name}」";

                try
                {
                    if (TryAssign(context, name, row.VariableId, scope, create, value, out var error))
                    {
                        successCount++;
                        context.Logger.Info(string.Equals(scope, ScopeGlobal, StringComparison.OrdinalIgnoreCase)
                            ? $"{InstanceName} 第 {index} 行：全局变量 {name} 已赋值"
                            : existed
                                ? $"{InstanceName} 第 {index} 行：本地变量 {name} 已赋值"
                                : $"{InstanceName} 第 {index} 行：本地变量 {name} 已创建并赋值");
                    }
                    else
                    {
                        failures.Add($"{label}{error}");
                        context.Logger.Error($"{InstanceName} 第 {index} 行「{name}」赋值失败：{error}");
                    }
                }
                catch (Exception ex)
                {
                    failures.Add($"{label}{ex.Message}");
                    context.Logger.Error($"{InstanceName} 第 {index} 行「{name}」赋值异常：{ex.Message}");
                }
            }

            if (failures.Count == 0)
            {
                Success.Value = true;
                return;
            }

            Success.Value = false;
            var summary = string.Join("；", failures.Take(3));
            if (failures.Count > 3) summary += $"；等 {failures.Count} 行";
            ErrorMessage.Value = $"{failures.Count}/{rows.Count} 行失败：{summary}";
            context.Logger.Error($"{InstanceName} {ErrorMessage.Value}");
        }

        private void RunLegacy(IExecutionContext context)
        {
            var varName = VariableName.GetTypedValue();
            var value = Value.GetTypedValue();
            var create = CreateIfNotExists.GetTypedValue();
            var scope = Scope.GetTypedValue();

            try
            {
                var existed = context.LocalVariables.ContainsKey(varName);
                if (!TryAssign(context, varName, Guid.Empty, scope, create, value, out var error))
                {
                    Success.Value = false;
                    ErrorMessage.Value = error;
                    context.Logger.Error($"{InstanceName} {error}");
                    return;
                }

                Success.Value = true;
                context.Logger.Info(string.Equals(scope, ScopeGlobal, StringComparison.OrdinalIgnoreCase)
                    ? $"{InstanceName} 全局变量 {varName} 已赋值"
                    : existed
                        ? $"{InstanceName} 本地变量 {varName} 已赋值"
                        : $"{InstanceName} 本地变量 {varName} 已创建并赋值");
            }
            catch (Exception ex)
            {
                Success.Value = false;
                ErrorMessage.Value = ex.Message;
                context.Logger.Error($"{InstanceName} 变量赋值失败: {ex.Message}");
            }
        }

        private object? GetRowValue(AssignmentRow row)
        {
            if (row == null) return null;
            if (row.UsesLegacyValuePort || string.Equals(row.ValuePortName, Value.Name, StringComparison.Ordinal))
                return Value.GetTypedValue();

            if (!string.IsNullOrWhiteSpace(row.ValuePortName)
                && Inputs.TryGetValue(row.ValuePortName, out var port))
                return port.GetActualValue();

            return null;
        }

        private bool TryAssign(
            IExecutionContext context,
            string varName,
            Guid variableId,
            string scope,
            bool create,
            object? value,
            out string error)
        {
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(varName) && variableId == Guid.Empty)
            {
                error = "变量名称不能为空";
                return false;
            }

            bool isGlobal = string.Equals(scope, ScopeGlobal, StringComparison.OrdinalIgnoreCase);
            if (!isGlobal && !string.Equals(scope, ScopeRuntime, StringComparison.OrdinalIgnoreCase))
            {
                error = $"作用域取值无法识别：「{scope}」（应为 {ScopeRuntime} 或 {ScopeGlobal}）";
                return false;
            }

            if (isGlobal)
            {
                // Id 优先、名字兜底：多设备同名点位只有 Id 能唯一定位；
                // 旧配置（Id 为空）走名字，与改造前行为一致。
                var access = context.SharedVariables;
                if (access == null)
                {
                    error = "当前执行环境不支持写全局变量";
                    return false;
                }

                if (!access.TryWrite(variableId, varName, value, out var writeError))
                {
                    error = string.IsNullOrWhiteSpace(writeError)
                        ? $"写全局变量 {varName} 失败"
                        : writeError;
                    return false;
                }

                return true;
            }

            if (context.LocalVariables.ContainsKey(varName))
            {
                var existing = context.LocalVariables[varName];
                if (value != null && existing != null && value.GetType() != existing.GetType())
                {
                    try
                    {
                        value = Convert.ChangeType(value, existing.GetType());
                        context.Logger.Warn($"{InstanceName} 赋值 {varName}：值类型已按变量既有类型 {existing.GetType().Name} 自动转换");
                    }
                    catch (Exception ex)
                    {
                        error = $"变量 {varName} 类型为 {existing.GetType().Name}，值 [{value}] 无法转换：{ex.Message}";
                        return false;
                    }
                }

                context.LocalVariables[varName] = value!;
                return true;
            }

            if (create)
            {
                context.LocalVariables.Add(varName, value!);
                return true;
            }

            error = $"本地变量 {varName} 不存在";
            return false;
        }

        #endregion

        #region 视图

        public object GetConfigView(IStepConfigData stepData)
        {
            Initialize(stepData);
            return new VariableAssignmentView { DataContext = this };
        }

        public override void Initialize()
        {
        }

        #endregion
    }
}
