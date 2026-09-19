using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace VisionMaster.Scada.Controls
{
    /// <summary>阀门的开度状态（由开度这一个数推出来，不是独立配置项）</summary>
    public enum ValveState
    {
        /// <summary>全关（开度 ≤ 0）：阀体里没有介质流过</summary>
        Closed,

        /// <summary>中间位（0 &lt; 开度 &lt; 100）：调节阀的常态，也是"动作中"的样子</summary>
        Throttled,

        /// <summary>全开（开度 ≥ 100）</summary>
        Open,
    }

    /// <summary>
    /// 阀门图元（TypeKey = <c>Hmi.Valve</c>）：管路上一只阀的可视化身，画的是标准<b>蝴蝶结</b>符号。
    ///
    /// <b>为什么开关阀与调节阀是同一个图元</b>
    /// ---------
    /// 现场把它们当两种设备，但对画面来说它们只差一个数：开关阀只有 0 / 100 两个取值，
    /// 调节阀在 0~100 之间连续取值。若拆成两个图元，就得有两份几乎一样的模板、两条描述符、
    /// 两处要同步修的几何代码——而它们的差别只有"填充比例会不会停在中间"这一点，
    /// 那本来就是同一个算式的结果。所以这里只留一个数字输入
    /// <see cref="Opening"/>，其余全部推导：
    ///
    /// <code>
    ///   Opening ──┬─▶ ValveState（0=关 / 中间 / 100=全开）
    ///             ├─▶ ValveBrush（关=空腔色、中间=中间位色、全开=全开色）
    ///             ├─▶ OpeningText（"50%"）
    ///             └─▶ FillClipGeometry（填充从左往右推进多少）
    /// </code>
    ///
    /// <b>为什么填充要用"裁一块"的办法，而不是画一根随比例伸缩的条</b>
    /// ---------
    /// 蝴蝶结是个尖角朝内的形状，任何"从边上量一段长度"的条子都塞不进它的轮廓。
    /// 这里的做法是：<b>填充与阀体共用同一份几何</b>（<see cref="ValveGeometry"/>），
    /// 再拿一个矩形（<see cref="FillClipGeometry"/>）把它裁到"开了多少"的宽度上——
    /// 于是无论阀体是什么形状，填充都严丝合缝地落在轮廓里，比例也天然是对的。
    /// 宽度为 0 的裁剪矩形是合法的，正好等于"一点都没开"，不需要为全关单开一条分支。
    ///
    /// <b>为什么几何全部在代码里算</b>
    /// ---------
    /// 蝴蝶结的四个顶点都长在控件边界上，随尺寸缩放；而"外沿要落在标注尺寸上"要求
    /// 内缩半个线宽——与表盘（<see cref="GaugeElement"/>）同一个账。模板里只留两个
    /// <c>Path</c> 绑只读几何属性，一个算术都不写。
    ///
    /// <b>文字为什么压在上下两个凹口里</b>
    /// ---------
    /// 蝴蝶结的上下各有一个 V 形空档（越靠中间越窄、越靠边越宽），
    /// 那正好是<see cref="ScadaElementBase.Text"/>（位号）与<see cref="OpeningText"/>（开度）的容身处：
    /// 两行字都不必给图元留额外高度，也不会盖住阀体本身。
    ///
    /// <b>为什么不做开启动画</b>
    /// ---------
    /// 与多态灯不做闪烁同一条理由：动画需要一条全画面统一的节拍源（S11 报警系统一并做），
    /// 每个图元各起一个定时器，同屏几个阀会各走各的，看上去像画面卡了。
    ///
    /// 运行时的接法：把 <see cref="Opening"/> 绑到工程变量上（0~100 的实数），阀就活了。
    /// </summary>
    public class ValveElement : ScadaElementBase
    {
        // 默认色必须冻结：依赖属性默认值被所有实例共享，未冻结的 Freezable 被某实例改到会串到别的实例。
        private static readonly Brush DefaultTrackColor = ScadaBrushes.Frozen("#FF3A3A3A");
        private static readonly Brush DefaultThrottleColor = ScadaBrushes.Frozen("#FF2F80ED");
        private static readonly Brush DefaultOpenColor = ScadaBrushes.Frozen("#FF34C759");

        #region 输入

        /// <summary>开度（0~100；开关阀只给 0 或 100）</summary>
        public static readonly DependencyProperty OpeningProperty = DependencyProperty.Register(
            nameof(Opening), typeof(double), typeof(ValveElement),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, OnValveInputChanged));

        /// <summary>开度显示格式串</summary>
        public static readonly DependencyProperty OpeningFormatProperty = DependencyProperty.Register(
            nameof(OpeningFormat), typeof(string), typeof(ValveElement),
            new FrameworkPropertyMetadata("0.#", FrameworkPropertyMetadataOptions.AffectsRender, OnValveInputChanged));

        /// <summary>是否显示开度</summary>
        public static readonly DependencyProperty ShowValueProperty = DependencyProperty.Register(
            nameof(ShowValue), typeof(bool), typeof(ValveElement),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        #endregion

        #region 外观输入

        /// <summary>阀体空腔色（也是全关时阀体的颜色：没开就没有介质，露出来的就是空腔）</summary>
        public static readonly DependencyProperty TrackColorProperty = DependencyProperty.Register(
            nameof(TrackColor), typeof(Brush), typeof(ValveElement),
            new FrameworkPropertyMetadata(DefaultTrackColor, FrameworkPropertyMetadataOptions.AffectsRender, OnValveInputChanged));

        /// <summary>中间位色（0 &lt; 开度 &lt; 100 时的填充色）</summary>
        public static readonly DependencyProperty ThrottleColorProperty = DependencyProperty.Register(
            nameof(ThrottleColor), typeof(Brush), typeof(ValveElement),
            new FrameworkPropertyMetadata(DefaultThrottleColor, FrameworkPropertyMetadataOptions.AffectsRender, OnValveInputChanged));

        /// <summary>全开色（开度 ≥ 100 时的填充色）</summary>
        public static readonly DependencyProperty OpenColorProperty = DependencyProperty.Register(
            nameof(OpenColor), typeof(Brush), typeof(ValveElement),
            new FrameworkPropertyMetadata(DefaultOpenColor, FrameworkPropertyMetadataOptions.AffectsRender, OnValveInputChanged));

        #endregion

        #region 推导结果（只读）

        private static readonly DependencyPropertyKey ValveBrushPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(ValveBrush), typeof(Brush), typeof(ValveElement),
            new PropertyMetadata(Brushes.Transparent));

        /// <summary>填充该用的颜色（由开度状态挑一个，只读）</summary>
        public static readonly DependencyProperty ValveBrushProperty = ValveBrushPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey OpeningTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(OpeningText), typeof(string), typeof(ValveElement), new PropertyMetadata(string.Empty));

        /// <summary>开度的显示串（如 "50%"，只读）</summary>
        public static readonly DependencyProperty OpeningTextProperty = OpeningTextPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey ValveGeometryPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(ValveGeometry), typeof(Geometry), typeof(ValveElement), new PropertyMetadata(null));

        /// <summary>阀体几何：两个尖角相对的三角形（蝴蝶结，只读）</summary>
        public static readonly DependencyProperty ValveGeometryProperty = ValveGeometryPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey FillClipGeometryPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(FillClipGeometry), typeof(Geometry), typeof(ValveElement), new PropertyMetadata(null));

        /// <summary>填充的裁剪矩形（宽度 = 可画宽度 × 开度比例，只读）</summary>
        public static readonly DependencyProperty FillClipGeometryProperty = FillClipGeometryPropertyKey.DependencyProperty;

        #endregion

        static ValveElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(ValveElement),
                new FrameworkPropertyMetadata(typeof(ValveElement)));
        }

        public ValveElement()
        {
            // 几何量都依赖"此刻多大"，而尺寸只有在布局跑完之后才知道——
            // 与棒图监听 SizeChanged 重算填充长度是同一个理由。
            SizeChanged += (_, _) => UpdateValve();

            // 构造即算第一帧：状态色与开度文字与尺寸无关，断言环境（不挂可视树、不跑布局）
            // 里不显式算一遍就会读到空值。
            UpdateValve();
        }

        // 收尾钩子：内缩量要扣掉半个线宽，而 StrokeThickness 是在 RefreshCore 里才落到控件上的，
        // 所以几何必须在所有输入落地之后重算一次（与棒图、表盘同一个理由）。
        protected override void OnElementRefreshed() => UpdateValve();

        /// <summary>开度（见 <see cref="OpeningProperty"/>）</summary>
        public double Opening
        {
            get => (double)GetValue(OpeningProperty);
            set => SetValue(OpeningProperty, value);
        }

        /// <summary>开度显示格式串（见 <see cref="OpeningFormatProperty"/>）</summary>
        public string? OpeningFormat
        {
            get => (string?)GetValue(OpeningFormatProperty);
            set => SetValue(OpeningFormatProperty, value);
        }

        /// <summary>是否显示开度（见 <see cref="ShowValueProperty"/>）</summary>
        public bool ShowValue
        {
            get => (bool)GetValue(ShowValueProperty);
            set => SetValue(ShowValueProperty, value);
        }

        /// <summary>阀体空腔色（见 <see cref="TrackColorProperty"/>）</summary>
        public Brush? TrackColor
        {
            get => (Brush?)GetValue(TrackColorProperty);
            set => SetValue(TrackColorProperty, value);
        }

        /// <summary>中间位色（见 <see cref="ThrottleColorProperty"/>）</summary>
        public Brush? ThrottleColor
        {
            get => (Brush?)GetValue(ThrottleColorProperty);
            set => SetValue(ThrottleColorProperty, value);
        }

        /// <summary>全开色（见 <see cref="OpenColorProperty"/>）</summary>
        public Brush? OpenColor
        {
            get => (Brush?)GetValue(OpenColorProperty);
            set => SetValue(OpenColorProperty, value);
        }

        /// <summary>填充色（只读，见 <see cref="ValveBrushProperty"/>）</summary>
        public Brush? ValveBrush => (Brush?)GetValue(ValveBrushProperty);

        /// <summary>开度显示串（只读，见 <see cref="OpeningTextProperty"/>）</summary>
        public string? OpeningText => (string?)GetValue(OpeningTextProperty);

        /// <summary>阀体几何（只读，见 <see cref="ValveGeometryProperty"/>）</summary>
        public Geometry? ValveGeometry => (Geometry?)GetValue(ValveGeometryProperty);

        /// <summary>填充裁剪矩形（只读，见 <see cref="FillClipGeometryProperty"/>）</summary>
        public Geometry? FillClipGeometry => (Geometry?)GetValue(FillClipGeometryProperty);

        /// <summary>
        /// 当前开度状态。
        ///
        /// 做成普通计算属性而不是依赖属性：没有任何东西绑它（模板要的是颜色，不是枚举），
        /// 注册成依赖属性只会多一份没人读的表面。运行态与断言从这条路上读语义。
        /// </summary>
        public ValveState State => PickState();

        /// <summary>
        /// 开度比例（0~1）。
        ///
        /// 非有限值（NaN/Infinity）与越界值一律收敛：一个坏值不该把整页渲染打断
        /// （与棒图、表盘同一条纪律）。
        /// </summary>
        public double Ratio()
        {
            double opening = Opening;

            return double.IsFinite(opening) ? Math.Clamp(opening / 100d, 0d, 1d) : 0d;
        }

        private static void OnValveInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((ValveElement)d).UpdateValve();

        private void UpdateValve()
        {
            // ---- 与尺寸无关的三项：先算，保证断言环境（不跑布局）里也是对的 ----

            SetValue(ValveBrushPropertyKey, PickBrush() ?? Brushes.Transparent);
            SetValue(OpeningTextPropertyKey, FormatOpening());

            // ---- 与尺寸有关的两项 ----

            double inset = StrokeThickness / 2d + 1d; // 半线宽 + 1 像素呼吸空间，外沿才落在标注尺寸上

            double width = ActualWidth - 2d * inset;
            double height = ActualHeight - 2d * inset;

            if (!(width > 0d) || !(height > 0d))
            {
                // 还没布局：几何量留空。尺寸一到（SizeChanged）就会重算，
                // 而不是在这里拿一个猜测的尺寸画出一个待会儿会跳一下的阀。
                SetValue(ValveGeometryPropertyKey, null);
                SetValue(FillClipGeometryPropertyKey, null);
                return;
            }

            SetValue(ValveGeometryPropertyKey, BuildBowtie(inset, width, height));

            // 裁剪矩形永远给（哪怕宽度是 0）：null 在 WPF 里等于"不裁剪"，
            // 那样全关的阀会被整块填充色盖住，正好画反。
            SetValue(FillClipGeometryPropertyKey, Freeze(new RectangleGeometry(
                new Rect(inset, inset, width * Ratio(), height))));
        }

        private ValveState PickState()
        {
            double opening = Opening;

            if (!double.IsFinite(opening) || opening <= 0d)
                return ValveState.Closed;

            return opening >= 100d ? ValveState.Open : ValveState.Throttled;
        }

        /// <summary>
        /// 按当前状态挑填充色。
        ///
        /// 全关时填充的裁剪宽度本来就是 0（一点都看不见），这里仍返回空腔色而不是透明：
        /// 万一将来有人改了裁剪的算法，"关"这个状态的颜色仍然是自洽的，不会突然漏出一块。
        /// 没配到颜色就退回透明——宁可看不见，也别拿别人的颜色顶上（与多态灯同一条纪律）。
        /// </summary>
        private Brush? PickBrush() => PickState() switch
        {
            ValveState.Open => OpenColor,
            ValveState.Throttled => ThrottleColor,
            _ => TrackColor,
        };

        /// <summary>
        /// 开度的显示串（"50%"）。
        ///
        /// 格式串写错时退回通用格式：与棒图、数值域、表盘同一条纪律——
        /// 一个坏格式串不该让整页渲染中断。
        /// </summary>
        private string FormatOpening()
        {
            var format = string.IsNullOrWhiteSpace(OpeningFormat) ? null : OpeningFormat;

            string text;
            try
            {
                text = Opening.ToString(format, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                text = Opening.ToString(CultureInfo.InvariantCulture);
            }

            return text + "%";
        }

        /// <summary>
        /// 蝴蝶结：两个尖角在正中相对的三角形。
        ///
        /// 两个三角形<b>各自闭合</b>（而不是拼成一个六边形），是因为轮廓要的就是中间那两条
        /// 汇向中心点的斜边——那正是"阀"这个符号的辨识特征。填充用 Nonzero 规则，
        /// 两块重叠区域（这里其实不重叠）不会互相挖空。
        /// </summary>
        private static Geometry BuildBowtie(double inset, double width, double height)
        {
            double centerX = inset + width / 2d;
            double centerY = inset + height / 2d;

            var group = new GeometryGroup { FillRule = FillRule.Nonzero };

            group.Children.Add(Triangle(
                new Point(inset, inset),
                new Point(inset, inset + height),
                new Point(centerX, centerY)));

            group.Children.Add(Triangle(
                new Point(inset + width, inset),
                new Point(inset + width, inset + height),
                new Point(centerX, centerY)));

            return Freeze(group);
        }

        private static Geometry Triangle(Point a, Point b, Point c)
        {
            var figure = new PathFigure { StartPoint = a, IsClosed = true, IsFilled = true };
            figure.Segments.Add(new LineSegment(b, true));
            figure.Segments.Add(new LineSegment(c, true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            return geometry;
        }

        /// <summary>
        /// 冻结几何：几何量每次重算都新建，本来不存在"多图元共享一份"的问题；
        /// 冻结是为了让算完的几何从类型上就不可改（与表盘同一条纪律）。
        /// </summary>
        private static Geometry Freeze(Geometry geometry)
        {
            geometry.Freeze();
            return geometry;
        }
    }
}
