using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Mvvm;

namespace VisionMaster.Communications
{

    public class DataPointHistory : BindableBase
    {
        #region 私有字段

        /// <summary>数据点名称</summary>
        private string _dataPointName = "";

        /// <summary>连接名称</summary>
        private string _connectionName = "";

        /// <summary>最大历史记录数</summary>
        private int _maxHistorySize = 1000;

        /// <summary>历史记录列表</summary>
        private readonly List<DataPointHistoryItem> _history = new();

        /// <summary>线程同步锁</summary>
        private readonly object _lock = new();

        #endregion

        #region 属性

        /// <summary>
        /// <para>获取或设置数据点名称。</para>
        /// </summary>
        public string DataPointName
        {
            get => _dataPointName;
            set => SetProperty(ref _dataPointName, value);
        }

        /// <summary>
        /// <para>获取或设置连接名称。</para>
        /// </summary>
        public string ConnectionName
        {
            get => _connectionName;
            set => SetProperty(ref _connectionName, value);
        }

        /// <summary>
        /// <para>获取或设置最大历史记录数。</para>
        /// <para>当记录数超过此值时，最旧的记录会被删除。</para>
        /// </summary>
        /// <value>最大记录数，默认1000</value>
        public int MaxHistorySize
        {
            get => _maxHistorySize;
            set => SetProperty(ref _maxHistorySize, Math.Max(10, value));
        }

        /// <summary>
        /// <para>获取当前历史记录的条数。</para>
        /// </summary>
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

        #endregion

        #region 公共方法

        /// <summary>
        /// <para>记录一个新的数据值。</para>
        /// </summary>
        /// <param name="value">要记录的值</param>
        /// <remarks>
        /// <para>记录过程：</para>
        /// <list type="number">
        ///   <item>添加带时间戳的新记录</item>
        ///   <item>如果超过最大记录数，删除最旧的记录</item>
        /// </list>
        /// </remarks>
        public void Record(object value)
        {
            lock (_lock)
            {
                _history.Add(new DataPointHistoryItem
                {
                    Timestamp = DateTime.Now,
                    Value = value
                });

                // 保持不超过最大记录数
                while (_history.Count > _maxHistorySize)
                {
                    _history.RemoveAt(0);
                }
            }
        }

        /// <summary>
        /// <para>获取指定时间范围内的历史记录。</para>
        /// </summary>
        /// <param name="start">开始时间</param>
        /// <param name="end">结束时间</param>
        /// <returns>时间范围内的历史记录列表（按时间升序）</returns>
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

        /// <summary>
        /// <para>获取最近N条历史记录。</para>
        /// </summary>
        /// <param name="count">要获取的记录数</param>
        /// <returns>最近的历史记录列表（按时间升序）</returns>
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

        /// <summary>
        /// <para>获取所有历史记录。</para>
        /// </summary>
        /// <returns>所有历史记录列表</returns>
        public IReadOnlyList<DataPointHistoryItem> GetAll()
        {
            lock (_lock)
            {
                return _history.ToList().AsReadOnly();
            }
        }

        /// <summary>
        /// <para>获取指定时间范围内的统计数据。</para>
        /// </summary>
        /// <param name="start">开始时间</param>
        /// <param name="end">结束时间</param>
        /// <returns>统计信息对象</returns>
        public DataPointStatistics GetStatistics(DateTime start, DateTime end)
        {
            lock (_lock)
            {
                // 筛选时间范围内的数值数据
                var items = _history
                    .Where(h => h.Timestamp >= start && h.Timestamp <= end)
                    .Select(h => h.NumericValue)
                    .Where(v => v.HasValue)
                    .Select(v => v!.Value)
                    .ToList();

                // 如果没有数据，返回空统计
                if (items.Count == 0)
                {
                    return new DataPointStatistics
                    {
                        Count = 0,
                        StartTime = start,
                        EndTime = end
                    };
                }

                // 计算统计信息
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

        /// <summary>
        /// <para>清空所有历史记录。</para>
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _history.Clear();
            }
        }

        #endregion
    }

    /// <summary>
    /// <para>历史数据条目，存储单个时间点的数据值。</para>
    /// </summary>
    public class DataPointHistoryItem
    {
        /// <summary>
        /// <para>获取或设置时间戳。</para>
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// <para>获取或设置数据值。</para>
        /// </summary>
        public object? Value { get; set; }

        /// <summary>
        /// <para>尝试将值转换为数值。</para>
        /// </summary>
        /// <returns>如果转换成功返回数值，否则返回null</returns>
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

    /// <summary>
    /// <para>数据点统计信息类，包含历史数据的统计结果。</para>
    /// </summary>
    public class DataPointStatistics
    {
        /// <summary>
        /// <para>获取或设置有效数据点的数量。</para>
        /// </summary>
        public int Count { get; set; }

        /// <summary>
        /// <para>获取或设置最小值。</para>
        /// </summary>
        public double Min { get; set; }

        /// <summary>
        /// <para>获取或设置最大值。</para>
        /// </summary>
        public double Max { get; set; }

        /// <summary>
        /// <para>获取或设置平均值。</para>
        /// </summary>
        public double Average { get; set; }

        /// <summary>
        /// <para>获取或设置统计开始时间。</para>
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// <para>获取或设置统计结束时间。</para>
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// <para>获取统计时间跨度。</para>
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// <para>获取数据范围（最大值-最小值）。</para>
        /// </summary>
        public double Range => Max - Min;

        /// <summary>
        /// <para>获取统计摘要字符串。</para>
        /// </summary>
        /// <returns>格式化的统计信息</returns>
        public override string ToString()
        {
            return $"Count: {Count}, Min: {Min:F2}, Max: {Max:F2}, Avg: {Average:F2}";
        }
    }
}
