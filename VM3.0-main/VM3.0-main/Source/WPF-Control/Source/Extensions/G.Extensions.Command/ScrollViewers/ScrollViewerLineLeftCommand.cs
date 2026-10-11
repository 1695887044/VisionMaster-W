global using System.Windows.Controls;

namespace G.Extensions.Command.ScrollViewers;

public class ScrollViewerLineLeftCommand : ScrollViewerScrollToHomeCommand
{
    protected override void Invoke(ScrollViewer scrollViewer)
    {
        scrollViewer.LineLeft();
    }
}
