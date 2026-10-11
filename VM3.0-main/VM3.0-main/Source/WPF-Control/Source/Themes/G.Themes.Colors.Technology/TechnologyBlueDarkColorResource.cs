using System.ComponentModel.DataAnnotations;
using System.Windows;

namespace G.Themes.Colors.Technology;

[Display(Name = "深空科技蓝（推荐）", GroupName = "强力推荐", Description = "纯色", Order = 0, Prompt = "强力推荐")]
public class TechnologyBlueDarkColorResource : ColorResourceBase
{
    public TechnologyBlueDarkColorResource()
    {
        this.IsDark = true;
    }
    public override ResourceDictionary Resource => new ResourceDictionary()
    {
        Source = new Uri("pack://application:,,,/G.Themes.Colors.Technology;component/TechnologyBlueDark.xaml")
    };
}
