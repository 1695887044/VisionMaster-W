using Microsoft.VisualBasic.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Core
{
    /// <summary>
    /// 硬件心跳检测器（支持多设备、自动重连、分级告警）
    /// </summary>
    public sealed class HardwareHeartbeatMonitor : IDisposable
    {
        private static readonly Lazy<HardwareHeartbeatMonitor> _instance = new(() => new HardwareHeartbeatMonitor());
        public static HardwareHeartbeatMonitor Instance => _instance.Value;

        private readonly ConcurrentDictionary<string, HardwareHeartbeatConfig> _devices = new();
        private Timer _globalTimer;
        private bool _disposed;

        /// <summary>
        /// 硬件状态变更事件
        /// </summary>
        public event EventHandler<HardwareStatusChangedEventArgs> HardwareStatusChanged;

        private HardwareHeartbeatMonitor()
        {
            // 默认每5秒执行一次全局心跳检测
            _globalTimer = new Timer(OnGlobalHeartbeatTick, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        }

        /// <summary>
        /// 注册需要监控的硬件
        /// </summary>
        /// <param name="deviceId">设备唯一ID</param>
        /// <param name="deviceName">设备名称</param>
        /// <param name="heartbeatFunc">心跳检测函数（返回true表示正常）</param>
        /// <param name="reconnectFunc">重连函数（返回true表示重连成功）</param>
        /// <param name="checkIntervalSec">检测间隔（秒）</param>
        /// <param name="maxRetries">最大重连次数（-1表示无限重试）</param>
        public void RegisterDevice(string deviceId, string deviceName,
            Func<bool> heartbeatFunc, Func<bool> reconnectFunc,
            int checkIntervalSec = 10, int maxRetries = 5)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(HardwareHeartbeatMonitor));

            var config = new HardwareHeartbeatConfig
            {
                DeviceId = deviceId,
                DeviceName = deviceName,
                HeartbeatFunc = heartbeatFunc,
                ReconnectFunc = reconnectFunc,
                CheckIntervalSec = checkIntervalSec,
                MaxRetries = maxRetries,
                Status = HardwareStatus.Normal
            };

            _devices.TryAdd(deviceId, config);
           // Log.Info($"注册硬件心跳监控: {deviceName} ({deviceId})");
        }

        /// <summary>
        /// 注销硬件监控
        /// </summary>
        public void UnregisterDevice(string deviceId)
        {
            if (_devices.TryRemove(deviceId, out _))
            {
                //Log.Info($"注销硬件心跳监控: {deviceId}");
            }
        }

        /// <summary>
        /// 手动触发所有硬件心跳检测
        /// </summary>
        public void ForceCheckAll()
        {
            OnGlobalHeartbeatTick(null);
        }

        private void OnGlobalHeartbeatTick(object state)
        {
            if (_disposed) return;

            Parallel.ForEach(_devices.Values, config =>
            {
                try
                {
                    CheckDeviceHeartbeat(config);
                }
                catch (Exception ex)
                {
                   // Log.Error($"硬件心跳检测异常 [{config.DeviceName}]: {ex.Message}", ex);
                }
            });
        }

        private void CheckDeviceHeartbeat(HardwareHeartbeatConfig config)
        {
            var now = DateTime.UtcNow;
            if ((now - config.LastCheckTime).TotalSeconds < config.CheckIntervalSec)
            {
                return;
            }

            config.LastCheckTime = now;

            // 执行心跳检测
            bool isAlive = false;
            try
            {
                isAlive = config.HeartbeatFunc();
            }
            catch (Exception ex)
            {
               // Log.Debug($"硬件心跳检测失败 [{config.DeviceName}]: {ex.Message}");
            }

            if (isAlive)
            {
                // 恢复正常
                if (config.Status != HardwareStatus.Normal)
                {
                    config.Status = HardwareStatus.Normal;
                    config.ConsecutiveFailures = 0;
                    OnHardwareStatusChanged(config, HardwareStatus.Normal);
                   // Log.Info($"硬件恢复正常 [{config.DeviceName}]");
                }
                return;
            }

            // 心跳失败
            config.ConsecutiveFailures++;
            //Log.Warn($"硬件心跳失败 [{config.DeviceName}]，连续失败次数: {config.ConsecutiveFailures}");

            // 分级处理
            if (config.ConsecutiveFailures == 1)
            {
                // 第一次失败，警告
                config.Status = HardwareStatus.Warning;
                OnHardwareStatusChanged(config, HardwareStatus.Warning);
            }
            else if (config.ConsecutiveFailures >= 3)
            {
                // 连续3次失败，尝试重连
                config.Status = HardwareStatus.Disconnected;
                OnHardwareStatusChanged(config, HardwareStatus.Disconnected);

                if (config.MaxRetries == -1 || config.ReconnectAttempts < config.MaxRetries)
                {
                    config.ReconnectAttempts++;
                   // Log.Info($"开始重连硬件 [{config.DeviceName}]，第 {config.ReconnectAttempts} 次尝试");

                    bool reconnectSuccess = false;
                    try
                    {
                        reconnectSuccess = RetryPolicy.Execute(
                            () => config.ReconnectFunc(),
                            maxRetries: 2,
                            initialDelayMs: 500);
                    }
                    catch (Exception ex)
                    {
                       // Log.Error($"硬件重连失败 [{config.DeviceName}]: {ex.Message}", ex);
                    }

                    if (reconnectSuccess)
                    {
                        config.Status = HardwareStatus.Normal;
                        config.ConsecutiveFailures = 0;
                        config.ReconnectAttempts = 0;
                        OnHardwareStatusChanged(config, HardwareStatus.Normal);
                       // Log.Info($"硬件重连成功 [{config.DeviceName}]");
                    }
                    else
                    {
                       // Log.Error($"硬件重连失败 [{config.DeviceName}]，已尝试 {config.ReconnectAttempts} 次");
                    }
                }
                else
                {
                    // 超过最大重连次数，标记为故障
                    config.Status = HardwareStatus.Fault;
                    OnHardwareStatusChanged(config, HardwareStatus.Fault);
                   // Log.Fatal($"硬件故障 [{config.DeviceName}]，已超过最大重连次数");
                }
            }
        }

        private void OnHardwareStatusChanged(HardwareHeartbeatConfig config, HardwareStatus newStatus)
        {
            HardwareStatusChanged?.Invoke(this, new HardwareStatusChangedEventArgs(
                config.DeviceId, config.DeviceName, newStatus, config.ConsecutiveFailures));
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _globalTimer?.Dispose();
            _devices.Clear();
        }

        #region 辅助类
        private class HardwareHeartbeatConfig
        {
            public string DeviceId { get; set; }
            public string DeviceName { get; set; }
            public Func<bool> HeartbeatFunc { get; set; }
            public Func<bool> ReconnectFunc { get; set; }
            public int CheckIntervalSec { get; set; }
            public int MaxRetries { get; set; }
            public HardwareStatus Status { get; set; }
            public int ConsecutiveFailures { get; set; }
            public int ReconnectAttempts { get; set; }
            public DateTime LastCheckTime { get; set; }
        }

        public enum HardwareStatus
        {
            Normal,
            Warning,
            Disconnected,
            Fault
        }

        public class HardwareStatusChangedEventArgs : EventArgs
        {
            public string DeviceId { get; }
            public string DeviceName { get; }
            public HardwareStatus NewStatus { get; }
            public int ConsecutiveFailures { get; }

            public HardwareStatusChangedEventArgs(string deviceId, string deviceName, HardwareStatus newStatus, int consecutiveFailures)
            {
                DeviceId = deviceId;
                DeviceName = deviceName;
                NewStatus = newStatus;
                ConsecutiveFailures = consecutiveFailures;
            }
        }
        #endregion
    }
}
