using G.Common.Attributes;
using G.Extensions.FontIcon;
using G.Modules.Help.Base;
using G.Services.Setting;
using System.ComponentModel.DataAnnotations;

namespace G.Modules.Help.WebSite;
[Icon(FontIcons.Home)]
[Display(Name = "官方网址", GroupName = SettingGroupNames.GroupSystem, Description = "查看官方网址")]
public class WebsiteOptions : UriHelpOptionsBase<WebsiteOptions>, IWebsiteOptions
{
    public override void LoadDefault()
    {
        base.LoadDefault();
        this.Uri = "https://G.github.io/WPF-Control/Home.html";
    }
}
