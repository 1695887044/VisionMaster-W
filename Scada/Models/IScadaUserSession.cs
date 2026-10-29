namespace VisionMaster.Scada
{
    /// <summary>
    /// 运行态用户会话出口：注销这一件事。
    ///
    /// 为什么只有注销、没有登录
    /// ---------
    /// 登录需要账号密码，而密码<b>不能</b>存在 .vms 里（组态文件会被复制、被发到现场）。
    /// 所以动作「登录」的落地方式是"把登录窗调出来"，由宿主提供那个弹窗（<c>AccessLoginPrompt</c>），
    /// 不需要本接口。注销则相反：它没有任何参数，只要"把当前会话结束"这一个动作，
    /// 于是单独开一个最小接口给分发器用，避免分发器直接依赖主程序里的 <c>ScadaAccessPolicy</c> 具体类。
    ///
    /// 线程：只允许在 UI 线程调用。
    /// </summary>
    public interface IScadaUserSession
    {
        /// <summary>当前是否已登录</summary>
        bool IsLoggedIn { get; }

        /// <summary>当前用户名；未登录为 null</summary>
        string? CurrentUserName { get; }

        /// <summary>结束当前会话；本来就没登录也返回 true（幂等），失败原因写 <paramref name="error"/></summary>
        bool TryLogout(out string? error);
    }
}
