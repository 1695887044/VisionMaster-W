namespace G.Controls.Diagram.Presenter.Flowables;

public interface IFlowableLinkData : ILinkData, IFlowablePartData
{
    Task<bool?> Start(IFlowableDiagramData diagramData);
}
