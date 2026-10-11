using G.Common.Commands;

namespace G.Services.Revertible;

public class RedoCommand : DisplayMarkupCommandBase
{
    public override void Execute(object parameter)
    {
        Ioc<IRevertibleService>.Instance?.Redo();
    }

    public override bool CanExecute(object parameter)
    {
        return Ioc<IRevertibleService>.Instance?.CanRedo == true;
    }
}
