using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 本地静态图片图元（TypeKey = <c>Hmi.Picture</c>）：把方案 <c>Image\</c> 下的一张图摆到画面上。
    ///
    /// 与 <see cref="ImageViewElement"/> 的分工
    /// ---------
    /// <b>Picture = 设计期资产</b>（logo、产品图、操作指引），路径落进 .vms，随方案文件夹走；
    /// <b>ImageView = 运行态图像</b>（相机/结果图），值由变量在运行时提供。
    /// 两者不合并：合并会让属性面板出现“既能选文件又能绑变量”的歧义。
    ///
    /// 与“页面背景”的分工
    /// ---------
    /// 整页背景是 <see cref="ScadaPage.BackgroundImage"/>（页面属性，铺满、压底、不参与框选）；
    /// 本图元是可移动、可选中、参与布局的内容图。现场绝大多数“拖进来的图”是背景，
    /// 所以那条路径才是主用法，这里只负责小图。
    ///
    /// 图片解码由宿主注入的 <see cref="ScadaElementBase.ImageAssetResolver"/> 完成：
    /// 控件库不引用方案服务，也不认识绝对路径；没有解析器（断言/单独预览）时显示占位。
    /// </summary>
    [TemplatePart(Name = PartImageHost, Type = typeof(Image))]
    [TemplatePart(Name = PartPlaceholder, Type = typeof(TextBlock))]
    public partial class PictureElement : ScadaElementBase
    {
        /// <summary>模板部件名：图像本体</summary>
        public const string PartImageHost = "ImageHost";

        /// <summary>模板部件名：无图时的占位提示</summary>
        public const string PartPlaceholder = "Placeholder";

        /// <summary>
        /// 方案内相对路径（如 <c>Image\logo.png</c>）。空 = 未设置，显示占位。
        /// 不参与变量绑定：它是设计期资产，不是运行值。
        /// </summary>
        [ScadaProperty(Key = "SourcePath", DisplayName = "图片", Kind = ElementPropertyKind.ImageFile,
                       Group = ScadaPropertyGroups.Image, DefaultValue = "", IsBindable = false,
                       Description = "从方案 Image 目录选一张图（选图时自动复制进方案）。" +
                                     "整页背景请用画面属性的「背景图」，不要拿本图元铺满")]
        public partial string? SourcePath { get; set; }

        /// <summary>缩放方式（复用 WPF 的 Stretch 枚举）</summary>
        [ScadaProperty(Key = "Stretch", DisplayName = "缩放方式", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.Image, DefaultValue = "Uniform",
                       Choices = new[] { "None", "Fill", "Uniform", "UniformToFill" },
                       Description = "图片在框内的缩放规则；默认 Uniform（等比缩放不变形）")]
        public partial Stretch Stretch { get; set; }

        /// <summary>没有图时显示的提示文字</summary>
        [ScadaProperty(Key = "PlaceholderText", DisplayName = "无图提示", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Image, DefaultValue = "未设置图片",
                       Description = "没选图片、或文件缺失时显示的字；有它才能分清“没配”和“配了但文件没了”")]
        public partial string? PlaceholderText { get; set; }

        private Image? _imageHost;

        static PictureElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(PictureElement),
                new FrameworkPropertyMetadata(typeof(PictureElement)));
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _imageHost = GetTemplateChild(PartImageHost) as Image;
            ApplyImage();
        }

        protected override void OnElementRefreshed()
        {
            base.OnElementRefreshed();
            ApplyImage();
        }

        /// <summary>
        /// 解析路径并落图。失败（无解析器 / 文件缺失 / 解码失败）一律回退占位，
        /// 不抛异常——一张图找不到不该让整幅画面打不开。
        /// </summary>
        private void ApplyImage()
        {
            var image = ResolveImageAsset(SourcePath);

            if (_imageHost != null)
                _imageHost.Source = image;

            SetPartVisible(PartImageHost, image != null);
            SetPartVisible(PartPlaceholder, image == null);
        }
    }
}
