namespace VisionMaster.Scada
{
    /// <summary>
    /// <b>"没人挂会话"时的那一套答案</b>：未登录 = 操作员，不限制的图元一律放行。
    ///
    /// 它解决的是什么问题
    /// ---------
    /// <see cref="ScadaRuntime"/> 的权限闸门必须<b>永远问得到一个答案</b>。没有这个东西，
    /// 每个调用点都得写一遍"策略为 null 怎么办"，而那条分支的每一份副本都是
    /// "某个入口忘了判权限"的潜在缺口——<b>漏判权限的默认结果必须是拒绝，不能是放行</b>。
    /// 所以这里不写 <c>if (policy != null)</c>，而是给一个空对象（Null Object）：
    /// 判定路径永远只有一条，<see cref="ScadaRuntime"/> 里也就不会出现"某条路径绕过了权限"。
    ///
    /// 什么时候真的会用到它
    /// ---------
    /// - 无界面的检查（<c>ScadaChecks</c>）：建会话时只关心导航与事件，不关心登录。
    /// - 设计态预览、将来任何不接用户系统的宿主。
    /// - WPF 应用里也<b>不该</b>用到它——那边 <c>App</c> 会注册带登录状态的那一份实现；
    ///   真用到了说明接线漏了，表现是"配了工程师权限的按钮，操作员也能按"，
    ///   这是本类刻意选择的失败方向：宁可让人发现"权限没生效"，也不能让人以为"权限生效了"。
    ///
    /// 为什么是<b>单例</b>
    /// ---------
    /// 它没有任何状态（三个属性都是常量），每次 new 一个只是白白让 GC 忙。
    /// 构造函数私有，逼所有调用方走 <see cref="Instance"/>，也就逼出了"这玩意儿无状态"这个事实。
    /// </summary>
    public sealed class DefaultScadaAccessPolicy : IScadaAccessPolicy
    {
        /// <summary>全局唯一的那一份（无状态，线程安全）</summary>
        public static readonly DefaultScadaAccessPolicy Instance = new();

        private DefaultScadaAccessPolicy()
        {
        }

        /// <summary>未登录 = 操作员（理由见 <see cref="IScadaAccessPolicy.CurrentRole"/>）</summary>
        public ScadaRole CurrentRole => ScadaRole.Operator;

        /// <summary>永远是 false：这个实现的存在前提就是"没有登录这回事"</summary>
        public bool IsLoggedIn => false;

        /// <summary>
        /// 固定回落成"未登录"。<b>不</b>返回空串：审计文件里一个空白的用户名列，
        /// 等于那条记录白记了（口径见 <see cref="IScadaAccessPolicy.CurrentUserName"/>）。
        /// </summary>
        public string CurrentUserName => "未登录";

        /// <inheritdoc/>
        public string LoginStateText => $"{CurrentUserName}（{CurrentRole.DisplayName()}）";

        /// <inheritdoc/>
        public bool CanOperate(ScadaRole? required, out string? reason)
            => CurrentRole.Allows(required, out reason);
    }
}
