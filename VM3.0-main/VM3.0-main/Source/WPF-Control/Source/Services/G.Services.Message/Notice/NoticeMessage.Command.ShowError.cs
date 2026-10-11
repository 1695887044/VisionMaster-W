namespace G.Services.Message.Notice;

public class ShowErrorNoticeMessageCommand : ShowNoticeMessageCommandBase
{
    public override void Execute(object parameter)
    {
        Ioc<INoticeMessageService>.Instance.ShowError(this.Message);
    }
}
