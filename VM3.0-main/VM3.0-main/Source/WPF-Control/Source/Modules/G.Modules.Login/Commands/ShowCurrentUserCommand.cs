using G.Common.Attributes;
using G.Extensions.FontIcon;
using G.Services.Message.Dialog.Commands;
using G.Services.Setting;
using System.ComponentModel.DataAnnotations;

namespace G.Modules.Login.Commands;

[Icon(FontIcons.View)]
[Display(Name = "用户信息", GroupName = SettingGroupNames.GroupSystem, Description = "显示当前用户信息")]
public class ShowCurrentUserCommand : ShowIocPresenterCommandBase<ICurrentUserViewPresenter>
{

}