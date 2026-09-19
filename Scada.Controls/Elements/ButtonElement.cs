using System.Windows;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 按钮图元（TypeKey = <c>Basic.Button</c>）：操作员按下就执行动作的控件
    /// （"启动""停止""复位""切换手/自动"）。
    ///
    /// 它<b>不是</b> WPF 的 <see cref="System.Windows.Controls.Button"/>：用 Button 会白白继承
    /// 一整套 Command/ClickMode/IsDefault 语义，而 SCADA 按钮的"点击"最终要落到
    /// "写工程变量 + 记录操作审计"上，那条链路由 S4 运行态接管，硬套 WPF 命令只会多一层壳。
    /// 这里只负责"看起来像按钮、能被点中、有悬停反馈"，因此复用矩形那套外观词汇：
    /// 一个带圆角的矩形 + 居中的文字。
    ///
    /// 与基类的两处外观差别写在自己的样式里（Focusable/Cursor/悬停层），
    /// 而不是塞进控件构造函数——样式是可被宿主换肤覆盖的，硬编码在构造里就锁死了。
    /// </summary>
    public class ButtonElement : ScadaElementBase
    {
        static ButtonElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(ButtonElement),
                new FrameworkPropertyMetadata(typeof(ButtonElement)));
        }

        // extra = 4：按钮文字离边稍远一点，密集按钮排在一起才不局促
        protected override void OnElementRefreshed() => ApplyStrokeInset(4);
    }
}
