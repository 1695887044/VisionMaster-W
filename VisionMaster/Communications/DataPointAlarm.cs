using System;
using System.ComponentModel;
using Prism.Mvvm;
using UI.Attributes;

namespace VisionMaster.Communications
{
    public enum AlarmLevel
    {
        Normal,
        HighHigh,
        High,
        Low,
        LowLow
    }

    public class DataPointAlarm : BindableBase
    {
        private bool _isEnabled = false;
        private double _highHigh = double.NaN;
        private double _high = double.NaN;
        private double _low = double.NaN;
        private double _lowLow = double.NaN;
        private bool _highHighEnabled = false;
        private bool _highEnabled = false;
        private bool _lowEnabled = false;
        private bool _lowLowEnabled = false;

        [Category("报警配置"), SuperDisplay(Name = "启用报警")]
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        [Category("上限报警"), SuperDisplay(Name = "启用上上限")]
        public bool HighHighEnabled
        {
            get => _highHighEnabled;
            set => SetProperty(ref _highHighEnabled, value);
        }

        [Category("上限报警"), SuperDisplay(Name = "上上限值")]
        public double HighHigh
        {
            get => _highHigh;
            set => SetProperty(ref _highHigh, value);
        }

        [Category("上限报警"), SuperDisplay(Name = "启用上限")]
        public bool HighEnabled
        {
            get => _highEnabled;
            set => SetProperty(ref _highEnabled, value);
        }

        [Category("上限报警"), SuperDisplay(Name = "上限值")]
        public double High
        {
            get => _high;
            set => SetProperty(ref _high, value);
        }

        [Category("下限报警"), SuperDisplay(Name = "启用下限")]
        public bool LowEnabled
        {
            get => _lowEnabled;
            set => SetProperty(ref _lowEnabled, value);
        }

        [Category("下限报警"), SuperDisplay(Name = "下限值")]
        public double Low
        {
            get => _low;
            set => SetProperty(ref _low, value);
        }

        [Category("下限报警"), SuperDisplay(Name = "启用下下限")]
        public bool LowLowEnabled
        {
            get => _lowLowEnabled;
            set => SetProperty(ref _lowLowEnabled, value);
        }

        [Category("下限报警"), SuperDisplay(Name = "下下限值")]
        public double LowLow
        {
            get => _lowLow;
            set => SetProperty(ref _lowLow, value);
        }

        public AlarmLevel Check(double value)
        {
            if (!IsEnabled) return AlarmLevel.Normal;

            if (HighHighEnabled && !double.IsNaN(HighHigh) && value >= HighHigh)
                return AlarmLevel.HighHigh;

            if (HighEnabled && !double.IsNaN(High) && value >= High)
                return AlarmLevel.High;

            if (LowEnabled && !double.IsNaN(Low) && value <= Low)
                return AlarmLevel.Low;

            if (LowLowEnabled && !double.IsNaN(LowLow) && value <= LowLow)
                return AlarmLevel.LowLow;

            return AlarmLevel.Normal;
        }

        public AlarmLevel Check(object? value)
        {
            if (value == null) return AlarmLevel.Normal;
            try
            {
                double dValue = Convert.ToDouble(value);
                return Check(dValue);
            }
            catch
            {
                return AlarmLevel.Normal;
            }
        }

        public string GetAlarmMessage(double value)
        {
            var level = Check(value);
            return level switch
            {
                AlarmLevel.HighHigh => $"上上限报警: {value} >= {HighHigh}",
                AlarmLevel.High => $"上限报警: {value} >= {High}",
                AlarmLevel.Low => $"下限报警: {value} <= {Low}",
                AlarmLevel.LowLow => $"下下限报警: {value} <= {LowLow}",
                _ => string.Empty
            };
        }
    }

    public class DataPointAlarmEventArgs : EventArgs
    {
        public string DataPointName { get; set; } = "";
        public string ConnectionName { get; set; } = "";
        public AlarmLevel Level { get; set; }
        public double Value { get; set; }
        public string Message { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
