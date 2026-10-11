namespace G.Themes.Colors;

public class ColorThemeExtension : MarkupExtension
{
    public ColorThemeType Type { get; set; }
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (this.Type == ColorThemeType.Default)
            return new DefaultColorResource().Resource;
        if (this.Type == ColorThemeType.Dark)
            return new DarkColorResource().Resource;
        if (this.Type == ColorThemeType.Light)
            return new LightColorResource().Resource;
        return null;
    }
}
