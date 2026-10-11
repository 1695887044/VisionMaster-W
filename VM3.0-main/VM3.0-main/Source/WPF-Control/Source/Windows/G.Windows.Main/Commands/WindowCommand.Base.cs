using G.Common.Commands;
using System.Windows.Input;

namespace G.Windows.Main.Commands;

public abstract class WindowCommandBase : DisplayMarkupCommandBase, ICommand
{
    public override bool CanExecute(object parameter)
    {
        return parameter is Window && base.CanExecute(parameter);
    }

    public override Task ExecuteAsync(object parameter)
    {
        return Task.CompletedTask;
    }
}
