using System.ComponentModel.DataAnnotations;
using System.Windows;

namespace G.Themes.Colors.Blue;
[Display(Name = "浅蓝色（长期支持）", GroupName = "纯色", Description = "纯色", Order = 50, Prompt = "长期支持")]
public class BlueLightColorResource : ColorResourceBase
{
    public BlueLightColorResource()
    {
        this.IsDark = false;
    }
    public override ResourceDictionary Resource => new ResourceDictionary()
    {
        Source = new Uri("pack://application:,,,/G.Themes.Colors.Blue;component/Light.xaml")
    };
}
