using System.Windows.Controls;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 「容器图元」契约：一个能把别的图元装进自己里面的图元（底板 / 面板）。
    ///
    /// 只有<b>一个</b>成员，因为容器的全部技术含义就是"我这儿有一块画布，子图元挂它上面"：
    /// 装进子画布的控件天然被父控件的边界裁剪（模板上 <c>ClipToBounds="True"</c>），
    /// 也天然跟着父控件移动（子控件是父可视树的子孙）。
    ///
    /// <b>为什么做成接口而不是"看模板里有没有某个部件"</b>
    /// ---------
    /// 画布必须能在<b>造控件时</b>回答"这个图元能不能装子图元"，据此决定子控件挂哪儿。
    /// 靠 <c>GetTemplateChild("PART_Children")</c> 去猜有两个问题：
    /// ① 它只有在模板套用之后才有效，而挂载发生在造控件的同一轮里（时序脆弱）；
    /// ② 猜错的后果是静默的——子图元会被当成顶层图元铺在画布上，
    ///    看起来"没装进去"，而不是报错。类型上的一个接口是编译期可查的事实，不靠猜。
    ///
    /// 落点是 <see cref="ChildrenHost"/>：容器模板里那块 Canvas。
    /// 模板没有这个部件时返回 null，画布把子图元降级为顶层渲染（宁可位置不对，也不能丢图元）。
    /// </summary>
    public interface IElementContainer
    {
        /// <summary>
        /// 承载子图元的画布（容器模板里的那块 <c>Canvas</c>）；模板还没套上时为 <c>null</c>。
        ///
        /// <b>坐标语义</b>：子控件在这里的 <c>Canvas.Left/Top</c> 是<b>相对容器</b>的
        /// （= 子图元的画面绝对坐标 − 容器的画面绝对坐标 − <see cref="ChildrenOriginX"/>/<see cref="ChildrenOriginY"/>），
        /// 换算在 <see cref="ScadaElementBase.ApplyGeometry"/> 里做且只做一处。
        /// </summary>
        Canvas? ChildrenHost { get; }

        /// <summary>
        /// 子图元区原点相对容器左上角的横向偏移（大多数容器是 0）。
        ///
        /// 存在的理由：底板有标题栏时，承载画布在标题栏<b>下面</b>，
        /// 于是它的原点不再等于底板的左上角。如果画布侧只按"绝对坐标 − 底板坐标"换算，
        /// 每个子图元都会静默下移一个标题栏的高度——模型说 Y=200，屏幕上画在 224。
        /// 这个偏移让容器自己回答"我的子图元原点在哪儿"，画布侧不必知道标题栏这回事。
        /// </summary>
        double ChildrenOriginX { get; }

        /// <summary>子图元区原点相对容器左上角的纵向偏移（见 <see cref="ChildrenOriginX"/>）</summary>
        double ChildrenOriginY { get; }

        /// <summary>
        /// 认领一个子图元控件（把它挂进 <see cref="ChildrenHost"/>）。
        ///
        /// <b>为什么由容器实现、而不是画布直接往 ChildrenHost 里塞</b>
        /// ---------
        /// 因为"模板什么时候就绪"只有容器自己知道。画布重建时控件刚造出来、还没进可视树，
        /// 模板尚未套用 → <see cref="ChildrenHost"/> 是 null。此时若画布直接判"装不进去"
        /// 就把子图元当顶层铺在画布上（实测就是这个结果：模型里 ParentId 全对，屏幕上却没嵌套）。
        ///
        /// 所以容器把"还没就绪时来的子图元"记在待办里，等
        /// <c>OnApplyTemplate</c> 一到再补挂。认领是幂等的。
        /// </summary>
        void TakeChild(ScadaElementBase child);

        /// <summary>解除认领（子图元被移出底板 / 底板自己被删时调）。幂等。</summary>
        void ReleaseChild(ScadaElementBase child);
    }
}
