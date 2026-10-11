using G.Common.Attributes;
using G.Extensions.FontIcon;
using G.Services.Message.Dialog.Commands;

namespace G.Modules.Identity.Commands
{
    [Icon(FontIcons.AddFriend)]
    [Display(Name = "用户管理", GroupName = SettingGroupNames.GroupAuthority, Description = "应用此功能进行用户管理")]
    public class ShowUserViewCommand : ShowIocCommand
    {
        public ShowUserViewCommand()
        {
            this.Type = typeof(IUserViewPresenter);
        }
    }
}

