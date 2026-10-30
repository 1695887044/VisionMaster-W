using System;
using VisionMaster.Scada;

namespace VisionMaster.Services
{
    /// <summary>
    /// <see cref="IScadaAlarmCommands"/> 的惰性适配器：构造期只持一个委托，首次派发才解析宿主。
    ///
    /// 为什么要这一层（2026-10-30 实测启动即崩）
    /// ---------
    /// ScadaRuntimeHost 的构造需要 <c>IScadaActionDispatcher</c>，而
    /// <c>IScadaAlarmCommands</c> 又映射回 ScadaRuntimeHost 单例——三者构成
    /// ScadaRuntimeHost → IScadaActionDispatcher → IScadaAlarmCommands → ScadaRuntimeHost
    /// 的<b>构造期环</b>。DryIoc 的工厂委托重入没有环检测，直接在分发器的构造参数里
    /// <c>c.Resolve&lt;IScadaAlarmCommands&gt;()</c> 会首次解析即 StackOverflow。
    /// 本适配器构造零依赖，把解析推迟到第一次真正确认报警时——那时宿主单例早已就绪，
    /// 行为与直接注入完全一致。
    ///
    /// 线程：只允许在 UI 线程调用（与 IScadaAlarmCommands 契约一致）。
    /// </summary>
    public sealed class LazyAlarmCommands : IScadaAlarmCommands
    {
        private readonly Func<ScadaRuntimeHost> _host;

        public LazyAlarmCommands(Func<ScadaRuntimeHost> host)
            => _host = host ?? throw new ArgumentNullException(nameof(host));

        private IScadaAlarmCommands Sink => _host();

        public bool TryAcknowledge(Guid alarmId, out string? error)
            => Sink.TryAcknowledge(alarmId, out error);

        public int AcknowledgeAll(out string? error)
            => Sink.AcknowledgeAll(out error);
    }
}
