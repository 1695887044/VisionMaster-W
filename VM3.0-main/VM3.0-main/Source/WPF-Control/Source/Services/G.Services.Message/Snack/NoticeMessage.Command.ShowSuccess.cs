namespace G.Services.Message.Snack;

public class ShowSuccessSnackMessageCommand : ShowSnackMessageCommandBase
{
    public override void Execute(object parameter)
    {
        Ioc<ISnackMessageService>.Instance.ShowSuccess(this.Message);
    }
}
