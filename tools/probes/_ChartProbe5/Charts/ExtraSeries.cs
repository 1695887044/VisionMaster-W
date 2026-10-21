using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Media;

namespace VM.Charts
{
    /// <summary>
    /// 扩展图形系列的通用契约(柱状/饼图/直方图/函数/填充/热力图)。
    /// 与 ISeriesVm(实时信号)、IScatterSeriesVm(XY 数组)并列,
    /// 数据一般在构造时给定,IsVisible 支持热更新。
    /// </summary>
    public interface IExtraSeriesVm : INotifyPropertyChanged
    {
        string Key { get; }
        string Label { get; }
        bool IsVisible { get; set; }
    }

    /// <summary>柱状图:values 为各柱高度;positions 为 null 时按 0..n-1 排列。</summary>
    public class BarSeriesVm : IExtraSeriesVm
    {
        private bool _isVisible = true;

        public BarSeriesVm(string key, string label, Color color, double[] values)
        {
            Key = key;
            Label = label;
            Color = color;
            Values = values;
        }

        public string Key { get; private set; }
        public string Label { get; private set; }
        public Color Color { get; private set; }

        /// <summary>各柱高度</summary>
        public double[] Values { get; private set; }

        /// <summary>各柱 X 位置;null = 0..n-1</summary>
        public double[] Positions { get; set; }

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

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>饼图/环图的一个扇区</summary>
    public class PieSliceVm : INotifyPropertyChanged
    {
        private double _value;
        private bool _isVisible = true;

        public PieSliceVm(string label, double value, Color color)
        {
            Label = label;
            Value = value;
            Color = color;
        }

        public string Label { get; private set; }
        public Color Color { get; private set; }

        public double Value
        {
            get { return _value; }
            set
            {
                if (_value == value) return;
                _value = value;
                OnPropertyChanged("Value");
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

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>
    /// 饼图/环图:DonutFraction = 0 画饼图,0.5 画环形图;
    /// 扇区值变化会实时反映(控件把 Slice.Value 同步回 plottable)。
    /// </summary>
    public class PieSeriesVm : IExtraSeriesVm
    {
        private bool _isVisible = true;
        private double _donutFraction;

        public PieSeriesVm(string key, IList<PieSliceVm> slices)
        {
            Key = key;
            Label = key;
            Slices = slices;
            foreach (var s in Slices)
                s.PropertyChanged += (s2, e2) =>
                {
                    if (e2.PropertyName == "Value") OnPropertyChanged("Data");
                };
        }

        public string Key { get; private set; }

        private string _label;
        public string Label
        {
            get { return _label; }
            set
            {
                _label = value;
                OnPropertyChanged("Label");
            }
        }

        public IList<PieSliceVm> Slices { get; private set; }

        /// <summary>0 = 饼图;0.5 = 环形图</summary>
        public double DonutFraction
        {
            get { return _donutFraction; }
            set { _donutFraction = value; OnPropertyChanged("DonutFraction"); }
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

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>直方图:控件内自动分箱(等宽 bins),画成柱状。</summary>
    public class HistogramSeriesVm : IExtraSeriesVm
    {
        private bool _isVisible = true;

        public HistogramSeriesVm(string key, string label, Color color, double[] values, int binCount)
        {
            Key = key;
            Label = label;
            Color = color;
            Values = values;
            BinCount = binCount;
        }

        public string Key { get; private set; }
        public string Label { get; private set; }
        public Color Color { get; private set; }

        /// <summary>原始样本数据</summary>
        public double[] Values { get; private set; }

        /// <summary>分箱数量</summary>
        public int BinCount { get; private set; }

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

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>函数曲线:y = F(x),渲染范围跟随轴(自动适配窗口)。</summary>
    public class FunctionSeriesVm : IExtraSeriesVm
    {
        private bool _isVisible = true;
        private double _lineWidth = 1.5;
        private ChartLineType _lineType = ChartLineType.Dash;

        public FunctionSeriesVm(string key, string label, Color color, Func<double, double> function)
        {
            Key = key;
            Label = label;
            Color = color;
            Function = function;
        }

        public string Key { get; private set; }
        public string Label { get; private set; }
        public Color Color { get; private set; }

        /// <summary>y = F(x);仅依赖 x,不依赖外部状态</summary>
        public Func<double, double> Function { get; private set; }

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
            set { _lineWidth = value; OnPropertyChanged("LineWidth"); }
        }

        public ChartLineType LineType
        {
            get { return _lineType; }
            set { _lineType = value; OnPropertyChanged("LineType"); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>区域填充:两条曲线(上下界)之间的区域着色,常用于公差带/包络。</summary>
    public class FillYSeriesVm : IExtraSeriesVm
    {
        private bool _isVisible = true;

        public FillYSeriesVm(string key, string label, Color fillColor,
            double[] xs, double[] ysTop, double[] ysBottom)
        {
            Key = key;
            Label = label;
            FillColor = fillColor;
            Xs = xs;
            YsTop = ysTop;
            YsBottom = ysBottom;
        }

        public string Key { get; private set; }
        public string Label { get; private set; }

        /// <summary>填充色(描边色自动取同色)</summary>
        public Color FillColor { get; private set; }

        public double[] Xs { get; private set; }
        public double[] YsTop { get; private set; }
        public double[] YsBottom { get; private set; }

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

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>热力图:二维强度矩阵;数据变更后调 <see cref="Update"/> 刷新。</summary>
    public class HeatmapSeriesVm : IExtraSeriesVm
    {
        private bool _isVisible = true;

        public HeatmapSeriesVm(string key, double[,] intensities, string colormap = "Viridis")
        {
            Key = key;
            Intensities = intensities;
            ColormapName = colormap;
        }

        public string Key { get; private set; }
        public string Label { get; private set; }
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

        /// <summary>二维强度矩阵(行×列),可在任意线程原址修改后调 Update()</summary>
        public double[,] Intensities { get; private set; }

        /// <summary>配色方案名:Viridis / Plasma / Inferno / Turbo / Magma</summary>
        public string ColormapName { get; set; }

        /// <summary>平滑插值(false = 像素块)</summary>
        public bool Smooth { get; set; } = true;

        /// <summary>数据变更后调用,触发重绘</summary>
        public void Update()
        {
            OnPropertyChanged("Data");
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }
}
