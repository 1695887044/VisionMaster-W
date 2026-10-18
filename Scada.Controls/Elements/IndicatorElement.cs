using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VisionMaster.Scada.Controls
{
    /// <summary>指示灯的灯体形状</summary>
    public enum IndicatorShape
    {
        /// <summary>圆形（默认，SCADA 里最常用）</summary>
        Circle,

        /// <summary>方形（多灯密集排列时比方圆更省地方）</summary>
        Square,
    }

    /// <summary>
    /// 指示灯图元（TypeKey = <c>Basic.Indicator</c>）：一根数字量变量的可视化身。
    ///
    /// 它的语义是"两种颜色里挑一种显示"，所以外观<b>不是</b>直接由 Fill 决定的，
    /// 而是由三个输入推导：点亮色 <see cref="OnColor"/>、熄灭色 <see cref="OffColor"/>、
    /// 状态 <see cref="IsOn"/>。推导结果放在只读的 <see cref="LampBrush"/> 上供模板绑定。
    ///
    /// 为什么推导结果要用<b>只读</b>依赖属性，而不是直接写回基类的 Fill：
    /// ① Fill 是可写属性，一旦有人（属性面板/绑定/其他代码）也去改它，灯色就有两个来源，
    ///    表现为"绑定变量后颜色偶尔不对"这种极难复现的问题；
    /// ② 只读属性从类型上就宣告了"这是算出来的"，外部只能改三个输入中的一个。
    /// 这与基类 Fill/Stroke 那套"可写外观词汇"并不冲突——指示灯只是不把它们当输入用。
    ///
    /// 运行时的接法（S4）：把 <see cref="IsOn"/> 绑到工程变量上，指示灯就活了。
    /// </summary>
    [TemplatePart(Name = PartLampCircle, Type = typeof(Ellipse))]
    [TemplatePart(Name = PartLampSquare, Type = typeof(Rectangle))]
    public partial class IndicatorElement : ScadaElementBase
    {
        /// <summary>模板部件名：圆形灯体</summary>
        public const string PartLampCircle = "LampCircle";

        /// <summary>模板部件名：方形灯体</summary>
        public const string PartLampSquare = "LampSquare";

        // 三个输入属性的声明次序 = 属性面板里的行序（生成器按源码次序收集），
        // 所以「点亮」排在最前：操作员配指示灯时先关心"绑哪根量"，再才是两个颜色。

        /// <summary>当前状态（true = 点亮）</summary>
        [ScadaProperty(Key = "IsOn", DisplayName = "点亮", Kind = ElementPropertyKind.Bool,
                       Group = "状态", DefaultValue = "False", IsBindable = true,
                       Description = "运行态把工程变量的布尔值绑到这里")]
        public partial bool IsOn { get; set; }

        /// <summary>点亮色</summary>
        [ScadaProperty(Key = "OnColor", DisplayName = "点亮色", Kind = ElementPropertyKind.Color,
                       Group = "状态", DefaultValue = "#FF34C759", IsBindable = true)]
        public partial Brush? OnColor { get; set; }

        /// <summary>熄灭色</summary>
        [ScadaProperty(Key = "OffColor", DisplayName = "熄灭色", Kind = ElementPropertyKind.Color,
                       Group = "状态", DefaultValue = "#FF3A3A3A", IsBindable = true)]
        public partial Brush? OffColor { get; set; }

        /// <summary>灯体形状</summary>
        [ScadaProperty(Key = "Shape", DisplayName = "形状", Kind = ElementPropertyKind.Choice,
                       Group = "外观", DefaultValue = "Circle", Choices = new[] { "Circle", "Square" })]
        public partial IndicatorShape Shape { get; set; }

        private static readonly DependencyPropertyKey LampBrushPropertyKey = DependencyProperty.RegisterReadOnly(
            nameof(LampBrush), typeof(Brush), typeof(IndicatorElement),
            new PropertyMetadata(Brushes.Transparent));

        /// <summary>当前该显示的灯色（= IsOn ? OnColor : OffColor，只读）</summary>
        public static readonly DependencyProperty LampBrushProperty = LampBrushPropertyKey.DependencyProperty;

        static IndicatorElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(IndicatorElement),
                new FrameworkPropertyMetadata(typeof(IndicatorElement)));
        }

        /// <summary>当前灯色（只读，见 <see cref="LampBrushProperty"/>）</summary>
        public Brush? LampBrush => (Brush?)GetValue(LampBrushProperty);

        protected override void OnElementRefreshed() => ApplyStrokeInset(1);

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            UpdateShapeVisibility();
        }

        // 三个输入属性共用同一条副作用：任一项变了都要重算灯色。
        // 生成器给每个属性一个独立钩子，所以这里是三行转调——比原先"一个静态回调挂三处"多两行，
        // 换来的是"每个属性各自可挂不同副作用"，且不再需要手写静态回调签名。
        partial void OnIsOnPropertyChanged() => UpdateLampBrush();
        partial void OnOnColorPropertyChanged() => UpdateLampBrush();
        partial void OnOffColorPropertyChanged() => UpdateLampBrush();
        partial void OnShapePropertyChanged() => UpdateShapeVisibility();

        /// <summary>
        /// 圆/方两块灯体同一时刻只显示一块。形状是个纯模板层的选择，但本环境下
        /// ControlTemplate.Triggers 里的 DataTrigger 不触发（见 ScadaElementBase.SetPartVisible），
        /// 所以只能由代码翻显隐；模板里那两块也就不写 Visibility 初值。
        /// </summary>
        private void UpdateShapeVisibility()
        {
            var square = Shape == IndicatorShape.Square;
            SetPartVisible(PartLampCircle, !square);
            SetPartVisible(PartLampSquare, square);
        }

        private void UpdateLampBrush()
            => SetValue(LampBrushPropertyKey, (IsOn ? OnColor : OffColor) ?? Brushes.Transparent);
    }
}
