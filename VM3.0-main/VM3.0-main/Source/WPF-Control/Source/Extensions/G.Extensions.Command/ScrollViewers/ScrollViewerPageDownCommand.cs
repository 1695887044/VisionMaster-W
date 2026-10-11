namespace G.Extensions.Command.ScrollViewers;

public class ScrollViewerPageDownCommand : ScrollViewerScrollToBottomCommand
{
    protected override void Invoke(ScrollViewer scrollViewer)
    {
        scrollViewer.PageDown();
    }
}
