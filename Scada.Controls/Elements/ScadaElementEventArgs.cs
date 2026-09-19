using System.Windows;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 图元事件被触发时沿可视树冒泡上来的载荷（只带一个<b>组态事件</b>，不带 WPF 鼠标参数）。
    ///
    /// 为什么要专门一个 RoutedEventArgs 而不是普通 C# 事件：
    /// 事件要从"图元控件"穿过画布、内容呈现器、模板，最后到运行窗口。用 C# 事件就得在中间
    /// 每一层挂一次、转一次发（三层转发代码，漏一层就是"点了没反应"的哑故障）；
    /// 冒泡路由事件只需要<b>窗口根上挂一个 handler</b>，中间层完全不知情，
    /// 第三方加的图元控件也天然接得上——它只要 <c>RaiseEvent</c>，不必知道宿主长什么样。
    ///
    /// 参数里刻意<b>不带</b> <see cref="ScadaElement"/>：控件自己就带着模型（<c>Source</c> 即
    /// <see cref="ScadaElementBase"/>），复制一份引用只会造成"两个地方描述同一个图元"，
    /// 而运行态真正的判定（这是不是当前画面上的图元）在 <c>ScadaRuntime</c> 里做。
    /// </summary>
    public sealed class ScadaElementEventArgs : RoutedEventArgs
    {
        public ScadaElementEventArgs(RoutedEvent routedEvent, ScadaEventType scadaEvent)
            : base(routedEvent)
        {
            ScadaEvent = scadaEvent;
        }

        /// <summary>被触发的那个组态事件（按下/释放/值改变…）</summary>
        public ScadaEventType ScadaEvent { get; }
    }
}
