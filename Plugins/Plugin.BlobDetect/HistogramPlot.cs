using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Plugin.BlobDetect
{
    /// <summary>
    /// 灰度直方图矢量绘制控件。
    ///
    /// 【为什么不用 Image 控件显示一张渲染好的位图】
    /// 直方图要随窗口缩放、还要叠"固定阈值上下限"的标记，如果先渲染成位图塞进 Image，
    /// 一缩放就糊、标记也得跟着重渲染。这里用 <see cref="FrameworkElement.OnRender"/> + DrawingContext
    /// 直接画矢量几何（等价于 XAML 里的 Path/Polyline，但缩放时能在 OnRender 里按实际尺寸重算坐标），
    /// 窗口拉大拉小都清晰，阈值标记也永远对齐。
    ///
    /// 只做绘制、不持任何业务状态：数据来自绑定（<see cref="GrayHistogram"/>），
    /// 标记位置来自绑定（当前固定阈值），本控件不认识插件。
    /// </summary>
    public sealed class HistogramPlot : FrameworkElement
    {
        #region 依赖属性（绑定入口）

        /// <summary>直方图数据（null = 暂无，画占位提示）</summary>
        public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
            nameof(Data), typeof(GrayHistogram), typeof(HistogramPlot),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>是否画固定阈值标记（只有"固定阈值"方式下才有意义）</summary>
        public static readonly DependencyProperty ShowThresholdMarkersProperty = DependencyProperty.Register(
            nameof(ShowThresholdMarkers), typeof(bool), typeof(HistogramPlot),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>固定阈值下限（灰度值）</summary>
        public static readonly DependencyProperty ThresholdLowProperty = DependencyProperty.Register(
            nameof(ThresholdLow), typeof(double), typeof(HistogramPlot),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>固定阈值上限（灰度值）</summary>
        public static readonly DependencyProperty ThresholdHighProperty = DependencyProperty.Register(
            nameof(ThresholdHigh), typeof(double), typeof(HistogramPlot),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

        public GrayHistogram? Data
        {
            get => (GrayHistogram?)GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        public bool ShowThresholdMarkers
        {
            get => (bool)GetValue(ShowThresholdMarkersProperty);
            set => SetValue(ShowThresholdMarkersProperty, value);
        }

        public double ThresholdLow
        {
            get => (double)GetValue(ThresholdLowProperty);
            set => SetValue(ThresholdLowProperty, value);
        }

        public double ThresholdHigh
        {
            get => (double)GetValue(ThresholdHighProperty);
            set => SetValue(ThresholdHighProperty, value);
        }

        #endregion

        #region 画笔（主题令牌优先，取不到再退回内置值）

        // 颜色全部来自 UI 库的 Plugin* 令牌（Themes/PluginConfigColors.xaml），换主题改那一本即可。
        //
        // 为什么必须带回退值：本控件可能被实例化在"没有 Application / 没合并 UI 资源"的环境里
        // （冒烟断言、离线跑流程）。TryFindResource 那时拿不到东西——
        // 主题缺失可以丑，但绝不能让直方图渲染不出来，所以每个键都配一份内置回退值。
        private static readonly Brush FallbackFillBrush = Freeze(new SolidColorBrush(Color.FromArgb(72, 0x3A, 0x7B, 0xD5)));
        private static readonly Brush FallbackStrokeBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0xBF)));
        private static readonly Brush FallbackBandBrush = Freeze(new SolidColorBrush(Color.FromArgb(46, 0xE6, 0xA2, 0x3C)));
        private static readonly Brush FallbackMarkerBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE6, 0xA2, 0x3C)));
        private static readonly Brush FallbackGridBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)));
        private static readonly Brush FallbackPlaceholderBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)));
        private static readonly Typeface FallbackTypeface = new(new FontFamily("Microsoft YaHei"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        // 解析结果缓存在实例上：OnRender 会频繁触发，别每次都去查资源树
        private bool _resourcesResolved;
        private Brush? _fillBrush;
        private Pen? _strokePen;
        private Brush? _bandBrush;
        private Pen? _markerPen;
        private Pen? _borderPen;
        private Brush? _placeholderBrush;
        private Typeface? _placeholderTypeface;

        private void EnsureResources()
        {
            if (_resourcesResolved) return;
            _resourcesResolved = true;

            _fillBrush = LookupBrush("PluginChartFillBrush", FallbackFillBrush);
            _strokePen = LookupPen("PluginChartStrokeBrush", FallbackStrokeBrush, "PluginChartStrokeThickness", 1);
            _bandBrush = LookupBrush("PluginChartBandBrush", FallbackBandBrush);
            _markerPen = LookupPen("PluginChartMarkerBrush", FallbackMarkerBrush, "PluginChartMarkerThickness", 1.2);
            _borderPen = LookupPen("PluginChartGridBrush", FallbackGridBrush, "PluginChartStrokeThickness", 1);
            _placeholderBrush = LookupBrush("PluginChartPlaceholderBrush", FallbackPlaceholderBrush);
            _placeholderTypeface = TryFindResource("PluginFontFamily") is FontFamily ff
                ? new Typeface(ff, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal)
                : FallbackTypeface;
        }

        private Brush LookupBrush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

        private Pen LookupPen(string brushKey, Brush fallbackBrush, string thicknessKey, double fallbackThickness)
        {
            var brush = LookupBrush(brushKey, fallbackBrush);
            double thickness = TryFindResource(thicknessKey) is double d ? d : fallbackThickness;
            return Freeze(new Pen(brush, thickness));
        }

        private static T Freeze<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }

        #endregion

        protected override void OnRender(DrawingContext dc)
        {
            double width = ActualWidth, height = ActualHeight;
            if (width <= 1 || height <= 1) return;

            EnsureResources();   // 主题令牌优先，取不到用内置回退值

            var data = Data;
            // 注意判据只看 Bins：单一灰度图（BinMin == BinMax）也是"有数据"的——
            // GrayHistogram 对极窄范围会兜到 8 档，此时应画出那根独峰，而不是显示"暂无数据"
            if (data == null || data.Bins.Length == 0)
            {
                DrawPlaceholder(dc, "（暂无直方图数据）", height);
                return;
            }

            // 留 1px 内边距，让外框线不被裁掉
            const double pad = 1;
            double plotW = Math.Max(1, width - 2 * pad);
            double plotH = Math.Max(1, height - 2 * pad);
            double bottom = pad + plotH;

            // ── 1. 直方图轮廓：折线 + 底部闭合的矢量化几何 ──
            // 每档取档位中心作为横坐标；纵坐标 = (1 - 归一化频数)，因为 WPF 的 y 轴向下。
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(pad, bottom), isFilled: true, isClosed: false);
                int n = data.Bins.Length;
                for (int i = 0; i < n; i++)
                {
                    double x = n == 1 ? pad + plotW / 2 : pad + (i + 0.5) / n * plotW;
                    double v = Math.Clamp(data.Bins[i], 0, 1);
                    double y = pad + (1 - v) * plotH;
                    ctx.LineTo(new Point(x, y), isStroked: true, isSmoothJoin: false);
                }
                ctx.LineTo(new Point(pad + plotW, bottom), isStroked: true, isSmoothJoin: false);
            }
            geometry.Freeze();
            dc.DrawGeometry(_fillBrush, _strokePen, geometry);

            // ── 2. 固定阈值标记：两条竖线 + 中间半透明区间填充 ──
            // 只有"固定阈值"方式下才画（自动/动态阈值没有固定阈值参数，画了会误导）
            if (ShowThresholdMarkers)
            {
                double xLow = GrayToX(ThresholdLow, data, pad, plotW);
                double xHigh = GrayToX(ThresholdHigh, data, pad, plotW);
                double left = Math.Min(xLow, xHigh);
                double right = Math.Max(xLow, xHigh);

                if (right > left)
                    dc.DrawRectangle(_bandBrush, null, new Rect(left, pad, right - left, plotH));

                dc.DrawLine(_markerPen, new Point(xLow, pad), new Point(xLow, bottom));
                dc.DrawLine(_markerPen, new Point(xHigh, pad), new Point(xHigh, bottom));
            }

            // ── 3. 外框 ──
            dc.DrawRectangle(null, _borderPen, new Rect(pad + 0.5, pad + 0.5, plotW - 1, plotH - 1));
        }

        /// <summary>
        /// 灰度值 → 控件横坐标。灰度范围就是横轴范围，超出范围的阈值夹到两端
        /// （用户把阈值设在图像灰度范围之外时，标记停在边界上比画到控件外面强）。
        /// </summary>
        private static double GrayToX(double gray, GrayHistogram data, double pad, double plotW)
        {
            double span = data.BinMax - data.BinMin;
            // 单一灰度图（span == 0）时不能做除法：阈值等于该灰度就画在中间，否则按大小落在两端
            if (span <= 0)
                return Math.Abs(gray - data.BinMin) < 1e-9 ? pad + plotW / 2 : (gray < data.BinMin ? pad : pad + plotW);

            double t = (gray - data.BinMin) / span;
            return Math.Clamp(pad + t * plotW, pad, pad + plotW);
        }

        private void DrawPlaceholder(DrawingContext dc, string text, double height)
        {
            EnsureResources();

            // 未接入可视树时个别环境取 DPI 会抛，退回 1.0（占位文字不涉及精度，安全优先）
            double pixelsPerDip;
            try { pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip; }
            catch { pixelsPerDip = 1.0; }

            var formatted = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                _placeholderTypeface ?? FallbackTypeface,
                TryFindResource("PluginHintFontSize") is double fs ? fs : 11,
                _placeholderBrush,
                pixelsPerDip);

            dc.DrawText(formatted, new Point(8, Math.Max(0, (height - formatted.Height) / 2)));
        }
    }
}
