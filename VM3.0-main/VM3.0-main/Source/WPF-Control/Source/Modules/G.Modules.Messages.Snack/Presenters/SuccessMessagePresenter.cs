using G.Common.Attributes;
using G.Extensions.FontIcon;
using System.ComponentModel.DataAnnotations;

namespace G.Modules.Messages.Snack
{
    [Icon(FontIcons.Completed)]
    [Display(Name = "成功消息", Description = "这是一条成功消息")]
    public class SuccessMessagePresenter : MessagePresenterBase
    {
        public SuccessMessagePresenter()
        {
            this.Level = 1;
        }
    }
}
