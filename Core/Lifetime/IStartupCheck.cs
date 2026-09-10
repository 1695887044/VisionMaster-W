using System.Threading;
using System.Threading.Tasks;

namespace VisionMaster.Lifetime
{
    /// <summary>自检结果对启动的影响级别</summary>
    public enum CheckLevel
    {
        /// <summary>失败则阻止进入主界面</summary>
        Error,

        /// <summary>失败仅提示，放行进入主界面（进入后可修复/重连）</summary>
        Warning
    }

    /// <summary>单项自检结果</summary>
    public record CheckResult(bool Passed, CheckLevel Level, string Message)
    {
        public static CheckResult Ok(string message = "") => new(true, CheckLevel.Error, message);
        public static CheckResult Fail(CheckLevel level, string message) => new(false, level, message);
    }

    /// <summary>
    /// 启动自检项：按注册顺序逐项执行，结果实时反馈到 SplashScreen。
    /// Error 级失败中断启动链；Warning 级失败记录后继续。
    /// </summary>
    public interface IStartupCheck
    {
        /// <summary>自检项名称（Splash 列表显示）</summary>
        string Name { get; }

        /// <summary>执行自检。实现应自带超时保护，不无限阻塞启动。</summary>
        Task<CheckResult> ExecuteAsync(CancellationToken ct);
    }
}
