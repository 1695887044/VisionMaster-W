namespace G.Services.Message.Dialog;

public class CancelDialogCommand : DialogCommandBase
{
    public override void Execute(object parameter)
    {
        this.Cancel(parameter);
    }
}
