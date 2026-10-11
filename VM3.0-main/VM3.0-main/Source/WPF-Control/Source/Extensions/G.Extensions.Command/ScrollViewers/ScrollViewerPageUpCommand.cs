namespace G.Extensions.Command.ScrollViewers;

public class ScrollViewerPageUpCommand : ScrollViewerScrollToTopCommand
{
    protected override void Invoke(ScrollViewer scrollViewer)
    {
        scrollViewer.PageUp();
    }
}
