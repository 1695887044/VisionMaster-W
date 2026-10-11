using G.Common.Attributes;
using G.Extensions.FontIcon;
using G.Services.Identity.Author;
using G.Services.Message.Dialog.Commands;

namespace G.Modules.Identity.Commands
{
    [Icon(FontIcons.Edit)]
    [Display(Name = "权限管理", GroupName = SettingGroupNames.GroupAuthority, Description = "应用此功能进行权限管理")]
    public class ShowAuthorityViewCommand : ShowIocCommand
    {
        public ShowAuthorityViewCommand()
        {
            this.Type = typeof(IAuthorityViewPresenter);
        }
    }
}

