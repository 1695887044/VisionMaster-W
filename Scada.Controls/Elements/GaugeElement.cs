using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 表盘图元（TypeKey = <c>Hmi.Gauge</c>）：一个模拟量在圆形刻度上的可视化身。
    ///
    /// 它与棒图（<see cref="ProgressBarElement"/>）是同一族——"值 → 一个比例 → 一个几何量"，
    /// 区别只在于棒图的比例落在长度上，表盘的比例落在<b>角度</b>上。
    ///
    /// <b>为什么这个图元的几何全部在代码里算，而不是用 XAML 拼</b>
    /// ---------
    /// 表盘的三个可见部分都依赖"控件此刻有多大"：
    /// ① 表盘面是<b>内接圆</b>（控件不是正方形时，XAML 里的 Ellipse 会被拉成椭圆，
    ///    而按圆周算出来的刻度线却仍在内接圆上，两者立刻错位）；
    /// ② 刻度线的每个端点都在圆周上；
    /// ③ 指针长度与粗细要随表盘缩放，否则小表盘上指针粗得像根柱子。
    ///
    /// 这三件事都算不出"与尺寸无关的常量"，所以模板里只留三个 <c>Path</c> 绑只读几何属性，
    /// 一个算术都不写——与棒图把长度算在 <c>FillLength</c> 上是同一条纪律。
    ///
    /// <b>角度口径</b>（WPF 约定，与直觉相反，写在这里免得下一个人踩）
    /// ---------
    /// 0° 指向右（3 点钟），角度<b>顺时针</b>增加，90° 指向下（6 点钟）。
    /// 默认 <see cref="StartAngle"/> = 135°（左下，7 点半方向）、<see cref="SweepAngle"/> = 270°，
    /// 于是缺口正好落在正下方——那是放数值与标签的地方，也是工业仪表的标准长相。
    ///
    /// 运行时的接法（S6）：把 <see cref="Value"/> 绑到工程变量上，指针就活了。
    /// </summary>
    [TemplatePart(Name = PartValueLabel, Type = typeof(TextBlock))]
    public class GaugeElement : ScadaElementBase
    {
        /// <summary>模板部件名：表盘下方的数值文字</summary>
        public const string PartValueLabel = "ValueLabel";

        private static readonly Brush DefaultTrackColor = ScadaBrushes.Frozen("#FF3A3A3A");
        private static readonly Brush DefaultScaleColor = ScadaBrushes.Frozen("#FF9AA5B1");

        #region 输入

        /// <summary>当前值</summary>
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(GaugeElement),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, OnDialInputChanged));

        /// <summary>量程下限</summary>
        public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
            nameof(Minimum), typeof(double), typeof(GaugeElement),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender, OnDialInputChanged));

        /// <summary>量程上限</summary>
        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
            nameof(Maximum), typeof(double), typeof(GaugeElement),
            new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender, OnDialInputChanged));

        /// <summary>数值格式串</summary>
        public static readonly DependencyProperty ValueFormatProperty = DependencyProperty.Register(
            nameof(ValueFormat), typeof(string), typeof(GaugeElement),
            new FrameworkPropertyMetadata("0.#", FrameworkPropertyMetadataOptions.AffectsRender, OnDialInputChanged));

        /// <summary>单位（跟在数值后面的一小段字）</summary>
        public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
            nameof(Unit), typeof(string), typeof(GaugeElement),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender, OnDialInputChanged));

        /// <summary>起始角（度，WPF 约定：0° 向右、顺时针为正）</summary>
        public static readonly DependencyProperty StartAngleProperty = DependencyProperty.Register(
            nameof(StartAngle), typeof(double), typeof(GaugeElement),
            new FrameworkPropertyMetadata(135d, FrameworkPropertyMetadataOptions.AffectsRender, OnDialInputChanged));

        /// <summary>扫过角度（度；负值表示逆时针）</summary>
        public static readonly DependencyProperty SweepAngleProperty = DependencyProperty.Register(
            nameof(SweepAngle), typeof(double), typeof(GaugeElement),
            new FrameworkPropertyMetadata(270d, FrameworkPropertyMetadataOptions.AffectsRender, OnDialInputChanged));

        /// <summary>刻度格数（格数 + 1 = 刻度线根数；0 = 不画刻度）</summary>
        public static readonly DependencyProperty TickDivisionsProperty = DependencyProperty.Register(
            nameof(TickDivisions), typeof(int), typeof(GaugeElement),
            new FrameworkPropertyMetadata(5, FrameworkPropertyMetadataOptions.AffectsRender, OnDialInputChanged));

        /// <summary>是否显示数值</summary>
        public static readonly DependencyProperty ShowValueProperty = DependencyProperty.Register(
            nameof(ShowValue), typeof(bool), typeof(GaugeElement),
            new FrameworkPropertyMetadata(
                true, FrameworkPropertyMetadataOptions.AffectsRender, OnShowValueChanged));

        #endregion

        #region 外观输入

        /// <summary>表盘底色</summary>
        public static readonly DependencyProperty TrackColorProperty = DependencyProperty.Register(
            nameof(TrackColor), typeof(Brush), typeof(GaugeElement),
            new FrameworkPropertyMetadata(DefaultTrackColor, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>刻度色（刻度线与量程弧）</summary>
        public static readonly DependencyProperty ScaleColorProperty = DependencyProperty.Register(
            nameof(ScaleColor), typeof(Brush), typeof(GaugeElement),
            new FrameworkPropertyMetadata(DefaultScaleColor, FrameworkPropertyMetadataOptions.AffectsRender));

        #endregion

        #region 推导结果（只读）

        private static readonly DependencyPropertyKey FaceGeometryPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(FaceGeometry), typeof(Geometry), typeof(GaugeElement), new PropertyMetadata(null));

        /// <summary>表盘面（内接圆，只读）</summary>
        public static readonly DependencyProperty FaceGeometryProperty = FaceGeometryPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey ScaleGeometryPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(ScaleGeometry), typeof(Geometry), typeof(GaugeElement), new PropertyMetadata(null));

        /// <summary>量程弧 + 刻度线（只读）</summary>
        public static readonly DependencyProperty ScaleGeometryProperty = ScaleGeometryPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey NeedleGeometryPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(NeedleGeometry), typeof(Geometry), typeof(GaugeElement), new PropertyMetadata(null));

        /// <summary>指针（含中心轴环，只读）</summary>
        public static readonly DependencyProperty NeedleGeometryProperty = NeedleGeometryPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey NeedleAnglePropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(NeedleAngle), typeof(double), typeof(GaugeElement), new PropertyMetadata(0d));

        /// <summary>指针当前角度（度，绝对角度、非相对起始角；只读）</summary>
        public static readonly DependencyProperty NeedleAngleProperty = NeedleAnglePropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey NeedleThicknessPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(NeedleThickness), typeof(double), typeof(GaugeElement), new PropertyMetadata(3d));

        /// <summary>指针线宽（随表盘尺寸缩放，只读）</summary>
        public static readonly DependencyProperty NeedleThicknessProperty = NeedleThicknessPropertyKey.DependencyProperty;

        private static readonly DependencyPropertyKey ValueTextPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(ValueText), typeof(string), typeof(GaugeElement), new PropertyMetadata(string.Empty));

        /// <summary>数值 + 单位的显示串（只读）</summary>
        public static readonly DependencyProperty ValueTextProperty = ValueTextPropertyKey.DependencyProperty;

        #endregion

        static GaugeElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(GaugeElement),
                new FrameworkPropertyMetadata(typeof(GaugeElement)));
        }

        public GaugeElement()
        {
            // 几何量都依赖"此刻多大"，而尺寸只有在布局跑完之后才知道——
            // 与棒图监听 SizeChanged 重算填充长度是同一个理由。
            SizeChanged += (_, _) => UpdateDial();

            // 构造即算第一帧：断言环境不挂可视树、不跑布局，不显式算一遍就是个空表盘。
            UpdateDial();
        }

        // 收尾钩子：三个几何量既依赖"值/量程"（上面那些 DP），也依赖"外圈粗细"——
        // 而半径要扣掉半线宽，StrokeThickness 是在 RefreshCore 里才落到控件上的。
        // 所以放在收尾钩子上重算一次，一定晚于所有输入落地（与棒图的 OnElementRefreshed 同一个理由）。
        protected override void OnElementRefreshed() => UpdateDial();

        // 模板刚套上时补摆一次：ShowValue 若在套模板之前就落地，那次 SetPartVisible 是空操作
        //（那时还取不到模板部件），这里必须再摆一次。
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            UpdateDial();
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

        /// <summary>数值格式串（见 <see cref="ValueFormatProperty"/>）</summary>
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

        /// <summary>起始角（见 <see cref="StartAngleProperty"/>）</summary>
        public double StartAngle
        {
            get => (double)GetValue(StartAngleProperty);
            set => SetValue(StartAngleProperty, value);
        }

        /// <summary>扫过角度（见 <see cref="SweepAngleProperty"/>）</summary>
        public double SweepAngle
        {
            get => (double)GetValue(SweepAngleProperty);
            set => SetValue(SweepAngleProperty, value);
        }

        /// <summary>刻度格数（见 <see cref="TickDivisionsProperty"/>）</summary>
        public int TickDivisions
        {
            get => (int)GetValue(TickDivisionsProperty);
            set => SetValue(TickDivisionsProperty, value);
        }

        /// <summary>是否显示数值（见 <see cref="ShowValueProperty"/>）</summary>
        public bool ShowValue
        {
            get => (bool)GetValue(ShowValueProperty);
            set => SetValue(ShowValueProperty, value);
        }

        /// <summary>表盘底色（见 <see cref="TrackColorProperty"/>）</summary>
        public Brush? TrackColor
        {
            get => (Brush?)GetValue(TrackColorProperty);
            set => SetValue(TrackColorProperty, value);
        }

        /// <summary>刻度色（见 <see cref="ScaleColorProperty"/>）</summary>
        public Brush? ScaleColor
        {
            get => (Brush?)GetValue(ScaleColorProperty);
            set => SetValue(ScaleColorProperty, value);
        }

        /// <summary>表盘面几何（只读，见 <see cref="FaceGeometryProperty"/>）</summary>
        public Geometry? FaceGeometry => (Geometry?)GetValue(FaceGeometryProperty);

        /// <summary>量程弧 + 刻度线几何（只读，见 <see cref="ScaleGeometryProperty"/>）</summary>
        public Geometry? ScaleGeometry => (Geometry?)GetValue(ScaleGeometryProperty);

        /// <summary>指针几何（只读，见 <see cref="NeedleGeometryProperty"/>）</summary>
        public Geometry? NeedleGeometry => (Geometry?)GetValue(NeedleGeometryProperty);

        /// <summary>指针当前角度（只读，见 <see cref="NeedleAngleProperty"/>）</summary>
        public double NeedleAngle => (double)GetValue(NeedleAngleProperty);

        /// <summary>指针线宽（只读，见 <see cref="NeedleThicknessProperty"/>）</summary>
        public double NeedleThickness => (double)GetValue(NeedleThicknessProperty);

        /// <summary>数值显示串（只读，见 <see cref="ValueTextProperty"/>）</summary>
        public string? ValueText => (string?)GetValue(ValueTextProperty);

        /// <summary>
        /// 值在量程里的比例（0~1）。
        ///
        /// 量程配错（下限 ≥ 上限、或出现 NaN）时返回 0 而不是让除法算出 NaN/Infinity——
        /// 一个非法量程不该把整页渲染打断（与棒图同一条纪律）。
        /// </summary>
        public double Ratio()
        {
            double range = Maximum - Minimum;

            if (!double.IsFinite(range) || range <= 0d)
                return 0d;

            double ratio = (Value - Minimum) / range;

            return double.IsFinite(ratio) ? Math.Clamp(ratio, 0d, 1d) : 0d;
        }

        private static void OnDialInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((GaugeElement)d).UpdateDial();

        // 「显不显数值」是个纯模板层的开关，但本环境下 ControlTemplate.Triggers 里的 DataTrigger
        // 不触发（见 ScadaElementBase.SetPartVisible），所以由代码翻，模板里也不写 Visibility 初值。
        private static void OnShowValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((GaugeElement)d).SetPartVisible(PartValueLabel, ((GaugeElement)d).ShowValue);

        private void UpdateDial()
        {
            // 数值文字的显隐与尺寸无关，先办掉——下面尺寸为 0 时会提前 return，别把它漏在外面。
            SetPartVisible(PartValueLabel, ShowValue);

            // ---- 与尺寸无关的两项：先算，保证断言环境（不跑布局）里也是对的 ----

            double start = NormalizeAngle(StartAngle);
            double sweep = NormalizeSweep(SweepAngle);

            // 指针角度 = 起始角 + 扫过角 × 比例。比例已收敛在 0~1，所以指针永远落在量程弧内。
            SetValue(NeedleAnglePropertyKey, start + sweep * Ratio());
            SetValue(ValueTextPropertyKey, FormatValue());

            // ---- 与尺寸有关的三项 ----

            double w = ActualWidth;
            double h = ActualHeight;

            if (!(w > 0d) || !(h > 0d))
            {
                // 还没布局：几何量留空。尺寸一到（SizeChanged）就会重算，
                // 而不是在这里拿一个猜测的尺寸画出一个待会儿会跳一下的表盘。
                SetValue(FaceGeometryPropertyKey, null);
                SetValue(ScaleGeometryPropertyKey, null);
                SetValue(NeedleGeometryPropertyKey, null);
                return;
            }

            double cx = w / 2d;
            double cy = h / 2d;

            // 描边以边界线为中心向两侧各铺半个线宽，所以内缩半线宽 + 1 像素呼吸空间，
            // 表盘外沿才落在标注尺寸上（与基类 ApplyStrokeInset 同一个账）。
            double radius = Math.Min(w, h) / 2d - (StrokeThickness / 2d + 1d);

            if (!(radius > 0d))
            {
                SetValue(FaceGeometryPropertyKey, null);
                SetValue(ScaleGeometryPropertyKey, null);
                SetValue(NeedleGeometryPropertyKey, null);
                return;
            }

            double needleThickness = Math.Clamp(Math.Min(w, h) * 0.035d, 2d, 8d);
            SetValue(NeedleThicknessPropertyKey, needleThickness);

            SetValue(FaceGeometryPropertyKey, Freeze(new EllipseGeometry(new Point(cx, cy), radius, radius)));
            SetValue(ScaleGeometryPropertyKey, BuildScale(cx, cy, radius, start, sweep));
            SetValue(NeedleGeometryPropertyKey, BuildNeedle(cx, cy, radius, NeedleAngle, needleThickness));
        }

        /// <summary>
        /// 量程弧 + 刻度线。
        ///
        /// 弧画在半径 0.82 处、刻度线从 0.87 到 0.97——刻度比弧更靠外，
        /// 是因为操作员读的是刻度，弧只用来标示"量程到哪儿为止"。
        /// </summary>
        private Geometry? BuildScale(double cx, double cy, double radius, double start, double sweep)
        {
            var group = new GeometryGroup { FillRule = FillRule.Nonzero };

            double arcRadius = radius * 0.82d;

            if (Math.Abs(sweep) >= 360d)
            {
                // 整圆：ArcSegment 一段画不出 360°（起点终点重合，它无从判断走哪边），
                // 直接给一个圆环几何，比拆成两段弧少一次边界判断。
                group.Children.Add(new EllipseGeometry(new Point(cx, cy), arcRadius, arcRadius));
            }
            else if (Math.Abs(sweep) > 0.01d)
            {
                var figure = new PathFigure
                {
                    StartPoint = PointOnCircle(cx, cy, arcRadius, start),
                    IsClosed = false,
                    IsFilled = false,
                };

                figure.Segments.Add(new ArcSegment(
                    point: PointOnCircle(cx, cy, arcRadius, start + sweep),
                    size: new Size(arcRadius, arcRadius),
                    rotationAngle: 0d,
                    isLargeArc: Math.Abs(sweep) > 180d,
                    sweepDirection: sweep >= 0d ? SweepDirection.Clockwise : SweepDirection.Counterclockwise,
                    isStroked: true));

                var arc = new PathGeometry();
                arc.Figures.Add(figure);
                group.Children.Add(arc);
            }

            // 刻度线：格数 + 1 根（含两端），所以默认 5 格就是 0/20/40/60/80/100 六个读数点。
            int divisions = TickDivisions;

            if (divisions > 0 && Math.Abs(sweep) > 0.01d)
            {
                double outer = radius * 0.97d;
                double inner = radius * 0.87d;

                for (int i = 0; i <= divisions; i++)
                {
                    double angle = start + sweep * i / divisions;

                    group.Children.Add(new LineGeometry(
                        PointOnCircle(cx, cy, inner, angle),
                        PointOnCircle(cx, cy, outer, angle)));
                }
            }

            return group.Children.Count == 0 ? null : Freeze(group);
        }

        /// <summary>
        /// 指针：一根从圆心指向刻度盘的线 + 中心一个轴环。
        ///
        /// 指针长度取半径的 0.92，让针尖压在刻度线内侧一点——
        /// 顶到 1.0 会盖住刻度，短了又显得针没指到位。
        /// </summary>
        private static Geometry BuildNeedle(double cx, double cy, double radius, double angle, double thickness)
        {
            var group = new GeometryGroup { FillRule = FillRule.Nonzero };

            group.Children.Add(new LineGeometry(
                new Point(cx, cy),
                PointOnCircle(cx, cy, radius * 0.92d, angle)));

            // 轴环：把针根那一点收成一个圆，指针转动时不会露出一个生硬的线头
            group.Children.Add(new EllipseGeometry(new Point(cx, cy), thickness * 1.1d, thickness * 1.1d));

            return Freeze(group);
        }

        /// <summary>
        /// 数值 + 单位的显示串。
        ///
        /// 格式串写错时退回通用格式：与棒图、数值域同一条纪律——
        /// 一个坏格式串不该让整页渲染中断。
        /// </summary>
        private string FormatValue()
        {
            var format = string.IsNullOrWhiteSpace(ValueFormat) ? null : ValueFormat;

            string text;
            try
            {
                text = Value.ToString(format, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                text = Value.ToString(CultureInfo.InvariantCulture);
            }

            return string.IsNullOrEmpty(Unit) ? text : text + " " + Unit;
        }

        private static double NormalizeAngle(double angle) => double.IsFinite(angle) ? angle : 0d;

        private static double NormalizeSweep(double sweep)
            => double.IsFinite(sweep) ? Math.Clamp(sweep, -360d, 360d) : 0d;

        /// <summary>圆周上某角度处的点（角度按 WPF 约定：0° 向右、顺时针为正）</summary>
        private static Point PointOnCircle(double cx, double cy, double radius, double angle)
        {
            double radians = angle * Math.PI / 180d;

            return new Point(cx + radius * Math.Cos(radians), cy + radius * Math.Sin(radians));
        }

        /// <summary>
        /// 冻结几何。
        ///
        /// 几何量是每次重算都新建的对象，本来不存在"多个图元共享同一份"的问题；
        /// 冻结是为了让"算完的几何"从类型上就不可改——免得将来有人从外面
        /// 拿到 <see cref="FaceGeometry"/> 就往里塞线段，画出个谁也说不清的表盘。
        /// </summary>
        private static Geometry Freeze(Geometry geometry)
        {
            geometry.Freeze();
            return geometry;
        }
    }
}
