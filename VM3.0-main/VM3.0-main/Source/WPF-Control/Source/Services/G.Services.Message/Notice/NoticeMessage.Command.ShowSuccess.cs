namespace G.Services.Message.Notice;

public class ShowSuccessNoticeMessageCommand : ShowNoticeMessageCommandBase
{
    public override void Execute(object parameter)
    {
        Ioc<INoticeMessageService>.Instance.ShowSuccess(this.Message);
    }
}
