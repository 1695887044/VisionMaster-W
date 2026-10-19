namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 运行态二次确认出口：图元在"会动机构 / 会改配置"的操作前问一句"真的要执行吗"。
    ///
    /// 为什么是接口而不是直接调 <c>MessageBox</c>：
    /// 控件库（<c>VM.Scada.Controls</c>）不认识宿主，也不该认识宿主的弹窗实现；
    /// 断言工程要能在无窗口环境里把"确认 / 取消"两条路都跑一遍。
    /// 宿主在 <c>ScadaRuntimeHost</c> 里注入实现（真机走 <c>EasyDialog</c>）。
    ///
    /// 返回 true = 用户确认，false = 取消。实现必须<b>不抛异常</b>；
    /// 图元侧也会兜底 catch，但那是最后一道防线，不是契约。
    /// </summary>
    public interface IScadaConfirmationService
    {
        /// <summary>弹出一次二次确认，返回用户是否确认</summary>
        /// <param name="title">标题（如「示教确认」）</param>
        /// <param name="message">正文（要写清"对哪根轴、做什么、目标是多少"）</param>
        bool Confirm(string title, string message);
    }
}
