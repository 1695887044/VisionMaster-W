using G.Common.Attributes;
using G.Extensions.FontIcon;
using G.Extensions.Mvvm.ViewModels.Base;
using G.Services.Identity;

namespace G.Modules.Login
{
    [Icon(FontIcons.Connect)]
    public class LoginedSplashViewPresenter : DisplayBindableBase, ILoginedSplashViewPresenter
    {
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
    }
}
