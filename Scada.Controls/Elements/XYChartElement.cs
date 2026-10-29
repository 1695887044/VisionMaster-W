using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VisionMaster.Scada;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// XY 曲线图元（TypeKey = <c>Hmi.XYChart</c>）：把一个工程变量当 X、另一个当 Y，
    /// 每拍取一对值追加成一个点，画成"点集 / 轨迹"（例如 位置—压力、角度—扭矩、温度—湿度）。
    ///
    /// 与多曲线趋势（<see cref="MultiTrendElement"/>）的分工
    /// ---------
    /// 趋势是<b>值随时间</b>走（横轴是时间，滚动窗口）；本图元是<b>两个量互相对照</b>
    /// （横轴是另一个变量的当前值，没有时间轴）。所以它不需要时间窗、也不需要回放历史：
    /// 缓冲只是一段"最近 MaxPoints 对值"的轨迹，重启运行就作废。
    ///
    /// 为什么绘制放在模板里的 <see cref="XYChartSurface"/> 而不是本类的 OnRender
    /// ---------
    /// 卡片底（<c>Fill</c> / <c>Stroke</c> / 圆角）由模板里的 <c>Border</c> 画，而 WPF 的
    /// <b>模板子元素画在控件自身 OnRender 之上</b>（实测：控件 OnRender 里铺满的矩形被
    /// 模板 Border 的不透明底色整个盖住）。于是"纯 OnRender 画图"必须落在一个模板子元素上——
    /// 这正是 <see cref="TrendSurface"/> 之于多曲线趋势的同一条做法。
    ///
    /// 数据
    /// ---------
    /// 每拍从 <see cref="ScadaElementBase.RuntimeContext"/> 的 <c>Values</c> 按名字解析两个句柄；
    /// 任一解析不到 → 画面中间给一行提示（「未接入值源」/「变量未解析」），<b>不画成 0</b>
    /// （缺失与 0 是两件事）。同一对值连续两拍只记一个点：变量没变就不该长点。
    /// </summary>
    [TemplatePart(Name = PartSurface, Type = typeof(XYChartSurface))]
    public partial class XYChartElement : ScadaElementBase
    {
        /// <summary>绘制面的模板部件名（见 <c>Themes/XYChartElement.xaml</c>）</summary>
        public const string PartSurface = "ChartSurface";

        /// <summary>网格与刻度的等分数（横竖各 4 格）</summary>
        private const int GridDivisions = 4;

        private const string DesignHint = "设计期不采样";
        private const string NotConnectedHint = "未接入值源（运行期才有数据）";
        private const string UnconfiguredHint = "未配置 X / Y 变量";
        private const string WaitingHint = "等待采样…";

        [ScadaProperty(Key = "XVariable", DisplayName = "X 变量", Kind = ElementPropertyKind.Text,
            Group = ScadaPropertyGroups.Data, DefaultValue = "",
            Description = "横轴取值的工程变量名（填名字，不是绑定）；留空 = 未配置")]
        public partial string? XVariable { get; set; }

        [ScadaProperty(Key = "YVariable", DisplayName = "Y 变量", Kind = ElementPropertyKind.Text,
            Group = ScadaPropertyGroups.Data, DefaultValue = "",
            Description = "纵轴取值的工程变量名（填名字，不是绑定）；留空 = 未配置")]
        public partial string? YVariable { get; set; }

        [ScadaProperty(Key = "MaxPoints", DisplayName = "最多点数", Kind = ElementPropertyKind.Number,
            Group = ScadaPropertyGroups.Data, DefaultValue = "200", Min = 2, Max = 5000,
            Description = "轨迹最多保留多少对值（整数）；满了挤掉最旧的一点（显示缓冲，不是记录系统）")]
        // 用 int 而不是 double：容量没有小数语义。与 Hmi.AlarmTable.MaxRows 同类型，
        // 面板输入 2.5 时两处行为一致（由 ScadaElementBase.ConvertFromString 四舍五入，
        // 手改 .vms 写成 "200.0" 也能读成 200）。
        public partial int MaxPoints { get; set; }

        [ScadaProperty(Key = "AutoScale", DisplayName = "自动量程", Kind = ElementPropertyKind.Bool,
            Group = ScadaPropertyGroups.Scale, DefaultValue = "True",
            Description = "勾选 = 按数据的最小/最大自动定标（留 5% 边距）；不勾 = 用下面四个固定值")]
        public partial bool AutoScale { get; set; }

        [ScadaProperty(Key = "XMin", DisplayName = "X 最小", Kind = ElementPropertyKind.Number,
            Group = ScadaPropertyGroups.Scale, DefaultValue = "0",
            Description = "横轴下界；只在「自动量程」不勾时生效")]
        public partial double XMin { get; set; }

        [ScadaProperty(Key = "XMax", DisplayName = "X 最大", Kind = ElementPropertyKind.Number,
            Group = ScadaPropertyGroups.Scale, DefaultValue = "100",
            Description = "横轴上界；只在「自动量程」不勾时生效")]
        public partial double XMax { get; set; }

        [ScadaProperty(Key = "YMin", DisplayName = "Y 最小", Kind = ElementPropertyKind.Number,
            Group = ScadaPropertyGroups.Scale, DefaultValue = "0",
            Description = "纵轴下界；只在「自动量程」不勾时生效")]
        public partial double YMin { get; set; }

        [ScadaProperty(Key = "YMax", DisplayName = "Y 最大", Kind = ElementPropertyKind.Number,
            Group = ScadaPropertyGroups.Scale, DefaultValue = "100",
            Description = "纵轴上界；只在「自动量程」不勾时生效")]
        public partial double YMax { get; set; }

        [ScadaProperty(Key = "LineColor", DisplayName = "曲线颜色", Kind = ElementPropertyKind.Color,
            Group = ScadaPropertyGroups.Appearance, DefaultValue = "#FF2D7DD2",
            Description = "折线与采样点的颜色")]
        public partial Brush? LineColor { get; set; }

        [ScadaProperty(Key = "PointSize", DisplayName = "点大小", Kind = ElementPropertyKind.Number,
            Group = ScadaPropertyGroups.Appearance, DefaultValue = "3", Min = 0, Max = 20,
            Description = "每个采样点的直径（像素）；0 = 只画折线不画点")]
        public partial double PointSize { get; set; }

        [ScadaProperty(Key = "ShowGrid", DisplayName = "显示网格", Kind = ElementPropertyKind.Bool,
            Group = ScadaPropertyGroups.Appearance, DefaultValue = "True",
            Description = "画 4 × 4 的参考网格；坐标轴与刻度数字始终显示")]
        public partial bool ShowGrid { get; set; }

        // ---- 绘制用的常量笔刷（与柱/饼图同一套深色取值；R140 只约束 XAML，这里与 ChartPaint 同口径） ----

        private static readonly Brush LabelBrush = Frozen(Color.FromRgb(0xE6, 0xEE, 0xF7));
        private static readonly Brush FaintBrush = Frozen(Color.FromRgb(0x8A, 0x94, 0xA6));
        private static readonly Brush GridBrush = Frozen(Color.FromRgb(0x2A, 0x37, 0x45));
        private static readonly Brush AxisBrush = Frozen(Color.FromRgb(0x33, 0x41, 0x55));
        private static readonly Brush FallbackLineBrush = Frozen(Color.FromRgb(0x2D, 0x7D, 0xD2));

        private static readonly Typeface LabelTypeface = new(
            new FontFamily("Microsoft YaHei UI"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        /// <summary>轨迹缓冲：容量 = <see cref="MaxPoints"/>，满了从队首挤掉最旧的一点</summary>
        private readonly Queue<(double X, double Y)> _points = new();

        private XYChartSurface? _surface;

        /// <summary>上一对入队的值（"同一对值不重复追加"的判据；清空后作废）</summary>
        private bool _hasLast;
        private double _lastX;
        private double _lastY;

        /// <summary>运行期提示（null = 数据正常）。设计期不看这一位，固定显示 <see cref="DesignHint"/></summary>
        private string? _hint = WaitingHint;

        /// <summary>缓冲里现在有几个点（断言与诊断用；点数变化时会重绘）</summary>
        public int PointCount => _points.Count;

        static XYChartElement()
        {
            // 少了这一句，WPF 会按基类的键去找样式 → 本图元的模板永远查不到 →
            // 画布上拖上去只剩基类的空模板，而且不抛任何异常（ElementStyleKeyChecks 就是钉这条）。
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(XYChartElement),
                new FrameworkPropertyMetadata(typeof(XYChartElement)));
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            _surface = GetTemplateChild(PartSurface) as XYChartSurface;
            if (_surface != null)
                _surface.Owner = this;

            InvalidateVisual();
            _surface?.InvalidateVisual();
        }

        /// <summary>
        /// 每拍接法（照 <see cref="BarChartElement"/>）：先调基类（它在这里挂/摘闪烁节拍），
        /// 再自己挂/摘本图元的采样节拍。换上下文 = 换一轮运行，旧轨迹一并作废。
        /// </summary>
        protected override void OnRuntimeContextChanged(ScadaRuntimeContext? oldContext, ScadaRuntimeContext? newContext)
        {
            base.OnRuntimeContextChanged(oldContext, newContext);

            if (oldContext?.Beat is { } oldBeat)
                oldBeat.Beat -= OnBeat;

            if (newContext?.Beat is { } newBeat)
                newBeat.Beat += OnBeat;

            ClearPoints();
            SetHint(newContext == null ? DesignHint : WaitingHint);
        }

        /// <summary>一拍：读一对值 → 变了才追加 → 重绘。设计期没有节拍，走不到这里</summary>
        private void OnBeat(TimeSpan elapsed)
        {
            Sample();
            InvalidateVisual();
            _surface?.InvalidateVisual();
        }

        /// <summary>
        /// 配置变了：旧轨迹作废（换了变量或量程，上一轮的轨迹已经没有参照意义）。
        /// 缓冲清空后重新攒点，避免"改完配置还挂着旧曲线"的错觉。
        /// </summary>
        protected override void OnElementRefreshed()
        {
            base.OnElementRefreshed();

            ClearPoints();
            SetHint(RuntimeContext == null ? DesignHint : WaitingHint);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            InvalidateVisual();
            _surface?.InvalidateVisual();
        }

        /// <summary>
        /// 采一拍：解析 X / Y 两个变量，两者都拿到才追加一个点。
        ///
        /// <b>public 是给断言用的</b>（与 <c>DataGridElement.Sample</c> 同一口径）：去重与容量上限
        /// 全在"一拍"里，让断言直接推一拍，比在断言宿主里跑一个真定时器可靠得多。
        /// 生产路径只有节拍调它。
        /// </summary>
        public void Sample()
        {
            if (RuntimeContext?.Values is not { } values)
            {
                // 没接值源不是错误：纯静态预览、断言手搭的画布都是这个状态
                SetHint(NotConnectedHint);
                return;
            }

            var xName = XVariable?.Trim();
            var yName = YVariable?.Trim();

            if (string.IsNullOrEmpty(xName) || string.IsNullOrEmpty(yName))
            {
                SetHint(UnconfiguredHint);
                return;
            }

            if (!TryRead(values, xName!, out var x))
            {
                SetHint($"X 变量「{xName}」未解析");
                return;
            }

            if (!TryRead(values, yName!, out var y))
            {
                SetHint($"Y 变量「{yName}」未解析");
                return;
            }

            // NaN / Infinity 进了量程计算会把整张图的映射毁掉（min/max 全变 NaN），
            // 所以"不是有效数字"按"这一拍没拿到值"处理，并说清原因。
            if (!double.IsFinite(x) || !double.IsFinite(y))
            {
                SetHint("X / Y 值不是有效数字");
                return;
            }

            SetHint(null);
            Append(x, y);
        }

        /// <summary>按名字取一个数值；解析不到 / 转换失败返回 false（<b>不</b>用 0 冒充）</summary>
        private static bool TryRead(IScadaValueSource values, string variable, out double value)
        {
            value = 0;

            try
            {
                if (!values.TryResolve(Guid.Empty, variable, out var handle) || handle?.Value == null)
                    return false;

                value = Convert.ToDouble(handle.Value, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>追加一对值；与上一对完全相同则不追加（变量没变就不该长点）</summary>
        private void Append(double x, double y)
        {
            if (_hasLast && _lastX.Equals(x) && _lastY.Equals(y))
                return;

            _points.Enqueue((x, y));
            _hasLast = true;
            _lastX = x;
            _lastY = y;

            var capacity = Capacity;
            while (_points.Count > capacity)
                _points.Dequeue();

            InvalidateVisual();
            _surface?.InvalidateVisual();
        }

        private void ClearPoints()
        {
            _points.Clear();
            _hasLast = false;
            _lastX = 0;
            _lastY = 0;

            InvalidateVisual();
            _surface?.InvalidateVisual();
        }

        private void SetHint(string? hint)
        {
            if (string.Equals(_hint, hint, StringComparison.Ordinal))
                return;

            _hint = hint;
            InvalidateVisual();
            _surface?.InvalidateVisual();
        }

        /// <summary>容量：属性是整数，落成缓冲容量时夹到 [2, 5000]（与面板上的 Min/Max 同口径）</summary>
        private int Capacity
        {
            get
            {
                if (MaxPoints < 2) return 2;
                return MaxPoints > 5000 ? 5000 : MaxPoints;
            }
        }

        /// <summary>本图元的绘制入口（由模板里的 <see cref="XYChartSurface"/> 在自己的 OnRender 里调）</summary>
        internal void PaintChart(DrawingContext dc, FrameworkElement surface)
        {
            var width = surface.ActualWidth;
            var height = surface.ActualHeight;
            if (width < 32 || height < 32)
                return;

            // padRight 给得比 padLeft 宽：X 轴最后一个刻度数字是居中画在右边界上的，
            // 留窄了会被绘制面的 ClipToBounds 切掉半个字。
            const double padLeft = 46, padRight = 26, padTop = 14, padBottom = 26;
            var plotWidth = width - padLeft - padRight;
            var plotHeight = height - padTop - padBottom;
            if (plotWidth <= 8 || plotHeight <= 8)
                return;

            var xRange = Range(forX: true);
            var yRange = Range(forX: false);

            DrawFrame(dc, surface, padLeft, padTop, plotWidth, plotHeight, xRange, yRange);

            if (RuntimeContext == null)
            {
                // 设计期没有值源也没有节拍：画好轴与网格，明确说"这里现在没有数据"
                DrawHint(dc, surface, padLeft, padTop, plotWidth, plotHeight, DesignHint);
                return;
            }

            if (_points.Count == 0)
            {
                DrawHint(dc, surface, padLeft, padTop, plotWidth, plotHeight, _hint ?? WaitingHint);
                return;
            }

            DrawCurve(dc, padLeft, padTop, plotWidth, plotHeight, xRange, yRange);

            // 有轨迹但这一拍取不到值（变量被删 / 改名）：曲线留着，提示压在上面说清原因
            if (!string.IsNullOrEmpty(_hint))
                DrawHint(dc, surface, padLeft, padTop, plotWidth, plotHeight, _hint!);
        }

        /// <summary>
        /// 当前量程：自动 = 数据最小/最大留 5% 边距；固定 = 面板上的四个值。
        /// 数据全相等或量程写反时给一段最小跨度，避免除零把整张图压成一条线。
        /// </summary>
        private (double Min, double Max) Range(bool forX)
        {
            if (AutoScale)
            {
                if (_points.Count == 0)
                    return (0, 1);

                var min = double.MaxValue;
                var max = double.MinValue;

                foreach (var point in _points)
                {
                    var value = forX ? point.X : point.Y;
                    if (value < min) min = value;
                    if (value > max) max = value;
                }

                if (max - min < 1e-9)
                    return (min - 0.5, max + 0.5);

                var margin = (max - min) * 0.05;
                return (min - margin, max + margin);
            }

            var lower = forX ? XMin : YMin;
            var upper = forX ? XMax : YMax;

            if (!double.IsFinite(lower) || !double.IsFinite(upper) || upper - lower < 1e-9)
                return (lower, lower + 1);

            return (lower, upper);
        }

        private void DrawFrame(DrawingContext dc, Visual owner, double left, double top,
            double plotWidth, double plotHeight, (double Min, double Max) xRange, (double Min, double Max) yRange)
        {
            var gridPen = MakePen(GridBrush, 1);
            var axisPen = MakePen(AxisBrush, 1);

            if (ShowGrid)
            {
                for (var i = 1; i < GridDivisions; i++)
                {
                    var y = top + plotHeight * i / GridDivisions;
                    dc.DrawLine(gridPen, new Point(left, y), new Point(left + plotWidth, y));

                    var x = left + plotWidth * i / GridDivisions;
                    dc.DrawLine(gridPen, new Point(x, top), new Point(x, top + plotHeight));
                }
            }

            // 坐标轴：左 + 下两条实线（网格线比它浅一档，轴永远看得见）
            dc.DrawLine(axisPen, new Point(left, top), new Point(left, top + plotHeight));
            dc.DrawLine(axisPen, new Point(left, top + plotHeight), new Point(left + plotWidth, top + plotHeight));

            // 刻度数字：X 轴在下、Y 轴在左（都取 5 个等分点）
            for (var i = 0; i <= GridDivisions; i++)
            {
                var fraction = (double)i / GridDivisions;

                var xValue = xRange.Min + (xRange.Max - xRange.Min) * fraction;
                DrawText(dc, owner, Format(xValue), left + plotWidth * fraction, top + plotHeight + 6,
                    10, FaintBrush, center: true);

                var yValue = yRange.Max - (yRange.Max - yRange.Min) * fraction;
                DrawText(dc, owner, Format(yValue), left - 6, top + plotHeight * fraction - 7,
                    10, FaintBrush, right: true);
            }
        }

        private void DrawCurve(DrawingContext dc, double left, double top, double plotWidth, double plotHeight,
            (double Min, double Max) xRange, (double Min, double Max) yRange)
        {
            var points = new List<Point>(_points.Count);

            foreach (var (x, y) in _points)
            {
                points.Add(new Point(
                    Map(x, xRange, left, plotWidth),
                    Map(y, yRange, top + plotHeight, -plotHeight)));
            }

            var clip = new RectangleGeometry(new Rect(left, top, plotWidth, plotHeight));
            if (clip.CanFreeze)
                clip.Freeze();

            var lineBrush = LineColor ?? FallbackLineBrush;
            var linePen = MakePen(lineBrush, 1.6);

            dc.PushClip(clip);

            if (points.Count >= 2)
            {
                var geometry = new StreamGeometry();

                using (var ctx = geometry.Open())
                {
                    ctx.BeginFigure(points[0], isFilled: false, isClosed: false);
                    ctx.PolyLineTo(points.GetRange(1, points.Count - 1), isStroked: true, isSmoothJoin: true);
                }

                if (geometry.CanFreeze)
                    geometry.Freeze();

                dc.DrawGeometry(null, linePen, geometry);
            }

            var radius = Math.Max(0, Math.Min(20, PointSize)) / 2;
            if (radius > 0)
            {
                foreach (var point in points)
                    dc.DrawEllipse(lineBrush, null, point, radius, radius);
            }

            dc.Pop();
        }

        /// <summary>值 → 像素。夹到 ±1e5：量程固定而数据跑出去时，不把几何算成天文数字</summary>
        private static double Map(double value, (double Min, double Max) range, double origin, double span)
        {
            var fraction = (value - range.Min) / (range.Max - range.Min);
            var pixel = origin + fraction * span;

            if (double.IsNaN(pixel)) return origin;
            return pixel < -1e5 ? -1e5 : pixel > 1e5 ? 1e5 : pixel;
        }

        /// <summary>
        /// 一行提示（居中）。垫一块卡片底色：提示正好压在中间那条网格线上，不垫底色读不清
        /// （<c>Fill</c> 透明时等于没垫，没有副作用）。
        /// </summary>
        private void DrawHint(DrawingContext dc, Visual owner, double left, double top,
            double plotWidth, double plotHeight, string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            var formatted = FormatText(owner, text, 11.5, LabelBrush);
            var x = left + (plotWidth - formatted.Width) / 2;
            var y = top + (plotHeight - formatted.Height) / 2;

            dc.DrawRoundedRectangle(Fill, null,
                new Rect(x - 8, y - 3, formatted.Width + 16, formatted.Height + 6), 4, 4);

            dc.DrawText(formatted, new Point(x, y));
        }

        /// <summary>
        /// 一段文字。DPI 取绘制面的 <c>PixelsPerDip</c>——写死 1.0 时高分屏上刻度会发虚
        /// （与 <c>ChartPaint.Text</c> 同一条口径）。
        /// </summary>
        private static void DrawText(DrawingContext dc, Visual owner, string text, double x, double y,
            double size, Brush brush, bool center = false, bool right = false)
        {
            if (string.IsNullOrEmpty(text))
                return;

            var formatted = FormatText(owner, text, size, brush);

            var originX = center ? x - formatted.Width / 2 : right ? x - formatted.Width : x;
            dc.DrawText(formatted, new Point(originX, y));
        }

        private static FormattedText FormatText(Visual owner, string text, double size, Brush brush)
            => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, LabelTypeface, size, brush,
                VisualTreeHelper.GetDpi(owner).PixelsPerDip);

        /// <summary>刻度数字：常规量级用 0.###，极大/极小换科学计数（否则轴上全是 0）</summary>
        private static string Format(double value)
        {
            var magnitude = Math.Abs(value);
            if (magnitude >= 1e6 || (magnitude > 0 && magnitude < 1e-3))
                return value.ToString("0.##E+0", CultureInfo.InvariantCulture);

            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static Pen MakePen(Brush brush, double thickness)
        {
            var pen = new Pen(brush, thickness);
            if (pen.CanFreeze)
                pen.Freeze();
            return pen;
        }

        private static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>
    /// XY 曲线的绘制面（<see cref="FrameworkElement"/> + <c>OnRender</c>）。
    ///
    /// 为什么要单独一个元素：卡片底由模板里的 <c>Border</c> 画，而模板子元素画在控件自身
    /// <c>OnRender</c> 之上（实测：控件里铺满的矩形会被 Border 的不透明底色整个盖住）。
    /// 绘制面挂在 Border 里面，既保住了"卡片底走模板"的既有做法，又让曲线画在底之上。
    ///
    /// 与 <see cref="TrendSurface"/> 同一分工：本类只管"把 Owner 的轨迹画出来"，
    /// 采样、缓冲、配置全在 <see cref="XYChartElement"/> 里。
    /// </summary>
    public sealed class XYChartSurface : FrameworkElement
    {
        /// <summary>绘制谁的数据（模板应用时由 <see cref="XYChartElement.OnApplyTemplate"/> 注入）</summary>
        public XYChartElement? Owner { get; set; }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            Owner?.PaintChart(dc, this);
        }
    }
}
