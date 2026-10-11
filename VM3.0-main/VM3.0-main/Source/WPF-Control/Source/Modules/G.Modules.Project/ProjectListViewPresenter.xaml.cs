global using G.Extensions.Mvvm.ViewModels.Base;
using G.Extensions.FontIcon;

namespace G.Modules.Project;

[Icon(FontIcons.List)]
public class ProjectListViewPresenter : DisplayBindableBase
{
    private IProjectItem _selectedItem;
    public IProjectItem SelectedItem
    {
        get { return _selectedItem; }
        set
        {
            _selectedItem = value;
            RaisePropertyChanged();
        }
    }
}
