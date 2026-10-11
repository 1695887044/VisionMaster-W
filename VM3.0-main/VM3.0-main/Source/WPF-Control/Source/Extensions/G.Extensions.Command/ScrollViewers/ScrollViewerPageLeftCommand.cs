namespace G.Extensions.Command.ScrollViewers;

public class ScrollViewerPageLeftCommand : ScrollViewerScrollToHomeCommand
{
    protected override void Invoke(ScrollViewer scrollViewer)
    {
        scrollViewer.PageLeft();
    }
}
