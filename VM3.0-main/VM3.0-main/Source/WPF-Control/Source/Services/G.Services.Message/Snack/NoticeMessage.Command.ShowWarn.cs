namespace G.Services.Message.Snack;

public class ShowWarnSnackMessageCommand : ShowSnackMessageCommandBase
{
    public override void Execute(object parameter)
    {
        Ioc<ISnackMessageService>.Instance.ShowWarn(this.Message);
    }
}
