namespace VisionMaster.Scada
{
    /// <summary>
    /// 账号校验出口。Web 运行态模块只引用 <c>VM.Scada</c>，
    /// 看不到宿主工程里的 <c>ScadaUserStore</c>，因此用本接口跨程序集解耦。
    ///
    /// <b>线程</b>：实现方内部负责线程安全（锁内复制记录、锁外做 PBKDF2）；
    /// 调用方应在后台线程调用，**不要**在 UI 线程做十万次哈希。
    /// <b>失败</b>：返回 false 时 <paramref name="error"/> 给用户可读原因；
    /// 不区分"用户不存在"与"密码错误"是刻意的（避免账号枚举）。
    /// </summary>
    public interface IScadaUserAuthenticator
    {
        /// <summary>校验用户名/密码；成功时给出角色</summary>
        bool TryValidate(string? userName, string? password, out ScadaRole role, out string? error);

        /// <summary>
        /// 指定账号是否仍在使用出厂默认密码。Web 启用的门禁之一：
        /// 默认密码未改时拒绝开放操作（只读可以）。
        /// </summary>
        bool IsUsingDefaultPassword(string? userName);
    }
}
