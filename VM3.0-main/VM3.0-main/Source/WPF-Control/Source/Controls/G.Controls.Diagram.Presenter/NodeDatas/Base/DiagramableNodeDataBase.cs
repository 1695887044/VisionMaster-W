using G.Controls.Diagram.Presenter.DiagramDatas.Base;
using G.Controls.Diagram.Presenter.Extensions;

namespace G.Controls.Diagram.Presenter.NodeDatas.Base;

public interface IDiagramableNodeData : INodeData
{
    IDiagramData DiagramData { get; set; }
}

public abstract class DiagramableNodeDataBase : TextNodeData, IDiagramableNodeData
{
    [Browsable(false)]
    public IDiagramData DiagramData { get; set; }
    protected override void Loaded(object obj)
    {
        base.Loaded(obj);
        if (obj is IDiagramData diagramData)
            this.DiagramData = diagramData;
    }
    [JsonIgnore]
    [Browsable(false)]
    public IEnumerable<INodeData> AllFromNodeDatas => this.GetAllFromNodeDatas();
    [JsonIgnore]
    [Browsable(false)]
    public IEnumerable<INodeData> FromNodeDatas => this.GetFromNodeDatas();
    [JsonIgnore]
    [Browsable(false)]
    public IEnumerable<INodeData> ToNodeDatas => this.GetToNodeDatas();
}
