using Microsoft.VisualBasic.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VisionMaster.Core
{
    /// <summary>
    /// 自动重试策略（支持指数退避、异常过滤、最大重试次数）
    /// </summary>
    public static class RetryPolicy
    {
        /// <summary>
        /// 执行带重试的操作
        /// </summary>
        /// <param name="action">要执行的操作</param>
        /// <param name="maxRetries">最大重试次数</param>
        /// <param name="initialDelayMs">初始延迟（毫秒）</param>
        /// <param name="useExponentialBackoff">是否使用指数退避</param>
        /// <param name="exceptionFilter">异常过滤器（返回true表示可以重试）</param>
        public static void Execute(Action action, int maxRetries = 3, int initialDelayMs = 100,
            bool useExponentialBackoff = true, Func<Exception, bool> exceptionFilter = null)
        {
            int retryCount = 0;
            while (true)
            {
                try
                {
                    action();
                    return;
                }
                catch (Exception ex)
                {
                    retryCount++;
                    if (retryCount > maxRetries || (exceptionFilter != null && !exceptionFilter(ex)))
                    {
                        throw new InvalidOperationException($"操作失败，已重试{maxRetries}次", ex);
                    }

                    int delay = useExponentialBackoff
                        ? initialDelayMs * (int)Math.Pow(2, retryCount - 1)
                        : initialDelayMs;

                    Thread.Sleep(delay);
                }
            }
        }

        /// <summary>
        /// 执行带重试的函数（有返回值）
        /// </summary>
        public static T Execute<T>(Func<T> func, int maxRetries = 3, int initialDelayMs = 100,
            bool useExponentialBackoff = true, Func<Exception, bool> exceptionFilter = null)
        {
            int retryCount = 0;
            while (true)
            {
                try
                {
                    return func();
                }
                catch (Exception ex)
                {
                    retryCount++;
                    if (retryCount > maxRetries || (exceptionFilter != null && !exceptionFilter(ex)))
                    {
                        throw new InvalidOperationException($"操作失败，已重试{maxRetries}次", ex);
                    }

                    int delay = useExponentialBackoff
                        ? initialDelayMs * (int)Math.Pow(2, retryCount - 1)
                        : initialDelayMs;

                    Thread.Sleep(delay);
                }
            }
        }
    }
}
