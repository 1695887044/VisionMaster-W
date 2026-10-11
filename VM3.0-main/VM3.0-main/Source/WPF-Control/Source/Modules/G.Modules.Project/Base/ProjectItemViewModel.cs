using G.Extensions.Mvvm.ViewModels;

namespace G.Modules.Project.Base;

public class ProjectItemViewModel : SelectBindable<IProjectItem>
{
    public ProjectItemViewModel(IProjectItem project) : base(project)
    {
    }

    private string _groupName;
    public string GroupName
    {
        get { return _groupName; }
        set
        {
            _groupName = value;
            RaisePropertyChanged();
        }
    }
}
