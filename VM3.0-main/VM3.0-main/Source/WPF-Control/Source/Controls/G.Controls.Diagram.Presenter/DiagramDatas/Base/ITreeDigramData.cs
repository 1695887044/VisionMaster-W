global using G.Extensions.Mvvm.ViewModels.Tree;

namespace G.Controls.Diagram.Presenter.DiagramDatas.Base;

public interface ITreeDigramData
{
    TreeNodeBase<Part> Root { get; set; }
}
