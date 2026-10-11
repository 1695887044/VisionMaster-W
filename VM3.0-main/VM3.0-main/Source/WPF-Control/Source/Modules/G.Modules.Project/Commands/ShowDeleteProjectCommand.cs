global using G.Modules.Project;

namespace G.Modules.Project.Commands;

[Display(Name = "删除项目", Description = "删除当前选中的项目")]
public class ShowDeleteProjectCommand : DisplayMarkupCommandBase
{
    public override async Task ExecuteAsync(object parameter)
    {
        if (parameter is IProjectItem project)
            await IocProject.Instance.ShowDeleteProject(project);
    }
    public override bool CanExecute(object parameter)
    {
        return IocProject.Instance != null;
    }
}