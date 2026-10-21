using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScottPlot.Plottable;

namespace VM.Charts
{
    /// <summary>十字光标坐标(经 <see cref="ChartView.CursorMoved"/> 抛给 VM 的参数)。</summary>
    public class ChartCursorInfo
    {
        public double X { get; set; }
        public double Y { get; set; }

        /// <summary>系列设置了 StartTime 时给出绝对时刻文本,否则为 null。</summary>
        public string TimeText { get; set; }
    }

    /// <summary>视图范围(轴状态的双向绑定载体)。null 表示交回控件自动缩放。</summary>
    public class ChartViewLimits
    {
        public double XMin { get; set; }
        public double XMax { get; set; }
        public double YMin { get; set; }
        public double YMax { get; set; }
    }

    /// <summary>
    /// ScottPlot 的 MVVM 封装(ScottPlot 4.1.x)。
    ///
    /// 【边界】VM 只面向 <see cref="ISeriesVm"/>(纯数据);所有 ScottPlot 类型
    /// 都关在本类里,升级 ScottPlot 5 时只重写本类。
    /// 【渲染】全局调度器统一 30ms 轮询,有脏才 Render;采集线程只写 Buffer;
    /// 交互(拖拽/滚轮)中自动降质渲染 + 暂停跟随,松手恢复。
    /// 【主题】颜色优先取 Fluent 令牌(见 ChartTheme),换肤后调 RefreshTheme()。
    /// 【诊断】慢帧(>50ms)走 DiagnosticsLog;连续渲染异常降级为提示而不是崩窗。
    /// </summary>
    public class ChartView : UserControl
    {
        private ScottPlot.WpfPlot _plot;
        private Crosshair _crosshair;
        private readonly Dictionary<string, SignalPlot> _signals = new Dictionary<string, SignalPlot>();
        private readonly Dictionary<string, ISeriesVm> _seriesByKey = new Dictionary<string, ISeriesVm>();
        private readonly Dictionary<string, IPlottable> _annotations = new Dictionary<string, IPlottable>();
        private readonly Dictionary<string, IChartAnnotation> _annotationsByKey = new Dictionary<string, IChartAnnotation>();
        private int _paletteIndex;
        private bool _plottablesDirty = true;
        private int _lastVersion;
        private DateTime _lastCursorRaise = DateTime.MinValue;

        private bool _mouseDown;
        private DateTime _lastInteractionUtc = DateTime.MinValue;
        private bool _userFollow = true;

        private TextBlock _chipText;
        private int _renderFailures;
        private DateTime _lastSlowFrameLog = DateTime.MinValue;
        private DateTime? _cursorTimeBase; // 系列里第一个非空 StartTime

        /// <summary>诊断输出钩子(慢帧/渲染异常)。宿主可重定向;默认走 Trace 警告。</summary>
        public static Action<string> DiagnosticsLog = message =>
            System.Diagnostics.Trace.TraceWarning(message);

        #region 依赖属性

        public static readonly DependencyProperty SeriesSourceProperty =
            DependencyProperty.Register("SeriesSource", typeof(IEnumerable), typeof(ChartView),
                new PropertyMetadata(null, OnSeriesSourceChanged));

        public IEnumerable SeriesSource
        {
            get { return (IEnumerable)GetValue(SeriesSourceProperty); }
            set { SetValue(SeriesSourceProperty, value); }
        }

        /// <summary>阈值线/事件标记等附加元素</summary>
        public static readonly DependencyProperty AnnotationsSourceProperty =
            DependencyProperty.Register("AnnotationsSource", typeof(IEnumerable), typeof(ChartView),
                new PropertyMetadata(null, OnAnnotationsSourceChanged));

        public IEnumerable AnnotationsSource
        {
            get { return (IEnumerable)GetValue(AnnotationsSourceProperty); }
            set { SetValue(AnnotationsSourceProperty, value); }
        }

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(ChartView),
                new PropertyMetadata(null, OnLabelChanged));

        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }

        public static readonly DependencyProperty XLabelProperty =
            DependencyProperty.Register("XLabel", typeof(string), typeof(ChartView),
                new PropertyMetadata(null, OnLabelChanged));

        public string XLabel
        {
            get { return (string)GetValue(XLabelProperty); }
            set { SetValue(XLabelProperty, value); }
        }

        public static readonly DependencyProperty YLabelProperty =
            DependencyProperty.Register("YLabel", typeof(string), typeof(ChartView),
                new PropertyMetadata(null, OnLabelChanged));

        public string YLabel
        {
            get { return (string)GetValue(YLabelProperty); }
            set { SetValue(YLabelProperty, value); }
        }

        public static readonly DependencyProperty Y2LabelProperty =
            DependencyProperty.Register("Y2Label", typeof(string), typeof(ChartView),
                new PropertyMetadata(null, OnLabelChanged));

        public string Y2Label
        {
            get { return (string)GetValue(Y2LabelProperty); }
            set { SetValue(Y2LabelProperty, value); }
        }

        /// <summary>跟随数据头滚动(示波器模式);静态图设 false,仅数据变化时重绘</summary>
        public static readonly DependencyProperty AutoScrollProperty =
            DependencyProperty.Register("AutoScroll", typeof(bool), typeof(ChartView),
                new PropertyMetadata(false));

        public bool AutoScroll
        {
            get { return (bool)GetValue(AutoScrollProperty); }
            set { SetValue(AutoScrollProperty, value); }
        }

        /// <summary>AutoScroll 时可见窗口长度(秒)</summary>
        public static readonly DependencyProperty AutoScrollSecondsProperty =
            DependencyProperty.Register("AutoScrollSeconds", typeof(double), typeof(ChartView),
                new PropertyMetadata(5d));

        public double AutoScrollSeconds
        {
            get { return (double)GetValue(AutoScrollSecondsProperty); }
            set { SetValue(AutoScrollSecondsProperty, value); }
        }

        /// <summary>是否显示图例</summary>
        public static readonly DependencyProperty ShowLegendProperty =
            DependencyProperty.Register("ShowLegend", typeof(bool), typeof(ChartView),
                new PropertyMetadata(false, OnLabelChanged));

        public bool ShowLegend
        {
            get { return (bool)GetValue(ShowLegendProperty); }
            set { SetValue(ShowLegendProperty, value); }
        }

        /// <summary>轴范围,双向绑定。设 null = 自动缩放;用户缩放/拖拽后控件把新范围推回。</summary>
        public static readonly DependencyProperty ViewLimitsProperty =
            DependencyProperty.Register("ViewLimits", typeof(ChartViewLimits), typeof(ChartView),
                new PropertyMetadata(null, OnViewLimitsChanged));

        public ChartViewLimits ViewLimits
        {
            get { return (ChartViewLimits)GetValue(ViewLimitsProperty); }
            set { SetValue(ViewLimitsProperty, value); }
        }

        /// <summary>十字光标坐标变化时执行(参数 ChartCursorInfo)</summary>
        public static readonly DependencyProperty CursorMovedProperty =
            DependencyProperty.Register("CursorMoved", typeof(ICommand), typeof(ChartView),
                new PropertyMetadata(null));

        public ICommand CursorMoved
        {
            get { return (ICommand)GetValue(CursorMovedProperty); }
            set { SetValue(CursorMovedProperty, value); }
        }

        /// <summary>跨图十字光标同步组:同组图竖线位置对齐(多通道对比场景)。</summary>
        public static readonly DependencyProperty SyncGroupProperty =
            DependencyProperty.Register("SyncGroup", typeof(string), typeof(ChartView),
                new PropertyMetadata(null, OnSyncGroupChanged));

        public string SyncGroup
        {
            get { return (string)GetValue(SyncGroupProperty); }
            set { SetValue(SyncGroupProperty, value); }
        }

        #endregion

        public ChartView()
        {
            ChartTheme.Reload();

            var grid = new Grid();
            _plot = new ScottPlot.WpfPlot();
            grid.Children.Add(_plot);

            // 跟随/暂停状态 chip(右下角,可点击切换)
            _chipText = new TextBlock { FontSize = 11 };
            var chip = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 24, 34),
                Padding = new Thickness(8, 3, 8, 3),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(0xD9, 0xF5, 0xF5, 0xF5)),
                Child = _chipText,
                Cursor = Cursors.Hand,
            };
            chip.MouseLeftButtonUp += delegate
            {
                _userFollow = !_userFollow;
                UpdateChip();
            };
            grid.Children.Add(chip);

            Content = grid;

            _crosshair = _plot.Plot.AddCrosshair(0, 0);
            _crosshair.IsVisible = false;
            _crosshair.Color = System.Drawing.Color.Gray;
            _crosshair.LineWidth = 1;
            _crosshair.LineStyle = ScottPlot.LineStyle.Dot;
            _crosshair.HorizontalLine.PositionLabelOppositeAxis = true;

            _plot.MouseMove += OnPlotMouseMove;
            _plot.MouseLeave += OnPlotMouseLeave;
            _plot.MouseDoubleClick += OnPlotMouseDoubleClick;
            _plot.MouseLeftButtonUp += OnPlotMouseLeftUp;
            _plot.MouseLeftButtonDown += delegate { _mouseDown = true; };
            _plot.MouseWheel += OnPlotMouseWheel;

            Loaded += delegate
            {
                ChartTheme.Reload();
                ApplyTheme();
                AttachSeriesEvents();
                ChartRenderScheduler.Register(this);
                JoinSyncGroup();
            };
            Unloaded += delegate
            {
                ChartRenderScheduler.Unregister(this);
                DetachSeriesEvents();
            };
        }

        /// <summary>把当前图导出为 PNG(VM 可直接调用,不需要窗口参与)。</summary>
        public void SavePng(string filePath, int width = 800, int height = 600)
        {
            _plot.Plot.SaveFig(filePath, width, height);
        }

        /// <summary>把十字光标放到数据坐标处(程序化标注/自动化测试用),并抛出 CursorMoved、同步同组图。</summary>
        public void PlaceCursor(double x, double y)
        {
            _crosshair.X = x;
            _crosshair.Y = y;
            _crosshair.IsVisible = true;
            RaiseCursorMoved(x, y);
            BroadcastCursor(x);
        }

        /// <summary>主题(或 Fluent 令牌)变化后重刷图表配色。</summary>
        public void RefreshTheme()
        {
            ChartTheme.Reload();
            ApplyTheme();
            _plot.Refresh();
        }

        #region 鼠标交互

        private void OnPlotMouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(_plot);
            // 鼠标事件给的是 DIP,ScottPlot 位图是设备像素:高 DPI 屏(125%/150%)
            // 不乘缩放十字光标就会错位
            double sx = 1, sy = 1;
            var source = PresentationSource.FromVisual(_plot);
            if (source != null && source.CompositionTarget != null)
            {
                sx = source.CompositionTarget.TransformToDevice.M11;
                sy = source.CompositionTarget.TransformToDevice.M22;
            }
            var coord = _plot.Plot.GetCoordinate((float)(pos.X * sx), (float)(pos.Y * sy));
            // 首次渲染前/绘图区外 GetCoordinate 会返回 NaN,必须丢弃,
            // 否则 NaN 会顺着 PlaceCursor 一路广播给同组图
            if (!IsFinite(coord.x) || !IsFinite(coord.y)) return;
            PlaceCursor(coord.x, coord.y);
        }

        private static bool IsFinite(double v)
        {
            return !double.IsNaN(v) && !double.IsInfinity(v);
        }

        private void OnPlotMouseLeave(object sender, MouseEventArgs e)
        {
            _crosshair.IsVisible = false;
            _mouseDown = false;
        }

        private void OnPlotMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // 滚轮缩放不经过 MouseUp,也要推回 ViewLimits 并暂停跟随
            _lastInteractionUtc = DateTime.UtcNow;
            PushLimits();
        }

        private void OnPlotMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ViewLimits = null;   // 交回自动缩放
            _userFollow = true;  // 双击 = 恢复跟随
            UpdateChip();
            _plot.Plot.AxisAuto();
            _plot.Refresh();
        }

        private void OnPlotMouseLeftUp(object sender, MouseButtonEventArgs e)
        {
            _mouseDown = false;
            PushLimits();
        }

        private static bool NearlyEqual(double a, double b)
        {
            return Math.Abs(a - b) <= Math.Max(Math.Abs(a), Math.Abs(b)) * 1e-6;
        }

        /// <summary>把当前轴范围推回 ViewLimits(双向绑定的 VM 侧)</summary>
        private void PushLimits()
        {
            var lim = _plot.Plot.GetAxisLimits();
            var current = ViewLimits;
            if (current != null
                && NearlyEqual(current.XMin, lim.XMin) && NearlyEqual(current.XMax, lim.XMax)
                && NearlyEqual(current.YMin, lim.YMin) && NearlyEqual(current.YMax, lim.YMax))
                return;
            ViewLimits = new ChartViewLimits
            {
                XMin = lim.XMin,
                XMax = lim.XMax,
                YMin = lim.YMin,
                YMax = lim.YMax,
            };
        }

        private void RaiseCursorMoved(double x, double y)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastCursorRaise).TotalMilliseconds < 40) return; // 命令节流
            _lastCursorRaise = now;

            var cmd = CursorMoved;
            if (cmd == null || !cmd.CanExecute(null)) return;

            var info = new ChartCursorInfo { X = x, Y = y };
            if (_cursorTimeBase != null)
                info.TimeText = _cursorTimeBase.Value.AddSeconds(x).ToString("HH:mm:ss.fff");
            cmd.Execute(info);
        }

        #endregion

        #region 跨图光标同步

        private static void OnSyncGroupChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (ChartView)d;
            if (view.IsLoaded) view.JoinSyncGroup();
        }

        private static readonly Dictionary<string, List<WeakReference<ChartView>>> SyncGroups =
            new Dictionary<string, List<WeakReference<ChartView>>>();

        /// <summary>把自己注册进同步组(幂等)。</summary>
        private void JoinSyncGroup()
        {
            var group = SyncGroup;
            if (string.IsNullOrEmpty(group)) return;

            lock (SyncGroups)
            {
                List<WeakReference<ChartView>> members;
                if (!SyncGroups.TryGetValue(group, out members))
                {
                    members = new List<WeakReference<ChartView>>();
                    SyncGroups[group] = members;
                }

                foreach (var wr in members)
                {
                    ChartView existing;
                    if (wr.TryGetTarget(out existing) && ReferenceEquals(existing, this))
                        return;
                }
                members.Add(new WeakReference<ChartView>(this));
                DiagnosticsLog("JoinSyncGroup: " + group + " members=" + members.Count);
            }
        }

        private void BroadcastCursor(double x)
        {
            var group = SyncGroup;
            if (string.IsNullOrEmpty(group)) return;

            lock (SyncGroups)
            {
                List<WeakReference<ChartView>> members;
                if (!SyncGroups.TryGetValue(group, out members))
                {
                    members = new List<WeakReference<ChartView>>();
                    SyncGroups[group] = members;
                }

                // 先把自己注册进组(幂等),再广播给其他成员
                var selfRegistered = false;
                foreach (var wr in members)
                {
                    ChartView existing;
                    if (wr.TryGetTarget(out existing) && ReferenceEquals(existing, this))
                    {
                        selfRegistered = true;
                        break;
                    }
                }
                if (!selfRegistered) members.Add(new WeakReference<ChartView>(this));

                for (var i = members.Count - 1; i >= 0; i--)
                {
                    ChartView other;
                    if (!members[i].TryGetTarget(out other))
                    {
                        members.RemoveAt(i);
                        continue;
                    }
                    if (!ReferenceEquals(other, this)) other.ReceiveCursorX(x);
                }
            }
        }

        /// <summary>同组图同步竖线位置(只动 X,不回抛,避免递归)。</summary>
        private void ReceiveCursorX(double x)
        {
            _crosshair.X = x;
            _crosshair.IsVisible = true;
            DiagnosticsLog("ReceiveCursorX: x=" + x.ToString("F2"));
            _plot.Refresh(); // 立即渲染:静态图自身无数据变化,不主动重绘
        }

        #endregion

        #region 系列/注释 ↔ Plottable 适配

        private static void OnSeriesSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((ChartView)d).AttachSource(e.OldValue as IEnumerable, e.NewValue as IEnumerable);
        }

        private void AttachSource(IEnumerable oldValue, IEnumerable newValue)
        {
            var oldNotifier = oldValue as INotifyCollectionChanged;
            if (oldNotifier != null) oldNotifier.CollectionChanged -= OnSourceCollectionChanged;
            var newNotifier = newValue as INotifyCollectionChanged;
            if (newNotifier != null) newNotifier.CollectionChanged += OnSourceCollectionChanged;

            RebindSeries();
        }

        private void OnSourceCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            RebindSeries();
        }

        /// <summary>重挂全部系列事件(先解后挂,幂等;Loaded 与 Rebind 共用)</summary>
        private void AttachSeriesEvents()
        {
            foreach (var s in _seriesByKey.Values)
            {
                s.PropertyChanged -= OnSeriesPropertyChanged;
                s.PropertyChanged += OnSeriesPropertyChanged;
            }
        }

        private void DetachSeriesEvents()
        {
            foreach (var s in _seriesByKey.Values)
                s.PropertyChanged -= OnSeriesPropertyChanged;
        }

        /// <summary>按 Key 差分同步:新建/移除 Plottable,未变系列的引用与用户缩放状态得以保留。</summary>
        private void RebindSeries()
        {
            var wanted = new Dictionary<string, ISeriesVm>();
            if (SeriesSource != null)
            {
                foreach (var item in SeriesSource)
                {
                    var s = item as ISeriesVm;
                    if (s != null && !wanted.ContainsKey(s.Key)) wanted[s.Key] = s;
                }
            }

            // 移除消失的
            foreach (var gone in _seriesByKey.Keys.Where(k => !wanted.ContainsKey(k)).ToList())
            {
                SignalPlot plottable;
                if (_signals.TryGetValue(gone, out plottable))
                {
                    _plot.Plot.Remove(plottable);
                    _signals.Remove(gone);
                }
                _seriesByKey[gone].PropertyChanged -= OnSeriesPropertyChanged;
                _seriesByKey.Remove(gone);
            }

            // 新增出现的(Plottable 延迟到有数据时再建:实时系列往往从空开始)
            foreach (var pair in wanted)
            {
                if (_seriesByKey.ContainsKey(pair.Key)) continue;
                _seriesByKey[pair.Key] = pair.Value;
                pair.Value.PropertyChanged -= OnSeriesPropertyChanged;
                pair.Value.PropertyChanged += OnSeriesPropertyChanged;
            }

            _plottablesDirty = true;
        }

        private void OnSeriesPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var s = sender as ISeriesVm;
            if (s == null) return;
            SignalPlot signal;
            if (!_signals.TryGetValue(s.Key, out signal)) return;

            if (e.PropertyName == "IsVisible") signal.IsVisible = s.IsVisible;
            else if (e.PropertyName == "Label") signal.Label = s.Label;
            else if (e.PropertyName == "Color") signal.Color = ChartTheme.ToDrawing(s.Color);
            else if (e.PropertyName == "LineWidth") signal.LineWidth = (float)s.LineWidth;
            else if (e.PropertyName == "LineType") signal.LineStyle = MapLineStyle(s.LineType);
            _plot.Refresh();
        }

        private static ScottPlot.LineStyle MapLineStyle(ChartLineType type)
        {
            switch (type)
            {
                case ChartLineType.Dash: return ScottPlot.LineStyle.Dash;
                case ChartLineType.Dot: return ScottPlot.LineStyle.Dot;
                default: return ScottPlot.LineStyle.Solid;
            }
        }

        private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (ChartView)d;
            view._plot.Plot.Title(view.Title ?? "");
            view._plot.Plot.XLabel(view.XLabel ?? "");
            view._plot.Plot.YLabel(view.YLabel ?? "");
            if (!string.IsNullOrEmpty(view.Y2Label))
            {
                view._plot.Plot.YAxis2.Ticks(true);
                view._plot.Plot.YAxis2.Label(view.Y2Label);
            }
            view._plot.Plot.Legend(view.ShowLegend);
            view._plot.Refresh();
        }

        private static void OnViewLimitsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (ChartView)d;
            var limits = e.NewValue as ChartViewLimits;
            if (limits != null)
            {
                view._plot.Plot.SetAxisLimits(limits.XMin, limits.XMax, limits.YMin, limits.YMax);
                view._plot.Refresh();
            }
            // null = 自动缩放,交给渲染循环的 AxisAuto 分支
        }

        private void ApplyTheme()
        {
            _plot.Plot.Style(
                figureBackground: ChartTheme.ToDrawing(ChartTheme.FigureBackground),
                dataBackground: ChartTheme.ToDrawing(ChartTheme.DataBackground),
                grid: ChartTheme.ToDrawing(ChartTheme.Grid),
                tick: ChartTheme.ToDrawing(ChartTheme.Tick),
                axisLabel: ChartTheme.ToDrawing(ChartTheme.AxisLabel),
                titleLabel: ChartTheme.ToDrawing(ChartTheme.TitleLabel),
                dataBackgroundImage: null,
                figureBackgroundImage: null);
        }

        #endregion

        #region 注释(阈值线/事件标记)适配

        private static void OnAnnotationsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (ChartView)d;

            var oldNotifier = e.OldValue as INotifyCollectionChanged;
            if (oldNotifier != null) oldNotifier.CollectionChanged -= view.OnAnnotationsCollectionChanged;
            var newNotifier = e.NewValue as INotifyCollectionChanged;
            if (newNotifier != null) newNotifier.CollectionChanged += view.OnAnnotationsCollectionChanged;

            view.SyncAnnotations();
        }

        private void OnAnnotationsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            SyncAnnotations();
        }

        /// <summary>按 Key 差分同步注释元素,并把属性变化实时映射到 Plottable。</summary>
        private void SyncAnnotations()
        {
            var wanted = new Dictionary<string, IChartAnnotation>();
            if (AnnotationsSource != null)
            {
                foreach (var item in AnnotationsSource)
                {
                    var a = item as IChartAnnotation;
                    if (a != null && !wanted.ContainsKey(a.Key)) wanted[a.Key] = a;
                }
            }

            // 移除消失的
            foreach (var gone in _annotationsByKey.Keys.Where(k => !wanted.ContainsKey(k)).ToList())
            {
                IPlottable plottable;
                if (_annotations.TryGetValue(gone, out plottable))
                {
                    _plot.Plot.Remove(plottable);
                    _annotations.Remove(gone);
                }
                _annotationsByKey[gone].PropertyChanged -= OnAnnotationPropertyChanged;
                _annotationsByKey.Remove(gone);
            }

            // 新增出现的
            foreach (var pair in wanted)
            {
                if (_annotationsByKey.ContainsKey(pair.Key)) continue;
                var ann = pair.Value;
                ann.PropertyChanged -= OnAnnotationPropertyChanged;
                ann.PropertyChanged += OnAnnotationPropertyChanged;
                _annotationsByKey[pair.Key] = ann;
                _annotations[pair.Key] = CreateAnnotationPlottable(ann);
                _plottablesDirty = true;
            }
        }

        private IPlottable CreateAnnotationPlottable(IChartAnnotation ann)
        {
            var lineStyle = MapLineStyle(ann.LineType);
            var color = ChartTheme.ToDrawing(ann.Color);

            var threshold = ann as ThresholdLineVm;
            if (threshold != null)
            {
                var hLine = _plot.Plot.AddHorizontalLine(threshold.Y, color, (float)threshold.LineWidth, lineStyle);
                hLine.Label = ann.Label;
                hLine.IsVisible = ann.IsVisible;
                return hLine;
            }

            var marker = ann as EventMarkerVm;
            if (marker != null)
            {
                var vLine = _plot.Plot.AddVerticalLine(marker.X, color, (float)marker.LineWidth, lineStyle);
                vLine.Label = ann.Label;
                vLine.IsVisible = ann.IsVisible;
                return vLine;
            }

            throw new NotSupportedException("未知注释类型:" + ann.GetType().Name);
        }

        private void OnAnnotationPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var ann = sender as IChartAnnotation;
            if (ann == null) return;
            IPlottable plottable;
            if (!_annotations.TryGetValue(ann.Key, out plottable)) return;

            var hLine = plottable as HLine;
            var vLine = plottable as VLine;
            var lineStyle = MapLineStyle(ann.LineType);
            var color = ChartTheme.ToDrawing(ann.Color);

            if (hLine != null)
            {
                var threshold = (ThresholdLineVm)ann;
                hLine.Y = threshold.Y;
                hLine.Color = color;
                hLine.LineStyle = lineStyle;
                hLine.LineWidth = (float)threshold.LineWidth;
                hLine.IsVisible = ann.IsVisible;
            }
            else if (vLine != null)
            {
                var marker = (EventMarkerVm)ann;
                vLine.X = marker.X;
                vLine.Color = color;
                vLine.LineStyle = lineStyle;
                vLine.LineWidth = (float)marker.LineWidth;
                vLine.IsVisible = ann.IsVisible;
            }

            _plot.Refresh();
        }

        #endregion

        #region 渲染循环(全局调度 + 节流 + 兜底)

        /// <summary>调度器每帧调用。任何渲染异常在这里兜底,连续失败降级为提示,不让现场崩窗。</summary>
        internal void TryRender()
        {
            try
            {
                RenderCore();
                _renderFailures = 0;
            }
            catch (Exception ex)
            {
                _renderFailures++;
                DiagnosticsLog("ChartView 渲染异常(" + _renderFailures + "):" + ex.Message);

                if (_renderFailures >= 5)
                {
                    ChartRenderScheduler.Unregister(this);
                    _plot.Plot.Clear();
                    _plot.Plot.Title("图表渲染失败:" + ex.Message);
                    _plot.Refresh();
                }
            }
        }

        private void RenderCore()
        {
            if (_plot.Visibility != Visibility.Visible) return;

            // 延迟建 Plottable:系列有数据但还没上图的(实时流从空开始的场景)
            foreach (var s in _seriesByKey.Values)
            {
                if (s.Count > 0 && !_signals.ContainsKey(s.Key))
                {
                    var color = s.Color.A == 0 ? ChartTheme.AutoColor(_paletteIndex++) : s.Color;
                    var signal = _plot.Plot.AddSignal(s.Buffer, s.SampleRate);
                    signal.Label = s.Label;
                    signal.Color = ChartTheme.ToDrawing(color);
                    signal.IsVisible = s.IsVisible;
                    signal.YAxisIndex = s.YAxisIndex;
                    signal.LineWidth = (float)s.LineWidth;
                    signal.LineStyle = MapLineStyle(s.LineType);
                    signal.MaxRenderIndex = s.Count - 1; // Signal 禁止 NaN:有效区外靠渲染索引截断
                    if (s.YAxisIndex == 1)
                        _plot.Plot.YAxis2.Ticks(true); // 有右轴系列时启用右轴刻度
                    _signals[s.Key] = signal;
                    _plottablesDirty = true;
                }
            }

            // 数据持续增长时同步渲染上界
            foreach (var pair in _signals)
            {
                var seriesCount = _seriesByKey[pair.Key].Count;
                if (seriesCount > 0 && pair.Value.MaxRenderIndex != seriesCount - 1)
                    pair.Value.MaxRenderIndex = seriesCount - 1;
            }

            // 绝对时间轴:取系列里第一个非空 StartTime
            DateTime? timeBase = null;
            foreach (var s in _seriesByKey.Values)
            {
                if (s.StartTime != null) { timeBase = s.StartTime; break; }
            }
            if (timeBase != _cursorTimeBase)
            {
                _cursorTimeBase = timeBase;
                if (timeBase != null)
                {
                    var start = timeBase.Value;
                    _plot.Plot.XAxis.TickLabelFormat(x => start.AddSeconds(x).ToString("HH:mm:ss"));
                }
                _plottablesDirty = true;
            }

            var series = _seriesByKey.Values.Where(s => s.Count > 0 && s.IsVisible).ToList();

            var maxVersion = 0;
            foreach (var s in _seriesByKey.Values)
                if (s.Version > maxVersion) maxVersion = s.Version;

            bool dataDirty = maxVersion != _lastVersion;
            bool needRender = _plottablesDirty || dataDirty;

            // 用户交互(拖拽中/滚轮后 1.5s)期间暂停跟随,否则交互会被每帧覆盖
            var interactionPause = _mouseDown
                || (DateTime.UtcNow - _lastInteractionUtc).TotalMilliseconds < 1500;
            var follow = AutoScroll && series.Count > 0 && !interactionPause && _userFollow;

            if (follow)
            {
                double head = series.Max(s => s.HeadSeconds);
                double window = Math.Max(1, AutoScrollSeconds);
                double xMin = Math.Max(0, head - window);
                double xMax = head + window * 0.05;

                // 按轴分组求窗口内 Y 范围;大缓冲步进采样,扫描成本封顶
                double y1Min = double.MaxValue, y1Max = double.MinValue;
                double y2Min = double.MaxValue, y2Max = double.MinValue;
                foreach (var s in series)
                {
                    lock (s.Lock)
                    {
                        var n = Math.Min(s.Count, (int)(window * s.SampleRate));
                        var stride = Math.Max(1, n / 2000);
                        for (var i = s.Count - n; i < s.Count; i += stride)
                        {
                            var v = s.Buffer[i];
                            if (s.YAxisIndex == 1)
                            {
                                if (v < y2Min) y2Min = v;
                                if (v > y2Max) y2Max = v;
                            }
                            else
                            {
                                if (v < y1Min) y1Min = v;
                                if (v > y1Max) y1Max = v;
                            }
                        }
                        // 步进会跳过末尾样本,补一次确认极值
                        var last = s.Buffer[s.Count - 1];
                        if (s.YAxisIndex == 1)
                        {
                            if (last < y2Min) y2Min = last;
                            if (last > y2Max) y2Max = last;
                        }
                        else
                        {
                            if (last < y1Min) y1Min = last;
                            if (last > y1Max) y1Max = last;
                        }
                    }
                }

                var p1 = Pad(y1Min, y1Max);
                _plot.Plot.SetAxisLimits(xMin, xMax, y1Min - p1, y1Max + p1);
                if (y2Min != double.MaxValue)
                {
                    var p2 = Pad(y2Min, y2Max);
                    _plot.Plot.SetAxisLimits(null, null, y2Min - p2, y2Max + p2, 0, 1);
                }
                needRender = true;
            }
            else if (needRender && !_plottablesDirty && series.Count > 0 && ViewLimits == null)
            {
                // 静态图:数据变化后自动缩放一次;用户已接管视图(ViewLimits 非空)时不抢
                _plot.Plot.AxisAuto();
            }

            if (!needRender) return;

            _plottablesDirty = false;
            _lastVersion = maxVersion;
            _plot.Plot.Legend(ShowLegend);
            UpdateChip();

            // 渲染计时:交互中降质渲染(松手后由下一帧高质量重绘),慢帧记诊断
            var interacting = _mouseDown
                || (DateTime.UtcNow - _lastInteractionUtc).TotalMilliseconds < 300;
            var watch = Stopwatch.StartNew();
            _plot.Refresh(interacting);
            watch.Stop();

            if (watch.ElapsedMilliseconds > 50
                && (DateTime.UtcNow - _lastSlowFrameLog).TotalSeconds > 1)
            {
                _lastSlowFrameLog = DateTime.UtcNow;
                DiagnosticsLog("ChartView 慢帧:" + watch.ElapsedMilliseconds + "ms");
            }
        }

        private static double Pad(double min, double max)
        {
            if (min == double.MaxValue) return 1;
            return (max - min) * 0.15 + 0.001;
        }

        private void UpdateChip()
        {
            // 非 AutoScroll 的静态图不显示状态 chip(它本来就不跟随,不存在"暂停"语义)
            var following = AutoScroll && _userFollow;
            var chip = _chipText.Parent as Border;
            if (chip != null) chip.Visibility = AutoScroll ? Visibility.Visible : Visibility.Collapsed;
            _chipText.Text = following ? "● 跟随中" : "‖ 已暂停(点击恢复)";
            _chipText.Foreground = following
                ? new SolidColorBrush(Color.FromRgb(0x0E, 0x8A, 0x5F))
                : new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));
        }

        #endregion
    }
}
