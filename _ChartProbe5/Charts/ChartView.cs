using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ScottPlot;
using ScottPlot.Plottables;

namespace VM.Charts
{
    /// <summary>十字光标坐标(经 <see cref="ChartView.CursorMoved"/> 抛给 VM 的参数)。</summary>
        public class ChartCursorInfo
        {
            public double X { get; set; }
            public double Y { get; set; }

            /// <summary>系列设置了 StartTime 时给出绝对时刻文本,否则为 null。</summary>
            public string TimeText { get; set; }

            /// <summary>开启 SnapToNearest 且光标吸附到数据点时,给出所属系列名。</summary>
            public string SeriesLabel { get; set; }

            /// <summary>本次坐标是否吸附到了数据点(而非自由位置)。</summary>
            public bool Snapped { get; set; }
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
    /// ScottPlot 的 MVVM 封装 —— <b>ScottPlot 5.1.x</b> 版。
    ///
    /// 【实时流】5.1 的 <c>Signal</c> 支持原址数组 + MaxRenderIndex 截断,
    ///   与 4.1 同构:缓冲区引用交给 Signal,控件每帧同步 MaxRenderIndex。
    ///   (5 的 DataStreamer 是推模型且自带轴管理,与"轮询拉模型"冲突,未采用)
    /// 【轴系统】SetLimits/AutoScale 走 Axes 管理器;Y2 用 SetLimitsY(..., yAxis)。
    /// 其余边界约定与 4.1 版一致:VM 零 ScottPlot 类型,升级只重写本类。
    /// </summary>
    public class ChartView : UserControl
    {
        private ScottPlot.WPF.WpfPlot _plot;
        private Crosshair _crosshair;
        private Crosshair _crosshairY2; // 右轴十字光标(双轴图的 Y2 读数)
        private readonly Dictionary<string, Signal> _signals = new Dictionary<string, Signal>();
        private readonly Dictionary<string, ISeriesVm> _seriesByKey = new Dictionary<string, ISeriesVm>();
        private readonly Dictionary<string, Scatter> _scatters = new Dictionary<string, Scatter>();
        private readonly Dictionary<string, IScatterSeriesVm> _scattersByKey = new Dictionary<string, IScatterSeriesVm>();
        private readonly Dictionary<string, IPlottable> _annotations = new Dictionary<string, IPlottable>();
        private readonly Dictionary<string, IChartAnnotation> _annotationsByKey = new Dictionary<string, IChartAnnotation>();
        private readonly Dictionary<string, GaugeVm> _gaugesByKey = new Dictionary<string, GaugeVm>();
        private RadialGaugePlot _gauges;
        private double[] _gaugeValues;
        private volatile bool _gaugesDirty;
        private int _paletteIndex;
        private bool _plottablesDirty = true;
        private int _lastVersion;
        private DateTime _lastCursorRaise = DateTime.MinValue;

        private bool _mouseDown;
        private DateTime _lastInteractionUtc = DateTime.MinValue;
        private bool _userFollow = true;

        private TextBlock _chipText;
        private Border _tip;
        private TextBlock _tipText;
        private Point _lastMousePos = new Point(24, 12);
        private DateTime _lastTipUtc = DateTime.MinValue;
        private ScottPlot.Plottables.VerticalLine _measureA;
        private ScottPlot.Plottables.VerticalLine _measureB;
        private Border _measurePanel;
        private TextBlock _measurePanelText;
        private bool _measureDirty;
        private bool _measureInitialized;
        private int _renderFailures;
        private DateTime _lastSlowFrameLog = DateTime.MinValue;
        private DateTime? _cursorTimeBase; // 系列里第一个非空 StartTime

        // 各 Source DP 归一化后的可枚举(单个 VM 也能绑定)
        private IEnumerable _seriesSourceCurrent;
        private IEnumerable _annotationsSourceCurrent;
        private IEnumerable _extrasSourceCurrent;
        private IEnumerable _gaugesSourceCurrent;
        private IEnumerable _menuItemsCurrent;
        private bool _menuCustomized;

        /// <summary>渲染循环的可见系列复用缓冲(单线程渲染 tick 内使用)</summary>
        private readonly List<ISeriesVm> _visibleSeriesBuffer = new List<ISeriesVm>();

        /// <summary>纯饼图时自动隐藏坐标轴网格的状态(条件解除后恢复)</summary>
        private bool _autoHidAxes;

        /// <summary>诊断输出钩子(慢帧/渲染异常)。宿主可重定向;默认走 Trace 警告。</summary>
        public static Action<string> DiagnosticsLog = message =>
            System.Diagnostics.Trace.TraceWarning(message);

        #region 依赖属性

        // 注意:Source 类 DP 一律用 typeof(object) 注册 ——
        // 单个系列 VM 绑到 IEnumerable 型 DP 时,WPF 绑定引擎因类型不匹配
        // 会静默丢弃值,回调根本不会触发(实测踩坑);object 型能接收任何值,
        // 归一化(单个→数组)在回调里做。
        public static readonly DependencyProperty SeriesSourceProperty =
            DependencyProperty.Register("SeriesSource", typeof(object), typeof(ChartView),
                new PropertyMetadata(null, OnSeriesSourceChanged));

        public object SeriesSource
        {
            get { return GetValue(SeriesSourceProperty); }
            set { SetValue(SeriesSourceProperty, value); }
        }

        public static readonly DependencyProperty AnnotationsSourceProperty =
            DependencyProperty.Register("AnnotationsSource", typeof(object), typeof(ChartView),
                new PropertyMetadata(null, OnAnnotationsSourceChanged));

        public object AnnotationsSource
        {
            get { return GetValue(AnnotationsSourceProperty); }
            set { SetValue(AnnotationsSourceProperty, value); }
        }

        /// <summary>扩展图形集合(柱状/饼图/直方图/函数/填充/热力图;单个对象亦可)</summary>
        public static readonly DependencyProperty ExtrasSourceProperty =
            DependencyProperty.Register("ExtrasSource", typeof(object), typeof(ChartView),
                new PropertyMetadata(null, OnExtrasSourceChanged));

        public object ExtrasSource
        {
            get { return GetValue(ExtrasSourceProperty); }
            set { SetValue(ExtrasSourceProperty, value); }
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

        public static readonly DependencyProperty AutoScrollProperty =
            DependencyProperty.Register("AutoScroll", typeof(bool), typeof(ChartView),
                new PropertyMetadata(false));

        public bool AutoScroll
        {
            get { return (bool)GetValue(AutoScrollProperty); }
            set { SetValue(AutoScrollProperty, value); }
        }

        public static readonly DependencyProperty AutoScrollSecondsProperty =
            DependencyProperty.Register("AutoScrollSeconds", typeof(double), typeof(ChartView),
                new PropertyMetadata(5d));

        public double AutoScrollSeconds
        {
            get { return (double)GetValue(AutoScrollSecondsProperty); }
            set { SetValue(AutoScrollSecondsProperty, value); }
        }

        public static readonly DependencyProperty ShowLegendProperty =
            DependencyProperty.Register("ShowLegend", typeof(bool), typeof(ChartView),
                new PropertyMetadata(false, OnLabelChanged));

        public bool ShowLegend
        {
            get { return (bool)GetValue(ShowLegendProperty); }
            set { SetValue(ShowLegendProperty, value); }
        }

        public static readonly DependencyProperty ViewLimitsProperty =
            DependencyProperty.Register("ViewLimits", typeof(ChartViewLimits), typeof(ChartView),
                new PropertyMetadata(null, OnViewLimitsChanged));

        public ChartViewLimits ViewLimits
        {
            get { return (ChartViewLimits)GetValue(ViewLimitsProperty); }
            set { SetValue(ViewLimitsProperty, value); }
        }

        public static readonly DependencyProperty CursorMovedProperty =
            DependencyProperty.Register("CursorMoved", typeof(ICommand), typeof(ChartView),
                new PropertyMetadata(null));

        public ICommand CursorMoved
        {
            get { return (ICommand)GetValue(CursorMovedProperty); }
            set { SetValue(CursorMovedProperty, value); }
        }

        /// <summary>双轴图开启第二根十字光标(打在右轴上,与主光标同步移动)</summary>
        public static readonly DependencyProperty CrosshairOnY2Property =
            DependencyProperty.Register("CrosshairOnY2", typeof(bool), typeof(ChartView),
                new PropertyMetadata(false));

        public bool CrosshairOnY2
        {
            get { return (bool)GetValue(CrosshairOnY2Property); }
            set { SetValue(CrosshairOnY2Property, value); }
        }

        /// <summary>
        /// 十字光标开关。实时采集运行中置 false 可隐藏十字光标
        /// (采集时鼠标扫过图表不再打断渲染)。
        /// </summary>
        public static readonly DependencyProperty CrosshairEnabledProperty =
            DependencyProperty.Register("CrosshairEnabled", typeof(bool), typeof(ChartView),
                new PropertyMetadata(true));

        public bool CrosshairEnabled
        {
            get { return (bool)GetValue(CrosshairEnabledProperty); }
            set { SetValue(CrosshairEnabledProperty, value); }
        }

        /// <summary>
        /// 悬浮数据提示:鼠标扫过曲线时,在光标旁显示黑底数据签
        /// (吸附时带系列名与点值,未吸附时显示坐标)。
        /// </summary>
        public static readonly DependencyProperty ShowDataTipsProperty =
            DependencyProperty.Register("ShowDataTips", typeof(bool), typeof(ChartView),
                new PropertyMetadata(true));

        public bool ShowDataTips
        {
            get { return (bool)GetValue(ShowDataTipsProperty); }
            set { SetValue(ShowDataTipsProperty, value); }
        }

        /// <summary>
        /// 最近点吸附:开启后鼠标靠近曲线时,十字光标自动吸附到最近的数据点,
        /// CursorMoved 参数会带出所属系列名(Snapped=true)。
        /// </summary>
        public static readonly DependencyProperty SnapToNearestProperty =
            DependencyProperty.Register("SnapToNearest", typeof(bool), typeof(ChartView),
                new PropertyMetadata(false));

        public bool SnapToNearest
        {
            get { return (bool)GetValue(SnapToNearestProperty); }
            set { SetValue(SnapToNearestProperty, value); }
        }

        /// <summary>
        /// 双卡尺测量模式:两条可拖拽的静态竖线,两线间常驻显示
        /// ΔX / 1÷ΔX(频率)/ 各可见曲线的 ΔY,用于测周期/脉宽/相位差。
        /// </summary>
        public static readonly DependencyProperty ShowMeasureCursorsProperty =
            DependencyProperty.Register("ShowMeasureCursors", typeof(bool), typeof(ChartView),
                new PropertyMetadata(false, OnShowMeasureCursorsChanged));

        public bool ShowMeasureCursors
        {
            get { return (bool)GetValue(ShowMeasureCursorsProperty); }
            set { SetValue(ShowMeasureCursorsProperty, value); }
        }

        /// <summary>卡尺线 A 的 X 位置(双向,可拖拽);拖动后本属性自动更新。</summary>
        public static readonly DependencyProperty MeasureCursorAXProperty =
            DependencyProperty.Register("MeasureCursorAX", typeof(double), typeof(ChartView),
                new PropertyMetadata(double.NaN, OnMeasureCursorXChanged));

        public double MeasureCursorAX
        {
            get { return (double)GetValue(MeasureCursorAXProperty); }
            set { SetValue(MeasureCursorAXProperty, value); }
        }

        /// <summary>卡尺线 B 的 X 位置(双向,可拖拽);拖动后本属性自动更新。</summary>
        public static readonly DependencyProperty MeasureCursorBXProperty =
            DependencyProperty.Register("MeasureCursorBX", typeof(double), typeof(ChartView),
                new PropertyMetadata(double.NaN, OnMeasureCursorXChanged));

        public double MeasureCursorBX
        {
            get { return (double)GetValue(MeasureCursorBXProperty); }
            set { SetValue(MeasureCursorBXProperty, value); }
        }

        private static void OnShowMeasureCursorsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (ChartView)d;
            if ((bool)e.NewValue) view.EnsureMeasureCursors();
            else view.RemoveMeasureCursors();
        }

        /// <summary>
        /// 该图是否支持测量卡尺(仅折线/曲线图有意义)。
        /// false 时右键菜单不显示"显示测量卡尺"项;若已开启则自动关闭。
        /// 饼图/柱状图/仪表/热力图等统计图置 false。
        /// </summary>
        public static readonly DependencyProperty MeasurableProperty =
            DependencyProperty.Register("Measurable", typeof(bool), typeof(ChartView),
                new PropertyMetadata(true, OnMeasurableChanged));

        public bool Measurable
        {
            get { return (bool)GetValue(MeasurableProperty); }
            set { SetValue(MeasurableProperty, value); }
        }

        private static void OnMeasurableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (ChartView)d;
            if (!(bool)e.NewValue && view.ShowMeasureCursors)
                view.ShowMeasureCursors = false; // 自动关闭并移除卡尺
            view.RebuildMenu();
        }

        private static void OnMeasureCursorXChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (ChartView)d;
            if (view._measureA != null && !double.IsNaN(view.MeasureCursorAX))
                view._measureA.X = view.MeasureCursorAX;
            if (view._measureB != null && !double.IsNaN(view.MeasureCursorBX))
                view._measureB.X = view.MeasureCursorBX;
            view._measureDirty = true;
        }

        /// <summary>仪表盘集合(GaugeVm)。仅放仪表的图会自动隐藏网格与坐标轴。</summary>
        public static readonly DependencyProperty GaugesSourceProperty =
            DependencyProperty.Register("GaugesSource", typeof(object), typeof(ChartView),
                new PropertyMetadata(null, OnGaugesSourceChanged));

        public object GaugesSource
        {
            get { return GetValue(GaugesSourceProperty); }
            set { SetValue(GaugesSourceProperty, value); }
        }

        /// <summary>
        /// 底层绘图控件(转义舱口):复杂的自定义交互(自定义右键菜单、
        /// 特殊光标事件)可经此接入,但约定只许 View 层使用。
        /// </summary>
        public ScottPlot.WPF.WpfPlot PlotControl
        {
            get { return _plot; }
        }

        /// <summary>
        /// 是否显示右键菜单(中文内置项:复制图像 / 另存为 PNG / 复位视图)。
        /// false 时清空菜单;再由 <see cref="MenuItemsSource"/> 或 <see cref="ConfigureMenu"/> 提供自定义项。
        /// </summary>
        public static readonly DependencyProperty ShowDefaultMenuProperty =
            DependencyProperty.Register("ShowDefaultMenu", typeof(bool), typeof(ChartView),
                new PropertyMetadata(true, OnShowDefaultMenuChanged));

        public bool ShowDefaultMenu
        {
            get { return (bool)GetValue(ShowDefaultMenuProperty); }
            set { SetValue(ShowDefaultMenuProperty, value); }
        }

        /// <summary>
        /// 业务菜单项集合(ChartMenuItemVm):追加在"复位视图"之后,点击执行其命令。
        /// 接受集合或单个项(内部归一化)。
        /// </summary>
        public static readonly DependencyProperty MenuItemsSourceProperty =
            DependencyProperty.Register("MenuItemsSource", typeof(object), typeof(ChartView),
                new PropertyMetadata(null, OnMenuItemsSourceChanged));

        public object MenuItemsSource
        {
            get { return GetValue(MenuItemsSourceProperty); }
            set { SetValue(MenuItemsSourceProperty, value); }
        }

        private static void OnShowDefaultMenuChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((ChartView)d).RebuildMenu();
        }

        private static void OnMenuItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (ChartView)d;
            view._menuItemsCurrent = NormalizeSource(e.NewValue, typeof(ChartMenuItemVm));

            var oldNotifier = e.OldValue as INotifyCollectionChanged;
            if (oldNotifier != null) oldNotifier.CollectionChanged -= view.OnMenuItemsCollectionChanged;
            var newNotifier = e.NewValue as INotifyCollectionChanged;
            if (newNotifier != null) newNotifier.CollectionChanged += view.OnMenuItemsCollectionChanged;

            view.RebuildMenu();
        }

        private void OnMenuItemsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            RebuildMenu();
        }

        /// <summary>
        /// 完全自定义右键菜单(取代内置中文项):Clear() 后 Add() 自己的项。
        /// 调用后 ShowDefaultMenu / MenuItemsSource 的自动重建被跳过。
        /// 示例:chart.ConfigureMenu(m => { m.Clear(); m.Add("导出图像", _ => chart.SavePng("a.png")); });
        /// </summary>
        public void ConfigureMenu(Action<ScottPlot.IPlotMenu> configure)
        {
            _menuCustomized = true;
            configure?.Invoke(_plot.Menu);
        }

        /// <summary>重建右键菜单:内置中文项 + 业务项(MenuItemsSource)。</summary>
        private void RebuildMenu()
        {
            if (_menuCustomized) return;
            var menu = _plot.Menu;
            if (menu == null) return;
            menu.Clear();
            if (!ShowDefaultMenu) return;

            menu.Add("复制图像", _ => CopyImageToClipboard());
            menu.Add("另存为 PNG...", _ => SavePngDialog());
            menu.Add("复位视图", _ =>
            {
                _plot.Plot.Axes.AutoScale();
                _plot.Refresh();
            });
            menu.AddSeparator();
            if (Measurable)
            {
                // 卡尺只对折线/曲线图有意义(Measurable=false 的图不显示该菜单项)
                menu.Add(ShowMeasureCursors ? "✔ 显示测量卡尺" : "显示测量卡尺", _ =>
                {
                    ShowMeasureCursors = !ShowMeasureCursors;
                    RebuildMenu();
                });
            }
            menu.Add(SyncEnabled ? "✔ 光标同步" : "光标同步", _ =>
            {
                // 同组图十字光标竖线对齐开关(组身份由 SyncGroup 决定)
                SyncEnabled = !SyncEnabled;
                RebuildMenu();
            });
            menu.Add(LinkedAxis ? "✔ 轴范围同步" : "轴范围同步", _ =>
            {
                // 本图缩放/设置范围时,同组图跟随(多图缩放联动)
                LinkedAxis = !LinkedAxis;
                if (LinkedAxis && ViewLimits != null) BroadcastLimits(ViewLimits);
                RebuildMenu();
            });
            menu.Add("导入数据...", _ => ImportCsvDialog());
            menu.Add("导出数据 CSV...", _ => ExportCsvDialog());

            var items = (_menuItemsCurrent ?? Enumerable.Empty<object>()).OfType<ChartMenuItemVm>().ToList();
            if (items.Count > 0)
            {
                menu.AddSeparator();
                foreach (var item in items)
                {
                    menu.Add(item.Label, _ =>
                    {
                        if (item.Command != null && item.Command.CanExecute(item.CommandParameter))
                            item.Command.Execute(item.CommandParameter);
                    });
                }
            }

            // 自检输出:确认自定义菜单真的构建了(排查"菜单项缺失/还是英文菜单"用)
            try
            {
                DiagnosticsLog("右键菜单项数:" + menu.ContextMenuItems.Count +
                               (ShowMeasureCursors ? " (卡尺开)" : " (卡尺关)"));
            }
            catch { }
        }

        /// <summary>右键"导入数据..."：解析 CSV 后触发事件,由宿主决定送往哪个系列。</summary>
        public event Action<CsvImportData> CsvImportRequested;

        private void ImportCsvDialog()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "CSV 数据 (*.csv)|*.csv" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var lines = System.IO.File.ReadAllLines(dlg.FileName);
                if (lines.Length < 2)
                {
                    DiagnosticsLog("导入失败:CSV 只有表头,没有数据行");
                    return;
                }
                var header = lines[0].Split(',');
                var colCount = header.Length;
                var rows = new List<double[]>();
                for (var i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    var parts = lines[i].Split(',');
                    if (parts.Length < colCount) continue;
                    var row = new double[colCount];
                    for (var c = 0; c < colCount; c++)
                    {
                        double.TryParse(parts[c].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out row[c]);
                    }
                    rows.Add(row);
                }
                var columns = new double[colCount][];
                for (var c = 0; c < colCount; c++)
                {
                    columns[c] = new double[rows.Count];
                    for (var r = 0; r < rows.Count; r++) columns[c][r] = rows[r][c];
                }
                CsvImportRequested?.Invoke(new CsvImportData
                {
                    FilePath = dlg.FileName,
                    ColumnNames = header,
                    Columns = columns,
                });
                DiagnosticsLog("导入 CSV:" + dlg.FileName + " (" + rows.Count + " 行 × " + colCount + " 列)");
            }
            catch (Exception ex)
            {
                DiagnosticsLog("导入失败:" + ex.Message);
            }
        }

        /// <summary>右键"导出数据 CSV..."：按公共时间轴导出全部可见信号系列(散点系列跳过)。</summary>
        private void ExportCsvDialog()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV 数据 (*.csv)|*.csv",
                FileName = "数据.csv",
                DefaultExt = ".csv",
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var signals = _seriesByKey.Values.Where(s => s.IsVisible).ToList();
                if (signals.Count == 0)
                {
                    DiagnosticsLog("导出失败:没有可见的信号系列");
                    return;
                }
                var sb = new System.Text.StringBuilder();
                var header = new List<string> { "time" };
                foreach (var s in signals) header.Add(s.Label);
                sb.AppendLine(string.Join(",", header));

                // 公共时间轴取第一条可见系列(采样率/起点);其余按各自 XOffset 取值
                var baseRate = signals[0].SampleRate;
                var baseOffset = signals[0].XOffsetSeconds;
                var count = signals.Max(s => s.Count);
                for (var i = 0; i < count; i++)
                {
                    var time = i / (double)baseRate + baseOffset;
                    var cells = new List<string> { time.ToString("F4", CultureInfo.InvariantCulture) };
                    foreach (var s in signals)
                    {
                        lock (s.Lock)
                        {
                            cells.Add(s.Count > i
                                ? s.Buffer[i].ToString("F6", CultureInfo.InvariantCulture)
                                : "");
                        }
                    }
                    sb.AppendLine(string.Join(",", cells));
                }
                System.IO.File.WriteAllText(dlg.FileName, sb.ToString(), System.Text.Encoding.UTF8);
                DiagnosticsLog("导出 CSV:" + dlg.FileName + " (" + count + " 行 × " + signals.Count + " 系列)");
            }
            catch (Exception ex)
            {
                DiagnosticsLog("导出失败:" + ex.Message);
            }
        }

        private void CopyImageToClipboard()
        {
            try
            {
                var w = (int)Math.Max(1, _plot.ActualWidth);
                var h = (int)Math.Max(1, _plot.ActualHeight);
                var png = _plot.Plot.GetImageBytes(w, h, ScottPlot.ImageFormat.Png);
                if (png == null || png.Length == 0) return;
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                using (var stream = new System.IO.MemoryStream(png))
                {
                    bmp.BeginInit();
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.StreamSource = stream;
                    bmp.EndInit();
                }
                System.Windows.Clipboard.SetImage(bmp);
            }
            catch (Exception ex)
            {
                DiagnosticsLog("复制图像失败:" + ex.Message);
            }
        }

        private void SavePngDialog()
        {
            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "PNG 图片 (*.png)|*.png",
                    FileName = "图表.png",
                    DefaultExt = ".png",
                };
                if (dlg.ShowDialog() == true)
                    _plot.Plot.SavePng(dlg.FileName,
                        (int)Math.Max(1, _plot.ActualWidth),
                        (int)Math.Max(1, _plot.ActualHeight));
            }
            catch (Exception ex)
            {
                DiagnosticsLog("保存图像失败:" + ex.Message);
            }
        }

        /// <summary>设置轴刻度/标签颜色(null = 不改)。</summary>
        public void SetAxisColors(System.Windows.Media.Color? xAxis, System.Windows.Media.Color? yAxis, System.Windows.Media.Color? y2Axis)
        {
            if (xAxis != null) _plot.Plot.Axes.Bottom.TickLabelStyle.ForeColor = ChartTheme.ToSP(xAxis.Value);
            if (yAxis != null) _plot.Plot.Axes.Left.TickLabelStyle.ForeColor = ChartTheme.ToSP(yAxis.Value);
            if (y2Axis != null) _plot.Plot.Axes.Right.TickLabelStyle.ForeColor = ChartTheme.ToSP(y2Axis.Value);
            _plot.Refresh();
        }

        public static readonly DependencyProperty SyncGroupProperty =
            DependencyProperty.Register("SyncGroup", typeof(string), typeof(ChartView),
                new PropertyMetadata(null, OnSyncGroupChanged));

        public string SyncGroup
        {
            get { return (string)GetValue(SyncGroupProperty); }
            set { SetValue(SyncGroupProperty, value); }
        }

        /// <summary>
        /// 光标同步开关(默认开):同组图的十字光标竖线相互对齐。
        /// 与 <see cref="SyncGroup"/> 独立——组身份由 SyncGroup 决定,本开关只管收发。
        /// </summary>
        public static readonly DependencyProperty SyncEnabledProperty =
            DependencyProperty.Register("SyncEnabled", typeof(bool), typeof(ChartView),
                new PropertyMetadata(true));

        public bool SyncEnabled
        {
            get { return (bool)GetValue(SyncEnabledProperty); }
            set { SetValue(SyncEnabledProperty, value); }
        }

        /// <summary>
        /// 轴范围同步开关(默认关):本图轴范围变化(用户缩放/VM 设置)时
        /// 广播给同组其它图,多图缩放联动。
        /// </summary>
        public static readonly DependencyProperty LinkedAxisProperty =
            DependencyProperty.Register("LinkedAxis", typeof(bool), typeof(ChartView),
                new PropertyMetadata(false));

        public bool LinkedAxis
        {
            get { return (bool)GetValue(LinkedAxisProperty); }
            set { SetValue(LinkedAxisProperty, value); }
        }

        private bool _isReceivingLimits;

        // 自研拖拽状态(卡尺线/可拖拽阈值线;ScottPlot 5.1 默认交互器对 IsDraggable 支持不可靠)
        private enum DragTarget
        {
            None,
            MeasureA,
            MeasureB,
            Threshold,
        }
        private DragTarget _dragTarget;
        private ScottPlot.Plottables.HorizontalLine _dragHLine;
        private ThresholdLineVm _dragThreshold;

        #endregion

        public ChartView()
        {
            ChartTheme.Reload();

            var grid = new Grid();
            _plot = new ScottPlot.WPF.WpfPlot();
            grid.Children.Add(_plot);

            _chipText = new TextBlock { FontSize = 11 };
            var chip = new Border
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 24, 34),
                Padding = new Thickness(8, 3, 8, 3),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xD9, 0xF5, 0xF5, 0xF5)),
                Child = _chipText,
                Cursor = Cursors.Hand,
            };
            chip.MouseLeftButtonUp += delegate
            {
                _userFollow = !_userFollow;
                UpdateChip();
            };
            grid.Children.Add(chip);

            // 悬浮数据提示(跟随鼠标的黑底数据签)
            _tipText = new TextBlock { FontSize = 11, Foreground = Brushes.White };
            _tip = new Border
            {
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                Padding = new Thickness(7, 4, 7, 4),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xD8, 0x20, 0x20, 0x20)),
                Child = _tipText,
            };
            grid.Children.Add(_tip);

            Content = grid;

            _crosshair = _plot.Plot.Add.Crosshair(0, 0);
            _crosshair.IsVisible = false;
            _crosshair.LineColor = new ScottPlot.Color(128, 128, 128);
            _crosshair.LineWidth = 1;
            _crosshair.LinePattern = ScottPlot.LinePattern.Dotted;

            // 双轴图的第二根十字光标:竖线与主光标同步,横线打在右轴刻度上
            _crosshairY2 = _plot.Plot.Add.Crosshair(0, 0);
            _crosshairY2.IsVisible = false;
            _crosshairY2.LineColor = new ScottPlot.Color(128, 128, 128);
            _crosshairY2.LineWidth = 1;
            _crosshairY2.LinePattern = ScottPlot.LinePattern.Dotted;
            _crosshairY2.Axes.YAxis = _plot.Plot.Axes.Right;
            _crosshairY2.HorizontalLine.LabelOppositeAxis = false;

            _plot.MouseMove += OnPlotMouseMove;
            _plot.MouseLeave += OnPlotMouseLeave;
            _plot.MouseDoubleClick += OnPlotMouseDoubleClick;
            _plot.MouseLeftButtonUp += OnPlotMouseLeftUp;
            // 用隧道事件做命中:标记 Handled 让 ScottPlot 自带的 pan/zoom 收不到这次按下
            _plot.PreviewMouseLeftButtonDown += OnPlotMouseLeftDown;
            _plot.MouseWheel += OnPlotMouseWheel;

            Loaded += delegate
            {
                ChartTheme.Reload();
                ApplyTheme();
                AttachAllSources();
                ChartRenderScheduler.Register(this);
                JoinSyncGroup();
                // 初始就构建自定义菜单(DP 变化回调只在绑定后触发,
                // 未绑定 MenuItemsSource 的图会一直停留在 ScottPlot 默认英文菜单)
                RebuildMenu();
            };
            Unloaded += delegate
            {
                ChartRenderScheduler.Unregister(this);
                // 统一解绑全部 VM 订阅,防止长生命周期 VM 拽住已丢弃的控件
                DetachAllSources();
            };
        }

        /// <summary>UI 线程封送:VM 的 INPC 可能来自采集/连接线程,控件必须回 UI 线程刷新。</summary>
        private void RunOnUiThread(Action action)
        {
            if (Dispatcher.CheckAccess()) action();
            else Dispatcher.BeginInvoke(action);
        }

        /// <summary>把当前图导出为 PNG(VM 可直接调用,不需要窗口参与)。</summary>
        public void SavePng(string filePath, int width = 800, int height = 600)
        {
            _plot.Plot.SavePng(filePath, width, height);
        }

        public void PlaceCursor(double x, double y)
        {
            PlaceCursor(x, y, null, false);
        }

        /// <summary>带吸附信息的重载:snapped=true 时 CursorMoved 会带出所属系列名。</summary>
        public void PlaceCursor(double x, double y, string seriesLabel, bool snapped)
        {
            // 公共入口:强制刷新悬浮提示(程序化/自动化不受鼠标节流限制)
            PlaceCursorInternal(x, y, seriesLabel, snapped, true);
        }

        /// <summary>光标内部实现。updateTip=false 时跳过悬浮提示重算(鼠标高频移动的节流路径)。</summary>
        private void PlaceCursorInternal(double x, double y, string seriesLabel, bool snapped, bool updateTip)
        {
            _crosshair.X = x;
            _crosshair.Y = y;
            _crosshair.IsVisible = true;
            UpdateY2Crosshair(x);
            if (updateTip)
            {
                _lastTipUtc = DateTime.UtcNow;
                UpdateDataTip(x, y, seriesLabel, snapped);
            }

            var now = DateTime.UtcNow;
            if ((now - _lastCursorRaise).TotalMilliseconds >= 40)
            {
                _lastCursorRaise = now;
                var cmd = CursorMoved;
                if (cmd != null && cmd.CanExecute(null))
                {
                    var info = new ChartCursorInfo { X = x, Y = y, SeriesLabel = seriesLabel, Snapped = snapped };
                    if (_cursorTimeBase != null)
                        info.TimeText = _cursorTimeBase.Value.AddSeconds(x).ToString("HH:mm:ss.fff");
                    cmd.Execute(info);
                }
            }

            BroadcastCursor(x);
        }

        /// <summary>
        /// 悬浮数据提示:读取鼠标 X 时刻所有可见曲线的值,
        /// 显示"该位置的读数"(多行),是当前时刻的数值而不是裸坐标。
        /// </summary>
        private void UpdateDataTip(double x, double y, string seriesLabel, bool snapped)
        {
            if (!ShowDataTips || _tip == null) return;

            var lines = new List<string>();
            if (_cursorTimeBase != null)
                lines.Add("T = " + _cursorTimeBase.Value.AddSeconds(x).ToString("HH:mm:ss.fff"));

            foreach (var pair in _signals)
            {
                var s = _seriesByKey[pair.Key];
                if (s == null || !s.IsVisible || s.Count == 0) continue;
                var v = ReadSignalValue(s, x);
                if (v != null)
                    lines.Add("● " + pair.Value.LegendText + "  " + v.Value.ToString("0.######", CultureInfo.InvariantCulture));
            }
            foreach (var pair in _scatters)
            {
                var s = _scattersByKey[pair.Key];
                if (s == null || !s.IsVisible || s.MaxIndex < 0) continue;
                var v = ReadScatterValue(pair.Value, x);
                if (v != null)
                    lines.Add("● " + pair.Value.LegendText + "  " + v.Value.ToString("0.######", CultureInfo.InvariantCulture));
            }

            // —— 统计图命中:柱/环/热力显示"鼠标所指图元的值",而不是裸坐标 ——
            foreach (var pair in _extras)
            {
                var ex = _extrasByKey[pair.Key];
                if (ex == null || !ex.IsVisible) continue;
                var name = string.IsNullOrEmpty(ex.Label) ? ex.Key : ex.Label;

                var barPlot = pair.Value as BarPlot;
                if (barPlot != null)
                {
                    foreach (var bar in barPlot.Bars)
                    {
                        if (!bar.IsVisible) continue;
                        var r = bar.AxisLimits;
                        if (x >= r.XRange.Min && x <= r.XRange.Max && y >= r.YRange.Min && y <= r.YRange.Max)
                        {
                            lines.Add("● " + name + "  [" + bar.Position.ToString("0.##", CultureInfo.InvariantCulture) +
                                      "]  =  " + bar.Value.ToString("0.###", CultureInfo.InvariantCulture));
                            break;
                        }
                    }
                    continue;
                }

                var pie = pair.Value as Pie;
                if (pie != null)
                {
                    // 圆心=(0,0),外半径=Radius 的像素值;像素空间做极坐标命中(与渲染同构)
                    var mp = _plot.Plot.GetPixel(new ScottPlot.Coordinates(x, y));
                    var origin = _plot.Plot.GetPixel(new ScottPlot.Coordinates(0, 0));
                    var outer = Math.Min(
                        Math.Abs(_plot.Plot.GetPixel(new ScottPlot.Coordinates(pie.Radius, 0)).X - origin.X),
                        Math.Abs(_plot.Plot.GetPixel(new ScottPlot.Coordinates(0, pie.Radius)).Y - origin.Y));
                    var dx = (double)(mp.X - origin.X);
                    var dy = (double)(mp.Y - origin.Y);
                    var dist = Math.Sqrt(dx * dx + dy * dy);
                    var inner = outer * pie.DonutFraction;
                    if (outer > 0 && dist > inner && dist <= outer)
                    {
                        // Skia 角度:0°=三点钟,正角顺时针;Pie 默认 Rotation = -90°(12 点起)
                        var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                        var rel = (angle - pie.Rotation.Degrees) % 360.0;
                        if (rel < 0) rel += 360.0;
                        var totalValue = pie.Slices.Sum(s => s.Value);
                        var cum = 0.0;
                        foreach (var slice in pie.Slices)
                        {
                            var sweep = totalValue > 0 ? 360.0 * slice.Value / totalValue : 0;
                            if (rel >= cum && rel < cum + sweep)
                            {
                                var pct = totalValue > 0 ? slice.Value / totalValue * 100.0 : 0;
                                lines.Add("● " + slice.Label + "  =  " + slice.Value.ToString("0.##", CultureInfo.InvariantCulture) +
                                          "  (" + pct.ToString("0.#", CultureInfo.InvariantCulture) + "%)");
                                break;
                            }
                            cum += sweep;
                        }
                    }
                    continue;
                }

                var heat = pair.Value as Heatmap;
                if (heat != null)
                {
                    var v = heat.GetValue(new ScottPlot.Coordinates(x, y));
                    if (!double.IsNaN(v))
                        lines.Add("● " + name + "  =  " + v.ToString("0.##", CultureInfo.InvariantCulture) +
                                  "   @ (" + x.ToString("0.##", CultureInfo.InvariantCulture) +
                                  ", " + y.ToString("0.##", CultureInfo.InvariantCulture) + ")");
                    continue;
                }
            }

            if (lines.Count == 0)
                lines.Add("X = " + x.ToString("F3", CultureInfo.InvariantCulture) +
                          "   Y = " + y.ToString("F3", CultureInfo.InvariantCulture));

            _tipText.Text = string.Join("\n", lines);
            _tip.Visibility = Visibility.Visible;

            // 贴在光标右上方;超出右缘时翻到左边(简单保护)
            var left = _lastMousePos.X + 14;
            if (left > ActualWidth - 190) left = _lastMousePos.X - 190;
            var top = Math.Max(2, _lastMousePos.Y - 8);
            _tip.Margin = new Thickness(left, top, 0, 0);
        }

        /// <summary>按时间轴 X 读信号值(等间隔,O(1);含滑窗 XOffset 换算)。</summary>
        private double? ReadSignalValue(ISeriesVm s, double x)
        {
            var idx = (int)Math.Round((x - s.XOffsetSeconds) * s.SampleRate);
            if (idx < 0 || idx >= s.Count) return null;
            lock (s.Lock)
                return s.Buffer[idx];
        }

        /// <summary>按 X 读散点值(用 ScottPlot 的最近 X 查询)。</summary>
        private double? ReadScatterValue(Scatter scatter, double x)
        {
            try
            {
                var dp = scatter.GetNearestX(
                    new ScottPlot.Coordinates(x, 0),
                    _plot.Plot.LastRender, 500);
                return dp.IsReal ? dp.Coordinates.Y : (double?)null;
            }
            catch
            {
                return null;
            }
        }

        #region 双卡尺测量(两条可拖拽竖线 + 常驻 ΔX/ΔY 读数)

        private void EnsureMeasureCursors()
        {
            if (_measureA != null) return;

            // 卡尺线:2.5px 深橙色(太细难命中、难看清);拖拽由本控件自研实现
            _measureA = _plot.Plot.Add.VerticalLine(0, 2.5f,
                new ScottPlot.Color(0xFF, 0x8C, 0x00), ScottPlot.LinePattern.Solid);
            _measureB = _plot.Plot.Add.VerticalLine(0, 2.5f,
                new ScottPlot.Color(0xFF, 0x8C, 0x00), ScottPlot.LinePattern.Dashed);
            _measureA.IsDraggable = true;
            _measureB.IsDraggable = true;
            _plot.Plot.MoveToFront(_measureA);
            _plot.Plot.MoveToFront(_measureB);

            _measurePanelText = new TextBlock
            {
                FontSize = 11,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Left,
            };
            _measurePanel = new Border
            {
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                Padding = new Thickness(7, 4, 7, 4),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xC8, 0x50, 0x3A, 0x0D)),
                Child = _measurePanelText,
            };
            ((Grid)Content).Children.Add(_measurePanel);

            _measureInitialized = false;
            _measureDirty = true;
        }

        private void RemoveMeasureCursors()
        {
            if (_measureA != null) { _plot.Plot.Remove(_measureA); _measureA = null; }
            if (_measureB != null) { _plot.Plot.Remove(_measureB); _measureB = null; }
            if (_measurePanel != null)
            {
                ((Grid)Content).Children.Remove(_measurePanel);
                _measurePanel = null;
            }
            _plot.Refresh();
        }

        /// <summary>
        /// 每帧维护卡尺:首次按当前轴范围放线、检测拖拽回写 DP、
        /// 刷新两线中点的 ΔX/ΔY 常驻读数面板。
        /// </summary>
        private void UpdateMeasureReadout()
        {
            if (_measureA == null || _measureB == null || _measurePanel == null) return;

            if (!_measureInitialized)
            {
                var lim = _plot.Plot.Axes.GetLimits();
                var span = lim.XRange.Max - lim.XRange.Min;
                _measureInitialized = true;
                if (double.IsNaN(MeasureCursorAX) || double.IsNaN(MeasureCursorBX))
                {
                    _measureA.X = lim.XRange.Min + span * 0.25;
                    _measureB.X = lim.XRange.Min + span * 0.75;
                    SetCurrentValue(MeasureCursorAXProperty, _measureA.X);
                    SetCurrentValue(MeasureCursorBXProperty, _measureB.X);
                }
                else
                {
                    _measureA.X = MeasureCursorAX;
                    _measureB.X = MeasureCursorBX;
                }
            }

            // 拖拽检测:用户拖动后把新位置回写 DP(宿主可绑定读取)
            if (!NearlyEqual(_measureA.X, MeasureCursorAX))
            {
                SetCurrentValue(MeasureCursorAXProperty, _measureA.X);
                _plottablesDirty = true;
            }
            if (!NearlyEqual(_measureB.X, MeasureCursorBX))
            {
                SetCurrentValue(MeasureCursorBXProperty, _measureB.X);
                _plottablesDirty = true;
            }

            if (!_measureDirty) return;
            _measureDirty = false;

            var xA = _measureA.X;
            var xB = _measureB.X;
            var dx = Math.Abs(xB - xA);

            var lines = new List<string>
            {
                "ΔX = " + dx.ToString("F4", CultureInfo.InvariantCulture) + (
                    dx > 1e-9 ? "   f = " + (1.0 / dx).ToString("F4", CultureInfo.InvariantCulture) + " Hz" : ""),
            };
            foreach (var pair in _signals)
            {
                var s = _seriesByKey[pair.Key];
                if (s == null || !s.IsVisible || s.Count == 0) continue;
                var va = ReadSignalValue(s, xA);
                var vb = ReadSignalValue(s, xB);
                if (va != null && vb != null)
                    lines.Add("● " + pair.Value.LegendText + "  ΔY = " + (vb.Value - va.Value).ToString("0.######", CultureInfo.InvariantCulture));
            }
            foreach (var pair in _scatters)
            {
                var s = _scattersByKey[pair.Key];
                if (s == null || !s.IsVisible || s.MaxIndex < 0) continue;
                var va = ReadScatterValue(pair.Value, xA);
                var vb = ReadScatterValue(pair.Value, xB);
                if (va != null && vb != null)
                    lines.Add("● " + pair.Value.LegendText + "  ΔY = " + (vb.Value - va.Value).ToString("0.######", CultureInfo.InvariantCulture));
            }
            _measurePanelText.Text = string.Join("\n", lines);
            _measurePanel.Visibility = Visibility.Visible;

            // 面板居中于两线中点上方(像素定位)
            try
            {
                var mid = (xA + xB) / 2.0;
                var lim = _plot.Plot.Axes.GetLimits();
                var px = _plot.Plot.GetPixel(new ScottPlot.Coordinates(mid, lim.YRange.Max));
                var left = Math.Max(4, Math.Min(px.X - 90, ActualWidth - 190));
                _measurePanel.Margin = new Thickness(left, 6, 0, 0);
            }
            catch { }
        }

        #endregion

        /// <summary>右轴十字光标:竖线跟随 X,横线 Y 用右轴刻度(仅 CrosshairOnY2 时显示)。</summary>
        private void UpdateY2Crosshair(double x)
        {
            if (!CrosshairOnY2) return;
            _crosshairY2.X = x;
            _crosshairY2.Y = 0; // 横线位置由右轴刻度决定,竖线跟随 X 即可
            _crosshairY2.IsVisible = true;
            _crosshairY2.VerticalLine.IsVisible = true;
            _crosshairY2.HorizontalLine.IsVisible = false;
        }

        public void RefreshTheme()
        {
            ChartTheme.Reload();
            ApplyTheme();
            _plot.Refresh();
        }

        /// <summary>
        /// 显式设置轴范围(含右轴;传 null 表示不动该轴)。
        /// 供宿主实现"X 随数据增长拉伸""固定 Y 量程"等策略。
        /// </summary>
        public void SetAxisLimits(double xMin, double xMax, double yMin, double yMax,
            double? y2Min = null, double? y2Max = null)
        {
            _plot.Plot.Axes.SetLimits(xMin, xMax, yMin, yMax);
            if (y2Min.HasValue && y2Max.HasValue)
                _plot.Plot.Axes.SetLimitsY(y2Min.Value, y2Max.Value, _plot.Plot.Axes.Right);
            _plot.Refresh();
        }

        #region 鼠标交互

        private void OnPlotMouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(_plot);
            _lastMousePos = pos;
            var dpi = VisualTreeHelper.GetDpi(_plot).PixelsPerDip;
            var coord = _plot.Plot.GetCoordinates(new ScottPlot.Pixel((float)(pos.X * dpi), (float)(pos.Y * dpi)));
            if (!IsFinite(coord.X) || !IsFinite(coord.Y)) return;

            // —— 拖拽中:直接更新目标(卡尺线/阈值线),不进入光标逻辑 ——
            if (_dragTarget != DragTarget.None)
            {
                if (_dragTarget == DragTarget.MeasureA && _measureA != null)
                {
                    _measureA.X = coord.X;
                    _measureDirty = true;
                }
                else if (_dragTarget == DragTarget.MeasureB && _measureB != null)
                {
                    _measureB.X = coord.X;
                    _measureDirty = true;
                }
                else if (_dragTarget == DragTarget.Threshold && _dragHLine != null)
                {
                    var threshold = _dragThreshold;
                    if (threshold != null)
                    {
                        _dragHLine.Y = coord.Y;
                        threshold.Y = coord.Y; // INPC 回传 VM(防回环由阈值线处理器保证)
                    }
                }
                _plot.Refresh();
                return;
            }

            if (!CrosshairEnabled) return;

            // 悬浮提示节流:鼠标 100+Hz 移动时最多 ~30Hz 重算读数。
            // 吸附模式整体按此节流(散点的 GetNearest 在大数组下逐事件执行开销明显);
            // 非吸附路径十字位置仍逐事件跟手,只有提示文本节流。
            var tipDue = (DateTime.UtcNow - _lastTipUtc).TotalMilliseconds >= 30;

            if (SnapToNearest)
            {
                if (!tipDue) return; // 30Hz 吸附率,观感平滑且开销封顶

                // 最近点吸附:在所有可见系列的渲染范围内找 15px 内的最近数据点
                ScottPlot.Coordinates best = new ScottPlot.Coordinates(coord.X, coord.Y);
                string bestLabel = null;
                var bestDist = double.MaxValue;
                var location = new ScottPlot.Coordinates(coord.X, coord.Y);

                foreach (var pair in _signals)
                {
                    if (!pair.Value.IsVisible) continue;
                    var dp = pair.Value.GetNearest(location, _plot.Plot.LastRender, 15);
                    if (!dp.IsReal) continue;
                    var px = _plot.Plot.GetPixel(dp.Coordinates);
                    var dist = (px.X - pos.X) * (px.X - pos.X) + (px.Y - pos.Y) * (px.Y - pos.Y);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = dp.Coordinates;
                        bestLabel = pair.Value.LegendText;
                    }
                }
                foreach (var pair in _scatters)
                {
                    if (!pair.Value.IsVisible) continue;
                    var dp = pair.Value.GetNearest(location, _plot.Plot.LastRender, 15);
                    if (!dp.IsReal) continue;
                    var px = _plot.Plot.GetPixel(dp.Coordinates);
                    var dist = (px.X - pos.X) * (px.X - pos.X) + (px.Y - pos.Y) * (px.Y - pos.Y);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = dp.Coordinates;
                        bestLabel = pair.Value.LegendText;
                    }
                }

                PlaceCursorInternal(best.X, best.Y, bestLabel, bestLabel != null, true);
                return;
            }

            PlaceCursorInternal(coord.X, coord.Y, null, false, tipDue);
        }

        private static bool IsFinite(double v)
        {
            return !double.IsNaN(v) && !double.IsInfinity(v);
        }

        private void OnPlotMouseLeave(object sender, MouseEventArgs e)
        {
            EndDrag();
            _crosshair.IsVisible = false;
            _crosshairY2.IsVisible = false;
            _mouseDown = false;
            if (_tip != null) _tip.Visibility = Visibility.Collapsed;
        }

        private void OnPlotMouseWheel(object sender, MouseWheelEventArgs e)
        {
            _lastInteractionUtc = DateTime.UtcNow;
            PushLimits();
        }

        private void OnPlotMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ViewLimits = null;
            _userFollow = true;
            UpdateChip();
            _plot.Plot.Axes.AutoScale();
            _plot.Refresh();
        }

        /// <summary>
        /// 按下时命中测试可拖拽目标(卡尺线 ±8px / 可拖拽阈值线 ±8px),
        /// 命中后本控件接管拖拽并临时禁用 ScottPlot 自带交互(pan/zoom),
        /// 避免"拖线同时全景平移"。
        /// </summary>
        private void OnPlotMouseLeftDown(object sender, MouseButtonEventArgs e)
        {
            _mouseDown = true;

            var pos = e.GetPosition(_plot);
            var dpi = VisualTreeHelper.GetDpi(_plot).PixelsPerDip;
            var pixel = new ScottPlot.Pixel((float)(pos.X * dpi), (float)(pos.Y * dpi));

            try
            {
                if (_measureA != null)
                {
                    var pxA = _plot.Plot.GetPixel(new ScottPlot.Coordinates(_measureA.X, 0));
                    if (Math.Abs(pxA.X - pixel.X) < 8)
                    {
                        BeginDrag(DragTarget.MeasureA);
                        e.Handled = true; // 阻止 ScottPlot 自带 pan 启动
                        return;
                    }
                }
                if (_measureB != null)
                {
                    var pxB = _plot.Plot.GetPixel(new ScottPlot.Coordinates(_measureB.X, 0));
                    if (Math.Abs(pxB.X - pixel.X) < 8)
                    {
                        BeginDrag(DragTarget.MeasureB);
                        e.Handled = true;
                        return;
                    }
                }
                foreach (var pair in _annotations)
                {
                    var threshold = pair.Value as ThresholdLineVm;
                    var hLine = _annotations[pair.Key] as HorizontalLine;
                    if (threshold == null || hLine == null || !threshold.IsDraggable) continue;
                    var px = _plot.Plot.GetPixel(new ScottPlot.Coordinates(0, hLine.Y));
                    if (Math.Abs(px.Y - pixel.Y) < 8)
                    {
                        _dragHLine = hLine;
                        _dragThreshold = threshold;
                        BeginDrag(DragTarget.Threshold);
                        e.Handled = true;
                        return;
                    }
                }
            }
            catch { }
        }

        private void BeginDrag(DragTarget target)
        {
            _dragTarget = target;
            // 按下事件已被 Preview 阶段标记 Handled,ScottPlot 自带 pan 不会启动
            _lastInteractionUtc = DateTime.UtcNow;
        }

        private void EndDrag()
        {
            if (_dragTarget == DragTarget.None) return;
            _dragTarget = DragTarget.None;
            _dragHLine = null;
            _dragThreshold = null;
            _plottablesDirty = true;
        }

        private void OnPlotMouseLeftUp(object sender, MouseButtonEventArgs e)
        {
            _mouseDown = false;
            EndDrag();
            PushLimits();
        }

        private static bool NearlyEqual(double a, double b)
        {
            return Math.Abs(a - b) <= Math.Max(Math.Abs(a), Math.Abs(b)) * 1e-6;
        }

        private void PushLimits()
        {
            var lim = _plot.Plot.Axes.GetLimits();
            var current = ViewLimits;
            if (current != null
                && NearlyEqual(current.XMin, lim.XRange.Min) && NearlyEqual(current.XMax, lim.XRange.Max)
                && NearlyEqual(current.YMin, lim.YRange.Min) && NearlyEqual(current.YMax, lim.YRange.Max))
                return;
            var limits = new ChartViewLimits
            {
                XMin = lim.XRange.Min,
                XMax = lim.XRange.Max,
                YMin = lim.YRange.Min,
                YMax = lim.YRange.Max,
            };
            ViewLimits = limits;
            if (LinkedAxis) BroadcastLimits(limits);
        }

        #endregion

        #region 跨图光标同步

        private static readonly Dictionary<string, List<WeakReference<ChartView>>> SyncGroups =
            new Dictionary<string, List<WeakReference<ChartView>>>();

        private static void OnSyncGroupChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (ChartView)d;
            if (view.IsLoaded) view.JoinSyncGroup();
        }

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
            if (!SyncEnabled) return;
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

        private void ReceiveCursorX(double x)
        {
            if (!SyncEnabled) return;
            _crosshair.X = x;
            _crosshair.IsVisible = true;
            UpdateY2Crosshair(x);
            _plottablesDirty = true; // 下一帧重绘出竖线
        }

        /// <summary>轴范围同步:把范围广播给同组其它图(LinkedAxis 开启时由宿主缩放/设置触发)。</summary>
        private void BroadcastLimits(ChartViewLimits limits)
        {
            var group = SyncGroup;
            if (string.IsNullOrEmpty(group)) return;

            lock (SyncGroups)
            {
                List<WeakReference<ChartView>> members;
                if (!SyncGroups.TryGetValue(group, out members)) return;

                for (var i = members.Count - 1; i >= 0; i--)
                {
                    ChartView other;
                    if (!members[i].TryGetTarget(out other))
                    {
                        members.RemoveAt(i);
                        continue;
                    }
                    if (!ReferenceEquals(other, this) && other.LinkedAxis)
                        other.ReceiveLimits(limits);
                }
                // 成员全部释放后移除空组条目,避免同步组字典残留
                if (members.Count == 0) SyncGroups.Remove(group);
            }
        }

        /// <summary>接收轴范围同步(不回发,避免广播环)。</summary>
        private void ReceiveLimits(ChartViewLimits limits)
        {
            _isReceivingLimits = true;
            try
            {
                SetCurrentValue(ViewLimitsProperty, limits);
            }
            finally
            {
                _isReceivingLimits = false;
            }
        }

        #endregion

        #region 系列/注释 ↔ Plottable 适配

        /// <summary>
        /// 单个系列 VM 绑到 IEnumerable 型 DP 时会静默失败(WPF 绑定不支持类型不匹配),
        /// 这里把"单个对象"归一化成单元素数组后再走同步管线。
        /// </summary>
        private static IEnumerable NormalizeSource(object value, Type singleType)
        {
            if (value == null) return null;
            if (singleType.IsInstanceOfType(value)) return new[] { value };
            return value as IEnumerable;
        }

        private static void OnSeriesSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (ChartView)d;
            var normalized = NormalizeSource(e.NewValue, typeof(ISeriesVm));
            view._seriesSourceCurrent = normalized;
            view.AttachSource(e.OldValue as IEnumerable, normalized);
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

        /// <summary>全量重挂 VM 订阅(先解后挂,幂等;Loaded 时调用)。</summary>
        private void AttachAllSources()
        {
            foreach (var s in _seriesByKey.Values)
            {
                s.PropertyChanged -= OnSeriesPropertyChanged;
                s.PropertyChanged += OnSeriesPropertyChanged;
            }
            foreach (var s in _scattersByKey.Values)
            {
                s.PropertyChanged -= OnScatterPropertyChanged;
                s.PropertyChanged += OnScatterPropertyChanged;
            }
            foreach (var a in _annotationsByKey.Values)
            {
                a.PropertyChanged -= OnAnnotationPropertyChanged;
                a.PropertyChanged += OnAnnotationPropertyChanged;
            }
            foreach (var g in _gaugesByKey.Values)
            {
                g.PropertyChanged -= OnGaugePropertyChanged;
                g.PropertyChanged += OnGaugePropertyChanged;
            }
            foreach (var x in _extrasByKey.Values)
            {
                x.PropertyChanged -= OnExtraPropertyChanged;
                x.PropertyChanged += OnExtraPropertyChanged;
            }
        }

        /// <summary>全量解绑 VM 订阅(Unloaded 时调用,防泄漏)。</summary>
        private void DetachAllSources()
        {
            foreach (var s in _seriesByKey.Values)
                s.PropertyChanged -= OnSeriesPropertyChanged;
            foreach (var s in _scattersByKey.Values)
                s.PropertyChanged -= OnScatterPropertyChanged;
            foreach (var a in _annotationsByKey.Values)
                a.PropertyChanged -= OnAnnotationPropertyChanged;
            foreach (var g in _gaugesByKey.Values)
                g.PropertyChanged -= OnGaugePropertyChanged;
            foreach (var x in _extrasByKey.Values)
                x.PropertyChanged -= OnExtraPropertyChanged;
        }

        private void RebindSeries()
        {
            var wanted = new Dictionary<string, ISeriesVm>();
            var wantedScatters = new Dictionary<string, IScatterSeriesVm>();
            if (_seriesSourceCurrent != null)
            {
                foreach (var item in _seriesSourceCurrent)
                {
                    var s = item as ISeriesVm;
                    if (s != null) { if (!wanted.ContainsKey(s.Key)) wanted[s.Key] = s; continue; }
                    var sc = item as IScatterSeriesVm;
                    if (sc != null && !wantedScatters.ContainsKey(sc.Key)) wantedScatters[sc.Key] = sc;
                }
            }

            foreach (var gone in _seriesByKey.Keys.Where(k => !wanted.ContainsKey(k)).ToList())
            {
                Signal signal;
                if (_signals.TryGetValue(gone, out signal))
                {
                    _plot.Plot.Remove(signal);
                    _signals.Remove(gone);
                }
                _seriesByKey[gone].PropertyChanged -= OnSeriesPropertyChanged;
                _seriesByKey.Remove(gone);
            }

            foreach (var gone in _scattersByKey.Keys.Where(k => !wantedScatters.ContainsKey(k)).ToList())
            {
                Scatter scatter;
                if (_scatters.TryGetValue(gone, out scatter))
                {
                    _plot.Plot.Remove(scatter);
                    _scatters.Remove(gone);
                }
                _scattersByKey[gone].PropertyChanged -= OnScatterPropertyChanged;
                _scattersByKey.Remove(gone);
            }

            foreach (var pair in wanted)
            {
                if (_seriesByKey.ContainsKey(pair.Key)) continue;
                _seriesByKey[pair.Key] = pair.Value;
                pair.Value.PropertyChanged -= OnSeriesPropertyChanged;
                pair.Value.PropertyChanged += OnSeriesPropertyChanged;
            }

            foreach (var pair in wantedScatters)
            {
                if (_scattersByKey.ContainsKey(pair.Key)) continue;
                _scattersByKey[pair.Key] = pair.Value;
                pair.Value.PropertyChanged -= OnScatterPropertyChanged;
                pair.Value.PropertyChanged += OnScatterPropertyChanged;
            }

            _plottablesDirty = true;
        }

        private void OnSeriesPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnSeriesPropertyChanged(sender, e)));
                return;
            }

            var s = sender as ISeriesVm;
            if (s == null) return;
            Signal signal;
            if (!_signals.TryGetValue(s.Key, out signal)) return;

            if (e.PropertyName == "IsVisible") signal.IsVisible = s.IsVisible;
            else if (e.PropertyName == "Label") signal.LegendText = s.Label;
            else if (e.PropertyName == "Color") signal.Color = ChartTheme.ToSP(s.Color);
            else if (e.PropertyName == "LineWidth") signal.LineWidth = (float)s.LineWidth;
            else if (e.PropertyName == "LineType") signal.LinePattern = MapPattern(s.LineType);
            _plottablesDirty = true; // 脏标记合并进 30ms 渲染帧(高频改属性不触发多次全图重绘)
        }

        private void OnScatterPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnScatterPropertyChanged(sender, e)));
                return;
            }

            var s = sender as IScatterSeriesVm;
            if (s == null) return;
            Scatter scatter;
            if (!_scatters.TryGetValue(s.Key, out scatter)) return;

            if (e.PropertyName == "Data")
            {
                // SetData 整组替换:重建对应曲线(新旧数组长度/引用都变了)
                _plot.Plot.Remove(scatter);
                _scatters.Remove(s.Key);
                _plottablesDirty = true;
                return;
            }

            if (e.PropertyName == "IsVisible") scatter.IsVisible = s.IsVisible;
            else if (e.PropertyName == "Label") scatter.LegendText = s.Label;
            else if (e.PropertyName == "Color") scatter.Color = ChartTheme.ToSP(s.Color);
            else if (e.PropertyName == "LineWidth") scatter.LineWidth = (float)s.LineWidth;
            else if (e.PropertyName == "LineType") scatter.LinePattern = MapPattern(s.LineType);
            else if (e.PropertyName == "MarkerSize")
            {
                scatter.MarkerSize = s.MarkerSize;
                scatter.MarkerShape = s.MarkerSize > 0 ? ScottPlot.MarkerShape.FilledCircle : ScottPlot.MarkerShape.None;
            }
            _plottablesDirty = true; // 合并进渲染帧
        }

        private static ScottPlot.LinePattern MapPattern(ChartLineType type)
        {
            switch (type)
            {
                case ChartLineType.Dash: return ScottPlot.LinePattern.Dashed;
                case ChartLineType.Dot: return ScottPlot.LinePattern.Dotted;
                default: return ScottPlot.LinePattern.Solid;
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
                view._plot.Plot.Axes.Right.IsVisible = true;
                var rightLabel = view._plot.Plot.Axes.Right.Label;
                rightLabel.Text = view.Y2Label;
            }
            view._plot.Plot.Legend.IsVisible = view.ShowLegend;
            view._plot.Refresh();
        }

        private static void OnViewLimitsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (ChartView)d;
            var limits = e.NewValue as ChartViewLimits;
            if (limits != null)
            {
                view._plot.Plot.Axes.SetLimits(limits.XMin, limits.XMax, limits.YMin, limits.YMax);
                view._plot.Refresh();

                // 轴范围同步:VM/宿主设置也广播(接收途中的不广播,防环)
                if (view.LinkedAxis && !view._isReceivingLimits)
                    view.BroadcastLimits(limits);
            }
        }

        #region 仪表盘

        private static void OnGaugesSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (ChartView)d;
            view._gaugesSourceCurrent = NormalizeSource(e.NewValue, typeof(GaugeVm));

            var oldNotifier = e.OldValue as INotifyCollectionChanged;
            if (oldNotifier != null) oldNotifier.CollectionChanged -= view.OnGaugesCollectionChanged;
            var newNotifier = e.NewValue as INotifyCollectionChanged;
            if (newNotifier != null) newNotifier.CollectionChanged += view.OnGaugesCollectionChanged;

            view.SyncGauges();
        }

        private void OnGaugesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            SyncGauges();
        }

        /// <summary>按 Key 差分同步仪表,并重建值数组(RadialGaugePlot 原址读取)。</summary>
        private void SyncGauges()
        {
            var wanted = new List<GaugeVm>();
            if (_gaugesSourceCurrent != null)
            {
                foreach (var item in _gaugesSourceCurrent)
                {
                    var g = item as GaugeVm;
                    if (g != null && wanted.All(w => w.Key != g.Key)) wanted.Add(g);
                }
            }

            foreach (var gone in _gaugesByKey.Values.Where(g => wanted.All(w => w.Key != g.Key)).ToList())
            {
                gone.PropertyChanged -= OnGaugePropertyChanged;
                _gaugesByKey.Remove(gone.Key);
            }

            foreach (var g in wanted)
            {
                if (_gaugesByKey.ContainsKey(g.Key)) continue;
                _gaugesByKey[g.Key] = g;
                g.PropertyChanged -= OnGaugePropertyChanged;
                g.PropertyChanged += OnGaugePropertyChanged;
            }

            if (_gaugesByKey.Count == 0)
            {
                if (_gauges != null)
                {
                    _plot.Plot.Remove(_gauges);
                    _gauges = null;
                    _plottablesDirty = true;
                }
                return;
            }

            // 数量变化时重建;Levels 数组是只读引用,原址改值驱动仪表摆动
            if (_gauges == null || _gauges.GaugeCount != _gaugesByKey.Count)
            {
                if (_gauges != null) _plot.Plot.Remove(_gauges);
                _gauges = _plot.Plot.Add.RadialGaugePlot(new double[_gaugesByKey.Count]);
                _gauges.ShowLevels = true;
                // 纯仪表图隐藏网格与坐标轴
                if (_seriesByKey.Count == 0 && _scattersByKey.Count == 0)
                    _plot.Plot.HideAxesAndGrid();
                _plottablesDirty = true;
            }

            _gaugeValues = _gauges.Levels;
            var valueIndex = 0;
            foreach (var g in _gaugesByKey.Values)
            {
                _gaugeValues[valueIndex] = g.Value;
                valueIndex++;
            }
            _gauges.Labels = _gaugesByKey.Values.Select(g => g.Label).ToArray();
            _gauges.Colors = _gaugesByKey.Values.Select(g => ChartTheme.ToSP(g.Color)).ToArray();
            _plot.Refresh();
        }

        private void OnGaugePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnGaugePropertyChanged(sender, e)));
                return;
            }

            var g = sender as GaugeVm;
            if (g == null) return;

            if (e.PropertyName == "Value" && _gauges != null && _gaugeValues != null)
            {
                // 值数组原址更新(RadialGaugePlot 持有同一引用),标记下一帧重绘
                var idx = 0;
                foreach (var kv in _gaugesByKey)
                {
                    if (ReferenceEquals(kv.Value, g)) { _gaugeValues[idx] = g.Value; break; }
                    idx++;
                }
                _gaugesDirty = true;
            }
            else if (e.PropertyName == "IsVisible" && _gauges != null)
            {
                _gauges.IsVisible = _gaugesByKey.Values.All(v => v.IsVisible);
                _plottablesDirty = true; // 合并进渲染帧
            }
        }

        #endregion

        private void ApplyTheme()
        {
            // ScottPlot 5 默认字体无中文字形(标题/轴标签渲染成方块),需指定中文字体
            try
            {
                _plot.Plot.Font.Set("Microsoft YaHei UI");
            }
            catch
            {
                try { _plot.Plot.Font.Set("SimSun"); } catch { }
            }
            _plot.Plot.FigureBackground.Color = ChartTheme.ToSP(ChartTheme.FigureBackground);
            _plot.Plot.DataBackground.Color = ChartTheme.ToSP(ChartTheme.DataBackground);
            _plot.Plot.Axes.Color(ChartTheme.ToSP(ChartTheme.Tick));
            _plot.Plot.Grid.MajorLineColor = ChartTheme.ToSP(ChartTheme.Grid);
        }

        private static void OnAnnotationsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (ChartView)d;
            view._annotationsSourceCurrent = NormalizeSource(e.NewValue, typeof(IChartAnnotation));

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

        private void SyncAnnotations()
        {
            var wanted = new Dictionary<string, IChartAnnotation>();
            if (_annotationsSourceCurrent != null)
            {
                foreach (var item in _annotationsSourceCurrent)
                {
                    var a = item as IChartAnnotation;
                    if (a != null && !wanted.ContainsKey(a.Key)) wanted[a.Key] = a;
                }
            }

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
            var pattern = MapPattern(ann.LineType);
            var color = ChartTheme.ToSP(ann.Color);

            var threshold = ann as ThresholdLineVm;
            if (threshold != null)
            {
                var hLine = _plot.Plot.Add.HorizontalLine(threshold.Y, (float)threshold.LineWidth, color, pattern);
                hLine.LegendText = ann.Label;
                hLine.IsVisible = ann.IsVisible;
                hLine.IsDraggable = threshold.IsDraggable;
                return hLine;
            }

            var marker = ann as EventMarkerVm;
            if (marker != null)
            {
                var vLine = _plot.Plot.Add.VerticalLine(marker.X, (float)marker.LineWidth, color, pattern);
                vLine.LegendText = ann.Label;
                vLine.IsVisible = ann.IsVisible;
                return vLine;
            }

            throw new NotSupportedException("未知注释类型:" + ann.GetType().Name);
        }

        private void OnAnnotationPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnAnnotationPropertyChanged(sender, e)));
                return;
            }

            var ann = sender as IChartAnnotation;
            if (ann == null) return;
            IPlottable plottable;
            if (!_annotations.TryGetValue(ann.Key, out plottable)) return;

            var hLine = plottable as HorizontalLine;
            var vLine = plottable as VerticalLine;
            var pattern = MapPattern(ann.LineType);
            var color = ChartTheme.ToSP(ann.Color);

            if (hLine != null)
            {
                var threshold = (ThresholdLineVm)ann;
                hLine.Y = threshold.Y;
                hLine.Color = color;
                hLine.LinePattern = pattern;
                hLine.LineWidth = (float)threshold.LineWidth;
                hLine.IsVisible = ann.IsVisible;
            }
            else if (vLine != null)
            {
                var marker = (EventMarkerVm)ann;
                vLine.X = marker.X;
                vLine.Color = color;
                vLine.LinePattern = pattern;
                vLine.LineWidth = (float)marker.LineWidth;
                vLine.IsVisible = ann.IsVisible;
            }

            _plottablesDirty = true; // 合并进渲染帧(阈值线连续拖动也不会逐事件重绘)
        }

        #endregion

        #region 扩展图形(柱状/饼图/直方图/函数/填充/热力图)

        private readonly Dictionary<string, IPlottable> _extras = new Dictionary<string, IPlottable>();
        private readonly Dictionary<string, IExtraSeriesVm> _extrasByKey = new Dictionary<string, IExtraSeriesVm>();

        private static void OnExtrasSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (ChartView)d;
            view._extrasSourceCurrent = NormalizeSource(e.NewValue, typeof(IExtraSeriesVm));

            var oldNotifier = e.OldValue as INotifyCollectionChanged;
            if (oldNotifier != null) oldNotifier.CollectionChanged -= view.OnExtrasCollectionChanged;
            var newNotifier = e.NewValue as INotifyCollectionChanged;
            if (newNotifier != null) newNotifier.CollectionChanged += view.OnExtrasCollectionChanged;

            view.SyncExtras();
        }

        private void OnExtrasCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            SyncExtras();
        }

        /// <summary>按 Key 差分同步扩展图形,每种类型一个创建分支。</summary>
        private void SyncExtras()
        {
            var wanted = new Dictionary<string, IExtraSeriesVm>();
            if (_extrasSourceCurrent != null)
            {
                foreach (var item in _extrasSourceCurrent)
                {
                    var ex = item as IExtraSeriesVm;
                    if (ex != null && !wanted.ContainsKey(ex.Key)) wanted[ex.Key] = ex;
                }
            }
            DiagnosticsLog("SyncExtras: 来源=" + (_extrasSourceCurrent != null ? "有" : "null") + " 期望=" + wanted.Count + " 已有=" + _extras.Count);

            foreach (var gone in _extrasByKey.Keys.Where(k => !wanted.ContainsKey(k)).ToList())
            {
                IPlottable plottable;
                if (_extras.TryGetValue(gone, out plottable))
                {
                    _plot.Plot.Remove(plottable);
                    _extras.Remove(gone);
                }
                _extrasByKey[gone].PropertyChanged -= OnExtraPropertyChanged;
                _extrasByKey.Remove(gone);
            }

            foreach (var pair in wanted)
            {
                if (_extrasByKey.ContainsKey(pair.Key)) continue;
                try
                {
                    // 空数据/非法参数不应让整张图废掉:创建失败记日志并跳过该图形
                    _extrasByKey[pair.Key] = pair.Value;
                    pair.Value.PropertyChanged -= OnExtraPropertyChanged;
                    pair.Value.PropertyChanged += OnExtraPropertyChanged;
                    _extras[pair.Key] = CreateExtraPlottable(pair.Value);
                    _plottablesDirty = true;
                }
                catch (Exception ex)
                {
                    DiagnosticsLog("创建扩展图形失败(" + pair.Key + "):" + ex.Message);
                }
            }

            // 纯饼图(环形图)没有坐标轴语义:自动隐藏轴与网格,条件解除后恢复
            var pieOnly = _seriesByKey.Count == 0 && _scattersByKey.Count == 0 && _gauges == null
                          && _extrasByKey.Count > 0
                          && _extrasByKey.Values.All(x => x is PieSeriesVm);
            if (pieOnly && !_autoHidAxes)
            {
                _plot.Plot.HideAxesAndGrid();
                _autoHidAxes = true;
                _plottablesDirty = true;
            }
            else if (!pieOnly && _autoHidAxes)
            {
                _plot.Plot.ShowAxesAndGrid();
                _autoHidAxes = false;
                _plottablesDirty = true;
            }
        }

        private IPlottable CreateExtraPlottable(IExtraSeriesVm vm)
        {
            // 柱状图
            var bars = vm as BarSeriesVm;
            if (bars != null)
            {
                var barPlot = _plot.Plot.Add.Bars(bars.Values);
                barPlot.LegendText = bars.Label;
                barPlot.Color = ChartTheme.ToSP(bars.Color);
                barPlot.IsVisible = bars.IsVisible;
                if (bars.Positions != null)
                {
                    for (var i = 0; i < barPlot.Bars.Count && i < bars.Positions.Length; i++)
                        barPlot.Bars[i].Position = bars.Positions[i];
                }
                return barPlot;
            }

            // 饼图/环图
            var pie = vm as PieSeriesVm;
            if (pie != null)
            {
                var values = pie.Slices.Select(s => s.Value);
                var piePlot = _plot.Plot.Add.Pie(values);
                piePlot.IsVisible = pie.IsVisible;
                piePlot.DonutFraction = pie.DonutFraction;
                var sliceList = pie.Slices.ToList();
                for (var i = 0; i < piePlot.Slices.Count && i < sliceList.Count; i++)
                {
                    piePlot.Slices[i].Label = sliceList[i].Label;
                    piePlot.Slices[i].LegendText = sliceList[i].Label;
                    piePlot.Slices[i].FillColor = ChartTheme.ToSP(sliceList[i].Color);
                }
                return piePlot;
            }

            // 直方图(手动等宽分箱,画成柱状;X 位置 = 各箱中心)
            var hist = vm as HistogramSeriesVm;
            if (hist != null)
            {
                var values = hist.Values;
                var min = values.Length > 0 ? values.Min() : 0;
                var max = values.Length > 0 ? values.Max() : 1;
                var binWidth = (max - min) / hist.BinCount;
                if (binWidth <= 0) binWidth = 1;
                var counts = new double[hist.BinCount];
                foreach (var v in values)
                {
                    var idx = (int)((v - min) / binWidth);
                    if (idx >= hist.BinCount) idx = hist.BinCount - 1;
                    counts[idx]++;
                }
                var barPlot = _plot.Plot.Add.Bars(counts);
                barPlot.LegendText = hist.Label;
                barPlot.Color = ChartTheme.ToSP(hist.Color);
                barPlot.IsVisible = hist.IsVisible;
                for (var i = 0; i < barPlot.Bars.Count; i++)
                    barPlot.Bars[i].Position = min + (i + 0.5) * binWidth;
                return barPlot;
            }

            // 函数曲线
            var func = vm as FunctionSeriesVm;
            if (func != null)
            {
                var funcPlot = _plot.Plot.Add.Function(func.Function);
                funcPlot.LegendText = func.Label;
                funcPlot.LineColor = ChartTheme.ToSP(func.Color);
                funcPlot.LineWidth = (float)func.LineWidth;
                funcPlot.LinePattern = MapPattern(func.LineType);
                funcPlot.IsVisible = func.IsVisible;
                return funcPlot;
            }

            // 区域填充
            var fill = vm as FillYSeriesVm;
            if (fill != null)
            {
                var fillPlot = _plot.Plot.Add.FillY(fill.Xs, fill.YsTop, fill.YsBottom);
                fillPlot.LegendText = fill.Label;
                fillPlot.IsVisible = fill.IsVisible;
                fillPlot.FillColor = ChartTheme.ToSP(fill.FillColor);
                fillPlot.LineColor = ChartTheme.ToSP(fill.FillColor);
                return fillPlot;
            }

            // 热力图
            var heat = vm as HeatmapSeriesVm;
            if (heat != null)
            {
                var heatmap = _plot.Plot.Add.Heatmap(heat.Intensities);
                heatmap.IsVisible = heat.IsVisible;
                heatmap.Smooth = heat.Smooth;
                heatmap.Colormap = MapColormap(heat.ColormapName);
                return heatmap;
            }

            throw new NotSupportedException("未知扩展图形类型:" + vm.GetType().Name);
        }

        private static ScottPlot.IColormap MapColormap(string name)
        {
            switch (name)
            {
                case "Plasma": return new ScottPlot.Colormaps.Plasma();
                case "Inferno": return new ScottPlot.Colormaps.Inferno();
                case "Turbo": return new ScottPlot.Colormaps.Turbo();
                case "Magma": return new ScottPlot.Colormaps.Magma();
                default: return new ScottPlot.Colormaps.Viridis();
            }
        }

        private void OnExtraPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => OnExtraPropertyChanged(sender, e)));
                return;
            }

            var vm = sender as IExtraSeriesVm;
            if (vm == null) return;
            IPlottable plottable;
            if (!_extras.TryGetValue(vm.Key, out plottable)) return;

            if (e.PropertyName == "IsVisible") plottable.IsVisible = vm.IsVisible;
            else if (e.PropertyName == "Data")
            {
                // 饼图扇区值变化:按索引同步回 Slice(数量不变的场景)
                var pie = vm as PieSeriesVm;
                var piePlot = plottable as Pie;
                if (pie != null && piePlot != null)
                {
                    var slices = pie.Slices.ToList();
                    for (var i = 0; i < piePlot.Slices.Count && i < slices.Count; i++)
                        piePlot.Slices[i].Value = slices[i].Value;
                }

                var heat = vm as HeatmapSeriesVm;
                if (heat != null) { /* Intensities 原址更新,无需处理 */ }
            }
            _plottablesDirty = true; // 合并进渲染帧(热力图每 200ms 更新不再逐次全图重绘)
        }

        #endregion

        #region 渲染循环(全局调度 + 增量推送 + 兜底)

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

            var maxVersion = 0;
            foreach (var s in _seriesByKey.Values)
                if (s.Version > maxVersion) maxVersion = s.Version;
            foreach (var sc in _scattersByKey.Values)
                if (sc.Version > maxVersion) maxVersion = sc.Version;

            bool dataDirty = maxVersion != _lastVersion;
            bool needRender = _plottablesDirty || dataDirty;

            // 延迟建 Signal:系列有数据但还没上图的(实时流从空开始的场景)。
            // Signal 直接引用 VM 的缓冲区,数据原址更新,无需任何拷贝/推送。
            foreach (var s in _seriesByKey.Values)
            {
                if (s.Count > 0 && !_signals.ContainsKey(s.Key))
                {
                    var color = s.Color.A == 0 ? ChartTheme.AutoColor(_paletteIndex++) : s.Color;
                    var signal = _plot.Plot.Add.Signal(s.Buffer, 1.0 / s.SampleRate);
                    signal.LegendText = s.Label;
                    signal.Color = ChartTheme.ToSP(color);
                    signal.IsVisible = s.IsVisible;
                    signal.LineWidth = (float)s.LineWidth;
                    signal.LinePattern = MapPattern(s.LineType);
                    signal.MaxRenderIndex = s.Count - 1;
                    if (s.YAxisIndex == 1)
                    {
                        signal.Axes.YAxis = _plot.Plot.Axes.Right;
                        _plot.Plot.Axes.Right.IsVisible = true;
                    }
                    _signals[s.Key] = signal;
                    _plottablesDirty = true;
                }
            }

            // 延迟建 Scatter:XY 数组系列(外部按索引填充数据,MaxIndex 推进上界)
            foreach (var sc in _scattersByKey.Values)
            {
                if (sc.MaxIndex >= 0 && !_scatters.ContainsKey(sc.Key))
                {
                    var color = sc.Color.A == 0 ? ChartTheme.AutoColor(_paletteIndex++) : sc.Color;
                    var scatter = _plot.Plot.Add.Scatter(sc.Xs, sc.Ys);
                    scatter.LegendText = sc.Label;
                    scatter.Color = ChartTheme.ToSP(color);
                    scatter.IsVisible = sc.IsVisible;
                    scatter.LineWidth = (float)sc.LineWidth;
                    scatter.LinePattern = MapPattern(sc.LineType);
                    scatter.MarkerSize = sc.MarkerSize;
                    scatter.MarkerShape = sc.MarkerSize > 0 ? ScottPlot.MarkerShape.FilledCircle : ScottPlot.MarkerShape.None;
                    scatter.MaxRenderIndex = sc.MaxIndex;
                    if (sc.YAxisIndex == 1)
                    {
                        scatter.Axes.YAxis = _plot.Plot.Axes.Right;
                        _plot.Plot.Axes.Right.IsVisible = true;
                    }
                    _scatters[sc.Key] = scatter;
                    _plottablesDirty = true;
                }
            }

            // 数据增长时同步渲染上界(Signal/Scatter 禁止渲染未写入区间)
            foreach (var pair in _signals)
            {
                var seriesCount = _seriesByKey[pair.Key].Count;
                if (seriesCount > 0 && pair.Value.MaxRenderIndex != seriesCount - 1)
                    pair.Value.MaxRenderIndex = seriesCount - 1;

                // 滑窗 X 偏移:写满后缓冲区起点随时间滚动,
                // 不设偏移曲线会整体向左错位(时间轴正确但曲线位置错)
                var xOffset = _seriesByKey[pair.Key].XOffsetSeconds;
                if (pair.Value.Data != null && pair.Value.Data.XOffset != xOffset)
                    pair.Value.Data.XOffset = xOffset;
            }
            foreach (var pair in _scatters)
            {
                var scatterVm = _scattersByKey[pair.Key];
                var scatterMax = scatterVm.MaxIndex;
                if (scatterMax >= 0)
                {
                    // 越界护栏:MaxIndex 不允许超出数组长度,否则渲染抛异常
                    var maxAllowed = Math.Min(scatterVm.Xs.Length, scatterVm.Ys.Length) - 1;
                    var clamped = Math.Min(scatterMax, maxAllowed);
                    if (pair.Value.MaxRenderIndex != clamped)
                        pair.Value.MaxRenderIndex = clamped;
                }
            }

            // 可拖拽阈值线:用户拖动后把新 Y 回传 VM(INPC 双向闭环)
            foreach (var pair in _annotationsByKey)
            {
                var threshold = pair.Value as ThresholdLineVm;
                if (threshold == null || !threshold.IsDraggable) continue;
                var hLine = _annotations[pair.Key] as HorizontalLine;
                if (hLine != null && !NearlyEqual(hLine.Y, threshold.Y))
                    threshold.Y = hLine.Y;
            }

            // 十字光标置于最上层(先于系列创建,默认会被曲线盖住)
            _plot.Plot.MoveToFront(_crosshair);
            if (CrosshairOnY2) _plot.Plot.MoveToFront(_crosshairY2);

            if (_gaugesDirty)
            {
                _gaugesDirty = false;
                needRender = true;
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
                    var tickGen = _plot.Plot.Axes.Bottom.TickGenerator as ScottPlot.TickGenerators.NumericAutomatic;
                    if (tickGen != null)
                        tickGen.LabelFormatter = x => start.AddSeconds(x).ToString("HH:mm:ss");
                }
                _plottablesDirty = true;
            }

            // 复用缓冲:每帧过滤可见系列,避免每秒数十次的小 List 分配
            _visibleSeriesBuffer.Clear();
            foreach (var s in _seriesByKey.Values)
                if (s.Count > 0 && s.IsVisible)
                    _visibleSeriesBuffer.Add(s);
            var series = _visibleSeriesBuffer;

            // 双卡尺:每帧维护(首次放线/拖拽回写/读数面板定位)
            if (_measureA != null)
                UpdateMeasureReadout();

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
                _plot.Plot.Axes.SetLimits(xMin, xMax, y1Min - p1, y1Max + p1);
                if (y2Min != double.MaxValue)
                {
                    var right = _plot.Plot.Axes.Right;
                    var p2 = Pad(y2Min, y2Max);
                    _plot.Plot.Axes.SetLimitsY(y2Min - p2, y2Max + p2, right);
                    right.IsVisible = true;
                }
                needRender = true;
            }
            else if (needRender && ViewLimits == null && !AutoScroll)
            {
                // 静态图/纯仪表图:数据变化后自动缩放一次。
                // 仪表盘的半径以"1 个数据单位的像素数"为基准,必须 AutoScale 到
                // 其数据范围(±1.1 左右)才能占满绘图区,否则缩成中心小圆环。
                _plot.Plot.Axes.AutoScale();
            }

            if (!needRender) return;

            _plottablesDirty = false;
            _lastVersion = maxVersion;
            _plot.Plot.Legend.IsVisible = ShowLegend;
            UpdateChip();

            var interacting = _mouseDown
                || (DateTime.UtcNow - _lastInteractionUtc).TotalMilliseconds < 300;
            var watch = Stopwatch.StartNew();
            _plot.Refresh();
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
            var following = AutoScroll && _userFollow;
            var chip = _chipText.Parent as Border;
            if (chip != null) chip.Visibility = AutoScroll ? Visibility.Visible : Visibility.Collapsed;
            _chipText.Text = following ? "● 跟随中" : "‖ 已暂停(点击恢复)";
            _chipText.Foreground = following
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0E, 0x8A, 0x5F))
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x60, 0x60, 0x60));
        }

        #endregion
    }
}
