global using G.Extensions.Mvvm.ViewModels;

namespace G.Controls.Diagram.Presenter.DiagramDatas;

public interface INodeDataGroup : IDisplayBindable
{
    ObservableCollection<INodeData> NodeDatas { get; set; }
}
