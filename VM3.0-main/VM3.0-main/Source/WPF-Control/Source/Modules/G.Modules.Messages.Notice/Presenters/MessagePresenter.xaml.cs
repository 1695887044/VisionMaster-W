using G.Extensions.Mvvm.ViewModels.Base;

namespace G.Modules.Messages.Notice
{
    public abstract class MessagePresenterBase : DisplayBindableBase, INoticeItem
    {
        public string Time { get; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        private string _message;
        public string Message
        {
            get { return _message; }
            set
            {
                _message = value;
                RaisePropertyChanged();
            }
        }
        public int Level { get; set; }
    }
}
