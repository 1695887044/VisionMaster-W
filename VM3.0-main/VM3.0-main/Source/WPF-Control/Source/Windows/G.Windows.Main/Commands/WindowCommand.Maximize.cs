namespace G.Windows.Main.Commands;

[Icon(FontIcons.ChromeMaximize)]
[Display(Name = "最大化", Description = "点击此按钮将当前窗口最大化")]
public class MaximizeWindowCommand : WindowCommandBase
{
    public override void Execute(object parameter)
    {
        if (parameter is Window window)
            SystemCommands.MaximizeWindow(window);
    }
}
