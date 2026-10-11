namespace G.Extensions.Command.ScrollViewers;

public class ScrollViewerLineDownCommand : ScrollViewerScrollToBottomCommand
{
    protected override void Invoke(ScrollViewer scrollViewer)
    {
        scrollViewer.LineDown();
    }
}
