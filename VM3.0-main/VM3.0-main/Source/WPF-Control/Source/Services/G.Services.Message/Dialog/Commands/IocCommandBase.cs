namespace G.Services.Message.Dialog.Commands;

public abstract class IocCommandBase<T> : ShowDialogCommandBase
{
    public override bool CanExecute(object parameter)
    {
        return Ioc.Exist<T>() && base.CanExecute(parameter);
    }

    protected T Service => Ioc.GetService<T>(false);
}
