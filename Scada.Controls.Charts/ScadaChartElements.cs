using System;
using System.Collections.Generic;
using VisionMaster.Scada.Controls;

namespace VisionMaster.Scada.Controls.Charts
{
    /// <summary>
    /// 曲线图元库的注册入口（<b>必须由宿主显式调一次</b>）。
    ///
    /// <b>为什么要一个显式入口，而不像内置图元那样在静态构造里自动注册</b>
    /// ---------
    /// 内置图元的注册发生在 <see cref="ElementRegistry"/> 的静态构造里，那是"引用程序集就自动生效"。
    /// 但曲线图元在<b>另一个程序集</b>里，而 .NET 的静态构造只在"那个类型被真正用到"时才跑——
    /// 光引用 DLL 是不够的：JIT 不会因为"程序集被加载"就去初始化里面的类型，
    /// 于是注册永远不会发生，工具箱里就是少一个曲线图元，且<b>不报任何错</b>。
    ///
    /// 这正是本仓库反复吃亏的那类静默失败（见 AGENTS.md 里"产物陈旧""不重建就静默陈旧"那几条），
    /// 所以这里不改机制、只把风险显式化：
    ///   · 宿主在启动时调一次（主程序、断言工程各一处）；
    ///   · 断言 <c>ScadaChecks</c> 有一条钉住"Hmi.ChartTrend 确实注册上了"，
    ///     漏调会让那条断言红，而不是等现场发现工具箱少东西。
    ///
    /// 幂等：重复调用安全（第二次会以"已注册"失败，被有意忽略）。
    /// </summary>
    public static class ScadaChartElements
    {
        private static readonly object Gate = new();
        private static bool _registered;

        /// <summary>本库自带图元的注册失败原因（正常为空）；由断言工程在构建期读取。</summary>
        public static IReadOnlyList<string> Errors { get; private set; } = Array.Empty<string>();

        /// <summary>
        /// 把本库的图元注册进 <see cref="ElementRegistry"/>。可重复调用。
        ///
        /// 注册失败<b>不抛异常</b>而是收集到 <see cref="Errors"/>：与
        /// <see cref="ElementRegistry.Register"/> 同一口径——注册发生在启动路径上，
        /// 抛异常会把"改错一个默认值"变成"程序打不开"，而把原因留给断言工程能在构建期一次性挡掉。
        /// </summary>
        public static void Register()
        {
            lock (Gate)
            {
                if (_registered)
                    return;

                _registered = true;

                var errors = new List<string>();
                foreach (var descriptor in ChartElements.All)
                {
                    var error = ElementRegistry.Register(descriptor);
                    if (error != null)
                        errors.Add(error);
                }

                Errors = errors.ToArray();
            }
        }
    }
}
