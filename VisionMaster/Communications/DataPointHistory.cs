using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Mvvm;

namespace VisionMaster.Communications
{
    public class DataPointHistory : BindableBase
    {
        private string _dataPointName = "";
        private string _connectionName = "";
        private int _maxHistorySize = 1000;
        private readonly List<DataPointHistoryItem> _history = new();
        private readonly object _lock = new();

        public string DataPointName
        {
            get => _dataPointName;
            set => SetProperty(ref _dataPointName, value);
        }

        public string ConnectionName
        {
            get => _connectionName;
            set => SetProperty(ref _connectionName, value);
        }

        public int MaxHistorySize
        {
            get => _maxHistorySize;
            set => SetProperty(ref _maxHistorySize, value);
        }

        public int CurrentCount
        {
            get
            {
                lock (_lock)
                {
                    return _history.Count;
                }
            }
        }

        public void Record(object value)
        {
            lock (_lock)
            {
                _history.Add(new DataPointHistoryItem
                {
                    Timestamp = DateTime.Now,
                    Value = value
                });

                while (_history.Count > _maxHistorySize)
                {
                    _history.RemoveAt(0);
                }
            }
        }

        public IReadOnlyList<DataPointHistoryItem> GetHistory(DateTime start, DateTime end)
        {
            lock (_lock)
            {
                return _history
                    .Where(h => h.Timestamp >= start && h.Timestamp <= end)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public IReadOnlyList<DataPointHistoryItem> GetRecent(int count)
        {
            lock (_lock)
            {
                return _history
                    .OrderByDescending(h => h.Timestamp)
                    .Take(count)
                    .OrderBy(h => h.Timestamp)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public IReadOnlyList<DataPointHistoryItem> GetAll()
        {
            lock (_lock)
            {
                return _history.ToList().AsReadOnly();
            }
        }

        public DataPointStatistics GetStatistics(DateTime start, DateTime end)
        {
            lock (_lock)
            {
                var items = _history
                    .Where(h => h.Timestamp >= start && h.Timestamp <= end)
                    .Select(h => h.NumericValue)
                    .Where(v => v.HasValue)
                    .Select(v => v!.Value)
                    .ToList();

                if (items.Count == 0)
                {
                    return new DataPointStatistics
                    {
                        Count = 0,
                        StartTime = start,
                        EndTime = end
                    };
                }

                return new DataPointStatistics
                {
                    Count = items.Count,
                    Min = items.Min(),
                    Max = items.Max(),
                    Average = items.Average(),
                    StartTime = start,
                    EndTime = end,
                    Duration = end - start
                };
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _history.Clear();
            }
        }
    }

    public class DataPointHistoryItem
    {
        public DateTime Timestamp { get; set; }
        public object? Value { get; set; }

        public double? NumericValue
        {
            get
            {
                if (Value == null) return null;
                try
                {
                    return Convert.ToDouble(Value);
                }
                catch
                {
                    return null;
                }
            }
        }
    }

    public class DataPointStatistics
    {
        public int Count { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public double Average { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration { get; set; }

        public double Range => Max - Min;
    }
}
