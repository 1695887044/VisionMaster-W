namespace G.Services.Message.Notice;

public class ShowDialogNoticeMessageCommand : ShowNoticeMessageCommandBase
{
    public override async void Execute(object parameter)
    {
        bool? r = await Ioc<INoticeMessageService>.Instance.ShowDialog(this.Message);
        if (r == true)
            Ioc<INoticeMessageService>.Instance.ShowSuccess(this.Message);
        else
            Ioc<INoticeMessageService>.Instance.ShowError(this.Message);
    }
}
