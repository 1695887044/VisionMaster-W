using G.Mvvm.ViewModels.Base;
using G.Services.Message.Notice;
using System.Collections.ObjectModel;

namespace G.Modules.Messages.Notice
{
    public class NoticeBoxPresenter : BindableBase
    {
        private ObservableCollection<INoticeItem> _collection = new ObservableCollection<INoticeItem>();
        public ObservableCollection<INoticeItem> Collection
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
