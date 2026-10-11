namespace G.Services.Message.Snack;

public class ShowFatalSnackMessageCommand : ShowSnackMessageCommandBase
{
    public override void Execute(object parameter)
    {
        Ioc<ISnackMessageService>.Instance.ShowFatal(this.Message);
    }
}
