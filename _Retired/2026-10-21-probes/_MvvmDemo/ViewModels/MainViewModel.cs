using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using VM.Charts;

namespace _MvvmDemo.ViewModels
{
    /// <summary>
    /// 主 ViewModel —— **纯 MVVM**:本类与整个 ViewModel 层不出现任何 ScottPlot 类型,
    /// 只使用 VM.Charts 的数据契约(SignalSeriesVm / 标注 / 扩展图形 / 仪表 / 菜单项),
    /// 由 ChartView 在 View 层翻译成绘图调用。
    /// </summary>
    public class MainViewModel : INotifyPropertyChanged
    {
        private string _readout = "把鼠标移到曲线上:悬浮显示各通道当前值,十字光标吸附最近点";
        private ChartViewLimits _limits;
        private double _measureA = double.NaN;
        private double _measureB = double.NaN;
        private CancellationTokenSource _cts;

        #region 数据(采集线程写,UI 只读)

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

        public GaugeVm GaugeRate { get; private set; }
        public GaugeVm GaugeBuffer { get; private set; }
        public ObservableCollection<GaugeVm> Gauges { get; private set; }

        public ObservableCollection<ChartMenuItemVm> MenuItems { get; private set; }

        #endregion

        #region 绑定状态(界面只读或双向)

        /// <summary>底部读数(由光标命令更新)</summary>
        public string Readout
        {
            get { return _readout; }
            private set { _readout = value; OnPropertyChanged("Readout"); }
        }

        /// <summary>静态图轴范围(与 ChartView.ViewLimits 双向绑定)</summary>
        public ChartViewLimits Limits
        {
            get { return _limits; }
            set { _limits = value; OnPropertyChanged("Limits"); }
        }

        /// <summary>卡尺线 A 位置(与 ChartView.MeasureCursorAX 双向绑定;图上拖动即回写)</summary>
        public double MeasureA
        {
            get { return _measureA; }
            set { _measureA = value; OnPropertyChanged("MeasureA"); }
        }

        /// <summary>卡尺线 B 位置(与 MeasureCursorBX 双向绑定)</summary>
        public double MeasureB
        {
            get { return _measureB; }
            set { _measureB = value; OnPropertyChanged("MeasureB"); }
        }

        public ICommand CursorMovedCommand { get; private set; }
        public ICommand ExportCsvCommand { get; private set; }
        public ICommand ToggleChannel2Command { get; private set; }

        #endregion

        public MainViewModel()
        {
            var start = DateTime.Now;

            // —— 实时通道(200Hz,8 秒滑窗,绝对时间轴;CH2 走右轴)——
            Channel1 = new SignalSeriesVm("ch1", "CH1 传感器A", Color.FromRgb(0x0F, 0x6C, 0xBD), 200, 8) { StartTime = start };
            Channel2 = new SignalSeriesVm("ch2", "CH2 温度(右轴)", Color.FromRgb(0xD9, 0x73, 0x0D), 200, 8, yAxisIndex: 1) { StartTime = start };
            LiveSeries = new ObservableCollection<ISeriesVm> { Channel1, Channel2 };

            UpperLimit = new ThresholdLineVm { Key = "upper", Y = 2.8, Label = "上限 2.8V", IsDraggable = true };
            EventMark = new EventMarkerVm { Key = "mark", X = 0.5, Label = "事件 A" };
            Annotations = new ObservableCollection<IChartAnnotation> { UpperLimit, EventMark };

            // —— 静态对比曲线 ——
            StaticA = new SignalSeriesVm("s1", "理论曲线", Color.FromRgb(0x0E, 0x8A, 0x5F), 100, 6);
            StaticB = new SignalSeriesVm("s2", "实测曲线", Color.FromRgb(0x7A, 0x50, 0xC8), 100, 6);
            for (var i = 0; i < 600; i++)
            {
                var t = i / 100.0;
                StaticA.Append(2.5 * Math.Sin(2 * Math.PI * 2 * t));
                StaticB.Append(2.5 * Math.Sin(2 * Math.PI * 2 * t + 0.35) + (i % 50) * 0.01);
            }
            StaticSeries = new ObservableCollection<ISeriesVm> { StaticA, StaticB };

            // —— 统计图形 ——
            AlarmBars = new BarSeriesVm("bars", "本周报警", Color.FromRgb(0xC5, 0x0F, 0x1F),
                new double[] { 12, 5, 9, 3, 7, 2 });
            TimePie = new PieSeriesVm("pie", new ObservableCollection<PieSliceVm>
            {
                new PieSliceVm("图像采集", 40, Color.FromRgb(0x0F, 0x6C, 0xBD)),
                new PieSliceVm("算法处理", 35, Color.FromRgb(0x0E, 0x8A, 0x5F)),
                new PieSliceVm("通信等待", 15, Color.FromRgb(0xD9, 0x73, 0x0D)),
                new PieSliceVm("空闲", 10, Color.FromRgb(0x8A, 0x8A, 0x8A)),
            })
            { Label = "工时占比", DonutFraction = 0.35 };
            var samples = new double[3000];
            var rnd = new Random(42);
            for (var i = 0; i < samples.Length; i++)
            {
                double sum = 0;
                for (var k = 0; k < 6; k++) sum += rnd.NextDouble();
                samples[i] = 3.0 + (sum - 3.0) * 1.2;
            }
            SizeHist = new HistogramSeriesVm("hist", "尺寸分布", Color.FromRgb(0x7A, 0x50, 0xC8), samples, 30);

            // —— 仪表盘 ——
            GaugeRate = new GaugeVm("rate", "采集速率", 100);
            GaugeBuffer = new GaugeVm("buf", "缓冲占用", 35);
            Gauges = new ObservableCollection<GaugeVm> { GaugeRate, GaugeBuffer };

            // —— 右键业务菜单(点走命令)——
            ExportCsvCommand = new DelegateCommand(ExportCsv);
            ToggleChannel2Command = new DelegateCommand(() => Channel2.IsVisible = !Channel2.IsVisible);
            MenuItems = new ObservableCollection<ChartMenuItemVm>
            {
                new ChartMenuItemVm("导出 CSV...", ExportCsvCommand),
                new ChartMenuItemVm("显示/隐藏 CH2 (右轴)", ToggleChannel2Command),
            };

            // —— 光标回传命令 ——
            CursorMovedCommand = new DelegateCommand<ChartCursorInfo>(info =>
            {
                var time = info.TimeText != null
                    ? "T = " + info.TimeText
                    : "X = " + info.X.ToString("F2", CultureInfo.InvariantCulture) + " s";
                var snap = info.Snapped ? "   [吸附:" + info.SeriesLabel + "]" : "";
                Readout = "十字光标: " + time + "   Y = " +
                          info.Y.ToString("F3", CultureInfo.InvariantCulture) + snap;
            });

            StartCollection();
        }

        #region 采集线程(模拟设备回调;真实工程替换为相机/轴卡/PLC 的事件)

        public void StartCollection()
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            Task.Factory.StartNew(delegate
            {
                var t = 0.0;
                var tick = 0;
                var rnd = new Random(7);
                while (!token.IsCancellationRequested)
                {
                    t += 0.005;
                    tick++;

                    // 只写数据:线程安全,不碰任何 UI
                    Channel1.Append(3.0 * Math.Sin(2 * Math.PI * 3 * t) + (rnd.NextDouble() - 0.5) * 0.6);
                    Channel2.Append(2.0 * Math.Sin(2 * Math.PI * 1.2 * t)
                                    + 1.0 * Math.Sin(2 * Math.PI * 0.8 * t)
                                    + (rnd.NextDouble() - 0.5) * 0.4);

                    if (tick % 20 == 0)
                    {
                        GaugeRate.Value = 50 + 45 * Math.Sin(2 * Math.PI * 0.5 * t);
                        GaugeBuffer.Value = 35 + 25 * Math.Sin(2 * Math.PI * 0.23 * t + 1);
                    }

                    Thread.Sleep(5);
                }
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public void StopCollection()
        {
            if (_cts != null) _cts.Cancel();
        }

        #endregion

        /// <summary>业务菜单项示例:导出当前两通道为 CSV。</summary>
        private void ExportCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("time,ch1,ch2");
            var n = Math.Min(Channel1.Count, Channel2.Count);
            for (var i = 0; i < n; i++)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:F4},{1:F4},{2:F4}",
                    i / 200.0, Channel1.Buffer[i], Channel2.Buffer[i]));
            }
            File.WriteAllText("export.csv", sb.ToString());
            Readout = "已导出 export.csv(" + n + " 行)";
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>最小命令实现(真实工程可换 Prism DelegateCommand / CommunityToolkit)</summary>
    public class DelegateCommand : ICommand
    {
        private readonly Action _exec;
        public DelegateCommand(Action exec) { _exec = exec; }
        public event EventHandler CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object parameter) { return true; }
        public void Execute(object parameter) { _exec(); }
    }

    public class DelegateCommand<T> : ICommand
    {
        private readonly Action<T> _exec;
        public DelegateCommand(Action<T> exec) { _exec = exec; }
        public event EventHandler CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object parameter) { return true; }
        public void Execute(object parameter) { _exec((T)parameter); }
    }
}
