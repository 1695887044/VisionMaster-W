using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using Core.Commands;
using Core.Interfaces;
using VisionMaster.Plugins.Util;

namespace Plugin.Util.Models
{
    /// <summary>
    /// 「变量赋值」行列表里一行在界面上的视图模型。
    ///
    /// 视图的 DataContext 仍是插件实例（宿主靠它取 IPluginConfigView.OnConfirm）；
    /// 行模板的 DataContext 是本类。所有业务判断仍走插件（候选、提示、端口），
    /// 这里只做绑定投影与变更通知。
    /// </summary>
    public sealed class AssignmentRowVm : ObservableObject
    {
        private readonly VariableAssignmentPlugin _plugin;
        private IInputPort? _port;

        public AssignmentRowVm(VariableAssignmentPlugin plugin, AssignmentRow row)
        {
            _plugin = plugin ?? throw new ArgumentNullException(nameof(plugin));
            Row = row ?? throw new ArgumentNullException(nameof(row));
            Row.PropertyChanged += OnRowPropertyChanged;
        }

        public AssignmentRow Row { get; }

        public IStepConfigData? StepData => _plugin.StepData;

        /// <summary>本行「值」的输入端口（LinkableValueEditor 的 Port）。</summary>
        public IInputPort? Port => _port;

        /// <summary>本行名字候选（按作用域过滤；全局失效时首项是只读失效项）。</summary>
        public ObservableCollection<NameCandidate> Candidates { get; } = new();

        /// <summary>全局候选浮层里的过滤结果（搜索框输入时更新）。</summary>
        public ObservableCollection<NameCandidate> FilteredCandidates { get; } = new();

        private bool _isGlobalNameOpen;
        /// <summary>全局名字浮层是否打开（ToggleButton / Popup 双向绑定）。</summary>
        public bool IsGlobalNameOpen
        {
            get => _isGlobalNameOpen;
            set
            {
                if (!SetProperty(ref _isGlobalNameOpen, value)) return;
                if (value) GlobalSearchText = string.Empty;
            }
        }

        private string _globalSearchText = string.Empty;
        public string GlobalSearchText
        {
            get => _globalSearchText;
            set
            {
                if (!SetProperty(ref _globalSearchText, value ?? string.Empty)) return;
                ApplyGlobalFilter();
            }
        }

        public bool HasName => !string.IsNullOrWhiteSpace(Row.VariableName);

        private ICommand? _selectGlobalCandidateCommand;
        /// <summary>全局候选点击：落成名字常量并关闭浮层（只能选，不能手填）。</summary>
        public ICommand SelectGlobalCandidateCommand => _selectGlobalCandidateCommand ??= new RelayCommand(parameter =>
        {
            if (parameter is not NameCandidate candidate) return;
            if (candidate.IsInvalid) return;
            Row.VariableName = candidate.Name;
            IsGlobalNameOpen = false;
        });

        /// <summary>
        /// 全局名字 ComboBox 的选中项。
        ///
        /// 为什么绑 SelectedItem 而不是 SelectedValue：可编辑/非可编辑 ComboBox 在
        /// ItemsSource 被 Clear/Add 时会把 SelectedValue 推成 null，行名被顶空后
        /// 即使再顶回字符串，ComboBox 也不一定重新选中同名项（真机表现为"全局名字显示空白"）。
        /// 绑对象本身，集合重建后再按名字找回同一个候选，显示就稳。
        /// </summary>
        public NameCandidate? SelectedGlobalCandidate
        {
            get
            {
                var name = Row.VariableName;
                return Candidates.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));
            }
            set
            {
                if (value == null) return;
                if (!string.Equals(Row.VariableName, value.Name, StringComparison.Ordinal))
                    Row.VariableName = value.Name;
            }
        }

        public string Hint { get; private set; } = string.Empty;
        public string HintLevel { get; private set; } = "Info"; // Info / Warn / Error

        public bool IsGlobal => string.Equals(Row.Scope, VariableAssignmentPlugin.ScopeGlobal, StringComparison.OrdinalIgnoreCase);
        public bool IsRuntime => !IsGlobal;

        public bool IsInvalidGlobal => IsGlobal && !string.IsNullOrWhiteSpace(Row.VariableName) && _plugin.FindVariable(Row.VariableName, VariableAssignmentPlugin.ScopeGlobal) == null;
        public bool IsManualRuntime => IsRuntime && !string.IsNullOrWhiteSpace(Row.VariableName) && _plugin.FindVariable(Row.VariableName, VariableAssignmentPlugin.ScopeRuntime) == null;

        public bool HasLegacyBadge => Row.UsesLegacyValuePort;

        public string DisplayName => string.IsNullOrWhiteSpace(Row.VariableName) ? "未命名变量" : Row.VariableName.Trim();
        public string ScopeText => IsGlobal ? "全局变量" : "运行时变量";
        public string TypeText => _plugin.GetTypeText(Row);
        public string ValueSourceText => _plugin.GetValueSourceText(Row);
        public string RowIndexText => _plugin.GetRowIndexText(this);

        public string RowMessage => string.Equals(HintLevel, "Info", StringComparison.Ordinal) ? string.Empty : Hint;
        public bool HasRowMessage => !string.IsNullOrEmpty(RowMessage);
        public string RowMessageLevel => HintLevel;

        /// <summary>全局失效行已经有「失效」徽标 + 红色行内提示，不再占一个警告图标位。</summary>
        public bool ShowWarnIcon => HasRowMessage && !IsInvalidGlobal;

        public bool CanCreate => IsRuntime;
        public bool ShowLock => IsGlobal;

        internal bool IsRefreshingCandidates { get; private set; }

        internal void SetPort(IInputPort? port)
        {
            if (ReferenceEquals(_port, port)) return;
            _port = port;
            OnPropertyChanged(nameof(Port));
        }

        internal void RefreshCandidates()
        {
            IsRefreshingCandidates = true;
            try
            {
                var keep = Row.VariableName;
                Candidates.Clear();
                foreach (var candidate in _plugin.BuildCandidates(Row))
                    Candidates.Add(candidate);

                // 可编辑 ComboBox 在 ItemsSource 被 Clear() 时会把 Text 置空并沿 TwoWay 推回端口；
                // 全局 ComboBox 的 SelectedValue 同理。端口/行值是唯一真值来源，重算候选不该改它。
                if (!string.Equals(Row.VariableName, keep, StringComparison.Ordinal))
                    Row.VariableName = keep;
            }
            finally
            {
                IsRefreshingCandidates = false;
            }

            OnPropertyChanged(nameof(Candidates));
            OnPropertyChanged(nameof(SelectedGlobalCandidate));
            ApplyGlobalFilter();
        }

        private void ApplyGlobalFilter()
        {
            FilteredCandidates.Clear();
            var query = (GlobalSearchText ?? string.Empty).Trim();
            foreach (var candidate in Candidates)
            {
                if (query.Length == 0
                    || candidate.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                    || candidate.Note.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                    || candidate.TypeName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    FilteredCandidates.Add(candidate);
                }
            }
        }

        internal void RefreshHint()
        {
            var hint = _plugin.BuildHint(Row);
            Hint = hint.Text;
            HintLevel = hint.Level;
            OnPropertyChanged(nameof(Hint));
            OnPropertyChanged(nameof(HintLevel));
            OnPropertyChanged(nameof(RowMessage));
            OnPropertyChanged(nameof(HasRowMessage));
            OnPropertyChanged(nameof(RowMessageLevel));
            OnPropertyChanged(nameof(ShowWarnIcon));
            OnPropertyChanged(nameof(HasName));
            OnPropertyChanged(nameof(DisplayName));
            // 作用域切换会换掉整块模板（全局只能选 / 运行时可选可填）、右侧作用域徽标、
            // 自动创建列的可编辑性；这些投影不在 Row 上，必须在这里逐个通知。
            OnPropertyChanged(nameof(IsGlobal));
            OnPropertyChanged(nameof(IsRuntime));
            OnPropertyChanged(nameof(ScopeText));
            OnPropertyChanged(nameof(CanCreate));
            OnPropertyChanged(nameof(ShowLock));
            OnPropertyChanged(nameof(IsInvalidGlobal));
            OnPropertyChanged(nameof(IsManualRuntime));
            OnPropertyChanged(nameof(TypeText));
            OnPropertyChanged(nameof(ValueSourceText));
            OnPropertyChanged(nameof(SelectedGlobalCandidate));
        }

        internal void RefreshAll()
        {
            RefreshHint();
            OnPropertyChanged(string.Empty);
        }

        internal void Detach()
        {
            Row.PropertyChanged -= OnRowPropertyChanged;
        }

        private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
            => _plugin.OnRowPropertyChanged(this, e);
    }
}
