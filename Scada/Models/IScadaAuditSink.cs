namespace VisionMaster.Scada
{
    /// <summary>
    /// 操作审计落盘出口。可选模块（Web 运行态）只引用 <c>VM.Scada</c>，
    /// 看不到宿主工程里的 <c>ScadaAuditWriter</c>，因此用本接口跨程序集解耦。
    ///
    /// <b>为什么用字符串参数而不是直接搬 <c>ScadaAuditEntry</c></b>
    /// ---------
    /// <c>ScadaAuditEntry</c> 现在住在宿主工程，搬进 <c>VM.Scada</c> 会牵动
    /// <c>ScadaActionDispatcher</c>、<c>ScadaChecks</c> 等一批 using。本接口只描述
    /// "一次操作要记什么"，由宿主实现映射到既有 DTO，避免为了一个可选模块
    /// 改公共审计模型的物理位置。
    ///
    /// <b>线程</b>：实现方（<c>ScadaAuditWriter</c>）约定只在 UI 线程写；
    /// 调用方必须把审计写操作投到命令泵的 UI 回调里，不许在网络线程直接调。
    /// <b>失败</b>：返回 false 表示"这次没记上"（磁盘满/权限/文件被占用），
    /// 调用方按既有口径记日志、不阻断动作本身。
    /// </summary>
    public interface IScadaAuditSink
    {
        /// <param name="actor">操作者（已登录用户名；未登录用"未登录"）</param>
        /// <param name="sessionId">会话标识（Web 会话；桌面可为空串）</param>
        /// <param name="clientAddress">客户端地址（Web 会话的远端 IP；桌面可为空串）</param>
        /// <param name="subject">被操作对象（图元名/画面名/变量名）</param>
        /// <param name="eventText">事件名（"点击"/"画面加载"等）</param>
        /// <param name="action">动作描述（"写变量"/"启动流程"/"确认报警"等）</param>
        /// <param name="result">结果（"成功"/"失败"/"拒绝"等）</param>
        /// <param name="detail">补充说明（旧值 → 新值、拒绝原因、错误原文）</param>
        /// <returns>是否成功写入</returns>
        bool Append(
            string actor,
            string sessionId,
            string clientAddress,
            string subject,
            string eventText,
            string action,
            string result,
            string detail);
    }
}
