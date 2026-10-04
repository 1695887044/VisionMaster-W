using System;
using System.Collections.Generic;
using System.Text;
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

        /// <summary>
        /// 重复异常日志限流。**日志侧**的限流（<see cref="LogThrottled"/>）与下面的
        /// <see cref="IsThrottled"/> 是两回事：后者只管"别拿同一条消息反复弹窗/刷状态栏"，
        /// 前者管"别把同一条异常逐条写进日志"—— 后者漏了日志，正是 29141 条 WARN 的由来。
        /// </summary>
        private readonly ExceptionThrottle _logThrottle = new();

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
                    // 被合并掉的那部分同样不弹窗：同一条异常刷 50 次/秒时，弹窗比日志更致命
                    if (LogThrottled(prefix, ex))
                        if (!IsThrottled(prefix + ex.Message))
                            ShowNotice(prefix + ex.Message);
                    break;

                case ExceptionSeverity.Warning:
                    if (LogThrottled(prefix, ex))
                        if (!IsThrottled(prefix + ex.Message))
                            WarningRaised?.Invoke(prefix + ex.Message);
                    break;

                case ExceptionSeverity.Critical:
                    // 严重级不设限流：它只出现一次（随后安全停机 + 退出），
                    // 而且现场必须保留完整的 ToString（含堆栈）
                    _log.Error(prefix + ex.ToString());
                    CriticalOccurred?.Invoke(ex);
                    break;
            }
        }

        /// <summary>
        /// 记录日志（重复异常按 <see cref="ExceptionThrottle"/> 的口径合并）。
        /// 返回 true = 这一条**原样**记录了，调用方可以继续做用户提示；被合并/汇总时返回 false。
        /// </summary>
        private bool LogThrottled(string prefix, Exception ex)
        {
            var decision = _logThrottle.Decide(ex.GetType().Name + "|" + prefix + Describe(ex), DateTime.Now);

            // "疑似自激"：单独一条 Error，别混在 WARN 里被忽略 —— 它意味着界面大概率已经无响应
            if (decision.StormHint)
            {
                _log.Error(
                    $"{prefix}【异常风暴】同一条异常在 {_logThrottle.Window.TotalSeconds:0} 秒内已重复 {decision.TotalCount} 次以上，"
                    + "且仍在以极高频率继续。这通常意味着某处在\"自激重试\"（布局/渲染/定时器空转），界面很可能已无响应；"
                    + "请优先排查最近改动过的样式与模板触发器"
                    + "（历史案例：Foreground 被写成 Color 令牌 → TextBlock 测量期抛异常 → 布局无限重试）。");
            }

            if (!decision.ShouldLog) return false;

            if (decision.IsSummary)
            {
                _log.Warn(
                    $"{prefix}（同类异常已合并）{Summarize(Describe(ex))} —— 该异常累计 {decision.TotalCount} 次，"
                    + $"本窗口另有 {decision.SuppressedSinceLastLog} 次未逐条记录（后续仍按此合并，直到不再重复）");
                return false;
            }

            _log.Warn(prefix + Describe(ex));
            return true;
        }

        /// <summary>汇总行里的样例消息截断：风暴期间这条消息可能很长，日志要能一眼扫过</summary>
        private static string Summarize(string text)
            => text.Length <= 160 ? text : text.Substring(0, 160) + "…";

        /// <summary>
        /// 展开异常链：容器解析 / XAML 解析抛出的都是包装异常，真正的原因藏在
        /// <see cref="Exception.InnerException"/> 里，只记 <see cref="Exception.Message"/>
        /// 会让现场日志失去定位能力。无内部异常时输出与 <c>ex.Message</c> 逐字一致，
        /// 不改变既有日志形态。
        /// </summary>
        private static string Describe(Exception ex)
        {
            if (ex.InnerException == null) return ex.Message;

            var sb = new StringBuilder(ex.Message);
            for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
                sb.Append(" ← ").Append(inner.GetType().Name).Append(": ").Append(inner.Message);
            return sb.ToString();
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
