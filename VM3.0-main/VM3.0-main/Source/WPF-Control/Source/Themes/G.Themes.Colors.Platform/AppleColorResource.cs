using System.ComponentModel.DataAnnotations;
using System.Windows;

namespace G.Themes.Colors.Platform;
[Display(Name = "Apple (IOS)", GroupName = "系统平台", Description = "纯色", Order = 100, Prompt = "试验")]
public class AppleColorResource : ColorResourceBase
{
    public AppleColorResource()
    {
        this.IsDark = false;
    }
    public override ResourceDictionary Resource => new ResourceDictionary()
    {
        Source = new Uri("pack://application:,,,/G.Themes.Colors.Platform;component/Apple.xaml")
    };
}
