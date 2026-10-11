namespace G.Controls.Diagram.Presenter.Flowables;

public interface IFlowablePartData : IPartData, IFlowable
{
    FlowableState State { get; set; }
}
