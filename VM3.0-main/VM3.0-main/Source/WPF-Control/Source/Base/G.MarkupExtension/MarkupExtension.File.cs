using System.Windows.Markup;

namespace G.MarkupExtension;

[MarkupExtensionReturnType(typeof(string))]
public class SpecialFolderExtension : System.Windows.Markup.MarkupExtension
{
    public Environment.SpecialFolder SpecialFolder { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return Environment.GetFolderPath(this.SpecialFolder);
    }
}
