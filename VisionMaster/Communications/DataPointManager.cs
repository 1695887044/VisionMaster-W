using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using HslCommunication;

namespace VisionMaster.Communications
{
    public class DataPointManager : IDisposable
    {
        private readonly Dictionary<string, object> _dataPoints = new();
        private readonly Dictionary<string, List<object>> _dataPointByConnection = new();
        private readonly Dictionary<string, (DateTime Timestamp, object? Value)> _valueCache = new();
        private readonly Dictionary<string, System.Timers.Timer> _pollingTimers = new();
        private readonly AdvancedCommunicationManager _commManager;
        private readonly object _lock = new();
        private bool _disposed = false;

        public event EventHandler<DataPointChangedEventArgs>? DataChanged;
        public int DataPointCount => _dataPoints.Count;
        public int CachedValueCount => _valueCache.Count;

        public DataPointManager(AdvancedCommunicationManager commManager)
        {
            _commManager = commManager ?? throw new ArgumentNullException(nameof(commManager));
        }

        public DataPoint<T> RegisterDataPoint<T>(string name, string connectionName, DeviceAddressBase address)
        {
            lock (_lock)
            {
                var key = $"{connectionName}.{name}";
                if (_dataPoints.TryGetValue(key, out var existing))
                    return (DataPoint<T>)existing;

                var dataPoint = new DataPoint<T>(name, address)
                {
                    Quality = DataQuality.NotConnected
                };
                _dataPoints[key] = dataPoint;

                if (!_dataPointByConnection.TryGetValue(connectionName, out var list))
                {
                    list = new List<object>();
                    _dataPointByConnection[connectionName] = list;
                }
                list.Add(dataPoint);

                return dataPoint;
            }
        }

        public DataPoint<T>? GetDataPoint<T>(string name, string connectionName)
        {
            lock (_lock)
            {
                var key = $"{connectionName}.{name}";
                return _dataPoints.TryGetValue(key, out var dp) ? (DataPoint<T>)dp : null;
            }
        }

        public void StartPolling(string connectionName, int intervalMs = 500)
        {
            lock (_lock)
            {
                if (_pollingTimers.ContainsKey(connectionName))
                    return;

                var timer = new System.Timers.Timer(intervalMs);
                timer.Elapsed += (s, e) => PollConnection(connectionName);
                timer.AutoReset = true;
                timer.Start();
                _pollingTimers[connectionName] = timer;
            }
        }

        public void StopPolling(string connectionName)
        {
            lock (_lock)
            {
                if (_pollingTimers.Remove(connectionName, out var timer))
                {
                    timer.Stop();
                    timer.Dispose();
                }
            }
        }

        private void PollConnection(string connectionName)
        {
            if (_disposed) return;

            lock (_lock)
            {
                if (!_dataPointByConnection.TryGetValue(connectionName, out var dataPoints))
                    return;

                var connection = _commManager.GetConnection(connectionName);
                if (connection == null || !connection.IsConnected)
                {
                    foreach (var dpObj in dataPoints)
                    {
                        dynamic dp = dpObj;
                        dp.MarkAsBad("Connection not established");
                    }
                    return;
                }

                foreach (var dpObj in dataPoints)
                {
                    ReadSingleDataPoint(connection, dpObj);
                }
            }
        }

        private void ReadSingleDataPoint(ICommunicationConnection connection, object dataPointObj)
        {
            try
            {
                dynamic dp = dataPointObj;
                string address = dp.Address.Address;
                var cacheKey = $"{connection.GetType().Name}.{address}";

                if (_valueCache.TryGetValue(cacheKey, out var cached))
                {
                    if ((DateTime.Now - cached.Timestamp).TotalMilliseconds < 100)
                    {
                        if (!Equals(dp.Value, cached.Value))
                        {
                            dp.Value = (dynamic)cached.Value!;
                            NotifyDataChanged(dp);
                        }
                        return;
                    }
                }

                object? value = connection.Read<dynamic>(address);
                if (value != null)
                {
                    _valueCache[cacheKey] = (DateTime.Now, value);
                    if (!Equals(dp.Value, value))
                    {
                        dp.Value = (dynamic)value;
                        NotifyDataChanged(dp);
                    }
                }
            }
            catch (Exception ex)
            {
                dynamic dp = dataPointObj;
                dp.MarkAsBad(ex.Message);
            }
        }

        private void NotifyDataChanged(dynamic dataPoint)
        {
            DataChanged?.Invoke(this, new DataPointChangedEventArgs
            {
                DataPointName = dataPoint.Name,
                ConnectionName = dataPoint.Address.ConnectionName,
                NewValue = dataPoint.Value,
                Timestamp = DateTime.Now
            });
        }

        public async System.Threading.Tasks.Task<bool> WriteDataPointAsync<T>(string name, string connectionName, T value)
        {
            lock (_lock)
            {
                var key = $"{connectionName}.{name}";
                if (!_dataPoints.TryGetValue(key, out var dpObj))
                    return false;

                dynamic dp = dpObj;
                try
                {
                    var connection = _commManager.GetConnection(connectionName);
                    if (connection == null || !connection.IsConnected)
                        throw new InvalidOperationException("Connection not established");

                    connection.Write(dp.Address.Address, value);

                    var cacheKey = $"{connectionName}.{dp.Address.Address}";
                    _valueCache[cacheKey] = (DateTime.Now, value);
                    dp.Value = value;

                    NotifyDataChanged(dp);
                    return true;
                }
                catch (Exception ex)
                {
                    dp.MarkAsBad(ex.Message);
                    return false;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var timer in _pollingTimers.Values)
            {
                timer.Stop();
                timer.Dispose();
            }
            _pollingTimers.Clear();
        }
    }

    public class DataPointChangedEventArgs : EventArgs
    {
        public string DataPointName { get; set; } = "";
        public string ConnectionName { get; set; } = "";
        public object? NewValue { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
