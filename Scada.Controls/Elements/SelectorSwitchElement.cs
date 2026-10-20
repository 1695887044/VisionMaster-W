using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 多档旋钮图元（TypeKey = <c>Hmi.SelectorSwitch</c>）：一排 2~4 个档位，点哪一档就写哪一档。
    ///
    /// <b>它和「位按钮」的边界在哪</b>
    /// ---------
    /// 位按钮是"一根开关量"的化身（0/1 两态）；多档旋钮是"一根整数变量"的化身
    /// （手/自动/检修、本地/远程、1#/2#/3# 工位）。现场设备上的转换开关本来就是"拨到某一档并保持"，
    /// 用一排位按钮拼出来的替代品既占地方、又容易误触——本图元就是把那只转换开关搬到画面上。
    ///
    /// <b>写值的口径</b>
    /// ---------
    /// 与数值域、位按钮同一条：控件<b>不自己改 <see cref="Value"/></b>，只把档位号交给
    /// <see cref="IScadaValueWriter"/> 写回工程变量，再由数据泵读回来高亮。
    /// 这样"画面上亮着的档"与"变量里存的档"永远是同一个数；写失败就留在 <see cref="WriteError"/>，
    /// 不假装已经拨过去了。
    ///
    /// <b>为什么点击位置要自己算档位</b>
    /// ---------
    /// 模板里那几块 <c>PART_Opt0..3</c> 是四个独立 Border，点击事件落在整个控件上；
    /// 用"横向位置 ÷ 控件宽度 × 档位数"取整，得到的档位与视觉上的分栏严格一致，
    /// 不依赖模板把四块摆成等宽（ui 侧换成别的布局也仍然对）。
    ///
    /// <b>可编辑的四道门</b>
    /// ---------
    /// 模式不是只读、宿主给了写通道、<see cref="Value"/> 真绑了变量、当前角色过得了权限——
    /// 少任何一条，点击都不写变量（与数值域 <see cref="IOFieldElement.IsEditable"/> 同一口径）。
    /// </summary>
    [TemplatePart(Name = PartOpt0, Type = typeof(Border))]
    [TemplatePart(Name = PartOpt1, Type = typeof(Border))]
    [TemplatePart(Name = PartOpt2, Type = typeof(Border))]
    [TemplatePart(Name = PartOpt3, Type = typeof(Border))]
    [TemplatePart(Name = PartOptText0, Type = typeof(TextBlock))]
    [TemplatePart(Name = PartOptText1, Type = typeof(TextBlock))]
    [TemplatePart(Name = PartOptText2, Type = typeof(TextBlock))]
    [TemplatePart(Name = PartOptText3, Type = typeof(TextBlock))]
    public partial class SelectorSwitchElement : ScadaElementBase
    {
        /// <summary>模板部件名：第 1 档底板</summary>
        public const string PartOpt0 = "PART_Opt0";

        /// <summary>模板部件名：第 2 档底板</summary>
        public const string PartOpt1 = "PART_Opt1";

        /// <summary>模板部件名：第 3 档底板</summary>
        public const string PartOpt2 = "PART_Opt2";

        /// <summary>模板部件名：第 4 档底板</summary>
        public const string PartOpt3 = "PART_Opt3";

        /// <summary>模板部件名：第 1 档文字</summary>
        public const string PartOptText0 = "PART_OptText0";

        /// <summary>模板部件名：第 2 档文字</summary>
        public const string PartOptText1 = "PART_OptText1";

        /// <summary>模板部件名：第 3 档文字</summary>
        public const string PartOptText2 = "PART_OptText2";

        /// <summary>模板部件名：第 4 档文字</summary>
        public const string PartOptText3 = "PART_OptText3";

        /// <summary>模式取值：只读（只看档位，点击不写变量）</summary>
        public const string ModeOutput = "Output";

        /// <summary>模式取值：读写（点击档位写回变量）</summary>
        public const string ModeInputOutput = "InputOutput";

        /// <summary>档位数下限：一档的"多档旋钮"没有意义</summary>
        public const int MinOptionCount = 2;

        /// <summary>档位数上限：模板只提供四个档位部件</summary>
        public const int MaxOptionCount = 4;

        /// <summary><see cref="Value"/> 在描述符里的属性键（写回时按它找绑定）</summary>
        private const string ValueKey = "Value";

        /// <summary>未选中档位的文字色相对选中档位的透明度（次级字色）</summary>
        private const double SecondaryForegroundOpacity = 0.62d;

        // 声明次序 = 属性面板里的行序：数据 → 文字 → 常规 → 外观。

        /// <summary>当前档位（0 起；绑到工程变量上）</summary>
        [ScadaProperty(Key = "Value", DisplayName = "当前档位", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "0", Min = 0, Max = 3, IsBindable = true,
                       Description = "当前选中的档位（0 起）。运行态把工程变量绑到这里；" +
                                     "点击某一档后控件把档位号写回变量，再由数据泵读回来高亮")]
        public partial int Value { get; set; }

        /// <summary>档位数（2~4）</summary>
        [ScadaProperty(Key = "OptionCount", DisplayName = "档位数", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "3", Min = 2, Max = 4,
                       Description = "显示几个档位（2~4）。超出范围的旧数据会收敛到 2~4；" +
                                     "第 3/4 档的底板与文字按这个数显隐")]
        public partial int OptionCount { get; set; }

        /// <summary>第 1 档文字（档位 0）</summary>
        [ScadaProperty(Key = "Option0Text", DisplayName = "第1档文字", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "手动",
                       Description = "档位 0 上显示的字（如「手动」「本地」「1#工位」）")]
        public partial string? Option0Text { get; set; }

        /// <summary>第 2 档文字（档位 1）</summary>
        [ScadaProperty(Key = "Option1Text", DisplayName = "第2档文字", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "自动",
                       Description = "档位 1 上显示的字（如「自动」「远程」「2#工位」）")]
        public partial string? Option1Text { get; set; }

        /// <summary>第 3 档文字（档位 2）</summary>
        [ScadaProperty(Key = "Option2Text", DisplayName = "第3档文字", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "检修",
                       Description = "档位 2 上显示的字（档位数 ≥ 3 时才显示）")]
        public partial string? Option2Text { get; set; }

        /// <summary>第 4 档文字（档位 3）</summary>
        [ScadaProperty(Key = "Option3Text", DisplayName = "第4档文字", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Text, DefaultValue = "备用",
                       Description = "档位 3 上显示的字（档位数 = 4 时才显示）")]
        public partial string? Option3Text { get; set; }

        /// <summary>模式：<see cref="ModeOutput"/> 只读 / <see cref="ModeInputOutput"/> 读写</summary>
        [ScadaProperty(Key = "Mode", DisplayName = "模式", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.General, DefaultValue = "InputOutput",
                       Choices = new[] { "InputOutput", "Output" }, AffectsRender = false,
                       Description = "InputOutput = 点击档位写回变量（还需绑了变量、有写通道、权限够）；" +
                                     "Output = 只看不写，点击不产生任何写入")]
        public partial string? Mode { get; set; }

        /// <summary>未选中档位的底色</summary>
        [ScadaProperty(Key = "TrackColor", DisplayName = "未选中底色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "#FF26313D",
                       Description = "未选中档位的底色；选中档位用「填充色」做底色，两者要有明显明度差")]
        public partial Brush? TrackColor { get; set; }

        private static readonly DependencyPropertyKey IsEditablePropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(IsEditable), typeof(bool), typeof(SelectorSwitchElement),
            new PropertyMetadata(false));

        /// <summary>此刻点击能不能写变量（只读；四道门见类注释）</summary>
        public static readonly DependencyProperty IsEditableProperty = IsEditablePropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey SelectedTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(SelectedText), typeof(string), typeof(SelectorSwitchElement),
            new PropertyMetadata(string.Empty));

        /// <summary>当前档位对应的文字（只读；档位越界时按有效范围收敛）</summary>
        public static readonly DependencyProperty SelectedTextProperty = SelectedTextPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey WriteErrorPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(WriteError), typeof(string), typeof(SelectorSwitchElement),
            new PropertyMetadata(string.Empty));

        /// <summary>上一次点击没能写入的原因（只读；成功时为空串）</summary>
        public static readonly DependencyProperty WriteErrorProperty = WriteErrorPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey EffectiveOptionCountPropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(EffectiveOptionCount), typeof(int), typeof(SelectorSwitchElement),
                new PropertyMetadata(MinOptionCount));

        /// <summary>实际生效的档位数（只读；= <see cref="OptionCount"/> 收敛到 2~4）</summary>
        public static readonly DependencyProperty EffectiveOptionCountProperty =
            EffectiveOptionCountPropertyKey.DependencyProperty;

        static SelectorSwitchElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(SelectorSwitchElement),
                new FrameworkPropertyMetadata(typeof(SelectorSwitchElement)));

            // 选中档是深绿底（见描述符的 Fill 默认值），文字必须是白的；
            // 描述符按契约只暴露 FontSize/FontWeight，不暴露 Foreground，
            // 所以这里把本类型的 Foreground 默认值改成运行态主文字色（样式仍可覆盖）。
            ForegroundProperty.OverrideMetadata(
                typeof(SelectorSwitchElement),
                new FrameworkPropertyMetadata(ScadaBrushes.Frozen("#FFFFFFFF")));
        }

        public SelectorSwitchElement()
        {
            // 默认值与描述符一致，ApplyTargetProperty 会因"值没变"跳过 SetValue，
            // 于是变更钩子一个都不响——构造时必须自己算第一帧（与数值域、罐体同一坑）。
            UpdateVisualState();
        }

        /// <summary>此刻点击能不能写变量（只读，见 <see cref="IsEditableProperty"/>）</summary>
        public bool IsEditable => (bool)GetValue(IsEditableProperty);

        /// <summary>当前档位文字（只读，见 <see cref="SelectedTextProperty"/>）</summary>
        public string? SelectedText => (string?)GetValue(SelectedTextProperty);

        /// <summary>上一次写入失败的原因（对外只读，见 <see cref="WriteErrorProperty"/>）</summary>
        public string? WriteError
        {
            get => (string?)GetValue(WriteErrorProperty);
            private set => SetValue(WriteErrorPropertyKey, value ?? string.Empty);
        }

        /// <summary>实际生效的档位数（只读，见 <see cref="EffectiveOptionCountProperty"/>）</summary>
        public int EffectiveOptionCount => (int)GetValue(EffectiveOptionCountProperty);

        /// <summary>模式是否为只读</summary>
        private bool IsOutputMode => string.Equals(Mode, ModeOutput, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 能不能写：模式允许写、有写通道、Value 真绑了变量且没停用、没被禁用、当前角色够格。
        /// 与 <see cref="IOFieldElement.IsEditable"/> / <see cref="BitButtonElement"/> 同一口径。
        /// </summary>
        private bool CanOperate
            => !IsOutputMode
               && RuntimeContext?.Writer is not null
               && Element?.FindBinding(ValueKey) is { IsEnabled: true }
               && IsEnabled
               && (Element?.RequiredRole is not { } required
                   || RuntimeContext?.AccessPolicy.CanOperate(required, out _) == true);

        protected override void OnRuntimeContextChanged(ScadaRuntimeContext? oldContext, ScadaRuntimeContext? newContext)
            => UpdateVisualState();

        protected override void OnElementRefreshed() => UpdateVisualState();

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            UpdateVisualState();
        }

        /// <summary>
        /// 直接改依赖属性（不经模型）时也要重画：Fill / Foreground 参与高亮配色，
        /// 但它们不是 [ScadaProperty]（基类属性），没有生成钩子。
        /// TrackColor 是 [ScadaProperty]，由 OnTrackColorPropertyChanged 负责，不在这里重复。
        /// </summary>
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);

            if (e.Property == FillProperty
                || e.Property == ForegroundProperty
                || e.Property == IsEnabledProperty)
            {
                UpdateVisualState();
            }
        }

        /// <summary>
        /// 按点击位置选档（鼠标与断言共用的唯一入口）。
        /// </summary>
        /// <param name="x">点击点在控件坐标系里的横坐标</param>
        /// <returns>写入成功返回 true</returns>
        public bool SelectAt(double x)
        {
            if (!(ActualWidth > 0d) || double.IsNaN(x) || double.IsInfinity(x))
            {
                WriteError = "控件还没有完成布局，无法判断档位";
                return false;
            }

            int count = EffectiveOptionCount;
            int index = (int)Math.Floor(x / ActualWidth * count);
            index = Math.Clamp(index, 0, count - 1);

            return Select(index);
        }

        /// <summary>
        /// 选某一档并写回变量。
        /// </summary>
        /// <remarks>
        /// 公开而不是私有：鼠标事件在断言里造不出来（<see cref="MouseEventArgs"/> 没有公开构造），
        /// 而"点击 → 写变量"这条链必须有可驱动的入口——与 <see cref="BitButtonElement.BeginPress"/>
        /// 和 <see cref="IOFieldElement.BeginEdit"/> 是同一个先例。
        /// </remarks>
        public bool Select(int index)
        {
            WriteError = string.Empty;

            if (!IsEditable)
            {
                WriteError = DescribeRejection();
                return false;
            }

            int count = EffectiveOptionCount;

            if (index < 0 || index >= count)
            {
                WriteError = $"档位 {index} 超出有效范围 0~{count - 1}，未写入";
                return false;
            }

            if (RuntimeContext?.Writer is not { } writer)
            {
                WriteError = "当前没有写通道，无法写入变量";
                return false;
            }

            if (Element is not { } element)
            {
                WriteError = "图元还没挂到画面上，无法写入变量";
                return false;
            }

            if (!writer.TryWriteText(element, ValueKey, index.ToString(CultureInfo.InvariantCulture), out var error))
            {
                WriteError = string.IsNullOrWhiteSpace(error) ? "写入失败" : error;
                return false;
            }

            // 不在这里 RaiseScadaEvent(Pressed)：画布在运行态已经对命中的图元统一发一次 Pressed
            //（ScadaCanvas.Interaction），控件再发一次会让同一条事件钩子执行两遍。
            // 写失败时本来也不该发声；成功路径只负责写变量，事件源保持画布唯一。
            return true;
        }

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonDown(e);

            if (e.Handled || !IsEditable)
                return;

            SelectAt(e.GetPosition(this).X);
        }

        partial void OnValuePropertyChanged() => UpdateVisualState();
        partial void OnOptionCountPropertyChanged() => UpdateVisualState();
        partial void OnOption0TextPropertyChanged() => UpdateVisualState();
        partial void OnOption1TextPropertyChanged() => UpdateVisualState();
        partial void OnOption2TextPropertyChanged() => UpdateVisualState();
        partial void OnOption3TextPropertyChanged() => UpdateVisualState();
        partial void OnModePropertyChanged() => UpdateVisualState();

        private void UpdateVisualState()
        {
            int count = Math.Clamp(OptionCount, MinOptionCount, MaxOptionCount);
            int index = Math.Clamp(Value, 0, count - 1);

            SetValue(EffectiveOptionCountPropertyKey, count);
            SetValue(SelectedTextPropertyKey, OptionText(index));
            SetValue(IsEditablePropertyKey, CanOperate);

            var selectedFill = Fill ?? Brushes.Transparent;
            var unselectedFill = TrackColor ?? Brushes.Transparent;
            var selectedForeground = Foreground ?? Brushes.Transparent;
            var unselectedForeground = SecondaryForeground(Foreground);

            for (int i = 0; i < MaxOptionCount; i++)
            {
                bool visible = i < count;
                bool selected = i == index;

                if (GetTemplateChild(BorderName(i)) is Border border)
                {
                    border.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    border.Background = selected ? selectedFill : unselectedFill;
                }

                if (GetTemplateChild(TextName(i)) is TextBlock text)
                {
                    text.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                    text.Text = OptionText(i);
                    text.Foreground = selected ? selectedForeground : unselectedForeground;
                }
            }
        }

        /// <summary>档位越界/只读时给操作员的原因（失败一定要说清是哪道门没过）</summary>
        private string DescribeRejection()
        {
            if (IsOutputMode)
                return "当前为只读模式，未写入";

            if (RuntimeContext?.Writer is null)
                return "当前没有写通道，无法写入变量";

            if (Element?.FindBinding(ValueKey) is not { IsEnabled: true })
                return "「当前档位」没有绑定可用的变量，未写入";

            if (!IsEnabled)
                return "图元已禁用，未写入";

            return "当前角色没有操作权限，未写入";
        }

        private string OptionText(int index) => index switch
        {
            0 => Option0Text ?? string.Empty,
            1 => Option1Text ?? string.Empty,
            2 => Option2Text ?? string.Empty,
            _ => Option3Text ?? string.Empty,
        };

        private static string BorderName(int index) => index switch
        {
            0 => PartOpt0,
            1 => PartOpt1,
            2 => PartOpt2,
            _ => PartOpt3,
        };

        private static string TextName(int index) => index switch
        {
            0 => PartOptText0,
            1 => PartOptText1,
            2 => PartOptText2,
            _ => PartOptText3,
        };

        /// <summary>
        /// 次级文字色：拿 <see cref="Control.Foreground"/> 降透明度，而不是另立一个可配颜色。
        /// 这样"选中/未选中"的层级在任何主题下都跟着主文字色走，不会出现"换了深色皮肤，
        /// 未选中的字反而比选中的亮"这种要逐图元调色的局面。
        /// </summary>
        private static Brush SecondaryForeground(Brush? foreground)
        {
            if (foreground is SolidColorBrush solid)
            {
                var color = solid.Color;
                byte alpha = (byte)Math.Round(color.A * SecondaryForegroundOpacity);
                return ScadaBrushes.Frozen(Color.FromArgb(alpha, color.R, color.G, color.B));
            }

            return foreground ?? Brushes.Transparent;
        }
    }
}
