using G.Services.Setting;

namespace G.Modules.Project;

[Display(Name = "工程管理", GroupName = SettingGroupNames.GroupData, Description = "应用此功能可以管理工程信息")]
public class ProjectViewPresenter : IProjectViewPresenter
{
    private readonly IProjectService _projectService;
    public ProjectViewPresenter(IProjectService projectService)
    {
        this._projectService = projectService;
    }
}
