using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Scada;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 组态「报警配置」弹窗：把方案里的报警定义集中到一张<b>平铺表</b>上增删改。
    ///
    /// 它解决的是什么问题
    /// ---------
    /// 报警定义（<see cref="ScadaAlarmDefinition"/>）是<b>方案级</b>内容：跟着 .vms 文件走、
    /// 进撤销栈、运行态由 <c>ScadaAlarmEngine</c> 逐条求值。它没有"归属图元"，
    /// 所以属性面板（描述的是"当前选中的那个图元"）里没有落脚点——必须有一个弹窗专门管它。
    ///
    /// 为什么是"平铺表"而不是"左清单 + 右详情"
    /// ---------
    /// 报警的形状是"一行一条定义"，且字段多（种类 / 阈值 / 回差 / 延时 / 严重度 / 报警组 /
    /// 信息文本 / 原因 / 措施 / 附加信息）。左清单右详情会让"比着改一批报警"变成
    /// "点一条、改一处、再点下一条"，而现场最常见的动作恰恰是"把这一列的数值挨个调一遍"。
    /// 平铺表让同一列上下相邻，比较与批量修改都直接。
    ///
    /// 为什么次要字段收进"展开行"
    /// ---------
    /// 一张表列太多会横向溢出（本仓默认弹窗宽度 920），把七列主字段摆在外面、
    /// 七个次要字段收进 <c>DataGrid.RowDetails</c>，主次一眼分明；
    /// 需要细调某一条时再展开它，不用为了改一个"解决措施"把表横向拖来拖去。
    ///
    /// 写回口径
    /// ---------
    /// 与属性面板逐条一致：<b>即时写回 + 进撤销栈</b>。本弹窗没有"确定"这个提交动作，
    /// 每一次改单元格都当场落进模型并包一层作用域，所以底部的按钮只有「关闭」——
    /// 它关的是窗，不是"是否采纳"。
    ///
    /// 为什么增删走 <see cref="ScadaDocument.AddAlarm"/> / <c>TryRemoveAlarm</c>，
    /// 而改字段要自己包作用域
    /// ---------
    /// 领域层那三个方法（新建 / 删除 / 改名）<b>各自已经自带</b> <c>BeginEdit</c>，
    /// 弹窗外面包一层反而会把两次撤销并成一次；而"改阈值 / 改严重度 / 改报警组"
    /// 这些字段级写入没有自带作用域，必须由本视图模型显式包（否则改了撤销不回去）。
    ///
    /// 为什么数值列用<b>字符串</b>属性接输入框
    /// ---------
    /// 与 <see cref="ScadaVariableEventDialogViewModel"/> 同一个理由：直接把 <c>double</c> 绑到
    /// <c>TextBox.Text</c>，用户敲进"8O"（字母 O）时绑定会静默失效、属性保持旧值，
    /// 界面上看不出任何异常。这里收字符串、在失焦时统一解析，解析不了就<b>回灌原值并给一句中文原因</b>。
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

        private readonly IWorkspaceManager _workspace;
        private readonly IScadaVariablePicker _picker;
        private readonly ScadaAuditWriter? _audit;
        private readonly Func<string?>? _actorProvider;

        /// <summary>平铺表的全部行，一行 = 一条报警定义，顺序即 <see cref="ScadaDocument.Alarms"/> 的顺序</summary>
        private readonly ObservableCollection<ScadaAlarmConfigRow> _rows = new();

        private ScadaAlarmConfigRow? _selectedRow;
        private string? _errorMessage;

        /// <param name="workspace">只用来取"当前方案的组态文档"</param>
        /// <param name="picker">「选变量」入口：为当前报警选它要监视的工程变量</param>
        /// <param name="audit">
        /// 操作审计落盘端。默认 null = 不记（单机调试 / 断言里直接 new 的路径）。
        /// <b>可选参数必须由宿主显式工厂注册</b>：交给容器去猜的结果是编译能过、
        /// 运行起来"配了半天一条审计都没有"而且不报错（见 App.xaml.cs 的注册处）。
        /// </param>
        /// <param name="actorProvider">"此刻登录着谁"；取不到时 <see cref="ScadaAuditEntry"/> 自己回落"未登录"</param>
        public ScadaAlarmConfigDialogViewModel(
            IWorkspaceManager workspace,
            IScadaVariablePicker picker,
            ScadaAuditWriter? audit = null,
            Func<string?>? actorProvider = null)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _picker = picker ?? throw new ArgumentNullException(nameof(picker));
            _audit = audit;
            _actorProvider = actorProvider;

            AddCommand = new DelegateCommand(OnAdd);
            RemoveCommand = new DelegateCommand(OnRemove, () => _selectedRow != null);
            PickVariableCommand = new DelegateCommand(OnPickVariable, () => _selectedRow != null);
            CloseCommand = new DelegateCommand(() => RequestClose.Invoke(new DialogParameters(), ButtonResult.OK));
        }

        public DialogCloseListener RequestClose { get; set; }

        public string Title => "报警配置";

        /// <summary>平铺表的行集合，供 DataGrid 绑定</summary>
        public ObservableCollection<ScadaAlarmConfigRow> Rows => _rows;

        /// <summary>当前选中的行（工具条的「删除」「选择变量」作用在它身上）</summary>
        public ScadaAlarmConfigRow? SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (SetProperty(ref _selectedRow, value))
                {
                    RemoveCommand.RaiseCanExecuteChanged();
                    PickVariableCommand.RaiseCanExecuteChanged();
                    RaisePropertyChanged(nameof(HasSelection));
                }
            }
        }

        /// <summary>报警条数（标题栏右侧那行小字）</summary>
        public int AlarmCount => _rows.Count;

        /// <summary>条数提示文案</summary>
        public string CountHint => $"共 {AlarmCount} 条报警";

        /// <summary>表里有没有内容（空态提示靠它显隐）</summary>
        public bool HasRows => _rows.Count > 0;

        /// <summary>有没有选中行（工具条的删除 / 选变量按钮靠它置灰）</summary>
        public bool HasSelection => _selectedRow != null;

        /// <summary>校验 / 删除失败的原因（红字）。没有问题时为 <c>null</c></summary>
        public string? ErrorMessage
        {
            get => _errorMessage;
            private set
            {
                if (SetProperty(ref _errorMessage, value))
                    RaisePropertyChanged(nameof(HasError));
            }
        }

        /// <summary>只用来决定错误条显示与否（<c>string</c> 转可见性要额外一个转换器，不值当）</summary>
        public bool HasError => !string.IsNullOrEmpty(_errorMessage);

        /// <summary>
        /// 条件种类的全部候选（下拉数据源）。直接取枚举全量 + <c>DisplayName()</c> 出中文，
        /// 不手写一份平行清单——枚举一旦加成员，这里自动跟上，不会漏。
        /// </summary>
        public IReadOnlyList<ScadaAlarmKindOption> KindOptions { get; } =
            Enum.GetValues<ScadaAlarmKind>().Select(k => new ScadaAlarmKindOption(k)).ToArray();

        /// <summary>严重度的全部候选（下拉数据源）</summary>
        public IReadOnlyList<ScadaAlarmSeverityOption> SeverityOptions { get; } =
            Enum.GetValues<ScadaAlarmSeverity>().Select(s => new ScadaAlarmSeverityOption(s)).ToArray();

        /// <summary>
        /// 「报警组」的历史值下拉数据源：当前方案里已经用过的组名（去重 + 排序）。
        /// 报警组是自由文本（组名随现场工艺划分走，枚举一发布就不能改），
        /// 但"一号线""冷却水"这类组名在同一份方案里会反复出现——给历史值下拉，
        /// 既省掉重复录入，又不妨碍输入一个全新的组名。
        /// </summary>
        public ObservableCollection<string> AlarmGroupSuggestions { get; } = new();

        /// <summary>「新增」：建一条报警定义（名字自动给"报警_N"，种类默认高限）</summary>
        public DelegateCommand AddCommand { get; }

        /// <summary>「删除」：删掉选中那一条（允许删到一条不剩）</summary>
        public DelegateCommand RemoveCommand { get; }

        /// <summary>「选择变量」：为选中那条报警选它要监视的工程变量</summary>
        public DelegateCommand PickVariableCommand { get; }

        /// <summary>「关闭」：本弹窗没有提交动作（写回是即时的），所以它只关窗</summary>
        public DelegateCommand CloseCommand { get; }

        /// <summary>当前方案的组态文档；没有打开的方案时为 <c>null</c></summary>
        internal ScadaDocument? Document => _workspace.CurrentSolution?.Scada;

        /// <summary>把一句失败原因摆到错误条上（行内编辑失败时由行回调过来）</summary>
        internal void ReportError(string? message) => ErrorMessage = message;

        /// <summary>记一条"改了某条报警的某个字段"的审计（主体取那条报警的名字）</summary>
        internal void AuditField(ScadaAlarmDefinition alarm, string detail)
            => Audit(AuditActionField, true, detail, null, alarm.Name);

        /// <summary>记一条改名的审计（单独一个动作名，"改名"在现场排查时比"改字段"有用得多）</summary>
        internal void AuditRename(ScadaAlarmDefinition alarm, string detail)
            => Audit(AuditActionRename, true, detail, null, alarm.Name);

        /// <summary>重建「报警组」历史值下拉（改过组名之后调）</summary>
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

            AlarmGroupSuggestions.Clear();
            foreach (var name in names)
                AlarmGroupSuggestions.Add(name);
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

            // AddAlarm 内部已包 BeginEdit，这里不要再包一层（会把两次撤销并成一次）
            var alarm = document.AddAlarm();
            var row = new ScadaAlarmConfigRow(this, alarm);

            _rows.Add(row);
            RefreshAlarmGroups();
            SelectedRow = row;

            ReportError(null);
            RaiseCountChanged();
            Audit(AuditActionCreate, true, $"新建报警 [{alarm.Name}]", null, alarm.Name);
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

            // TryRemoveAlarm 内部已包 BeginEdit
            if (!document.TryRemoveAlarm(row.Model, out var error))
            {
                ReportError(error);
                Audit(AuditActionRemove, false, null, error, name);
                return;
            }

            Audit(AuditActionRemove, true, $"删除报警 [{name}]", null, name);

            row.Detach();
            _rows.Remove(row);
            RefreshAlarmGroups();

            // 删完之后选中落在剩下第一条（而不是塌回"什么都没选"）：用户刚动过的地方不该整个消失
            SelectedRow = _rows.FirstOrDefault();
            ReportError(null);
            RaiseCountChanged();
        }

        private void OnPickVariable()
        {
            var row = _selectedRow;
            if (row == null)
                return;

            _picker.Pick(row.Model.VariableId, row.VariableName, (id, name) =>
            {
                var document = Document;
                if (document == null)
                {
                    ReportError("当前没有打开的方案，无法绑定变量。");
                    return;
                }

                // Bind 是两次属性写（Id + 名字），必须包作用域，否则撤销只回退一半
                using (document.BeginEdit($"报警 [{row.Name}] 选择变量 [{name}]"))
                {
                    row.Model.Bind(id, name);
                }

                row.NotifyVariableChanged();
                ReportError(null);
                Audit(AuditActionBind, true, $"报警 [{row.Name}] 监视变量 → {name}", null, row.Name);
            });
        }

        /// <summary>重建整张表（打开弹窗 / 外部改动之后调）</summary>
        private void Reload(Guid keepAlarmId)
        {
            foreach (var row in _rows)
                row.Detach();

            _rows.Clear();

            if (Document is { } document)
            {
                foreach (var alarm in document.Alarms)
                    _rows.Add(new ScadaAlarmConfigRow(this, alarm));
            }

            RefreshAlarmGroups();

            SelectedRow = _rows.FirstOrDefault(r => r.AlarmId == keepAlarmId)
                          ?? _rows.FirstOrDefault();

            ErrorMessage = null;
            RaiseCountChanged();
        }

        private void RaiseCountChanged()
        {
            RaisePropertyChanged(nameof(AlarmCount));
            RaisePropertyChanged(nameof(CountHint));
            RaisePropertyChanged(nameof(HasRows));
        }

        private void Audit(string action, bool ok, string? detail, string? error, string? subject = null)
            => _audit?.Append(new ScadaAuditEntry(
                _actorProvider?.Invoke(),
                subject ?? _selectedRow?.Name ?? AuditSubjectFallback,
                AuditEventText,
                action,
                ok ? ScadaAuditOutcome.Success : ScadaAuditOutcome.Failed,
                ok ? detail : error));

        #region IDialogAware

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() { }

        /// <summary>
        /// 打开时现读整张报警表。
        /// 每次打开都重建，是因为两次打开之间用户可能已经改过方案（撤销、加载别的方案），
        /// 缓存在字段里跨弹窗复用，就会给出"表里写着它、模型里却没有"的幽灵条目。
        /// </summary>
        public void OnDialogOpened(IDialogParameters parameters) => Reload(Guid.Empty);

        #endregion
    }

    /// <summary>
    /// 平铺表里的一行：一条报警定义，<b>可就地编辑</b>。
    ///
    /// 为什么行要持有宿主视图模型：本行的每一次写入都要包一层
    /// <see cref="ScadaDocument.BeginEdit"/>（进撤销栈）、失败时要能把中文原因摆到弹窗的错误条上、
    /// 还要记一条审计——这三件事的原料（文档、错误条、审计端）都在宿主手上，
    /// 所以行是"视图模型的一个可写投影"，不是纯只读投影。
    ///
    /// 为什么订阅 <see cref="ScadaAlarmDefinition.PropertyChanged"/>：
    /// 行自己写模型之后，界面要跟着刷新（种类一改，"阈值"那一格该不该出现、
    /// "条件"摘要那句话都得变）。订阅之后由模型回推一次，比在每个 setter 里手写一串
    /// <c>RaisePropertyChanged</c> 更不容易漏——漏一处的表现是"界面显示的还是旧值"。
    /// </summary>
    public sealed class ScadaAlarmConfigRow : BindableBase
    {
        private readonly ScadaAlarmConfigDialogViewModel _host;
        private readonly ScadaAlarmDefinition _model;

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

        /// <summary>
        /// 「报警组」的历史值候选（本方案里已经用过的组名）。
        /// 直接转发宿主那个集合（不复制一份）：宿主改过组名之后会重建它，
        /// 复制一份的话下拉里就一直是打开弹窗那一刻的旧快照。
        /// </summary>
        public ObservableCollection<string> GroupSuggestions => _host.AlarmGroupSuggestions;

        /// <summary>
        /// 从历史值下拉里挑了一个组名：等同于把那个名字敲进输入框。
        /// 之所以要一个命令而不是让 MenuItem 直接写：MenuItem 无法把"点了哪一项"塞进目标属性，
        /// 只能走命令 + <c>CommandParameter</c>；写入口径仍然统一落在 <see cref="AlarmGroup"/> 的 setter 上。
        /// </summary>
        public DelegateCommand<string> ApplyGroupCommand { get; }

        /// <summary>报警名（可就地改名；查重与空名拒绝由文档负责）</summary>
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

                // TryRenameAlarm 内部已包 BeginEdit，并且带空名校验 + 查重（中文原因带回）
                if (!document.TryRenameAlarm(_model, target, out var error))
                {
                    _host.ReportError(error);
                    RaisePropertyChanged(); // 把界面回灌成原值
                    return;
                }

                _host.AuditRename(_model, $"报警改名：{before} → {target}");
                _host.ReportError(null);
                RaisePropertyChanged();
            }
        }

        /// <summary>条件种类（四限 / 布尔跳变 / 断线）</summary>
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

        /// <summary>报警等级（提示 / 警告 / 严重）</summary>
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
            set => CommitNumber(value, nameof(ThresholdText), "阈值", () => _model.Threshold, v => _model.Threshold = v);
        }

        /// <summary>回差（字符串接输入框；小于 0 会被领域层夹到 0）</summary>
        public string DeadbandText
        {
            get => Format(_model.Deadband);
            set => CommitNumber(value, nameof(DeadbandText), "回差", () => _model.Deadband, v => _model.Deadband = v);
        }

        /// <summary>延时（秒）（字符串接输入框；小于 0 会被领域层夹到 0）</summary>
        public string DelaySecondsText
        {
            get => Format(_model.DelaySeconds);
            set => CommitNumber(value, nameof(DelaySecondsText), "延时", () => _model.DelaySeconds, v => _model.DelaySeconds = v);
        }

        /// <summary>断线判定秒数（字符串接输入框；小于 1 会被领域层夹到 1）</summary>
        public string StaleSecondsText
        {
            get => Format(_model.StaleSeconds);
            set => CommitNumber(value, nameof(StaleSecondsText), "断线判定", () => _model.StaleSeconds, v => _model.StaleSeconds = v);
        }

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
            }
        }

        /// <summary>信息文本（留空时报警条与历史里回落显示报警名）</summary>
        public string Message
        {
            get => _model.Message ?? string.Empty;
            set => CommitText(value, nameof(Message), "信息文本", () => _model.Message, v => _model.Message = v);
        }

        /// <summary>故障原因</summary>
        public string Cause
        {
            get => _model.Cause ?? string.Empty;
            set => CommitText(value, nameof(Cause), "故障原因", () => _model.Cause, v => _model.Cause = v);
        }

        /// <summary>解决措施</summary>
        public string Remedy
        {
            get => _model.Remedy ?? string.Empty;
            set => CommitText(value, nameof(Remedy), "解决措施", () => _model.Remedy, v => _model.Remedy = v);
        }

        /// <summary>附加信息</summary>
        public string ExtraInfo
        {
            get => _model.ExtraInfo ?? string.Empty;
            set => CommitText(value, nameof(ExtraInfo), "附加信息", () => _model.ExtraInfo, v => _model.ExtraInfo = v);
        }

        /// <summary>监视变量的名字（交给选择器的"当前值"；没绑定时为 <c>null</c>）</summary>
        public string? VariableName => _model.VariableName;

        /// <summary>变量列的显示文本（没绑定时的兜底提示）</summary>
        public string VariableDisplay
            => string.IsNullOrWhiteSpace(_model.VariableName) ? "未选变量" : _model.VariableName!;

        /// <summary>有没有绑定监视变量（变量列的空值样式靠它切换）</summary>
        public bool HasVariable => !string.IsNullOrWhiteSpace(_model.VariableName);

        /// <summary>条件摘要（"高限 &gt; 80（回差 5）"这类人话）</summary>
        public string ConditionText => _model.ConditionText;

        /// <summary>种类的中文名（只读展示用）</summary>
        public string KindText => _model.Kind.DisplayName();

        /// <summary>严重度的中文名（只读展示用）</summary>
        public string SeverityText => _model.Severity.DisplayName();

        /// <summary>是不是"四限"之一（只有这几种才用得到阈值这一格）</summary>
        public bool IsLimitKind => _model.Kind.IsLimit();

        /// <summary>是不是布尔跳变类（展开行里的阈值相关字段对它们无意义）</summary>
        public bool IsBooleanKind => _model.Kind.IsBoolean();

        /// <summary>是不是断线类（只有它用得到"断线判定秒数"）</summary>
        public bool IsStaleKind => _model.Kind == ScadaAlarmKind.Stale;

        /// <summary>取消对模型的订阅（行被移除时必须调，否则模型活着、行也活着）</summary>
        internal void Detach() => _model.PropertyChanged -= OnModelPropertyChanged;

        /// <summary>选完变量后刷新变量列（Id / 名字是模型属性，订阅会兜住，这里是显式再保一次）</summary>
        internal void NotifyVariableChanged()
        {
            RaisePropertyChanged(nameof(VariableName));
            RaisePropertyChanged(nameof(VariableDisplay));
            RaisePropertyChanged(nameof(HasVariable));
        }

        /// <summary>
        /// 包一层作用域写模型。拿不到文档（没有打开的方案）时不写、报错、并把界面回灌成原值。
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

            using (document.BeginEdit(label))
            {
                write();
            }

            _host.ReportError(null);
            return true;
        }

        /// <summary>
        /// 数值列的统一提交口径：空串与解析失败都回灌原值 + 给中文原因，
        /// 等值不占撤销位。解析成功后按模型的实际落值记审计（回差 / 延时 / 断线都会被夹下限）。
        /// </summary>
        private void CommitNumber(string? text, string propertyName, string label, Func<double> read, Action<double> write)
        {
            var before = read();
            var raw = (text ?? string.Empty).Trim();

            if (raw.Length == 0)
            {
                _host.ReportError($"「{label}」不能为空；已恢复成 {Format(before)}。");
                RaisePropertyChanged(propertyName);
                return;
            }

            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                _host.ReportError($"「{label}」请填数字，当前填的是「{raw}」；已恢复成 {Format(before)}。");
                RaisePropertyChanged(propertyName);
                return;
            }

            if (parsed.Equals(before))
            {
                _host.ReportError(null);
                return;
            }

            if (!TryWrite($"修改报警 [{_model.Name}] 的{label}", () => write(parsed)))
                return;

            var after = read();
            _host.AuditField(_model, $"{label}：{Format(before)} → {Format(after)}");
            RaisePropertyChanged(propertyName);
            RaisePropertyChanged(nameof(ConditionText));
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

            _host.AuditField(_model, $"{label}已更新");
            RaisePropertyChanged(propertyName);
        }

        private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e) => RaisePropertyChanged(null);

        /// <summary>数值的显示 / 解析口径（不变文化，避免"小数点变成逗号"这种随语言环境漂移的问题）</summary>
        private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private static string Show(string text) => text.Length == 0 ? "（空）" : text;
    }

    /// <summary>
    /// 「条件种类」下拉框的一项：把"给人看的中文名"（<see cref="DisplayName"/>）和
    /// "落盘的枚举值"（<see cref="Kind"/>）配成一对，让 ComboBox 用
    /// <c>SelectedValuePath</c> + <c>SelectedValue</c> 就能直接绑，不必为"枚举显示成中文"再引进一个值转换器。
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

        /// <summary>下拉里显示的名字（与报警条 / 导出里的措辞同一个出处）</summary>
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
