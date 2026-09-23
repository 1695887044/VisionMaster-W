using System;
using VisionMaster.Scada;

namespace VisionMaster.Services
{
    /// <summary>
    /// <see cref="IScadaNavigator"/> 的默认实现：把"切到哪一页"这句话转交给<b>此刻活着的那一个会话</b>。
    ///
    /// 为什么需要这一层薄适配
    /// ---------
    /// 动作分发器是容器里的单例（它只依赖日志与值源，没有会话），而运行态会话是每次点"运行"时
    /// 现建的（<c>ScadaRuntimeHost.Start</c> 里 <c>new ScadaRuntime(document)</c>）、关窗口就作废。
    /// 一个活得久的单例要调一个活得很短的对象，中间必须有人记住"现在是谁"——本类就是那根线头。
    ///
    /// 与 <c>RegistryScadaValueSource</c> 是同一个角色（薄适配 + 空实现安静返回 false），
    /// 区别只在于值源是从注册表现查，本类是从宿主手里拿现成的会话。
    ///
    /// 什么时候 Attach / 什么时候摘
    /// ---------
    /// - <c>ScadaRuntimeHost.Start</c> 建好会话后立刻 <see cref="Attach"/>；
    /// - 窗口关闭的收尾里 <see cref="Attach"/>(<c>null</c>) 摘掉。
    /// 摘掉这一步不能省：会话作废之后还挂着，一条迟到的动作会去操作一个已经停掉的会话，
    /// 日志上就会出现"运行已经结束"之外看不出毛病的怪现象。
    ///
    /// 线程：只在 UI 线程上读写（宿主与会话同线程），故不加锁。
    /// </summary>
    public sealed class ScadaNavigator : IScadaNavigator
    {
        private ScadaRuntime? _session;

        /// <summary>此刻挂着的会话（没在运行时为 null）；断言与诊断用，产品代码不必读它</summary>
        public ScadaRuntime? Session => _session;

        /// <summary>挂上/摘掉当前会话。<c>null</c> = 运行结束。</summary>
        public void Attach(ScadaRuntime? session) => _session = session;

        /// <inheritdoc/>
        public bool Navigate(Guid pageId, string? fallbackName, out string? reason)
        {
            // 先取到局部变量再判空：Attach(null) 可能就发生在这一行的下一条语句里（关窗口的收尾），
            // 用字段直接调会在那一瞬间炸出空引用。取一次快照，语义上就是"这一次点击归谁处理"。
            var session = _session;

            if (session == null)
            {
                reason = "运行已经结束";
                return false;
            }

            return session.Navigate(pageId, fallbackName, out reason);
        }
    }
}
