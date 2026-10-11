using G.Common.Attributes;
using G.Extensions.FontIcon;

namespace G.Modules.Messages.Notice
{
    [Icon(FontIcons.Info)]
    [Display(Name = "提示消息", Description = "这是一条提示消息")]
    public class InfoMessagePresenter : MessagePresenterBase
    {
        public InfoMessagePresenter()
        {
            this.Level = 2;
        }
    }
}
