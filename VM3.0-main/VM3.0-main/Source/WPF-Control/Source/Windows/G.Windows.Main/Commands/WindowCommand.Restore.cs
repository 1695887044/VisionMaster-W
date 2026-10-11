namespace G.Windows.Main.Commands;

[Icon(FontIcons.ChromeRestore)]
[Display(Name = "还原", Description = "点击此按钮将当前窗口还原")]
public class RestoreWindowCommand : WindowCommandBase
{
    public override void Execute(object parameter)
    {
        if (parameter is Window window)
            SystemCommands.RestoreWindow(window);
    }
}
