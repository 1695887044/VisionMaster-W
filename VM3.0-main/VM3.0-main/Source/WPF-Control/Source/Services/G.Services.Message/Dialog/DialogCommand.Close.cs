namespace G.Services.Message.Dialog;

public class CloseDialogCommand : DialogCommandBase
{
    public override void Execute(object parameter)
    {
        this.Close(parameter);
    }
}
