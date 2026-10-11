using G.Mvvm.Commands.Base;

namespace G.Mvvm.Commands;

public class RelayCommand : CommandBase
{
    protected Action<object> _action;
    protected readonly Predicate<object> _canExecute;

    public RelayCommand(Action<object> action)
    {
        _action = action;
    }
    public RelayCommand(Action<object> execute, Predicate<object> canExecute) : this(execute)
    {
        _canExecute = canExecute;
    }

    public override void Execute(object parameter)
    {
        if (_action != null)
            _action(parameter);
    }

    public override bool CanExecute(object parameter)
    {
        return _canExecute == null ? true : _canExecute.Invoke(parameter);
    }
}
