using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace VisionMaster.Communications
{
    public class ConnectionHealthCheck : IConnectionHealthCheck, IDisposable
    {
        private readonly AdvancedCommunicationManager _commManager;
        private readonly Dictionary<string, System.Timers.Timer> _healthTimers = new();
        private readonly Dictionary<string, HealthCheckResult> _lastResults = new();
        private readonly Dictionary<string, int> _consecutiveFailures = new();
        private readonly object _lock = new();
        private bool _disposed = false;

        public event EventHandler<HealthCheckResult>? HealthCheckCompleted;

        public ConnectionHealthCheck(AdvancedCommunicationManager commManager)
        {
            _commManager = commManager ?? throw new ArgumentNullException(nameof(commManager));
        }

        public async Task<HealthCheckResult> CheckHealthAsync(string connectionName)
        {
            var result = new HealthCheckResult
            {
                CheckedTime = DateTime.Now
            };

            var stopwatch = Stopwatch.StartNew();
            try
            {
                var connection = _commManager.GetConnection(connectionName);
                if (connection == null || !connection.IsConnected)
                {
                    result.IsHealthy = false;
                    result.StatusMessage = "Connection not found or not connected";
                    IncrementFailure(connectionName);
                }
                else
                {
                    try
                    {
                        var testResult = connection.TestConnection();
                        stopwatch.Stop();

                        result.ResponseTimeMs = stopwatch.ElapsedMilliseconds;
                        result.IsHealthy = testResult;

                        if (testResult)
                        {
                            result.StatusMessage = "Healthy";
                            ResetFailure(connectionName);
                        }
                        else
                        {
                            result.StatusMessage = "Connection test failed";
                            IncrementFailure(connectionName);
                        }
                    }
                    catch (Exception ex)
                    {
                        result.IsHealthy = false;
                        result.StatusMessage = $"Exception: {ex.Message}";
                        result.ResponseTimeMs = stopwatch.ElapsedMilliseconds;
                        IncrementFailure(connectionName);
                    }
                }
            }
            catch (Exception ex)
            {
                result.IsHealthy = false;
                result.StatusMessage = $"Error: {ex.Message}";
                result.ResponseTimeMs = stopwatch.ElapsedMilliseconds;
                IncrementFailure(connectionName);
            }

            result.ConsecutiveFailures = GetFailureCount(connectionName);

            lock (_lock)
            {
                _lastResults[connectionName] = result;
            }

            return result;
        }

        public void StartHealthMonitor(string connectionName, int intervalMs = 60000)
        {
            lock (_lock)
            {
                if (_healthTimers.ContainsKey(connectionName))
                    return;

                var timer = new System.Timers.Timer(intervalMs);
                timer.Elapsed += async (s, e) =>
                {
                    var result = await CheckHealthAsync(connectionName);
                    HealthCheckCompleted?.Invoke(this, result);
                };
                timer.AutoReset = true;
                timer.Start();
                _healthTimers[connectionName] = timer;
            }
        }

        public void StopHealthMonitor(string connectionName)
        {
            lock (_lock)
            {
                if (_healthTimers.Remove(connectionName, out var timer))
                {
                    timer.Stop();
                    timer.Dispose();
                }
            }
        }

        public HealthCheckResult? GetLastResult(string connectionName)
        {
            lock (_lock)
            {
                return _lastResults.TryGetValue(connectionName, out var result) ? result : null;
            }
        }

        private void IncrementFailure(string connectionName)
        {
            lock (_lock)
            {
                if (!_consecutiveFailures.ContainsKey(connectionName))
                    _consecutiveFailures[connectionName] = 0;
                _consecutiveFailures[connectionName]++;
            }
        }

        private void ResetFailure(string connectionName)
        {
            lock (_lock)
            {
                _consecutiveFailures[connectionName] = 0;
            }
        }

        private int GetFailureCount(string connectionName)
        {
            lock (_lock)
            {
                return _consecutiveFailures.TryGetValue(connectionName, out var count) ? count : 0;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            lock (_lock)
            {
                foreach (var timer in _healthTimers.Values)
                {
                    timer.Stop();
                    timer.Dispose();
                }
                _healthTimers.Clear();
            }
        }
    }
}
