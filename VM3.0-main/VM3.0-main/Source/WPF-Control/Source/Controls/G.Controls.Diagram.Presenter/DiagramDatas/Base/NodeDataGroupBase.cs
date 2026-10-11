global using G.Extensions.Mvvm.ViewModels.Base;

namespace G.Controls.Diagram.Presenter.DiagramDatas.Base;

[Icon("\xE722")]
public abstract class NodeDataGroupBase : GroupDisplayBindableBase<INodeData>, INodeDataGroup
{
    private bool _isTemplate;
    public bool IsTemplate
    {
        get { return _isTemplate; }
        set
        {
            _isTemplate = value;
            RaisePropertyChanged();
        }
    }
}
