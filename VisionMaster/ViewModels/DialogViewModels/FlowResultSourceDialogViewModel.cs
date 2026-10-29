using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Core.Interfaces;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 「运行统计 · 结果源」配置弹窗（设计基线 docs/UI设计基线/flow-result-source/ v1.1）。
    ///
    /// 一条流程显式声明"本轮 OK/NG 从哪来"：
    /// 自动 / 结果输出步骤 / 结果变量 / 步骤端口 / 不参与。
    ///
    /// 三条口径
    /// ---------
    /// ① **只改配置、不改流程图**：保存只写 <see cref="FlowModel.ResultSource"/> 等字段，
    ///    不自动插入结果输出步骤、不自动改变量。
    /// ② **下一轮生效**：解析器（<c>FlowVerdictResolver</c>）在轮末读取这些字段，
    ///    保存后无需重启流程、无需重新编译。
    /// ③ **Auto 不清空其它配置**：切回自动时保留已选的变量/端口，作为回退链的一环；
    ///    只有显式模式才"只认那一个来源"。
    ///
    /// 候选口径
    /// ---------
    /// - 变量：用户变量里 bool / string / int / long / double 四类（Q2 拍板），bool 排前；
    ///   非 bool 值由后端按 OK/NG/1/0 归一化，界面上给一行说明。
    /// - 步骤：深表（含分支内步骤，Q5 允许），只列非容器步骤。
    /// - 端口：所选步骤的 bool 输出端口（静态定义 + 动态端口快照），排除基类
    ///   <c>Success</c>/<c>ErrorMessage</c>（那是步骤技术状态，不是产品判定）。
    /// </summary>
    public sealed class FlowResultSourceDialogViewModel : BindableBase, IDialogAware
    {
        /// <summary>弹窗注册名（<c>App.RegisterTypes</c> 里 RegisterDialog 用的键，两处必须一致）</summary>
        public const string DialogName = "FlowResultSourceDialogView";

        /// <summary>入参键：流程 Id（FlowModel.FlowID，字符串）</summary>
        public const string FlowIdKey = "FlowId";

        /// <summary>入参键：流程名（Id 解析不到时的兜底）</summary>
        public const string FlowNameKey = "FlowName";

        /// <summary>结果输出步骤的显示名（与 Plugin.Utility 的 [Display(Name)] 一致）</summary>
        private const string ResultStepDisplayName = "结果输出";

        private readonly IWorkspaceManager _workspace;
        private readonly IPluginProvider _plugins;
        private readonly IFlowConfigAccess _access;

        private FlowModel? _flow;
        private FlowResultSourceKind _mode = FlowResultSourceKind.Auto;
        private ResultSourceModeOption? _selectedMode;
        private ResultSourceVariableOption? _selectedVariable;
        private ResultSourceStepOption? _selectedStep;
        private ResultSourcePortOption? _selectedPort;
        private string _readOnlyReason = string.Empty;

        public FlowResultSourceDialogViewModel(
            IWorkspaceManager workspace,
            IPluginProvider plugins,
            IFlowConfigAccess access)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _plugins = plugins ?? throw new ArgumentNullException(nameof(plugins));
            _access = access ?? throw new ArgumentNullException(nameof(access));

            SaveCommand = new DelegateCommand(OnSave, () => CanSave);
            CancelCommand = new DelegateCommand(() => RequestClose.Invoke(ButtonResult.Cancel));
            ClearVariableFilterCommand = new DelegateCommand(
                () => VariableFilter = string.Empty,
                () => HasVariableFilter);
        }

        public DialogCloseListener RequestClose { get; set; }

        public string Title => "运行统计 · 结果源";

        #region 候选

        public ObservableCollection<ResultSourceModeOption> Modes { get; } = new();

        public ObservableCollection<ResultSourceVariableOption> Variables { get; } = new();

        /// <summary>
        /// 变量下拉实际绑定的是这一份（按 <see cref="VariableFilter"/> 过滤）。
        /// 不直接给 ComboBox 换 ItemsSource：换源会清掉 SelectedItem 并经 TwoWay 推回 null。
        /// </summary>
        public ObservableCollection<ResultSourceVariableOption> FilteredVariables { get; } = new();

        private string _variableFilter = string.Empty;

        /// <summary>变量搜索词（名字 / 类型 / 来源任一命中；空串 = 不过滤）</summary>
        public string VariableFilter
        {
            get => _variableFilter;
            set
            {
                if (!SetProperty(ref _variableFilter, value ?? string.Empty)) return;
                ApplyVariableFilter();
                RaisePropertyChanged(nameof(HasVariableFilter));
                ClearVariableFilterCommand.RaiseCanExecuteChanged();
            }
        }

        public bool HasVariableFilter => _variableFilter.Trim().Length > 0;

        /// <summary>过滤后一条都没有（已选变量会被保留，所以只有"没选且没命中"才为 true）</summary>
        public bool HasNoVariableMatch => FilteredVariables.Count == 0;

        public ObservableCollection<ResultSourceStepOption> Steps { get; } = new();

        public ObservableCollection<ResultSourcePortOption> Ports { get; } = new();

        #endregion

        #region 选中项

        public ResultSourceModeOption? SelectedMode
        {
            get => _selectedMode;
            set
            {
                if (!SetProperty(ref _selectedMode, value) || value == null) return;
                _mode = value.Kind;
                RaiseModeFlags();
                Refresh();
            }
        }

        public ResultSourceVariableOption? SelectedVariable
        {
            get => _selectedVariable;
            set
            {
                if (!SetProperty(ref _selectedVariable, value)) return;
                // 已选项永远保留在过滤结果里，避免"打字时把已配好的选择弄丢"
                ApplyVariableFilter();
                Refresh();
            }
        }

        public ResultSourceStepOption? SelectedStep
        {
            get => _selectedStep;
            set
            {
                if (!SetProperty(ref _selectedStep, value)) return;
                RebuildPorts();
                // 换了步骤后，旧端口若不在新候选里必须清掉：否则校验放行、下拉却是空的
                if (_selectedPort != null && !Ports.Contains(_selectedPort))
                {
                    _selectedPort = null;
                    RaisePropertyChanged(nameof(SelectedPort));
                }
                Refresh();
            }
        }

        public ResultSourcePortOption? SelectedPort
        {
            get => _selectedPort;
            set
            {
                if (!SetProperty(ref _selectedPort, value)) return;
                Refresh();
            }
        }

        #endregion

        #region 只读 / 文案

        /// <summary>当前账号没有流程编辑权限：所有候选与保存都不可用</summary>
        public bool IsReadOnly => _readOnlyReason.Length > 0;

        /// <summary>可编辑（XAML 里给候选控件绑 IsEnabled，省一个反向转换器）</summary>
        public bool IsEditable => !IsReadOnly;

        public string ReadOnlyText => IsReadOnly ? "只读：" + _readOnlyReason : string.Empty;

        public string FlowTitle => _flow == null
            ? "流程不存在"
            : $"{_flow.FlowName} · {RoleText(_flow.Role)}";

        public bool IsAutoMode => _mode == FlowResultSourceKind.Auto;
        public bool IsResultStepMode => _mode == FlowResultSourceKind.ResultStep;
        public bool IsVariableMode => _mode == FlowResultSourceKind.Variable;
        public bool IsStepPortMode => _mode == FlowResultSourceKind.StepPort;
        public bool IsNoneMode => _mode == FlowResultSourceKind.None;

        private bool _isLineVerdictSource;

        /// <summary>
        /// 本流程是否作为方案的"线级来源"（保存时写 <see cref="SolutionModel.LineVerdictFlowId"/>）。
        ///
        /// 【2026-10-29 起只剩一个用途】底栏 KPI 已下线，这个标记不再决定底栏 OK/NG；
        /// 现在只影响「查看最近一轮」优先打开哪条流程（没有标记时取第一条生产流程）。
        /// 勾上本项即把其它流程的标记顶掉（同一时刻只有一条）。
        /// </summary>
        public bool IsLineVerdictSource
        {
            get => _isLineVerdictSource;
            set
            {
                if (!SetProperty(ref _isLineVerdictSource, value)) return;
                RaisePropertyChanged(nameof(WarningText));
            }
        }

        /// <summary>各模式的说明（与设计稿一致）</summary>
        public string HintText => _mode switch
        {
            FlowResultSourceKind.Auto =>
                "按固定优先级解析；命中第一个可用的来源。适合新方案，也兼容旧变量。",
            FlowResultSourceKind.ResultStep =>
                "只认流程图里的「结果输出」步骤；没有或本轮没执行 → 未判定。",
            FlowResultSourceKind.Variable =>
                "读一个已存在的变量。变量必须每轮被流程写入；跳过写入步骤时本轮会沿用上一轮。",
            FlowResultSourceKind.StepPort =>
                "读某个步骤的 bool 输出端口（如判定表的 IsOk）。步骤本轮没执行 → 未判定。",
            _ => "该流程不计入底栏 OK/NG / 未判定；轮次仍记录，可在下钻里查看。",
        };

        /// <summary>非阻断警告（保存仍然允许）</summary>
        public string WarningText => _mode switch
        {
            FlowResultSourceKind.ResultStep =>
                "没有结果输出步骤时，本轮记「未判定」；不会自动插入步骤。",
            FlowResultSourceKind.Variable =>
                "变量必须由流程每轮无条件写入；否则建议改用结果输出步骤或加轮次戳。",
            FlowResultSourceKind.StepPort =>
                "分支里的步骤允许选；但分支未走到时本轮记「未判定」，不会沿用上一轮。",
            FlowResultSourceKind.None =>
                IsLineVerdictSource
                    ? "当前是线级来源：「查看最近一轮」会优先打开本流程；选「不参与」不影响轮次记录。"
                    : "Home / End / 辅助流程适合选「不参与」；轮次仍记录，可在下钻里查看。",
            _ => HasMultipleResultSteps
                ? $"流程里有 {ResultStepCount} 个结果输出步骤（判定源歧义）：本轮跳过结果步骤、按回退链继续；建议只保留一个。"
                : HasResultStep
                    ? string.Empty
                    : "没有结果输出步骤；保存后按回退顺序解析（变量 > 端口 > 约定变量），可能为未判定。",
        };

        public bool HasWarning => WarningText.Length > 0;

        /// <summary>阻断保存的校验（空串 = 可保存）</summary>
        public string ValidationText
        {
            get
            {
                // 只读态由 ReadOnlyText 单独一行说明；这里不再重复一句"只读"，
                // 也不再逐项提示"请选择…"——只读下用户根本改不了，提示是噪音。
                if (IsReadOnly) return string.Empty;
                if (_flow == null) return "流程不存在（可能已被删除）";

                return _mode switch
                {
                    // 一条流程只允许一个结果输出（2026-10-29 规则）：多于一个时判定源歧义，
                    // 保存"只认结果输出步骤"的配置没有意义，先让用户把流程改对。
                    FlowResultSourceKind.ResultStep when HasMultipleResultSteps =>
                        $"流程里有 {ResultStepCount} 个结果输出步骤；只能保留一个（多余的请删除，或改用步骤端口模式）",
                    FlowResultSourceKind.Variable when _selectedVariable == null => "请选择结果变量",
                    FlowResultSourceKind.StepPort when _selectedStep == null => "请选择判定步骤",
                    FlowResultSourceKind.StepPort when _selectedPort == null => "请选择 bool 输出端口",
                    _ => string.Empty,
                };
            }
        }

        public bool HasValidationError => ValidationText.Length > 0;

        /// <summary>面板内的行内错误（与底部校验同源，按模式只亮一处；只读下不亮）</summary>
        public bool HasVariableError => !IsReadOnly && IsVariableMode && _selectedVariable == null;

        public bool HasStepError => !IsReadOnly && IsStepPortMode && _selectedStep == null;

        public bool HasPortError => !IsReadOnly && IsStepPortMode && _selectedPort == null;

        public bool CanSave => !IsReadOnly && _flow != null && ValidationText.Length == 0;

        /// <summary>底部固定摘要："当前生效：…"</summary>
        public string CurrentSummary
        {
            get
            {
                if (_flow == null) return "未选择流程";

                return _mode switch
                {
                    FlowResultSourceKind.Auto => HasResultStep
                        ? $"自动 · 结果输出步骤「{ResultStepName}」"
                        : "自动 · 未找到结果输出步骤（回退到变量 / 端口 / 约定变量）",
                    FlowResultSourceKind.ResultStep => HasResultStep
                        ? $"结果输出步骤 · {ResultStepName}"
                        : "结果输出步骤 · 未找到（本轮将未判定）",
                    FlowResultSourceKind.Variable => _selectedVariable == null
                        ? "结果变量 · 未选择"
                        : $"结果变量 · {_selectedVariable.Name}（当前值 {_selectedVariable.ValueText}）",
                    FlowResultSourceKind.StepPort => _selectedStep == null || _selectedPort == null
                        ? "步骤端口 · 未选择"
                        : $"步骤端口 · {_selectedStep.StepName}.{_selectedPort.Name}",
                    _ => "不参与统计",
                };
            }
        }

        public bool HasResultStep => ResultStepName.Length > 0;

        /// <summary>流程里检测到的结果输出步骤个数（一条流程只允许一个）</summary>
        public int ResultStepCount { get; private set; }

        /// <summary>多于一个：判定源歧义（解析器按未判定处理，不猜"最后执行"）</summary>
        public bool HasMultipleResultSteps => ResultStepCount > 1;

        /// <summary>检测到的结果输出步骤名（顿号分隔，用于歧义提示）</summary>
        public string ResultStepListText => string.Join("、", _resultStepNames);

        /// <summary>自动模式的"当前生效"是否落在结果输出步骤上（歧义时不算）</summary>
        public bool AutoUsesResultStep => HasResultStep && !HasMultipleResultSteps;

        private readonly List<string> _resultStepNames = new();

        /// <summary>流程里检测到的结果输出步骤名（没有则空串）</summary>
        public string ResultStepName { get; private set; } = string.Empty;

        /// <summary>结果输出步骤的位置说明（主路径 / 分支内）</summary>
        public string ResultStepLocation { get; private set; } = string.Empty;

        public string VariableValueText => _selectedVariable == null
            ? "当前值 — · 类型 —"
            : $"当前值 {_selectedVariable.ValueText} · 类型 {_selectedVariable.TypeText}";

        public string PortValueText => _selectedPort == null
            ? "端口类型 —"
            : $"端口类型 {_selectedPort.TypeText}";

        public string StepLocationText => _selectedStep == null
            ? "位置 —"
            : _selectedStep.IsBranch ? "位置 分支内（未走到时本轮未判定）" : "位置 主路径";

        #endregion

        public DelegateCommand SaveCommand { get; }

        public DelegateCommand CancelCommand { get; }

        /// <summary>清空变量搜索词（搜索框右侧的 ×；空串时按钮收起）</summary>
        public DelegateCommand ClearVariableFilterCommand { get; }

        #region IDialogAware

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() { }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            string? flowId = null;
            string? flowName = null;
            if (parameters != null)
            {
                if (parameters.TryGetValue<string>(FlowIdKey, out var id)) flowId = id;
                if (parameters.TryGetValue<string>(FlowNameKey, out var name)) flowName = name;
            }

            _flow = FindFlow(flowId, flowName);
            if (_flow == null)
            {
                Variables.Clear();
                FilteredVariables.Clear();
                Steps.Clear();
                Ports.Clear();
                ResultStepName = string.Empty;
                ResultStepLocation = string.Empty;
                Refresh();
                return;
            }

            bool canEdit = _access.CanEdit(out var reason);
            _readOnlyReason = canEdit ? string.Empty : (string.IsNullOrWhiteSpace(reason) ? "需要流程编辑权限" : reason);

            // 线级汇总来源：显式配置优先；没配过时按解析结果回显（第一条勾「整线的」流程），
            // 让"当前实际生效的那一条"在界面上可见 —— 否则用户看到全都没勾，却有人占了线级口径。
            var solution = _workspace.CurrentSolution;
            _isLineVerdictSource = solution != null
                && string.Equals(solution.ResolveLineVerdictFlow()?.FlowID, _flow.FlowID, StringComparison.Ordinal);
            RaisePropertyChanged(nameof(IsLineVerdictSource));

            BuildModes();
            BuildVariables();
            BuildSteps();
            RebuildPorts();
            Preselect();
            Refresh();
        }

        #endregion

        #region 构建

        private void BuildModes()
        {
            Modes.Clear();
            Modes.Add(new ResultSourceModeOption(
                FlowResultSourceKind.Auto, "自动", "结果输出步骤 > 结果变量 > 步骤端口", "推荐"));
            Modes.Add(new ResultSourceModeOption(
                FlowResultSourceKind.ResultStep, "结果输出步骤", "只用流程图里的「结果输出」", string.Empty));
            Modes.Add(new ResultSourceModeOption(
                FlowResultSourceKind.Variable, "结果变量", "读已有变量（bool / string / int / double）", string.Empty));
            Modes.Add(new ResultSourceModeOption(
                FlowResultSourceKind.StepPort, "步骤端口", "读判定表 / 逻辑运算的 bool 输出", string.Empty));
            Modes.Add(new ResultSourceModeOption(
                FlowResultSourceKind.None, "不参与统计", "不计入底栏 OK/NG；仍记录轮次", string.Empty));
        }

        private void BuildVariables()
        {
            Variables.Clear();

            var rows = new List<ResultSourceVariableOption>();
            foreach (var variable in _workspace.GlobalVariables ?? Enumerable.Empty<IVariable>())
            {
                if (variable == null) continue;
                if (variable.VariableType == VariableType.System) continue;

                string? typeText = TypeTextOf(variable.DataType);
                if (typeText == null) continue;

                rows.Add(new ResultSourceVariableOption(
                    variable.VariableId,
                    variable.Name ?? string.Empty,
                    typeText,
                    variable.Value?.ToString() ?? "—",
                    KindTextOf(variable.VariableType)));
            }

            // bool 排前（Q2：bool 是默认选择），其余按名字
            foreach (var row in rows
                         .OrderBy(r => r.TypeText == "bool" ? 0 : 1)
                         .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
            {
                Variables.Add(row);
            }

            ApplyVariableFilter();
        }

        /// <summary>
        /// 按 <see cref="VariableFilter"/> 重建 <see cref="FilteredVariables"/>。
        ///
        /// 两条纪律：
        /// ① 已选中的变量永远保留（否则用户一边打字一边把已配好的选择弄丢）；
        /// ② 重填集合时 ComboBox 可能经 TwoWay 把 SelectedItem 推成 null——填完把选择顶回去。
        /// </summary>
        private void ApplyVariableFilter()
        {
            var keep = _selectedVariable;
            string filter = (_variableFilter ?? string.Empty).Trim();

            FilteredVariables.Clear();
            foreach (var option in Variables)
            {
                bool match = filter.Length == 0
                             || option.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                             || option.TypeText.Contains(filter, StringComparison.OrdinalIgnoreCase)
                             || option.KindText.Contains(filter, StringComparison.OrdinalIgnoreCase);

                if (match || ReferenceEquals(option, keep))
                    FilteredVariables.Add(option);
            }

            if (keep != null && !ReferenceEquals(_selectedVariable, keep))
            {
                _selectedVariable = keep;
                RaisePropertyChanged(nameof(SelectedVariable));
            }

            RaisePropertyChanged(nameof(HasNoVariableMatch));
        }

        private void BuildSteps()
        {
            Steps.Clear();
            ResultStepName = string.Empty;
            ResultStepLocation = string.Empty;
            _resultStepNames.Clear();
            ResultStepCount = 0;

            if (_flow == null) return;

            WalkSteps(_flow.Steps, inBranch: false);
            ResultStepCount = _resultStepNames.Count;
        }

        private void WalkSteps(IEnumerable<StepModel>? steps, bool inBranch)
        {
            if (steps == null) return;

            foreach (var step in steps)
            {
                if (step == null) continue;

                if (step is IContainerStep container)
                {
                    if (container.Children != null)
                    {
                        foreach (var branch in container.Children)
                            WalkSteps(branch?.Steps, inBranch: true);
                    }
                    continue;
                }

                string display = (inBranch ? "    " : string.Empty) + (step.StepName ?? string.Empty);
                Steps.Add(new ResultSourceStepOption(
                    step.StepID,
                    step.StepName ?? string.Empty,
                    display,
                    inBranch,
                    step.PluginTypeName ?? string.Empty));

                if (IsResultOutputStep(step))
                {
                    _resultStepNames.Add(step.StepName ?? ResultStepDisplayName);
                    if (ResultStepName.Length == 0)
                    {
                        ResultStepName = step.StepName ?? ResultStepDisplayName;
                        ResultStepLocation = inBranch ? "分支内" : "主路径";
                    }
                }
            }
        }

        private bool IsResultOutputStep(StepModel step)
        {
            string? typeName = step.PluginTypeName;
            if (string.IsNullOrWhiteSpace(typeName)) return false;
            if (!_plugins.ModulePlugins.TryGetValue(typeName, out var tool) || tool == null) return false;
            return string.Equals(tool.Name, ResultStepDisplayName, StringComparison.Ordinal);
        }

        private void RebuildPorts()
        {
            Ports.Clear();

            var step = _selectedStep;
            if (step == null) return;

            var seen = new HashSet<string>(StringComparer.Ordinal);

            if (_plugins.ModulePlugins.TryGetValue(step.PluginTypeName, out var tool)
                && tool?.OutputDefinitions != null)
            {
                foreach (var port in tool.OutputDefinitions)
                    AddPort(port?.Name, port?.DataTypeName, port?.Description, seen);
            }

            // 动态端口快照（如判定表按规则生成的结果端口）
            var model = FindStepModel(step.StepId);
            if (model?.OutputPortDefinitions != null)
            {
                foreach (var port in model.OutputPortDefinitions)
                    AddPort(port?.Name, port?.DataTypeName, port?.Description, seen);
            }
        }

        private void AddPort(string? name, string? dataTypeName, string? description, HashSet<string> seen)
        {
            if (string.IsNullOrWhiteSpace(name) || !seen.Add(name!)) return;
            // 基类端口是"步骤技术状态"，不是产品判定：不列，免得配出"NG 判成步骤失败"
            if (string.Equals(name, "Success", StringComparison.Ordinal)) return;
            if (string.Equals(name, "ErrorMessage", StringComparison.Ordinal)) return;

            if (!IsBoolTypeName(dataTypeName)) return;

            Ports.Add(new ResultSourcePortOption(name!, "bool", description ?? string.Empty));
        }

        private void Preselect()
        {
            if (_flow == null) return;

            _mode = _flow.ResultSource;
            _selectedMode = Modes.FirstOrDefault(m => m.Kind == _mode) ?? Modes.FirstOrDefault();
            _mode = _selectedMode?.Kind ?? FlowResultSourceKind.Auto;

            // 变量：Id 优先、名字兜底（与解析器同一顺序）
            _selectedVariable = null;
            if (_flow.ResultVariableId != Guid.Empty)
            {
                _selectedVariable = Variables.FirstOrDefault(v => v.VariableId == _flow.ResultVariableId);
            }
            if (_selectedVariable == null && !string.IsNullOrWhiteSpace(_flow.ResultVariableName))
            {
                _selectedVariable = Variables.FirstOrDefault(v =>
                    string.Equals(v.Name, _flow.ResultVariableName, StringComparison.Ordinal));
            }

            // 步骤：Id 优先；端口按配置名回选
            _selectedStep = _flow.ResultStepId == Guid.Empty
                ? null
                : Steps.FirstOrDefault(s => s.StepId == _flow.ResultStepId);
            RebuildPorts();
            _selectedPort = string.IsNullOrWhiteSpace(_flow.ResultPortName)
                ? null
                : Ports.FirstOrDefault(p =>
                    string.Equals(p.Name, _flow.ResultPortName, StringComparison.Ordinal));

            // 预选值是直接写字段的，必须显式通知：否则 ListBox 在 OnDialogOpened 之前就绑定了
            // 一次 null，之后收不到 SelectedItem 变化，界面上"当前模式"没有高亮。
            RaisePropertyChanged(nameof(SelectedMode));
            RaisePropertyChanged(nameof(SelectedVariable));
            RaisePropertyChanged(nameof(SelectedStep));
            RaisePropertyChanged(nameof(SelectedPort));

            // 预选是直接写字段的：补一次过滤，保证"已选项一定在候选里"
            ApplyVariableFilter();

            RaiseModeFlags();
        }

        private StepModel? FindStepModel(Guid stepId)
        {
            if (_flow == null) return null;
            return FindStepModel(_flow.Steps, stepId);
        }

        private static StepModel? FindStepModel(IEnumerable<StepModel>? steps, Guid stepId)
        {
            if (steps == null) return null;

            foreach (var step in steps)
            {
                if (step == null) continue;
                if (step.StepID == stepId) return step;

                if (step is IContainerStep container && container.Children != null)
                {
                    foreach (var branch in container.Children)
                    {
                        var hit = FindStepModel(branch?.Steps, stepId);
                        if (hit != null) return hit;
                    }
                }
            }

            return null;
        }

        private FlowModel? FindFlow(string? flowId, string? flowName)
        {
            var flows = _workspace.CurrentSolution?.Flows;
            if (flows == null) return null;

            if (!string.IsNullOrWhiteSpace(flowId))
            {
                var byId = flows.FirstOrDefault(f => f != null
                    && string.Equals(f.FlowID, flowId, StringComparison.OrdinalIgnoreCase));
                if (byId != null) return byId;
            }

            if (!string.IsNullOrWhiteSpace(flowName))
            {
                return flows.FirstOrDefault(f => f != null
                    && string.Equals(f.FlowName, flowName, StringComparison.Ordinal));
            }

            return null;
        }

        #endregion

        #region 保存

        private void OnSave()
        {
            if (!Apply()) return;
            RequestClose.Invoke(new DialogParameters(), ButtonResult.OK);
        }

        /// <summary>
        /// 保存核心（不关窗）：校验 → 写 <see cref="FlowModel"/> 的结果源字段。
        /// 返回 false 表示校验/只读未通过，什么都没写。
        ///
        /// 单独暴露出来是为了让断言宿主能验"写了什么"，而不必伪造 Prism 的关窗回调
        /// （<c>DialogCloseListener</c> 的回调由 DialogService 注入，宿主里没有容器）。
        /// </summary>
        public bool Apply()
        {
            if (!CanSave || _flow == null) return false;

            _flow.ResultSource = _mode;

            // 线级来源：勾上就占位（同一时刻只有一条，直接覆盖），取消勾选只清"自己的"标记，
            // 不误清别的流程 —— 否则取消勾选会顺手把别人配好的来源抹掉。
            // （底栏 KPI 已于 2026-10-29 下线；这个标记现在只影响「查看最近一轮」优先打开哪条流程。）
            var solution = _workspace.CurrentSolution;
            if (solution != null)
            {
                bool ownsFlag = string.Equals(solution.LineVerdictFlowId, _flow.FlowID, StringComparison.Ordinal);
                if (IsLineVerdictSource) solution.LineVerdictFlowId = _flow.FlowID;
                else if (ownsFlag) solution.LineVerdictFlowId = string.Empty;
            }

            // 显式模式写自己的字段；Auto/None 保留其它配置作为回退链（切回来不丢）
            if (_selectedVariable != null)
            {
                _flow.ResultVariableId = _selectedVariable.VariableId;
                _flow.ResultVariableName = _selectedVariable.Name;
            }

            if (_selectedStep != null)
                _flow.ResultStepId = _selectedStep.StepId;

            if (_selectedPort != null)
                _flow.ResultPortName = _selectedPort.Name;

            return true;
        }

        #endregion

        #region 通知

        private void RaiseModeFlags()
        {
            RaisePropertyChanged(nameof(IsAutoMode));
            RaisePropertyChanged(nameof(IsResultStepMode));
            RaisePropertyChanged(nameof(IsVariableMode));
            RaisePropertyChanged(nameof(IsStepPortMode));
            RaisePropertyChanged(nameof(IsNoneMode));
        }

        private void Refresh()
        {
            RaisePropertyChanged(nameof(FlowTitle));
            RaisePropertyChanged(nameof(IsLineVerdictSource));
            RaisePropertyChanged(nameof(HintText));
            RaisePropertyChanged(nameof(WarningText));
            RaisePropertyChanged(nameof(HasWarning));
            RaisePropertyChanged(nameof(ValidationText));
            RaisePropertyChanged(nameof(HasValidationError));
            RaisePropertyChanged(nameof(HasVariableError));
            RaisePropertyChanged(nameof(HasStepError));
            RaisePropertyChanged(nameof(HasPortError));
            RaisePropertyChanged(nameof(CanSave));
            RaisePropertyChanged(nameof(CurrentSummary));
            RaisePropertyChanged(nameof(HasResultStep));
            RaisePropertyChanged(nameof(ResultStepName));
            RaisePropertyChanged(nameof(ResultStepLocation));
            RaisePropertyChanged(nameof(ResultStepCount));
            RaisePropertyChanged(nameof(HasMultipleResultSteps));
            RaisePropertyChanged(nameof(ResultStepListText));
            RaisePropertyChanged(nameof(AutoUsesResultStep));
            RaisePropertyChanged(nameof(VariableValueText));
            RaisePropertyChanged(nameof(PortValueText));
            RaisePropertyChanged(nameof(StepLocationText));
            RaisePropertyChanged(nameof(HasNoVariableMatch));
            RaisePropertyChanged(nameof(IsReadOnly));
            RaisePropertyChanged(nameof(IsEditable));
            RaisePropertyChanged(nameof(ReadOnlyText));
            SaveCommand.RaiseCanExecuteChanged();
        }

        #endregion

        #region 工具

        private static string RoleText(FlowRole role) => role switch
        {
            FlowRole.Home => "回原点",
            FlowRole.End => "收尾",
            _ => "普通流程",
        };

        private static string? TypeTextOf(Type? type)
        {
            if (type == null) return null;
            if (type == typeof(bool)) return "bool";
            if (type == typeof(string)) return "string";
            if (type == typeof(int)) return "int";
            if (type == typeof(long)) return "long";
            if (type == typeof(double)) return "double";
            return null;
        }

        private static string KindTextOf(VariableType type) => type switch
        {
            VariableType.Communication => "网络",
            VariableType.Io => "IO",
            VariableType.System => "系统",
            _ => "本地",
        };

        private static bool IsBoolTypeName(string? dataTypeName)
        {
            if (string.IsNullOrWhiteSpace(dataTypeName)) return false;
            string name = dataTypeName!;
            int comma = name.IndexOf(',');
            if (comma > 0) name = name.Substring(0, comma);
            return string.Equals(name.Trim(), "System.Boolean", StringComparison.Ordinal)
                   || string.Equals(name.Trim(), "bool", StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }

    /// <summary>结果源模式卡片（左栏一项）</summary>
    public sealed class ResultSourceModeOption
    {
        public ResultSourceModeOption(FlowResultSourceKind kind, string title, string description, string badge)
        {
            Kind = kind;
            Title = title;
            Description = description;
            Badge = badge ?? string.Empty;
        }

        public FlowResultSourceKind Kind { get; }
        public string Title { get; }
        public string Description { get; }
        public string Badge { get; }
        public bool IsRecommended => Badge.Length > 0;
    }

    /// <summary>结果变量候选</summary>
    public sealed class ResultSourceVariableOption
    {
        public ResultSourceVariableOption(Guid variableId, string name, string typeText, string valueText, string kindText)
        {
            VariableId = variableId;
            Name = name;
            TypeText = typeText;
            ValueText = valueText;
            KindText = kindText;
        }

        public Guid VariableId { get; }
        public string Name { get; }
        public string TypeText { get; }
        public string ValueText { get; }
        public string KindText { get; }
        public string Display => $"{Name}（{TypeText} · {KindText}）";
    }

    /// <summary>判定步骤候选（深表，含分支内步骤）</summary>
    public sealed class ResultSourceStepOption
    {
        public ResultSourceStepOption(Guid stepId, string stepName, string display, bool isBranch, string pluginTypeName)
        {
            StepId = stepId;
            StepName = stepName;
            Display = display;
            IsBranch = isBranch;
            PluginTypeName = pluginTypeName;
        }

        public Guid StepId { get; }
        public string StepName { get; }
        public string Display { get; }
        public bool IsBranch { get; }
        public string PluginTypeName { get; }
        public string LocationText => IsBranch ? "分支内" : "主路径";
    }

    /// <summary>步骤的 bool 输出端口候选</summary>
    public sealed class ResultSourcePortOption
    {
        public ResultSourcePortOption(string name, string typeText, string description)
        {
            Name = name;
            TypeText = typeText;
            Description = description;
        }

        public string Name { get; }
        public string TypeText { get; }
        public string Description { get; }
        public string Display => string.IsNullOrWhiteSpace(Description)
            ? Name
            : $"{Name}（{Description}）";
    }
}
