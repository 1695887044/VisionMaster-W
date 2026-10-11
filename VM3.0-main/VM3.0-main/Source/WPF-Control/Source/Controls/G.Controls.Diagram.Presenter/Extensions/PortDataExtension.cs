namespace G.Controls.Diagram.Presenter.Extensions;

public static class PortDataExtension
{
    public static IEnumerable<INodeData> GetFromNodeDatas(this IPortData portData, IDiagramData diagramData)
    {
        return portData.GetFromLinkDatas(diagramData).Select(x => x.GetFromNodeData(diagramData));
    }

    public static IEnumerable<ILinkData> GetToLinkDatas(this IPortData port, IDiagramData diagramData)
    {
        return diagramData.LinkDatas.Where(l => l.FromPortID == port.ID);
    }

    public static IEnumerable<ILinkData> GetFromLinkDatas(this IPortData port, IDiagramData diagramData)
    {
        return diagramData.LinkDatas.Where(l => l.FromPortID == port.ID);
    }
}

