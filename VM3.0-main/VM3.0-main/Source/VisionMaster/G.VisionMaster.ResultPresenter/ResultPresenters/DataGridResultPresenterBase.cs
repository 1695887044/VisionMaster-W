namespace G.VisionMaster.ResultPresenter.ResultPresenters;

public abstract class DataGridResultPresenterBase : ResultPresenterBase
{
    private Type _type;
    public Type Type
    {
        get { return _type; }
        set
        {
            _type = value;
            RaisePropertyChanged();
        }
    }
}
