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
        private string _readout = "把鼠标放到上图,十字光标坐标会回传到这里(MVVM 双向)";
        private ChartViewLimits _limits;
        private CancellationTokenSource _cts;

        public SignalSeriesVm Channel1 { get; private set; }
        public SignalSeriesVm Channel2 { get; private set; }
        public ObservableCollection<ISeriesVm> LiveSeries { get; private set; }
        public ObservableCollection<ISeriesVm> StaticSeries { get; private set; }

        public string Readout
        {
            get { return _readout; }
            set
            {
                _readout = value;
                OnPropertyChanged("Readout");
            }
        }

        /// <summary>静态图的轴范围(双向绑定演示:程序改它 → 图变;图上拖拽 → 它变)</summary>
        public ChartViewLimits Limits
        {
            get { return _limits; }
            set
            {
                _limits = value;
                OnPropertyChanged("Limits");
            }
        }

        /// <summary>采集起点:两个通道共享,设置后 X 轴显示真实时钟</summary>
        public DateTime StartTime { get; private set; }

        public ThresholdLineVm UpperLimit { get; private set; }
        public EventMarkerVm EventMark { get; private set; }
        public ObservableCollection<IChartAnnotation> Annotations { get; private set; }

        public ICommand CursorMoved { get; private set; }

        public ChartDemoVm()
        {
            StartTime = DateTime.Now;

            Channel1 = new SignalSeriesVm("ch1", "CH1 传感器A", Color.FromRgb(0x0F, 0x6C, 0xBD), 200, 8) { StartTime = StartTime };
            Channel2 = new SignalSeriesVm("ch2", "CH2 温度(右轴)", Color.FromRgb(0xD9, 0x73, 0x0D), 200, 8, yAxisIndex: 1) { StartTime = StartTime };
            LiveSeries = new ObservableCollection<ISeriesVm> { Channel1, Channel2 };

            var staticA = new SignalSeriesVm("s1", "理论曲线", Color.FromRgb(0x0E, 0x8A, 0x5F), 100, 6);
            var staticB = new SignalSeriesVm("s2", "实测曲线", Color.FromRgb(0x7A, 0x50, 0xC8), 100, 6);
            for (var i = 0; i < 600; i++)
            {
                var t = i / 100.0;
                staticA.Append(2.5 * Math.Sin(2 * Math.PI * 2 * t));
                staticB.Append(2.5 * Math.Sin(2 * Math.PI * 2 * t + 0.35) + (i % 50) * 0.01);
            }
            StaticSeries = new ObservableCollection<ISeriesVm> { staticA, staticB };

            UpperLimit = new ThresholdLineVm { Key = "upper", Y = 2.8, Label = "上限 2.8V" };
            EventMark = new EventMarkerVm { Key = "mark", X = 0.5, Label = "事件 A" };
            Annotations = new ObservableCollection<IChartAnnotation> { UpperLimit, EventMark };

            CursorMoved = new DelegateCommand<ChartCursorInfo>(p =>
            {
                Readout = p.TimeText != null
                    ? string.Format(CultureInfo.InvariantCulture, "十字光标:  T = {0}    Y = {1:F3}", p.TimeText, p.Y)
                    : string.Format(CultureInfo.InvariantCulture, "十字光标:  X = {0:F2} s    Y = {1:F3}", p.X, p.Y);
            });
        }

        /// <summary>模拟采集线程:200Hz 往 Buffer 写数据,不知道任何 UI 的存在。</summary>
        public void StartCollection()
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            var rnd = new Random(7);
            Task.Factory.StartNew(delegate
            {
                var t = 0.0;
                while (!token.IsCancellationRequested)
                {
                    t += 0.005;
                    Channel1.Append(3.0 * Math.Sin(2 * Math.PI * 3 * t) + (rnd.NextDouble() - 0.5) * 0.6);
                    Channel2.Append(2.0 * Math.Sin(2 * Math.PI * 1.2 * t)
                                    + 1.0 * Math.Sin(2 * Math.PI * 0.8 * t)
                                    + (rnd.NextDouble() - 0.5) * 0.4);
                    Thread.Sleep(5);
                }
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public void StopCollection()
        {
            if (_cts != null) _cts.Cancel();
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
        private static void Main()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log("AppDomain 未处理异常: " + e.ExceptionObject);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (s, e) =>
            {
                Log("Dispatcher 异常: " + e.Exception);
                e.Handled = true;
            };

            try
            {
                Run();
            }
            catch (Exception ex)
            {
                Log("Main 异常: " + ex);
            }
        }

        private static void Log(object message)
        {
            try
            {
                File.AppendAllText(@"D:\C#\VM\_ChartProbe\crash.txt",
                    DateTime.Now.ToString("HH:mm:ss.fff ") + message + Environment.NewLine);
            }
            catch { }
        }

        private static void Run()
        {
            Log("启动");
            ChartView.DiagnosticsLog = m => Log(m); // 诊断(慢帧/同步/渲染异常)统一进 crash.txt
            var vm = new ChartDemoVm();

            // —— 上图:实时流(示波器模式 + 绝对时间轴 + 阈值线/事件标记 + 跨图光标同步) ——
            var liveChart = new ChartView
            {
                Height = 300,
                AutoScroll = true,
                AutoScrollSeconds = 4,
                SyncGroup = "main",
            };
            liveChart.SetBinding(ChartView.SeriesSourceProperty, new Binding("LiveSeries"));
            liveChart.SetBinding(ChartView.AnnotationsSourceProperty, new Binding("Annotations"));
            liveChart.SetBinding(ChartView.CursorMovedProperty, new Binding("CursorMoved"));
            liveChart.Title = "实时采集 — 采集线程 200Hz 写 Buffer,控件 30ms 节流渲染";
            liveChart.XLabel = "时间";
            liveChart.YLabel = "幅值 CH1 (V)";
            liveChart.Y2Label = "温度 CH2 (°C)";

            var readout = new TextBlock
            {
                Margin = new Thickness(4, 6, 0, 6),
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x6C, 0xBD)),
            };
            readout.SetBinding(TextBlock.TextProperty, new Binding("Readout"));

            // —— 下图:静态多系列 + 图例 + ViewLimits 双向绑定(同组光标同步) ——
            var staticChart = new ChartView { Height = 240, ShowLegend = true, SyncGroup = "main" };
            staticChart.SetBinding(ChartView.SeriesSourceProperty, new Binding("StaticSeries"));
            staticChart.SetBinding(ChartView.ViewLimitsProperty, new Binding("Limits") { Mode = BindingMode.TwoWay });
            staticChart.Title = "静态多系列 + 图例(下图轴范围由 VM 的 Limits 绑定驱动)";
            staticChart.XLabel = "时间 (s)";
            staticChart.YLabel = "幅值";

            var root = new Grid { Margin = new Thickness(10) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new TextBlock
            {
                Text = "ChartView — ScottPlot 4.1.60 MVVM 封装演示(VM 零 ScottPlot 类型)",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(2, 0, 0, 8),
            };
            Grid.SetRow(header, 0);
            Grid.SetRow(liveChart, 1);
            Grid.SetRow(readout, 2);
            Grid.SetRow(staticChart, 3);
            root.Children.Add(header);
            root.Children.Add(liveChart);
            root.Children.Add(readout);
            root.Children.Add(staticChart);

            root.DataContext = vm;

            var win = new Window
            {
                Title = "ChartView MVVM 演示",
                Content = root,
                Width = 1200,
                Height = 780,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 10,
                Top = 10,
            };

            vm.StartCollection();
            Log("开始采集");
            win.Show();
            Log("窗口已显示");
            Pump(3000); // 让实时曲线跑 3 秒
            Log("跑完 3 秒");

            Capture(root, 1200, 780, "chart_render_1.png");

            // 程序化放一个十字光标(PlaceCursor → 走 CursorMoved 命令回传 VM → 同组图竖线同步)
            liveChart.PlaceCursor(0.5, 2.8);
            Log("十字光标已放置, readout=" + vm.Readout);
            Log("十字光标已放置, readout=" + vm.Readout);
            Pump(600);
            Capture(root, 1200, 780, "chart_render_2.png");

            // ViewLimits 双向绑定演示:VM 改范围 → 图跟着缩放
            vm.Limits = new ChartViewLimits { XMin = 1.0, XMax = 3.0, YMin = -3, YMax = 3 };
            Pump(400);
            Capture(root, 1200, 780, "chart_render_3.png");

            // 导出 API:VM 不经窗口直接出图
            liveChart.SavePng(@"D:\C#\VM\_ChartProbe\chart_export.png", 1200, 300);
            Console.WriteLine("saved chart_export.png");

            vm.StopCollection();
            win.Close();
            Console.WriteLine("done");
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
            using (var fs = File.Create(@"D:\C#\VM\_ChartProbe\" + fileName))
                enc.Save(fs);
            Console.WriteLine("saved " + fileName);
        }
    }
}
