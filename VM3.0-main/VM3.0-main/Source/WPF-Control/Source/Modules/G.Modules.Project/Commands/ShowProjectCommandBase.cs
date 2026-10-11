global using G.Services.Message;
global using G.Services.Message.Dialog.Commands;

namespace G.Modules.Project.Commands;

public abstract class ShowProjectCommandBase : ShowIocPresenterCommandBase<IProjectService>
{
    public override bool CanExecute(object parameter)
    {
        return IocProject.Instance != null && base.CanExecute(parameter);
    }
}