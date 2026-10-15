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

        /// <summary>
        /// 本项兜底超时（毫秒）。超时不等于"检查失败"——见 <see cref="TimeoutLevel"/>：
        /// 执行方抛异常时可自行决定 Error/Warning，而超时由宿主按该项声明的级别处置。
        /// 默认 <see cref="DefaultTimeoutMs"/>；冷启动明显更慢的项（如插件扫描要冷读十几 MB DLL）
        /// 应自行调大，否则会把"磁盘慢"误判成"软件坏了"。
        /// <para>取值必须为正：0 会让 <c>CancellationTokenSource.CancelAfter</c> 立即取消（每轮都报超时），
        /// 负数则是"永不取消"（兜底失效）。</para>
        /// </summary>
        int TimeoutMs => DefaultTimeoutMs;

        /// <summary>
        /// 本项"未在 <see cref="TimeoutMs"/> 内返回"时按什么级别处置。
        /// 默认 <see cref="CheckLevel.Error"/>——超时说明该项没履行"不无限阻塞启动"的契约，按失败阻断。
        /// 只有"该项缺失也不影响主界面可用"的自检才允许放宽成 Warning：
        /// 典型是插件扫描，冷读十几 MB DLL 在慢盘上可能超过任何合理超时，
        /// 但"插件没加载上"是可用性问题，不该等于"软件打不开"。
        /// </summary>
        CheckLevel TimeoutLevel => CheckLevel.Error;

        /// <summary>兜底超时默认值（30 秒）</summary>
        public const int DefaultTimeoutMs = 30_000;

        /// <summary>执行自检。实现应自带超时保护，不无限阻塞启动。</summary>
        Task<CheckResult> ExecuteAsync(CancellationToken ct);
    }
}
