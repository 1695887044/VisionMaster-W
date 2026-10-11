using System.Windows.Markup;

namespace G.Themes.Colors.Mineral;

public class MineralThemeExtension : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new MineralColorResource().Resource;
    }
}
