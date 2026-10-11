namespace G.Extensions.Command.ScrollViewers;

public class ScrollViewerLineUpCommand : ScrollViewerScrollToTopCommand
{
    protected override void Invoke(ScrollViewer scrollViewer)
    {
        scrollViewer.LineUp();
    }
}
