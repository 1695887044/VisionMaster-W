using G.Themes.Extensions;

namespace G.Themes.FontSizes;

public class FontSizeThemeExtension : MarkupExtension
{
    public FontSizeThemeType Type { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return this.Type.GetFontSizeResource();
    }
}
