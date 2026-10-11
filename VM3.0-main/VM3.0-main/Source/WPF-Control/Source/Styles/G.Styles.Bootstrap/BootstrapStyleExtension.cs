namespace G.Styles.Bootstrap;

public class BootstrapStyleExtension : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new ResourceDictionary()
        {
            Source = new Uri("pack://application:,,,/G.Styles.Bootstrap;component/BootstrapControls.xaml")
        };
    }
}
