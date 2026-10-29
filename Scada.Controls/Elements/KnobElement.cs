using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 旋钮图元（TypeKey = <c>Hmi.Knob</c>）：连续量的"拖动即改值"操作件，形态是一圈刻度 + 一段进度弧 + 一根指针。
    ///
    /// <b>写值语义与 <see cref="SliderElement"/> 完全一致</b>（同一份契约，不要各写一套）：
    /// <list type="number">
    /// <item>拖动只改<b>显示值</b>，松手（<c>MouseLeftButtonUp</c> / <c>LostMouseCapture</c>）才写一次变量；</item>
    /// <item>写之前判<b>权限</b>（<see cref="ScadaRuntimeContext.AccessPolicy"/> 的 <c>CanElement</c>）与
    ///       <b>写通道</b>（<see cref="ScadaRuntimeContext.Writer"/>）；</item>
    /// <item>写失败<b>回弹</b>到写前的值（模型 <see cref="Value"/>，即数据泵读回的当前值），
    ///       并把原因放进 <see cref="WriteError"/> + 挂到 ToolTip + 在框边画一圈报警红——
    ///       不许停在"看起来已经改了"的位置。</item>
    /// </list>
    ///
    /// <b>为什么拖动按"垂直位移"而不是"鼠标绕圈的角度"</b>
    /// ---------
    /// 绕圈映射在圆心的正上方/正下方附近有一处奇点（角度跳 180°），手指划过那一点时值会瞬间从 0 跳到满量程——
    /// 那是"看起来能用、现场一定骂人"的交互。垂直位移没有奇点，且与滑块同一套肌肉记忆：
    /// 往上拖 = 增大，每像素 0.5% 量程（<see cref="RangePerPixel"/>），整段量程约 200 像素拖完。
    ///
    /// <b>与圆形进度条"联动"是怎么实现的</b>
    /// ---------
    /// 旋钮自己画"已转过的那段弧"（<see cref="StartAngle"/> / <see cref="SweepAngle"/> / <see cref="TrackColor"/> /
    /// <see cref="KnobColor"/> 与圆形进度条同一组默认值，所以两者默认长得就是同一套视觉语言）。
    /// <b>联动 = 把同一个工程变量绑到两者的「当前值」上</b>：数据泵一拍写两处，两个图元各自按自己的量程画。
    /// 控件里<b>不引用</b>另一个图元（跨图元耦合会让"删掉一个"变成"另一个也坏"），这条注释就是全部约定。
    ///
    /// <b>几何为什么全部画在模板里的子元素上，而不是控件自己的 OnRender</b>
    /// ---------
    /// 本控件的模板根是一个带 <c>Fill</c> 背景的 <c>Border</c>，而模板子元素画在控件自身渲染的<b>上层</b>——
    /// 控件 OnRender 画的东西会被那块背景整个盖住（静默，看起来像"什么都没画"）。
    /// 所以几何交给模板里的 <see cref="KnobSurface"/>（<c>PART_Surface</c>），它只读本控件的状态。
    /// </summary>
    [TemplatePart(Name = PartSurface, Type = typeof(KnobSurface))]
    public partial class KnobElement : ScadaElementBase
    {
        /// <summary>模板部件名：自绘旋钮面（刻度 / 轨道弧 / 进度弧 / 指针 / 读数）</summary>
        public const string PartSurface = "PART_Surface";

        private const string ValueKey = "Value";

        /// <summary>数值格式写坏时的兜底格式（不显示成空串，也不抛）</summary>
        private const string FallbackFormat = "F0";

        /// <summary>拖动灵敏度：每像素 0.5% 量程（向上 = 增大，整段量程约 200 像素拖完）</summary>
        private const double RangePerPixel = 0.005d;

        private KnobSurface? _surface;
        private double _displayValue;
        private bool _dragging;
        private double _dragStartY;
        private double _dragStartValue;

        // 声明次序 = 属性面板里的行序（生成器按源码次序收集）：数据 → 刻度 → 操作 → 外观。

        /// <summary>当前值（绑到工程变量上；拖动期间不改它，松手才写）</summary>
        [ScadaProperty(Key = "Value", DisplayName = "当前值", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "0", IsBindable = true,
                       Description = "绑到工程变量上。拖动只改显示，松手（滚轮/方向键则立即）写一次变量；" +
                                     "写失败回弹到这个值。与圆形进度条绑同一个变量，两者即联动")]
        public partial double Value { get; set; }

        /// <summary>量程下限</summary>
        [ScadaProperty(Key = "Min", DisplayName = "最小值", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Scale, DefaultValue = "0",
                       Description = "下限不小于上限时比例按 0 处理，不会让非法量程打断渲染")]
        public partial double Min { get; set; }

        /// <summary>量程上限</summary>
        [ScadaProperty(Key = "Max", DisplayName = "最大值", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Scale, DefaultValue = "100")]
        public partial double Max { get; set; }

        /// <summary>步长（吸附与滚轮/方向键的步进量；≤0 时按不吸附处理）</summary>
        [ScadaProperty(Key = "Step", DisplayName = "步长", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Scale, DefaultValue = "1", Min = 0,
                       Description = "滚轮/方向键每次改变多少；打开「按步长吸附」时松手也会吸附到它的整数倍。" +
                                     "填 0 或负数 = 不吸附，滚轮/方向键改按量程的 1% 步进")]
        public partial double Step { get; set; }

        /// <summary>起始角度（度，WPF 约定：0° 向右、顺时针为正）</summary>
        [ScadaProperty(Key = "StartAngle", DisplayName = "起始角度", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Scale, DefaultValue = "-135", Min = -360, Max = 360,
                       Description = "度。0° 指向右（3 点钟）、顺时针增加，所以 -135° 是左下；" +
                                     "默认 -135° 配 270° 让缺口落在正下方——与圆形进度条同一组默认值")]
        public partial double StartAngle { get; set; }

        /// <summary>扫过角度（度；负值表示逆时针）</summary>
        [ScadaProperty(Key = "SweepAngle", DisplayName = "扫过角度", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Scale, DefaultValue = "270", Min = -360, Max = 360,
                       Description = "度。正数顺时针、负数逆时针，绝对值 360 画整圈；超出 ±360 会收敛（刻度不会绕回来叠在一起）")]
        public partial double SweepAngle { get; set; }

        /// <summary>刻度线根数（含两端；只影响绘制，不影响取值）</summary>
        [ScadaProperty(Key = "TickCount", DisplayName = "刻度数", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Scale, DefaultValue = "11", Min = 2, Max = 61,
                       Description = "沿量程均布的刻度线根数（含两端），只影响绘制，不参与取值；" +
                                     "默认 11 根 = 0/10/20…100 十等分")]
        public partial int TickCount { get; set; }

        /// <summary>是否按步长吸附</summary>
        [ScadaProperty(Key = "SnapToStep", DisplayName = "按步长吸附", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Operation, DefaultValue = "True",
                       Description = "打开时松手/滚轮/方向键写出去的值都是步长的整数倍（从「最小值」起算）；" +
                                     "关闭后拖动可写出任意连续值")]
        public partial bool SnapToStep { get; set; }

        /// <summary>是否显示数值</summary>
        [ScadaProperty(Key = "ShowValue", DisplayName = "显示数值", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "True",
                       Description = "关掉只留指针与刻度（读数由旁边的数值域承担）")]
        public partial bool ShowValue { get; set; }

        /// <summary>数值显示格式（标准 .NET 数字格式串）</summary>
        [ScadaProperty(Key = "ValueFormat", DisplayName = "数值格式", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "F0",
                       Description = "标准 .NET 数字格式串（F0 取整、F1 一位小数、0.# 去尾零）；" +
                                     "写错退回 F0，不让画面出错")]
        public partial string? ValueFormat { get; set; }

        /// <summary>旋钮色（进度弧 + 指针）</summary>
        [ScadaProperty(Key = "KnobColor", DisplayName = "旋钮色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "#FF2D7DD2",
                       Description = "已转过的那段弧与指针的颜色；与圆形进度条的「进度色」取同一个默认值，绑同一变量时观感一致")]
        public partial Brush? KnobColor { get; set; }

        /// <summary>轨道色（未走过的那段弧 + 刻度线 + 旋钮盘面）</summary>
        [ScadaProperty(Key = "TrackColor", DisplayName = "轨道色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "#FF2A3745",
                       Description = "未走过的那段弧、刻度线与旋钮盘面的颜色；要和「旋钮色」有明度差，否则看不出转了多少")]
        public partial Brush? TrackColor { get; set; }

        private static readonly DependencyPropertyKey IsEditablePropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(IsEditable), typeof(bool), typeof(KnobElement),
            new PropertyMetadata(false));

        /// <summary>此刻能不能写变量（只读；四道门见 <see cref="CanOperate"/>）</summary>
        public static readonly DependencyProperty IsEditableProperty = IsEditablePropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey WriteErrorPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(WriteError), typeof(string), typeof(KnobElement),
            new PropertyMetadata(string.Empty));

        /// <summary>上一次写入失败的原因（只读；成功/未写时为空串）</summary>
        public static readonly DependencyProperty WriteErrorProperty = WriteErrorPropertyKey.DependencyProperty;

        static KnobElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(KnobElement),
                new FrameworkPropertyMetadata(typeof(KnobElement)));
        }

        public KnobElement()
        {
            Focusable = true;
            IsTabStop = true;
            _displayValue = Value;
        }

        /// <summary>此刻能不能写变量（只读；见 <see cref="KnobElement"/> 类注释里的四道门）</summary>
        public bool IsEditable => (bool)GetValue(IsEditableProperty);

        /// <summary>上一次写入失败的原因（可直接展示给操作员的中文；成功时为空串）</summary>
        public string? WriteError
        {
            get => (string?)GetValue(WriteErrorProperty);
            private set
            {
                SetValue(WriteErrorPropertyKey, value ?? string.Empty);
                UpdateVisuals();
            }
        }

        /// <summary>当前显示值（拖动中会与模型 <see cref="Value"/> 不同；写成功后由数据泵追平）</summary>
        public double DisplayValue => _displayValue;

        /// <summary>显示值在量程里的比例（0~1；<c>Max&lt;=Min</c> 时为 0，不除零）</summary>
        public double ValueRatio => ComputeRatio(_displayValue);

        /// <summary>显示值的格式化文本（<see cref="ShowValue"/> 只决定画不画，不改变这里的结果）</summary>
        public string DisplayText
        {
            get
            {
                if (!double.IsFinite(_displayValue))
                    return "—";

                var format = NormalizeFormat(ValueFormat);

                try
                {
                    return _displayValue.ToString(format, CultureInfo.InvariantCulture);
                }
                catch (FormatException)
                {
                    return _displayValue.ToString(FallbackFormat, CultureInfo.InvariantCulture);
                }
            }
        }

        /// <summary>
        /// 格式串体检：只认"标准数值格式符"（F0 / F2 / N1 / G …）或"含占位符 0/# 的自定义格式"。
        ///
        /// <b>为什么不能只靠 try/catch</b>：.NET 对"没有占位符的自定义格式串"（用户手打了一句「这是格式」）
        /// <b>不抛异常</b>，而是把格式串原样当结果显示——画面上就冒出一行「这是格式」，
        /// 比抛异常更难查（滑块的「0.#」注释里记的是同一个坑）。
        /// 所以先体检格式串的形状，形状不对直接按坏格式回落 F0。
        /// </summary>
        private static string NormalizeFormat(string? format)
        {
            if (string.IsNullOrWhiteSpace(format))
                return FallbackFormat;

            foreach (var ch in format)
            {
                if (ch is '0' or '#')
                    return format; // 自定义格式：有数字占位符（"0.0 ℃" / "###,###"）
            }

            var head = format[0];

            if (format.Length <= 3
                && (head is 'F' or 'f' or 'N' or 'n' or 'E' or 'e' or 'G' or 'g'
                    or 'C' or 'c' or 'P' or 'p' or 'D' or 'd' or 'X' or 'x' or 'R' or 'r'))
            {
                return format; // 标准格式符（可带精度）
            }

            return FallbackFormat;
        }

        /// <summary>是否正在拖动（拖动期间不写变量）</summary>
        public bool IsDragging => _dragging;

        /// <summary>设计期（没有运行上下文）：可拖动预览，但绝不写变量</summary>
        public bool IsDesignTime => RuntimeContext is null;

        /// <summary>
        /// 四道门：有写通道、<see cref="Value"/> 真绑了可用变量、图元启用、当前角色过得了权限。
        /// 设计期没有运行上下文，因此恒为 false——那时"能拖"是另一条路径（见 <see cref="CanDrag"/>）。
        /// </summary>
        private bool CanOperate
            => RuntimeContext is not null
               && RuntimeContext.Writer is not null
               && Element?.FindBinding(ValueKey) is { IsEnabled: true }
               && IsEnabled
               && RuntimeContext.AccessPolicy.CanElement(Element);

        /// <summary>能不能进入拖动：运行态要过四道门，设计期只要启用（预览，不写值）</summary>
        private bool CanDrag => IsEnabled && (RuntimeContext is null || CanOperate);

        private double SafeMin => double.IsFinite(Min) ? Min : 0d;

        private double SafeMax
        {
            get
            {
                var min = SafeMin;
                return double.IsFinite(Max) ? Max : min;
            }
        }

        private double Range => SafeMax - SafeMin;

        /// <summary>给自绘面读的起始角（NaN/∞ 收敛到 0）</summary>
        internal double NormalizedStartAngle => double.IsFinite(StartAngle) ? StartAngle : 0d;

        /// <summary>给自绘面读的扫过角（NaN/∞ 收敛到 0，绝对值收敛到 360）</summary>
        internal double NormalizedSweepAngle
            => double.IsFinite(SweepAngle) ? Math.Clamp(SweepAngle, -360d, 360d) : 0d;

        protected override void OnRuntimeContextChanged(ScadaRuntimeContext? oldContext, ScadaRuntimeContext? newContext)
            => UpdateEditable();

        protected override void OnElementRefreshed()
        {
            // 描述符按源码次序落地（Value 在 Min/Max 之前），逐个钩子用当时的默认量程夹会把
            // 150/200 提前夹成 100。等所有属性都落地后再按最终量程夹一次（与滑块同一条纪律）。
            if (!_dragging)
                _displayValue = Clamp(Value);

            UpdateEditable();
            UpdateVisuals();
        }

        public override void OnApplyTemplate()
        {
            if (_surface is not null)
                _surface.Owner = null;

            base.OnApplyTemplate();

            _surface = GetTemplateChild(PartSurface) as KnobSurface;

            if (_surface is not null)
                _surface.Owner = this;

            UpdateEditable();
            UpdateVisuals();
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);

            // 基类依赖属性（Fill/Stroke/字体/文字/启用态）没有生成钩子，这里补一次；
            // 自绘面只认这些输入，控件自身不重画，所以必须显式让它 InvalidateVisual。
            if (e.Property == IsEnabledProperty)
            {
                UpdateEditable();
                return;
            }

            if (e.Property == FillProperty
                || e.Property == StrokeProperty
                || e.Property == StrokeThicknessProperty
                || e.Property == ForegroundProperty
                || e.Property == FontFamilyProperty
                || e.Property == FontSizeProperty
                || e.Property == FontWeightProperty
                || e.Property == FontStyleProperty
                || e.Property == FontStretchProperty
                || e.Property == TextProperty)
            {
                UpdateVisuals();
            }
        }

        // ---- [ScadaProperty] 生成钩子：凡影响画面的都让自绘面重画 ----

        partial void OnValuePropertyChanged()
        {
            // 这里不 Clamp：描述符落地次序是 Value → Min → Max，此刻的量程可能还是默认值。
            // 最终量程在 OnElementRefreshed 里统一夹一次（与滑块同一处口径）。
            if (!_dragging)
                _displayValue = double.IsFinite(Value) ? Value : SafeMin;

            UpdateVisuals();
        }

        partial void OnMinPropertyChanged()
        {
            _displayValue = Clamp(_displayValue);
            UpdateVisuals();
        }

        partial void OnMaxPropertyChanged()
        {
            _displayValue = Clamp(_displayValue);
            UpdateVisuals();
        }

        partial void OnStepPropertyChanged() => UpdateVisuals();
        partial void OnStartAnglePropertyChanged() => UpdateVisuals();
        partial void OnSweepAnglePropertyChanged() => UpdateVisuals();
        partial void OnTickCountPropertyChanged() => UpdateVisuals();
        partial void OnSnapToStepPropertyChanged() => UpdateVisuals();
        partial void OnShowValuePropertyChanged() => UpdateVisuals();
        partial void OnValueFormatPropertyChanged() => UpdateVisuals();
        partial void OnKnobColorPropertyChanged() => UpdateVisuals();
        partial void OnTrackColorPropertyChanged() => UpdateVisuals();

        // ---- 交互入口（鼠标 / 滚轮 / 键盘 / 断言共用同一条 Commit 路径）----

        /// <summary>
        /// 开始拖动（真实鼠标与断言共用入口）。<paramref name="pointerY"/> 是按下点的控件内 Y 坐标，
        /// 后续 <see cref="DragTo"/> 用它算垂直位移。
        /// 不可写时不开始，原因写进 <see cref="WriteError"/>（ToolTip 同步）。
        /// </summary>
        public bool BeginDrag(double pointerY, bool captureMouse = true)
        {
            WriteError = string.Empty;

            if (!CanDrag)
            {
                WriteError = DescribeRejection();
                return false;
            }

            if (double.IsNaN(pointerY) || double.IsInfinity(pointerY))
                return false;

            // 真实鼠标路径必须拿到捕获；断言路径可以传 false（没有 PresentationSource 时
            // CaptureMouse 必然失败，不能因此把逻辑测试挡在门外）——与滑块同一口径。
            if (captureMouse && !CaptureMouse())
            {
                WriteError = "无法捕获鼠标，未开始拖动";
                return false;
            }

            _dragging = true;
            _dragStartY = pointerY;
            _dragStartValue = _displayValue;
            UpdateVisuals();
            return true;
        }

        /// <summary>拖动到某个指针 Y：只改显示（向上 = 增大，每像素 0.5% 量程），不写变量。</summary>
        public void DragTo(double pointerY)
        {
            if (!_dragging || double.IsNaN(pointerY) || double.IsInfinity(pointerY))
                return;

            var range = Range;

            if (!(range > 0d))
                return; // 非法量程：拖不动，也不除零

            var delta = (_dragStartY - pointerY) * RangePerPixel * range;
            _displayValue = Clamp(_dragStartValue + delta);
            UpdateVisuals();
        }

        /// <summary>松手：写一次最终值（失败回弹并保留原因）。值没变则不写（点一下只是聚焦）。</summary>
        public bool EndDrag()
        {
            if (!_dragging)
                return false;

            _dragging = false;
            ReleaseMouseCapture();

            if (Math.Abs(_displayValue - Value) < 1e-9)
                return true;

            return Commit(_displayValue);
        }

        /// <summary>键盘/滚轮步进：立即写一次（与松手同一出口）。</summary>
        public bool Nudge(double delta)
        {
            WriteError = string.Empty;

            if (!CanDrag)
            {
                WriteError = DescribeRejection();
                return false;
            }

            return Commit(_displayValue + delta);
        }

        /// <summary>
        /// <b>唯一写值出口</b>：夹到 [Min,Max] → 按 <see cref="SnapToStep"/> 吸附到 <see cref="Step"/> 的整数倍 →
        /// 权限/写通道检查 → 写一次 → 失败回弹 + 给原因。
        /// 设计期（<see cref="IsDesignTime"/>）只改显示，不写变量。
        /// </summary>
        public bool Commit(double value)
        {
            WriteError = string.Empty;

            var target = Clamp(value);

            if (SnapToStep)
                target = Clamp(Snap(target));

            _displayValue = target;
            UpdateVisuals();

            if (RuntimeContext is null)
                return true; // 设计期：只改显示（ToolTip 已写明"设计期不写值"）

            return WriteCurrent();
        }

        /// <summary>测试/脚本入口：与鼠标、滚轮、方向键走同一条 <see cref="Commit"/>。</summary>
        public void SetValueFromUi(double raw) => Commit(raw);

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonDown(e);

            if (e.Handled)
                return;

            if (!CanDrag)
            {
                // 不进入拖动，但要把原因留下（ToolTip 显示）——与滑块同一条口径：
                // 没有权限/没有写通道时"点了没反应"比"点了说清楚"更伤人。
                WriteError = DescribeRejection();
                return;
            }

            Focus();

            if (BeginDrag(e.GetPosition(this).Y))
                e.Handled = true;
        }

        protected override void OnPreviewMouseMove(MouseEventArgs e)
        {
            base.OnPreviewMouseMove(e);

            if (!_dragging)
                return;

            // 左键已经松开（MouseUp 因切窗/系统抢占丢失）：按松手处理，写最终值。
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndDrag();
                return;
            }

            DragTo(e.GetPosition(this).Y);
            e.Handled = true;
        }

        protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonUp(e);

            if (!_dragging)
                return;

            DragTo(e.GetPosition(this).Y);
            EndDrag();
            e.Handled = true;
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);

            // 捕获被系统抢走（切窗口/弹窗）也要把最终值写掉，不能停在拖动中途。
            if (_dragging)
            {
                _dragging = false;
                Commit(_displayValue);
            }
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);

            if (e.Handled)
                return;

            if (!CanDrag)
            {
                WriteError = DescribeRejection();
                return;
            }

            var step = EffectiveStep();

            if (!(step > 0d))
                return;

            // 滚轮是离散操作，不需要"松手"——立即写（与拖动松手同一出口）。
            Commit(_displayValue + (e.Delta >= 0 ? step : -step));
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (!CanDrag)
                return;

            var step = EffectiveStep();
            var handled = true;

            switch (e.Key)
            {
                case Key.Up:
                case Key.Right:
                    if (step > 0d)
                        Commit(_displayValue + step);
                    break;

                case Key.Down:
                case Key.Left:
                    if (step > 0d)
                        Commit(_displayValue - step);
                    break;

                case Key.Home:
                    Commit(SafeMin);
                    break;

                case Key.End:
                    Commit(SafeMax);
                    break;

                default:
                    handled = false;
                    break;
            }

            e.Handled = handled;
        }

        // ---- 写值与回弹 ----

        private bool WriteCurrent()
        {
            if (!CanOperate)
            {
                WriteError = DescribeRejection();
                ResetDisplay();
                return false;
            }

            if (RuntimeContext?.Writer is not { } writer || Element is not { } element)
            {
                WriteError = "当前没有写通道，无法写入变量";
                ResetDisplay();
                return false;
            }

            var valueToWrite = _displayValue;
            var text = valueToWrite.ToString("R", CultureInfo.InvariantCulture);

            if (!writer.TryWriteText(element, ValueKey, text, out var error))
            {
                // 写失败必须回弹：停在"看起来已经改了"的位置比报错更危险。
                WriteError = string.IsNullOrWhiteSpace(error) ? "写入失败，已回弹" : error;
                ResetDisplay();
                return false;
            }

            _displayValue = Clamp(valueToWrite);
            WriteError = string.Empty;
            UpdateVisuals();
            RaiseScadaEvent(ScadaEventType.InputCompleted);
            RaiseScadaEvent(ScadaEventType.ObjectChanged);
            return true;
        }

        /// <summary>把显示值退回模型值（数据泵读回的当前值）。</summary>
        private void ResetDisplay()
        {
            _displayValue = Clamp(Value);
            UpdateVisuals();
        }

        private string DescribeRejection()
        {
            if (!IsEnabled)
                return "图元已禁用，未写入";

            if (RuntimeContext is null)
                return "设计期不写值（只改显示）";

            if (RuntimeContext.Writer is null)
                return "当前没有写通道，无法写入变量";

            if (Element?.FindBinding(ValueKey) is not { IsEnabled: true })
                return "「当前值」没有绑定可用的变量，未写入";

            return "当前角色没有操作权限，未写入";
        }

        private void UpdateEditable()
        {
            SetValue(IsEditablePropertyKey, CanOperate);

            if (!CanDrag && _dragging)
            {
                _dragging = false;
                ReleaseMouseCapture();
                ResetDisplay();
            }

            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            _surface?.InvalidateVisual();
            UpdateToolTip();
        }

        private void UpdateToolTip()
        {
            if (RuntimeContext is null)
            {
                ToolTip = "设计期不写值：可拖动预览（上下拖动改显示），运行态才写变量";
                return;
            }

            if (!string.IsNullOrEmpty(WriteError))
            {
                ToolTip = WriteError;
                return;
            }

            ToolTip = IsEditable
                ? "上下拖动 / 滚轮 / 方向键改值；松手写一次变量"
                : DescribeRejection();
        }

        // ---- 量程换算（一处口径，全部非有限输入都有收敛值）----

        private double Clamp(double value)
        {
            var min = SafeMin;
            var max = SafeMax;

            if (!double.IsFinite(value))
                return min; // NaN/∞ 按 Min 处理

            if (max <= min)
                return min; // 非法量程：收敛到下限，不除零

            return Math.Clamp(value, min, max);
        }

        private double Snap(double value)
        {
            var step = Step;

            if (!double.IsFinite(step) || !(step > 0d))
                return value; // ≤0 或非法步长 = 不吸附

            var min = SafeMin;
            var steps = Math.Round((value - min) / step, MidpointRounding.AwayFromZero);

            return min + steps * step;
        }

        /// <summary>滚轮/方向键的步进量：<see cref="Step"/>；≤0 时按量程 1%（量程非法时为 0）。</summary>
        private double EffectiveStep()
        {
            if (double.IsFinite(Step) && Step > 0d)
                return Step;

            var range = Range;
            return range > 0d ? range * 0.01d : 0d;
        }

        private double ComputeRatio(double value)
        {
            var min = SafeMin;
            var max = SafeMax;

            if (!double.IsFinite(value) || max <= min)
                return 0d;

            var ratio = (value - min) / (max - min);
            return double.IsFinite(ratio) ? Math.Clamp(ratio, 0d, 1d) : 0d;
        }
    }

    /// <summary>
    /// 旋钮的自绘面（<c>PART_Surface</c>）：轨道弧 + 进度弧 + 刻度线 + 盘面 + 指针 + 读数。
    ///
    /// 为什么是"模板里的子元素"而不是控件自己的 <c>OnRender</c>：模板根 <c>Border</c> 带背景，
    /// 模板子元素画在控件自身渲染的上层，控件 OnRender 画的东西会被整块盖住（静默）。
    ///
    /// 为什么画在代码里而不是模板拼 XAML：弧的端点、刻度的每个端点、指针长度都依赖"此刻多大"，
    /// 与 <see cref="GaugeElement"/> 同一条纪律——模板里一个算术都没有。
    ///
    /// 只读 <see cref="Owner"/> 的状态，自己不存任何值：状态只有一份（控件上那堆依赖属性）。
    /// </summary>
    public sealed class KnobSurface : FrameworkElement
    {
        /// <summary>写失败时框边那一圈报警红（与滑块的失败红同色）</summary>
        private static readonly Brush ErrorBrush = ScadaBrushes.Frozen("#FFEF4444");

        /// <summary>颜色属性被清空（设为 null）时的兜底画刷，与两个属性的默认值同色</summary>
        private static readonly Brush TrackFallback = ScadaBrushes.Frozen("#FF2A3745");
        private static readonly Brush KnobFallback = ScadaBrushes.Frozen("#FF2D7DD2");
        private static readonly Brush StrokeFallback = ScadaBrushes.Frozen("#FF2A3745");
        private static readonly Brush TextFallback = ScadaBrushes.Frozen("#FFE6EEF7");

        /// <summary>宿主旋钮（由 <see cref="KnobElement.OnApplyTemplate"/> 注入；null 时不画）</summary>
        public KnobElement? Owner { get; set; }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            if (Owner is not { } knob)
                return;

            var w = ActualWidth;
            var h = ActualHeight;

            if (!(w > 16d) || !(h > 16d))
                return;

            double cx = w / 2d;
            double cy = h / 2d;

            // 描边以边界线为中心向两侧铺半个线宽，所以半径先内缩 1 像素呼吸空间，
            // 弧的外沿才不会顶到控件边界被裁掉。
            double radius = Math.Min(w, h) / 2d - 1d;

            if (!(radius > 8d))
                return;

            double thickness = Math.Clamp(radius * 0.16d, 3d, 16d);
            double arcRadius = radius - thickness / 2d;

            double start = knob.NormalizedStartAngle;
            double sweep = knob.NormalizedSweepAngle;
            double ratio = knob.ValueRatio;

            var trackBrush = knob.TrackColor ?? TrackFallback;
            var knobBrush = knob.KnobColor ?? KnobFallback;

            // ① 轨道弧（整段量程）
            DrawArc(dc, cx, cy, arcRadius, start, sweep,
                MakePen(trackBrush, thickness, round: false));

            // ② 已转过的那段弧（与圆形进度条同一视觉语言：同一组角度/颜色属性）
            if (ratio > 0.001d && Math.Abs(sweep) > 0.01d)
            {
                DrawArc(dc, cx, cy, arcRadius, start, sweep * Math.Clamp(ratio, 0d, 1d),
                    MakePen(knobBrush, thickness, round: true));
            }

            // ③ 刻度线（只影响绘制，不参与取值）
            DrawTicks(dc, cx, cy, arcRadius, thickness, radius, start, sweep, trackBrush, knob.TickCount);

            // ④ 盘面 + 指针
            double capRadius = radius * 0.56d;
            var rimPen = new Pen(knob.Stroke ?? StrokeFallback, 1d);
            dc.DrawEllipse(trackBrush, rimPen, new Point(cx, cy), capRadius, capRadius);

            if (Math.Abs(sweep) > 0.01d)
            {
                double angle = start + sweep * ratio;
                var pointerPen = MakePen(knobBrush, Math.Max(2d, radius * 0.085d), round: true);
                dc.DrawLine(
                    pointerPen,
                    PointOnCircle(cx, cy, capRadius * 0.15d, angle),
                    PointOnCircle(cx, cy, capRadius * 0.80d, angle));
            }

            // ⑤ 读数（当前值）+ 中心说明字（基类 Text）
            DrawTexts(dc, knob, cx, cy, capRadius);

            // ⑥ 写失败的框边报警红（原因同时挂在 ToolTip 上，见 KnobElement.UpdateToolTip）
            if (!string.IsNullOrEmpty(knob.WriteError))
            {
                dc.DrawEllipse(null, new Pen(ErrorBrush, Math.Max(2d, radius * 0.045d)),
                    new Point(cx, cy), radius - 1d, radius - 1d);
            }
        }

        private static Pen MakePen(Brush brush, double thickness, bool round)
            => new(brush, thickness)
            {
                StartLineCap = round ? PenLineCap.Round : PenLineCap.Flat,
                EndLineCap = round ? PenLineCap.Round : PenLineCap.Flat,
            };

        private static void DrawTicks(
            DrawingContext dc, double cx, double cy, double arcRadius, double thickness,
            double radius, double start, double sweep, Brush brush, int tickCount)
        {
            if (tickCount < 2 || Math.Abs(sweep) < 0.01d)
                return;

            // 刻度贴在轨道弧内侧：外端留 2 像素缝隙，内端再进 8% 半径——
            // 顶到弧上会和弧的线宽糊成一根，短了又读不出格。
            double outer = arcRadius - thickness / 2d - 2d;
            double inner = outer - Math.Max(3d, radius * 0.08d);

            if (!(inner > 0d))
                return;

            var pen = new Pen(brush, Math.Max(1d, radius * 0.03d));

            for (int i = 0; i < tickCount; i++)
            {
                double angle = start + sweep * i / (tickCount - 1);
                dc.DrawLine(pen, PointOnCircle(cx, cy, inner, angle), PointOnCircle(cx, cy, outer, angle));
            }
        }

        private void DrawTexts(DrawingContext dc, KnobElement knob, double cx, double cy, double capRadius)
        {
            var valueBrush = knob.Foreground ?? TextFallback;
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            FormattedText? value = null;

            if (knob.ShowValue)
            {
                value = MakeText(knob, knob.DisplayText, knob.FontSize, valueBrush, dpi);

                // 读数不许溢出盘面：长数字按省略号截断（截断比压到刻度上更可读）
                value.MaxTextWidth = Math.Max(8d, capRadius * 1.6d);
                value.Trimming = TextTrimming.CharacterEllipsis;
            }

            FormattedText? label = null;

            if (!string.IsNullOrWhiteSpace(knob.Text))
            {
                label = MakeText(knob, knob.Text!, Math.Max(7d, knob.FontSize * 0.8d), valueBrush, dpi);
                label.MaxTextWidth = Math.Max(8d, capRadius * 1.6d);
                label.Trimming = TextTrimming.CharacterEllipsis;
            }

            double total = (value?.Height ?? 0d) + (label?.Height ?? 0d);
            double y = cy - total / 2d;

            if (value is not null)
            {
                dc.DrawText(value, new Point(cx - value.Width / 2d, y));
                y += value.Height;
            }

            if (label is not null)
                dc.DrawText(label, new Point(cx - label.Width / 2d, y));
        }

        private static FormattedText MakeText(
            KnobElement knob, string text, double size, Brush brush, double dpi)
            => new(
                text ?? string.Empty,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(knob.FontFamily, knob.FontStyle, knob.FontWeight, knob.FontStretch),
                Math.Max(6d, size),
                brush,
                dpi);

        /// <summary>
        /// 画一段弧。整圆（|sweep| ≥ 360）走 <see cref="DrawingContext.DrawEllipse"/>：
        /// <c>ArcTo</c> 在起点终点重合时无从判断走哪边，画不出整圈。
        /// </summary>
        private static void DrawArc(
            DrawingContext dc, double cx, double cy, double radius, double start, double sweep, Pen pen)
        {
            if (Math.Abs(sweep) < 0.01d || !(radius > 0d))
                return;

            if (Math.Abs(sweep) >= 359.99d)
            {
                dc.DrawEllipse(null, pen, new Point(cx, cy), radius, radius);
                return;
            }

            var geometry = new StreamGeometry();

            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(PointOnCircle(cx, cy, radius, start), isFilled: false, isClosed: false);
                ctx.ArcTo(
                    PointOnCircle(cx, cy, radius, start + sweep),
                    new Size(radius, radius),
                    rotationAngle: 0d,
                    isLargeArc: Math.Abs(sweep) > 180d,
                    sweepDirection: sweep >= 0d ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
                    isStroked: true,
                    isSmoothJoin: false);
            }

            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }

        /// <summary>圆周上某角度处的点（WPF 约定：0° 向右、顺时针为正；与表盘同一口径）</summary>
        private static Point PointOnCircle(double cx, double cy, double radius, double angle)
        {
            double radians = angle * Math.PI / 180d;

            return new Point(cx + radius * Math.Cos(radians), cy + radius * Math.Sin(radians));
        }
    }
}
