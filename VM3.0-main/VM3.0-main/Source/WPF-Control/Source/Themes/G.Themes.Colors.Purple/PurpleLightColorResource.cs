using System.ComponentModel.DataAnnotations;
using System.Windows;

namespace G.Themes.Colors.Purple;
[Display(Name = "浅紫色（长期支持）", GroupName = "纯色", Description = "纯色", Order = 100, Prompt = "长期支持")]
public class PurpleLightColorResource : ColorResourceBase
{
    public PurpleLightColorResource()
    {
        this.IsDark = false;
    }
    public override ResourceDictionary Resource => new ResourceDictionary()
    {
        Source = new Uri("pack://application:,,,/G.Themes.Colors.Purple;component/Light.xaml")
    };
}
