using G.Common.Attributes;
using G.Extensions.FontIcon;
using G.Services.Identity;
using G.Services.Message.Dialog.Commands;
using G.Services.Setting;
using System.ComponentModel.DataAnnotations;
using System.Windows;

namespace G.Modules.Login.Commands;

[Icon(FontIcons.AddFriend)]
[Display(Name = "登录", GroupName = SettingGroupNames.GroupSystem, Description = "显示登录页面")]
public class LoginCommand : IocCommandBase<ILoginService>
{
    public override Task ExecuteAsync(object parameter)
    {
        if (Application.Current is ILoginableApplication loginable)
            loginable.Login();
        return base.ExecuteAsync(parameter);
    }
    public override bool CanExecute(object parameter)
    {
        return base.CanExecute(parameter) && this.Service?.User == null;
    }
}
