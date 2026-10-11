using G.Common.Attributes;
using G.Extensions.FontIcon;

namespace G.Modules.Messages.Notice
{
    [Icon(FontIcons.DefenderApp)]
    [Display(Name = "严重错误", Description = "这是一条严重错误")]
    public class FatalMessagePresenter : MessagePresenterBase
    {
        public FatalMessagePresenter()
        {
            this.Level = 5;
        }
    }
}
