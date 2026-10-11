namespace G.Services.Message.Dialog.Commands;

public class ShowNotImplementedCommand : ShowMessageCommand
{
    public ShowNotImplementedCommand()
    {
        this.Message = "功能暂未实现";
    }
}
