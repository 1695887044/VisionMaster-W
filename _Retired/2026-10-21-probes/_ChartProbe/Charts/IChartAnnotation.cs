using System;
using System.ComponentModel;
using System.Windows.Media;

namespace VM.Charts
{
    /// <summary>图上附加元素(阈值线/事件标记)的契约,与系列同样按 Key 增量同步。</summary>
    public interface IChartAnnotation : INotifyPropertyChanged
    {
        string Key { get; }
        string Label { get; }
        Color Color { get; }
        bool IsVisible { get; set; }
        ChartLineType LineType { get; set; }
    }

    /// <summary>水平阈值线:超限判断的可视化(一条横线 + 图例文字)。</summary>
    public class ThresholdLineVm : IChartAnnotation
    {
        private double _y;
        private bool _isVisible = true;
        private ChartLineType _lineType = ChartLineType.Dash;

        public string Key { get; set; }
        public string Label { get; set; }
        public Color Color { get; set; } = Color.FromRgb(0xC5, 0x0F, 0x1F);
        public double LineWidth { get; set; } = 1.2;

        /// <summary>阈值所在 Y 值(数据坐标)。改这个属性线会实时移动。</summary>
        public double Y
        {
            get { return _y; }
            set
            {
                if (_y == value) return;
                _y = value;
                OnPropertyChanged("Y");
            }
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

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>事件标记:某个时刻发生的动作(报警触发/开始保存…)在时间轴上的竖线。</summary>
    public class EventMarkerVm : IChartAnnotation
    {
        private double _x;
        private bool _isVisible = true;
        private ChartLineType _lineType = ChartLineType.Dash;

        public string Key { get; set; }
        public string Label { get; set; }
        public Color Color { get; set; } = Color.FromRgb(0x7A, 0x50, 0xC8);
        public double LineWidth { get; set; } = 1.2;

        /// <summary>事件发生的 X 位置(秒;若系列设置了 StartTime 则与绝对时刻对应)。</summary>
        public double X
        {
            get { return _x; }
            set
            {
                if (_x == value) return;
                _x = value;
                OnPropertyChanged("X");
            }
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

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }
}
