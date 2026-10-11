namespace G.Controls.Diagram.Presenter.Flowables;

public interface IFlowableNodeData : INodeData, IMessageable, IFlowablePartData
{

    FlowableInvokeMode InvokeMode { get; set; }
    //Task<IFlowableResult> TryInvokeAsync(IFlowableLinkData previors, IFlowableDiagramData diagram);

    Task<bool?> Start(IFlowableDiagramData diagramData, IFlowableLinkData from = null);
}


