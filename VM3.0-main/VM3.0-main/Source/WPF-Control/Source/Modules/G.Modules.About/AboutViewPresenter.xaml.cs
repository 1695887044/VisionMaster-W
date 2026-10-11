using G.Common.Attributes;
using G.Extensions.FontIcon;
using G.Services.Common.About;
using G.Services.Setting;
using System.ComponentModel.DataAnnotations;

namespace G.Modules.About
{

    [Icon(FontIcons.Info)]
    [Display(Name = "关于", GroupName = SettingGroupNames.GroupSystem, Description = "这是一个关于页面的信息")]
    public class AboutViewPresenter : Ioc<AboutViewPresenter, IAboutViewPresenter>, IAboutViewPresenter
    {

    }
}
