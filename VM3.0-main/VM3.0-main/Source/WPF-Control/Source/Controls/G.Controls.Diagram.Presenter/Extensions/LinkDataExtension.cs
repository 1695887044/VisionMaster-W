namespace G.Controls.Diagram.Presenter.Extensions;

public static class LinkDataExtension
{
    public static INodeData GetToNodeData(this ILinkData linkData, IDiagramData diagramData)
    {
        return diagramData.NodeDatas.FirstOrDefault(l => l.ID == linkData.ToNodeID);
    }


    public static INodeData GetFromNodeData(this ILinkData linkData, IDiagramData diagramData)
    {
        if (linkData == null)
            return null;
        return diagramData.NodeDatas.FirstOrDefault(l => l.ID == linkData.FromNodeID);
    }

    public static IPortData GetFromPortData(this ILinkData linkData, IDiagramData diagramData)
    {
        return diagramData.GetPortDatas().FirstOrDefault(l => l.ID == linkData.FromPortID);
    }
    public static IPortData GetToPortData(this ILinkData linkData, IDiagramData diagramData)
    {
        return diagramData.GetPortDatas().FirstOrDefault(l => l.ID == linkData.ToPortID);
    }
}

