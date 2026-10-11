namespace G.Extensions.Command.ScrollViewers;

public class ScrollViewerPageRightCommand : ScrollViewerScrollToEndCommand
{
    protected override void Invoke(ScrollViewer scrollViewer)
    {
        scrollViewer.PageRight();
    }
}
