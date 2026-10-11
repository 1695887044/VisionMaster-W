using G.Common.Attributes;
using G.Extensions.FontIcon;
using System.ComponentModel.DataAnnotations;

namespace G.Modules.Messages.Snack
{
    [Icon(FontIcons.Refresh)]
    [Display(Name = "进度消息", Description = "这是一条进度消息")]
    public class StringMessagePresenter : MessagePresenterBase
    {

    }
}
