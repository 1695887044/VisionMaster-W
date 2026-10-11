namespace G.Extensions.Command.ScrollViewers;

public class ScrollViewerLineRightCommand : ScrollViewerScrollToEndCommand
{
    protected override void Invoke(ScrollViewer scrollViewer)
    {
        scrollViewer.LineRight();
    }
}
