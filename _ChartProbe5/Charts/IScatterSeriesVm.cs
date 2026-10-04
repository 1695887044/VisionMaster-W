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
        public Color Color { get; private set; }
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

        public int Version { get { return _version; } }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }
}
