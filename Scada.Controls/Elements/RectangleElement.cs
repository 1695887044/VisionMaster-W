using System.Windows;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 矩形图元（TypeKey = <c>Basic.Rectangle</c>）：SCADA 画面里最常用的"东西"。
    ///
    /// 一块填色带边框的区域，里面可以放一段文字，于是它同时扮演三种角色：
    /// 设备本体（"1#水泵"+边框）、区域背景（半透明填色、无边框）、状态块（底色随变量变）。
    ///
    /// 外观属性全部落在基类的公共依赖属性上（Fill/Stroke/StrokeThickness/CornerRadius/Text/
    /// Foreground/FontSize），所以本类没有一行"把字符串变成颜色"的逻辑——那是模板与
    /// <see cref="ScadaElementBase.ApplyTargetProperty"/> 的活。
    ///
    /// 为什么本类需要自己的静态构造：外观与基类的默认模板不同（基类只有一个居中的
    /// ContentPresenter），WPF 靠 <see cref="FrameworkElement.DefaultStyleKeyProperty"/>
    /// 去 Themes/Generic.xaml 找样式，不换键就会套用基类那条样式。
    /// </summary>
    public class RectangleElement : ScadaElementBase
    {
        static RectangleElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(RectangleElement),
                new FrameworkPropertyMetadata(typeof(RectangleElement)));
        }

        // extra = 2：让文字与边框之间留 2px 呼吸空间，避免长标签顶到边框上
        protected override void OnElementRefreshed() => ApplyStrokeInset(2);
    }
}
