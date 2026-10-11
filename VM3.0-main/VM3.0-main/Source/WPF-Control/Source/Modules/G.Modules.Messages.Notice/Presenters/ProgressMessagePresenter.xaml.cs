using G.Common.Attributes;
using G.Extensions.FontIcon;
namespace G.Modules.Messages.Notice
{
    [Icon(FontIcons.Refresh)]
    [Display(Name = "进度条消息", Description = "这是一条进度条消息")]
    public class ProgressMessagePresenter : MessagePresenterBase, IPercentNoticeItem
    {
        private double _value;
        public double Value
        {
            get { return _value; }
            set
            {
                _value = value;
                RaisePropertyChanged();
            }
        }
    }
}
