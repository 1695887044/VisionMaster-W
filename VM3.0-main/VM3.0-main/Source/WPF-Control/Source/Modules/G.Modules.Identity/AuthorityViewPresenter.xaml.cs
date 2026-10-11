using G.Common.Attributes;
using G.Extensions.FontIcon;
using G.Services.Identity.Author;

namespace G.Modules.Identity
{
    [Icon(FontIcons.Edit)]
    [Display(Name = "用户管理", GroupName = SettingGroupNames.GroupAuthority, Description = "应用此功能进行用户管理")]
    public class AuthorityViewPresenter : IAuthorityViewPresenter
    {
    }
}
