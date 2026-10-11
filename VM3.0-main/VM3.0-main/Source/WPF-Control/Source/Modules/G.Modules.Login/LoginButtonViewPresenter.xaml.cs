global using G.Common.Attributes;
global using G.Extensions.FontIcon;
global using G.Mvvm.ViewModels.Base;
global using G.Services.Setting;
global using System.ComponentModel.DataAnnotations;

namespace G.Modules.Login
{
    [Icon(FontIcons.Contact)]
    [Display(Name = "登录工具", GroupName = SettingGroupNames.GroupSystem, Description = "显示登录工具按钮")]
    public class LoginButtonViewPresenter : BindableBase, ILoginButtonViewPresenter
    {

    }

    public interface ILoginButtonViewPresenter
    {

    }
}
