namespace G.Themes.Backgrounds;

public class LinearGradientBrushResource : IBackgroundResource
{
    public string Name => "渐变（推荐）";
    public ResourceDictionary Resource => new ResourceDictionary()
    {
        Source = new Uri("pack://application:,,,/G.Theme;component/Backgrounds/LinearGradientBrush.xaml")
    };
    public override string ToString()
    {
        return this.Name;
    }
}
