using G.Common.Attributes;
using G.Extensions.Common;
using G.Extensions.FontIcon;
using G.Services.Identity;
using G.Services.Message.Dialog.Commands;
using G.Services.Setting;
using System.ComponentModel.DataAnnotations;
using System.Windows;

namespace G.Modules.Login.Commands;

[Icon(FontIcons.BlockContact)]
[Display(Name = "退出登录", GroupName = SettingGroupNames.GroupSystem, Description = "退出当前账号的登录")]
public class LogoutCommand : IocCommandBase<ILoginService>
{
    public override Task ExecuteAsync(object parameter)
    {
        this.Service.Logout(out string message);
        if (LoginOptions.Instance.UseLogoutRestart)
            Application.Current.Restart();
        return base.ExecuteAsync(parameter);
    }

    public override bool CanExecute(object parameter)
    {
        return base.CanExecute(parameter) && this.Service?.User != null;
    }
}
