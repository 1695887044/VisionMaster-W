using System.Windows.Markup;

namespace G.Extensions.Color;

public class GetSystemColorsExtension : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return ColorFactory.CreateSystemColors();
    }
}
