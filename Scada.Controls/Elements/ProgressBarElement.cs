using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VisionMaster.Scada.Controls
{
    /// <summary>棒图的走向</summary>
    public enum ProgressBarOrientation
    {
        /// <summary>横向（默认）：从左往右长，适合"产量完成率""阀门开度"</summary>
        Horizontal,

        /// <summary>纵向：从下往上长，适合"罐体液位""料仓余量"——液位天生是竖着的</summary>
        Vertical,
    }

    /// <summary>
    /// 棒图 / 进度条图元（TypeKey = <c>Hmi.ProgressBar</c>）：一个数值在量程里的可视化身。
    ///
    /// 与指示灯的差别在"输入"这一侧：指示灯吃一个布尔，棒图吃一个 <b>连续量</b>——
    /// 所以它多了量程（<see cref="Minimum"/>/<see cref="Maximum"/>）和走向（<see cref="Orientation"/>），
    /// 外观也由"值在量程里的比例"推导，而不是直接由 <see cref="ScadaElementBase.Fill"/> 决定。
    ///
    /// <b>为什么填充长度要在控件里算，而不是在模板里绑一个比例</b>
    /// ---------
    /// WPF 里"按比例长出一条边"没有现成的写法：Grid 的 ColumnDefinition 不在可视树上、
    /// 绑不了数据；用 ScaleTransform 缩放又会把圆角和描边一起拉变形。剩下的路只有两条——
    /// 要么引入一个"比例 × 可用宽度"的转换器，要么把长度算在控件里。
    /// 这里选后者：算出的是一个<b>只读依赖属性</b> <see cref="FillLength"/>，
    /// 模板只管把它贴到填充块的 Width/Height 上，与指示灯把 LampBrush 算好再给模板绑定是同一套路子。
    /// 好处是断言能直接读这个长度（"值 50 / 量程 0~100 → 长度是可用宽度的一半"），
    /// 不必去数像素。
    ///
    /// 运行时的接法（S6）：把 <see cref="Value"/> 绑到工程变量上，棒图就跟着变量长。
    /// </summary>
    [TemplatePart(Name = PartFillBar, Type = typeof(Border))]
    [TemplatePart(Name = PartValueLabel, Type = typeof(TextBlock))]
    public class ProgressBarElement : ScadaElementBase
    {
        /// <summary>模板部件名：填充块</summary>
        public const string PartFillBar = "FillBar";

        /// <summary>模板部件名：条上的数值文字</summary>
        public const string PartValueLabel = "ValueLabel";

        /// <summary>当前值（绑到工程变量上）</summary>
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(ProgressBarElement),
            new FrameworkPropertyMetadata(
                0d, FrameworkPropertyMetadataOptions.AffectsRender, OnBarInputChanged));

        /// <summary>量程下限</summary>
        public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
            nameof(Minimum), typeof(double), typeof(ProgressBarElement),
            new FrameworkPropertyMetadata(
                0d, FrameworkPropertyMetadataOptions.AffectsRender, OnBarInputChanged));

        /// <summary>量程上限</summary>
        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
            nameof(Maximum), typeof(double), typeof(ProgressBarElement),
            new FrameworkPropertyMetadata(
                100d, FrameworkPropertyMetadataOptions.AffectsRender, OnBarInputChanged));

        /// <summary>走向</summary>
        public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
            nameof(Orientation), typeof(ProgressBarOrientation), typeof(ProgressBarElement),
            new FrameworkPropertyMetadata(
                ProgressBarOrientation.Horizontal, FrameworkPropertyMetadataOptions.AffectsRender, OnBarInputChanged));

        /// <summary>空槽颜色（未被填充的那一段）</summary>
        public static readonly DependencyProperty TrackColorProperty = DependencyProperty.Register(
            nameof(TrackColor), typeof(Brush), typeof(ProgressBarElement),
            new FrameworkPropertyMetadata(
                ScadaBrushes.Frozen("#FF3A3A3A"), FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>是否在条上显示数值</summary>
        public static readonly DependencyProperty ShowValueProperty = DependencyProperty.Register(
            nameof(ShowValue), typeof(bool), typeof(ProgressBarElement),
            new FrameworkPropertyMetadata(
                true, FrameworkPropertyMetadataOptions.AffectsRender, OnShowValueChanged));

        /// <summary>数值的显示格式（标准 .NET 数字格式串，可带单位，如 <c>0.0 ℃</c>）</summary>
        public static readonly DependencyProperty ValueFormatProperty = DependencyProperty.Register(
            nameof(ValueFormat), typeof(string), typeof(ProgressBarElement),
            new FrameworkPropertyMetadata("0.#", FrameworkPropertyMetadataOptions.AffectsRender, OnBarInputChanged));

        private static readonly DependencyPropertyKey FillLengthPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(FillLength), typeof(double), typeof(ProgressBarElement),
            new PropertyMetadata(0d));

        /// <summary>填充块当前该有多长（像素，只读；模板贴到 Width/Height 上）</summary>
        public static readonly DependencyProperty FillLengthProperty = FillLengthPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey ValueTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(ValueText), typeof(string), typeof(ProgressBarElement),
            new PropertyMetadata(string.Empty));

        /// <summary>条上显示的数值文字（只读；= Value 按 ValueFormat 格式化）</summary>
        public static readonly DependencyProperty ValueTextProperty = ValueTextPropertyKey.DependencyProperty;

        static ProgressBarElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(ProgressBarElement),
                new FrameworkPropertyMetadata(typeof(ProgressBarElement)));
        }

        public ProgressBarElement()
        {
            // 填充长度取决于"控件此刻有多少像素可用"，而那要等布局跑完才知道。
            // 不订 SizeChanged 的话，设计期拖改宽高、运行期窗口缩放都会让条子停在旧长度上——
            // 表现为"数值明明变了，条子却只长到某个地方就不动了"。
            SizeChanged += (_, _) => UpdateFill();
        }

        /// <summary>当前值（见 <see cref="ValueProperty"/>）</summary>
        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        /// <summary>量程下限（见 <see cref="MinimumProperty"/>）</summary>
        public double Minimum
        {
            get => (double)GetValue(MinimumProperty);
            set => SetValue(MinimumProperty, value);
        }

        /// <summary>量程上限（见 <see cref="MaximumProperty"/>）</summary>
        public double Maximum
        {
            get => (double)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        /// <summary>走向（见 <see cref="OrientationProperty"/>）</summary>
        public ProgressBarOrientation Orientation
        {
            get => (ProgressBarOrientation)GetValue(OrientationProperty);
            set => SetValue(OrientationProperty, value);
        }

        /// <summary>空槽颜色（见 <see cref="TrackColorProperty"/>）</summary>
        public Brush? TrackColor
        {
            get => (Brush?)GetValue(TrackColorProperty);
            set => SetValue(TrackColorProperty, value);
        }

        /// <summary>是否在条上显示数值（见 <see cref="ShowValueProperty"/>）</summary>
        public bool ShowValue
        {
            get => (bool)GetValue(ShowValueProperty);
            set => SetValue(ShowValueProperty, value);
        }

        /// <summary>数值显示格式（见 <see cref="ValueFormatProperty"/>）</summary>
        public string? ValueFormat
        {
            get => (string?)GetValue(ValueFormatProperty);
            set => SetValue(ValueFormatProperty, value);
        }

        /// <summary>填充块长度（像素，只读；见 <see cref="FillLengthProperty"/>）</summary>
        public double FillLength => (double)GetValue(FillLengthProperty);

        /// <summary>条上数值文字（只读；见 <see cref="ValueTextProperty"/>）</summary>
        public string? ValueText => (string?)GetValue(ValueTextProperty);

        // 收尾钩子：StrokeThickness / Padding 是"量长度时要扣掉的量"，它们变了条子也得跟着重算。
        // 这两个值都在 RefreshCore 里落地，所以放在收尾钩子上一定晚于它们。
        protected override void OnElementRefreshed() => UpdateFill();

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            // 模板刚套上时填充块还是"裸"的（模板里不再写 Width/Height/对齐），这里按走向摆一次。
            UpdateFill();
        }

        private static void OnBarInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((ProgressBarElement)d).UpdateFill();

        private static void OnShowValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((ProgressBarElement)d).UpdateBarLayout();

        private void UpdateFill()
        {
            // 可用长度 = 控件实际尺寸 − Padding − 轨道那圈描边。
            // Border 的描边画在自己的布局框<b>内</b>（不像 Rectangle 那样骑在边界线上），
            // 所以这里只需要扣掉，不需要像矩形那样把 Padding 当内缩用。
            var available = Orientation == ProgressBarOrientation.Horizontal
                ? ActualWidth - Padding.Left - Padding.Right
                : ActualHeight - Padding.Top - Padding.Bottom;

            available -= 2 * StrokeThickness;
            if (!double.IsFinite(available) || available < 0)
                available = 0;

            SetValue(FillLengthPropertyKey, available * Ratio());
            SetValue(ValueTextPropertyKey, FormatValue());

            UpdateBarLayout();
        }

        /// <summary>
        /// 按走向把填充块摆好，并按 <see cref="ShowValue"/> 翻数值文字的显隐。
        ///
        /// 为什么不用模板里的 DataTrigger：本环境下 ControlTemplate.Triggers 里的 DataTrigger
        /// 不触发（见 ScadaElementBase.SetPartVisible），纵向棒图与"不显数值"两个开关以前都是死的。
        /// 另外 Setter.Value 里嵌的绑定（原来是 <c>{Binding FillLength}</c>）同样走不通，
        /// 所以长度也一律在代码里赋。
        /// </summary>
        private void UpdateBarLayout()
        {
            if (GetTemplateChild(PartFillBar) is not FrameworkElement fillBar)
                return;

            var vertical = Orientation == ProgressBarOrientation.Vertical;

            // 长度落在哪条边、靠哪一边，两档是成对的：纵向靠底拉满宽、横向靠左拉满高。
            // 两条边都要显式赋（用 NaN 表示"不约束"），否则从纵向切回横向时会留着上一档的 Height。
            fillBar.Width = vertical ? double.NaN : FillLength;
            fillBar.Height = vertical ? FillLength : double.NaN;
            fillBar.HorizontalAlignment = vertical ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
            fillBar.VerticalAlignment = vertical ? VerticalAlignment.Bottom : VerticalAlignment.Stretch;

            SetPartVisible(PartValueLabel, ShowValue);
        }

        /// <summary>当前值在量程里的比例（恒在 0~1；量程非法或值超界都被收敛，绝不返回 NaN）</summary>
        private double Ratio()
        {
            var range = Maximum - Minimum;
            if (!double.IsFinite(range) || range <= 0)
                return 0d; // 量程配错（上限 ≤ 下限）时画空条，而不是让 NaN 把渲染打断

            var ratio = (Value - Minimum) / range;
            return double.IsFinite(ratio) ? Math.Clamp(ratio, 0d, 1d) : 0d;
        }

        private string FormatValue()
        {
            // 用不变文化：.vms 是跨机器交换的，显示串也不该跟着系统区域设置变
            // （德语系统上小数点会变成逗号，同一份方案在两地显示不一样）。
            try
            {
                return Value.ToString(ValueFormat, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                // 格式串写错（如 "0.0.0"）不该让整页渲染中断，退回通用格式
                return Value.ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}
