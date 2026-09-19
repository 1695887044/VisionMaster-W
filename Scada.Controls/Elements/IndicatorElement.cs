using System.Windows;
using System.Windows.Media;

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
    public class IndicatorElement : ScadaElementBase
    {
        // 默认色必须是冻结（Freeze）的：依赖属性默认值会被所有实例共享，
        // 未冻结的 Freezable 一旦被某个实例的样式/动画改到，其余实例会跟着变。
        private static readonly Brush DefaultOnColor = ScadaBrushes.Frozen("#FF34C759");
        private static readonly Brush DefaultOffColor = ScadaBrushes.Frozen("#FF3A3A3A");

        /// <summary>点亮色</summary>
        public static readonly DependencyProperty OnColorProperty = DependencyProperty.Register(
            nameof(OnColor), typeof(Brush), typeof(IndicatorElement),
            new FrameworkPropertyMetadata(
                DefaultOnColor, FrameworkPropertyMetadataOptions.AffectsRender, OnLampInputChanged));

        /// <summary>熄灭色</summary>
        public static readonly DependencyProperty OffColorProperty = DependencyProperty.Register(
            nameof(OffColor), typeof(Brush), typeof(IndicatorElement),
            new FrameworkPropertyMetadata(
                DefaultOffColor, FrameworkPropertyMetadataOptions.AffectsRender, OnLampInputChanged));

        /// <summary>当前状态（true = 点亮）</summary>
        public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register(
            nameof(IsOn), typeof(bool), typeof(IndicatorElement),
            new FrameworkPropertyMetadata(
                false, FrameworkPropertyMetadataOptions.AffectsRender, OnLampInputChanged));

        /// <summary>灯体形状</summary>
        public static readonly DependencyProperty ShapeProperty = DependencyProperty.Register(
            nameof(Shape), typeof(IndicatorShape), typeof(IndicatorElement),
            new FrameworkPropertyMetadata(
                IndicatorShape.Circle, FrameworkPropertyMetadataOptions.AffectsRender));

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

        /// <summary>点亮色（见 <see cref="OnColorProperty"/>）</summary>
        public Brush? OnColor
        {
            get => (Brush?)GetValue(OnColorProperty);
            set => SetValue(OnColorProperty, value);
        }

        /// <summary>熄灭色（见 <see cref="OffColorProperty"/>）</summary>
        public Brush? OffColor
        {
            get => (Brush?)GetValue(OffColorProperty);
            set => SetValue(OffColorProperty, value);
        }

        /// <summary>当前状态（见 <see cref="IsOnProperty"/>）</summary>
        public bool IsOn
        {
            get => (bool)GetValue(IsOnProperty);
            set => SetValue(IsOnProperty, value);
        }

        /// <summary>灯体形状（见 <see cref="ShapeProperty"/>）</summary>
        public IndicatorShape Shape
        {
            get => (IndicatorShape)GetValue(ShapeProperty);
            set => SetValue(ShapeProperty, value);
        }

        /// <summary>当前灯色（只读，见 <see cref="LampBrushProperty"/>）</summary>
        public Brush? LampBrush => (Brush?)GetValue(LampBrushProperty);

        protected override void OnElementRefreshed() => ApplyStrokeInset(1);

        private static void OnLampInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((IndicatorElement)d).UpdateLampBrush();

        private void UpdateLampBrush()
            => SetValue(LampBrushPropertyKey, (IsOn ? OnColor : OffColor) ?? Brushes.Transparent);
    }
}
