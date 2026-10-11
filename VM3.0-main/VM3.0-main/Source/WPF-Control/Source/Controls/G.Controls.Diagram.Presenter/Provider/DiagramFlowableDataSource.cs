namespace G.Controls.Diagram.Presenter.Provider;

public class FlowableDiagramDataSource : DiagramDataSource
{
    public FlowableDiagramDataSource(List<Node> nodeSource) : base(nodeSource)
    {
    }

    public FlowableDiagramDataSource(IEnumerable<INodeData> nodes, IEnumerable<ILinkData> links) : base(nodes, links)
    {

    }

    protected override ILinkData CreateLinkData()
    {
        return new FlowableLinkData();
    }

}
