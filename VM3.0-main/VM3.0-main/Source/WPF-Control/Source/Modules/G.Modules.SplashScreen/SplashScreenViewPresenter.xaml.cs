
global using G.Mvvm.ViewModels.Base;
global using G.Services.Common.SplashScreen;

namespace G.Modules.SplashScreen;

public class SplashScreenViewPresenter : BindableBase, ISplashScreenViewPresenter
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
