using System;
using System.Threading.Tasks;
using Prism.Mvvm;

namespace VisionMaster.Communications
{
    public class HealthCheckResult : BindableBase
    {
        private bool _isHealthy;
        private double _responseTimeMs;
        private int _consecutiveFailures;
        private string _statusMessage = "";
        private DateTime _checkedTime;

        public bool IsHealthy
        {
            get => _isHealthy;
            set => SetProperty(ref _isHealthy, value);
        }

        public double ResponseTimeMs
        {
            get => _responseTimeMs;
            set => SetProperty(ref _responseTimeMs, value);
        }

        public int ConsecutiveFailures
        {
            get => _consecutiveFailures;
            set => SetProperty(ref _consecutiveFailures, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public DateTime CheckedTime
        {
            get => _checkedTime;
            set => SetProperty(ref _checkedTime, value);
        }
    }

    public interface IConnectionHealthCheck
    {
        Task<HealthCheckResult> CheckHealthAsync(string connectionName);
        void StartHealthMonitor(string connectionName, int intervalMs = 60000);
        void StopHealthMonitor(string connectionName);
        HealthCheckResult? GetLastResult(string connectionName);
    }
}
