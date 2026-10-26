using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using VisionMaster.Scada;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 柱状图（TypeKey = <c>Hmi.BarChart</c>）：几个对象彼此比高低。
    ///
    /// 为什么用 <see cref="OnRender"/> 直接画，而不是模板 + ItemsControl
    /// ---------
    /// 柱子的位置/高度全部依赖<b>实时算出来的轴范围</b>（含负值，见 <see cref="ScadaChartMath.Range"/>），
    /// 而轴范围又依赖控件尺寸。模板方案要在"尺寸变化 → 逐项重算像素 → 通知 N 个行模型"之间来回同步，
    /// 还得为每根柱造一个可绑定对象；这里一次画完，尺寸变了重画一次，没有中间态可以不同步。
    /// 图元本身也没有可点的子控件（点柱子不是交互需求），所以不违背"控件模板放外观"的纪律。
    ///
    /// 数据
    /// ---------
    /// 每拍从 <see cref="ScadaElementBase.RuntimeContext"/> 的 <c>Values</c> 按名字取值：
    /// 变量解析不到 → 灰色斜纹柱 + 「未解析」，<b>不画成 0</b>（缺失与 0 是两件事）。
    /// </summary>
    public partial class BarChartElement : ScadaElementBase
    {
        static BarChartElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(BarChartElement),
                new FrameworkPropertyMetadata(typeof(BarChartElement)));
        }

        [ScadaProperty(Key = ScadaChartCodec.BarKey, DisplayName = "数据项",
            Kind = ElementPropertyKind.ChartSeries, Group = ScadaPropertyGroups.Data, DefaultValue = "",
            Description = "每行一项：标签=变量名[=#颜色]。例：\n周一=Qty1\n周二=Qty2=#38BDF8")]
        public partial string SeriesJson { get; set; }

        [ScadaProperty(Key = "Horizontal", DisplayName = "横向",
            Kind = ElementPropertyKind.Bool, Group = ScadaPropertyGroups.Appearance, DefaultValue = "False",
            Description = "勾选 = 横向柱（标签长的场景更好读）")]
        public partial bool Horizontal { get; set; }

        [ScadaProperty(Key = "MaxValue", DisplayName = "最大值",
            Kind = ElementPropertyKind.Number, Group = ScadaPropertyGroups.Data, DefaultValue = "0", Min = 0,
            Description = "0 = 按实际最大值自动；>0 固定上界（同屏多图对齐时用）")]
        public partial double MaxValue { get; set; }

        [ScadaProperty(Key = "TargetValue", DisplayName = "目标值",
            Kind = ElementPropertyKind.Number, Group = ScadaPropertyGroups.Data, DefaultValue = "0",
            Description = "0 = 不画目标线；>0 画一条虚线，超过的柱变红且数值加粗")]
        public partial double TargetValue { get; set; }

        [ScadaProperty(Key = "ShowValues", DisplayName = "显示数值",
            Kind = ElementPropertyKind.Bool, Group = ScadaPropertyGroups.Appearance, DefaultValue = "True",
            Description = "在柱顶/柱尾标数值")]
        public partial bool ShowValues { get; set; }

        [ScadaProperty(Key = "SortMode", DisplayName = "排序",
            Kind = ElementPropertyKind.Choice, Choices = new[] { "None", "Desc", "Asc" },
            Group = ScadaPropertyGroups.Data, DefaultValue = "Desc",
            Description = "None = 按配置顺序；Desc = 降序；Asc = 升序。未解析的柱永远排最后")]
        public partial string SortMode { get; set; }

        [ScadaProperty(Key = "ValueFormat", DisplayName = "数值格式",
            Kind = ElementPropertyKind.Text, Group = ScadaPropertyGroups.Data, DefaultValue = "",
            Description = "如 F0 / P1；空 = 0.###。非法格式串回落 0.###")]
        public partial string ValueFormat { get; set; }

        private ScadaBeatSource? _beat;

        protected override void OnRuntimeContextChanged(ScadaRuntimeContext? oldContext, ScadaRuntimeContext? newContext)
        {
            base.OnRuntimeContextChanged(oldContext, newContext);

            if (oldContext?.Beat is { } oldBeat)
                oldBeat.Beat -= OnBeat;

            _beat = newContext?.Beat;

            if (_beat != null)
                _beat.Beat += OnBeat;

            InvalidateVisual();
        }

        private void OnBeat(TimeSpan elapsed) => InvalidateVisual();

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            var width = ActualWidth;
            var height = ActualHeight;
            if (width < 24 || height < 24)
                return;

            var items = ScadaChartCodec.Parse(SeriesJson);
            if (items.Count == 0)
            {
                ChartPaint.Hint(dc, this, width, height, "未配置数据项（属性面板「数据项」每行一项：标签=变量名）");
                return;
            }

            var bars = items
                .Select((item, index) => new ScadaChartMath.BarDatum(item.Label, ChartPaint.Read(RuntimeContext, item.Variable), item.Color))
                .ToList();

            bars = ScadaChartMath.Sort(bars, ChartPaint.ParseSort(SortMode)).ToList();

            var range = ScadaChartMath.Range(bars, MaxValue);
            var zero = ScadaChartMath.ZeroFraction(range);
            var target = TargetValue > 0 ? TargetValue : 0;

            if (Horizontal)
                PaintHorizontal(dc, bars, range, target, width, height);
            else
                PaintVertical(dc, bars, range, zero, target, width, height);
        }

        private void PaintVertical(DrawingContext dc, IReadOnlyList<ScadaChartMath.BarDatum> bars,
            (double Min, double Max) range, double zero, double target, double width, double height)
        {
            const double padLeft = 44, padRight = 12, padTop = 16, padBottom = 26;
            var innerWidth = width - padLeft - padRight;
            var innerHeight = height - padTop - padBottom;
            if (innerWidth <= 8 || innerHeight <= 8)
                return;

            var baselineY = padTop + innerHeight * zero;
            var zeroY = padTop + innerHeight * zero;

            ChartPaint.Axis(dc, padLeft, padTop, padLeft, padTop + innerHeight);
            ChartPaint.Axis(dc, padLeft, padTop + innerHeight, width - padRight, padTop + innerHeight);

            var count = bars.Count;
            var gap = Math.Min(18, innerWidth / (count * 4.0));
            var barWidth = Math.Max(6, Math.Min(56, (innerWidth - gap * (count - 1)) / Math.Max(1, count)));

            for (var i = 0; i < count; i++)
            {
                var bar = bars[i];
                var x = padLeft + i * (barWidth + gap);

                if (bar.Value is not { } value)
                {
                    // 未解析：画一小段灰条 + 文字，占位但不冒充 0
                    var missingHeight = Math.Min(24, innerHeight * 0.15);
                    var rect = new Rect(x, zeroY - missingHeight, barWidth, missingHeight);
                    dc.DrawRectangle(ChartPaint.MissingBrush, null, rect);
                    ChartPaint.Text(dc, this, "未解析", x + barWidth / 2, zeroY - missingHeight - 12, 10, ChartPaint.FaintBrush, center: true);
                }
                else
                {
                    var fraction = ScadaChartMath.Fraction(value, range);
                    var pixels = Math.Abs(fraction - zero) * innerHeight;
                    var over = target > 0 && value > target;
                    var brush = ChartPaint.Fill(bar.Color, i, over);
                    var y = value >= 0 ? zeroY - pixels : zeroY;

                    dc.DrawRectangle(brush, null, new Rect(x, y, barWidth, Math.Max(1, pixels)));

                    if (ShowValues)
                        ChartPaint.Text(dc, this, ScadaChartMath.Text(value, ValueFormat),
                            x + barWidth / 2, y - 14, 10.5, over ? ChartPaint.DangerBrush : ChartPaint.TextBrush,
                            center: true, bold: over);
                }

                ChartPaint.Text(dc, this, bar.Label, x + barWidth / 2, padTop + innerHeight + 6, 10, ChartPaint.FaintBrush, center: true);
            }

            if (target > 0)
                ChartPaint.TargetLine(dc, this, padLeft, padTop + innerHeight - innerHeight * ScadaChartMath.Fraction(target, range),
                    width - padRight, target, ValueFormat);

            _ = baselineY;
        }

        private void PaintHorizontal(DrawingContext dc, IReadOnlyList<ScadaChartMath.BarDatum> bars,
            (double Min, double Max) range, double target, double width, double height)
        {
            const double padLeft = 78, padRight = 42, padTop = 14, padBottom = 12;
            var innerWidth = width - padLeft - padRight;
            var innerHeight = height - padTop - padBottom;
            if (innerWidth <= 8 || innerHeight <= 8)
                return;

            var count = bars.Count;
            var gap = Math.Min(10, innerHeight / (count * 4.0));
            var barHeight = Math.Max(5, Math.Min(28, (innerHeight - gap * (count - 1)) / Math.Max(1, count)));

            ChartPaint.Axis(dc, padLeft, padTop, padLeft, padTop + innerHeight);
            ChartPaint.Axis(dc, padLeft, padTop + innerHeight, width - padRight, padTop + innerHeight);

            for (var i = 0; i < count; i++)
            {
                var bar = bars[i];
                var y = padTop + i * (barHeight + gap);

                ChartPaint.Text(dc, this, bar.Label, padLeft - 6, y + barHeight / 2 - 7, 10, ChartPaint.FaintBrush, right: true);

                if (bar.Value is not { } value)
                {
                    dc.DrawRectangle(ChartPaint.MissingBrush, null, new Rect(padLeft, y, Math.Min(60, innerWidth * 0.2), barHeight));
                    ChartPaint.Text(dc, this, "未解析", padLeft + 6, y + barHeight / 2 - 7, 10, ChartPaint.FaintBrush);
                    continue;
                }

                var fraction = ScadaChartMath.Fraction(value, range);
                var pixels = Math.Max(1, fraction * innerWidth);
                var over = target > 0 && value > target;
                dc.DrawRectangle(ChartPaint.Fill(bar.Color, i, over), null, new Rect(padLeft, y, pixels, barHeight));

                if (ShowValues)
                    ChartPaint.Text(dc, this, ScadaChartMath.Text(value, ValueFormat),
                        padLeft + pixels + 6, y + barHeight / 2 - 7, 10.5,
                        over ? ChartPaint.DangerBrush : ChartPaint.TextBrush, bold: over);
            }

            if (target > 0)
            {
                var x = padLeft + innerWidth * ScadaChartMath.Fraction(target, range);
                ChartPaint.TargetLineV(dc, this, x, padTop, padTop + innerHeight, target, ValueFormat);
            }
        }
    }

    /// <summary>
    /// 饼图（TypeKey = <c>Hmi.PieChart</c>）：整体里各占多少。
    ///
    /// 与柱状图同一套数据项与取值口径，差别只在画法：占比、环形、小片合并
    /// （规则全在 <see cref="ScadaChartMath.Slices"/>，这里只负责把角度画出来）。
    /// </summary>
    public partial class PieChartElement : ScadaElementBase
    {
        static PieChartElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(PieChartElement),
                new FrameworkPropertyMetadata(typeof(PieChartElement)));
        }

        [ScadaProperty(Key = ScadaChartCodec.PieKey, DisplayName = "数据项",
            Kind = ElementPropertyKind.ChartSeries, Group = ScadaPropertyGroups.Data, DefaultValue = "",
            Description = "每行一项：标签=变量名[=#颜色]。合计为 0 时显示「没有计数」")]
        public partial string SlicesJson { get; set; }

        [ScadaProperty(Key = "Donut", DisplayName = "环形",
            Kind = ElementPropertyKind.Bool, Group = ScadaPropertyGroups.Appearance, DefaultValue = "True",
            Description = "勾选 = 环形（中间显示合计）；不勾 = 实心饼")]
        public partial bool Donut { get; set; }

        [ScadaProperty(Key = "ShowPercent", DisplayName = "显示百分比",
            Kind = ElementPropertyKind.Bool, Group = ScadaPropertyGroups.Appearance, DefaultValue = "True",
            Description = "图例里显示占比；数值始终显示")]
        public partial bool ShowPercent { get; set; }

        [ScadaProperty(Key = "MergeBelow", DisplayName = "小片合并阈值",
            Kind = ElementPropertyKind.Number, Group = ScadaPropertyGroups.Data, DefaultValue = "0.03",
            Min = 0, Max = 1,
            Description = "占比小于该值的片并入「其他」；0 = 不合并。片数 > 8 时强制按 2% 合并")]
        public partial double MergeBelow { get; set; }

        [ScadaProperty(Key = "ValueFormat", DisplayName = "数值格式",
            Kind = ElementPropertyKind.Text, Group = ScadaPropertyGroups.Data, DefaultValue = "",
            Description = "如 F0；空 = 0.###")]
        public partial string ValueFormat { get; set; }

        private ScadaBeatSource? _beat;

        protected override void OnRuntimeContextChanged(ScadaRuntimeContext? oldContext, ScadaRuntimeContext? newContext)
        {
            base.OnRuntimeContextChanged(oldContext, newContext);

            if (oldContext?.Beat is { } oldBeat)
                oldBeat.Beat -= OnBeat;

            _beat = newContext?.Beat;

            if (_beat != null)
                _beat.Beat += OnBeat;

            InvalidateVisual();
        }

        private void OnBeat(TimeSpan elapsed) => InvalidateVisual();

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            var width = ActualWidth;
            var height = ActualHeight;
            if (width < 24 || height < 24)
                return;

            var items = ScadaChartCodec.Parse(SlicesJson);
            if (items.Count == 0)
            {
                ChartPaint.Hint(dc, this, width, height, "未配置数据项（属性面板「数据项」每行一项：标签=变量名）");
                return;
            }

            var data = items
                .Select(item => new ScadaChartMath.PieDatum(item.Label, ChartPaint.Read(RuntimeContext, item.Variable), item.Color))
                .ToList();

            var slices = ScadaChartMath.Slices(data, MergeBelow);
            if (slices.Count == 0)
            {
                ChartPaint.EmptyCircle(dc, this, width, height);
                return;
            }

            var legendWidth = Math.Min(230, width * 0.46);
            var plotWidth = width - legendWidth;
            var radius = Math.Max(10, Math.Min(plotWidth, height) / 2 - 16);
            var cx = plotWidth / 2;
            var cy = height / 2;
            var inner = Donut ? radius * 0.58 : 0;

            var total = slices.Sum(s => s.Value);
            var start = -Math.PI / 2;

            for (var i = 0; i < slices.Count; i++)
            {
                var slice = slices[i];
                var sweep = slice.Value / total * Math.PI * 2;
                var brush = ChartPaint.Fill(slice.Color, i, false);

                if (sweep >= Math.PI * 2 - 1e-6)
                {
                    dc.DrawEllipse(brush, null, new Point(cx, cy), radius, radius);
                    if (inner > 0)
                        dc.DrawEllipse(Brushes.Transparent, null, new Point(cx, cy), inner, inner);
                }
                else
                {
                    dc.DrawGeometry(brush, null, ChartPaint.PieGeometry(cx, cy, radius, inner, start, sweep));
                }

                start += sweep;
            }

            if (Donut)
            {
                ChartPaint.Text(dc, this, ScadaChartMath.Text(total, ValueFormat), cx, cy - 14, 20, ChartPaint.TextBrush, center: true, bold: true);
                ChartPaint.Text(dc, this, "合计", cx, cy + 8, 10.5, ChartPaint.FaintBrush, center: true);
            }

            var lx = plotWidth + 12;
            var ly = Math.Max(16, cy - slices.Count * 13);
            for (var i = 0; i < slices.Count; i++)
            {
                var slice = slices[i];
                var y = ly + i * 26;
                if (y > height - 14)
                    break;

                dc.DrawRectangle(ChartPaint.Fill(slice.Color, i, false), null, new Rect(lx, y - 9, 10, 10));

                var text = ShowPercent
                    ? $"{slice.Label}　{slice.Percent:0.#}%　{ScadaChartMath.Text(slice.Value, ValueFormat)}"
                    : $"{slice.Label}　{ScadaChartMath.Text(slice.Value, ValueFormat)}";

                ChartPaint.Text(dc, this, text, lx + 16, y - 8, 11, ChartPaint.TextBrush);
            }
        }
    }

    /// <summary>图表绘制的小工具（颜色、文字、轴、提示）。只被本文件的图元使用</summary>
    internal static class ChartPaint
    {
        private static readonly Color[] Palette =
        {
            Color.FromRgb(0x25, 0x63, 0xEB), Color.FromRgb(0x38, 0xBD, 0xF8),
            Color.FromRgb(0x22, 0xC5, 0x5E), Color.FromRgb(0xF5, 0x9E, 0x0B),
            Color.FromRgb(0x8B, 0x5C, 0xF6), Color.FromRgb(0x14, 0xB8, 0xA6),
        };

        private static readonly Dictionary<string, SolidColorBrush> Cache = new(StringComparer.OrdinalIgnoreCase);

        public static readonly Brush TextBrush = Freeze(Color.FromRgb(0xE6, 0xEE, 0xF7));
        public static readonly Brush FaintBrush = Freeze(Color.FromRgb(0x8A, 0x94, 0xA6));
        public static readonly Brush AxisBrush = Freeze(Color.FromRgb(0x33, 0x41, 0x55));
        public static readonly Brush DangerBrush = Freeze(Color.FromRgb(0xE0, 0x3A, 0x2B));
        public static readonly Brush MissingBrush = Freeze(Color.FromRgb(0x4B, 0x55, 0x63));

        public static Brush Fill(string color, int index, bool over)
        {
            if (over)
                return DangerBrush;

            if (!string.IsNullOrWhiteSpace(color))
            {
                lock (Cache)
                {
                    if (Cache.TryGetValue(color, out var cached))
                        return cached;
                }

                try
                {
                    var parsed = (Color)ColorConverter.ConvertFromString(color);
                    var brush = Freeze(parsed);
                    lock (Cache)
                        Cache[color] = brush;
                    return brush;
                }
                catch (FormatException)
                {
                    // 落到调色板
                }
            }

            return Freeze(Palette[Math.Abs(index) % Palette.Length]);
        }

        public static ScadaChartMath.BarSort ParseSort(string? text) => text?.Trim().ToLowerInvariant() switch
        {
            "desc" => ScadaChartMath.BarSort.Descending,
            "asc" => ScadaChartMath.BarSort.Ascending,
            _ => ScadaChartMath.BarSort.None,
        };

        /// <summary>按名字取变量当前值；解析不到返回 null（不是 0）</summary>
        public static double? Read(ScadaRuntimeContext? context, string? variable)
        {
            if (context?.Values == null || string.IsNullOrWhiteSpace(variable))
                return null;

            try
            {
                if (!context.Values.TryResolve(Guid.Empty, variable, out var handle) || handle?.Value == null)
                    return null;

                return Convert.ToDouble(handle.Value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void Text(DrawingContext dc, Visual owner, string text, double x, double y, double size,
            Brush brush, bool center = false, bool right = false, bool bold = false)
        {
            if (string.IsNullOrEmpty(text))
                return;

            var dpi = VisualTreeHelper.GetDpi(owner).PixelsPerDip;
            var typeface = new Typeface(new FontFamily("Microsoft YaHei UI"),
                FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);

            var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                typeface, size, brush, dpi);

            var originX = center ? x - formatted.Width / 2 : right ? x - formatted.Width : x;
            dc.DrawText(formatted, new Point(originX, y));
        }

        public static void Axis(DrawingContext dc, double x1, double y1, double x2, double y2)
        {
            var pen = new Pen(AxisBrush, 1);
            pen.Freeze();
            dc.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));
        }

        public static void TargetLine(DrawingContext dc, Visual owner, double x1, double y, double x2, double value, string? format)
        {
            var pen = new Pen(DangerBrush, 1) { DashStyle = new DashStyle(new double[] { 5, 4 }, 0) };
            pen.Freeze();
            dc.DrawLine(pen, new Point(x1, y), new Point(x2, y));
            Text(dc, owner, "目标 " + ScadaChartMath.Text(value, format), x2 - 4, y - 14, 10, DangerBrush, right: true);
        }

        public static void TargetLineV(DrawingContext dc, Visual owner, double x, double y1, double y2, double value, string? format)
        {
            var pen = new Pen(DangerBrush, 1) { DashStyle = new DashStyle(new double[] { 5, 4 }, 0) };
            pen.Freeze();
            dc.DrawLine(pen, new Point(x, y1), new Point(x, y2));
            Text(dc, owner, "目标 " + ScadaChartMath.Text(value, format), x + 4, y1, 10, DangerBrush);
        }

        public static void Hint(DrawingContext dc, Visual owner, double width, double height, string text)
            => Text(dc, owner, text, width / 2, height / 2 - 8, 11.5, FaintBrush, center: true);

        public static void EmptyCircle(DrawingContext dc, Visual owner, double width, double height)
        {
            var pen = new Pen(AxisBrush, 2);
            pen.Freeze();
            var radius = Math.Max(10, Math.Min(width, height) / 2 - 24);
            dc.DrawEllipse(null, pen, new Point(width / 2, height / 2), radius, radius);
            Text(dc, owner, "没有计数", width / 2, height / 2 - 8, 11.5, FaintBrush, center: true);
        }

        /// <summary>饼图一片的几何（环形时挖掉内圆；sweep 已保证 &lt; 2π）</summary>
        public static Geometry PieGeometry(double cx, double cy, double radius, double inner, double start, double sweep)
        {
            var geometry = new StreamGeometry();

            using (var ctx = geometry.Open())
            {
                var p1 = new Point(cx + radius * Math.Cos(start), cy + radius * Math.Sin(start));
                var end = start + sweep;
                var p2 = new Point(cx + radius * Math.Cos(end), cy + radius * Math.Sin(end));
                var large = sweep > Math.PI ? 1 : 0;

                ctx.BeginFigure(p1, isFilled: true, isClosed: true);
                ctx.ArcTo(p2, new Size(radius, radius), 0, large == 1, SweepDirection.Clockwise, true, false);

                if (inner > 0)
                {
                    var q2 = new Point(cx + inner * Math.Cos(end), cy + inner * Math.Sin(end));
                    var q1 = new Point(cx + inner * Math.Cos(start), cy + inner * Math.Sin(start));
                    ctx.LineTo(q2, true, false);
                    ctx.ArcTo(q1, new Size(inner, inner), 0, large == 1, SweepDirection.Counterclockwise, true, false);
                }
                else
                {
                    ctx.LineTo(new Point(cx, cy), true, false);
                }
            }

            geometry.Freeze();
            return geometry;
        }

        private static SolidColorBrush Freeze(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
