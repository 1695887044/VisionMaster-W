using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using UI.CustomControl;
using VisionMaster.Communications;
using VisionMaster.Models;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>网络数组的写入范围：只写改过的项 / 整组替换</summary>
    public enum ArrayWriteMode
    {
        /// <summary>逐项下发（默认）：只写与打开时快照不同的元素，未改动的设备值不会被覆盖</summary>
        ChangedOnly,

        /// <summary>整组替换：全部元素一次下发（超单请求上限会失败；未改动的项也会被重写）</summary>
        WholeGroup,
    }

    /// <summary>
    /// 「编辑 / 写入数组」弹窗的一行。
    /// <para>编辑器只处理字符串，类型转换由 VM 在确认时统一做（与
    /// <see cref="GlobalVariableManagerViewModel.TryBuildArray"/> 同一份规则）。</para>
    /// </summary>
    public sealed class ArrayEditorRow : BindableBase
    {
        private string _indexText = string.Empty;
        private string _value = string.Empty;
        private string _baselineText = string.Empty;
        private string _baselineLabel = "基准：";
        private string _conversionText = "—";
        private bool _hasConversion;
        private bool _isNetworkMode;
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

        /// <summary>元素文本（工程值；网络数组带换算时也是工程值，写入时反算）</summary>
        public string Value
        {
            get => _value;
            set
            {
                if (SetProperty(ref _value, value ?? string.Empty))
                    ValueEdited?.Invoke(this);
            }
        }

        /// <summary>对照值文本：本地数组是基准（DefaultValue），网络数组是打开时的快照</summary>
        public string BaselineText
        {
            get => _baselineText;
            set => SetProperty(ref _baselineText, value);
        }

        /// <summary>对照值标签：本地「基准：」/ 网络「原值：」</summary>
        public string BaselineLabel
        {
            get => _baselineLabel;
            set => SetProperty(ref _baselineLabel, value);
        }

        /// <summary>换算文案（×0.1 +5 ℃ / —），只在网络数组行显示</summary>
        public string ConversionText
        {
            get => _conversionText;
            set => SetProperty(ref _conversionText, value);
        }

        /// <summary>该行是否配置了逐元素换算（图标高亮用）</summary>
        public bool HasConversion
        {
            get => _hasConversion;
            set => SetProperty(ref _hasConversion, value);
        }

        /// <summary>是否网络数组行（决定行高、对照值标签与「换算」列）</summary>
        public bool IsNetworkMode
        {
            get => _isNetworkMode;
            set => SetProperty(ref _isNetworkMode, value);
        }

        /// <summary>对照值里是否有这一项（本地：基准长度内；网络：快照长度内）</summary>
        public bool HasBaseline
        {
            get => _hasBaseline;
            set => SetProperty(ref _hasBaseline, value);
        }

        /// <summary>
        /// 该行是否与对照值不同：本地 = 与基准不同；网络 = 与打开时的快照不同。
        /// <para>界面据此显示「原值/基准」子行与行尾 ↺，并计入底部「已修改」。</para>
        /// </summary>
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
    /// 「编辑数组」/「写入数组」弹窗。
    ///
    /// <para><b>本地数组</b>（变量管理 → 本地数组根的「编辑数组…」）：</para>
    /// <list type="number">
    /// <item>装填当前值；基准是 DefaultValue，默认只写当前值；</item>
    /// <item>可增删/移动元素；校验在弹窗内，失败标红不关窗。</item>
    /// </list>
    ///
    /// <para><b>网络数组</b>（变量管理 → 网络数组根的「写入数组…」）：</para>
    /// <list type="number">
    /// <item><b>打开时快照</b>是「原值」：每行与它比较得到「已修改」，行尾 ↺ 撤销这一行；</item>
    /// <item><b>默认只写修改项</b>：逐项按元素地址下发（<c>TryWriteElement</c>），
    /// 未改动的设备值不会被覆盖；元素个数固定，不能增删/移动；</item>
    /// <item><b>整组写入</b>是显式选择：全部元素一次下发，确认前检查镜像冲突与单请求上限；</item>
    /// <item><b>逐元素工程量换算</b>：每个元素一份 Scale/Offset/单位（行内 Σ / 批量换算），
    /// 读路径由变量模型的 <c>ToEngineeringValue</c> 应用同一份档案。</item>
    /// </list>
    ///
    /// <para>为什么默认「只写修改项」：整组替换会把打开弹窗那一刻的镜像快照全部重写，
    /// 期间设备/别人改过的值会被静默覆盖；只写修改项把"用户意图"限制在他真正动过的下标上。</para>
    /// </summary>
    public class ArrayEditorViewModel : BindableBase, IDialogAware
    {
        /// <summary>弹窗注册名（与 App.xaml.cs 的 RegisterDialog 一致）</summary>
        public const string DialogName = "ArrayEditor";

        /// <summary>入参/回传键：被编辑的数组变量</summary>
        public const string VariableKey = "Variable";

        /// <summary>回传键：转换好的新数组（本地数组）</summary>
        public const string ArrayKey = "Array";

        /// <summary>回传键：是否同时写入初始值（基准）</summary>
        public const string SyncBaselineKey = "SyncBaseline";

        /// <summary>入参键：网络数组写入前复查"流程是否在运行"（弹窗打开后流程仍可能被定时/变量触发）</summary>
        public const string IsFlowRunningKey = "IsFlowRunning";

        private IVariable? _model;
        private Type? _elementType;
        private Func<bool>? _isFlowRunning;

        /// <summary>
        /// 对照值：本地 = DefaultValue（基准）；网络 = 打开时快照（原值）。
        /// <para>两者都用 <see cref="Array"/> 存：本地可能为 null（新建数组没有基准）；
        /// 网络总是有 N 个槽位（第一拍轮询前槽位是 null，用户填什么就算改什么）。</para>
        /// </summary>
        private Array? _baseline;

        /// <summary>网络数组的逐元素换算档案（编辑中的草稿；确认时才落回模型）</summary>
        private ElementConversion?[] _pendingConversions = System.Array.Empty<ElementConversion?>();

        /// <summary>打开时的换算档案（「撤销全部修改」与"换算是否改过"的对照）</summary>
        private ElementConversion?[] _originalConversions = System.Array.Empty<ElementConversion?>();

        private string _errorText = string.Empty;
        private string _infoText = string.Empty;
        private int _changedCount;
        private bool _conversionChanged;
        private bool _syncBaseline;
        private bool _isBusy;
        private string _busyText = string.Empty;
        private ArrayWriteMode _writeMode = ArrayWriteMode.ChangedOnly;

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
            WriteChangedOnlyCommand = new DelegateCommand(() => SetWriteMode(ArrayWriteMode.ChangedOnly));
            WriteWholeGroupCommand = new DelegateCommand(() => SetWriteMode(ArrayWriteMode.WholeGroup));
            RefreshCommand = new DelegateCommand(RefreshFromDevice, () => IsNetwork);
            EditConversionCommand = new DelegateCommand<object?>(EditConversion);
            BatchConversionCommand = new DelegateCommand(EditBatchConversion, () => IsNetwork);
            ClearConversionCommand = new DelegateCommand(ClearConversions, () => IsNetwork);
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

        /// <summary>网络数组显示"写入范围（只写修改项 / 整组写入）"面板</summary>
        public bool ShowWriteModeControls => IsNetwork;

        /// <summary>网络数组元素个数固定，不允许增删/移动</summary>
        public bool CanEditStructure => !IsNetwork;

        /// <summary>网络数组行显示「换算」列与行内 Σ</summary>
        public bool ShowConversionControls => IsNetwork;

        /// <summary>当前协议是否支持按元素地址逐项写入（Modbus/S7；MC/FINS 暂不支持）</summary>
        public bool SupportsElementWrite => NetworkModel?.SupportsElementWrite == true;

        /// <summary>本地数组才显示「基准」对照；网络数组显示「原值」</summary>
        public string ResetAllText => IsNetwork ? "撤销全部修改" : "恢复全部为基准";

        /// <summary>第三列标题：本地是「操作」，网络是「换算」</summary>
        public string OperationHeader => IsNetwork ? "换算" : "操作";

        /// <summary>确认按钮文案：写设备与编辑内存是两种后果，不能都叫「确定」</summary>
        public string ConfirmText => !IsNetwork
            ? "确定"
            : IsChangedOnly ? "写入修改项" : "整组写入";

        /// <summary>
        /// 网络数组必须能拿到一组可写的值才允许确认：
        /// <para>· 只写修改项：至少有一项改动（或换算改动），且协议支持按元素写；</para>
        /// <para>· 整组写入：镜像完整，或用户把全部项都填满了（空表直接确认会把整段设备数据写成 0）。</para>
        /// </summary>
        public bool CanConfirm
        {
            get
            {
                if (IsBusy || _model == null || _elementType == null) return false;
                if (!IsNetwork) return true;
                if (RequiredLength <= 0 || Elements.Count != RequiredLength) return false;

                if (IsChangedOnly)
                {
                    if (ChangedCount == 0 && !ConversionChanged) return false;
                    if (ChangedCount > 0 && !SupportsElementWrite) return false;
                    return true;
                }

                if (HasSizeWarning) return false;
                return HasCompleteMirror || AllRowsFilled;
            }
        }

        /// <summary>确认按钮为什么不可用（禁用按钮必须给理由，不能让人对着灰按钮猜）</summary>
        public string ConfirmHint
        {
            get
            {
                if (IsBusy) return BusyText;
                if (!IsNetwork) return string.Empty;
                if (RequiredLength <= 0) return "地址未配置元素个数，无法写入。";
                if (Elements.Count != RequiredLength)
                    return $"元素个数必须为 {RequiredLength}（设备点位跨度固定）。";

                if (IsChangedOnly)
                {
                    if (ChangedCount == 0 && !ConversionChanged)
                        return "没有修改项：只写改过的行（行尾显示「原值」）。";
                    if (ChangedCount > 0 && !SupportsElementWrite)
                        return "当前协议不支持按元素写入，请改用「整组写入」。";
                    return string.Empty;
                }

                if (HasSizeWarning) return SizeWarning;
                if (!HasCompleteMirror && !AllRowsFilled)
                    return "当前值未读取：请点「刷新」，或把全部项填满后整组写入。";
                return string.Empty;
            }
        }

        /// <summary>标题：网络数组带上连接名（同名变量挂在两条连接上时只靠变量名分不清）</summary>
        public string Title
        {
            get
            {
                string name = _model?.Name ?? string.Empty;
                if (!IsNetwork) return $"编辑数组 · {name}";

                string connection = NetworkModel?.ConnectionName ?? string.Empty;
                return string.IsNullOrWhiteSpace(connection)
                    ? $"写入数组 · {name}"
                    : $"写入数组 · {connection}.{name}";
            }
        }

        /// <summary>副标题：本地显示当前/基准项数；网络显示固定项数与设备地址</summary>
        public string Subtitle
        {
            get
            {
                if (_elementType == null) return string.Empty;
                if (!IsNetwork)
                    return $"{DescribeElementType(_elementType)} · 当前 {Elements.Count} 项 · 基准 {BaselineCount} 项";

                string address = NetworkModel?.AddressConfig?.Address ?? string.Empty;
                return string.IsNullOrWhiteSpace(address)
                    ? $"{DescribeElementType(_elementType)} · {Elements.Count} 项（设备点位固定）"
                    : $"{DescribeElementType(_elementType)} · {Elements.Count} 项 · {address}（设备点位固定）";
            }
        }

        private int BaselineCount => _baseline?.Length ?? 0;

        /// <summary>基准提示：与当前值等长时只说项数；不等长时明确标出来</summary>
        public string BaselineHint => BaselineCount == Elements.Count
            ? $"基准 {BaselineCount} 项"
            : $"基准 {BaselineCount} 项（长度不同）";

        /// <summary>写入范围的后果说明（随选择变化）</summary>
        public string ScopeHint
        {
            get
            {
                if (!IsNetwork)
                {
                    return SyncBaseline
                        ? "新数组会同时成为初始值；此后「恢复初始值」回到这组值。"
                        : "初始值（基准）保持不变；「恢复初始值」仍回到原基准。";
                }

                return IsChangedOnly
                    ? "只写入改过的项（逐项下发）；未改动的设备值不会被覆盖。"
                    : $"整组替换：{Elements.Count} 项全部下发；未修改的项也会按当前快照重写。";
            }
        }

        /// <summary>分段选择左段（本地：只写当前值）的选中态</summary>
        public bool IsCurrentOnly => !SyncBaseline;

        /// <summary>网络写入范围：只写修改项</summary>
        public bool IsChangedOnly => IsNetwork && _writeMode == ArrayWriteMode.ChangedOnly;

        /// <summary>网络写入范围：整组写入</summary>
        public bool IsWholeGroup => IsNetwork && _writeMode == ArrayWriteMode.WholeGroup;

        /// <summary>已修改项数（相对对照值；新增行没有对照值，不计入）</summary>
        public int ChangedCount
        {
            get => _changedCount;
            private set
            {
                if (!SetProperty(ref _changedCount, value)) return;
                RaisePropertyChanged(nameof(HasChanges));
                RaisePropertyChanged(nameof(HasPendingChanges));
                RaisePropertyChanged(nameof(ChangedSummary));
                RaisePropertyChanged(nameof(FooterSummary));
                RaisePropertyChanged(nameof(CanConfirm));
                RaisePropertyChanged(nameof(ConfirmHint));
            }
        }

        /// <summary>换算档案是否改过（只改换算、不改值，也是一次待落地的修改）</summary>
        public bool ConversionChanged
        {
            get => _conversionChanged;
            private set
            {
                if (!SetProperty(ref _conversionChanged, value)) return;
                RaisePropertyChanged(nameof(HasPendingChanges));
                RaisePropertyChanged(nameof(FooterSummary));
                RaisePropertyChanged(nameof(CanConfirm));
                RaisePropertyChanged(nameof(ConfirmHint));
            }
        }

        public bool HasChanges => _changedCount > 0;

        /// <summary>有值或换算待落地（「撤销全部」按钮的可用性）</summary>
        public bool HasPendingChanges => _changedCount > 0 || _conversionChanged;

        public string ChangedSummary => _changedCount > 0 ? $"{_changedCount} 项已修改" : "未修改";

        /// <summary>底部汇总：本地「N 项已修改」；网络「共 N 项 · 已修改 M 项」（不再重复显示总数）</summary>
        public string FooterSummary
        {
            get
            {
                if (!IsNetwork) return ChangedSummary;

                string summary = _changedCount > 0
                    ? $"共 {Elements.Count} 项 · 已改 {_changedCount} 项"
                    : $"共 {Elements.Count} 项 · 未改";
                return _conversionChanged ? summary + " · 换算改" : summary;
            }
        }

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

        /// <summary>非错误提示（粘贴了多少项、刷新完成、换算已更新…）</summary>
        public string InfoText
        {
            get => _infoText;
            private set
            {
                if (SetProperty(ref _infoText, value))
                    RaisePropertyChanged(nameof(HasInfo));
            }
        }

        public bool HasInfo => !string.IsNullOrEmpty(_infoText);

        /// <summary>质量预警：镜像不是 Good 时提醒"当前值是旧值/没读到"</summary>
        public string QualityWarning
        {
            get
            {
                if (NetworkModel is not { } nv) return string.Empty;
                return nv.Quality switch
                {
                    VariableQuality.Good => string.Empty,
                    VariableQuality.Uncertain => "当前值是上次读到的旧值（最近一次读失败），写入前建议先点「刷新」。",
                    VariableQuality.Bad => "尚未读取到设备值（断线或从未读到），请先点「刷新」。",
                    VariableQuality.Disabled => "该点位已被禁用。",
                    _ => string.Empty,
                };
            }
        }

        public bool HasQualityWarning => !string.IsNullOrEmpty(QualityWarning);

        /// <summary>整组写入的单请求上限预警（Modbus 120 寄存器 / S7 约 110 字节）</summary>
        public string SizeWarning => NetworkModel?.GetWholeGroupWriteWarning() ?? string.Empty;

        public bool HasSizeWarning => !string.IsNullOrEmpty(SizeWarning);

        /// <summary>写入中：禁用取消/确定，避免"写到一半关窗"</summary>
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (!SetProperty(ref _isBusy, value)) return;
                RaisePropertyChanged(nameof(CanCancel));
                RaisePropertyChanged(nameof(CanConfirm));
                RaisePropertyChanged(nameof(ConfirmHint));
            }
        }

        public bool CanCancel => !IsBusy;

        public string BusyText
        {
            get => _busyText;
            private set
            {
                if (SetProperty(ref _busyText, value))
                    RaisePropertyChanged(nameof(ConfirmHint));
            }
        }

        /// <summary>网络数组刷新（立刻读）可用；本地数组无设备可读</summary>
        public bool CanRefresh => IsNetwork;

        /// <summary>
        /// 最近一次确认操作产生的异步任务（断言宿主用；本地数组同步完成）。
        /// <para>ConfirmCommand 是同步命令，网络写入在后台跑；需要等待结果的调用方
        /// await 这个属性即可，不必改命令签名。</para>
        /// </summary>
        public Task? PendingWrite { get; private set; }

        public DelegateCommand AddCommand { get; }
        public DelegateCommand<object?> RemoveCommand { get; }
        public DelegateCommand<object?> MoveUpCommand { get; }
        public DelegateCommand<object?> MoveDownCommand { get; }
        public DelegateCommand<object?> ResetRowCommand { get; }
        public DelegateCommand ResetAllCommand { get; }
        public DelegateCommand WriteCurrentOnlyCommand { get; }
        public DelegateCommand WriteBothCommand { get; }
        public DelegateCommand WriteChangedOnlyCommand { get; }
        public DelegateCommand WriteWholeGroupCommand { get; }
        public DelegateCommand RefreshCommand { get; }
        public DelegateCommand<object?> EditConversionCommand { get; }
        public DelegateCommand BatchConversionCommand { get; }
        public DelegateCommand ClearConversionCommand { get; }
        public DelegateCommand ConfirmCommand { get; }
        public DelegateCommand CancelCommand { get; }

        public DialogCloseListener RequestClose { get; set; }

        public bool CanCloseDialog() => !IsBusy;

        public void OnDialogClosed() { }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            _model = parameters.TryGetValue<IVariable>(VariableKey, out var model) ? model : null;
            _elementType = _model?.DataType.GetElementType();
            _isFlowRunning = parameters.TryGetValue<Func<bool>>(IsFlowRunningKey, out var isRunning)
                ? isRunning
                : null;

            // 同一实例被复用时（单例注册 / 设计器）不能带着上次的勾选与错误条打开
            SyncBaseline = false;
            ErrorText = string.Empty;
            InfoText = string.Empty;
            IsBusy = false;
            BusyText = string.Empty;
            _writeMode = ArrayWriteMode.ChangedOnly;

            // MC/FINS 没有元素地址（TryCreateElementAddress 未实现）：默认落到整组写入，
            // 否则用户打开弹窗就停在一个不可用/不可确认的段上。
            if (IsNetwork && !SupportsElementWrite)
                _writeMode = ArrayWriteMode.WholeGroup;

            Elements.Clear();
            _pendingConversions = System.Array.Empty<ElementConversion?>();
            _originalConversions = System.Array.Empty<ElementConversion?>();
            _baseline = null;

            if (IsNetwork)
                OpenNetworkArray();
            else
                OpenLocalArray();

            RefreshRowState();
            RaiseAllStateChanged();
        }

        /// <summary>本地数组：装填当前值（不是基准）；基准用于逐行对照与恢复</summary>
        private void OpenLocalArray()
        {
            _baseline = _model?.DefaultValue as Array;

            // 只有当前值还不是数组（null）时才退回基准，避免空弹窗让人以为变量没数据。
            Array? source = _model?.Value as Array ?? _baseline;
            if (source != null)
            {
                foreach (object? item in source)
                    Elements.Add(NewRow(FormatEditable(item)));
            }
        }

        /// <summary>
        /// 网络数组：装填当前镜像并留一份「打开时快照」。
        /// <para>快照是脏标记与「原值」的对照基准，也是整组写入前冲突检查的依据。
        /// 第一拍轮询前镜像为空时也要摆出 N 行（槽位值为 null）：用户填什么就算改什么，
        /// 不再出现"确定按钮永久禁用且没有理由"。</para>
        /// </summary>
        private void OpenNetworkArray()
        {
            Array? source = _model?.Value as Array;
            int length = RequiredLength > 0 ? RequiredLength : source?.Length ?? 0;

            var snapshot = new object?[length];
            if (source != null)
            {
                int copy = Math.Min(length, source.Length);
                for (int i = 0; i < copy; i++)
                    snapshot[i] = source.GetValue(i);
            }
            _baseline = snapshot;

            for (int i = 0; i < length; i++)
                Elements.Add(NewRow(FormatEditable(i < (source?.Length ?? 0) ? source!.GetValue(i) : null)));

            if (NetworkModel is { } nv)
            {
                _originalConversions = NormalizeConversions(nv.ElementConversions, length);
                _pendingConversions = CloneConversions(_originalConversions);
            }
        }

        /// <summary>造一行（Value 先于 ValueEdited 赋值：构造期不会触发重算）</summary>
        private ArrayEditorRow NewRow(string value) => new()
        {
            Value = value,
            IsNetworkMode = IsNetwork,
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

        /// <summary>行内 ↺：把该行恢复为对照值（本地=基准，网络=打开时原值）</summary>
        private void ResetRow(object? parameter)
        {
            if (parameter is not ArrayEditorRow row) return;

            int index = Elements.IndexOf(row);
            if (index < 0) return;

            if (_baseline != null && index < _baseline.Length)
                row.Value = FormatEditable(_baseline.GetValue(index));
            else
                row.Value = string.Empty;

            RefreshRowState();
            ClearError();
        }

        /// <summary>「恢复全部为基准」/「撤销全部修改」：整张列表换回对照值，换算也一并撤销</summary>
        private void ResetAll()
        {
            if (_baseline != null)
            {
                Elements.Clear();
                foreach (object? item in _baseline)
                    Elements.Add(NewRow(FormatEditable(item)));
            }

            if (IsNetwork)
            {
                _pendingConversions = CloneConversions(_originalConversions);
                ConversionChanged = !ConversionsEqual(_pendingConversions, _originalConversions);
            }

            RefreshRowState();
            ClearError();
            InfoText = string.Empty;
        }

        private void SetWriteMode(ArrayWriteMode mode)
        {
            if (!IsNetwork || _writeMode == mode) return;

            _writeMode = mode;
            RaisePropertyChanged(nameof(IsChangedOnly));
            RaisePropertyChanged(nameof(IsWholeGroup));
            RaisePropertyChanged(nameof(ScopeHint));
            RaisePropertyChanged(nameof(ConfirmText));
            RaisePropertyChanged(nameof(CanConfirm));
            RaisePropertyChanged(nameof(ConfirmHint));
            ClearError();
        }

        /// <summary>重排下标 + 刷新上下移可用性 + 重算每行对照/已修改 + 刷新副标题</summary>
        private void RefreshRowState()
        {
            for (int i = 0; i < Elements.Count; i++)
            {
                var row = Elements[i];
                row.IndexText = $"[{i}]";
                row.CanMoveUp = i > 0;
                row.CanMoveDown = i < Elements.Count - 1;
                row.IsNetworkMode = IsNetwork;
                row.BaselineLabel = IsNetwork ? "原值：" : "基准：";

                bool hasBaseline = _baseline != null && i < _baseline.Length;
                row.HasBaseline = hasBaseline;
                object? baselineValue = hasBaseline ? _baseline!.GetValue(i) : null;
                row.BaselineText = hasBaseline ? FormatEditable(baselineValue) : string.Empty;
                row.IsChanged = hasBaseline && !RowEqualsBaseline(row.Value, baselineValue);

                if (IsNetwork)
                {
                    var conversion = i < _pendingConversions.Length ? _pendingConversions[i] : null;
                    row.ConversionText = conversion?.Describe() ?? "—";
                    row.HasConversion = conversion != null
                        && (!conversion.IsIdentity || !string.IsNullOrEmpty(conversion.Unit));
                }
                else
                {
                    row.ConversionText = "—";
                    row.HasConversion = false;
                }
            }

            ChangedCount = Elements.Count(row => row.IsChanged);
            ConversionChanged = IsNetwork && !ConversionsEqual(_pendingConversions, _originalConversions);
            RaisePropertyChanged(nameof(Subtitle));
            RaisePropertyChanged(nameof(BaselineHint));
            RaisePropertyChanged(nameof(FooterSummary));
            RaisePropertyChanged(nameof(CanConfirm));
            RaisePropertyChanged(nameof(ConfirmHint));
            RaisePropertyChanged(nameof(HasPendingChanges));
        }

        /// <summary>用户改了一行：只重算这一行的「已修改」与总数（不重建列表，保住焦点）</summary>
        private void OnRowEdited(ArrayEditorRow row)
        {
            int index = Elements.IndexOf(row);
            if (index >= 0 && _baseline != null && index < _baseline.Length)
            {
                object? baselineValue = _baseline.GetValue(index);
                row.HasBaseline = true;
                row.BaselineText = FormatEditable(baselineValue);
                row.IsChanged = !RowEqualsBaseline(row.Value, baselineValue);
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
        /// 行文本与对照值是否相等：按元素类型转换后比较（"12.50" 与 "12.5" 视为相同）。
        /// 转换失败 = 与对照值不同（错误行同时也会显示对照值，方便对照）。
        /// </summary>
        private bool RowEqualsBaseline(string text, object? baselineValue)
        {
            // 网络数组第一拍轮询前快照槽位是 null：空文本 = 没动过（不能因为"原值未知"就整列标红）
            if (baselineValue == null && string.IsNullOrWhiteSpace(text))
                return true;

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

            // 网络数组带换算时镜像是 double[]、元素类型可能是 int：直接 Equals(int 105, double 105.0)
            // 会判 false，导致"没改的行全被标成已修改"。数值按数值比（带极小容差），其余按 Equals。
            if (IsNumericValue(converted) && IsNumericValue(baselineValue))
            {
                double left = Convert.ToDouble(converted, CultureInfo.InvariantCulture);
                double right = Convert.ToDouble(baselineValue, CultureInfo.InvariantCulture);
                if (left.Equals(right)) return true;

                double tolerance = 1e-9 * Math.Max(1, Math.Max(Math.Abs(left), Math.Abs(right)));
                return Math.Abs(left - right) <= tolerance;
            }

            return Equals(converted, baselineValue);
        }

        private static bool IsNumericValue(object? value)
            => value is sbyte or byte or short or ushort or int or uint or long or ulong
                or float or double or decimal;

        private void ClearError()
        {
            if (string.IsNullOrEmpty(_errorText)) return;

            ErrorText = string.Empty;
            foreach (var row in Elements)
                row.HasError = false;
        }

        private void ClearInfo()
        {
            if (string.IsNullOrEmpty(_infoText)) return;
            InfoText = string.Empty;
        }

        /// <summary>网络数组：从设备立刻读一次并重建行/快照（有未落地修改时先拒绝，避免吞掉编辑）</summary>
        private void RefreshFromDevice()
        {
            if (NetworkModel is not { } nv) return;

            if (HasPendingChanges)
            {
                ErrorText = "有未写入的修改：请先「撤销全部修改」或写入后再刷新。";
                return;
            }

            if (!nv.TryReadNow(out _, out var error))
            {
                ErrorText = error ?? "读取设备失败。";
                return;
            }

            OpenNetworkArray();
            RefreshRowState();
            InfoText = "已从设备重新读取当前值。";
            ClearError();
            RaisePropertyChanged(nameof(QualityWarning));
            RaisePropertyChanged(nameof(HasQualityWarning));
        }

        /// <summary>行内 Σ：编辑该元素的换算（确认写入后生效）</summary>
        private void EditConversion(object? parameter)
        {
            if (!IsNetwork || parameter is not ArrayEditorRow row) return;

            int index = Elements.IndexOf(row);
            if (index < 0) return;

            if (Application.Current == null)
            {
                InfoText = "无界面上下文，无法打开换算编辑。";
                return;
            }

            var current = index < _pendingConversions.Length ? _pendingConversions[index] : null;
            var draft = ArrayConversionDraft.From(current);
            if (!EasyDialog.ShowPropertyGridSync($"第 {index} 项换算", draft)) return;

            if (!draft.TryBuild(out var conversion, out var error))
            {
                ErrorText = error;
                return;
            }

            _pendingConversions[index] = conversion;
            RefreshRowState();
            InfoText = $"第 {index} 项换算已更新（确认写入后生效）。";
            ClearError();
        }

        /// <summary>批量换算：同一份档案应用到全部元素（个别项再用行内 Σ 调整）</summary>
        private void EditBatchConversion()
        {
            if (!IsNetwork) return;

            if (Application.Current == null)
            {
                InfoText = "无界面上下文，无法打开换算编辑。";
                return;
            }

            var draft = ArrayConversionDraft.From(null);
            if (!EasyDialog.ShowPropertyGridSync("整组换算（应用到全部元素）", draft)) return;

            if (!draft.TryBuild(out var conversion, out var error))
            {
                ErrorText = error;
                return;
            }

            for (int i = 0; i < _pendingConversions.Length; i++)
                _pendingConversions[i] = conversion?.Clone();

            RefreshRowState();
            InfoText = conversion == null
                ? "已清除全部元素的换算。"
                : $"已把 {conversion.Describe()} 应用到全部 {_pendingConversions.Length} 项。";
            ClearError();
        }

        /// <summary>清除全部逐元素换算（确认后落回模型）</summary>
        private void ClearConversions()
        {
            if (!IsNetwork) return;

            for (int i = 0; i < _pendingConversions.Length; i++)
                _pendingConversions[i] = null;

            RefreshRowState();
            InfoText = "已清除全部元素的换算。";
            ClearError();
        }

        /// <summary>
        /// 多行粘贴：每行一个元素（Excel 复制的一列；制表符后只取第一格）。
        /// <para>从 <paramref name="startRow"/> 开始铺；本地数组行数不够时自动补行，
        /// 网络数组长度固定，多出的行忽略并在提示里说明。</para>
        /// </summary>
        public int Paste(string? text, ArrayEditorRow? startRow = null)
        {
            if (string.IsNullOrEmpty(text)) return 0;

            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int start = startRow != null ? Elements.IndexOf(startRow) : 0;
            if (start < 0) start = 0;

            int filled = 0;
            int ignored = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string cell = lines[i];
                int tab = cell.IndexOf('\t');
                if (tab >= 0) cell = cell.Substring(0, tab);

                int index = start + i;
                if (index >= Elements.Count)
                {
                    if (IsNetwork)
                    {
                        ignored++;
                        continue;
                    }

                    // 本地数组：粘贴多少行就补多少行（最多到 10000 行，防止误粘一个巨大表格）
                    if (Elements.Count >= 10000) { ignored++; continue; }
                    Elements.Add(NewRow(string.Empty));
                }

                Elements[index].Value = cell.Trim();
                filled++;
            }

            RefreshRowState();
            ClearError();

            if (ignored > 0)
                InfoText = $"已粘贴 {filled} 项；多出的 {ignored} 行未使用（网络数组长度由设备点位固定）。";
            else
                InfoText = $"已粘贴 {filled} 项。";

            return filled;
        }

        private void Confirm()
        {
            PendingWrite = ConfirmAsync();
        }

        private async Task ConfirmAsync()
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

            // 网络数组：写设备。长度必须等于地址配置的元素个数——
            // 短写/长写会覆盖邻居或留下旧值，不能在弹窗里"顺手改长度"。
            if (IsNetwork)
            {
                await ConfirmNetworkAsync(array);
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

        private async Task ConfirmNetworkAsync(Array array)
        {
            if (_model is not NetworkVariableModel networkModel)
            {
                ErrorText = "变量模型不可用，无法写入。";
                return;
            }

            if (_isFlowRunning?.Invoke() == true)
            {
                ErrorText = "流程已启动，本次写入未执行。请先停止流程——整组替换会覆盖运行步骤正在读的那组数据。";
                return;
            }

            if (Elements.Count != RequiredLength)
            {
                ErrorText = $"元素个数必须为 {RequiredLength}（设备点位跨度固定，不能增删）";
                return;
            }

            var pending = BuildPendingConversions();

            if (IsChangedOnly)
            {
                var dirty = BuildDirtyIndices();

                // 只改了换算、没有改值：不需要写设备，只把换算档案落回模型
                if (dirty.Count == 0)
                {
                    if (!ConversionChanged)
                    {
                        ErrorText = "没有修改项：只写改过的行（行尾显示「原值」）。";
                        return;
                    }

                    networkModel.ElementConversions = pending;
                    RequestClose.Invoke(
                        new DialogParameters { { VariableKey, networkModel } },
                        ButtonResult.OK);
                    return;
                }

                if (!networkModel.SupportsElementWrite)
                {
                    ErrorText = "当前协议不支持按元素写入，请改用「整组写入」。";
                    return;
                }

                await WriteChangedElementsAsync(networkModel, pending, dirty);
                return;
            }

            // 整组写入：先查镜像冲突与长度（防止把过期快照/空数组写下去）
            if (HasSizeWarning)
            {
                ErrorText = SizeWarning;
                return;
            }

            // 镜像完整时做冲突检查；镜像没到但用户把全部项填满时，写入的就是用户显式给的这组值
            if (_model.Value is Array current
                && current.Length == RequiredLength
                && HasMirrorConflict(current))
            {
                ErrorText = "设备值在编辑期间已变化，整组写入会覆盖这些新值。请点「刷新」后重新确认，或改用「只写修改项」。";
                return;
            }

            await WriteWholeGroupAsync(networkModel, array, pending);
        }

        /// <summary>逐项写入改过的元素（每项一个元素地址，不读改写、不碰邻居）</summary>
        private async Task WriteChangedElementsAsync(
            NetworkVariableModel networkModel, ElementConversion?[] pending, IReadOnlyList<int> dirty)
        {
            // 后台线程只读快照：值先拷出来，避免 UI 线程/集合在写入期间变化
            var writes = dirty
                .Select(index => (Index: index, Text: Elements[index].Value))
                .ToList();

            var previous = networkModel.ElementConversions;
            networkModel.ElementConversions = pending;

            IsBusy = true;
            BusyText = $"正在写入 {writes.Count} 项…";
            var failures = new List<(int Index, string Error)>();
            int written = 0;

            try
            {
                await Task.Run(() =>
                {
                    foreach (var (index, text) in writes)
                    {
                        if (networkModel.TryWriteElement(index, text, out var writeError))
                        {
                            written++;
                        }
                        else
                        {
                            failures.Add((index, writeError ?? "写入设备失败"));
                        }
                    }
                });
            }
            finally
            {
                IsBusy = false;
                BusyText = string.Empty;
            }

            if (failures.Count == 0)
            {
                RequestClose.Invoke(
                    new DialogParameters { { VariableKey, networkModel } },
                    ButtonResult.OK);
                return;
            }

            // 全失败：换算档案回滚（一项都没写下去，配置不该留在模型上）；
            // 部分成功：保留新档案（已写下去的值就是按它换算的），并把成功项的原值推进到新值，
            // 让「已修改」只剩失败项，重试时不会把成功的项再写一遍。
            if (written == 0)
                networkModel.ElementConversions = previous;

            foreach (var (index, text) in writes)
            {
                if (failures.Any(f => f.Index == index)) continue;
                if (_baseline != null && index < _baseline.Length)
                    _baseline.SetValue(ParseRowValue(text), index);
            }

            foreach (var failure in failures)
            {
                if (failure.Index >= 0 && failure.Index < Elements.Count)
                    Elements[failure.Index].HasError = true;
            }

            RefreshRowState();
            string indices = string.Join("、", failures.Select(f => f.Index));
            string firstError = failures[0].Error;
            ErrorText = $"第 {indices} 项写入失败：{firstError}" + (failures.Count > 1 ? $"（共 {failures.Count} 项失败）" : string.Empty);
            InfoText = written > 0 ? $"已成功写入 {written} 项；失败项可修正后重试。" : string.Empty;
        }

        /// <summary>整组写入（全部元素一次下发；超限/冲突已在调用前拦住）</summary>
        private async Task WriteWholeGroupAsync(
            NetworkVariableModel networkModel, Array array, ElementConversion?[] pending)
        {
            var previous = networkModel.ElementConversions;
            networkModel.ElementConversions = pending;

            IsBusy = true;
            BusyText = $"正在整组写入 {Elements.Count} 项…";
            bool ok;
            string? writeError;
            try
            {
                (ok, writeError) = await Task.Run(() =>
                {
                    bool result = networkModel.TryWriteToValue(array, out var error);
                    return (result, error);
                });
            }
            finally
            {
                IsBusy = false;
                BusyText = string.Empty;
            }

            if (!ok)
            {
                networkModel.ElementConversions = previous;
                ErrorText = writeError ?? "写入设备失败";
                return;
            }

            RequestClose.Invoke(
                new DialogParameters { { VariableKey, networkModel } },
                ButtonResult.OK);
        }

        /// <summary>改过的下标（相对打开时快照；第一拍轮询前快照为 null，用户填了值即算改）</summary>
        private List<int> BuildDirtyIndices()
        {
            var dirty = new List<int>();
            for (int i = 0; i < Elements.Count; i++)
            {
                if (Elements[i].IsChanged)
                    dirty.Add(i);
            }
            return dirty;
        }

        /// <summary>把编辑中的换算草稿拷成模型档案（长度 = 元素个数；全 identity 时保留对象以便显示单位）</summary>
        private ElementConversion?[] BuildPendingConversions()
        {
            var result = new ElementConversion?[Elements.Count];
            for (int i = 0; i < result.Length; i++)
            {
                var conversion = i < _pendingConversions.Length ? _pendingConversions[i] : null;
                result[i] = conversion?.Clone();
            }
            return result;
        }

        /// <summary>整组写入前的镜像冲突检查：当前镜像与打开时快照逐项比较</summary>
        private bool HasMirrorConflict(Array current)
        {
            if (_baseline == null || current.Length != _baseline.Length) return false;

            for (int i = 0; i < current.Length; i++)
            {
                if (!ValuesEqual(current.GetValue(i), _baseline.GetValue(i)))
                    return true;
            }
            return false;
        }

        private static bool ValuesEqual(object? left, object? right)
        {
            if (left == null || right == null) return left == null && right == null;
            if (Equals(left, right)) return true;

            try
            {
                return Convert.ToDouble(left, CultureInfo.InvariantCulture)
                    .Equals(Convert.ToDouble(right, CultureInfo.InvariantCulture));
            }
            catch
            {
                return string.Equals(
                    Convert.ToString(left, CultureInfo.InvariantCulture),
                    Convert.ToString(right, CultureInfo.InvariantCulture),
                    StringComparison.Ordinal);
            }
        }

        private bool HasCompleteMirror
            => _model?.Value is Array current && current.Length == RequiredLength;

        /// <summary>
        /// 整组写入的兜底：镜像没到时，用户把每一项都填满也能写。
        /// <para>字符串数组不适用：空串是合法值，"没读到"与"故意清空"无法区分，
        /// 这种类型必须等镜像完整（点「刷新」）后才能整组写。</para>
        /// </summary>
        private bool AllRowsFilled
        {
            get
            {
                if (Elements.Count == 0) return false;
                if (_elementType == typeof(string)) return false;
                return Elements.All(row => !string.IsNullOrWhiteSpace(row.Value));
            }
        }

        private static ElementConversion?[] NormalizeConversions(ElementConversion?[]? source, int length)
        {
            var result = new ElementConversion?[length];
            if (source == null) return result;

            int copy = Math.Min(length, source.Length);
            for (int i = 0; i < copy; i++)
                result[i] = source[i]?.Clone();
            return result;
        }

        private static ElementConversion?[] CloneConversions(ElementConversion?[] source)
        {
            var result = new ElementConversion?[source.Length];
            for (int i = 0; i < source.Length; i++)
                result[i] = source[i]?.Clone();
            return result;
        }

        private static bool ConversionsEqual(ElementConversion?[] left, ElementConversion?[] right)
        {
            if (left.Length != right.Length) return false;

            for (int i = 0; i < left.Length; i++)
            {
                var a = left[i];
                var b = right[i];
                if (a == null || b == null)
                {
                    if (a != null || b != null) return false;
                    continue;
                }

                if (a.Scale != b.Scale || a.Offset != b.Offset
                    || a.DecimalPlaces != b.DecimalPlaces
                    || !string.Equals(a.Unit ?? string.Empty, b.Unit ?? string.Empty, StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        private void RaiseAllStateChanged()
        {
            RaisePropertyChanged(nameof(Title));
            RaisePropertyChanged(nameof(Subtitle));
            RaisePropertyChanged(nameof(BaselineHint));
            RaisePropertyChanged(nameof(ScopeHint));
            RaisePropertyChanged(nameof(ShowBaselineControls));
            RaisePropertyChanged(nameof(ShowWriteModeControls));
            RaisePropertyChanged(nameof(CanEditStructure));
            RaisePropertyChanged(nameof(ShowConversionControls));
            RaisePropertyChanged(nameof(SupportsElementWrite));
            RaisePropertyChanged(nameof(IsChangedOnly));
            RaisePropertyChanged(nameof(IsWholeGroup));
            RaisePropertyChanged(nameof(OperationHeader));
            RaisePropertyChanged(nameof(ConfirmText));
            RaisePropertyChanged(nameof(ResetAllText));
            RaisePropertyChanged(nameof(CanConfirm));
            RaisePropertyChanged(nameof(ConfirmHint));
            RaisePropertyChanged(nameof(CanRefresh));
            RaisePropertyChanged(nameof(CanCancel));
            RaisePropertyChanged(nameof(QualityWarning));
            RaisePropertyChanged(nameof(HasQualityWarning));
            RaisePropertyChanged(nameof(SizeWarning));
            RaisePropertyChanged(nameof(HasSizeWarning));
            RaisePropertyChanged(nameof(FooterSummary));
            RaisePropertyChanged(nameof(HasPendingChanges));
            RaisePropertyChanged(nameof(IsNetwork));
            RefreshCommand.RaiseCanExecuteChanged();
            BatchConversionCommand.RaiseCanExecuteChanged();
            ClearConversionCommand.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// 元素值转可编辑文本：数值不用 F2（"105.00" 虽然能解析，但每行都带尾零很吵），
        /// 用不变文化 + 最多 12 位小数，保证 round-trip 不丢精度。
        /// </summary>
        private static string FormatEditable(object? value)
        {
            return value switch
            {
                null => string.Empty,
                double d => d.ToString("0.############", CultureInfo.InvariantCulture),
                float f => f.ToString("0.############", CultureInfo.InvariantCulture),
                decimal m => m.ToString("0.############", CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty,
            };
        }

        /// <summary>
        /// 把行文本解析回元素类型（推进「原值」用）。
        /// <para>不能直接拿字符串当对照值：<c>RowEqualsBaseline</c> 对数值元素做数值比较，
        /// 字符串 "25" 与 int 25 会被判成不同 → 已成功写入的行仍显示「已修改」，重试时会重复写。</para>
        /// </summary>
        private object? ParseRowValue(string text)
        {
            if (_elementType == null) return text;
            if (_elementType == typeof(string)) return text;
            if (string.IsNullOrWhiteSpace(text))
                return _elementType.IsValueType ? Activator.CreateInstance(_elementType) : null;

            return VisionMaster.Helpers.VariableValueConverter.TryConvert(text, _elementType, out var converted, out _)
                ? converted
                : text;
        }

        /// <summary>元素类型的界面文案（"Int32" / "String"；可空类型标出来）</summary>
        private static string DescribeElementType(Type elementType)
        {
            var underlying = Nullable.GetUnderlyingType(elementType);
            return underlying != null ? $"{underlying.Name}?" : elementType.Name;
        }
    }
}
