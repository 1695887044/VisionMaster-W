using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 多曲线趋势图元（TypeKey = <c>Hmi.MultiTrend</c>）：最多 4 条曲线同屏。
    ///
    /// 为什么是 4 条而不是 8 条：现场最常见的组合是 PV / SP / OP + 一路辅助量；
    /// 8 条会让属性面板长出 24 行（值/名字/颜色），而画面里 8 条线也读不清。
    /// 每条曲线的值都是独立的可绑属性（<see cref="Value1"/>…<see cref="Value4"/>），
    /// 直接复用运行态数据泵，不需要另造“多值通道”。
    ///
    /// 采样：每次属性值变化追加一个点（时间取全画面统一节拍）；绘制走
    /// <see cref="TrendSurface"/>（StreamGeometry，不为每条线造控件）。
    /// 设计期没有运行节拍时用系统时钟，保证编辑器里也能看到滚动。
    /// </summary>
    [TemplatePart(Name = PartChart, Type = typeof(TrendSurface))]
    public partial class MultiTrendElement : ScadaElementBase
    {
        public const string PartChart = "TrendChart";

        public const string RangeShared = "Shared";
        public const string RangeIndependent = "Independent";

        [ScadaProperty(Key = "Value1", DisplayName = "曲线 1 值", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "0", IsBindable = true,
                       Description = "绑一个数值变量；每来一个新值追加一个采样点")]
        public partial double Value1 { get; set; }

        [ScadaProperty(Key = "Value2", DisplayName = "曲线 2 值", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "0", IsBindable = true,
                       Description = "第二条曲线（留空不显示）；典型 PV/SP/OP 的第二路")]
        public partial double Value2 { get; set; }

        [ScadaProperty(Key = "Value3", DisplayName = "曲线 3 值", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "0", IsBindable = true,
                       Description = "第三条曲线（留空不显示）")]
        public partial double Value3 { get; set; }

        [ScadaProperty(Key = "Value4", DisplayName = "曲线 4 值", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "0", IsBindable = true,
                       Description = "第四条曲线（留空不显示）")]
        public partial double Value4 { get; set; }

        [ScadaProperty(Key = "Series1Name", DisplayName = "曲线 1 名称", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "温度")]
        public partial string? Series1Name { get; set; }

        [ScadaProperty(Key = "Series2Name", DisplayName = "曲线 2 名称", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "压力")]
        public partial string? Series2Name { get; set; }

        [ScadaProperty(Key = "Series3Name", DisplayName = "曲线 3 名称", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "转速")]
        public partial string? Series3Name { get; set; }

        [ScadaProperty(Key = "Series4Name", DisplayName = "曲线 4 名称", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "辅助量")]
        public partial string? Series4Name { get; set; }

        [ScadaProperty(Key = "Series1Color", DisplayName = "曲线 1 颜色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "#FF38BDF8")]
        public partial Brush? Series1Color { get; set; }

        [ScadaProperty(Key = "Series2Color", DisplayName = "曲线 2 颜色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "#FF22C55E")]
        public partial Brush? Series2Color { get; set; }

        [ScadaProperty(Key = "Series3Color", DisplayName = "曲线 3 颜色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "#FFF59E0B")]
        public partial Brush? Series3Color { get; set; }

        [ScadaProperty(Key = "Series4Color", DisplayName = "曲线 4 颜色", Kind = ElementPropertyKind.Color,
                       Group = ScadaPropertyGroups.Appearance, DefaultValue = "#FFA855F7")]
        public partial Brush? Series4Color { get; set; }

        [ScadaProperty(Key = "TimeWindow", DisplayName = "时间窗", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "60", Min = 2, Max = 3600,
                       Description = "横轴显示最近多少秒；滚动窗口，不补历史")]
        public partial double TimeWindow { get; set; }

        [ScadaProperty(Key = "RangeMode", DisplayName = "量程模式", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.Data, DefaultValue = RangeIndependent,
                       Choices = new[] { RangeIndependent, RangeShared },
                       Description = "独立 = 每条曲线按自己的最小/最大归一化（混单位用）；共享 = 同一量程（同单位对比用）")]
        public partial string? RangeMode { get; set; }

        [ScadaProperty(Key = "ShowLegend", DisplayName = "显示图例", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "True")]
        public partial bool ShowLegend { get; set; }

        private TrendSurface? _surface;
        private readonly TrendSurface.Series[] _series =
        {
            new TrendSurface.Series(),
            new TrendSurface.Series(),
            new TrendSurface.Series(),
            new TrendSurface.Series(),
        };
        private TimeSpan _lastElapsed = TimeSpan.Zero;

        /// <summary>当前缓冲的采样点数（断言/诊断用；4 条曲线求和）</summary>
        public int SampleCount
        {
            get
            {
                var count = 0;
                foreach (var series in _series)
                    count += series.Samples.Count;
                return count;
            }
        }

        static MultiTrendElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(MultiTrendElement),
                new FrameworkPropertyMetadata(typeof(MultiTrendElement)));
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            _surface = GetTemplateChild(PartChart) as TrendSurface;
            if (_surface == null)
                return;

            _surface.SeriesList = _series;
            _surface.MouseMove += OnSurfaceMouseMove;
            _surface.MouseLeave += OnSurfaceMouseLeave;

            ApplyConfiguration();
            _surface.InvalidateVisual();
        }

        protected override void OnRuntimeContextChanged(ScadaRuntimeContext? oldContext, ScadaRuntimeContext? newContext)
        {
            if (oldContext != null)
                oldContext.Beat.Beat -= OnBeat;

            if (newContext != null)
                newContext.Beat.Beat += OnBeat;

            _lastElapsed = TimeSpan.Zero;
        }

        protected override void OnElementRefreshed()
        {
            base.OnElementRefreshed();
            ApplyConfiguration();
            _surface?.InvalidateVisual();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            _surface?.InvalidateVisual();
        }

        private void ApplyConfiguration()
        {
            if (_surface == null)
                return;

            _series[0].Name = Series1Name ?? "温度";
            _series[1].Name = Series2Name ?? "压力";
            _series[2].Name = Series3Name ?? "转速";
            _series[3].Name = Series4Name ?? "辅助量";

            _series[0].Color = ToColor(Series1Color, Color.FromRgb(0x38, 0xBD, 0xF8));
            _series[1].Color = ToColor(Series2Color, Color.FromRgb(0x22, 0xC5, 0x5E));
            _series[2].Color = ToColor(Series3Color, Color.FromRgb(0xF5, 0x9E, 0x0B));
            _series[3].Color = ToColor(Series4Color, Color.FromRgb(0xA8, 0x55, 0xF7));

            _surface.TimeWindow = Math.Max(2, TimeWindow);
            _surface.IndependentRanges = !string.Equals(RangeMode, RangeShared, StringComparison.OrdinalIgnoreCase);
            _surface.ShowLegend = ShowLegend;
        }

        private void OnBeat(TimeSpan elapsed)
        {
            if (_surface == null)
                return;

            _lastElapsed = elapsed;
            _surface.Now = elapsed.TotalSeconds;

            Trim();
            _surface.InvalidateVisual();
        }

        private void OnSurfaceMouseMove(object sender, MouseEventArgs e)
        {
            if (_surface == null)
                return;

            _surface.CursorX = e.GetPosition(_surface).X;
            _surface.InvalidateVisual();
        }

        private void OnSurfaceMouseLeave(object sender, MouseEventArgs e)
        {
            if (_surface == null)
                return;

            _surface.CursorX = -1;
            _surface.InvalidateVisual();
        }

        partial void OnValue1PropertyChanged() => AppendSample(0, Value1);
        partial void OnValue2PropertyChanged() => AppendSample(1, Value2);
        partial void OnValue3PropertyChanged() => AppendSample(2, Value3);
        partial void OnValue4PropertyChanged() => AppendSample(3, Value4);
        partial void OnSeries1NamePropertyChanged() => ApplyConfiguration();
        partial void OnSeries2NamePropertyChanged() => ApplyConfiguration();
        partial void OnSeries3NamePropertyChanged() => ApplyConfiguration();
        partial void OnSeries4NamePropertyChanged() => ApplyConfiguration();
        partial void OnSeries1ColorPropertyChanged() => ApplyConfiguration();
        partial void OnSeries2ColorPropertyChanged() => ApplyConfiguration();
        partial void OnSeries3ColorPropertyChanged() => ApplyConfiguration();
        partial void OnSeries4ColorPropertyChanged() => ApplyConfiguration();
        partial void OnTimeWindowPropertyChanged() => ApplyConfiguration();
        partial void OnRangeModePropertyChanged() => ApplyConfiguration();
        partial void OnShowLegendPropertyChanged() => ApplyConfiguration();

        private void AppendSample(int index, double value)
        {
            if (_surface == null)
                return;

            var now = _lastElapsed.TotalSeconds;
            if (now <= 0)
                now = Environment.TickCount64 / 1000d;

            _series[index].Samples.Add((now, value));
            _surface.Now = now;
            Trim();
            _surface.InvalidateVisual();
        }

        private void Trim()
        {
            var cutoff = _surface!.Now - Math.Max(2, TimeWindow) * 2;
            foreach (var series in _series)
            {
                var remove = 0;
                while (remove < series.Samples.Count && series.Samples[remove].T < cutoff)
                    remove++;

                if (remove > 0)
                    series.Samples.RemoveRange(0, remove);
            }
        }

        private static Color ToColor(Brush? brush, Color fallback)
            => brush is SolidColorBrush solid ? solid.Color : fallback;
    }
}
