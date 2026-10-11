using System.Windows.Markup;

namespace G.Themes.Colors.Blue;

public class BlueDarkThemeExtension : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new BlueDarkColorResource().Resource;
    }
}

public class BlueLightThemeExtension : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new BlueLightColorResource().Resource;
    }
}
