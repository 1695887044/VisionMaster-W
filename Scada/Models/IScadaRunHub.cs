using System;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 运行态生命周期出口：把"当前这一轮运行"的会话、文档与启停信号暴露给可选模块
    /// （SCADA 运行态 Web 客户端、将来的报表/无人值守宿主）。
    ///
    /// <b>为什么单独一个接口，而不是给 <c>IScadaRuntimeHost</c> 加事件</b>
    /// ---------
    /// <c>IScadaRuntimeHost</c> 的语义是"桌面运行窗口策略"（单实例、开窗口、带到前台），
    /// 住在宿主工程里；可选模块只引用 <c>VM.Scada</c>，看不到它。把事件加在它上面还会
    /// 破坏断言工程的 <c>FakeRuntimeHost</c>。本接口只描述"运行态事实"，
    /// 由 <c>ScadaRuntimeHost</c> 同时实现，App 把两个接口映射到同一个单例。
    ///
    /// <b>线程</b>：所有成员只在 UI 线程读写；订阅方若要跨线程使用，自己切线程。
    /// <b>幂等</b>：同一轮运行只发一次 <see cref="RunStarted"/>、一次 <see cref="RunStopped"/>；
    /// 订阅方必须容忍"先 Started 后 Stopped"与"启动失败只发 Stopped 之外的零次"。
    /// </summary>
    public interface IScadaRunHub
    {
        /// <summary>当前是否有一轮运行活着（与桌面运行窗口是否存在同义）</summary>
        bool IsRunning { get; }

        /// <summary>当前运行会话；未运行时为 null</summary>
        ScadaRuntime? Session { get; }

        /// <summary>当前运行文档；未运行时为 null</summary>
        ScadaDocument? Document { get; }

        /// <summary>
        /// 运行轮次。每次成功启动递增；命令信封带它，执行前比对，
        /// 防止"旧方案/旧一轮的命令落到新会话上"。
        /// </summary>
        long RunGeneration { get; }

        /// <summary>
        /// 取"此刻正在跑的那一轮"。给<b>晚订阅者</b>用：先订阅 <see cref="RunStarted"/>，
        /// 再调一次本方法——返回 <c>true</c> 就把三个 out 当作"首帧"，
        /// 这样"订阅晚了一步、这一轮永远拿不到"就不会发生。
        ///
        /// 未运行时返回 <c>false</c>，且三个 out 一律 <c>null / null / 0</c>：
        /// 不返回上一轮的残留，订阅方也就不必自己再判一次"这个会话还活着吗"。
        /// 判据与 <see cref="IsRunning"/> 同源（窗口显示成功之后才算在跑）。
        /// </summary>
        /// <param name="session">当前运行会话；未运行时为 null</param>
        /// <param name="document">当前运行文档；未运行时为 null</param>
        /// <param name="generation">当前轮次；未运行时为 0（与 <see cref="RunGeneration"/> 同源）</param>
        /// <returns>此刻是否有一轮运行活着</returns>
        bool TryGetCurrent(out ScadaRuntime? session, out ScadaDocument? document, out long generation);

        /// <summary>
        /// 一轮运行成功启动（会话已 Start、报警/变量事件已挂、窗口已 Show）。
        /// 参数是会话、文档与本轮 generation；处理器必须自行 try/catch，不得让异常冒回宿主。
        /// </summary>
        event Action<ScadaRuntime, ScadaDocument, long>? RunStarted;

        /// <summary>
        /// 一轮运行结束（窗口关闭、切方案、显式 Stop）。只在确实发过 RunStarted 时发一次；
        /// 参数是本轮 generation。处理器不得在这里读 <see cref="Session"/>——
        /// 发布时字段已清空，需要"最后一帧"就用自己缓存的快照。
        /// </summary>
        event Action<long>? RunStopped;
    }
}
