using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 数值域图元（TypeKey = <c>Hmi.IOField</c>）：一个带边框的方框，框里一行
    /// 「说明字 + 数值 + 单位」——工业画面上出现频率最高的那一小块（温度 23.5 ℃、计数 1,204 pcs）。
    ///
    /// <b>和文本图元、棒图的边界在哪</b>
    /// ---------
    /// 文本图元（<see cref="TextElement"/>）显示的是"已经定型的字符串"，它不认识数值；
    /// 棒图（<see cref="ProgressBarElement"/>）把一个数值摊成"量程里的长度"，读者一眼看的是比例。
    /// 本图元填的是中间那一格：<b>读者要的是确切的数</b>，且这个数要按固定小数位显示、
    /// 要跟着单位、要有个框把它和背景分开。所以它保留棒图那套"值 + 格式串"，
    /// 丢掉量程与填充，换成"说明字 + 单位"这两段文字。
    ///
    /// <b>为什么值要算成只读依赖属性 <see cref="ValueText"/></b>
    /// ---------
    /// 与棒图把长度算成 <see cref="ProgressBarElement.FillLength"/> 同一套路子：
    /// "数值 → 显示串"这层换算只写一遍（含格式串写错时的兜底），模板只负责把结果贴到 TextBlock 上。
    /// 好处是断言能直接读这个串（"值 23.5 / 格式 0.0 / 单位 ℃ → 「23.5 ℃」"），不必去数像素。
    /// 同理，超限颜色（<see cref="DisplayBrush"/>）与出错描边（<see cref="DisplayStroke"/>）
    /// 也都是"输入属性算出来的只读属性"，模板里一条 <c>DataTrigger</c> 都不用写。
    ///
    /// <b>为什么单位并进同一个串，而不是再放一个 TextBlock</b>
    /// ---------
    /// 分开摆就要处理"单位为空时那块 TextBlock 还占着宽度"的收尾，而两段字之间到底留几个像素
    /// 又会变成第二处可配项。并进 <see cref="ValueText"/> 之后，"单位留空 = 只显数值"是自然结果，
    /// 与时钟图元把标签留空就塌掉整行是同一个口径。
    ///
    /// <b>可读可写（S9）：三种模式 + 缩放 + 上下限</b>
    /// ---------
    /// 组态时选 <see cref="Mode"/>：<see cref="ModeOutput"/>（只读显示，S6 的老行为）、
    /// <see cref="ModeInput"/>（只写：不跟随变量，只把操作员敲的数送下去）、
    /// <see cref="ModeInputOutput"/>（可读可写）。后两种模式下，运行态点一下框子就地变成输入框，
    /// 回车提交、Esc 放弃。提交不自己改值——交给 <see cref="IScadaValueWriter"/> 写回工程变量，
    /// 再由数据泵读回来显示，保证"画面上显示的"与"变量里存的"永远是同一个数。
    ///
    /// <see cref="Gain"/> / <see cref="Offset"/> 是一对工程换算：现场量纲与 HMI 显示量纲不一致时
    /// （PLC 里是 0.1℃ 整数、画面要显示 ℃）用它对齐，公式见两个属性各自的注释。
    /// <see cref="HasMinimum"/> / <see cref="HasMaximum"/> 是输入限制：越界的输入<b>不写下去</b>，
    /// 并把框子描成红色提示，而不是"夹到边界值"——静默改掉操作员敲的数，比拒绝更危险。
    ///
    /// 运行时的接法（S6）：把 <see cref="Value"/> 绑到工程变量上，框里的数就跟着变量走。
    /// </summary>
    [TemplatePart(Name = PartEditor, Type = typeof(TextBox))]
    public class IOFieldElement : ScadaElementBase
    {
        /// <summary>模板部件名：就地编辑用的输入框</summary>
        public const string PartEditor = "PART_Editor";

        /// <summary>模式取值：输出——只读显示（变量的值显示出来，不能改）</summary>
        public const string ModeOutput = "Output";

        /// <summary>模式取值：输入——只写（操作员敲的数写回变量，显示不跟随变量）</summary>
        public const string ModeInput = "Input";

        /// <summary>模式取值：输入输出——可读可写</summary>
        public const string ModeInputOutput = "InputOutput";

        /// <summary>格式类型取值：十进制（配合 <see cref="ValueFormat"/> 使用）</summary>
        public const string FormatDecimal = "Decimal";

        /// <summary>格式类型取值：十六进制（按整数显示，如 1F）</summary>
        public const string FormatHex = "Hex";

        /// <summary>格式类型取值：二进制（按整数显示，如 11111）</summary>
        public const string FormatBinary = "Binary";

        /// <summary><see cref="Value"/> 在描述符里的属性键（提交写回时按它找绑定）</summary>
        private const string ValueKey = "Value";

        /// <summary>出错时框子的描边色（不跟着主题走：报警语义的颜色不该被皮肤改掉）</summary>
        private static readonly Brush ErrorBrush = ScadaBrushes.Frozen("#FFD32F2F");

        /// <summary>当前数值（绑到工程变量上）</summary>
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(IOFieldElement),
            new FrameworkPropertyMetadata(
                0d, FrameworkPropertyMetadataOptions.AffectsRender, OnDisplayInputChanged));

        /// <summary>数值的显示格式（标准 .NET 数字格式串，如 <c>0.0</c> / <c>F2</c> / <c>#,##0</c>）</summary>
        public static readonly DependencyProperty ValueFormatProperty = DependencyProperty.Register(
            nameof(ValueFormat), typeof(string), typeof(IOFieldElement),
            new FrameworkPropertyMetadata(
                "0.##", FrameworkPropertyMetadataOptions.AffectsRender, OnDisplayInputChanged));

        /// <summary>单位（跟在数值后面的一小段字，如 ℃ / mm / pcs；留空则只显数值）</summary>
        public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
            nameof(Unit), typeof(string), typeof(IOFieldElement),
            new FrameworkPropertyMetadata(
                string.Empty, FrameworkPropertyMetadataOptions.AffectsRender, OnDisplayInputChanged));

        /// <summary>模式（见 <see cref="ModeOutput"/> / <see cref="ModeInput"/> / <see cref="ModeInputOutput"/>）</summary>
        public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
            nameof(Mode), typeof(string), typeof(IOFieldElement),
            new FrameworkPropertyMetadata(ModeOutput, OnModeChanged));

        /// <summary>格式类型（见 <see cref="FormatDecimal"/> / <see cref="FormatHex"/> / <see cref="FormatBinary"/>）</summary>
        public static readonly DependencyProperty FormatTypeProperty = DependencyProperty.Register(
            nameof(FormatType), typeof(string), typeof(IOFieldElement),
            new FrameworkPropertyMetadata(
                FormatDecimal, FrameworkPropertyMetadataOptions.AffectsRender, OnDisplayInputChanged));

        /// <summary>显示换算增益（见 <see cref="Gain"/>）</summary>
        public static readonly DependencyProperty GainProperty = DependencyProperty.Register(
            nameof(Gain), typeof(double), typeof(IOFieldElement),
            new FrameworkPropertyMetadata(
                1d, FrameworkPropertyMetadataOptions.AffectsRender, OnDisplayInputChanged));

        /// <summary>显示换算偏移量（见 <see cref="Offset"/>）</summary>
        public static readonly DependencyProperty OffsetProperty = DependencyProperty.Register(
            nameof(Offset), typeof(double), typeof(IOFieldElement),
            new FrameworkPropertyMetadata(
                0d, FrameworkPropertyMetadataOptions.AffectsRender, OnDisplayInputChanged));

        /// <summary>是否启用下限（见 <see cref="HasMinimum"/>）</summary>
        public static readonly DependencyProperty HasMinimumProperty = DependencyProperty.Register(
            nameof(HasMinimum), typeof(bool), typeof(IOFieldElement),
            new FrameworkPropertyMetadata(
                false, FrameworkPropertyMetadataOptions.AffectsRender, OnDisplayInputChanged));

        /// <summary>下限（见 <see cref="Minimum"/>）</summary>
        public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
            nameof(Minimum), typeof(double), typeof(IOFieldElement),
            new FrameworkPropertyMetadata(
                0d, FrameworkPropertyMetadataOptions.AffectsRender, OnDisplayInputChanged));

        /// <summary>是否启用上限（见 <see cref="HasMaximum"/>）</summary>
        public static readonly DependencyProperty HasMaximumProperty = DependencyProperty.Register(
            nameof(HasMaximum), typeof(bool), typeof(IOFieldElement),
            new FrameworkPropertyMetadata(
                false, FrameworkPropertyMetadataOptions.AffectsRender, OnDisplayInputChanged));

        /// <summary>上限（见 <see cref="Maximum"/>）</summary>
        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
            nameof(Maximum), typeof(double), typeof(IOFieldElement),
            new FrameworkPropertyMetadata(
                100d, FrameworkPropertyMetadataOptions.AffectsRender, OnDisplayInputChanged));

        /// <summary>超过上限时数值文字的颜色（见 <see cref="OverMaxColor"/>）</summary>
        public static readonly DependencyProperty OverMaxColorProperty = DependencyProperty.Register(
            nameof(OverMaxColor), typeof(Brush), typeof(IOFieldElement),
            new FrameworkPropertyMetadata(
                ScadaBrushes.Frozen("#FFE53935"), FrameworkPropertyMetadataOptions.AffectsRender, OnDisplayInputChanged));

        /// <summary>低于下限时数值文字的颜色（见 <see cref="UnderMinColor"/>）</summary>
        public static readonly DependencyProperty UnderMinColorProperty = DependencyProperty.Register(
            nameof(UnderMinColor), typeof(Brush), typeof(IOFieldElement),
            new FrameworkPropertyMetadata(
                ScadaBrushes.Frozen("#FFFB8C00"), FrameworkPropertyMetadataOptions.AffectsRender, OnDisplayInputChanged));

        /// <summary>是否正处于就地编辑态（运行态内部状态，不落描述符、不落 .vms）</summary>
        public static readonly DependencyProperty IsEditingProperty = DependencyProperty.Register(
            nameof(IsEditing), typeof(bool), typeof(IOFieldElement),
            new FrameworkPropertyMetadata(false));

        /// <summary>编辑框里的文本（运行态内部状态；双向绑到模板里的输入框）</summary>
        public static readonly DependencyProperty EditTextProperty = DependencyProperty.Register(
            nameof(EditText), typeof(string), typeof(IOFieldElement),
            new FrameworkPropertyMetadata(string.Empty));

        /// <summary>上一次提交失败的原因（运行态内部状态；非空时框子描红并把原因挂在悬停提示上）</summary>
        public static readonly DependencyProperty EditErrorProperty = DependencyProperty.Register(
            nameof(EditError), typeof(string), typeof(IOFieldElement),
            new FrameworkPropertyMetadata(null, OnEditErrorChanged));

        private static readonly DependencyPropertyKey ValueTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(ValueText), typeof(string), typeof(IOFieldElement),
            new PropertyMetadata(string.Empty));

        /// <summary>框里显示的完整数值串（只读；= 数值按格式串格式化 + 单位）</summary>
        public static readonly DependencyProperty ValueTextProperty = ValueTextPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey DisplayBrushPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(DisplayBrush), typeof(Brush), typeof(IOFieldElement),
            new PropertyMetadata(ScadaBrushes.Frozen("#FF202020")));

        /// <summary>数值文字的当前颜色（只读；超限时是超限色，否则是 <see cref="ScadaElementBase.Foreground"/>）</summary>
        public static readonly DependencyProperty DisplayBrushProperty = DisplayBrushPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey DisplayStrokePropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(DisplayStroke), typeof(Brush), typeof(IOFieldElement),
            new PropertyMetadata(null));

        /// <summary>框子描边的当前颜色（只读；提交出错时是红色，否则是 <see cref="ScadaElementBase.Stroke"/>）</summary>
        public static readonly DependencyProperty DisplayStrokeProperty = DisplayStrokePropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey IsEditablePropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(IsEditable), typeof(bool), typeof(IOFieldElement),
            new PropertyMetadata(false));

        /// <summary>此刻能不能改（只读；= 模式可写 且 有写通道 且 Value 绑了变量 且 绑定没停用）</summary>
        public static readonly DependencyProperty IsEditableProperty = IsEditablePropertyKey.DependencyProperty;

        static IOFieldElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(IOFieldElement),
                new FrameworkPropertyMetadata(typeof(IOFieldElement)));
        }

        public IOFieldElement()
        {
            // 先把第一帧算出来。一堆输入的依赖属性默认值与描述符声明的默认值完全一致，
            // 于是 ApplyTargetProperty 发现"值没变"会跳过 SetValue，那些变更回调一个都不会响——
            // 不在这里算一次，单独 new 出来的数值域框里就是空的（时钟图元踩过同一个坑）。
            UpdateDisplay();
            UpdateEditable();
        }

        /// <summary>当前数值（见 <see cref="ValueProperty"/>）</summary>
        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        /// <summary>数值显示格式（见 <see cref="ValueFormatProperty"/>）</summary>
        public string? ValueFormat
        {
            get => (string?)GetValue(ValueFormatProperty);
            set => SetValue(ValueFormatProperty, value);
        }

        /// <summary>单位（见 <see cref="UnitProperty"/>）</summary>
        public string? Unit
        {
            get => (string?)GetValue(UnitProperty);
            set => SetValue(UnitProperty, value);
        }

        /// <summary>
        /// 模式：<see cref="ModeOutput"/> 只读显示 / <see cref="ModeInput"/> 只写 / <see cref="ModeInputOutput"/> 可读可写。
        /// 默认 <see cref="ModeOutput"/>——老方案里的数值域全是只读显示，默认值必须让它们保持原样。
        /// </summary>
        public string? Mode
        {
            get => (string?)GetValue(ModeProperty);
            set => SetValue(ModeProperty, value);
        }

        /// <summary>
        /// 格式类型：<see cref="FormatDecimal"/> / <see cref="FormatHex"/> / <see cref="FormatBinary"/>。
        ///
        /// 只影响显示与就地输入的进制，不改 <see cref="Value"/> 本身——写回变量时交出去的仍是十进制数值串。
        /// 十六进制/二进制按整数处理（工业画面里的位状态、字状态、设备地址都当整数看）。
        /// </summary>
        public string? FormatType
        {
            get => (string?)GetValue(FormatTypeProperty);
            set => SetValue(FormatTypeProperty, value);
        }

        /// <summary>
        /// 显示换算增益：<c>HMI 显示值 = 变量值 × 增益 + 偏移量</c>。
        ///
        /// 反过来，操作员敲进来的显示值要写回变量时用 <c>变量值 = (输入值 − 偏移量) ÷ 增益</c>。
        /// 默认 1（不换算）。为 0 时无法反算，输入会被拒绝而不是算出无穷大。
        /// </summary>
        public double Gain
        {
            get => (double)GetValue(GainProperty);
            set => SetValue(GainProperty, value);
        }

        /// <summary>显示换算偏移量（公式见 <see cref="Gain"/>）</summary>
        public double Offset
        {
            get => (double)GetValue(OffsetProperty);
            set => SetValue(OffsetProperty, value);
        }

        /// <summary>是否启用下限限制；关掉时 <see cref="Minimum"/> 只是个不生效的备用值</summary>
        public bool HasMinimum
        {
            get => (bool)GetValue(HasMinimumProperty);
            set => SetValue(HasMinimumProperty, value);
        }

        /// <summary>下限（显示域，含增益与偏移换算后的量纲）</summary>
        public double Minimum
        {
            get => (double)GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }

        /// <summary>是否启用上限限制；关掉时 <see cref="Maximum"/> 只是个不生效的备用值</summary>
        public bool HasMaximum
        {
            get => (bool)GetValue(HasMaximumProperty);
            set => SetValue(HasMaximumProperty, value);
        }

        /// <summary>上限（显示域，含增益与偏移换算后的量纲）</summary>
        public double Maximum
        {
            get => (double)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        /// <summary>超过上限时数值文字的颜色</summary>
        public Brush? OverMaxColor
        {
            get => (Brush?)GetValue(OverMaxColorProperty);
            set => SetValue(OverMaxColorProperty, value);
        }

        /// <summary>低于下限时数值文字的颜色</summary>
        public Brush? UnderMinColor
        {
            get => (Brush?)GetValue(UnderMinColorProperty);
            set => SetValue(UnderMinColorProperty, value);
        }

        /// <summary>是否正处于就地编辑态（见 <see cref="IsEditingProperty"/>）</summary>
        public bool IsEditing
        {
            get => (bool)GetValue(IsEditingProperty);
            set => SetValue(IsEditingProperty, value);
        }

        /// <summary>编辑框里的文本（见 <see cref="EditTextProperty"/>）</summary>
        public string? EditText
        {
            get => (string?)GetValue(EditTextProperty);
            set => SetValue(EditTextProperty, value);
        }

        /// <summary>上一次提交失败的原因；为 null 表示没有未处理的错误（见 <see cref="EditErrorProperty"/>）</summary>
        public string? EditError
        {
            get => (string?)GetValue(EditErrorProperty);
            set => SetValue(EditErrorProperty, value);
        }

        /// <summary>框里显示的完整数值串（只读；见 <see cref="ValueTextProperty"/>）</summary>
        public string? ValueText => (string?)GetValue(ValueTextProperty);

        /// <summary>数值文字的当前颜色（只读；见 <see cref="DisplayBrushProperty"/>）</summary>
        public Brush? DisplayBrush => (Brush?)GetValue(DisplayBrushProperty);

        /// <summary>框子描边的当前颜色（只读；见 <see cref="DisplayStrokeProperty"/>）</summary>
        public Brush? DisplayStroke => (Brush?)GetValue(DisplayStrokeProperty);

        /// <summary>此刻能不能改（只读；见 <see cref="IsEditableProperty"/>）</summary>
        public bool IsEditable => (bool)GetValue(IsEditableProperty);

        /// <summary>
        /// 进入就地编辑态：把当前显示值填进编辑框、把焦点交给它。
        /// 不可写（只读模式、没绑变量、没写通道）时是空操作——点上去什么也不会发生，
        /// 这正是"输出域点了不该弹出键盘"要的语义。
        /// </summary>
        public void BeginEdit()
        {
            if (!IsEditable || IsEditing)
                return;

            EditText = FormatNumber(DisplayValue());
            EditError = null;
            IsEditing = true;

            if (GetTemplateChild(PartEditor) is TextBox editor)
            {
                editor.Focus();
                editor.SelectAll();
            }
        }

        /// <summary>
        /// 提交编辑：解析 → 上下限 → 反缩放 → 写回变量。
        ///
        /// 任何一步失败都<b>留在编辑态</b>并置 <see cref="EditError"/>：操作员敲的字还在框里，
        /// 改一下就能重提；若失败就退出编辑态，那串字连同错误原因会一起消失，
        /// 操作员只看到"点了没反应"。
        /// </summary>
        public void CommitEdit()
        {
            if (!IsEditing)
                return;

            var text = EditText?.Trim() ?? string.Empty;

            if (text.Length == 0)
            {
                CancelEdit(); // 清空 = 放弃这次输入，与 Esc 同义
                return;
            }

            if (!TryParseEntered(text, out double entered))
            {
                EditError = $"「{text}」不是有效数值，未写入";
                return;
            }

            if (HasMinimum && entered < Minimum)
            {
                EditError = $"低于下限 {FormatNumber(Minimum)}，未写入";
                return;
            }

            if (HasMaximum && entered > Maximum)
            {
                EditError = $"高于上限 {FormatNumber(Maximum)}，未写入";
                return;
            }

            if (Gain == 0d)
            {
                EditError = "显示换算增益为 0，无法算出要写入的变量值";
                return;
            }

            if (RuntimeContext?.Writer is not { } writer)
            {
                EditError = "当前没有写通道，无法写入变量";
                return;
            }

            if (Element is not { } element)
            {
                EditError = "图元还没挂到画面上，无法写入变量";
                return;
            }

            // 不换算时把操作员敲的原文原样交出去：这样"23.50"这种写法与"写变量"动作、
            // 变量管理弹窗走的是同一条转换口径，不会因为这里先转成 double 而丢掉写法。
            //
            // 但十六进制/二进制下不能交原文——"FF"交给变量转换器只会得到"转不成 Double"。
            // 那两种格式只是<b>显示与输入</b>的进制，变量里存的始终是同一个十进制数，
            // 所以这两种情况一律走下面那个已算好的十进制串。
            var raw = (entered - Offset) / Gain;
            var writeText = (Gain == 1d && Offset == 0d && IsDecimalFormat)
                ? text
                : raw.ToString("R", CultureInfo.InvariantCulture);

            if (!writer.TryWriteText(element, ValueKey, writeText, out var error))
            {
                EditError = string.IsNullOrWhiteSpace(error) ? "写入失败" : error;
                return;
            }

            IsEditing = false;
            EditError = null;

            // 成功也不自己改 Value：值由变量持有，数据泵马上会把新值读回来。
            // 唯一例外是"只写"模式——那条变量刷新被本图元拒收了（见 TryApplyRuntimeValue），
            // 不在这里承接一下，框里会一直停在旧数上。
            if (IsInputOnly)
                SetCurrentValue(ValueProperty, raw);

            // "输入完成时"发在最后一行，位置是有讲究的：
            // ① 只发在成功路径上——上面每一条失败分支都已经 return，压根走不到这里，
            //    于是"输入失败也发一次"在结构上就不可能发生（不靠一个额外的 if 去记着判）。
            // ② 发在"只写"模式补值之后，动作拿到的变量值与框里显示的值已经是同一个数；
            //    若发在前面，配在这条事件上的"写变量"动作会把旧值再写一遍。
            // 这里只负责发声：有没有配钩子、该不该执行、按什么顺序执行，全在运行态会话那几道闸门里
            // （见 ScadaElementBase.RaiseScadaEvent 的注释）。
            RaiseScadaEvent(ScadaEventType.InputCompleted);
        }

        /// <summary>放弃编辑：退出编辑态、清掉错误，<see cref="Value"/> 一点没动过</summary>
        public void CancelEdit()
        {
            if (!IsEditing)
                return;

            IsEditing = false;
            EditError = null;
        }

        /// <summary>
        /// 只写模式（<see cref="ModeInput"/>）下拒收变量的刷新：该域的值由操作员敲进去，
        /// 让数据泵把现场值盖上来就会出现"操作员正打字、数字自己跳走"。
        /// </summary>
        public override bool TryApplyRuntimeValue(
            ElementPropertyDescriptor property, object? value, string? format, out string? error)
        {
            ArgumentNullException.ThrowIfNull(property);

            if (IsInputOnly && property.TargetProperty == ValueProperty)
            {
                error = null; // 认下这条刷新（返回 true = 已处理），控件保持操作员敲进去的值
                return true;
            }

            return base.TryApplyRuntimeValue(property, value, format, out error);
        }

        protected override void OnRuntimeContextChanged(ScadaRuntimeContext? oldContext, ScadaRuntimeContext? newContext)
            => UpdateEditable();

        // 收尾钩子：模型刷新时那些输入多半"没变"（默认值就是那几个数），变更回调不会响，
        // 所以刷新末尾必须再算一次，保证"模型 → 控件"这条路每次都把显示与可写性对齐。
        protected override void OnElementRefreshed()
        {
            UpdateDisplay();
            UpdateEditable();
        }

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonDown(e);

            // 走隧道事件（而不是冒泡的 MouseLeftButtonDown）：编辑态下框里是 TextBox，
            // 它会吃掉冒泡的左键按下，冒泡处理器根本收不到第二次点击。
            if (IsEditable && !IsEditing)
            {
                BeginEdit();
                e.Handled = true;
            }
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            if (_editor is not null)
            {
                _editor.KeyDown -= OnEditorKeyDown;
                _editor.LostKeyboardFocus -= OnEditorLostFocus;
            }

            _editor = GetTemplateChild(PartEditor) as TextBox;

            if (_editor is not null)
            {
                _editor.KeyDown += OnEditorKeyDown;
                _editor.LostKeyboardFocus += OnEditorLostFocus;
            }
        }

        private TextBox? _editor;

        private void OnEditorKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    CommitEdit();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    CancelEdit();
                    e.Handled = true;
                    break;
            }
        }

        // 点别处即提交：工业画面上的操作员不会记得按回车，失焦提交才是默认预期。
        private void OnEditorLostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitEdit();

        private static void OnDisplayInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((IOFieldElement)d).UpdateDisplay();

        private static void OnEditErrorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((IOFieldElement)d).UpdateDisplayStroke();

        private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var field = (IOFieldElement)d;
            field.UpdateEditable();
            field.UpdateDisplay();
        }

        /// <summary>只写模式：值只往变量里送，不往画面里拉</summary>
        private bool IsInputOnly => string.Equals(Mode, ModeInput, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 是否十进制格式。十六进制/二进制下输入框里的字与变量里的数不是同一个写法
        /// （"FF" ↔ 255），提交时必须由程序给出十进制串，不能把原文原样交出去（见 <see cref="CommitEdit"/>）。
        /// </summary>
        private bool IsDecimalFormat
            => !string.Equals(FormatType, FormatHex, StringComparison.OrdinalIgnoreCase)
               && !string.Equals(FormatType, FormatBinary, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 显示域的值 = <c>变量值 × 增益 + 偏移量</c>。上下限、超限变色、就地编辑的初值
        /// 全都以它为准（操作员看到的量纲才是他判断"超没超"的依据）。
        /// </summary>
        private double DisplayValue() => Value * Gain + Offset;

        private void UpdateDisplay()
        {
            var display = DisplayValue();
            var text = FormatNumber(display);
            var unit = Unit;

            if (!string.IsNullOrEmpty(unit))
                text = text + " " + unit;

            SetValue(ValueTextPropertyKey, text);
            SetValue(DisplayBrushPropertyKey, ResolveDisplayBrush(display));
            UpdateDisplayStroke();
        }

        private void UpdateDisplayStroke()
            => SetValue(DisplayStrokePropertyKey, string.IsNullOrEmpty(EditError) ? Stroke : ErrorBrush);

        private Brush? ResolveDisplayBrush(double display)
        {
            if (HasMaximum && display > Maximum)
                return OverMaxColor ?? Foreground;

            if (HasMinimum && display < Minimum)
                return UnderMinColor ?? Foreground;

            return Foreground;
        }

        private void UpdateEditable()
        {
            // 三个条件缺一不可：模式允许写、宿主给了写通道、这个域的 Value 真绑了变量。
            // 少任何一条都点不出输入框——"点了没反应"总好过"敲完才告诉你写不了"。
            var editable = !string.Equals(Mode, ModeOutput, StringComparison.OrdinalIgnoreCase)
                           && RuntimeContext?.Writer is not null
                           && Element?.FindBinding(ValueKey) is { IsEnabled: true };

            SetValue(IsEditablePropertyKey, editable);

            if (!editable)
                CancelEdit(); // 运行停了 / 绑定被摘了：正在编辑的框要收掉，否则会留下一个改不动的输入框
        }

        /// <summary>
        /// 显示域的值 → 显示串。十六进制/二进制按整数出（工业画面里的位状态、字状态、设备地址都当整数看），
        /// 十进制走 <see cref="ValueFormat"/>。
        /// </summary>
        private string FormatNumber(double display)
        {
            switch (FormatType)
            {
                case FormatHex:
                    return ((long)Math.Round(display)).ToString("X", CultureInfo.InvariantCulture);
                case FormatBinary:
                    return Convert.ToString((long)Math.Round(display), 2) ?? "0";
                default:
                    // 用不变文化：.vms 是跨机器交换的，显示串也不该跟着系统区域设置变
                    //（德语系统上小数点会变成逗号，同一份方案在两地显示不一样）。
                    var format = string.IsNullOrWhiteSpace(ValueFormat) ? null : ValueFormat;

                    try
                    {
                        return display.ToString(format, CultureInfo.InvariantCulture);
                    }
                    catch (FormatException)
                    {
                        // 格式串写错（如 "0.0.0"）不该让整页渲染中断，退回通用格式
                        return display.ToString(CultureInfo.InvariantCulture);
                    }
            }
        }

        /// <summary>编辑框里敲进来的串 → 显示域的值。进制与 <see cref="FormatNumber"/> 对齐，敲什么进制读什么进制。</summary>
        private bool TryParseEntered(string text, out double entered)
        {
            switch (FormatType)
            {
                case FormatHex:
                    if (long.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
                    {
                        entered = hex;
                        return true;
                    }

                    break;
                case FormatBinary:
                    try
                    {
                        entered = Convert.ToInt64(text, 2);
                        return true;
                    }
                    catch (Exception ex) when (ex is FormatException or ArgumentException or OverflowException)
                    {
                        break;
                    }
                default:
                    return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out entered);
            }

            entered = 0d;
            return false;
        }
    }
}
