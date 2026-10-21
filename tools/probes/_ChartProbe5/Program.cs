using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VM.Charts;

namespace ChartProbe
{
    /// <summary>演示用 VM:只有数据、状态和命令,零 ScottPlot 类型。</summary>
    public class ChartDemoVm : INotifyPropertyChanged
    {
        private string _readout = "把鼠标放到曲线上,十字光标会吸附最近的数据点";
        private ChartViewLimits _limits;
        private CancellationTokenSource _cts;

        public SignalSeriesVm Channel1 { get; private set; }
        public SignalSeriesVm Channel2 { get; private set; }
        public ObservableCollection<ISeriesVm> LiveSeries { get; private set; }

        public SignalSeriesVm StaticA { get; private set; }
        public SignalSeriesVm StaticB { get; private set; }
        public ObservableCollection<ISeriesVm> StaticSeries { get; private set; }

        public ThresholdLineVm UpperLimit { get; private set; }
        public EventMarkerVm EventMark { get; private set; }
        public ObservableCollection<IChartAnnotation> Annotations { get; private set; }

        public BarSeriesVm AlarmBars { get; private set; }
        public PieSeriesVm TimePie { get; private set; }
        public HistogramSeriesVm SizeHist { get; private set; }

        public FunctionSeriesVm TheoryFunc { get; private set; }
        public FillYSeriesVm ToleranceBand { get; private set; }
        public ScatterSeriesVm MeasuredPoints { get; private set; }

        public HeatmapSeriesVm HeatData { get; private set; }

        public GaugeVm GaugeRate { get; private set; }
        public GaugeVm GaugeBuffer { get; private set; }
        public ObservableCollection<GaugeVm> Gauges { get; private set; }

        public ObservableCollection<ChartMenuItemVm> MenuItems { get; private set; }
        public ICommand ExportCsvCommand { get; private set; }
        public ICommand ToggleCh2Command { get; private set; }

        public string Readout
        {
            get { return _readout; }
            set
            {
                _readout = value;
                OnPropertyChanged("Readout");
            }
        }

        public ChartViewLimits Limits
        {
            get { return _limits; }
            set
            {
                _limits = value;
                OnPropertyChanged("Limits");
            }
        }

        public DateTime StartTime { get; private set; }

        public ICommand CursorMoved { get; private set; }

        public ChartDemoVm()
        {
            StartTime = DateTime.Now;

            Channel1 = new SignalSeriesVm("ch1", "CH1 传感器A", Color.FromRgb(0x0F, 0x6C, 0xBD), 200, 8) { StartTime = StartTime };
            Channel2 = new SignalSeriesVm("ch2", "CH2 温度(右轴)", Color.FromRgb(0xD9, 0x73, 0x0D), 200, 8, yAxisIndex: 1) { StartTime = StartTime };
            LiveSeries = new ObservableCollection<ISeriesVm> { Channel1, Channel2 };

            StaticA = new SignalSeriesVm("s1", "理论曲线", Color.FromRgb(0x0E, 0x8A, 0x5F), 100, 6);
            StaticB = new SignalSeriesVm("s2", "实测曲线", Color.FromRgb(0x7A, 0x50, 0xC8), 100, 6);
            for (var i = 0; i < 600; i++)
            {
                var t = i / 100.0;
                StaticA.Append(2.5 * Math.Sin(2 * Math.PI * 2 * t));
                StaticB.Append(2.5 * Math.Sin(2 * Math.PI * 2 * t + 0.35) + (i % 50) * 0.01);
            }
            StaticSeries = new ObservableCollection<ISeriesVm> { StaticA, StaticB };

            UpperLimit = new ThresholdLineVm { Key = "upper", Y = 2.8, Label = "上限 2.8V", IsDraggable = true };
            EventMark = new EventMarkerVm { Key = "mark", X = 0.5, Label = "事件 A" };
            Annotations = new ObservableCollection<IChartAnnotation> { UpperLimit, EventMark };

            AlarmBars = new BarSeriesVm("bars", "本周报警", Color.FromRgb(0xC5, 0x0F, 0x1F),
                new double[] { 12, 5, 9, 3, 7, 2 });

            TimePie = new PieSeriesVm("pie", new ObservableCollection<PieSliceVm>
            {
                new PieSliceVm("图像采集", 40, Color.FromRgb(0x0F, 0x6C, 0xBD)),
                new PieSliceVm("算法处理", 35, Color.FromRgb(0x0E, 0x8A, 0x5F)),
                new PieSliceVm("通信等待", 15, Color.FromRgb(0xD9, 0x73, 0x0D)),
                new PieSliceVm("空闲", 10, Color.FromRgb(0x8A, 0x8A, 0x8A)),
            })
            {
                Label = "工时占比",
                DonutFraction = 0.35,
            };

            var samples = new double[3000];
            var rnd = new Random(42);
            for (var i = 0; i < samples.Length; i++)
            {
                // 6 个均匀随机数求和 ≈ 正态分布
                double sum = 0;
                for (var k = 0; k < 6; k++) sum += rnd.NextDouble();
                samples[i] = 3.0 + (sum - 3.0) * 1.2;
            }
            SizeHist = new HistogramSeriesVm("hist", "尺寸分布", Color.FromRgb(0x7A, 0x50, 0xC8), samples, 30);

            TheoryFunc = new FunctionSeriesVm("func", "理论包络", Color.FromRgb(0xC5, 0x0F, 0x1F),
                x => 2.5 * Math.Sin(2 * Math.PI * x / 3.0));

            var n = 61;
            var xs = new double[n];
            var top = new double[n];
            var bottom = new double[n];
            for (var i = 0; i < n; i++)
            {
                xs[i] = i * 6.0 / (n - 1);
                top[i] = 2.5 * Math.Sin(2 * Math.PI * xs[i] / 3.0) + 0.6;
                bottom[i] = 2.5 * Math.Sin(2 * Math.PI * xs[i] / 3.0) - 0.6;
            }
            ToleranceBand = new FillYSeriesVm("fill", "公差带", Color.FromArgb(90, 0x0F, 0x6C, 0xBD), xs, top, bottom);

            var mxs = new double[40];
            var mys = new double[40];
            for (var i = 0; i < 40; i++)
            {
                mxs[i] = i * 6.0 / 39.0;
                mys[i] = 2.5 * Math.Sin(2 * Math.PI * mxs[i] / 3.0) + (rnd.NextDouble() - 0.5) * 0.8;
            }
            MeasuredPoints = new ScatterSeriesVm("meas", "实测点", Color.FromRgb(0x30, 0x30, 0x30), mxs, mys)
            {
                MarkerSize = 4,
                LineWidth = 0,
            };

            HeatData = new HeatmapSeriesVm("heat", new double[24, 32]);

            GaugeRate = new GaugeVm("rate", "采集速率", 100);
            GaugeBuffer = new GaugeVm("buf", "缓冲占用", 35);
            Gauges = new ObservableCollection<GaugeVm> { GaugeRate, GaugeBuffer };

            ExportCsvCommand = new DelegateCommand<object>(_ => ExportCsv());
            ToggleCh2Command = new DelegateCommand<object>(_ =>
            {
                Channel2.IsVisible = !Channel2.IsVisible;
                Console.WriteLine("CH2 可见性: " + Channel2.IsVisible);
            });
            MenuItems = new ObservableCollection<ChartMenuItemVm>
            {
                new ChartMenuItemVm("导出 CSV...", ExportCsvCommand),
                new ChartMenuItemVm("显示/隐藏 CH2 (右轴)", ToggleCh2Command),
            };

            UpperLimit.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == "Y") Console.WriteLine("阈值线拖动: 新 Y = " + UpperLimit.Y.ToString("F3"));
            };

            CursorMoved = new DelegateCommand<ChartCursorInfo>(p =>
            {
                var timePart = p.TimeText != null ? "T = " + p.TimeText + "    " : "X = " + p.X.ToString("F2", CultureInfo.InvariantCulture) + " s    ";
                var snapPart = p.Snapped ? "  [吸附:" + p.SeriesLabel + "]" : "";
                Readout = "十字光标:  " + timePart + "Y = " + p.Y.ToString("F3", CultureInfo.InvariantCulture) + snapPart;
            });
        }

        /// <summary>模拟采集线程:写曲线 Buffer、驱动仪表和热力图,不知道任何 UI 的存在。</summary>
        public void StartCollection()
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            var rnd = new Random(7);
            Task.Factory.StartNew(delegate
            {
                var t = 0.0;
                var tick = 0;
                while (!token.IsCancellationRequested)
                {
                    t += 0.005;
                    tick++;
                    Channel1.Append(3.0 * Math.Sin(2 * Math.PI * 3 * t) + (rnd.NextDouble() - 0.5) * 0.6);
                    Channel2.Append(2.0 * Math.Sin(2 * Math.PI * 1.2 * t)
                                    + 1.0 * Math.Sin(2 * Math.PI * 0.8 * t)
                                    + (rnd.NextDouble() - 0.5) * 0.4);
                    if (tick % 20 == 0)
                    {
                        GaugeRate.Value = 50 + 45 * Math.Sin(2 * Math.PI * 0.5 * t);
                        GaugeBuffer.Value = 35 + 25 * Math.Sin(2 * Math.PI * 0.23 * t + 1);

                        // 热力图原址更新一帧
                        for (var r = 0; r < 24; r++)
                            for (var c = 0; c < 32; c++)
                                HeatData.Intensities[r, c] =
                                    50 + 50 * Math.Sin(r * 0.3 + t * 2) * Math.Cos(c * 0.2 + t);
                        HeatData.Update();
                    }
                    Thread.Sleep(5);
                }
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public void StopCollection()
        {
            if (_cts != null) _cts.Cancel();
        }

        /// <summary>演示业务菜单项:把两个通道导出为 CSV。</summary>
        private void ExportCsv()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("time,ch1,ch2");
            var n = Math.Min(Channel1.Count, Channel2.Count);
            for (var i = 0; i < n; i++)
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:F4},{1:F4},{2:F4}", i / 200.0, Channel1.Buffer[i], Channel2.Buffer[i]));
            System.IO.File.WriteAllText(@"D:\C#\VM\_ChartProbe5\export.csv", sb.ToString());
            Console.WriteLine("导出 CSV 完成:" + n + " 行");
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    public class DelegateCommand<T> : ICommand
    {
        private readonly Action<T> _exec;
        public DelegateCommand(Action<T> exec) { _exec = exec; }
        public event EventHandler CanExecuteChanged;
        public bool CanExecute(object parameter) { return true; }
        public void Execute(object parameter) { _exec((T)parameter); }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (System.Windows.Application.Current == null)
                new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

            try
            {
                Run(args);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Main 异常: " + ex.Message);
            }
        }

        private static void Run(string[] args)
        {
            ChartView.DiagnosticsLog = m => Console.WriteLine("[Chart] " + m);
            var vm = new ChartDemoVm();
            vm.StartCollection();

            var tabs = new TabControl
            {
                Margin = new Thickness(6),
            };
            tabs.Items.Add(new TabItem { Header = "实时曲线", Content = BuildLiveTab(vm) });
            tabs.Items.Add(new TabItem { Header = "静态对比", Content = BuildStaticTab(vm) });
            tabs.Items.Add(new TabItem { Header = "统计图形", Content = BuildStatsTab(vm) });
            tabs.Items.Add(new TabItem { Header = "仪表盘", Content = BuildGaugeTab(vm) });
            tabs.Items.Add(new TabItem { Header = "函数与填充", Content = BuildFuncTab(vm) });
            tabs.Items.Add(new TabItem { Header = "热力图", Content = BuildHeatTab(vm) });

            var root = new Grid { Margin = new Thickness(6) };
            root.Children.Add(tabs);

            var autoShots = args != null && args.Contains("--shots");
            var win = new Window
            {
                Title = "ChartView — ScottPlot 5.1.59 MVVM 封装(VM 零 ScottPlot 类型)",
                Content = root,
                Width = 1220,
                Height = 760,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 10,
                Top = 10,
            };
            win.Show();

            if (autoShots)
            {
                // 自动化验证模式:逐页截图后退出
                Pump(2500);
                var names = new[] { "live", "static", "stats", "gauge", "func", "heat" };
                for (var i = 0; i < tabs.Items.Count; i++)
                {
                    ((TabItem)tabs.Items[i]).IsSelected = true;
                    Pump(900);
                    if (names[i] == "live")
                    {
                        // 程序化悬浮:验证数据提示渲染(无真实鼠标时提示显示在左上角)
                        var liveChart = FindFirstChart(tabs);
                        liveChart?.PlaceCursor(0.5, 2.8, "CH1 传感器A", true);
                        Pump(200);
                    }
                    if (names[i] == "stats")
                    {
                        // 统计图命中验证:柱(第3根=9)/ 环(第一片=图像采集40)/ 直方图(中部箱)
                        var charts = FindAllCharts(tabs);
                        if (charts.Count >= 3)
                        {
                            charts[0].PlaceCursor(2, 5, null, false);
                            charts[1].PlaceCursor(0, -0.5, null, false);
                            charts[2].PlaceCursor(3, 100, null, false);
                        }
                        Pump(200);
                    }
                    Capture(root, 1220, 760, "demo_" + names[i] + ".png");
                    if (names[i] == "live")
                    {
                        // 滑窗验证:等 12 秒(超过 8 秒容量)后时间轴应继续前进、曲线贴窗滚动
                        Pump(9500);
                        Capture(root, 1220, 760, "demo_live_slide.png");
                    }
                }
                vm.StopCollection();
                win.Close();
                Console.WriteLine("done");
                return;
            }

            // 交互模式:窗口一直开着,关掉窗口才退出;采集线程随后停止
            win.Closed += delegate
            {
                vm.StopCollection();
                Application.Current.Shutdown();
            };
            Application.Current.Run();
        }

        private static UIElement BuildLiveTab(ChartDemoVm vm)
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var liveChart = new ChartView
            {
                AutoScroll = true,
                AutoScrollSeconds = 4,
                SyncGroup = "main",
                SnapToNearest = true,
                CrosshairOnY2 = true,
                // 双卡尺默认关闭,经右键"显示测量卡尺"启用(可拖拽,带 ΔX/ΔY 读数)
            };
            liveChart.SetBinding(ChartView.SeriesSourceProperty, new Binding("LiveSeries"));
            liveChart.SetBinding(ChartView.AnnotationsSourceProperty, new Binding("Annotations"));
            liveChart.SetBinding(ChartView.CursorMovedProperty, new Binding("CursorMoved"));
            liveChart.SetBinding(ChartView.MenuItemsSourceProperty, new Binding("MenuItems"));
            // Demo:右键"导入数据..."触发 —— 宿主把列数据送往自己的 VM 系列
            liveChart.CsvImportRequested += data =>
            {
                Console.WriteLine("导入请求:" + data.FilePath + " 列=" + string.Join(" | ", data.ColumnNames));
                if (data.Columns.Length > 1)
                {
                    // 演示:把第 2 列(数据列)按 200Hz 追加到 CH2(真实宿主应按列名匹配 Key)
                    foreach (var v in data.Columns[1])
                        vm.Channel2.Append(v);
                    Console.WriteLine("已把第 2 列追加到 CH2:" + data.Columns[1].Length + " 点");
                }
            };
            liveChart.Title = "实时采集 — 采集线程 200Hz 写 Buffer,控件 30ms 节流渲染(拖动红色阈值线试试)";
            liveChart.XLabel = "时间";
            liveChart.YLabel = "幅值 CH1 (V)";
            liveChart.Y2Label = "温度 CH2 (°C)";
            Grid.SetRow(liveChart, 0);
            grid.Children.Add(liveChart);

            var readout = new TextBlock
            {
                Margin = new Thickness(4, 6, 0, 6),
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x6C, 0xBD)),
            };
            readout.SetBinding(TextBlock.TextProperty, new Binding("Readout"));
            Grid.SetRow(readout, 1);
            grid.Children.Add(readout);

            grid.DataContext = vm;
            return grid;
        }

        private static UIElement BuildStaticTab(ChartDemoVm vm)
        {
            var grid = new Grid();
            var chart = new ChartView { ShowLegend = true, SyncGroup = "main" };
            chart.SetBinding(ChartView.SeriesSourceProperty, new Binding("StaticSeries"));
            chart.SetBinding(ChartView.ViewLimitsProperty, new Binding("Limits") { Mode = BindingMode.TwoWay });
            chart.Title = "静态多系列 + 图例(同组同步:右键可开关光标/轴范围同步)";
            chart.XLabel = "时间 (s)";
            chart.YLabel = "幅值";
            grid.Children.Add(chart);
            grid.DataContext = vm;
            return grid;
        }

        private static UIElement BuildStatsTab(ChartDemoVm vm)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // 统计图形:Measurable=false(饼图/柱状图不需要卡尺)
            var bars = new ChartView { Measurable = false };
            bars.SetBinding(ChartView.ExtrasSourceProperty, new Binding("AlarmBars"));
            bars.Title = "柱状图 — 报警统计";
            bars.YLabel = "次数";
            Grid.SetRow(bars, 0);
            Grid.SetColumn(bars, 0);
            grid.Children.Add(bars);

            var pie = new ChartView { Measurable = false };
            pie.SetBinding(ChartView.ExtrasSourceProperty, new Binding("TimePie"));
            pie.Title = "环形图 — 工时占比";
            Grid.SetRow(pie, 0);
            Grid.SetColumn(pie, 1);
            grid.Children.Add(pie);

            var hist = new ChartView { Measurable = false };
            hist.SetBinding(ChartView.ExtrasSourceProperty, new Binding("SizeHist"));
            hist.Title = "直方图 — 尺寸分布(自动分箱)";
            hist.XLabel = "尺寸";
            hist.YLabel = "数量";
            Grid.SetRow(hist, 1);
            Grid.SetColumn(hist, 0);
            Grid.SetColumnSpan(hist, 2);
            grid.Children.Add(hist);

            grid.DataContext = vm;
            return grid;
        }

        private static UIElement BuildGaugeTab(ChartDemoVm vm)
        {
            var grid = new Grid();
            var gauges = new ChartView { Measurable = false };
            gauges.SetBinding(ChartView.GaugesSourceProperty, new Binding("Gauges"));
            gauges.Title = "仪表盘 — 采集线程实时驱动";
            grid.Children.Add(gauges);
            grid.DataContext = vm;
            return grid;
        }

        private static UIElement BuildFuncTab(ChartDemoVm vm)
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var funcChart = new ChartView { ShowLegend = true };
            funcChart.SetBinding(ChartView.ExtrasSourceProperty, new Binding("TheoryFunc"));
            funcChart.Title = "函数曲线 — y = F(x) 直接画理论线";
            Grid.SetRow(funcChart, 0);
            grid.Children.Add(funcChart);

            // 上下两图:上=公差带区域填充,下=实测散点
            var fillChart = new ChartView { ShowLegend = true };
            fillChart.SetBinding(ChartView.ExtrasSourceProperty, new Binding("ToleranceBand"));
            fillChart.Title = "区域填充(公差带)";
            Grid.SetRow(fillChart, 1);
            Grid.SetColumn(fillChart, 0);
            grid.Children.Add(fillChart);

            var points = new ChartView
            {
                ShowLegend = false,
                SnapToNearest = true,
            };
            points.SetBinding(ChartView.SeriesSourceProperty, new Binding("MeasuredPoints"));
            points.Title = "实测散点(十字光标吸附)";
            Grid.SetRow(points, 1);
            Grid.SetColumn(points, 1);
            grid.Children.Add(points);

            grid.DataContext = vm;
            return grid;
        }

        private static UIElement BuildHeatTab(ChartDemoVm vm)
        {
            var grid = new Grid();
            var heat = new ChartView { Measurable = false };
            heat.SetBinding(ChartView.ExtrasSourceProperty, new Binding("HeatData"));
            heat.Title = "热力图 — 采集线程原址更新矩阵";
            grid.Children.Add(heat);
            grid.DataContext = vm;
            return grid;
        }

        private static ChartView FindFirstChart(DependencyObject root)
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is ChartView chart) return chart;
                var deep = FindFirstChart(child);
                if (deep != null) return deep;
            }
            return null;
        }

        private static System.Collections.Generic.List<ChartView> FindAllCharts(DependencyObject root)
        {
            var result = new System.Collections.Generic.List<ChartView>();
            if (root is ChartView self) result.Add(self);
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
                result.AddRange(FindAllCharts(VisualTreeHelper.GetChild(root, i)));
            return result;
        }

        private static void Pump(int milliseconds)
        {
            var deadline = Environment.TickCount + milliseconds;
            while (Environment.TickCount < deadline)
            {
                Dispatcher.CurrentDispatcher.Invoke(delegate { }, DispatcherPriority.Background);
                Thread.Sleep(10);
            }
        }

        private static void Capture(FrameworkElement root, double dipWidth, double dipHeight, string fileName)
        {
            root.UpdateLayout();
            const double scale = 1.25;
            var rtb = new RenderTargetBitmap(
                (int)(dipWidth * scale), (int)(dipHeight * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            rtb.Render(root);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(@"D:\C#\VM\_ChartProbe5\" + fileName))
                enc.Save(fs);
            Console.WriteLine("saved " + fileName);
        }
    }
}


