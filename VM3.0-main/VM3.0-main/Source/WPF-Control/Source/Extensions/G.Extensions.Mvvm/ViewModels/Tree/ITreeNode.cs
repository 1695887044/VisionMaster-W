namespace G.Extensions.Mvvm.ViewModels.Tree;

public interface ITreeNode
{
    bool IsExpanded { get; set; }
    bool? IsChecked { get; set; }
    Visibility Visibility { get; set; }
}
