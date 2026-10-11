namespace G.MarkupExtension;

public class GetInstanceExtension : System.Windows.Markup.MarkupExtension
{
    public Type Type { get; set; }
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return Activator.CreateInstance(this.Type);
    }
}
