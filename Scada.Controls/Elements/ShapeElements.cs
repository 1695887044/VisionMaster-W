using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 相对坐标点串解析（<c>x,y;x,y</c>，取值 0~1）。
    /// 与 <see cref="LineElement"/> 的端点同一口径：点位按图元框缩放，拖动/缩放图元时几何跟着走。
    /// </summary>
    internal static class ShapePointParser
    {
        public static List<Point> Parse(string? text, bool clamp)
        {
            var result = new List<Point>();
            if (string.IsNullOrWhiteSpace(text))
                return result;

            foreach (var pair in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split(',');
                if (parts.Length != 2)
                    continue;

                if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                    || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                    continue;

                result.Add(clamp
                    ? new Point(Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1))
                    : new Point(x, y));
            }

            return result;
        }
    }

    /// <summary>
    /// 几何换算共用件：把"还没量过的控件"折成 1×1，避免把 <c>NaN</c> 算进几何。
    /// 构造期 <c>ActualWidth</c> 恒为 0、<c>Width</c> 可能是 <c>NaN</c>（自动尺寸），
    /// 而 <c>Math.Max(1, NaN)</c> 仍然是 <c>NaN</c>——几何里带 NaN 会让 WPF 在渲染期抛。
    /// </summary>
    internal static class ShapeGeometry
    {
        public static void Size(FrameworkElement control, out double width, out double height)
        {
            width = control.ActualWidth > 0
                ? control.ActualWidth
                : double.IsFinite(control.Width) && control.Width > 0 ? control.Width : 1d;
            height = control.ActualHeight > 0
                ? control.ActualHeight
                : double.IsFinite(control.Height) && control.Height > 0 ? control.Height : 1d;
        }
    }

    /// <summary>
    /// 多边形（TypeKey = <c>Hmi.Polygon</c>）：按 <see cref="Points"/> 画闭合填充区。
    ///
    /// 为什么不做"顶点可拖"的设计期编辑器：那是画布交互的一整块（命中、手柄、撤销），
    /// 而现场九成的多边形是"设备轮廓 / 区域块"，改坐标比拖点更快也更准。先给点串，
    /// 交互式顶点编辑留给画布批次。
    ///
    /// 几何只写进 <see cref="PolygonGeometry"/> 这一个只读依赖属性，模板用 <c>TemplateBinding</c>
    /// 消费它——不在代码里再给 Path 赋一次值（两个写入源迟早打架，且换模板就漏）。
    /// </summary>
    public partial class PolygonElement : ScadaElementBase
    {
        /// <summary>顶点清单：分号分隔的 <c>x,y</c>，取值 0~1（相对本图元框）；少于 3 个点不画</summary>
        [ScadaProperty(Key = "Points", DisplayName = "顶点", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Layout, DefaultValue = "0.5,0;1,1;0,1", IsBindable = false,
                       Description = "0~1 相对坐标，分号分隔：0,0;1,0;0.5,1；少于 3 个点不画")]
        public partial string? Points { get; set; }

        private static readonly DependencyPropertyKey PolygonGeometryPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(PolygonGeometry), typeof(Geometry), typeof(PolygonElement), new PropertyMetadata(null));

        /// <summary>多边形几何（只读，供模板与断言取用）</summary>
        public static readonly DependencyProperty PolygonGeometryProperty = PolygonGeometryPropertyKey.DependencyProperty;

        static PolygonElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(PolygonElement), new FrameworkPropertyMetadata(typeof(PolygonElement)));
        }

        public Geometry? PolygonGeometry => (Geometry?)GetValue(PolygonGeometryProperty);

        partial void OnPointsPropertyChanged() => Rebuild();

        protected override void OnElementRefreshed()
        {
            base.OnElementRefreshed();
            Rebuild();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            Rebuild();
        }

        private void Rebuild()
        {
            var points = ShapePointParser.Parse(Points, clamp: true);
            Geometry? geometry = null;

            if (points.Count >= 3)
            {
                ShapeGeometry.Size(this, out var width, out var height);
                var figure = new PathFigure
                {
                    StartPoint = new Point(points[0].X * width, points[0].Y * height),
                    IsClosed = true,
                    IsFilled = true,
                };
                var segment = new PolyLineSegment();
                for (int i = 1; i < points.Count; i++)
                    segment.Points.Add(new Point(points[i].X * width, points[i].Y * height));
                figure.Segments.Add(segment);
                var path = new PathGeometry();
                path.Figures.Add(figure);
                geometry = path;
            }

            SetValue(PolygonGeometryPropertyKey, geometry);
        }
    }

    /// <summary>
    /// 三次贝塞尔曲线（TypeKey = <c>Hmi.Bezier</c>）：四个点 = 起点、控制点1、控制点2、终点。
    ///
    /// 现场用途是"软管 / 走线 / 弯曲的工艺连线"——直线段表达不了的那些弧。
    /// 只描边不填充：填充一条开放曲线在工艺图上没有语义。
    /// </summary>
    public partial class BezierElement : ScadaElementBase
    {
        /// <summary>控制点清单：四个 <c>x,y</c>，取值 0~1（相对本图元框）</summary>
        [ScadaProperty(Key = "Points", DisplayName = "控制点", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Layout, DefaultValue = "0,1;0.3,0;0.7,0;1,1", IsBindable = false,
                       Description = "0~1 相对坐标，四个点：起点;控制点1;控制点2;终点")]
        public partial string? Points { get; set; }

        private static readonly DependencyPropertyKey BezierGeometryPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(BezierGeometry), typeof(Geometry), typeof(BezierElement), new PropertyMetadata(null));

        /// <summary>曲线几何（只读，供模板与断言取用）</summary>
        public static readonly DependencyProperty BezierGeometryProperty = BezierGeometryPropertyKey.DependencyProperty;

        static BezierElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(BezierElement), new FrameworkPropertyMetadata(typeof(BezierElement)));
        }

        public Geometry? BezierGeometry => (Geometry?)GetValue(BezierGeometryProperty);

        partial void OnPointsPropertyChanged() => Rebuild();

        protected override void OnElementRefreshed()
        {
            base.OnElementRefreshed();
            Rebuild();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            Rebuild();
        }

        private void Rebuild()
        {
            var points = ShapePointParser.Parse(Points, clamp: true);
            Geometry? geometry = null;

            if (points.Count >= 4)
            {
                ShapeGeometry.Size(this, out var width, out var height);
                var figure = new PathFigure
                {
                    StartPoint = new Point(points[0].X * width, points[0].Y * height),
                    IsClosed = false,
                    IsFilled = false,
                };
                figure.Segments.Add(new BezierSegment(
                    new Point(points[1].X * width, points[1].Y * height),
                    new Point(points[2].X * width, points[2].Y * height),
                    new Point(points[3].X * width, points[3].Y * height),
                    true));
                var path = new PathGeometry();
                path.Figures.Add(figure);
                geometry = path;
            }

            SetValue(BezierGeometryPropertyKey, geometry);
        }
    }

    /// <summary>
    /// 动态画线（TypeKey = <c>Hmi.DynamicLine</c>）：从控件中心出发的一条线，
    /// <see cref="Length"/> 与 <see cref="Angle"/> 都可绑变量，随值伸缩/旋转。
    ///
    /// 与「动画」的分工：动画是"值 → 图元位置/尺寸"的通用通道（Hmi.Line 也能用）；
    /// 本图元把长度与角度做成<b>两个独立的数据属性</b>，于是一条线可以同时被两个变量驱动
    /// （例：液位定长度、方向定角度），这是动画通道表达不了的。
    ///
    /// <see cref="Segments"/> &gt; 1 时画锯齿折线：用来示意"振动 / 流动态 / 报警抖动"，
    /// 不必为每种形态再开一个图元。
    /// </summary>
    public partial class DynamicLineElement : ScadaElementBase
    {
        /// <summary>长度：占控件短边的比例（0~1）</summary>
        [ScadaProperty(Key = "Length", DisplayName = "长度", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "0.8", Min = 0, Max = 1, IsBindable = true,
                       Description = "占控件宽/高的比例（0~1）；绑变量即可让线随值伸缩")]
        public partial double Length { get; set; }

        /// <summary>角度：度，0 = 向右，顺时针为正</summary>
        [ScadaProperty(Key = "Angle", DisplayName = "角度", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "0", Min = -360, Max = 360, IsBindable = true,
                       Description = "从控件中心出发的角度（度，0=向右，顺时针）；绑变量即可让线转动")]
        public partial double Angle { get; set; }

        /// <summary>段数：1 = 直线，&gt;1 = 锯齿折线</summary>
        [ScadaProperty(Key = "Segments", DisplayName = "段数", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "1", Min = 1, Max = 64,
                       Description = "1 = 直线；>1 时按锯齿折线画，用于动态轨迹示意")]
        public partial int Segments { get; set; }

        /// <summary>锯齿幅度：占短边的比例</summary>
        [ScadaProperty(Key = "Amplitude", DisplayName = "锯齿幅度", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "0.08", Min = 0, Max = 0.5,
                       Description = "段数 >1 时的横向锯齿幅度（占短边比例）")]
        public partial double Amplitude { get; set; }

        private static readonly DependencyPropertyKey DynamicGeometryPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(DynamicGeometry), typeof(Geometry), typeof(DynamicLineElement), new PropertyMetadata(null));

        /// <summary>线几何（只读，供模板与断言取用）</summary>
        public static readonly DependencyProperty DynamicGeometryProperty = DynamicGeometryPropertyKey.DependencyProperty;

        static DynamicLineElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(DynamicLineElement), new FrameworkPropertyMetadata(typeof(DynamicLineElement)));
        }

        public Geometry? DynamicGeometry => (Geometry?)GetValue(DynamicGeometryProperty);

        partial void OnLengthPropertyChanged() => Rebuild();
        partial void OnAnglePropertyChanged() => Rebuild();
        partial void OnSegmentsPropertyChanged() => Rebuild();
        partial void OnAmplitudePropertyChanged() => Rebuild();

        protected override void OnElementRefreshed()
        {
            base.OnElementRefreshed();
            Rebuild();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            Rebuild();
        }

        private void Rebuild()
        {
            ShapeGeometry.Size(this, out var width, out var height);
            double cx = width / 2d;
            double cy = height / 2d;
            double radius = Math.Min(width, height) * Math.Clamp(Length, 0, 1);
            double radians = Math.Clamp(Angle, -360, 360) * Math.PI / 180d;
            double ux = Math.Cos(radians);
            double uy = Math.Sin(radians);
            double nx = -uy;
            double ny = ux;
            int segments = Math.Clamp(Segments, 1, 64);
            double amplitude = Math.Min(width, height) * Math.Clamp(Amplitude, 0, 0.5);

            var figure = new PathFigure
            {
                StartPoint = new Point(cx - ux * radius / 2d, cy - uy * radius / 2d),
                IsClosed = false,
                IsFilled = false,
            };

            if (segments <= 1)
            {
                figure.Segments.Add(new LineSegment(new Point(cx + ux * radius / 2d, cy + uy * radius / 2d), true));
            }
            else
            {
                var segment = new PolyLineSegment();
                for (int i = 1; i <= segments; i++)
                {
                    double t = i / (double)segments;
                    double along = -radius / 2d + radius * t;
                    double offset = i % 2 == 0 ? amplitude : -amplitude;
                    segment.Points.Add(new Point(
                        cx + ux * along + nx * offset,
                        cy + uy * along + ny * offset));
                }
                figure.Segments.Add(segment);
            }

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            SetValue(DynamicGeometryPropertyKey, geometry);
        }
    }
}
