using System;
using System.Threading;

namespace VisionMaster.Scada
{
    /// <summary>
    /// 编辑期写守卫：把 D3"改模型只有一个入口"从约定变成可断言的机制。
    ///
    /// <b>为什么需要它</b>：约定管不住新增代码。半年后有人加一个"一键对齐"功能，
    /// 顺手在视图模型里写 <c>element.X = ...</c>，撤销栈就悄悄漏了这一步——而且不报错，
    /// 表现只是"撤销后位置没回去"，排查成本极高。有守卫就能在断言里把这类漏网钉死。
    ///
    /// <b>两种模式</b>：
    /// <list type="bullet">
    /// <item><see cref="Strict"/> = false（<b>默认</b>）：作用域外的写静默放行。
    /// 保留默认放行是必需的——全仓有大量"测试夹具直接构造对象"的写法
    /// （<c>new ScadaElement { X = 10 }</c>、<c>element.Name = "x"</c>），
    /// 它们是构造数据而不是编辑器路径，不该被拦。反序列化同理（Newtonsoft 直接走属性 setter）。</item>
    /// <item><see cref="Strict"/> = true：作用域外的写直接抛 <see cref="InvalidOperationException"/>。
    /// 断言用它在严格模式下跑一遍编辑器写路径，跑通即证明这些路径确实全在作用域内。</item>
    /// </list>
    ///
    /// <b>线程静态</b>：SCADA 编辑发生在 UI 线程，但断言可能在别的线程构造数据，
    /// 用 <see cref="ThreadStaticAttribute"/> 让两条线互不干扰。
    /// </summary>
    public static class ScadaWriteGuard
    {
        [ThreadStatic] private static int _scopeDepth;
        [ThreadStatic] private static int _suspendDepth;

        /// <summary>
        /// 严格模式：作用域外的模型写抛异常。默认关闭（见类注释的取舍说明）。
        /// 断言段打开它跑编辑器路径，跑完务必还原——它是静态状态，会串到后面的断言段。
        /// </summary>
        public static bool Strict { get; set; }

        /// <summary>当前线程是否处于可写状态（在作用域内，或守卫被显式挂起）</summary>
        public static bool IsWritable => _scopeDepth > 0 || _suspendDepth > 0;

        /// <summary>
        /// 每次模型写都要过这一关。非严格模式下它什么都不做（只是几个字段读），
        /// 所以可以无脑挂在所有 <c>SetProperty</c> 上而不影响性能敏感路径。
        /// </summary>
        public static void OnWrite(object? target, string propertyName)
        {
            if (IsWritable || !Strict)
                return;

            throw new InvalidOperationException(
                $"直接写模型被拒：{target?.GetType().Name}.{propertyName}。" +
                "编辑器路径必须包在 BeginEdit(...) 作用域里（D3 统一写入口）；" +
                "若是运行时数据泵或反序列化，请先 using (ScadaWriteGuard.Suspend())。");
        }

        /// <summary>作用域进入（只由 <see cref="ScadaChangeScope"/> 调，业务代码不该直接碰）</summary>
        internal static void EnterScope() => _scopeDepth++;

        /// <summary>作用域退出</summary>
        internal static void ExitScope() => _scopeDepth = _scopeDepth > 0 ? _scopeDepth - 1 : 0;

        /// <summary>
        /// 临时挂起守卫：撤销/重做回写、运行时数据泵、批量反序列化用。
        /// 与严格模式无关——挂起期间任何模式都放行。
        /// </summary>
        public static IDisposable Suspend() => new SuspendToken();

        private sealed class SuspendToken : IDisposable
        {
            private bool _disposed;

            public SuspendToken() => _suspendDepth++;

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _suspendDepth = _suspendDepth > 0 ? _suspendDepth - 1 : 0;
            }
        }
    }
}
