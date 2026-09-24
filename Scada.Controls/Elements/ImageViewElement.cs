using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 图像显示图元（TypeKey = <c>Hmi.ImageView</c>）：把一张位图摆在画面上。
    ///
    /// 它的输入是一个 <see cref="ImageSource"/>，而视觉流程产出的是 Halcon 的 HImage。
    /// 两者之间那道换算<b>不在本库做</b>：图元控件库刻意不引用 HalconDotNet，
    /// 换算在宿主侧（RegistryScadaValueSource 的取值出口）完成，
    /// 见 VisionMaster.Helpers.HalconImageHelper。于是本图元只认 WPF 位图，
    /// 画面层的边界保持干净——换算法库不必动画面。
    ///
    /// 为什么 Source 的默认值是 null、且模板里要配一块"无图像"占位文字
    /// ---------
    /// 组态时图元刚拖上画布还没绑变量，Source 必然是空的。若让 Image 空着，
    /// 操作员看到的是一个"看起来像坏掉的"空白框；给一句占位提示，才能分清
    /// "没配"和"配了但没图"。
    ///
    /// 空串与 null 的处理口径与基类一致：属性袋里 Source 这个键留空时，
    /// 基类的 ApplyTargetProperty 会回落到本依赖属性的默认值（null），
    /// 不会拿空串去转 ImageSource，因此注册期的字符串转换校验也自动豁免。
    /// </summary>
    [TemplatePart(Name = PartImageHost, Type = typeof(Image))]
    [TemplatePart(Name = PartPlaceholder, Type = typeof(TextBlock))]
    public class ImageViewElement : ScadaElementBase
    {
        /// <summary>模板部件名：图像本体</summary>
        public const string PartImageHost = "ImageHost";

        /// <summary>模板部件名：无图时的占位提示</summary>
        public const string PartPlaceholder = "Placeholder";

        /// <summary>
        /// 要显示的位图。默认 null（还没绑变量），此时显示占位提示。
        /// 绑定到本属性的是已冻结的 BitmapSource（冻结才能跨线程交给 UI），
        /// BitmapSource 本身就是 ImageSource，换算链在"值已是目标类型"那一档直通。
        /// </summary>
        public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
            nameof(Source), typeof(ImageSource), typeof(ImageViewElement),
            new FrameworkPropertyMetadata(
                null, FrameworkPropertyMetadataOptions.AffectsRender, OnSourceChanged));

        /// <summary>
        /// 缩放方式。直接复用 WPF 自带的 <see cref="Stretch"/> 枚举，不自造一套——
        /// 这样属性面板的下拉候选项、字符串转换器、绑定引擎的换算规则全都是现成的。
        /// </summary>
        public static readonly DependencyProperty StretchProperty = DependencyProperty.Register(
            nameof(Stretch), typeof(Stretch), typeof(ImageViewElement),
            new FrameworkPropertyMetadata(Stretch.Uniform, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>没有图时显示的提示文字</summary>
        public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
            nameof(PlaceholderText), typeof(string), typeof(ImageViewElement),
            new FrameworkPropertyMetadata("无图像", FrameworkPropertyMetadataOptions.AffectsRender));

        static ImageViewElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(ImageViewElement),
                new FrameworkPropertyMetadata(typeof(ImageViewElement)));
        }

        /// <summary>要显示的位图（见 <see cref="SourceProperty"/>）</summary>
        public ImageSource? Source
        {
            get => (ImageSource?)GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        /// <summary>缩放方式（见 <see cref="StretchProperty"/>）</summary>
        public Stretch Stretch
        {
            get => (Stretch)GetValue(StretchProperty);
            set => SetValue(StretchProperty, value);
        }

        /// <summary>无图时的提示文字（见 <see cref="PlaceholderTextProperty"/>）</summary>
        public string? PlaceholderText
        {
            get => (string?)GetValue(PlaceholderTextProperty);
            set => SetValue(PlaceholderTextProperty, value);
        }

        protected override void OnElementRefreshed() => ApplyStrokeInset();

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            UpdatePlaceholderVisibility();
        }

        private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((ImageViewElement)d).UpdatePlaceholderVisibility();

        /// <summary>
        /// 图像与占位提示互斥显示。这是个纯模板层的选择，但本环境下
        /// ControlTemplate.Triggers 里的 DataTrigger 不触发（见 ScadaElementBase.SetPartVisible），
        /// 所以只能由代码翻显隐；模板里那两块也就不写 Visibility 初值。
        /// </summary>
        private void UpdatePlaceholderVisibility()
        {
            var hasImage = Source != null;
            SetPartVisible(PartImageHost, hasImage);
            SetPartVisible(PartPlaceholder, !hasImage);
        }
    }
}
