using System;
using System.ComponentModel;
using System.Threading;
using System.Windows.Media;

namespace VM.Charts
{
    /// <summary>
    /// XY 散点/折线系列契约:X/Y 是两个显式数组(非等间隔信号),
    /// 数据由外部按索引填充,<see cref="MaxIndex"/> 随数据增长推进渲染上界。
    /// </summary>
    public interface IScatterSeriesVm : INotifyPropertyChanged
    {
        string Key { get; }
        string Label { get; }
        Color Color { get; }
        bool IsVisible { get; set; }
        double LineWidth { get; set; }
        ChartLineType LineType { get; set; }

        /// <summary>标记点尺寸,0 = 不显示标记点</summary>
        float MarkerSize { get; set; }

        /// <summary>Y 轴索引:0 = 左轴,1 = 右轴</summary>
        int YAxisIndex { get; }

        double[] Xs { get; }
        double[] Ys { get; }

        /// <summary>横轴起点（绝对时间）；null = 横轴显示相对秒数。见实现类上的同名属性说明。</summary>
        DateTime? StartTime { get; set; }

        /// <summary>渲染上界(含):随数据增长推进,控件据此截断未写入区间</summary>
        int MaxIndex { get; set; }

        /// <summary>版本号:<see cref="MaxIndex"/> 变化时递增,控件轮询判定重绘</summary>
        int Version { get; }
    }

    /// <summary>XY 散点/折线系列的默认实现。数组由外部持有并填充,本类只做状态与通知。</summary>
    public class ScatterSeriesVm : INotifyPropertyChanged, IScatterSeriesVm
    {
        private bool _isVisible = true;
        private double _lineWidth = 1.5;
        private ChartLineType _lineType = ChartLineType.Solid;
        private float _markerSize;
        private int _maxIndex = -1;
        private int _version;

        public ScatterSeriesVm(string key, string label, Color color,
            double[] xs, double[] ys, int yAxisIndex = 0)
        {
            Key = key;
            Label = label;
            Color = color;
            Xs = xs;
            Ys = ys;
            YAxisIndex = yAxisIndex;
        }

        public string Key { get; private set; }
        public string Label { get; private set; }

        /// <summary>
        /// 系列颜色。A=0 表示"未指定"，由控件按创建顺序从主题色板自动分配。
        /// 可在运行期改（宿主按量程/工况换色），改完发 <c>Color</c> 的 PropertyChanged
        /// 让控件下一次渲染帧跟上（<c>ChartView.OnScatterPropertyChanged</c> 已处理该属性）。
        /// </summary>
        public Color Color
        {
            get { return _color; }
            set
            {
                if (_color == value) return;
                _color = value;
                OnPropertyChanged("Color");
            }
        }

        /// <summary>
        /// 横轴起点（绝对时间）。非 null 时横轴刻度按 <c>StartTime + X 秒</c> 显示成钟点，
        /// 而不是显示"第几秒"。
        ///
        /// 为什么散点系列也需要它：趋势曲线的 X 是"值到达的真实时刻"（相对起点的秒数），
        /// 而现场看趋势图是为了把曲线上的波动跟日志/事件对上时间，
        /// 显示"第 137.4 秒"没有任何意义——必须显示 14:23:07。
        ///
        /// 与 <see cref="ISeriesVm.StartTime"/> 语义完全一致，只是那边属于定采样率信号系列。
        /// </summary>
        public DateTime? StartTime
        {
            get { return _startTime; }
            set
            {
                if (_startTime == value) return;
                _startTime = value;
                OnPropertyChanged("StartTime");
            }
        }

        private Color _color;
        private DateTime? _startTime;

        public int YAxisIndex { get; private set; }

        /// <summary>X 轴数据。整组替换用 <see cref="SetData"/>(控件会重建对应曲线)。</summary>
        public double[] Xs { get; private set; }

        /// <summary>Y 轴数据。整组替换用 <see cref="SetData"/>。</summary>
        public double[] Ys { get; private set; }

        /// <summary>整组替换数据源(如"固定曲线"按新点位重画)。控件将重建对应曲线。</summary>
        public void SetData(double[] xs, double[] ys)
        {
            Xs = xs;
            Ys = ys;
            _maxIndex = ys.Length - 1;
            Interlocked.Increment(ref _version);
            OnPropertyChanged("Data");
        }

        public bool IsVisible
        {
            get { return _isVisible; }
            set
            {
                if (_isVisible == value) return;
                _isVisible = value;
                OnPropertyChanged("IsVisible");
            }
        }

        public double LineWidth
        {
            get { return _lineWidth; }
            set
            {
                if (_lineWidth == value) return;
                _lineWidth = value;
                OnPropertyChanged("LineWidth");
            }
        }

        public ChartLineType LineType
        {
            get { return _lineType; }
            set
            {
                if (_lineType == value) return;
                _lineType = value;
                OnPropertyChanged("LineType");
            }
        }

        public float MarkerSize
        {
            get { return _markerSize; }
            set
            {
                if (_markerSize == value) return;
                _markerSize = value;
                OnPropertyChanged("MarkerSize");
            }
        }

        public int MaxIndex
        {
            get { return _maxIndex; }
            set
            {
                if (_maxIndex == value) return;
                _maxIndex = value;
                Interlocked.Increment(ref _version);
                OnPropertyChanged("MaxIndex");
            }
        }

        /// <summary>
        /// 告诉控件"数据变了，但渲染上界没变"——缓冲写满之后的每一笔新数据都要调它。
        ///
        /// <b>为什么不能只靠 <see cref="MaxIndex"/></b>：环形/滑窗缓冲写满后上界恒为容量 - 1，
        /// setter 的同值守卫会提前返回 → <see cref="Version"/> 不再递增 → 控件的渲染调度器
        /// 判定"没有变化"、不再重绘。那时画面就停在最后一帧上，看起来像"曲线卡住了"。
        ///
        /// 语义上它就是"数据脏"这一件事，与上界无关；分开表达之后，
        /// "写满"这个边界不再是渲染的盲区。
        /// </summary>
        public void MarkAppended()
        {
            Interlocked.Increment(ref _version);
            OnPropertyChanged("MaxIndex");
        }

        public int Version { get { return _version; } }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }
}
