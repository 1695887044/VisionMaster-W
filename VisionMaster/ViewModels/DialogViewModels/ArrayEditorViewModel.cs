using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Models;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>
    /// 「编辑数组」弹窗的一行。
    /// <para>编辑器只处理字符串，类型转换由 VM 在确认时统一做（与
    /// <see cref="GlobalVariableManagerViewModel.TryBuildArray"/> 同一份规则）。</para>
    /// </summary>
    public sealed class ArrayEditorRow : BindableBase
    {
        private string _indexText = string.Empty;
        private string _value = string.Empty;
        private string _baselineText = string.Empty;
        private bool _hasBaseline;
        private bool _isChanged;
        private bool _hasError;
        private bool _canMoveUp;
        private bool _canMoveDown;

        /// <summary>下标文案（[0] / [1] …），每次增删/移动后由 VM 重排</summary>
        public string IndexText
        {
            get => _indexText;
            set => SetProperty(ref _indexText, value);
        }

        /// <summary>元素文本</summary>
        public string Value
        {
            get => _value;
            set
            {
                if (SetProperty(ref _value, value ?? string.Empty))
                    ValueEdited?.Invoke(this);
            }
        }

        /// <summary>基准值文本（该下标在基准里存在时才有意义）</summary>
        public string BaselineText
        {
            get => _baselineText;
            set => SetProperty(ref _baselineText, value);
        }

        /// <summary>基准里是否有这一项（新增元素没有"改没改"可言）</summary>
        public bool HasBaseline
        {
            get => _hasBaseline;
            set => SetProperty(ref _hasBaseline, value);
        }

        /// <summary>该行是否与基准不同：输入框下方显示「基准：…」，行尾出现 ↺</summary>
        public bool IsChanged
        {
            get => _isChanged;
            set => SetProperty(ref _isChanged, value);
        }

        /// <summary>校验失败标记：该行输入框标红</summary>
        public bool HasError
        {
            get => _hasError;
            set => SetProperty(ref _hasError, value);
        }

        /// <summary>首行不能上移 / 末行不能下移（视图直接绑 IsEnabled）</summary>
        public bool CanMoveUp
        {
            get => _canMoveUp;
            set => SetProperty(ref _canMoveUp, value);
        }

        public bool CanMoveDown
        {
            get => _canMoveDown;
            set => SetProperty(ref _canMoveDown, value);
        }

        /// <summary>用户改过本行文本 → VM 重算「已修改」并清掉上一次的校验错误</summary>
        public Action<ArrayEditorRow>? ValueEdited { get; set; }
    }

    /// <summary>
    /// 「编辑数组」弹窗（变量管理 → 数组根的「编辑数组…」）。
    ///
    /// 设计基线：`docs\UI设计基线\global-variables\` v3（2026-10-21 弹窗专项优化）。
    ///
    /// 三条语义（v2 起，v3 不改）：
    /// ① **装填当前值**：弹窗里看到的就是表格里那组值，不是 DefaultValue；
    /// ② **默认只写当前值**：写入范围选「同时写初始值」才把新数组写进 DefaultValue；
    /// ③ **校验在弹窗内**：失败项标红 + 底部「第 N 项：…」，弹窗不关、其余编辑保留。
    ///
    /// v3 的信息层级：
    /// - 写入范围是分段选择（只写当前值 / 同时写初始值），下方一句话随选择说明后果；
    /// - 每行显示基准值与「已修改」标记，行尾 ↺ 只恢复这一项；
    /// - 底部汇总「N 项已修改」，并提供「恢复全部为基准」。
    /// </summary>
    public class ArrayEditorViewModel : BindableBase, IDialogAware
    {
        /// <summary>弹窗注册名（与 App.xaml.cs 的 RegisterDialog 一致）</summary>
        public const string DialogName = "ArrayEditor";

        /// <summary>入参/回传键：被编辑的本地数组变量</summary>
        public const string VariableKey = "Variable";

        /// <summary>回传键：转换好的新数组</summary>
        public const string ArrayKey = "Array";

        /// <summary>回传键：是否同时写入初始值（基准）</summary>
        public const string SyncBaselineKey = "SyncBaseline";

        /// <summary>入参键：网络数组写入前复查"流程是否在运行"（弹窗打开后流程仍可能被定时/变量触发）</summary>
        public const string IsFlowRunningKey = "IsFlowRunning";

        private IVariable? _model;
        private Type? _elementType;
        private Func<bool>? _isFlowRunning;

        /// <summary>打开时快照的基准数组（DefaultValue）：用于逐行「基准：…」与恢复</summary>
        private Array? _baseline;

        private string _errorText = string.Empty;
        private bool _syncBaseline;
        private int _changedCount;

        public ArrayEditorViewModel()
        {
            AddCommand = new DelegateCommand(AddElement);
            RemoveCommand = new DelegateCommand<object?>(RemoveElement);
            MoveUpCommand = new DelegateCommand<object?>(parameter => MoveElement(parameter, -1));
            MoveDownCommand = new DelegateCommand<object?>(parameter => MoveElement(parameter, 1));
            ResetRowCommand = new DelegateCommand<object?>(ResetRow);
            ResetAllCommand = new DelegateCommand(ResetAll);
            WriteCurrentOnlyCommand = new DelegateCommand(() => SyncBaseline = false);
            WriteBothCommand = new DelegateCommand(() => SyncBaseline = true);
            ConfirmCommand = new DelegateCommand(Confirm);
            CancelCommand = new DelegateCommand(() => RequestClose.Invoke(ButtonResult.Cancel));
        }

        /// <summary>网络数组：整组写入设备，没有"基准/恢复初始值"语义</summary>
        private NetworkVariableModel? NetworkModel => _model as NetworkVariableModel;

        /// <summary>是否为网络数组写入模式（与本地数组编辑共用同一个弹窗）</summary>
        public bool IsNetwork => NetworkModel != null;

        /// <summary>网络数组的固定元素个数（来自地址配置；0 = 未知）</summary>
        private int RequiredLength => NetworkModel?.AddressConfig?.Length ?? 0;

        /// <summary>本地数组才显示"写入范围/基准"面板；网络数组没有基准</summary>
        public bool ShowBaselineControls => !IsNetwork;

        /// <summary>网络数组元素个数固定，不允许增删/移动</summary>
        public bool CanEditStructure => !IsNetwork;

        /// <summary>
        /// 网络数组必须等第一拍轮询到值才允许确认：否则表格是空的，
        /// 用户直接点确定会把整段设备数据写成 0（本地数组没有这个风险）。
        /// </summary>
        public bool CanConfirm => !IsNetwork
            || (_model?.Value is Array current && current.Length == RequiredLength);

        public string Title => _model == null
            ? "编辑数组"
            : IsNetwork ? $"写入数组 · {_model.Name}" : $"编辑数组 · {_model.Name}";

        /// <summary>副标题：本地显示当前/基准项数；网络显示固定项数</summary>
        public string Subtitle => _elementType == null
            ? string.Empty
            : IsNetwork
                ? $"{DescribeElementType(_elementType)} · {Elements.Count} 项（设备点位固定）"
                : $"{DescribeElementType(_elementType)} · 当前 {Elements.Count} 项 · 基准 {BaselineCount} 项";

        private int BaselineCount => _baseline?.Length ?? 0;

        /// <summary>基准提示：与当前值等长时只说项数；不等长时明确标出来</summary>
        public string BaselineHint => BaselineCount == Elements.Count
            ? $"基准 {BaselineCount} 项"
            : $"基准 {BaselineCount} 项（长度不同）";

        /// <summary>写入范围的后果说明（随分段选择变化；网络模式说明整组写设备）</summary>
        public string ScopeHint => IsNetwork
            ? "整组写入设备；元素个数由地址配置固定，不能增删。"
            : SyncBaseline
                ? "新数组会同时成为初始值；此后「恢复初始值」回到这组值。"
                : "初始值（基准）保持不变；「恢复初始值」仍回到原基准。";

        /// <summary>分段选择左段（只写当前值）的选中态</summary>
        public bool IsCurrentOnly => !SyncBaseline;

        /// <summary>已修改项数（相对基准；新增行没有基准，不计入）</summary>
        public int ChangedCount
        {
            get => _changedCount;
            private set
            {
                if (!SetProperty(ref _changedCount, value)) return;
                RaisePropertyChanged(nameof(HasChanges));
                RaisePropertyChanged(nameof(ChangedSummary));
                RaisePropertyChanged(nameof(FooterSummary));
            }
        }

        public bool HasChanges => _changedCount > 0;

        public string ChangedSummary => _changedCount > 0 ? $"{_changedCount} 项已修改" : "未修改";

        /// <summary>底部汇总：本地数组显示"N 项已修改"；网络数组没有基准，显示总项数</summary>
        public string FooterSummary => IsNetwork ? $"共 {Elements.Count} 项" : ChangedSummary;

        /// <summary>元素行（顺序即数组顺序）</summary>
        public ObservableCollection<ArrayEditorRow> Elements { get; } = new();

        /// <summary>是否同时写入初始值（基准）。默认 false：基准是「恢复初始值」的锚点，改它要显式选择</summary>
        public bool SyncBaseline
        {
            get => _syncBaseline;
            set
            {
                if (!SetProperty(ref _syncBaseline, value)) return;
                RaisePropertyChanged(nameof(IsCurrentOnly));
                RaisePropertyChanged(nameof(ScopeHint));
            }
        }

        public string ErrorText
        {
            get => _errorText;
            private set
            {
                if (SetProperty(ref _errorText, value))
                    RaisePropertyChanged(nameof(HasError));
            }
        }

        public bool HasError => !string.IsNullOrEmpty(_errorText);

        public DelegateCommand AddCommand { get; }
        public DelegateCommand<object?> RemoveCommand { get; }
        public DelegateCommand<object?> MoveUpCommand { get; }
        public DelegateCommand<object?> MoveDownCommand { get; }
        public DelegateCommand<object?> ResetRowCommand { get; }
        public DelegateCommand ResetAllCommand { get; }
        public DelegateCommand WriteCurrentOnlyCommand { get; }
        public DelegateCommand WriteBothCommand { get; }
        public DelegateCommand ConfirmCommand { get; }
        public DelegateCommand CancelCommand { get; }

        public DialogCloseListener RequestClose { get; set; }

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() { }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            _model = parameters.TryGetValue<IVariable>(VariableKey, out var model) ? model : null;
            _elementType = _model?.DataType.GetElementType();
            _baseline = _model?.DefaultValue as Array;
            _isFlowRunning = parameters.TryGetValue<Func<bool>>(IsFlowRunningKey, out var isRunning)
                ? isRunning
                : null;

            // 同一实例被复用时（单例注册 / 设计器）不能带着上次的勾选与错误条打开
            SyncBaseline = false;
            ErrorText = string.Empty;

            Elements.Clear();

            // 装填**当前值**（表格里那组），不是基准。
            // 只有当前值还不是数组（null）时才退回基准，避免空弹窗让人以为变量没数据。
            Array? source = _model?.Value as Array ?? _baseline;
            if (source != null)
            {
                foreach (object? item in source)
                    Elements.Add(NewRow(Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty));
            }

            // 网络数组：元素个数由设备点位固定。当前值还没到（第一拍轮询前）也要把 N 行摆出来，
            // 否则用户看到的是一张空表、以为"这个数组没有元素"。
            if (IsNetwork && RequiredLength > 0)
            {
                while (Elements.Count < RequiredLength)
                    Elements.Add(NewRow(string.Empty));
                while (Elements.Count > RequiredLength)
                    Elements.RemoveAt(Elements.Count - 1);
            }

            RefreshRowState();
            RaisePropertyChanged(nameof(Title));
            RaisePropertyChanged(nameof(Subtitle));
            RaisePropertyChanged(nameof(BaselineHint));
            RaisePropertyChanged(nameof(ScopeHint));
            RaisePropertyChanged(nameof(ShowBaselineControls));
            RaisePropertyChanged(nameof(CanEditStructure));
            RaisePropertyChanged(nameof(CanConfirm));
            RaisePropertyChanged(nameof(IsNetwork));
            RaisePropertyChanged(nameof(FooterSummary));
        }

        /// <summary>造一行（Value 先于 ValueEdited 赋值：构造期不会触发重算）</summary>
        private ArrayEditorRow NewRow(string value) => new()
        {
            Value = value,
            ValueEdited = OnRowEdited,
        };

        private void AddElement()
        {
            if (IsNetwork) return; // 网络数组元素个数固定
            Elements.Add(NewRow(string.Empty));
            RefreshRowState();
            ClearError();
        }

        private void RemoveElement(object? parameter)
        {
            if (IsNetwork) return; // 网络数组元素个数固定
            // R27：CommandParameter 来自 ItemsControl 的绑定，编译器不检查类型 → 用 object? + 判型
            if (parameter is not ArrayEditorRow row) return;

            Elements.Remove(row);
            RefreshRowState();
            ClearError();
        }

        private void MoveElement(object? parameter, int delta)
        {
            if (IsNetwork) return; // 网络数组元素个数固定
            if (parameter is not ArrayEditorRow row) return;

            int from = Elements.IndexOf(row);
            int to = from + delta;
            if (from < 0 || to < 0 || to >= Elements.Count) return;

            Elements.Move(from, to);
            RefreshRowState();
            ClearError();   // 红框跟着行走了，错误文案里的「第 N 项」已经失效
        }

        /// <summary>行内 ↺：把该行恢复为基准对应项（基准里没有这一项时什么都不做）</summary>
        private void ResetRow(object? parameter)
        {
            if (parameter is not ArrayEditorRow row) return;

            int index = Elements.IndexOf(row);
            if (index < 0 || _baseline == null || index >= _baseline.Length) return;

            row.Value = Convert.ToString(_baseline.GetValue(index), CultureInfo.InvariantCulture) ?? string.Empty;
        }

        /// <summary>「恢复全部为基准」：整张列表换成基准（长度也跟着变）</summary>
        private void ResetAll()
        {
            if (_baseline == null) return;

            Elements.Clear();
            foreach (object? item in _baseline)
                Elements.Add(NewRow(Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty));

            RefreshRowState();
            ClearError();
        }

        /// <summary>重排下标 + 刷新上下移可用性 + 重算每行基准/已修改 + 刷新副标题</summary>
        private void RefreshRowState()
        {
            for (int i = 0; i < Elements.Count; i++)
            {
                var row = Elements[i];
                row.IndexText = $"[{i}]";
                row.CanMoveUp = i > 0;
                row.CanMoveDown = i < Elements.Count - 1;

                bool hasBaseline = _baseline != null && i < _baseline.Length;
                row.HasBaseline = hasBaseline;
                row.BaselineText = hasBaseline
                    ? Convert.ToString(_baseline!.GetValue(i), CultureInfo.InvariantCulture) ?? string.Empty
                    : string.Empty;
                row.IsChanged = hasBaseline && !RowEqualsBaseline(row.Value, _baseline!.GetValue(i));
            }

            ChangedCount = Elements.Count(row => row.IsChanged);
            RaisePropertyChanged(nameof(Subtitle));
            RaisePropertyChanged(nameof(BaselineHint));
            RaisePropertyChanged(nameof(FooterSummary));
        }

        /// <summary>用户改了一行：只重算这一行的「已修改」与总数（不重建列表，保住焦点）</summary>
        private void OnRowEdited(ArrayEditorRow row)
        {
            int index = Elements.IndexOf(row);
            if (index >= 0 && _baseline != null && index < _baseline.Length)
            {
                row.HasBaseline = true;
                row.BaselineText = Convert.ToString(_baseline.GetValue(index), CultureInfo.InvariantCulture) ?? string.Empty;
                row.IsChanged = !RowEqualsBaseline(row.Value, _baseline.GetValue(index));
            }
            else
            {
                row.HasBaseline = false;
                row.BaselineText = string.Empty;
                row.IsChanged = false;
            }

            ChangedCount = Elements.Count(r => r.IsChanged);
            ClearError();
        }

        /// <summary>
        /// 行文本与基准值是否相等：按元素类型转换后比较（"12.50" 与 "12.5" 视为相同）。
        /// 转换失败 = 与基准不同（错误行同时也会显示基准，方便对照）。
        /// </summary>
        private bool RowEqualsBaseline(string text, object? baselineValue)
        {
            if (_elementType == null)
                return string.Equals(text, Convert.ToString(baselineValue, CultureInfo.InvariantCulture), StringComparison.Ordinal);

            // 字符串：null 与空串在界面上都显示为空，按"相同"处理——
            // 否则旧方案里 null 基准元素会让每一行空值都被标成"已修改"。
            if (_elementType == typeof(string))
                return string.Equals(
                    text,
                    Convert.ToString(baselineValue, CultureInfo.InvariantCulture) ?? string.Empty,
                    StringComparison.Ordinal);

            object? converted;
            if (string.IsNullOrWhiteSpace(text))
            {
                converted = _elementType.IsValueType ? Activator.CreateInstance(_elementType) : null;
            }
            else if (!VisionMaster.Helpers.VariableValueConverter.TryConvert(text, _elementType, out converted, out _))
            {
                return false;
            }

            return Equals(converted, baselineValue);
        }

        private void ClearError()
        {
            if (string.IsNullOrEmpty(_errorText)) return;

            ErrorText = string.Empty;
            foreach (var row in Elements)
                row.HasError = false;
        }

        private void Confirm()
        {
            if (_model == null || _elementType == null)
            {
                ErrorText = "变量模型不可用，无法保存。";
                return;
            }

            var texts = Elements.Select(row => row.Value).ToList();
            var array = GlobalVariableManagerViewModel.TryBuildArray(
                _elementType, texts, out string error, out int errorIndex);

            if (array == null)
            {
                for (int i = 0; i < Elements.Count; i++)
                    Elements[i].HasError = i == errorIndex;

                ErrorText = error;
                return;
            }

            ErrorText = string.Empty;

            // 网络数组：整组写设备。长度必须等于地址配置的元素个数——
            // 短写/长写会覆盖邻居或留下旧值，不能在弹窗里"顺手改长度"。
            if (IsNetwork)
            {
                if (_isFlowRunning?.Invoke() == true)
                {
                    ErrorText = "流程已启动，本次写入未执行。请先停止流程——整组替换会覆盖运行步骤正在读的那组数据。";
                    return;
                }
                if (_model?.Value is not Array current || current.Length != RequiredLength)
                {
                    ErrorText = "当前值尚未读取或长度与配置不一致，请等轮询到完整值后再写入（避免把空数组写成 0）。";
                    return;
                }
                if (Elements.Count != RequiredLength)
                {
                    ErrorText = $"元素个数必须为 {RequiredLength}（设备点位跨度固定，不能增删）";
                    return;
                }
                if (_model is not NetworkVariableModel networkModel)
                {
                    ErrorText = "变量模型不可用，无法写入。";
                    return;
                }
                if (!networkModel.TryWriteToValue(array, out var writeError))
                {
                    ErrorText = writeError ?? "写入设备失败";
                    return;
                }

                RequestClose.Invoke(
                    new DialogParameters { { VariableKey, networkModel } },
                    ButtonResult.OK);
                return;
            }

            var result = new DialogParameters
            {
                { ArrayKey, array },
                { VariableKey, _model },
                { SyncBaselineKey, SyncBaseline },
            };

            RequestClose.Invoke(result, ButtonResult.OK);
        }

        /// <summary>元素类型的界面文案（"Int32" / "String"；可空类型标出来）</summary>
        private static string DescribeElementType(Type elementType)
        {
            var underlying = Nullable.GetUnderlyingType(elementType);
            return underlying != null ? $"{underlying.Name}?" : elementType.Name;
        }
    }
}
