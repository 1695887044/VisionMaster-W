using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMaster.Core
{
    /// <summary>
    /// 防抖与节流工具类
    /// </summary>
    public static class DebounceThrottle
    {
        /// <summary>
        /// 防抖：延迟指定时间后执行，如果期间再次触发则重置计时器
        /// </summary>
        /// <param name="action">要执行的操作</param>
        /// <param name="delayMs">延迟时间（毫秒）</param>
        /// <returns>防抖后的函数</returns>
        public static Action Debounce(Action action, int delayMs)
        {
            CancellationTokenSource cts = null;

            return () =>
            {
                cts?.Cancel();
                cts = new CancellationTokenSource();

                Task.Delay(delayMs, cts.Token)
                    .ContinueWith(t =>
                    {
                        if (!t.IsCanceled)
                        {
                            action();
                        }
                    }, TaskScheduler.Default);
            };
        }

        /// <summary>
        /// 节流：指定时间内最多执行一次
        /// </summary>
        /// <param name="action">要执行的操作</param>
        /// <param name="intervalMs">间隔时间（毫秒）</param>
        /// <returns>节流后的函数</returns>
        public static Action Throttle(Action action, int intervalMs)
        {
            long lastExecutionTicks = 0;

            return () =>
            {
                var now = DateTime.UtcNow.Ticks;
                if (now - lastExecutionTicks >= intervalMs * TimeSpan.TicksPerMillisecond)
                {
                    lastExecutionTicks = now;
                    action();
                }
            };
        }
    }
}
