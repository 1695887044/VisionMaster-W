using G.Mvvm.Commands;
using G.Mvvm.ViewModels.Base;
using System.Windows.Input;

namespace G.Test.Mvvm
{
    public class MyViewModel : Bindable
    {
        public MyViewModel()
        {
            IncrementCounterCommand = new RelayCommand(x => IncrementCounter());
        }

        private int _cCounter;
        public int Counter
        {
            get { return _cCounter; }
            set
            {
                _cCounter = value;
                RaisePropertyChanged();
            }
        }


        public ICommand IncrementCounterCommand { get; }

        private void IncrementCounter() => Counter++;
    }
}
