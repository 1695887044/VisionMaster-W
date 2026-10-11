global using G.Common.Interfaces.Where;
using G.Common.Attributes;
using G.Extensions.FontIcon;

namespace G.Modules.Identity
{
    public class Filter : IFilterable
    {
        public bool IsMatch(object obj)
        {
            return true;
        }
    }

    [Icon(FontIcons.AddFriend)]
    [Display(Name = "用户管理", GroupName = SettingGroupNames.GroupAuthority, Description = "应用此功能进行用户管理")]
    public class UserViewPresenter : IUserViewPresenter
    {

    }
}

