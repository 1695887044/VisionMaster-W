namespace G.Services.Message.Notice;

public class ShowWarnNoticeMessageCommand : ShowNoticeMessageCommandBase
{
    public override void Execute(object parameter)
    {
        Ioc<INoticeMessageService>.Instance.ShowWarn(this.Message);
    }
}
