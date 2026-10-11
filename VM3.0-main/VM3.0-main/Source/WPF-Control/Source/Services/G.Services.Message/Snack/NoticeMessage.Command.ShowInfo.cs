namespace G.Services.Message.Snack;

public class ShowInfoSnackMessageCommand : ShowSnackMessageCommandBase
{
    public override void Execute(object parameter)
    {
        Ioc<ISnackMessageService>.Instance.ShowInfo(this.Message);
    }
}
