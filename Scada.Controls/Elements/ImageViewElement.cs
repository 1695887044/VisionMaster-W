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
    public partial class ImageViewElement : ScadaElementBase
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
        [ScadaProperty(Key = "Source", DisplayName = "图像", Kind = ElementPropertyKind.Image,
                       Group = "图像", DefaultValue = "", IsBindable = true,
                       Description = "绑一个图像变量（HImage）。Halcon 像素到 WPF 位图的换算在宿主侧完成，" +
                                     "本图元只认 WPF 位图——图元库刻意不认识视觉库，换算法库不必动画面。" +
                                     "位图没法手打，所以这一行只给绑定入口，不给输入框")]
        public partial ImageSource? Source { get; set; }

        /// <summary>
        /// 缩放方式。直接复用 WPF 自带的 <see cref="Stretch"/> 枚举，不自造一套——
        /// 这样属性面板的下拉候选项、字符串转换器、绑定引擎的换算规则全都是现成的。
        /// </summary>
        [ScadaProperty(Key = "Stretch", DisplayName = "缩放方式", Kind = ElementPropertyKind.Choice,
                       Group = "图像", DefaultValue = "Uniform",
                       Choices = new[] { "None", "Fill", "Uniform", "UniformToFill" },
                       Description = "图像在框内的缩放规则，直接复用 WPF 自带的 Stretch 枚举；" +
                                     "默认 Uniform（等比缩放到框内，不变形），要看像素真值（不缩放）选 None")]
        public partial Stretch Stretch { get; set; }

        /// <summary>没有图时显示的提示文字</summary>
        [ScadaProperty(Key = "PlaceholderText", DisplayName = "无图提示", Kind = ElementPropertyKind.Text,
                       Group = "图像", DefaultValue = "无图像",
                       Description = "还没绑变量、或变量当前没有图时显示的字。" +
                                     "有它才能分清「没配」和「配了但没图」——" +
                                     "否则操作员看到的只是一个像坏掉的空白框")]
        public partial string? PlaceholderText { get; set; }

        static ImageViewElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(ImageViewElement),
                new FrameworkPropertyMetadata(typeof(ImageViewElement)));
        }

        protected override void OnElementRefreshed() => ApplyStrokeInset();

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            UpdatePlaceholderVisibility();
        }

        // 有图 / 无图要翻占位提示的显隐；缩放方式与提示文字由模板绑定自己刷新。
        partial void OnSourcePropertyChanged() => UpdatePlaceholderVisibility();

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
