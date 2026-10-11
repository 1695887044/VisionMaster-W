using G.Common.Attributes;
using G.Extensions.FontIcon;

namespace G.Modules.Messages.Notice
{
    [Icon(FontIcons.ErrorBadge)]
    [Display(Name = "错误消息", Description = "这是一条错误消息")]
    public class ErrorMessagePresenter : MessagePresenterBase
    {
        public ErrorMessagePresenter()
        {
            this.Level = 4;
        }
    }
}
