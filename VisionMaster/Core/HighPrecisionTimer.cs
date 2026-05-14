using Microsoft.VisualBasic.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Core
{
    /// <summary>
    /// Windows高精度多媒体定时器（精度1ms以内）
    /// </summary>
    public sealed class HighPrecisionTimer : IDisposable
    {
        [DllImport("winmm.dll")]
        private static extern uint timeSetEvent(uint delay, uint resolution, TimerCallback callback, nint userData, uint eventType);

        [DllImport("winmm.dll")]
        private static extern uint timeKillEvent(uint timerId);

        private delegate void TimerCallback(uint id, uint msg, nint user, nint param1, nint param2);

        private const uint TIME_ONESHOT = 0;
        private const uint TIME_PERIODIC = 1;

        private uint _timerId;
        private readonly TimerCallback _callback;
        private Action _tickAction;
        private bool _disposed;

        /// <summary>
        /// 创建高精度定时器
        /// </summary>
        /// <param name="intervalMs">间隔时间（毫秒）</param>
        /// <param name="tickAction">定时器回调</param>
        /// <param name="autoStart">是否自动启动</param>
        public HighPrecisionTimer(int intervalMs, Action tickAction, bool autoStart = true)
        {
            if (intervalMs < 1) throw new ArgumentOutOfRangeException(nameof(intervalMs), "间隔时间不能小于1ms");

            _tickAction = tickAction ?? throw new ArgumentNullException(nameof(tickAction));
            _callback = TimerProc;

            if (autoStart)
            {
                Start(intervalMs);
            }
        }

        /// <summary>
        /// 启动定时器
        /// </summary>
        public void Start(int intervalMs)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(HighPrecisionTimer));
            if (_timerId != 0) Stop();

            _timerId = timeSetEvent((uint)intervalMs, 0, _callback, nint.Zero, TIME_PERIODIC);
            if (_timerId == 0)
            {
                throw new InvalidOperationException("创建高精度定时器失败");
            }
        }

        /// <summary>
        /// 停止定时器
        /// </summary>
        public void Stop()
        {
            if (_timerId != 0)
            {
                timeKillEvent(_timerId);
                _timerId = 0;
            }
        }

        private void TimerProc(uint id, uint msg, nint user, nint param1, nint param2)
        {
            try
            {
                _tickAction();
            }
            catch (Exception ex)
            {
                // 定时器回调异常不能抛出，否则会导致进程崩溃
              //  Log.Error($"高精度定时器异常: {ex.Message}", ex);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _tickAction = null;
        }
    }
}
