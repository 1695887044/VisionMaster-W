using System.Windows;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 椭圆图元（TypeKey = <c>Basic.Ellipse</c>）：储罐、电机、风机、泵这类"圆的设备"，
    /// 以及 SCADA 里惯用的"圆形状态点"。
    ///
    /// 与矩形共用同一套外观词汇（Fill/Stroke/StrokeThickness/Text/Foreground/FontSize），
    /// 少一个圆角——椭圆本身没有圆角可调。属性面板沿用同一批标签，
    /// 用户学会一个图元就等于学会另一个，这是"低学习曲线"最实惠的做法。
    /// </summary>
    public class EllipseElement : ScadaElementBase
    {
        static EllipseElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(EllipseElement),
                new FrameworkPropertyMetadata(typeof(EllipseElement)));
        }

        protected override void OnElementRefreshed() => ApplyStrokeInset(2);
    }
}
