using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 线 / 折线 / 箭头图元（TypeKey = <c>Hmi.Line</c>）。
    ///
    /// 一个 TypeKey 覆盖三种形态：两个端点 = 直线，多个端点 = 折线，<see cref="Arrow"/> 非 None = 箭头。
    /// 不拆三个类：属性面板、命中测试、序列化都会变成三份。
    ///
    /// <b>端点是相对坐标（0~1）</b>，按图元框缩放：这样拖动/缩放图元时线跟着走，
    /// 不必在每次几何变更时重算一堆绝对坐标。绝对坐标写法留给后续“端点吸附到图元边缘”那一版。
    ///
    /// <b>箭头是三角形填充</b>：WPF 的 Path 没有 marker，箭头必须自己算几何。
    /// 箭头长度随线宽走（线宽 × 3.5），细线不会顶着一个大箭头。
    /// </summary>
    [TemplatePart(Name = PartLinePath, Type = typeof(Path))]
    public partial class LineElement : ScadaElementBase
    {
        /// <summary>模板部件名：线本体</summary>
        public const string PartLinePath = "LinePath";

        /// <summary>
        /// 端点清单：分号分隔的 <c>x,y</c>，取值 0~1（相对本图元框）。
        /// 例：<c>0,0.5;1,0.5</c> = 水平线；<c>0,0;1,1</c> = 对角线；三个点 = 折线。
        /// </summary>
        [ScadaProperty(Key = "Points", DisplayName = "端点", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Layout, DefaultValue = "0,0;1,1", IsBindable = false,
                       Description = "分号分隔的 x,y，取值 0~1（相对本图元框）。两个点=直线，三个以上=折线")]
        public partial string? Points { get; set; }

        /// <summary>线型</summary>
        [ScadaProperty(Key = "Dash", DisplayName = "线型", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "Solid",
                       Choices = new[] { "Solid", "Dash", "Dot", "DashDot" },
                       Description = "实线 / 虚线 / 点线 / 点划线")]
        public partial string? Dash { get; set; }

        /// <summary>箭头方向</summary>
        [ScadaProperty(Key = "Arrow", DisplayName = "箭头", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "None",
                       Choices = new[] { "None", "Start", "End", "Both" },
                       Description = "无 / 起点 / 终点 / 双向；箭头大小随线宽走")]
        public partial string? Arrow { get; set; }

        private Path? _path;

        static LineElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(LineElement),
                new FrameworkPropertyMetadata(typeof(LineElement)));
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _path = GetTemplateChild(PartLinePath) as Path;
            RebuildGeometry();
        }

        protected override void OnElementRefreshed()
        {
            base.OnElementRefreshed();
            RebuildGeometry();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            RebuildGeometry();
        }

        private void RebuildGeometry()
        {
            if (_path == null)
                return;

            var points = ParsePoints(Points);
            var width = ActualWidth > 0 ? ActualWidth : Math.Max(1, Width);
            var height = ActualHeight > 0 ? ActualHeight : Math.Max(1, Height);

            var screen = new List<Point>(points.Count);
            foreach (var p in points)
                screen.Add(new Point(p.X * width, p.Y * height));

            var geometry = new GeometryGroup { FillRule = FillRule.Nonzero };

            var line = new PathFigure { StartPoint = screen[0], IsClosed = false, IsFilled = false };
            var segment = new PolyLineSegment();
            for (var i = 1; i < screen.Count; i++)
                segment.Points.Add(screen[i]);
            line.Segments.Add(segment);
            geometry.Children.Add(new PathGeometry(new[] { line }));

            var arrow = Arrow ?? "None";
            if (arrow is "Start" or "Both")
                AddArrowHead(geometry, screen[1], screen[0]);
            if (arrow is "End" or "Both")
                AddArrowHead(geometry, screen[^2], screen[^1]);

            _path.Data = geometry;
            _path.Stroke = Stroke;
            _path.StrokeThickness = Math.Max(1, StrokeThickness);
            _path.StrokeStartLineCap = PenLineCap.Round;
            _path.StrokeEndLineCap = PenLineCap.Round;
            _path.StrokeLineJoin = PenLineJoin.Round;
            _path.Fill = Stroke;
            _path.StrokeDashArray = (Dash ?? "Solid") switch
            {
                "Dash" => new DoubleCollection { 4, 2 },
                "Dot" => new DoubleCollection { 1, 2 },
                "DashDot" => new DoubleCollection { 4, 2, 1, 2 },
                _ => null,
            };
        }

        /// <summary>
        /// 在 <paramref name="tip"/> 处加一个指向 <paramref name="from"/> 方向的三角箭头。
        /// 长度 = 线宽 × 3.5（下限 6），半宽 = 长度 × 0.28。
        /// </summary>
        private void AddArrowHead(GeometryGroup geometry, Point from, Point tip)
        {
            var dx = tip.X - from.X;
            var dy = tip.Y - from.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 0.001)
                return;

            var ux = dx / length;
            var uy = dy / length;
            var head = Math.Max(6, Math.Max(1, StrokeThickness) * 3.5);
            var half = head * 0.28;

            var baseX = tip.X - ux * head;
            var baseY = tip.Y - uy * head;
            var nx = -uy * half;
            var ny = ux * half;

            var figure = new PathFigure { StartPoint = tip, IsClosed = true, IsFilled = true };
            figure.Segments.Add(new LineSegment(new Point(baseX + nx, baseY + ny), true));
            figure.Segments.Add(new LineSegment(new Point(baseX - nx, baseY - ny), true));
            geometry.Children.Add(new PathGeometry(new[] { figure }));
        }

        private static List<Point> ParsePoints(string? text)
        {
            var result = new List<Point>();
            if (!string.IsNullOrWhiteSpace(text))
            {
                foreach (var pair in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = pair.Split(',');
                    if (parts.Length != 2)
                        continue;

                    if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                        && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                    {
                        result.Add(new Point(Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1)));
                    }
                }
            }

            if (result.Count < 2)
            {
                result.Clear();
                result.Add(new Point(0, 0));
                result.Add(new Point(1, 1));
            }

            return result;
        }
    }
}
