using G.Common.Attributes;
using G.Extensions.FontIcon;

namespace G.Modules.Messages.Notice
{
    [Icon(FontIcons.OverwriteWordsFillKorean)]
    [Display(Name = "警告消息", Description = "这是一条警告消息")]
    public class WarnMessagePresenter : MessagePresenterBase
    {
        public WarnMessagePresenter()
        {
            this.Level = 3;
        }
    }
}
