using System;
using VisionMaster.Scada;

namespace VisionMaster.Services
{
    /// <summary>
    /// 把主程序里的 <see cref="ScadaAccessPolicy"/> 适配成领域层认的 <see cref="IScadaUserSession"/>。
    ///
    /// 为什么需要这一层（而不是让 ScadaAccessPolicy 直接实现接口）
    /// ---------
    /// 两者语义并不相同，硬合并会把两个概念焊死：
    /// · <c>ScadaAccessPolicy.CurrentUserName</c> 未登录时给的是「未登录」这句**给人看的文案**，
    ///   而 <see cref="IScadaUserSession.CurrentUserName"/> 的契约是"未登录为 null"——
    ///   分发器要拿它拼日志（"已注销（原用户 张三）"），拿到"未登录"会拼出"原用户 未登录"。
    /// · <c>ScadaAccessPolicy.Logout()</c> 是 void，接口要 <c>bool TryLogout(out error)</c>：
    ///   失败要给可上屏原因，不能抛。
    ///
    /// 于是这里只做两件翻译：把"未登录"归一成 null、把 void 包成 bool + 原因。
    /// 真正的会话状态仍然只有 <see cref="ScadaAccessPolicy"/> 一份（单一事实源）。
    /// </summary>
    public sealed class ScadaUserSessionAdapter : IScadaUserSession
    {
        private readonly ScadaAccessPolicy _policy;

        public ScadaUserSessionAdapter(ScadaAccessPolicy policy)
            => _policy = policy ?? throw new ArgumentNullException(nameof(policy));

        public bool IsLoggedIn => _policy.IsLoggedIn;

        /// <summary>未登录给 null（不把界面文案「未登录」当成用户名带进日志）</summary>
        public string? CurrentUserName => _policy.IsLoggedIn ? _policy.CurrentUserName : null;

        public bool TryLogout(out string? error)
        {
            error = null;

            try
            {
                _policy.Logout();
                return true;
            }
            catch (Exception ex)
            {
                // 注销本身没有业务失败分支，走到这里只可能是策略实现出了意外；
                // 照实报给操作员，不静默当成成功。
                error = $"注销失败：{ex.Message}";
                return false;
            }
        }
    }
}
