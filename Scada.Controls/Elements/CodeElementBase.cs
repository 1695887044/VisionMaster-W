using System;
using System.Windows;
using System.Windows.Controls;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 二维码 / 条码的公共基类：内容、码下文本、静区、占位与错误提示。
    ///
    /// 编码交给 <see cref="ScadaCodeRenderer"/>（ZXing）；本类只负责“把位图摆上去 + 失败时显示原因”。
    /// 失败（空内容 / 超长 / 字符集不支持 / 编码异常）时显示占位并保留原因，不抛异常。
    /// </summary>
    [TemplatePart(Name = PartCodeSurface, Type = typeof(Border))]
    [TemplatePart(Name = PartCodeImage, Type = typeof(Image))]
    [TemplatePart(Name = PartCodePlaceholder, Type = typeof(TextBlock))]
    [TemplatePart(Name = PartCodeText, Type = typeof(TextBlock))]
    public abstract partial class CodeElementBase : ScadaElementBase
    {
        public const string PartCodeSurface = "CodeSurface";
        public const string PartCodeImage = "CodeImage";
        public const string PartCodePlaceholder = "CodePlaceholder";
        public const string PartCodeText = "CodeText";

        [ScadaProperty(Key = "CodeContent", DisplayName = "内容", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "", IsBindable = true,
                       Description = "要编码的文本（SN、批次、追溯码）；绑一个字符串变量即可运行态跟着变")]
        public partial string? CodeContent { get; set; }

        [ScadaProperty(Key = "ShowText", DisplayName = "码下文本", Kind = ElementPropertyKind.Bool,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "True",
                       Description = "在码下方显示原始内容；扫码枪读的是码本身，这行只是给人看的")]
        public partial bool ShowText { get; set; }

        [ScadaProperty(Key = "QuietZone", DisplayName = "静区", Kind = ElementPropertyKind.Number,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "10", Min = 0, Max = 40,
                       Description = "码四周的空白边（像素）；太小会扫不出来，QR 建议 ≥4 模块")]
        public partial int QuietZone { get; set; }

        [ScadaProperty(Key = "PlaceholderText", DisplayName = "无内容提示", Kind = ElementPropertyKind.Text,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "未绑定内容",
                       Description = "内容为空时显示的字；编码失败时这里显示失败原因")]
        public partial string? PlaceholderText { get; set; }

        private Border? _surface;
        private Image? _image;
        private TextBlock? _placeholder;
        private TextBlock? _text;

        /// <summary>码制（QR / Code128 / Code39 / EAN13）</summary>
        protected abstract string CodeFormat { get; }

        /// <summary>纠错等级（仅 QR；条码返回 null）</summary>
        protected abstract string? ErrorCorrection { get; }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _surface = GetTemplateChild(PartCodeSurface) as Border;
            _image = GetTemplateChild(PartCodeImage) as Image;
            _placeholder = GetTemplateChild(PartCodePlaceholder) as TextBlock;
            _text = GetTemplateChild(PartCodeText) as TextBlock;
            ApplyCode();
        }

        protected override void OnElementRefreshed()
        {
            base.OnElementRefreshed();
            ApplyCode();
        }

        /// <summary>派生类的码制/纠错参数变了之后重编码</summary>
        protected void RefreshCode() => ApplyCode();

        private void ApplyCode()
        {
            var width = (int)Math.Max(64, Width);
            var height = (int)Math.Max(48, Height);

            var ok = ScadaCodeRenderer.TryRender(
                CodeContent,
                CodeFormat,
                width,
                height,
                QuietZone,
                ErrorCorrection,
                out var bitmap,
                out var error);

            if (_image != null)
                _image.Source = bitmap;

            SetPartVisible(PartCodeSurface, ok);
            SetPartVisible(PartCodeImage, ok);
            SetPartVisible(PartCodePlaceholder, !ok);
            SetPartVisible(PartCodeText, ok && ShowText);

            if (_placeholder != null)
                _placeholder.Text = string.IsNullOrWhiteSpace(error) ? (PlaceholderText ?? "未绑定内容") : error;

            if (_text != null)
                _text.Text = CodeContent ?? string.Empty;
        }

        partial void OnCodeContentPropertyChanged() => ApplyCode();
        partial void OnShowTextPropertyChanged() => ApplyCode();
        partial void OnQuietZonePropertyChanged() => ApplyCode();
        partial void OnPlaceholderTextPropertyChanged() => ApplyCode();
    }

    /// <summary>二维码图元（TypeKey = <c>Hmi.QRCode</c>）。</summary>
    public partial class QRCodeElement : CodeElementBase
    {
        [ScadaProperty(Key = "Ecc", DisplayName = "纠错等级", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.Data, DefaultValue = "M",
                       Choices = new[] { "L", "M", "Q", "H" },
                       Description = "L≈7% / M≈15% / Q≈25% / H≈30% 容错；现场有油污/磨损时用 Q 或 H")]
        public partial string? Ecc { get; set; }

        protected override string CodeFormat => ScadaCodeRenderer.FormatQr;

        protected override string? ErrorCorrection => Ecc;

        static QRCodeElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(QRCodeElement),
                new FrameworkPropertyMetadata(typeof(QRCodeElement)));
        }

        partial void OnEccPropertyChanged() => RefreshCode();
    }

    /// <summary>条形码图元（TypeKey = <c>Hmi.Barcode</c>）。</summary>
    public partial class BarcodeElement : CodeElementBase
    {
        [ScadaProperty(Key = "Format", DisplayName = "码制", Kind = ElementPropertyKind.Choice,
                       Group = ScadaPropertyGroups.Data, DefaultValue = ScadaCodeRenderer.FormatCode128,
                       Choices = new[]
                       {
                           ScadaCodeRenderer.FormatCode128,
                           ScadaCodeRenderer.FormatCode39,
                           ScadaCodeRenderer.FormatEan13,
                       },
                       Description = "Code128 支持全部 ASCII；Code39 只收大写/数字；EAN13 只收 12 或 13 位数字")]
        public partial string? Format { get; set; }

        protected override string CodeFormat => Format ?? ScadaCodeRenderer.FormatCode128;

        protected override string? ErrorCorrection => null;

        static BarcodeElement()
        {
            DefaultStyleKeyProperty.OverrideMetadata(
                typeof(BarcodeElement),
                new FrameworkPropertyMetadata(typeof(BarcodeElement)));
        }

        partial void OnFormatPropertyChanged() => RefreshCode();
    }
}
