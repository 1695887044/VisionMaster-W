using System;
using System.ComponentModel;
using System.Windows.Media;

namespace VM.Charts
{
    /// <summary>
    /// 仪表盘单项:值 + 标签 + 颜色。
    /// 多个 GaugeVm 组成 <see cref="ChartView.GaugesSource"/>,
    /// Value 变化(可在任意线程)驱动仪表实时摆动。
    /// </summary>
    public class GaugeVm : INotifyPropertyChanged
    {
        private double _value;
        private string _label;
        private Color _color = Color.FromRgb(0x0E, 0x8A, 0x5F);
        private bool _isVisible = true;

        public GaugeVm(string key, string label, double value = 0)
        {
            Key = key;
            _label = label;
            _value = value;
        }

        public string Key { get; private set; }

        public string Label
        {
            get { return _label; }
            set
            {
                if (_label == value) return;
                _label = value;
                OnPropertyChanged("Label");
            }
        }

        /// <summary>当前值(量程默认 0~100,超出会满圈)。</summary>
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
}
