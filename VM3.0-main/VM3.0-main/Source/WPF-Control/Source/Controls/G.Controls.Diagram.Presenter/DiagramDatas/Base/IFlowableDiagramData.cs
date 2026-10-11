global using G.Controls.Diagram.Presenter.Flowables;
namespace G.Controls.Diagram.Presenter.DiagramDatas.Base;

public interface IFlowableDiagramData : IDiagramData, IMessageable, IPartDataInvokeable
{
    DiagramFlowableMode FlowableMode { get; set; }
    DiagramFlowableState State { get; set; }
    Task<bool?> Start();
}
