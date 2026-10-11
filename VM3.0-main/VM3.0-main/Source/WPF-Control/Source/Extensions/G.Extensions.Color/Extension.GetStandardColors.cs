using System.Windows.Markup;

namespace G.Extensions.Color;

public class GetStandardColorsExtension : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return ColorFactory.CreateStandardColors();
    }
}
