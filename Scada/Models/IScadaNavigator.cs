using System;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 运行态的<b>切页出口</b>：一条"切换画面"动作要落到哪一页上，由它说了算。
    ///
    /// 它解决的是什么问题
    /// ---------
    /// 动作分发器（<c>ScadaActionDispatcher</c>）在 WPF 侧，它拿得到日志服务、拿得到变量值源，
    /// 却<b>拿不到运行态会话</b>——会话是每次点"运行"时现建的（<c>new ScadaRuntime(document)</c>），
    /// 不是一个能提前注册进容器的单例。于是"切到哪一页"这件事需要一个中介：
    /// 分发器只认识这个接口，接口的实现者手里握着"此刻活着的那一个会话"。
    ///
    /// 为什么接口定在领域层、实现放在 WPF 侧
    /// ---------
    /// 与 <see cref="IScadaValueSource"/> 的理由完全相同：<b>接口描述的是"要什么"，
    /// 实现解决的是"上哪儿拿"</b>。领域层知道"切页要按 Id 优先、名字兜底，只认本方案里的画面"
    /// 这条规则，却不知道会话对象存在哪儿、什么时候被换掉；WPF 侧的宿主知道这些，
    /// 但一旦让它去写"找不到就按名字猜"，规则的副本就多了一份。
    ///
    /// 返回值为什么是"目标状态"而不是"发生了一次切换"
    /// ---------
    /// <see cref="Navigate"/> 的语义是<b>"让运行态显示这一页"</b>，不是"执行一次换页动作"。
    /// 所以目标页<b>本来就在显示</b>时返回 <c>true</c>——它要的最终状态已经成立，而且
    /// 这条路径上不会重发 <see cref="ScadaEventType.Loaded"/>、不会压页栈（见
    /// <see cref="ScadaRuntime.Navigate(Guid, string?, out string?)"/>）。
    /// 这样定有两个好处：
    /// - 分发器不必替运行态猜"是不是点了两下"，日志里也就不会为一次正常的重复点击报警告；
    /// - 调用方（将来的定时器、脚本、外部接口）可以安全地重复下发"显示第 X 页"，天然幂等。
    ///
    /// 实现方的两条契约
    /// ---------
    /// 1) <b>不许抛异常</b>：返回 false 时用 <c>reason</c> 说清为什么，由分发器原样记进日志。
    ///    抛出来会把"一次点击"变成一次崩溃。
    /// 2) <b>无会话时安静返回 false</b>：设计态下属性面板预览、断言里没挂会话，都会调到空实现上。
    /// </summary>
    public interface IScadaNavigator
    {
        /// <summary>
        /// 让运行态显示目标画面。寻址口径<b>Id 优先、名字兜底</b>——与
        /// <see cref="IScadaValueSource.TryResolve"/> 同一套：画面改名后旧配置仍能靠 Id 找到，
        /// 而手写/导入的、只有名字的配置也不至于完全没用。
        /// </summary>
        /// <param name="pageId">目标画面 Id（权威）；<see cref="Guid.Empty"/> 表示没配</param>
        /// <param name="fallbackName">目标画面名（Id 落空时按名找，忽略大小写与首尾空格）</param>
        /// <param name="reason">
        /// 返回 false 时的一句话原因（"方案里没有这张画面" / "运行已经结束"），
        /// 由分发器原样写进日志，与属性面板上的提示口径一致。返回 true 时为 null。
        /// </param>
        /// <returns>目标状态是否已达成（目标页正在显示，或刚刚切成）</returns>
        bool Navigate(Guid pageId, string? fallbackName, out string? reason);
    }
}
