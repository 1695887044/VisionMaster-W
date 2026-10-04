using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Windows.Media;

namespace VM.Charts
{
    /// <summary>
    /// 定采样率实时系列:固定容量滑动窗口。
    ///
    /// 用法:采集线程随时调 <see cref="Append"/>(线程安全);
    /// 控件以 ~30ms 周期轮询 <see cref="Version"/>,变了才重绘 ——
    /// VM 与控件之间零跨线程事件、零委托泄漏。
    /// </summary>
    public class SignalSeriesVm : INotifyPropertyChanged, ISeriesVm
    {
        private readonly double[] _buffer;
        private int _count;
        private long _total;
        private int _version;
        private bool _isVisible = true;
        private double _lineWidth = 1.5;
        private ChartLineType _lineType = ChartLineType.Solid;
        private DateTime? _startTime;

        public SignalSeriesVm(string key, string label, Color color, int sampleRate,
            int capacitySeconds = 10, int yAxisIndex = 0)
        {
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException("sampleRate");
            Key = key;
            Label = label;
            Color = color;
            SampleRate = sampleRate;
            YAxisIndex = yAxisIndex;
            _buffer = new double[sampleRate * capacitySeconds];
            // ScottPlot 5 的 DataStreamer 自己管理滚动窗口,
            // 这里缓冲区只作为"最近 N 秒快照"供 VM/控件读取
        }

        public string Key { get; private set; }
        public string Label { get; private set; }
        public Color Color { get; private set; }
        public int SampleRate { get; private set; }
        public int YAxisIndex { get; private set; }
        public long TotalWritten { get { return Interlocked.Read(ref _total); } }

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

        public DateTime? StartTime
        {
            get { return _startTime; }
            set { _startTime = value; OnPropertyChanged("StartTime"); }
        }

        public double[] Buffer { get { return _buffer; } }
        public object Lock { get { return _buffer; } }
        public int Count { get { return Thread.VolatileRead(ref _count); } }
        public int Version { get { return Thread.VolatileRead(ref _version); } }
        public double HeadSeconds { get { return TotalWritten / (double)SampleRate; } }
        public double XOffsetSeconds
        {
            get
            {
                var total = TotalWritten;
                return total > _buffer.Length ? (total - _buffer.Length) / (double)SampleRate : 0;
            }
        }

        /// <summary>写入单个样本(容量满后滑窗丢最旧)。可在任意线程调用。</summary>
        public void Append(double value)
        {
            lock (Lock)
            {
                _total++;
                if (_count < _buffer.Length) _buffer[_count++] = value;
                else
                {
                    Array.Copy(_buffer, 1, _buffer, 0, _buffer.Length - 1);
                    _buffer[_buffer.Length - 1] = value;
                }
                Interlocked.Increment(ref _version);
            }
        }

        /// <summary>批量写入(整批只计一次版本递增)。可在任意线程调用。</summary>
        public void AppendRange(IEnumerable<double> values)
        {
            lock (Lock)
            {
                foreach (var v in values)
                {
                    _total++;
                    if (_count < _buffer.Length) _buffer[_count++] = v;
                    else
                    {
                        Array.Copy(_buffer, 1, _buffer, 0, _buffer.Length - 1);
                        _buffer[_buffer.Length - 1] = v;
                    }
                }
                Interlocked.Increment(ref _version);
            }
        }

        /// <summary>清空数据(开始新一轮采集时用)。</summary>
        public void Clear()
        {
            lock (Lock)
            {
                _count = 0;
                Interlocked.Increment(ref _version);
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
