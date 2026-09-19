using System.Windows;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 文本图元（TypeKey = <c>Hmi.Text</c>）：画面上的标题、说明、单位、动态数值显示。
    ///
    /// 它不画任何形状，只有一段可换行的文字（模板里就是一个 TextBlock）。
    /// 动态数值的做法是给 <c>Text</c> 属性绑一个工程变量（S4 由绑定引擎写入），
    /// 而不是另立"数值显示"图元——同一个控件、同一套属性，运行时才需要格式化。
    ///
    /// 这个类本身几乎不加东西：文字、字号、颜色、水平对齐都在基类上
    /// （见 <see cref="ScadaElementBase.TextAlignmentProperty"/> 的说明），
    /// 它只负责把默认样式键指到自己身上。
    /// </summary>
    public class TextElement : ScadaElementBase
    {
        static TextElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(TextElement),
                new FrameworkPropertyMetadata(typeof(TextElement)));
        }
    }
}
