namespace G.VisionMaster.ResultPresenter.ResultPresenters;

public class ValueResultPresenter<T> : ValueResultPresenterBase
{
    public ValueResultPresenter(T value)
    {
        this.Value = value;
    }
    private T _value;
    public T Value
    {
        get { return _value; }
        set
        {
            _value = value;
            RaisePropertyChanged();
        }
    }
}
