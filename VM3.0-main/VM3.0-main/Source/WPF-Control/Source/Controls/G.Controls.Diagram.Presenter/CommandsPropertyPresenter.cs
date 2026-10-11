using G.Controls.Diagram.Presenter.Extensions;
using G.Controls.Form;
using G.Presenters.Common;

namespace G.Controls.Diagram.Presenter;

public class CommandsPropertyPresenter : DialogCommandsPresenter<TabFormPresenter>
{
    public IDiagramableNodeData NodeData { get; set; }
    public CommandsPropertyPresenter(IDiagramableNodeData nodeData) : base(new TabFormPresenter(nodeData))
    {
        this.NodeData = nodeData;
    }
}
