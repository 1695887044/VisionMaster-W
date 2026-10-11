using System.Windows.Markup;

namespace G.MarkupExtension;

public class StaticPropertyExtension : StaticExtension
{
    public StaticPropertyExtension()
    {

    }
    public StaticPropertyExtension(string member) : base(member)
    {

    }
    public string PropertyName { get; set; }
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(this.PropertyName))
            return base.ProvideValue(serviceProvider);
        var obj = base.ProvideValue(serviceProvider);
        return obj.GetType().GetProperty(this.PropertyName).GetValue(obj);
    }
}
