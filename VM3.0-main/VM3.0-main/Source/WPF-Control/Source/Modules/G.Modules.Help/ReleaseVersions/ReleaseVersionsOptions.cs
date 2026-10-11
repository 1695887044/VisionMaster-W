using G.Common.Attributes;
using G.Extensions.FontIcon;
using G.Modules.Help.Base;
using G.Services.Setting;
using System.ComponentModel.DataAnnotations;

namespace G.Modules.Help.ReleaseVersions;

[Icon(FontIcons.History)]
[Display(Name = "发行说明", GroupName = SettingGroupNames.GroupSystem, Description = "查看软件发行说明")]
public class ReleaseVersionsOptions : UriHelpOptionsBase<ReleaseVersionsOptions>, IReleaseVersionsOptions
{
    public override void LoadDefault()
    {
        base.LoadDefault();
        this.Uri = "https://github.com/G/WPF-Control/releases";
    }
}

