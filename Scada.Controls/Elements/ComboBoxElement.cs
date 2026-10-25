using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 枚举型下拉选择器图元（TypeKey = <c>Hmi.ComboBox</c>）：绑一个 <c>int</c>，
    /// 用一张 <c>值=显示名</c> 映射表把它翻译成人话。
    ///
    /// <b>为什么核心值是 int，而不是显示文本</b>
    /// ---------
    /// PLC / Modbus 寄存器里存的就是数值；若把「自动」这种文本当值用，改一次显示文案
    /// 就会打断已经配好的流程与绑定。所以 <see cref="Value"/> 永远是整数，
    /// <see cref="Options"/> 只负责"这个整数叫什么"（格式 <c>0=手动;1=自动;2=检修</c>）。
    /// 值找不到映射时显示 <c>#7</c>——难看但诚实，比显示一个假的「手动」安全。
    ///
    /// <b>为什么只在 DropDownClosed 里写变量</b>
    /// ---------
    /// <see cref="Value"/> 同时是"数据泵写进来的运行值"和"用户选中的值"。
    /// 如果在 <see cref="Value"/> 的变更回调里写变量，数据泵每次把变量值刷进控件都会
    /// 立刻原样写回去，形成一条没有尽头的回写环（还会把设备侧的值覆盖掉）。
    /// 所以写入只挂在 <c>PART_Combo.DropDownClosed</c>——那是"用户真的选了一下"的唯一证据；
    /// 运行态数据变化只更新显示，一个字节都不往外写。
    ///
    /// <b>解析口径</b>
    /// ---------
    /// 按 <c>;</c> 分段、按第一个 <c>=</c> 分值与显示名；空段、缺 <c>=</c>、值不是整数的段一律跳过
    /// （坏配置不该让整页渲染中断）；重复值保留第一条；显示名为空时回落到数值文本。
    /// </summary>
    [TemplatePart(Name = PartCombo, Type = typeof(ComboBox))]
    public partial class ComboBoxElement : ScadaElementBase
    {
        /// <summary>模板部件名：下拉框本体</summary>
        public const string PartCombo = "PART_Combo";

        /// <summary>模式取值：只读（只显示，不可下拉）</summary>
        public const string ModeOutput = "Output";

        /// <summary>模式取值：读写（用户选择后写回变量）</summary>
        public const string ModeInputOutput = "InputOutput";

        /// <summary>呈现方式：现有下拉框外观</summary>
        public const string PresentationField = "Field";

        /// <summary>呈现方式：只读字段（状态读数；隐藏 chevron/原始值/整框，保留底边线）</summary>
        public const string PresentationText = "Text";

        /// <summary><see cref="Value"/> 在描述符里的属性键（写回时按它找绑定）</summary>
        private const string ValueKey = "Value";

        // 声明次序 = 属性面板里的行序：数据 → 常规。

        /// <summary>当前选中值（整数；绑到工程变量上）</summary>
        [ScadaProperty(Key = "Value", DisplayName = "当前值", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "0", IsBindable = true,
                       Description = "当前选中的整数值。运行态把 PLC/Modbus 的 int 变量绑到这里；" +
                                     "用户在下拉里选择后把新值写回变量。找不到映射时显示「#值」")]
        public partial int Value { get; set; }

        /// <summary>枚举映射表（格式 <c>值=显示名;值=显示名</c>）</summary>
        [ScadaProperty(Key = "Options", DisplayName = "选项映射", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "0=手动;1=自动;2=检修",
                       Description = "枚举映射表，格式：值=显示名;值=显示名（如 0=手动;1=自动;2=检修）。" +
                                     "值必须是整数；非法项自动跳过，空串不报错。显示名改了不会改变已配好的值")]
        public partial string? Options { get; set; }

        /// <summary>
        /// 方案级共享文本列表（稳定 Id 的字符串形式；空 = 继续用 <see cref="Options"/>）。
        ///
        /// 为什么存字符串而不是 Guid：属性袋是 string→string，属性面板的“文本列表”行
        /// 也按字符串写回；解析时再 TryParse。坏 Id 不抛，按“列表缺失”显示。
        /// </summary>
        [ScadaProperty(Key = "StateListId", DisplayName = "文本列表", Kind = ElementPropertyKind.StateList,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "", IsBindable = false,
                       Description = "方案级共享文本列表：一份定义、多个下拉框引用；留空则继续用「选项映射」。" +
                                     "列表改名不打断引用；删除后显示「未找到文本列表」")]
        public partial string? StateListId { get; set; }

        /// <summary>模式：<see cref="ModeOutput"/> 只读 / <see cref="ModeInputOutput"/> 读写</summary>
        [ScadaProperty(Key = "Mode", DisplayName = "模式", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.General, DefaultValue = "InputOutput",
                       Choices = new[] { "Output", "InputOutput" }, AffectsRender = false,
                       Description = "InputOutput = 用户选择后写回变量（还需绑了变量、有写通道、权限够）；" +
                                     "Output = 只显示，下拉框不可用")]
        public partial string? Mode { get; set; }

        /// <summary>呈现方式：<see cref="PresentationField"/> 下拉框 / <see cref="PresentationText"/> 只读字段</summary>
        [ScadaProperty(Key = "Presentation", DisplayName = "呈现方式", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.General, DefaultValue = "Field",
                       Choices = new[] { "Field", "Text" }, AffectsRender = false,
                       Description = "Field = 现有下拉框；Text = 只读字段（隐藏 chevron / 原始值 / 整框，保留底边线）。" +
                                     "Text 呈现只读，不写变量；要可写请用 Field。" +
                                     "Text 呈现只使用 Stroke（底边线）、文字属性与 TextAlignment；" +
                                     "Fill / CornerRadius / StrokeThickness 不生效")]
        public partial string? Presentation { get; set; }

        /// <summary>Text 呈现的底边线；Field 模式下无效</summary>
        [ScadaProperty(Key = "ShowSeparator", DisplayName = "显示底边线", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.General, DefaultValue = "True", AffectsRender = false,
                       Description = "Text 呈现的底边线；关掉就是纯文本档（KPI 卡 / 密集看板）。Field 模式下无效")]
        public partial bool ShowSeparator { get; set; }

        /// <summary>上一次写入失败的原因（运行态内部状态；空串表示没有错误）</summary>
        public static readonly DependencyProperty WriteErrorProperty = DependencyProperty.Register(
            nameof(WriteError), typeof(string), typeof(ComboBoxElement),
            new FrameworkPropertyMetadata(string.Empty));

        private static readonly DependencyPropertyKey OptionListPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(OptionList), typeof(ObservableCollection<ChoiceOption>), typeof(ComboBoxElement),
            new PropertyMetadata(null));

        /// <summary>解析后的候选项（只读；<see cref="Options"/> 变化时整表重建）</summary>
        public static readonly DependencyProperty OptionListProperty =
            OptionListPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey SelectedTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(SelectedText), typeof(string), typeof(ComboBoxElement),
            new PropertyMetadata(string.Empty));

        /// <summary>当前值对应的显示文本（只读；找不到映射时为「#值」）</summary>
        public static readonly DependencyProperty SelectedTextProperty =
            SelectedTextPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey StateListMissingPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(StateListMissing), typeof(bool), typeof(ComboBoxElement),
            new PropertyMetadata(false));

        /// <summary>配了 StateListId 但解析不到列表（删除/坏 Id/解析器未注入）</summary>
        public static readonly DependencyProperty StateListMissingProperty =
            StateListMissingPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey IsEditablePropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(IsEditable), typeof(bool), typeof(ComboBoxElement),
            new PropertyMetadata(false));

        /// <summary>此刻能不能改（只读；四道门同数值域）</summary>
        public static readonly DependencyProperty IsEditableProperty =
            IsEditablePropertyKey.DependencyProperty;

        /// <summary>空表的共享实例：<see cref="OptionListProperty"/> 的默认值不能放可变集合（DP 默认值全实例共享）</summary>
        private static readonly ObservableCollection<ChoiceOption> EmptyOptions = new();

        static ComboBoxElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(ComboBoxElement),
                new FrameworkPropertyMetadata(typeof(ComboBoxElement)));
        }

        public ComboBoxElement()
        {
            ReparseOptions(); // 构造即解析默认映射表，断言/设计态都能直接读到
            UpdateEditable();
        }

        /// <summary>上一次写入失败的原因（空串表示没有错误）</summary>
        public string? WriteError
        {
            get => (string?)GetValue(WriteErrorProperty);
            set => SetValue(WriteErrorProperty, value ?? string.Empty);
        }

        /// <summary>解析后的候选项（只读，见 <see cref="OptionListProperty"/>）</summary>
        public ObservableCollection<ChoiceOption> OptionList
            => (ObservableCollection<ChoiceOption>?)GetValue(OptionListProperty) ?? EmptyOptions;

        /// <summary>当前值的显示文本（只读，见 <see cref="SelectedTextProperty"/>）</summary>
        public string? SelectedText => (string?)GetValue(SelectedTextProperty);

        /// <summary>配了文本列表但解析不到（只读，见 <see cref="StateListMissingProperty"/>）</summary>
        public bool StateListMissing => (bool)GetValue(StateListMissingProperty);

        /// <summary>此刻能不能改（只读，见 <see cref="IsEditableProperty"/>）</summary>
        public bool IsEditable => (bool)GetValue(IsEditableProperty);

        private bool IsOutputMode => string.Equals(Mode, ModeOutput, StringComparison.OrdinalIgnoreCase);

        /// <summary>是不是“只读字段”呈现（Text）：它不写变量，也不接受下拉交互</summary>
        private bool IsTextPresentation
            => string.Equals(Presentation, PresentationText, StringComparison.OrdinalIgnoreCase);

        /// <summary>能不能改：呈现允许写、模式允许写、有写通道、Value 真绑了变量且没停用、当前角色够格</summary>
        private bool CanOperate
            => !IsTextPresentation
               && !IsOutputMode
               && RuntimeContext?.Writer is not null
               && Element?.FindBinding(ValueKey) is { IsEnabled: true }
               && (Element?.RequiredRole is not { } required
                   || RuntimeContext?.AccessPolicy.CanOperate(required, out _) == true);

        protected override void OnRuntimeContextChanged(ScadaRuntimeContext? oldContext, ScadaRuntimeContext? newContext)
        {
            UpdateEditable();
            ReparseOptions(); // 运行态上下文可能带来 StateLists（设计期解析器为 null 时靠它）
        }

        protected override void OnStateListResolverChanged() => ReparseOptions();

        protected override void OnElementRefreshed()
        {
            UpdateEditable();
            UpdateSelectedText();
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            if (_combo is not null)
            {
                _combo.DropDownOpened -= OnDropDownOpened;
                _combo.DropDownClosed -= OnDropDownClosed;
            }

            _combo = GetTemplateChild(PartCombo) as ComboBox;

            if (_combo is not null)
            {
                _combo.DropDownOpened += OnDropDownOpened;
                _combo.DropDownClosed += OnDropDownClosed;
            }

            UpdateSelectedText();
        }

        private ComboBox? _combo;
        private ScadaStateList? _subscribedStateList;
        private bool _dropDownOpened;
        private int _valueAtDropDownOpen;

        private void OnDropDownOpened(object? sender, EventArgs e)
        {
            _dropDownOpened = true;
            _valueAtDropDownOpen = Value;
        }

        private void OnDropDownClosed(object? sender, EventArgs e) => NotifyDropDownClosed();

        /// <summary>
        /// 用户选完并收起下拉：把当前 <see cref="Value"/> 写回变量。
        /// <b>只在这里写</b>，理由见类注释（Value 变更回调里写会形成回写环）。
        ///
        /// 公开而不是私有：WPF 的 <see cref="ComboBox.DropDownClosed"/> 是普通 CLR 事件、
        /// 没有可 Raise 的路由事件，断言里造不出"用户收起下拉"这个动作；
        /// 而"选择 → 写变量"这条链必须有可驱动的入口——与 <see cref="BitButtonElement.BeginPress"/>
        /// 同一先例。模板里的 <c>PART_Combo.DropDownClosed</c> 与本方法共用同一段逻辑。
        /// </summary>
        public void NotifyDropDownClosed()
        {
            WriteError = string.Empty;

            // 只“打开又关上、没有改选”时不该产生一次写变量（现场会多一条无意义的审计/PLC 写）。
            // 断言直接调用本方法时没有 DropDownOpened，_dropDownOpened=false，仍按“用户动作”处理。
            bool wasOpened = _dropDownOpened;
            _dropDownOpened = false;

            if (wasOpened && Value == _valueAtDropDownOpen)
                return;

            if (!IsEditable)
                return;

            if (RuntimeContext?.Writer is not { } writer || Element is not { } element)
                return;

            if (!writer.TryWriteText(element, ValueKey, Value.ToString(CultureInfo.InvariantCulture), out var error))
                WriteError = string.IsNullOrWhiteSpace(error) ? "写入失败" : error;
            else
                RaiseScadaEvent(ScadaEventType.ObjectChanged);
        }

        partial void OnValuePropertyChanged() => UpdateSelectedText();
        partial void OnOptionsPropertyChanged() => ReparseOptions();
        partial void OnStateListIdPropertyChanged() => ReparseOptions();
        partial void OnModePropertyChanged() => UpdateEditable();
        partial void OnPresentationPropertyChanged()
        {
            // 手改 .vms 可能写 "text"/"field" 或别的值：归一成规范值，
            // 否则 XAML 的 Trigger（大小写敏感）与 IsTextPresentation（忽略大小写）会各说各话。
            var canonical = string.Equals(Presentation, PresentationText, StringComparison.OrdinalIgnoreCase)
                ? PresentationText
                : PresentationField;

            if (!string.Equals(Presentation, canonical, StringComparison.Ordinal))
                Presentation = canonical; // 第二次进回调时已相等，不会递归

            UpdateEditable();
        }

        private void UpdateEditable()
            => SetValue(IsEditablePropertyKey, CanOperate);

        /// <summary>
        /// 按 <see cref="Options"/> 重建 <see cref="OptionList"/>。
        /// 坏段跳过、空串不崩、重复值保留第一条（见类注释的解析口径）。
        /// </summary>
        private void ReparseOptions()
        {
            var stateList = FindStateList();
            SubscribeStateList(stateList);

            var list = new ObservableCollection<ChoiceOption>();
            var seen = new HashSet<int>();
            var missing = false;

            if (stateList != null)
            {
                foreach (var entry in stateList.Entries)
                {
                    if (!seen.Add(entry.Value))
                        continue;

                    list.Add(new ChoiceOption(
                        entry.Value,
                        string.IsNullOrWhiteSpace(entry.Text)
                            ? entry.Value.ToString(CultureInfo.InvariantCulture)
                            : entry.Text));
                }
            }
            else if (!string.IsNullOrWhiteSpace(StateListId))
            {
                // 配了列表但解析不到：删除、坏 Id、或解析器还没注入。
                // 不静默退回 Options——那会让“列表配了却没生效”看起来像“列表本来就没配”。
                missing = true;
            }
            else
            {
                ParseOptions(list, seen);
            }

            SetValue(StateListMissingPropertyKey, missing);
            SetValue(OptionListPropertyKey, list);
            UpdateSelectedText();
        }

        /// <summary>老的 <see cref="Options"/> 解析（StateListId 为空时走这里）</summary>
        private void ParseOptions(ObservableCollection<ChoiceOption> list, HashSet<int> seen)
        {
            string? text = Options;

            if (string.IsNullOrEmpty(text))
                return;

            foreach (string raw in text!.Split(';'))
            {
                string entry = raw.Trim();

                if (entry.Length == 0)
                    continue;

                int separator = entry.IndexOf('=');
                if (separator <= 0)
                    continue; // 缺 "=" 或 "=" 前没有值：非法项

                string valueText = entry.Substring(0, separator).Trim();
                string display = entry.Substring(separator + 1).Trim();

                if (!int.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                    continue;

                if (!seen.Add(value))
                    continue; // 重复值保留第一条，避免 SelectedValue 命中两行

                list.Add(new ChoiceOption(value, display));
            }
        }

        private ScadaStateList? FindStateList()
        {
            var list = ResolveStateList(StateListId);
            if (list != null)
                return list;

            if (string.IsNullOrWhiteSpace(StateListId)
                || !Guid.TryParse(StateListId, out var id))
                return null;

            return RuntimeContext?.StateLists?.FirstOrDefault(item => item.StateListId == id);
        }

        private void SubscribeStateList(ScadaStateList? list)
        {
            if (ReferenceEquals(_subscribedStateList, list))
                return;

            if (_subscribedStateList != null)
                _subscribedStateList.PropertyChanged -= OnStateListChanged;

            _subscribedStateList = list;

            if (list != null)
                list.PropertyChanged += OnStateListChanged;
        }

        private void OnStateListChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 条目增删改都会推高 Version；列表对象换了（删除/重建）也会走到这里。
            if (e.PropertyName == nameof(ScadaStateList.Version))
                ReparseOptions();
        }

        private void UpdateSelectedText()
        {
            var stateList = FindStateList();
            if (stateList != null)
            {
                SetValue(SelectedTextPropertyKey, stateList.DisplayText(Value));
                return;
            }

            if (!string.IsNullOrWhiteSpace(StateListId))
            {
                SetValue(SelectedTextPropertyKey, "未找到文本列表");
                return;
            }

            foreach (var option in OptionList)
            {
                if (option.Value != Value)
                    continue;

                SetValue(SelectedTextPropertyKey, option.Display);
                return;
            }

            SetValue(SelectedTextPropertyKey, "#" + Value.ToString(CultureInfo.InvariantCulture));
        }
    }
}
