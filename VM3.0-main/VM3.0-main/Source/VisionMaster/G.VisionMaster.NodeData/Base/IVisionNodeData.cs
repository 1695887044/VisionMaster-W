using G.Controls.Diagram.Presenter.Flowables;
using G.Controls.Diagram.Presenter.NodeDatas.Base;
using G.VisionMaster.NodeData.ResultImages;

namespace G.VisionMaster.NodeData.Base;

public interface IVisionNodeData : IFlowableNodeData, IResultPresenterNodeData, IResultImageSourceNodeData, IHelpNodeData
{
    bool UseInvokedPart { get; set; }
}

public interface IVideoCaptureNodeData : IVisionNodeData
{

}

public interface IVisionNodeData<T> : IVisionNodeData
{
    public List<IVisionResultImage<T>> ResultImages { get; }

    public T Mat { get; }
}
