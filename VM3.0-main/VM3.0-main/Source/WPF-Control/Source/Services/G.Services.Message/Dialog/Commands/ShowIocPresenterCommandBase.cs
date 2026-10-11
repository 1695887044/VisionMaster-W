namespace G.Services.Message.Dialog.Commands;

public abstract class ShowIocPresenterCommandBase<T> : ShowDialogCommandBase
{
    public override async Task ExecuteAsync(object parameter)
    {
        await IocMessage.Dialog.Show(this.Service, x =>
        {
            x.DialogButton = DialogButton.Sumit;
            this.Invoke(x);
        });
    }

    protected virtual T Service => Ioc.GetService<T>();

    public override bool CanExecute(object parameter)
    {
        return Ioc.Exist<T>() && base.CanExecute(parameter);
    }
}
