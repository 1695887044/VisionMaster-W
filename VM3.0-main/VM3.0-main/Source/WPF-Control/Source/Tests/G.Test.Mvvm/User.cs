using G.Mvvm.ViewModels.Base;

namespace G.Test.Mvvm
{
    public class User : Bindable
    {
        private string _name;
        public string Name
        {
            get { return _name; }
            set
            {
                _name = value;
                RaisePropertyChanged();
            }
        }

    }
}
