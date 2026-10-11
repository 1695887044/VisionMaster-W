using System.Windows.Markup;

namespace G.MarkupExtension;

[MarkupExtensionReturnType(typeof(Type))]
public class GenericTypeExtension : System.Windows.Markup.MarkupExtension
{
    public Type GenericType { get; set; }

    public Type TypeArgument { get; set; }

    public Type[] TypeArguments { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (this.TypeArgument == null)
            return this.GenericType.MakeGenericType(this.TypeArguments);
        return this.GenericType.MakeGenericType(this.TypeArgument);
    }
}
