global using G.Controls.Diagram.GraphSource;

namespace G.Controls.Diagram;

public interface IDiagramDataSource : IGraphSource
{
    List<ILinkData> GetLinkDatas();
    List<INodeData> GetNodeDatas();
}
