namespace G.Extensions.Command.ScrollViewers;

[Icon("\xE77F")]
[Display(Name = "滚动左侧", Description = "将当前滚动条滚动到最左侧")]
public class ScrollViewerScrollToHomeCommand : ScrollViewerScrollToCommandBase
{
    protected override void Invoke(ScrollViewer scrollViewer)
    {
        scrollViewer.ScrollToHome();
    }
    protected override bool CanInvoke(ScrollViewer scrollViewer)
    {
        return scrollViewer.HorizontalOffset > 0;
    }
}
