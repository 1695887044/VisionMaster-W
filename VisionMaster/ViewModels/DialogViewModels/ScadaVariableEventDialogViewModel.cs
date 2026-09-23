using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Models;
using VisionMaster.Scada;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 组态「变量事件」弹窗：把<b>值驱动</b>的动作配置集中到一处。
    ///
    /// 它解决的是什么问题
    /// ---------
    /// 图元事件（按下/释放）与画面事件（加载/卸载）都能在属性面板里配，
    /// 而"液位超 80 就报警""开关量为真就启泵"这类<b>由值驱动</b>的规则没有"谁点了谁"，
    /// 属性面板里没有落脚点（面板描述的是"当前选中的那个图元"）。
    /// 手册 7.5.2 把这类规则单列成一类，可组态对象直接就是"变量"，本弹窗就是那条口径的落点。
    ///
    /// 为什么是"左清单 + 右详情"
    /// ---------
    /// 变量事件的形状是"一个变量一条记录"（见 <see cref="ScadaVariableEvent"/> 的类注释），
    /// 所以"有哪些变量、哪个配了"本身就是一张清单。左边一眼看完，
    /// 右边改的是<b>当前选中那一条</b>，与属性面板"选中什么就编辑什么"的手感一致。
    ///
    /// 为什么未配置的变量要"显式点按钮新建"
    /// ---------
    /// 选中即建记录看起来很省事，代价是"我只是点了一下看看，方案就多出一条空配置、
    /// 版本号就脏了一次"。空配置在执行侧等同没配，但它会跟着方案文件走、
    /// 会让"这次保存到底改了什么"变得说不清。所以建记录这一步必须由用户显式点。
    ///
    /// 为什么左栏分"变量 / 已失效"两段
    /// ---------
    /// 变量被删掉之后，那条变量事件记录还在方案里（它引用的是一个已经不存在的 Id）。
    /// 这种记录既不能执行，又占着地方——必须让用户看得见、删得掉，
    /// 但不能和正常变量混在一起（混着看，用户会以为"这个变量还在，只是没值"）。
    ///
    /// 写回口径
    /// ---------
    /// 与属性面板逐条一致：<b>即时写回 + 进撤销栈</b>。本弹窗没有"确定"这个提交动作，
    /// 每一次勾选、每一次改数值都当场落进模型并包一层作用域，
    /// 所以底部的按钮只有「关闭」——它关的是窗，不是"是否采纳"。
    ///
    /// 为什么阈值用<b>字符串</b>属性接输入框
    /// ---------
    /// 与 <see cref="ScadaSystemParametersViewModel"/> 同一个理由：直接把 <c>double?</c> 绑到
    /// <c>TextBox.Text</c>，用户敲进"8O"（字母 O）时绑定会静默失效、属性保持旧值，
    /// 界面上看不出任何异常。这里收字符串、在失焦时统一解析，解析不了就<b>回灌原值并给一句中文原因</b>，
    /// 是"宁可不改，也不能让他以为改成了"。
    /// </summary>
    public class ScadaVariableEventDialogViewModel : BindableBase, IDialogAware
    {
        /// <summary>审计"事件"列写<b>类别名</b>：这一列答的是"哪一类事"，"具体哪一件"在动作列</summary>
        private const string AuditEventText = "变量事件";

        private const string AuditActionCreate = "新建配置";
        private const string AuditActionRemove = "删除配置";
        private const string AuditActionThreshold = "修改阈值";
        private const string AuditActionToggle = "启停监视";

        /// <summary>取不到被操作对象名时的审计对象列兜底（正常路径下用不上）</summary>
        private const string AuditSubjectFallback = "变量事件表";

        /// <summary>
        /// 变量级事件的<b>全部</b>候选，顺序即界面顺序。
        ///
        /// 只取 <see cref="ScadaEventType"/> 里的值驱动那一段（4 至 8）：
        /// 加载完成 / 卸载是画面级、按下 / 释放是图元级、输入完成时是控件级，
        /// 它们各自有更合适的宿主，列在这里只会让用户配出一条运行态永远不会触发的规则。
        /// </summary>
        private static readonly ScadaEventType[] VariableEventTypes =
        {
            ScadaEventType.ValueChanged,
            ScadaEventType.ValueBecameTrue,
            ScadaEventType.ValueBecameFalse,
            ScadaEventType.ValueOverUpperLimit,
            ScadaEventType.ValueUnderLowerLimit,
        };

        private readonly IWorkspaceManager _workspace;
        private readonly IScadaVariablePicker _picker;
        private readonly IScadaPagePicker _pagePicker;
        private readonly ScadaAuditWriter? _audit;
        private readonly Func<string?>? _actorProvider;

        /// <summary>左栏全量行（<see cref="Rows"/> 是它按分组投影出来的视图）</summary>
        private readonly ObservableCollection<ScadaVariableEventRow> _rows = new();

        private ScadaVariableEventRow? _selectedRow;
        private string? _errorMessage;

        /// <summary>
        /// 正在把模型值灌回界面。这一段时间里的属性写不该被当成"用户改的"——
        /// 否则回灌会反过来再提交一次，形成"改一下、弹回去、再改一下"的循环。
        /// </summary>
        private bool _syncing;

        private bool _upperLimitEnabled;
        private bool _lowerLimitEnabled;
        private string _upperLimitText = string.Empty;
        private string _lowerLimitText = string.Empty;
        private bool _isRecordEnabled = true;

        /// <param name="workspace">只用来取"当前方案的组态文档"与"工程变量清单"</param>
        /// <param name="picker">「选变量」入口，转交给 5 个事件编辑器（它们要能配"写变量"动作）</param>
        /// <param name="pagePicker">「选画面」入口，同上（它们要能配"切换画面"动作）</param>
        /// <param name="audit">
        /// 操作审计落盘端。默认 null = 不记（单机调试 / 断言里直接 new 的路径）。
        /// <b>可选参数必须由宿主显式工厂注册</b>：交给容器去猜的结果是编译能过、
        /// 运行起来"配了半天一条审计都没有"而且不报错（见 App.xaml.cs 的注册处）。
        /// </param>
        /// <param name="actorProvider">"此刻登录着谁"；取不到时 <see cref="ScadaAuditEntry"/> 自己回落"未登录"</param>
        public ScadaVariableEventDialogViewModel(
            IWorkspaceManager workspace,
            IScadaVariablePicker picker,
            IScadaPagePicker pagePicker,
            ScadaAuditWriter? audit = null,
            Func<string?>? actorProvider = null)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _picker = picker ?? throw new ArgumentNullException(nameof(picker));
            _pagePicker = pagePicker ?? throw new ArgumentNullException(nameof(pagePicker));
            _audit = audit;
            _actorProvider = actorProvider;

            // 按"变量 / 已失效"两段分组。分组键直接取行上的中文串（GroupLabel），
            // 于是组标题就是它本身，不必再为"0/1 翻译成中文"引进一个值转换器；
            // 组序按各行首次出现的顺序，而 _rows 是先装变量行、后装失效行，
            // 所以"变量"永远排在"已失效"前面。
            Rows = CollectionViewSource.GetDefaultView(_rows);
            Rows.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ScadaVariableEventRow.GroupLabel)));

            CreateCommand = new DelegateCommand(OnCreate, () => _selectedRow is { Info: not null, Record: null });
            RemoveCommand = new DelegateCommand(OnRemove, () => _selectedRow?.Record != null);
            CloseCommand = new DelegateCommand(() => RequestClose.Invoke(new DialogParameters(), ButtonResult.OK));
        }

        public DialogCloseListener RequestClose { get; set; }

        public string Title => "变量事件";

        /// <summary>左栏的分组视图（变量 / 已失效），供 ListBox 绑定</summary>
        public ICollectionView Rows { get; }

        /// <summary>当前选中的行；右栏编辑的就是它的记录</summary>
        public ScadaVariableEventRow? SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (SetProperty(ref _selectedRow, value))
                    OnSelectionChanged();
            }
        }

        /// <summary>
        /// 当前选中行上那 5 个事件编辑器（变量级 5 类，顺序见 <see cref="VariableEventTypes"/>）。
        /// 没选中 / 没记录时为空集合。
        /// </summary>
        public ObservableCollection<ScadaEventEditorViewModel> Editors { get; } = new();

        /// <summary>左栏有没有内容（空态提示靠它显隐）</summary>
        public bool HasAnyRows => _rows.Count > 0;

        /// <summary>右栏有没有可编辑的对象</summary>
        public bool HasSelection => _selectedRow != null;

        /// <summary>当前选中行有没有配置记录（阈值区与事件区的显隐都由它决定）</summary>
        public bool HasRecord => _selectedRow?.Record != null;

        /// <summary>选中了一个变量但还没为它建配置：显示"新建"入口而不是空荡荡一片</summary>
        public bool ShowCreatePrompt => HasSelection && !HasRecord;

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

        /// <summary>上限判定开关（勾选 = 上限有值 = 运行态判这一条）</summary>
        public bool UpperLimitEnabled
        {
            get => _upperLimitEnabled;
            set
            {
                if (!SetProperty(ref _upperLimitEnabled, value) || _syncing)
                    return;

                var record = _selectedRow?.Record;
                if (record == null)
                    return;

                // 勾上时给一个"立刻看得见"的初值：框里已经填了有效数字就照它，否则落 0。
                // 为什么不落 null 让用户自己填：勾了却不判，界面上是"勾着的"，那是在骗人。
                double? seed = value
                    ? (TryParseLimit(_upperLimitText, out var parsed) ? parsed : 0d)
                    : null;

                using (record.BeginEdit(value ? "启用上限判定" : "取消上限判定"))
                {
                    record.UpperLimit = seed;
                }

                SyncFromRecord();
                Audit(AuditActionThreshold, true, $"上限判定：{(value ? $"启用（{Format(seed)}）" : "取消")}", null);
            }
        }

        /// <summary>下限判定开关（勾选 = 下限有值 = 运行态判这一条）</summary>
        public bool LowerLimitEnabled
        {
            get => _lowerLimitEnabled;
            set
            {
                if (!SetProperty(ref _lowerLimitEnabled, value) || _syncing)
                    return;

                var record = _selectedRow?.Record;
                if (record == null)
                    return;

                double? seed = value
                    ? (TryParseLimit(_lowerLimitText, out var parsed) ? parsed : 0d)
                    : null;

                using (record.BeginEdit(value ? "启用下限判定" : "取消下限判定"))
                {
                    record.LowerLimit = seed;
                }

                SyncFromRecord();
                Audit(AuditActionThreshold, true, $"下限判定：{(value ? $"启用（{Format(seed)}）" : "取消")}", null);
            }
        }

        /// <summary>上限数值（失焦提交；清空 = 不判这条限值）</summary>
        public string UpperLimitText
        {
            get => _upperLimitText;
            set
            {
                if (SetProperty(ref _upperLimitText, value ?? string.Empty) && !_syncing)
                    CommitLimit(upper: true);
            }
        }

        /// <summary>下限数值（失焦提交；清空 = 不判这条限值）</summary>
        public string LowerLimitText
        {
            get => _lowerLimitText;
            set
            {
                if (SetProperty(ref _lowerLimitText, value ?? string.Empty) && !_syncing)
                    CommitLimit(upper: false);
            }
        }

        /// <summary>
        /// 整条监视的启用开关。停用时运行态<b>连变量都不订阅</b>（配置保留），
        /// 所以"先配好、上线再开"是可行的，不必把配好的东西删掉。
        /// </summary>
        public bool IsRecordEnabled
        {
            get => _isRecordEnabled;
            set
            {
                if (!SetProperty(ref _isRecordEnabled, value) || _syncing)
                    return;

                var record = _selectedRow?.Record;
                if (record == null || record.IsEnabled == value)
                    return;

                using (record.BeginEdit(value ? "启用变量事件" : "停用变量事件"))
                {
                    record.IsEnabled = value;
                }

                Audit(AuditActionToggle, true, value ? "启用监视" : "停用监视", null);
            }
        }

        /// <summary>「新建」：为选中的变量建一条记录。只有"有变量、没记录"时才可用</summary>
        public DelegateCommand CreateCommand { get; }

        /// <summary>「删除」：删掉选中行的那条记录（连同它下面所有钩子与动作）</summary>
        public DelegateCommand RemoveCommand { get; }

        /// <summary>「关闭」：本弹窗没有提交动作（写回是即时的），所以它只关窗</summary>
        public DelegateCommand CloseCommand { get; }

        /// <summary>当前方案的组态文档；没有打开的方案时为 <c>null</c></summary>
        private ScadaDocument? Document => _workspace.CurrentSolution?.Scada;

        private void OnCreate()
        {
            var row = _selectedRow;
            var document = Document;

            if (row?.Info == null || row.Record != null)
                return;

            if (document == null)
            {
                ErrorMessage = "当前没有打开的方案，无法新建变量事件配置。";
                Audit(AuditActionCreate, false, null, ErrorMessage);
                return;
            }

            var record = document.GetOrAddVariableEvent(row.Info.Model.VariableId, row.Info.Name);
            row.Attach(record);

            // 换了记录就要重建 5 个编辑器（它们构造时把宿主钉死在记录上），阈值也要重新灌一遍
            OnSelectionChanged();

            ErrorMessage = null;
            Audit(AuditActionCreate, true, $"为变量「{row.Name}」新建事件配置", null);
        }

        private void OnRemove()
        {
            var row = _selectedRow;
            var document = Document;
            var record = row?.Record;

            if (row == null || record == null)
                return;

            if (document == null)
            {
                ErrorMessage = "当前没有打开的方案，无法删除变量事件配置。";
                Audit(AuditActionRemove, false, null, ErrorMessage);
                return;
            }

            if (!document.TryRemoveVariableEvent(record, out var error))
            {
                ErrorMessage = error;
                Audit(AuditActionRemove, false, null, error);
                return;
            }

            Audit(AuditActionRemove, true, $"删除变量「{row.Name}」的事件配置", null);

            // 重建整份清单并把选中留在同一个变量上：删完之后它应当停在"未配置"状态，
            // 而不是整个右栏塌回"请从左侧选择"——用户刚动过的地方不该消失。
            Reload(row.VariableId);
        }

        /// <summary>重建左栏清单，并把选中落在指定变量上（<see cref="Guid.Empty"/> = 不选）</summary>
        private void Reload(Guid keepVariableId)
        {
            var document = Document;

            _rows.Clear();

            var variables = new List<IVariable>();
            foreach (var variable in _workspace.GlobalVariables)
            {
                if (variable != null)
                    variables.Add(variable);
            }

            // 认领记录分两遍走：先让所有变量按 Id 认（权威身份），再让没认到的按名字兜底（旧数据）。
            // 混在一遍里会出错：一个"名字恰好和别人的旧记录同名"的变量会先把那条记录抢走，
            // 而它真正的记录（按 Id 认得的那条）反倒落空。
            var claimed = new HashSet<ScadaVariableEvent>();
            var matched = new Dictionary<IVariable, ScadaVariableEvent?>();

            foreach (var variable in variables)
            {
                var record = document?.FindVariableEvent(variable.VariableId);
                if (record != null && claimed.Add(record))
                    matched[variable] = record;
            }

            foreach (var variable in variables)
            {
                if (matched.ContainsKey(variable))
                    continue;

                var record = document?.FindVariableEventByName(variable.Name);
                matched[variable] = record != null && claimed.Add(record) ? record : null;
            }

            foreach (var variable in variables)
                _rows.Add(new ScadaVariableEventRow(variable, matched[variable]));

            // 剩下的记录 = 变量已经不在了（或被停用后删掉了变量），单独成段让用户能删干净
            if (document != null)
            {
                foreach (var record in document.VariableEvents)
                {
                    if (record == null || claimed.Contains(record))
                        continue;

                    _rows.Add(new ScadaVariableEventRow(null, record));
                }
            }

            RaisePropertyChanged(nameof(HasAnyRows));

            SelectedRow = keepVariableId == Guid.Empty
                ? null
                : _rows.FirstOrDefault(r => r.VariableId == keepVariableId);
        }

        /// <summary>
        /// 选中变化：重建 5 个编辑器、把记录里的阈值灌回界面、刷新几个派生标志与命令可用性。
        /// 建成一个方法是为了让"新建"那条路也能复用同一份收尾动作。
        /// </summary>
        private void OnSelectionChanged()
        {
            Editors.Clear();

            var record = _selectedRow?.Record;
            if (record != null)
            {
                foreach (var type in VariableEventTypes)
                {
                    Editors.Add(new ScadaEventEditorViewModel(
                        record,
                        type,
                        type.DisplayName(),
                        Describe(type),
                        _picker,
                        _pagePicker));
                }
            }

            SyncFromRecord();

            RaisePropertyChanged(nameof(HasSelection));
            RaisePropertyChanged(nameof(HasRecord));
            RaisePropertyChanged(nameof(ShowCreatePrompt));

            CreateCommand.RaiseCanExecuteChanged();
            RemoveCommand.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// 把记录里的当前值灌进编辑态（阈值两格、两个开关、整条监视开关）。
        /// 灌的同时顺手清掉上一次的报错——用户已经开始改下一个地方了，旧红字留着只会误导。
        /// </summary>
        private void SyncFromRecord()
        {
            var record = _selectedRow?.Record;

            _syncing = true;
            try
            {
                _upperLimitEnabled = record?.UpperLimit != null;
                _lowerLimitEnabled = record?.LowerLimit != null;
                _upperLimitText = Format(record?.UpperLimit);
                _lowerLimitText = Format(record?.LowerLimit);
                _isRecordEnabled = record?.IsEnabled ?? true;

                RaisePropertyChanged(nameof(UpperLimitEnabled));
                RaisePropertyChanged(nameof(LowerLimitEnabled));
                RaisePropertyChanged(nameof(UpperLimitText));
                RaisePropertyChanged(nameof(LowerLimitText));
                RaisePropertyChanged(nameof(IsRecordEnabled));
            }
            finally
            {
                _syncing = false;
            }

            ErrorMessage = null;
        }

        /// <summary>
        /// 提交一格阈值。空串 = 不判这条限值（顺带把勾选摘掉，界面上两处始终一致）；
        /// 解析不了 = <b>回灌原值 + 一句中文原因</b>，绝不把"看不懂的输入"当成 0 悄悄写进模型。
        /// </summary>
        private void CommitLimit(bool upper)
        {
            var record = _selectedRow?.Record;
            if (record == null)
                return;

            var text = (upper ? _upperLimitText : _lowerLimitText).Trim();
            var old = upper ? record.UpperLimit : record.LowerLimit;
            var label = upper ? "上限" : "下限";

            if (text.Length == 0)
            {
                if (old == null)
                    return;

                using (record.BeginEdit($"清空{label}"))
                {
                    if (upper)
                        record.UpperLimit = null;
                    else
                        record.LowerLimit = null;
                }

                SyncFromRecord();
                Audit(AuditActionThreshold, true, $"{label}：{Format(old)} → 不判", null);
                return;
            }

            if (!TryParseLimit(text, out var parsed))
            {
                SyncFromRecord();
                ErrorMessage = $"「{label}」请填数字，当前填的是「{text}」；已恢复成 {Format(old)}。";
                return;
            }

            if (old == parsed)
                return;

            using (record.BeginEdit($"修改{label}"))
            {
                if (upper)
                    record.UpperLimit = parsed;
                else
                    record.LowerLimit = parsed;
            }

            SyncFromRecord();
            Audit(AuditActionThreshold, true, $"{label}：{Format(old)} → {Format(parsed)}", null);
        }

        /// <summary>
        /// 落一条审计。落盘端自己吞掉 IO 异常并报一次诊断（见 <see cref="ScadaAuditWriter"/>），
        /// 所以这里不 try、也不看返回值：审计写不成不该让"配置改没改成"这件事变样。
        ///
        /// 成功与失败都记："谁试着删但没删成"与"删成了"是两条不同的线索。
        /// </summary>
        private void Audit(string action, bool ok, string? detail, string? error)
            => _audit?.Append(new ScadaAuditEntry(
                _actorProvider?.Invoke(),
                _selectedRow?.Name ?? AuditSubjectFallback,
                AuditEventText,
                action,
                ok ? ScadaAuditOutcome.Success : ScadaAuditOutcome.Failed,
                ok ? detail : error));

        /// <summary>数值的显示 / 解析口径（不变文化，避免"小数点变成逗号"这种随语言环境漂移的问题）</summary>
        private static string Format(double? value)
            => value?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty;

        private static bool TryParseLimit(string? text, out double value)
            => double.TryParse(
                (text ?? string.Empty).Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);

        /// <summary>5 类值驱动事件的悬停说明（说清"什么时候会触发"，而不是重复事件名）</summary>
        private static string Describe(ScadaEventType type) => type switch
        {
            ScadaEventType.ValueChanged => "变量的值发生变化时触发（变大变小都算，只记一次变化）",
            ScadaEventType.ValueBecameTrue => "变量的值由假变成真时触发一次",
            ScadaEventType.ValueBecameFalse => "变量的值由真变成假时触发一次",
            ScadaEventType.ValueOverUpperLimit => "值越过上限时触发（需在上方勾选并填写上限）",
            ScadaEventType.ValueUnderLowerLimit => "值低于下限时触发（需在上方勾选并填写下限）",
            _ => string.Empty,
        };

        #region IDialogAware

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() { }

        /// <summary>
        /// 打开时现读整张变量事件表。
        /// 每次打开都重建，是因为两次打开之间用户可能已经去变量管理里增删过变量、
        /// 或者改过方案；缓存在字段里跨弹窗复用，就会给出"清单里没有它，却怎么也选不上"的幽灵条目。
        /// </summary>
        public void OnDialogOpened(IDialogParameters parameters) => Reload(Guid.Empty);

        #endregion
    }

    /// <summary>
    /// 左栏清单里的一行：一个变量（或一条"变量已不存在"的记录）。
    ///
    /// 为什么变量那一半<b>组合</b> <see cref="ScadaVariableRow"/> 而不是再摊一遍：
    /// 类型 / 来源 / 当前值这三样的展示口径（尤其是"本地 / 网络"与友好类型名）
    /// 已经有一份实现，复制一份出去的结果是两处慢慢长歪——右栏写着"小数 (Double)"、
    /// 变量选择器里写着"浮点数"，用户会以为选错了变量。
    ///
    /// 为什么要能 <see cref="Attach"/> 记录：本行的记录可能是后补的
    /// （用户点「新建」那一刻才建出来），字段必须可换、换了要通知。
    /// </summary>
    public sealed class ScadaVariableEventRow : BindableBase
    {
        private ScadaVariableEvent? _record;

        /// <param name="variable">对应的工程变量；<c>null</c> = 这条记录引用的变量已经不在了</param>
        /// <param name="record">已配的变量事件记录；<c>null</c> = 这个变量还没配</param>
        public ScadaVariableEventRow(IVariable? variable, ScadaVariableEvent? record)
        {
            Info = variable == null ? null : new ScadaVariableRow(variable);
            Attach(record);
        }

        /// <summary>变量那一半的展示投影；<c>null</c> = 已失效</summary>
        public ScadaVariableRow? Info { get; }

        /// <summary>已配的记录；<c>null</c> = 还没配</summary>
        public ScadaVariableEvent? Record => _record;

        /// <summary>变量还在（右栏的类型 / 来源 / 当前值一行靠它显隐）</summary>
        public bool HasVariable => Info != null;

        /// <summary>变量已不存在，这条记录只能删（或留着，但运行态永远不触发）</summary>
        public bool IsStale => Info == null;

        /// <summary>分组键，同时也是组标题：变量行落在"变量"段，失效行落在"已失效"段</summary>
        public string GroupLabel => IsStale ? "已失效（变量已不存在）" : "变量";

        /// <summary>
        /// 稳定身份：优先取记录上的（那是运行态真正用的那个），没记录时退回变量的。
        /// 删除后要靠它把选中留回同一个变量上，所以必须和 Reload 的匹配口径一致。
        /// </summary>
        public Guid VariableId
            => _record?.VariableId is { } id && id != Guid.Empty
                ? id
                : Info?.VariableId ?? Guid.Empty;

        /// <summary>显示名（失效行只能靠记录里的名字快照）</summary>
        public string Name
            => Info?.Name
            ?? (string.IsNullOrWhiteSpace(_record?.VariableName) ? "未命名变量" : _record!.VariableName!);

        public Type? DataType => Info?.DataType;

        public string SourceLabel => Info?.SourceLabel ?? string.Empty;

        public string ValueText => Info?.ValueText ?? string.Empty;

        /// <summary>左栏那一小撮状态字：未配置 / 已配置 / 已停用</summary>
        public string SummaryText
            => _record == null ? "未配置" : _record.IsEnabled ? "已配置" : "已停用";

        /// <summary>这条记录有没有配阈值（左栏提示"只配了阈值、没配事件"这种半成品状态）</summary>
        public string ConditionText => _record?.ConditionText ?? string.Empty;

        /// <summary>
        /// 换/补上记录并重挂属性变更订阅。
        /// 订阅的理由：整条监视的启用开关一改，左栏那个状态字就得跟着变——
        /// 不订阅的话，用户停用之后左栏还写着"已配置"，他会以为没点着。
        /// </summary>
        public void Attach(ScadaVariableEvent? record)
        {
            if (ReferenceEquals(_record, record))
            {
                RaiseAll();
                return;
            }

            if (_record != null)
                _record.PropertyChanged -= OnRecordPropertyChanged;

            _record = record;

            if (_record != null)
                _record.PropertyChanged += OnRecordPropertyChanged;

            RaiseAll();
        }

        private void OnRecordPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            RaisePropertyChanged(nameof(SummaryText));
            RaisePropertyChanged(nameof(ConditionText));
        }

        private void RaiseAll()
        {
            RaisePropertyChanged(nameof(Record));
            RaisePropertyChanged(nameof(VariableId));
            RaisePropertyChanged(nameof(Name));
            RaisePropertyChanged(nameof(SummaryText));
            RaisePropertyChanged(nameof(ConditionText));
        }
    }
}
