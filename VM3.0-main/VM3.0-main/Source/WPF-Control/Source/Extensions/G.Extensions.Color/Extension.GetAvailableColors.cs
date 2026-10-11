using System.Windows.Markup;

namespace G.Extensions.Color;

public class GetAvailableColorsExtension : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return ColorFactory.CreateAvailableColors();
    }
}
