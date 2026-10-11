namespace G.Services.Message.Dialog.Commands;

public class ShowWaitCommand : ShowMessageDialogCommandBase
{
    public override async Task ExecuteAsync(object parameter)
    {
        Func<ICancelable, bool> func = c =>
        {
            Thread.Sleep(5000);
            return true;
        };
        await IocMessage.Dialog.ShowWait(func);
    }
}
