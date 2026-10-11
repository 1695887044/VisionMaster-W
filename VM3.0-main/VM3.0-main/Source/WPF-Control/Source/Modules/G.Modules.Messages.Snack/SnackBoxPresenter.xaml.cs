using G.Mvvm.ViewModels.Base;
using System.Collections.ObjectModel;

namespace G.Modules.Messages.Snack
{
    public class SnackBoxPresenter : BindableBase
    {
        private ObservableCollection<ISnackItem> _collection = new ObservableCollection<ISnackItem>();
        public ObservableCollection<ISnackItem> Collection
        {
            get { return _collection; }
            set
            {
                _collection = value;
                RaisePropertyChanged();
            }
        }
    }
}
