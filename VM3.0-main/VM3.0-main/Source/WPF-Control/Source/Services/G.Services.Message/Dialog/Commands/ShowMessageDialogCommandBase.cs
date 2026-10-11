using System.Windows.Input;

namespace G.Services.Message.Dialog.Commands;

[Icon("\xE77F")]
[Display(Name = "显示消息", Description = "显示弹窗消息")]
public abstract class ShowMessageDialogCommandBase : ShowDialogCommandBase, ICommand
{
    public string Message { get; set; } = "默认消息";
}
