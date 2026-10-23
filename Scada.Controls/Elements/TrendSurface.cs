using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 多曲线趋势的绘制面（FrameworkElement + OnRender）。
    ///
    /// 为什么不用 N 个 Polyline 控件：4 条 × 900 点的 Polyline 每次重绘都要重建几何与布局；
    /// 直接在 OnRender 里用 StreamGeometry 画，帧预算更可控，也不产生一堆命中测试对象。
    /// 绘制面只负责“把样本映射成像素”，采样、滚动窗口、属性都在 <see cref="MultiTrendElement"/> 里。
    /// </summary>
    public sealed class TrendSurface : FrameworkElement
    {
        public sealed class Series
        {
            public string Name { get; set; } = string.Empty;

            public Color Color { get; set; } = Colors.DeepSkyBlue;

            /// <summary>样本：(相对秒, 值)。按时间升序。</summary>
            public List<(double T, double V)> Samples { get; } = new();
        }

        private static readonly Typeface LabelTypeface = new("Segoe UI");

        private static readonly Brush GridBrush = new SolidColorBrush(Color.FromRgb(0x2A, 0x37, 0x45));
        private static readonly Brush AxisBrush = new SolidColorBrush(Color.FromRgb(0x7C, 0x8A, 0x99));
        private static readonly Brush CursorBrush = new SolidColorBrush(Color.FromRgb(0xA9, 0xB7, 0xC6));

        static TrendSurface()
        {
            GridBrush.Freeze();
            AxisBrush.Freeze();
            CursorBrush.Freeze();
        }

        public IReadOnlyList<Series> SeriesList { get; set; } = Array.Empty<Series>();

        public double TimeWindow { get; set; } = 60;

        public double Now { get; set; }

        public bool IndependentRanges { get; set; } = true;

        public bool ShowLegend { get; set; } = true;

        /// <summary>鼠标游标位置（像素）；&lt;0 表示不画。</summary>
        public double CursorX { get; set; } = -1;

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            var width = ActualWidth;
            var height = ActualHeight;
            if (width < 20 || height < 20)
                return;

            const double padL = 34;
            const double padR = 8;
            const double padT = 8;
            const double padB = 18;

            var plotW = Math.Max(1, width - padL - padR);
            var plotH = Math.Max(1, height - padT - padB);

            // 网格（4 条水平线）
            var gridPen = new Pen(GridBrush, 1);
            for (var i = 0; i <= 4; i++)
            {
                var y = padT + plotH * i / 4;
                dc.DrawLine(gridPen, new Point(padL, y), new Point(padL + plotW, y));
            }

            var window = Math.Max(1, TimeWindow);
            var tMax = Math.Max(1e-6, Now);
            var tMin = Math.Max(0, tMax - window);
            var span = Math.Max(1e-6, tMax - tMin);

            var visible = new List<(Series S, double Min, double Max)>();
            var sharedMin = double.MaxValue;
            var sharedMax = double.MinValue;

            foreach (var series in SeriesList)
            {
                if (series.Samples.Count == 0)
                    continue;

                var min = double.MaxValue;
                var max = double.MinValue;
                foreach (var sample in series.Samples)
                {
                    if (sample.T < tMin)
                        continue;
                    min = Math.Min(min, sample.V);
                    max = Math.Max(max, sample.V);
                }

                if (min > max)
                    continue;

                if (Math.Abs(max - min) < 1e-9)
                {
                    min -= 1;
                    max += 1;
                }
                else
                {
                    var pad = (max - min) * 0.05;
                    min -= pad;
                    max += pad;
                }

                visible.Add((series, min, max));
                sharedMin = Math.Min(sharedMin, min);
                sharedMax = Math.Max(sharedMax, max);
            }

            if (sharedMin > sharedMax)
            {
                sharedMin = 0;
                sharedMax = 1;
            }

            foreach (var (series, ownMin, ownMax) in visible)
            {
                var min = IndependentRanges ? ownMin : sharedMin;
                var max = IndependentRanges ? ownMax : sharedMax;
                var range = Math.Max(1e-9, max - min);

                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    var started = false;
                    foreach (var sample in series.Samples)
                    {
                        if (sample.T < tMin)
                            continue;

                        var x = padL + (sample.T - tMin) / span * plotW;
                        var y = padT + (1 - (sample.V - min) / range) * plotH;

                        if (!started)
                        {
                            ctx.BeginFigure(new Point(x, y), false, false);
                            started = true;
                        }
                        else
                        {
                            ctx.LineTo(new Point(x, y), true, false);
                        }
                    }
                }

                geometry.Freeze();
                dc.DrawGeometry(null, new Pen(new SolidColorBrush(series.Color), 1.8), geometry);
            }

            // 时间轴标签（左端 / 右端）
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            DrawText(dc, $"−{window:0}s", padL, height - 14, 9.5, AxisBrush, dpi);
            DrawText(dc, "now", padL + plotW - 22, height - 14, 9.5, AxisBrush, dpi);

            if (ShowLegend)
                DrawLegend(dc, padL, padT + 2, dpi);

            if (CursorX >= padL && CursorX <= padL + plotW && visible.Count > 0)
                DrawCursor(dc, padL, padT, plotW, plotH, tMin, tMax, dpi);
        }

        private void DrawLegend(DrawingContext dc, double x, double y, double dpi)
        {
            foreach (var series in SeriesList)
            {
                if (series.Samples.Count == 0)
                    continue;

                var pen = new Pen(new SolidColorBrush(series.Color), 2);
                dc.DrawLine(pen, new Point(x, y + 6), new Point(x + 12, y + 6));

                var text = MakeText(series.Name, 10, AxisBrush, dpi);
                dc.DrawText(text, new Point(x + 16, y));

                x += 16 + text.Width + 14;
                if (x > ActualWidth - 40)
                    break;
            }
        }

        private void DrawCursor(
            DrawingContext dc,
            double padL,
            double padT,
            double plotW,
            double plotH,
            double tMin,
            double tMax,
            double dpi)
        {
            var pen = new Pen(CursorBrush, 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
            dc.DrawLine(pen, new Point(CursorX, padT), new Point(CursorX, padT + plotH));

            var t = tMin + (CursorX - padL) / Math.Max(1e-9, plotW) * (tMax - tMin);
            var parts = new List<string>();

            foreach (var series in SeriesList)
            {
                if (series.Samples.Count == 0)
                    continue;

                var nearest = series.Samples[0];
                foreach (var sample in series.Samples)
                {
                    if (Math.Abs(sample.T - t) < Math.Abs(nearest.T - t))
                        nearest = sample;
                }

                parts.Add($"{series.Name} {nearest.V:0.##}");
            }

            if (parts.Count == 0)
                return;

            var text = MakeText(string.Join(" · ", parts), 9.5, CursorBrush, dpi);
            var x = Math.Min(CursorX + 6, ActualWidth - text.Width - 4);
            dc.DrawText(text, new Point(Math.Max(padL, x), padT));
        }

        private static FormattedText MakeText(string text, double size, Brush brush, double dpi)
            => new(
                text ?? string.Empty,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                size,
                brush,
                dpi);

        private static void DrawText(DrawingContext dc, string text, double x, double y, double size, Brush brush, double dpi)
            => dc.DrawText(MakeText(text, size, brush, dpi), new Point(x, y));
    }
}
