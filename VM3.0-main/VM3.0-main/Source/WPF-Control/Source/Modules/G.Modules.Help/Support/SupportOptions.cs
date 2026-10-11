using G.Common.Attributes;
using G.Extensions.FontIcon;
using G.Modules.Help.Base;
using G.Services.Setting;
using System.ComponentModel.DataAnnotations;

namespace G.Modules.Help.Support;
[Icon(FontIcons.Group)]
[Display(Name = "技术支持", GroupName = SettingGroupNames.GroupSystem, Description = "查看软件技术支持")]
public class SupportOptions : UriHelpOptionsBase<SupportOptions>, ISupportOptions
{
    public override void LoadDefault()
    {
        base.LoadDefault();
        this.Uri = "https://G.github.io/WPF-Control-Docs/";
    }
}
