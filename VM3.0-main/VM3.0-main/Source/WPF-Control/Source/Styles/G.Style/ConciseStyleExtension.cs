namespace G.Styles;

public class ConciseStyleExtension : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new ResourceDictionary()
        {
            Source = new Uri("pack://application:,,,/G.Style;component/ConciseControls.xaml")
        };
    }
}
