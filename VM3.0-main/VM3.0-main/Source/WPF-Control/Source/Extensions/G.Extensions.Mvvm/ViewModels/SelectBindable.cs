global using G.Common.Interfaces;

namespace G.Extensions.Mvvm.ViewModels;

public partial class SelectBindable<T> : ModelBindable<T>, ISelectable
{
    public SelectBindable(T t) : base(t)
    {

    }

    private bool _isSelected;
    [Browsable(false)]
    public bool IsSelected
    {
        get { return _isSelected; }
        set
        {
            _isSelected = value;
            RaisePropertyChanged();
        }
    }

}
