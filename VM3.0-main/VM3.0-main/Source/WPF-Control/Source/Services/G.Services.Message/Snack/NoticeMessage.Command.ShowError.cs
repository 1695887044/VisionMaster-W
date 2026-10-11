namespace G.Services.Message.Snack;

public class ShowErrorSnackMessageCommand : ShowSnackMessageCommandBase
{
    public override void Execute(object parameter)
    {
        Ioc<ISnackMessageService>.Instance.ShowError(this.Message);
    }
}
