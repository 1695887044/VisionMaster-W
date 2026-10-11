namespace G.Controls.Diagram.Presenter.Flowables;

public interface IFlowablePortData : IFlowablePartData, IPortData, ITextPortData
{
    Task<IFlowableResult> TryInvokeAsync(IFlowableLinkData linkData, IFlowableDiagramData diagram);

    Task<bool?> Start(IFlowableDiagramData diagramData,IFlowableNodeData nodeData, Predicate<IFlowableLinkData> predicate = null);

}
