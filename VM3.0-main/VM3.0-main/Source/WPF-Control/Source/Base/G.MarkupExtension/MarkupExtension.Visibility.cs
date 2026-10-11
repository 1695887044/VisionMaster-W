using System.Windows;
using System.Windows.Markup;

namespace G.MarkupExtension;

[MarkupExtensionReturnType(typeof(Visibility))]
public class GetVisibilityExtension : GetValueExtensionBase
{
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (Enum.TryParse<Visibility>(this.Value, out Visibility result))
        {
            return result;
        }
        return Visibility.Visible;
    }
}
