namespace G.Services.Message.Snack;

public class ShowDialogSnackMessageCommand : ShowSnackMessageCommandBase
{
    public override async void Execute(object parameter)
    {
        bool? r = await Ioc<ISnackMessageService>.Instance.ShowDialog(this.Message);
        if (r == true)
            Ioc<ISnackMessageService>.Instance.ShowSuccess(this.Message);
        else
            Ioc<ISnackMessageService>.Instance.ShowError(this.Message);
    }
}
