using G.Common.Attributes;
using G.Extensions.FontIcon;
using G.Extensions.Mvvm.ViewModels.Base;
using G.Services.Setting;
using System.ComponentModel.DataAnnotations;

namespace G.Modules.Login
{
    [Icon(FontIcons.Contact)]
    [Display(Name = "当前用户", GroupName = SettingGroupNames.GroupSystem, Description = "当前登录的用户信息")]
    public class CurrentUserViewPresenter : DisplayBindableBase, ICurrentUserViewPresenter
    {

    }

    public interface ICurrentUserViewPresenter
    {

    }
}
