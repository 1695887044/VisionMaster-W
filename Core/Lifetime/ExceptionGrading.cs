using System;
using System.Collections.Generic;
using Core.Interfaces;

namespace VisionMaster.Lifetime
{
    /// <summary>异常影响级别</summary>
    public enum ExceptionSeverity
    {
        /// <summary>提示级：局部功能失败，弹窗告知，不影响运行</summary>
        Notice,

        /// <summary>警告级：功能降级（如单条通讯断开），记日志+状态栏提示，不中断</summary>
        Warning,

        /// <summary>严重级：核心能力受损，记日志 → 安全停机 → 有序退出</summary>
        Critical
    }

    /// <summary>
    /// 运行期异常分级器：三处全局异常入口与业务代码显式调用统一走此分类处置。
    /// </summary>
    public class ExceptionGrading
    {
        private readonly ILogService _log;
        private readonly Dictionary<string, DateTime> _recentMessages = new();
        private static readonly TimeSpan ThrottleWindow = TimeSpan.FromSeconds(5);

        public ExceptionGrading(ILogService log) => _log = log;

        /// <summary>警告级异常抬升（Shell 状态栏等可订阅做非阻断提示）</summary>
        public event Action<string>? WarningRaised;

        /// <summary>严重级异常抬升（AppLifetimeService 订阅后执行安全停机退出）</summary>
        public event Action<Exception>? CriticalOccurred;

        /// <summary>按级别处置异常</summary>
        public void Handle(Exception ex, ExceptionSeverity severity, string context = "")
        {
            string prefix = string.IsNullOrEmpty(context) ? "" : $"[{context}] ";
            switch (severity)
            {
                case ExceptionSeverity.Notice:
                    _log.Warn(prefix + ex.Message);
                    if (!IsThrottled(prefix + ex.Message))
                        ShowNotice(prefix + ex.Message);
                    break;

                case ExceptionSeverity.Warning:
                    _log.Warn(prefix + ex.Message);
                    if (!IsThrottled(prefix + ex.Message))
                        WarningRaised?.Invoke(prefix + ex.Message);
                    break;

                case ExceptionSeverity.Critical:
                    _log.Error(prefix + ex.ToString());
                    CriticalOccurred?.Invoke(ex);
                    break;
            }
        }

        /// <summary>
        /// 全局兜底入口的默认分级：
        /// Dispatcher 未处理异常 → Warning（UI 可继续）；
        /// AppDomain 级 / 未观察 Task → Critical（进程状态已不可信）。
        /// </summary>
        public static ExceptionSeverity ClassifyGlobal(UnhandledExceptionSource source)
            => source == UnhandledExceptionSource.Dispatcher ? ExceptionSeverity.Warning : ExceptionSeverity.Critical;

        public enum UnhandledExceptionSource { Dispatcher, AppDomain, UnobservedTask }

        private bool IsThrottled(string message)
        {
            if (_recentMessages.TryGetValue(message, out var last) && DateTime.Now - last < ThrottleWindow)
                return true;
            _recentMessages[message] = DateTime.Now;
            if (_recentMessages.Count > 64) _recentMessages.Clear(); // 防膨胀
            return false;
        }

        private void ShowNotice(string message)
        {
            System.Windows.MessageBox.Show(message, "提示",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }
}
