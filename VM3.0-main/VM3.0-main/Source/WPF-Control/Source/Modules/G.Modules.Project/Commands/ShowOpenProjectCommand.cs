// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

namespace G.Modules.Project.Commands;

[Icon("\xE77F")]
[Display(Name = "打开项目", Description = "打开当前选中项目")]
public class ShowOpenProjectCommand : DisplayMarkupCommandBase
{
    public override async Task ExecuteAsync(object parameter)
    {
        if (parameter is IProjectItem project)
            await IocProject.Instance.ShowOpenProject(project);
    }
    public override bool CanExecute(object parameter)
    {
        return IocProject.Instance != null;
    }
}
