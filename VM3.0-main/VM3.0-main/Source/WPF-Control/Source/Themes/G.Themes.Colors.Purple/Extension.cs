using System.Windows.Markup;

namespace G.Themes.Colors.Purple;

public class PurpleDarkThemeExtension : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new PurpleDarkColorResource().Resource;
    }
}

public class PurpleLightThemeExtension : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new PurpleLightColorResource().Resource;
    }
}
