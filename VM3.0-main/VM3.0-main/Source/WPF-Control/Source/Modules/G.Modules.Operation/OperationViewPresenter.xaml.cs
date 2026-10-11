using G.Common.Attributes;
using G.Extensions.FontIcon;
using G.Services.Operation;
using G.Services.Setting;
using System.ComponentModel.DataAnnotations;

namespace G.Modules.Operation
{
    [Icon(FontIcons.Handwriting)]
    [Display(Name = "操作日志", GroupName = SettingGroupNames.GroupAuthority, Description = "应用此功能查看操作日志")]
    public class OperationViewPresenter : IOperationViewPresenter
    {

    }
}
