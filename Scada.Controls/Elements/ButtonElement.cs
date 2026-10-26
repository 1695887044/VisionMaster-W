using System.Windows;
using System.Windows.Input;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 按钮图元（TypeKey = <c>Basic.Button</c>）：操作员按下就执行动作的控件
    /// （"启动""停止""复位""切换手/自动"）。
    ///
    /// 它<b>不是</b> WPF 的 <see cref="System.Windows.Controls.Button"/>：用 Button 会白白继承
    /// 一整套 Command/ClickMode/IsDefault 语义，而 SCADA 按钮的"点击"最终要落到
    /// "写工程变量 + 记录操作审计"上，那条链路由 S4 运行态接管，硬套 WPF 命令只会多一层壳。
    /// 这里只负责"看起来像按钮、能被点中、有悬停/按下反馈"，因此复用矩形那套外观词汇：
    /// 一个带圆角的矩形 + 居中的文字。
    ///
    /// 按下状态为什么自己算（2026-10-25 按钮审查 P1）
    /// ---------
    /// 本控件是 <c>ContentControl</c> 派生，没有 <c>ButtonBase.IsPressed</c> 可用；
    /// 而"按下去要有反馈"是触摸屏上的硬要求（没有反馈，操作员会连点两三次）。
    /// 所以这里用一个只读 DP 表达按下态，由鼠标按下/抬起/丢捕获三处维护，
    /// 模板只消费它——状态判定留在控件里，外观留在样式里，与既有分工一致。
    /// </summary>
    public partial class ButtonElement : ScadaElementBase
    {
        private static readonly DependencyPropertyKey IsPressedPropertyKey =
            DependencyProperty.RegisterReadOnly(
                nameof(IsPressed), typeof(bool), typeof(ButtonElement), new PropertyMetadata(false));

        /// <summary>按下态（只读；模板用它做 1px 下沉/变暗的反馈）</summary>
        public static readonly DependencyProperty IsPressedProperty = IsPressedPropertyKey.DependencyProperty;

        static ButtonElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(ButtonElement),
                new FrameworkPropertyMetadata(typeof(ButtonElement)));
        }

        /// <summary>
        /// 危险动作变体（急停、复位、清报警）。只影响外观：红边 + 淡红文字 + 一层红色蒙版，
        /// 不改任何动作语义。默认关——老工程读不到这个键时仍是普通按钮。
        /// </summary>
        [ScadaProperty(Key = "Danger", DisplayName = "危险动作",
            Kind = ElementPropertyKind.Bool, Group = ScadaPropertyGroups.Appearance, DefaultValue = "False",
            Description = "勾选 = 红边 + 淡红文字（急停、复位、清报警这类动作）；只影响外观，不改变动作本身")]
        public partial bool Danger { get; set; }

        /// <summary>当前是否按下</summary>
        public bool IsPressed => (bool)GetValue(IsPressedProperty);

        // extra = 4：按钮文字离边稍远一点，密集按钮排在一起才不局促
        protected override void OnElementRefreshed() => ApplyStrokeInset(4);

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);

            if (!IsEnabled)
                return;

            SetValue(IsPressedPropertyKey, true);
            CaptureMouse();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            ReleasePress();
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            ReleasePress();
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            SetValue(IsPressedPropertyKey, false);
        }

        private void ReleasePress()
        {
            if (IsPressed)
                SetValue(IsPressedPropertyKey, false);

            if (IsMouseCaptured)
                ReleaseMouseCapture();
        }
    }
}
