namespace G.Controls.Diagram.Presenter.Extensions;

public static class DiagramableNodeDataExtension
{
    public static IEnumerable<INodeData> GetFromNodeDatas(this IDiagramableNodeData nodeData)
    {
        return nodeData.GetFromNodeDatas(nodeData.DiagramData);
    }

    public static IEnumerable<INodeData> GetAllFromNodeDatas(this IDiagramableNodeData nodeData)
    {
        return nodeData.GetAllFromNodeDatas(nodeData.DiagramData).Distinct();
    }

    public static IEnumerable<T> GetAllFromNodeDatas<T>(this IDiagramableNodeData nodeData)
    {
        return nodeData.GetAllFromNodeDatas().OfType<T>();
    }

    public static IEnumerable<INodeData> GetToNodeDatas(this IDiagramableNodeData nodeData)
    {
        return nodeData.GetToNodeDatas(nodeData.DiagramData);
    }
}

