namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 一次二次确认请求（P0）。字段与 <c>MessageDialogOptions</c> 一一对应：
    /// <see cref="Title"/> / <see cref="Message"/> / <see cref="Hint"/> / <see cref="Question"/>。
    /// <see cref="Danger"/> 决定图标与主按钮语气（红色），并让默认焦点落在取消。
    /// <see cref="PrimaryText"/> 由动作语义决定（确认停止 / 确认启动 / 确认执行），
    /// 不再让服务端对所有危险动作都写死“确认停止”——否则“确认启动流程？”会配一颗
    /// “确认停止”的按钮。
    /// </summary>
    public sealed record ScadaConfirmationRequest(
        string Title,
        string Message,
        string? Hint = null,
        string? Question = null,
        bool Danger = false,
        string? PrimaryText = null);

    /// <summary>
    /// 运行态二次确认出口：图元在"会动机构 / 会改配置"的操作前问一句"真的要执行吗"。
    ///
    /// 为什么是接口而不是直接调 <c>MessageBox</c>：
    /// 控件库（<c>VM.Scada.Controls</c>）不认识宿主，也不该认识宿主的弹窗实现；
    /// 断言工程要能在无窗口环境里把"确认 / 取消"两条路都跑一遍。
    /// 宿主在 <c>ScadaRuntimeHost</c> 里注入实现（真机走 <c>MessageDialog</c>）。
    ///
    /// 返回 true = 用户确认，false = 取消。实现必须<b>不抛异常</b>；
    /// 图元侧也会兜底 catch，但那是最后一道防线，不是契约。
    /// </summary>
    public interface IScadaConfirmationService
    {
        /// <summary>弹出一次二次确认，返回用户是否确认（旧签名：轴控台等既有调用方）</summary>
        /// <param name="title">标题（如「示教确认」）</param>
        /// <param name="message">正文（要写清"对哪根轴、做什么、目标是多少"）</param>
        bool Confirm(string title, string message);

        /// <summary>
        /// 带危险级别的旧签名重载（P0）。默认实现回落成 <see cref="Confirm(string, string)"/>，
        /// 既有实现者/断言桩不需要改；宿主实现会覆盖它并使用红色主按钮。
        /// </summary>
        bool Confirm(string title, string message, bool danger) => Confirm(title, message);

        /// <summary>
        /// 结构化确认请求（P0）。默认实现回落成标题 + 正文，既有实现者不需要改；
        /// 宿主实现覆盖它，把 Hint/Question/Danger 映射到 MessageDialog。
        /// </summary>
        bool Confirm(ScadaConfirmationRequest request) => Confirm(request.Title, request.Message);
    }
}
