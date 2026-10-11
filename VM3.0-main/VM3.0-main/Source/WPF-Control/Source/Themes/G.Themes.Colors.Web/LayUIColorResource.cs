using System.ComponentModel.DataAnnotations;
using System.Windows;

namespace G.Themes.Colors.Web;
[Display(Name = "LayUI", GroupName = "网站前端风", Description = "纯色", Order = 100, Prompt = "试验")]
public class LayUIColorResource : ColorResourceBase
{
    public override ResourceDictionary Resource => new ResourceDictionary()
    {
        Source = new Uri("pack://application:,,,/G.Themes.Colors.Web;component/LayUI.xaml")
    };
}
