// Copyright © 2024 By G(https://github.com/G) https://github.com/G/WPF-Control

namespace G.Services.Message.Notice;

public class ShowInfoNoticeMessageCommand : ShowNoticeMessageCommandBase
{
    public override void Execute(object parameter)
    {
        IocMessage.Dialog.Show(this.Message);
        Ioc<INoticeMessageService>.Instance.ShowInfo(this.Message);
    }
}

