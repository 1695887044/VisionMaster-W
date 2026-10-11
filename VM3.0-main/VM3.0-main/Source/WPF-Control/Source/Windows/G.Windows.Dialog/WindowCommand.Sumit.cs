using G.Common.Commands;
using System.Windows;

namespace G.Windows.Dialog;

public class SumitWindowCommand : DisplayMarkupCommandBase
{
    public override void Execute(object parameter)
    {
        if (parameter is Window window)
        {
            window.DialogResult = true;
            SystemCommands.CloseWindow(window);
        }
    }
}
