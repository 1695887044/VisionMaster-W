using G.Extensions.Mvvm.ViewModels.Base;
using G.Services.Message.Snack;

namespace G.Modules.Messages.Snack
{
    public abstract class MessagePresenterBase : DisplayBindableBase, ISnackItem
    {
        public string Time { get; } = DateTime.Now.ToString("HH:mm:ss");
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
