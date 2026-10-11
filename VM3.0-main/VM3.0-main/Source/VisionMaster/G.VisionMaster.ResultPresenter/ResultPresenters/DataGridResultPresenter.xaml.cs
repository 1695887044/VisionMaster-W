namespace G.VisionMaster.ResultPresenter.ResultPresenters;

public class DataGridResultPresenter<T> : DataGridResultPresenterBase where T : IResultPresenterItem
{
    public DataGridResultPresenter(IEnumerable<T> values)
    {
        this.Collection = values.ToObservable();
        for (int i = 0; i < this.Collection.Count(); i++)
        {
            this.Collection[i].Index = i + 1;
        }
        this.Type = typeof(T);
    }
    private ObservableCollection<T> _collection = new ObservableCollection<T>();
    public ObservableCollection<T> Collection
    {
        get { return _collection; }
        set
        {
            _collection = value;
            RaisePropertyChanged();
        }
    }

    private T _selectedItem;
    public T SelectedItem
    {
        get { return _selectedItem; }
        set
        {
            _selectedItem = value;
            RaisePropertyChanged();
        }
    }
}
