using System.Windows.Controls;

namespace G.Extensions.Common;

public static class TreeViewExtension
{
    public static void SelectNone(this TreeView treeView)
    {
        foreach (var item in treeView.GetChildren<TreeViewItem>())
        {
            item.IsSelected = false;
        }
    }
}
