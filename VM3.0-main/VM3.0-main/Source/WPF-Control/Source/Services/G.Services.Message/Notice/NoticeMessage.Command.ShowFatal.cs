namespace G.Services.Message.Notice;

public class ShowFatalNoticeMessageCommand : ShowNoticeMessageCommandBase
{
    public override void Execute(object parameter)
    {
        Ioc<INoticeMessageService>.Instance.ShowFatal(this.Message);
    }
}
