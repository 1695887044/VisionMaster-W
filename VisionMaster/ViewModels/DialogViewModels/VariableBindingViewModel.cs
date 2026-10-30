using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Core.Interfaces;
using Prism.Dialogs;
using UI.Converters;
using UI.CustomControl;
using VisionMaster.Helpers;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels
{
    public class VariableBindingViewModel : BindableBase, IDialogAware
    {
        private PortDefinition _lastBoundPort;
        private readonly IPluginProvider pluginProvider;
        private LinkReference _lastBoundLink;
        private bool _isSingleBindMode = false;

        /// <summary>
        /// 元素类型友好名的唯一来源与视图里的类型标签一致（如「小数 (Double)」），
        /// 避免索引面板自造一套叫法。
        /// </summary>
        private static readonly TypeNameToFriendlyNameConverter FriendlyNameConverter = new();

        public string Title => "变量绑定";
        public IWorkspaceManager Workspace { get; init; }

        /// <summary>
        /// 本次绑定的目标步骤 —— 候选树的"我在给谁连线"锚点，也是回写 LinkedSources 的对象。
        ///
        /// 取值优先级：弹窗参数 TargetStep 显式传入 > Workspace.CurrentStep。
        /// 为什么不能只靠 CurrentStep：下钻进容器语义下它指向的是外层容器而不是被双击的步骤
        /// （画布上双击 For 里的节点，CurrentStep 仍是那个 For），
        /// 只认它就永远算不出"For/While 子层里某步骤"的上游候选。
        /// 老调用方不传 TargetStep 时行为与原来完全一致。
        /// </summary>
        private StepModel _targetStep;

        public ObservableCollection<InputPortUIModel> DisplayDataPort { get; } = new();
        public ObservableCollection<ToolItemModel> TreeNodes { get; } = new();

        public ObservableCollection<PresetOptionItem> PresetOptions { get; } = new();

        private string _constantValue;
        public string ConstantValue
        {
            get => _constantValue;
            set
            {
                if (SetProperty(ref _constantValue, value))
                    RaisePropertyChanged(nameof(SelectedPresetDisplay));
            }
        }

        private bool _hasFunctionalEnumPort;
        public bool HasFunctionalEnumPort
        {
            get => _hasFunctionalEnumPort;
            set => SetProperty(ref _hasFunctionalEnumPort, value);
        }

        /// <summary>
        /// 封闭集合端口「当前没有候选」时的原因说明（2026-10-24 口径）。
        ///
        /// 空字符串 = 有候选、没话说。非空时视图把它显示在预设列表下方，
        /// 并禁用常量框 —— 名字类字段不再允许手填（运行期拿值当名字一定解析不到）。
        /// 文案分两档：程序缺陷（没登记来源 / 来源抛异常）与业务空态（有来源但方案里还没有）。
        /// </summary>
        private string _optionEmptyReason = string.Empty;
        public string OptionEmptyReason
        {
            get => _optionEmptyReason;
            set
            {
                if (SetProperty(ref _optionEmptyReason, value))
                    RaisePropertyChanged(nameof(IsOptionEmpty));
            }
        }

        /// <summary>该端口的候选取不到（视图据此禁用常量框并显示原因）。</summary>
        public bool IsOptionEmpty => !string.IsNullOrEmpty(_optionEmptyReason);

        private static string DescribeEmptyReason(int itemCount)
        {
            // 走到这里说明 GetOptionItems 给了空列表；具体是"没登记来源"还是"登记了但没配"，
            // 由 StepConfigOptionSource.GetOptionsResult 才分得出。这里只给一句通用可读的说明，
            // 避免在视图层再复制一份三档判据。
            return itemCount > 0 ? string.Empty
                : "当前方案里还没有可用的候选项：先到对应的设备设置里配好，再回到这里选择（不支持手填）。";
        }

        // ── 绑定进度（顶部上下文条 / 列脚「已绑定 M / N」） ──

        /// <summary>当前步骤的输入端口总数。</summary>
        public int TotalPortCount => DisplayDataPort.Count;

        /// <summary>当前步骤已绑定的输入端口数（LinkedAddress 非空即算已绑定）。</summary>
        public int BoundPortCount => DisplayDataPort.Count(p => p.IsBound);

        // ── 右栏「还有 N 个端口因类型不兼容被隐藏」 ──

        private int _hiddenByTypeCount;

        /// <summary>
        /// 当前来源下被类型过滤隐藏的输出端口数。
        /// 注意：与「显示全部」无关——勾选显示全部时这些端口会以置灰形式出现，
        /// 但计数仍表示"默认视图看不到的那批"，否则提示条会随勾选忽隐忽现。
        /// </summary>
        public int HiddenByTypeCount => _hiddenByTypeCount;

        // ── 数组 → 标量：行内索引面板 ──

        private PortDefinition? _pendingArrayPort;

        /// <summary>正在等待输入下标的数组输出端口；非空即表示行内索引面板展开。</summary>
        public PortDefinition? PendingArrayPort
        {
            get => _pendingArrayPort;
            private set
            {
                if (SetProperty(ref _pendingArrayPort, value))
                    RaisePropertyChanged(nameof(HasPendingArrayBind));
            }
        }

        private string _pendingArrayElementTypeName = string.Empty;

        /// <summary>待取元素的类型友好名（如「小数 (Double)」），仅供索引面板显示。</summary>
        public string PendingArrayElementTypeName
        {
            get => _pendingArrayElementTypeName;
            private set => SetProperty(ref _pendingArrayElementTypeName, value);
        }

        private string _arrayIndexText = string.Empty;

        /// <summary>行内索引输入框的文本；确认时按非负整数解析。</summary>
        public string ArrayIndexText
        {
            get => _arrayIndexText;
            set => SetProperty(ref _arrayIndexText, value);
        }

        /// <summary>行内索引面板是否展开。</summary>
        public bool HasPendingArrayBind => PendingArrayPort != null;

        // ── 绑定方式：上游端口 / 常量值（显式二选一） ──

        private bool _isConstantMode;

        /// <summary>
        /// true = 右栏按「常量值」编辑；false = 按「上游端口」编辑。
        /// 关掉常量模式必须清空 ConstantValue —— Confirm 的规则是"常量框非空就写常量"，
        /// 不清空会让"上游模式下点确定"把残留常量写进去（静默拆掉上游连线）。
        /// </summary>
        public bool IsConstantMode
        {
            get => _isConstantMode;
            set
            {
                if (!SetProperty(ref _isConstantMode, value))
                    return;

                if (!value)
                {
                    ConstantValue = string.Empty;
                }
                else if (GetExistingConstantLink() is { } constantLink)
                {
                    // 只在真有常量连线时回显。无条件回填会把 UpdatePresetOptions 刚填的
                    // 枚举首项冲掉（用户看到的就不是实际生效的值了）。
                    ConstantValue = ExtractConstantText(constantLink);
                }

                RaisePropertyChanged(nameof(IsUpstreamMode));
            }
        }

        /// <summary>绑定方式分段控件另一侧（IsConstantMode 的反值）。</summary>
        public bool IsUpstreamMode
        {
            get => !IsConstantMode;
            set => IsConstantMode = !value;
        }

        // ── 单绑定模式 / 当前步骤 ──

        /// <summary>单绑定模式：只回传 BoundLink，不写活模型（目标可能是条件节点）。</summary>
        public bool IsSingleBindMode
        {
            get => _isSingleBindMode;
            private set => SetProperty(ref _isSingleBindMode, value);
        }

        private bool _allowGlobals = true;
        /// <summary>
        /// 是否允许全局/系统变量进入候选（默认 true，兼容既有行为）。
        /// C#脚本的输入只接上游数据流 → 传 false，弹窗里「全局变量」节点整块隐藏。
        /// </summary>
        public bool AllowGlobals
        {
            get => _allowGlobals;
            private set => SetProperty(ref _allowGlobals, value);
        }

        private bool _pickNameMode;
        /// <summary>
        /// 名字选择模式：调用方只要「名字 + 身份」，不建立连线。
        /// 候选会裁掉"上游端口"（其 TargetPortName 是端口名，不是变量名）。
        /// </summary>
        public bool PickNameMode
        {
            get => _pickNameMode;
            private set => SetProperty(ref _pickNameMode, value);
        }

        private bool _allowStepPorts = true;
        /// <summary>是否允许上游步骤输出端口进入候选（名字选择模式恒为 false）。</summary>
        public bool AllowStepPorts
        {
            get => _allowStepPorts;
            private set => SetProperty(ref _allowStepPorts, value);
        }

        private string _scopeFilter = string.Empty;
        /// <summary>
        /// 名字选择模式下的作用域过滤（<c>"Global"</c> / <c>"Runtime"</c>；空 = 不过滤）。
        ///
        /// 为什么需要：全局行打开弹窗时会看到一个空的「运行时变量」组，运行时行则要在一万条
        /// 全局变量里翻——两种都是"看起来能选，其实选了就废"。作用域由调用方明确指定。
        /// 只在 <see cref="PickNameMode"/> 下生效；其他模式（正常绑定）完全不受影响。
        /// </summary>
        public string ScopeFilter
        {
            get => _scopeFilter;
            private set => SetProperty(ref _scopeFilter, value ?? string.Empty);
        }

        private bool _allowFreeText;
        /// <summary>
        /// 名字选择模式下的「手填变量名」出口（运行时变量是开集，R190 的唯一例外）。
        ///
        /// 全局变量是闭集（必须先在变量管理里存在），只在运行时作用域为 true。
        /// 手填产出的仍是 <see cref="LinkReference"/>（<c>Kind=RuntimeVariable</c>），
        /// 但调用方只取 <c>TargetPortName</c>，**不写 LinkedSources**。
        /// </summary>
        public bool AllowFreeText
        {
            get => _allowFreeText;
            private set => SetProperty(ref _allowFreeText, value);
        }

        private string _freeTextName = string.Empty;
        /// <summary>「手填变量名」输入框的内容。</summary>
        public string FreeTextName
        {
            get => _freeTextName;
            set
            {
                if (SetProperty(ref _freeTextName, value ?? string.Empty))
                    RaisePropertyChanged(nameof(CanUseFreeText));
            }
        }

        /// <summary>手填名字非空才允许确认。</summary>
        public bool CanUseFreeText => AllowFreeText && !string.IsNullOrWhiteSpace(FreeTextName);

        /// <summary>
        /// 确认使用手填的变量名：产出 <c>Kind=RuntimeVariable</c> 的连线引用。
        ///
        /// 运行期 <c>RuntimeVariableProxyPort</c> 本来就是"按名从 LocalVariables 取"，
        /// 所以这条引用不需要落盘成端口——名字选择模式的调用方只取名字与（空的）Id。
        /// </summary>
        public DelegateCommand UseFreeTextCommand { get; set; }

        private string _targetStepName = "当前步骤";
        /// <summary>顶部上下文条显示的当前步骤名（单绑定模式固定为「插件视图绑定请求」）。</summary>
        public string TargetStepName
        {
            get => _targetStepName;
            private set => SetProperty(ref _targetStepName, value);
        }

        /// <summary>枚举端口当前选中预设的显示名；没有选中预设时回退到 ConstantValue。</summary>
        public string SelectedPresetDisplay =>
            PresetOptions.FirstOrDefault(p => p.IsSelected)?.Display ?? ConstantValue;

        public DelegateCommand ConfirmCommand { get; set; }
        public DelegateCommand CancelCommand { get; set; }
        public DelegateCommand<string> SelectPresetCommand { get; set; }

        public DelegateCommand<InputPortUIModel> UnbindCommand =>
            new(port =>
            {
                if (port == null || IsSingleBindMode)
                    return;
                // 必须走 RemoveLink（内部 RaisePropertyChanged(LinkedSources)）而不是直改字典：
                // FlowModel 的版本递增唯一来源是步骤 PropertyChanged，裸写字典会让"解绑"看不见 →
                // 已编译会话继续用旧连线跑（静默错结果）
                // 键口径必须与 DoFinalBind / Confirm 一致：条件节点与等待节点用端口 Description（变量 Guid），
                // 其余用端口名。用 Definition.Name 会让这两类节点"界面上清掉了、模型里还留着"。
                string bindKey = ResolveBindKey(port.Definition);
                _targetStep?.RemoveLink(bindKey);
                // 只删连线不删 InputValues 会留下"界面未绑定、运行期却回落到旧手填值"的幽灵值。
                // 清除绑定 = 清掉这个端口的全部来源（连线 + 手填值），与 UI 语义一致。
                _targetStep?.RemoveInputValue(bindKey);
                port.LinkedAddress = null;
                _lastBoundLink = null;
                _lastBoundPort = null;
                ConstantValue = string.Empty;
            });

        public DelegateCommand<PortDefinition> DoublockClickBindCommand { get; set; }

        /// <summary>行内索引面板的「确认绑定」。</summary>
        public DelegateCommand ConfirmArrayBindCommand { get; set; }

        /// <summary>行内索引面板的「取消」（不写模型）。</summary>
        public DelegateCommand CancelArrayBindCommand { get; set; }

        private ObservableCollection<PortDefinition> _displayPorts = new();
        public ObservableCollection<PortDefinition> DisplayPorts
        {
            get => _displayPorts;
            set => SetProperty(ref _displayPorts, value);
        }

        private InputPortUIModel _selectedInputPort;
        public InputPortUIModel SelectedInputPort
        {
            get => _selectedInputPort;
            set
            {
                // 切端口必须先收起索引面板：旧端口的下标面板若留着，
                // 用户会以为它还是给新端口准备的（确认后绑到错误的数组端口上）
                ClearPendingArray();
                SetProperty(ref _selectedInputPort, value);
                LoadExistingConstant();
                UpdatePresetOptions();
                // 绑定方式跟着端口自身的值走：有常量（连线/手填）或是功能枚举端口 → 常量模式；
                // 否则是上游模式。必须放在 LoadExistingConstant/UpdatePresetOptions 之后，
                // 否则它们刚填进来的值会被这里提前判成"空"。
                IsConstantMode = !string.IsNullOrWhiteSpace(ConstantValue)
                    || value?.Definition?.IsFunctionalEnum == true;
                // 换端口必须重算候选：右侧列表是按"当前端口的类型"过滤出来的，
                // 不重算就会把上一个端口的过滤结果带过来（类型不符的项照样能双击）
                RefreshDisplayPorts();
            }
        }

        private PortDefinition _selectedOutputPort;
        public PortDefinition SelectedOutputPort
        {
            get => _selectedOutputPort;
            set
            {
                // 换候选时收起数组索引面板：面板里的 PendingArrayPort 属于上一个候选，
                // 留着会让「确认绑定」把旧数组端口写进当前选中的目标。
                if (SetProperty(ref _selectedOutputPort, value))
                    ClearPendingArray();
            }
        }

        private ToolItemModel _selectedNode;
        public ToolItemModel SelectedNode
        {
            get => _selectedNode;
            set
            {
                // 切来源同样收起索引面板：面板里的 port 属于上一个来源，
                // 留着会让"确认"把旧来源的端口名写进新来源的连线
                ClearPendingArray();
                SetProperty(ref _selectedNode, value);
                RefreshDisplayPorts();
            }
        }

        /// <summary>
        /// 是否"显示全部候选"（不看类型）。默认 false = 只列能绑上的。
        ///
        /// 为什么要留这个开关而不是一律隐藏不兼容项：
        /// ① 目标端口是 object/string 时几乎全都兼容，过滤本来就拦不住；
        /// ② 用户明明记得上游有某个变量却找不到，会以为是插件没暴露或流程没跑——
        ///    "看得见但灰着"比"凭空消失"更好排查；
        /// ③ 故意把任意类型塞进 string 端口（用于上报/显示）是合法用法。
        /// </summary>
        private bool _showAllPorts;
        public bool ShowAllPorts
        {
            get => _showAllPorts;
            set
            {
                if (SetProperty(ref _showAllPorts, value)) RefreshDisplayPorts();
            }
        }

        /// <summary>
        /// 按「当前选中的输入端口」的类型重算右侧候选列表。
        ///
        /// 触发点有两个：换上游节点、换左侧输入端口——两处都必须重算。
        /// 早先只在换节点时算，加了类型过滤之后就会把"A 端口的过滤结果"残留到 B 端口上。
        /// </summary>
        private void RefreshDisplayPorts()
        {
            var source = _selectedNode?.OutputDefinitions;
            if (source == null || source.Count == 0)
            {
                DisplayPorts = new ObservableCollection<PortDefinition>();
                UpdateHiddenByTypeCount(0);
                return;
            }

            // 目标类型取不到就按 object 处理：此时 CanBindTo 一律放行，
            // 绝不因为类型名解析失败而把本来能绑的项误杀掉
            Type targetType = SelectedInputPort?.Definition != null
                ? TypeHelper.ResolveType(SelectedInputPort.Definition.DataTypeName)
                : typeof(object);

            // 类型名 → Type 的解析按"本次重算"缓存：候选动辄几千条，而变量类型只有几种，
            // 每条都走一次 Type.GetType（解析不出来还要遍历全部已加载程序集）纯属白烧。
            // 现场 9000 个变量时这一项就是几百毫秒到数秒。
            var resolvedTypes = new Dictionary<string, Type>(StringComparer.Ordinal);
            Type ResolveCached(string typeName)
            {
                if (typeName == null)
                    return typeof(object);
                if (!resolvedTypes.TryGetValue(typeName, out var cached))
                {
                    cached = TypeHelper.ResolveType(typeName);
                    resolvedTypes[typeName] = cached;
                }
                return cached;
            }

            var list = new List<PortDefinition>(source.Count);
            int hiddenByType = 0;
            foreach (var schema in source)
            {
                Type outputType = ResolveCached(schema.DataTypeName);
                bool ok = TypeHelper.CanBindTo(outputType, targetType);

                if (!ok)
                {
                    // 计数与「显示全部」无关：提示条说的是"默认视图还藏了多少个"，
                    // 勾选显示全部后这些项会以置灰形式出现，但"被类型隐藏"的事实不变。
                    hiddenByType++;
                    // "显示全部"时才把不兼容项摆出来（置灰），默认直接不进列表
                    if (_showAllPorts)
                    {
                        // 复制一份再判定：OutputDefinitions 是节点持有的共享对象，
                        // 直接往上面写 IsBindable 会污染节点自身的数据（换端口/换节点后残留上一次的判定）。
                        // 只在真要上屏时才复制——默认视图里被类型隐藏的项不必造对象。
                        var hidden = CloneForCandidate(schema);
                        hidden.IsBindable = false;
                        hidden.IncompatibleReason =
                            $"类型不匹配：{Describe(outputType)} 无法绑到 {Describe(targetType)} 端口";
                        list.Add(hidden);
                    }
                    continue;
                }

                list.Add(CloneForCandidate(schema));
            }

            DisplayPorts = new ObservableCollection<PortDefinition>(list);
            UpdateHiddenByTypeCount(hiddenByType);
        }

        private void UpdateHiddenByTypeCount(int count)
        {
            // 每次重算都通知：计数可能"看起来没变"但来源已经换了，
            // 视图侧的提示条文案/可见性需要重新求值（与 SetProperty 的"变化才通知"不同）。
            _hiddenByTypeCount = count;
            RaisePropertyChanged(nameof(HiddenByTypeCount));
        }

        private static PortDefinition CloneForCandidate(PortDefinition src) => new()
        {
            Name = src.Name,
            Description = src.Description,
            DataTypeName = src.DataTypeName,
            VariableId = src.VariableId,
            Device = src.Device,
            IsFunctionalEnum = src.IsFunctionalEnum,
            PresetOptions = src.PresetOptions,
        };

        private static string Describe(Type t)
        {
            if (t == null) return "未知";
            if (!t.IsArray) return t.Name;
            var elem = t.GetElementType();
            return elem != null ? $"{elem.Name}[]" : t.Name;
        }

        public DialogCloseListener RequestClose { get; set; }

        public VariableBindingViewModel(IWorkspaceManager workspace, IPluginProvider pluginProvider)
        {
            this.Workspace = workspace;
            this.pluginProvider = pluginProvider;

            CancelCommand = new DelegateCommand(() =>
                RequestClose.Invoke(new DialogResult(ButtonResult.Cancel))
            );
            ConfirmCommand = new DelegateCommand(Confirm);
            DoublockClickBindCommand = new DelegateCommand<PortDefinition>(DoublockClickBind);
            SelectPresetCommand = new DelegateCommand<string>(SelectPresetOption);
            ConfirmArrayBindCommand = new DelegateCommand(ConfirmArrayBind);
            CancelArrayBindCommand = new DelegateCommand(ClearPendingArray);
            UseFreeTextCommand = new DelegateCommand(UseFreeText);
        }

        private void SelectPresetOption(string option)
        {
            ConstantValue = option;

            foreach (var preset in PresetOptions)
            {
                preset.IsSelected = preset.Option == option;
            }

            RaisePropertyChanged(nameof(SelectedPresetDisplay));
        }

        /// <summary>
        /// 把「当前选中端口」已经绑定的常量值回显到常量输入框。
        ///
        /// 为什么必须在每次换端口时都做，而不是打开弹窗时做一次：
        /// Confirm 的规则是「常量框非空就写常量」，而换端口并不会清空这个框。
        /// 于是"选中 A 端口填了值 → 再切到 B 端口 → 点确定"会把 A 的值写到 B 上 ——
        /// 静默写坏另一个端口的绑定，且界面上看不出任何异常。
        /// 所以换端口要么回显该端口自己的值，要么清空，绝不能留着上一个端口的残留。
        /// </summary>
        private void LoadExistingConstant()
        {
            ConstantValue = string.Empty;

            if (_targetStep == null || SelectedInputPort?.Definition == null)
                return;

            string key = ResolveBindKey(SelectedInputPort.Definition);

            // 情形一：端口有「连线」来源（在变量绑定窗口选过常量/上游端口，或画布上连了线）。
            // 有连线时以连线为准 —— 这一点与运行期 ApplyConfigValues 的取舍一致（IsLinked 就跳过 InputValues）。
            if (_targetStep.LinkedSources != null
                && _targetStep.LinkedSources.TryGetValue(key, out var link) && link != null)
            {
                // 只有常量来源才回显进常量框；上游端口/全局变量/运行时变量由左栏以地址展示，
                // 塞进常量框会让"点确定"把连线改写成常量 —— 等于悄悄把线拆了
                if (link.NormalizeKind() == LinkKind.Constant)
                    ConstantValue = ExtractConstantText(link);
                return;
            }

            // 情形二：没有连线，值就是「手填常量」—— 它存在 InputValues 里（插件 OnConfirm 写回的那份）。
            // 早先只查了 LinkedSources，于是手填的值永远回显不出来，常量框一直是空的。
            if (_targetStep.InputValues != null
                && _targetStep.InputValues.TryGetValue(key, out var raw) && raw != null)
            {
                // 布尔按提示里的写法回显（true/false），别露出 C# 的 True/False
                ConstantValue = raw is bool flag ? (flag ? "true" : "false") : raw.ToString();
            }
        }

        /// <summary>
        /// 当前选中端口是否存在「常量」连线（供 IsConstantMode 回显用）。
        /// 只认 Constant：上游/变量连线不能把值塞进常量框，否则点确定会把线悄悄改成常量。
        /// </summary>
        private LinkReference? GetExistingConstantLink()
        {
            if (_targetStep?.LinkedSources == null || SelectedInputPort?.Definition == null)
                return null;

            string key = ResolveBindKey(SelectedInputPort.Definition);
            return _targetStep.LinkedSources.TryGetValue(key, out var link)
                && link != null
                && link.NormalizeKind() == LinkKind.Constant
                    ? link
                    : null;
        }

        /// <summary>
        /// 端口在 LinkedSources 里的键：条件节点/等待节点用的是端口的描述（变量 Guid），其余用端口名。
        /// 与 DoFinalBind / Confirm 里的取法保持一致，三处必须是同一口径。
        /// </summary>
        private string ResolveBindKey(PortDefinition definition)
            => _targetStep is ConditionStep or WaitStep ? definition.Description : definition.Name;

        /// <summary>
        /// 从常量连线里取回原始文本。
        /// Confirm 写入时把原值放在 TargetPortName，显示地址则是「前缀 + 原值」；
        /// 优先取前者，取不到再退回剥前缀，兼容更早存下的方案。
        /// </summary>
        private static string ExtractConstantText(LinkReference link)
        {
            if (!string.IsNullOrEmpty(link.TargetPortName)
                && !string.Equals(link.TargetPortName, link.DisplayAddress, StringComparison.Ordinal))
                return link.TargetPortName;

            var address = link.DisplayAddress ?? string.Empty;
            return address.StartsWith(LinkProtocol.ConstantDisplayPrefix, StringComparison.Ordinal)
                ? address.Substring(LinkProtocol.ConstantDisplayPrefix.Length)
                : address;
        }

        private void UpdatePresetOptions()
        {
            PresetOptions.Clear();
            HasFunctionalEnumPort = false;

            if (SelectedInputPort == null || SelectedInputPort.Definition == null)
            {
                RaisePropertyChanged(nameof(SelectedPresetDisplay));
                return;
            }

            var portDef = SelectedInputPort.Definition;

            // 候选**现取**，不能只看 PresetOptions。
            //
            // PresetOptions 是**插件实例被创建那一刻**的产物（宿主扫描插件类型时反射 new 的），
            // 而那一刻方案还没打开、卡还没配 —— 取到的必然是空列表，这份空值被复制进
            // PortDefinition 后一路带到界面上：现象就是"绑定窗口里轴名/卡地址没有下拉"。
            // 候选项本就来自"当前方案有哪些卡、哪些轴"，注定随方案变化，
            // 所以有 OptionKind 就**每次打开都现问一次**，没有才退回那份快照。
            //
            // 用 GetOptionItems 而不是 GetOptions：前者带显示文本。
            // 卡地址的值必须是 IP（流程按地址引用），但只显示 IP 在几张卡时认不出谁是谁，
            // 所以显示成「上料轴卡（192.168.0.11）」—— 值与显示分离，改名不断流程。
            var items = portDef.OptionKind is { } kind
                ? StepConfigOptionSource.GetOptionItems(kind)
                : BuildFallbackOptions(portDef);

            // 封闭集合端口（枚举 / IsFunctionalEnum）**不管候选有没有取到**，都不允许切到上游供值：
            // 合法值只能来自枚举成员或方案里的名字，绑上游等于允许运行期拿一个方案里不存在的
            // 名字/轴名（R33）。旧写法把"候选非空"也算进判据，于是"方案里一条子程序都没勾"
            // 这种最常见的首次配置状态下，绑定弹窗会放开上游面板 —— 手一滑就造出一条
            // "值当名字"的连线，而参数面板那边又把 🔗 藏了，用户事后连拆都拆不掉。
            //
            // 候选为空时的退化口径（2026-10-24 起改）：**不再允许手填**，而是把原因说清楚。
            // 与 LinkableValueEditor 的 IsOptionEmpty 同一口径 —— 那边空候选时主输入框折叠、
            // 显示「当前没有可选值 / 程序缺陷：…」；这边则把原因挂到 OptionEmptyReason 上，
            // 由视图在预设列表的下方提示。两者的判据只有一个：封闭集合端口的合法值只能来自
            // 枚举成员或方案里已有的名字，凭空手打的名字运行期一定解析不到。
            if (portDef.IsFunctionalEnum)
            {
                HasFunctionalEnumPort = true;

                OptionEmptyReason = items.Count > 0 ? string.Empty : DescribeEmptyReason(items.Count);

                if (items.Count > 0)
                {
                    // 已有值（上面回显来的）优先，只有在空着的时候才退到第一个预设项 ——
                    // 无条件取第一个会把刚回显出来的值冲掉，用户看到的就不是实际生效的值了。
                    // 另外：端口已有非常量连线（上游/全局/运行时）时不能自动填首项 ——
                    // 否则点确定会用一个"用户没选过"的预设把原连线静默覆盖。
                    bool hasExistingLink = _targetStep?.LinkedSources != null
                        && _targetStep.LinkedSources.TryGetValue(ResolveBindKey(portDef), out var existing)
                        && existing != null;
                    if (string.IsNullOrWhiteSpace(ConstantValue) && !hasExistingLink)
                        ConstantValue = items[0].Value;
                    foreach (var item in items)
                    {
                        PresetOptions.Add(
                            new PresetOptionItem
                            {
                                Option = item.Value,
                                Display = item.Display,
                                IsSelected = item.Value == ConstantValue,
                            }
                        );
                    }
                }
            }

            // 预设集合整体重建，IsSelected 的变化不会自动传到 VM 上（子项是独立 BindableBase），
            // 这里统一通知一次，避免构造/批量填充阶段逐项通知造成递归。
            RaisePropertyChanged(nameof(SelectedPresetDisplay));
        }

        /// <summary>
        /// 没声明 OptionKind 的端口，候选从哪来。
        ///
        /// 【枚举要单独处理，而且必须从"类型"现场推】
        /// 枚举端口的候选就是它的枚举成员，这件事**不需要任何外部信息**，
        /// 任何时候都能从端口的数据类型算出来。
        /// 而 PresetOptions 是**插件扫描期的快照** —— 那份快照在方案加载前就生成好了，
        /// 枚举候选（尤其运行期才知道的类型）经常是空的，
        /// 于是界面上只剩一个空白文本框：用户根本不知道能填哪些值（如绝对/相对）。
        ///
        /// 这里先看类型是不是枚举：是就现场生成，彻底摆脱快照；
        /// 不是才退回快照（那种候选来自宿主，本来就只能由 OptionKind 现取）。
        /// </summary>
        private static IReadOnlyList<StepConfigOption> BuildFallbackOptions(PortDefinition portDef)
        {
            var type = TypeHelper.ResolveType(portDef?.DataTypeName);
            if (type != null && type.IsEnum)
                return Enum.GetNames(type).Select(n => StepConfigOption.ForEnum(type, n)).ToList();

            return (portDef?.PresetOptions ?? new List<string>())
                .Select(v => new StepConfigOption(v))
                .ToList();
        }

        private void DoublockClickBind(PortDefinition outputSchema)
        {
            if (SelectedInputPort == null)
            {
                EasyDialog.ShowSync("提示", "请先在左侧选择需要绑定的输入端口！");
                return;
            }
            if (SelectedNode == null)
            {
                EasyDialog.ShowSync("提示", "请先在中间选择数据来源！");
                return;
            }

            // 判据与右侧列表的过滤用的是同一个 TypeHelper.CanBindTo —— 一把尺子量两次，
            // 否则会出现"列表里放着、双击又说不匹配"这种自相矛盾的行为
            Type targetType =
                TypeHelper.ResolveType(SelectedInputPort.Definition.DataTypeName);
            Type outputType = TypeHelper.ResolveType(outputSchema.DataTypeName);

            // 数组 → 标量：允许"按下标取一个元素"。
            // object 端口是"整值装箱"语义（TypeHelper 对 object 双向放行），
            // 不能强制走索引 —— 否则 double[] 到 object 只能取一个元素，整数组永远绑不进去。
            bool isIndexing = outputType.IsArray
                && !targetType.IsArray
                && targetType != typeof(object);

            if (isIndexing)
            {
                Type elementType = TypeHelper.GetBindableElementType(outputType);
                if (!TypeHelper.CanBindTo(outputType, targetType))
                {
                    EasyDialog.ShowSync(
                        "类型不匹配",
                        $"变量集合中的元素类型是 [{elementType.Name}]，\n无法赋值给 [{targetType.Name}] 类型的端口！"
                    );
                    return;
                }

                // 行内索引面板：不再弹二级对话框（弹窗会遮住"选的是哪个数组端口"）。
                // 这里只负责展开面板并记住待绑定的端口，真正的写模型在 ConfirmArrayBindCommand。
                PendingArrayPort = outputSchema;
                PendingArrayElementTypeName = FriendlyNameConverter.Convert(
                        elementType,
                        typeof(string),
                        null!,
                        CultureInfo.CurrentCulture
                    )?.ToString()
                    ?? Describe(elementType);
                ArrayIndexText = "0";
                return;
            }
            else
            {
                if (!TypeHelper.CanBindTo(outputType, targetType))
                {
                    EasyDialog.ShowSync(
                        "类型不匹配",
                        $"无法将 [{outputType.Name}] 直接绑定到 [{targetType.Name}] 端口！"
                    );
                    return;
                }

                DoFinalBind(outputSchema);
            }

            if (IsSingleBindMode)
            {
                Confirm();
            }
        }

        /// <summary>
        /// 行内索引面板的「确认绑定」：解析非负下标 → 收起面板 → 走原有的 DoFinalBind。
        /// 单绑定模式沿用旧行为（选定后自动 Confirm 回传 BoundLink）。
        /// </summary>
        private void ConfirmArrayBind()
        {
            if (!int.TryParse(ArrayIndexText, out var index) || index < 0)
            {
                EasyDialog.ShowSync("格式错误", "索引必须是非负整数！");
                return;
            }

            var port = PendingArrayPort;
            if (port == null)
                return;

            // 先收起面板再写模型：DoFinalBind 会改 LinkedAddress/连线，
            // 面板状态若在写完后才清，异常路径下会残留一个"看似还能确认"的旧端口。
            ClearPendingArray();
            DoFinalBind(port, index);

            if (IsSingleBindMode)
                Confirm();
        }

        /// <summary>收起行内索引面板（切端口 / 切来源 / 取消时调用）。</summary>
        private void ClearPendingArray()
        {
            PendingArrayPort = null;
            PendingArrayElementTypeName = string.Empty;
            ArrayIndexText = string.Empty;
        }

        /// <summary>
        /// 拼一条变量来源的显示地址（纯函数，便于断言）。
        ///
        /// 格式：<c>{节点名}.{设备}.{变量名}[{下标}]</c>，无设备时退回 <c>{节点名}.{变量名}</c>。
        ///
        /// 为什么设备要插在**变量名之前**而不是之后：
        /// ① 两台设备的同名点位必须拼出不同地址，否则界面上分不出绑的是哪一台；
        /// ② 变量改名级联（<c>RebuildVariableDisplayAddress</c>）按**最后一个点**切前缀、
        ///    只替换名字那一段——设备放在名字后面会被当成前缀的一部分，改名后设备段就丢了。
        /// </summary>
        public static string BuildVariableDisplayAddress(string nodeName, PortDefinition port, int index)
        {
            var name = port?.Name ?? string.Empty;
            var device = (port?.Device ?? string.Empty).Trim();
            var qualified = device.Length == 0 ? name : device + "." + name;
            var head = $"{nodeName}.{qualified}";
            return index >= 0 ? $"{head}[{index}]" : head;
        }

        private void DoFinalBind(PortDefinition port, int index = -1)        {
            Guid targetId = SelectedNode.Id;
            // ★ 下标必须写进 TargetPortName：协议规定它就是"端口名（可带 [索引]）"，
            //   编译器只从那里解析下标并生成 ArrayIndexProxyPort。此前下标只进显示串 →
            //   "数组取元素"要么编译报类型不匹配，要么（目标端口是 object 时）把整个数组静默传下去。
            string targetPort = index >= 0 ? $"{port.Name}[{index}]" : port.Name;
            // 连线类型由候选节点自带（FlowQueryHelper 构造时指定），不再靠 Id 是否为空反推
            LinkKind kind = SelectedNode.DefaultLinkKind;
            string displayName;

            // 运行时变量引用：使用 Runtime. 前缀，与协议层 marker Guid 约定一致
            if (kind == LinkKind.RuntimeVariable)
            {
                displayName = $"Runtime.{port.Name}";
            }
            else
            {
                displayName = BuildVariableDisplayAddress(SelectedNode.Name, port, index);
            }
            var linkRef = new LinkReference(kind, targetId, targetPort, displayName)
            {
                // 全局变量连线带上稳定身份：变量改名后仍能命中，不必依赖 TargetPortName 里的旧名字。
                // 非变量来源的端口这里是 Guid.Empty，解析器按"无身份"处理并退回按名查找。
                TargetVariableId = port.VariableId
            };

            // P0-③：单绑模式下弹窗只负责"把用户选的变量还给出题人"（经 BoundLink 回传），
            // 绝不能直写目标步骤的 LinkedSources——此时目标可能是条件节点，
            // bindKey 会落到中文描述上（如"当前准备绑定的变量"），在活模型里留下垃圾键
            if (!IsSingleBindMode && _targetStep != null)
            {
                string bindKey;
                // 条件节点与等待节点的连线键都是"变量 Guid"（借 Description 承载）；
                // 其余步骤用端口名。
                if (_targetStep is ConditionStep or WaitStep)
                {
                    bindKey = SelectedInputPort.Definition.Description;
                }
                else
                {
                    bindKey = SelectedInputPort.Definition.Name;
                }

                // 走 SetLink（内部 RaisePropertyChanged(LinkedSources)）：FlowModel 的版本递增
                // 唯一来自步骤 PropertyChanged，裸写字典会让"刚绑的线"看不见 → 复用的已编译会话
                // 继续跑旧连线（用户侧症状：绑了不生效，界面看起来却是好的）
                _targetStep.SetLink(bindKey, linkRef);
            }
            SelectedInputPort.LinkedAddress = displayName;

            _lastBoundLink = linkRef;
            _lastBoundPort = port;
        }

        private void Confirm()
        {            if (!string.IsNullOrWhiteSpace(ConstantValue) && SelectedInputPort != null)
            {
                // 键必须走 ResolveBindKey（条件节点用的是端口描述而不是端口名），
                // 与 LoadExistingConstant / DoFinalBind 同一口径。
                // 早先这里直接取 Definition.Name，条件节点写常量会落到错误的键上——
                // 表现为"填了值但读不回来"，且界面上看不出异常。
                string bindKey = ResolveBindKey(SelectedInputPort.Definition);
                string displayName = $"{LinkProtocol.ConstantDisplayPrefix}{ConstantValue}";
                var linkRef = new LinkReference(LinkKind.Constant, Guid.Empty, ConstantValue, displayName);
                // P0-③：同 DoFinalBind，单绑模式下常量也只回传、不写活模型；
                // 写活模型时走 SetLink（直改字典不递增版本 → 复用的已编译会话看不到这次修改）
                if (!IsSingleBindMode && _targetStep != null)
                    _targetStep.SetLink(bindKey, linkRef);
                SelectedInputPort.LinkedAddress = displayName;
                _lastBoundLink = linkRef;
                _lastBoundPort = new PortDefinition
                {
                    Name = bindKey,
                    // 常量文本回传给单绑定调用方时，类型必须按目标端口声明走。
                    // 无条件写 System.String 会让 Int32 条件变量被声明成字符串。
                    DataTypeName = SelectedInputPort.Definition.DataTypeName ?? "System.String",
                };
            }

            if (_lastBoundLink == null && SelectedOutputPort != null && IsSingleBindMode)
            {
                DoFinalBind(SelectedOutputPort);
            }

            var p = new DialogParameters();
            if (_lastBoundLink != null)
                p.Add("BoundLink", _lastBoundLink);
            if (_lastBoundPort != null)
                p.Add("DataTypeName", _lastBoundPort.DataTypeName);
            RequestClose.Invoke(p, ButtonResult.OK);
        }

        /// <summary>
        /// 使用手填的变量名（仅名字选择模式 + 运行时作用域可见）。
        ///
        /// 产出 <c>Kind=RuntimeVariable</c> 的引用：运行期 <c>RuntimeVariableProxyPort</c>
        /// 本来就是"按名从 LocalVariables 取"。**不写 LinkedSources**——
        /// 单绑定模式的定义就是"只回传、不写模型"（名字选择更是如此：名字不是值）。
        /// </summary>
        private void UseFreeText()
        {
            if (!CanUseFreeText) return;

            var name = FreeTextName.Trim();
            var linkRef = new LinkReference(
                LinkKind.RuntimeVariable,
                LinkProtocol.RuntimeVariableMarkerGuid,
                name,
                $"运行时变量.{name}")
            {
                TargetVariableId = Guid.Empty,   // 运行时变量没有稳定身份
            };

            _lastBoundLink = linkRef;
            _lastBoundPort = new PortDefinition
            {
                Name = name,
                DataTypeName = typeof(object).AssemblyQualifiedName,
            };

            var p = new DialogParameters
            {
                { "BoundLink", linkRef },
                { "DataTypeName", typeof(object).AssemblyQualifiedName },
            };
            RequestClose.Invoke(p, ButtonResult.OK);
        }

        public void BuildTree(IDialogParameters parameters = null)
        {
            // 重建端口表前先收起索引面板：面板里的 PendingArrayPort 属于上一轮构建，
            // 留着会在新一轮端口表上"确认"一个已经不存在的端口。
            ClearPendingArray();

            if (DisplayDataPort == null || TreeNodes == null)
                return;

            // 单绑定模式的目标端口来自 TargetPortName/TargetTypeName，不依赖 _targetStep：
            // 插件视图发起绑定（ProcessViewModel.OnLinkPathEvent / ConditionEditorViewModel.OnBindVariable）
            // 可能只传 IsSingleBindMode，此时 Workspace.CurrentStep 也可能为空。
            if (_targetStep == null && !IsSingleBindMode)
                return;

            // 先退订再清空：端口模型是每次重建都新造的，旧对象若不退订，
            // 会继续向 VM 推 PropertyChanged（BoundPortCount 在已丢弃对象上重算）。
            foreach (var port in DisplayDataPort)
                port.PropertyChanged -= OnInputPortPropertyChanged;

            DisplayDataPort.Clear();
            TreeNodes.Clear();

            if (!IsSingleBindMode)
            {
                // 等待节点：没有插件端口表，它的"输入脚"就是条件变量表本身。
                // 键口径与 FlowCompiler 的消费方分支一致——用变量 Guid 的字符串
                //（这里借 PortDefinition.Description 承载，与 ConditionStep 的"描述即键"同一手法），
                // 名字只用于界面显示。漏掉这一支的表现是绑定窗口左侧一个端口都没有 →
                // 等待节点的条件变量永远连不上（条件恒取默认 false，必等到超时）。
                if (_targetStep is ConditionStep condition)
                {
                    // ConditionStep 的"输入脚"同样是条件变量表（不是 BuiltIn_If 的
                    // 虚拟变量/中间变量插件端口）：FlowCompiler 与条件编辑器都按变量 Guid 解析。
                    // 若这里走插件端口表，ResolveBindKey 写的是 Description（Guid），
                    // 回读却按 Name（虚拟变量）查，绑定会"写进去但重开不回显、解绑也清不掉"。
                    AddLocalVariablePorts(condition.LocalVariables, condition.LinkedSources);
                }
                else if (_targetStep is WaitStep wait)
                {
                    AddLocalVariablePorts(wait.LocalVariables, wait.LinkedSources);
                }
                else if (
                    pluginProvider.ModulePlugins.TryGetValue(
                        _targetStep.PluginTypeName,
                        out var pluginInfo
                    )
                )
                {
                    if (pluginInfo.InputDefinitions != null)
                    {
                        foreach (var schema in pluginInfo.InputDefinitions)
                        {
                            _targetStep.LinkedSources.TryGetValue(
                                schema.Name,
                                out var existingLink
                            );
                            AddDisplayPort(
                                new InputPortUIModel(
                                    schema,
                                    existingLink?.DisplayAddress,
                                    existingLink?.TargetVariableId ?? Guid.Empty
                                )
                            );
                        }
                    }
                }
            }
            else
            {
                // 单绑定模式：优先使用外部指定的目标端口（插件自定义视图发来的链接请求）
                string portName = "目标变量";
                string typeName = "System.Object";
                string desc = "当前准备绑定的变量";
                if (
                    parameters != null
                    && parameters.TryGetValue<string>("TargetPortName", out var targetPort)
                    && !string.IsNullOrEmpty(targetPort)
                )
                {
                    portName = targetPort;
                    desc = "当前准备绑定的输入端口";
                    typeName =
                        parameters.TryGetValue<string>("TargetTypeName", out var tn)
                        && !string.IsNullOrEmpty(tn)
                            ? tn
                            : "System.Object";
                }

                var mockNode = new InputPortUIModel(
                    new PortDefinition
                    {
                        Name = portName,
                        DataTypeName = typeName,
                        Description = desc,
                    },
                    ""
                );

                AddDisplayPort(mockNode);
            }
            // 空端口表也要把旧选中项清掉，否则重开/重建后 VM 还指着已经不在表里的端口。
            SelectedInputPort = DisplayDataPort.Count > 0 ? DisplayDataPort[0] : null;

            // 用户变量 + 系统变量（运动/IO）都进入绑定候选；系统变量按 VariableId 解析。
            var allVariables = (Workspace?.GlobalVariables ?? Enumerable.Empty<IVariable>())
                .Concat(Workspace?.SystemVariables ?? Enumerable.Empty<IVariable>())
                .ToList();

            // 没有目标步骤（插件视图单绑定且 Workspace.CurrentStep 为空）时无法计算"上游"，
            // 只给全局变量候选；这也避免 FlowQueryHelper 在无 Prism 容器的测试宿主里解析插件。
            var allSteps = _targetStep == null
                ? Enumerable.Empty<StepModel>()
                : Workspace?.CurrentFlow?.Steps ?? Enumerable.Empty<StepModel>();
            var availableVars = FlowQueryHelper.GetAvailableVariablesTree(
                allVariables,
                allSteps,
                _targetStep,
                // 名字选择模式不需要上游端口节点（写名不能拿端口名当变量名），
                // 关掉后也不必依赖 Prism 容器去展开上游插件的输出端口表。
                includeUpstreamStepPorts: !(PickNameMode && !AllowStepPorts)
            );
            foreach (var item in FlowQueryHelper.FilterGlobalNodes(availableVars, AllowGlobals))
            {
                // 名字选择模式：只留"变量来源"的节点（全局 / 运行时），
                // 上游步骤节点整块摘掉——它的 TargetPortName 是端口名（如 Out），
                // 当变量名用就是 R33 记的"拿值当名字"，运行期必然找不到变量。
                if (PickNameMode && !AllowStepPorts
                    && item != null
                    && item.DefaultLinkKind == LinkKind.StepPort)
                {
                    continue;
                }

                // 名字选择模式 + 指定了作用域：只留对应来源的节点。
                // 为什么必须裁：全局行打开弹窗时也会看到一个空的「运行时变量」组，
                // 运行时行打开则要在一万条全局变量里翻——两者都是"看起来像能选，其实选错就废"。
                // 作用域由调用方通过 ScopeFilter 明确指定（全局行 = Global / 运行时行 = Runtime），
                // 不传（null/空）时保持原行为（都列），既有调用方零影响。
                if (PickNameMode && !IsNodeInScope(item))
                    continue;

                TreeNodes.Add(item);
            }

            // 设计默认态是"已选第一个来源"，打开即可看到候选端口；用户仍可随时切换。
            if (TreeNodes.Count > 0
                && (SelectedNode == null || !TreeNodes.Contains(SelectedNode)))
            {
                SelectedNode = TreeNodes[0];
            }

            // 端口集合已重建完毕：一次性通知两个计数（逐项 Add 时集合尚未定型，
            // 在中间态通知会让进度条闪一串中间数字）。
            RaisePropertyChanged(nameof(TotalPortCount));
            RaisePropertyChanged(nameof(BoundPortCount));
        }

        /// <summary>
        /// 条件/等待节点的"输入脚"：把 LocalVariables 铺成端口，Description 承载变量 Guid。
        /// 两者共用同一份键口径（FlowCompiler 按 Guid 解析，ResolveBindKey 取 Description）。
        /// </summary>
        private void AddLocalVariablePorts(
            IEnumerable<LocalVariableItem> variables,
            IDictionary<string, LinkReference> linkedSources)
        {
            if (variables == null)
                return;

            foreach (var variable in variables)
            {
                if (variable == null)
                    continue;

                LinkReference? existingLink = null;
                if (linkedSources != null)
                    linkedSources.TryGetValue(variable.Id.ToString(), out existingLink);
                AddDisplayPort(
                    new InputPortUIModel(
                        new PortDefinition
                        {
                            Name = variable.Name,
                            DataTypeName = variable.DataTypeName,
                            Description = variable.Id.ToString(),
                        },
                        existingLink?.DisplayAddress,
                        existingLink?.TargetVariableId ?? Guid.Empty
                    )
                );
            }
        }

        /// <summary>加入端口表并订阅其属性变化（BoundPortCount 依赖 LinkedAddress/IsBound）。</summary>
        private void AddDisplayPort(InputPortUIModel port)
        {
            DisplayDataPort.Add(port);
            port.PropertyChanged += OnInputPortPropertyChanged;
        }

        private void OnInputPortPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (
                e.PropertyName == nameof(InputPortUIModel.LinkedAddress)
                || e.PropertyName == nameof(InputPortUIModel.IsBound)
            )
            {
                RaisePropertyChanged(nameof(BoundPortCount));
            }
        }

        public bool CanCloseDialog() => true;

        public void OnDialogClosed() { }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            IsSingleBindMode = parameters.GetValue<bool>("IsSingleBindMode");

            // 缺省 true：只有显式传 false 的插件（C#脚本输入）才隐藏全局/系统变量候选
            AllowGlobals = parameters == null
                || !parameters.TryGetValue<bool>("AllowGlobals", out var allowGlobals)
                || allowGlobals;

            // 名字选择模式（2026-10-30）：调用方只取名字/Id，不写 LinkedSources。
            // 与绑定模式共用同一个弹窗与候选树，差异只在候选裁剪与标题文案。
            PickNameMode = parameters != null
                && parameters.TryGetValue<bool>("PickNameMode", out var pickName)
                && pickName;
            AllowStepPorts = parameters == null
                || !parameters.TryGetValue<bool>("AllowStepPorts", out var allowStepPorts)
                || allowStepPorts;

            // 名字选择模式的作用域裁剪 + 手输出口（2026-10-30）。
            // 不传时保持原行为：不过滤、不给手填。
            ScopeFilter = parameters != null
                && parameters.TryGetValue<string>("ScopeFilter", out var scopeFilter)
                    ? (scopeFilter ?? string.Empty).Trim()
                    : string.Empty;
            AllowFreeText = parameters != null
                && parameters.TryGetValue<bool>("AllowFreeText", out var allowFreeText)
                && allowFreeText;

            // 目标步骤：外部显式传入优先（For/条件弹窗的下钻场景），否则退回 Workspace.CurrentStep。
            // 单靠 CurrentStep 会在"下钻进容器"时指错人：它指向的是容器本身，不是被双击的那个步骤。
            _targetStep =
                parameters != null
                && parameters.TryGetValue<StepModel>("TargetStep", out var explicitTarget)
                && explicitTarget != null
                    ? explicitTarget
                    : Workspace?.CurrentStep;

            // 顶部上下文条：单绑定没有"当前步骤"语义（目标来自插件视图的绑定请求），
            // 显示固定文案，避免露出 Workspace.CurrentStep 这种不相干的名字。
            TargetStepName = IsSingleBindMode
                ? "插件视图绑定请求"
                : _targetStep?.StepName ?? "当前步骤";

            // 名字选择模式：把当前已选的名字/Id 预选上（否则每次开窗都要在几千条里重新找）。
            // 必须在 BuildTree 之前记下来——BuildTree 会把 SelectedNode 复位到第一个节点。
            _presetPickVariableId = parameters != null
                && parameters.TryGetValue<Guid>("InitialVariableId", out var initialId)
                    ? initialId
                    : Guid.Empty;
            _presetPickName = parameters != null
                && parameters.TryGetValue<string>("InitialName", out var initialName)
                    ? (initialName ?? string.Empty).Trim()
                    : string.Empty;

            BuildTree(parameters);

            if (PickNameMode)
                ApplyPickNamePreset();
        }

        private Guid _presetPickVariableId = Guid.Empty;
        private string _presetPickName = string.Empty;

        /// <summary>
        /// 名字选择模式下的作用域裁剪判定：这个候选节点是否属于 <see cref="ScopeFilter"/>。
        ///
        /// 判定放在节点粒度（`ModuleGroup`）而不是端口粒度：一组候选就是一个来源，
        /// 全局组里不会有运行时变量。`ScopeFilter` 为空时一律放行（既有调用方零影响）。
        /// </summary>
        private bool IsNodeInScope(ToolItemModel node)
        {
            if (node == null) return false;
            if (string.IsNullOrWhiteSpace(ScopeFilter)) return true;
            if (!PickNameMode) return true;

            bool wantsGlobal = string.Equals(ScopeFilter, "Global", StringComparison.OrdinalIgnoreCase);
            bool wantsRuntime = string.Equals(ScopeFilter, "Runtime", StringComparison.OrdinalIgnoreCase);
            if (!wantsGlobal && !wantsRuntime) return true;   // 不认识的值 → 不过滤

            bool isRuntimeNode = node.DefaultLinkKind == LinkKind.RuntimeVariable
                                 || string.Equals(node.ModuleGroup, "Runtime", StringComparison.OrdinalIgnoreCase);
            bool isGlobalNode = node.DefaultLinkKind == LinkKind.GlobalVariable
                                || string.Equals(node.ModuleGroup, "Global", StringComparison.OrdinalIgnoreCase);

            return wantsRuntime ? isRuntimeNode : isGlobalNode;
        }

        /// <summary>
        /// 名字选择模式的预选：Id 优先、名字兜底（与全仓解析口径一致）。
        /// 找不到就什么都不做——不把 SelectedNode 指向不存在的项，也不清空已有选择。
        /// </summary>
        private void ApplyPickNamePreset()
        {
            if (_presetPickVariableId == Guid.Empty && _presetPickName.Length == 0)
                return;

            foreach (var node in TreeNodes)
            {
                var match = node?.OutputDefinitions?.FirstOrDefault(p =>
                    (_presetPickVariableId != Guid.Empty && p.VariableId == _presetPickVariableId)
                    || (_presetPickVariableId == Guid.Empty
                        && string.Equals((p.Name ?? string.Empty).Trim(), _presetPickName, StringComparison.OrdinalIgnoreCase)));

                if (match == null)
                    continue;

                SelectedNode = node;
                SelectedOutputPort = DisplayPorts.FirstOrDefault(p =>
                    (_presetPickVariableId != Guid.Empty && p.VariableId == _presetPickVariableId)
                    || (_presetPickVariableId == Guid.Empty
                        && string.Equals((p.Name ?? string.Empty).Trim(), _presetPickName, StringComparison.OrdinalIgnoreCase)));
                return;
            }
        }

        public class PresetOptionItem : BindableBase
        {
            private string _option;
            public string Option
            {
                get => _option;
                set => SetProperty(ref _option, value);
            }

            /// <summary>
            /// 界面上显示的文本（默认同 <see cref="Option"/>）。
            ///
            /// 为什么值与显示要分开：写进流程的是值（如卡地址 IP），而人能认出来的是显示
            ///（如「上料轴卡（192.168.0.11）」）。若拿显示文本当值用，
            /// 用户改一次设备名，已经配好的流程就全断了。
            /// </summary>
            private string _display = string.Empty;
            public string Display
            {
                get => _display;
                set => SetProperty(ref _display, value);
            }

            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set => SetProperty(ref _isSelected, value);
            }
        }
    }
}
