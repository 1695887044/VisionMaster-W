using System.ComponentModel.DataAnnotations;
using System.Windows;

namespace G.Themes.Colors.Solid;
[Display(Name = "Viking Dark", GroupName = "纯色", Description = "纯色", Order = 90, Prompt = "试验")]
public class VikingDarkColorResource : ColorResourceBase
{
    public VikingDarkColorResource()
    {
        this.IsDark = true;
    }
    public override ResourceDictionary Resource => new ResourceDictionary()
    {
        Source = new Uri("pack://application:,,,/G.Themes.Colors.Solid;component/VikingDark.xaml")
    };
}
