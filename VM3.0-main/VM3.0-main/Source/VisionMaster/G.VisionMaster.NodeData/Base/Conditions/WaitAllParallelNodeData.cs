using G.Controls.Diagram.Presenter.DiagramDatas.Base;
using G.Controls.Diagram.Presenter.Flowables;

namespace G.VisionMaster.NodeData.Base.Conditions;
/// <summary>
/// 等待所有并行节点执行完再执行后续逻辑
/// </summary>
/// <typeparam name="T"></typeparam>
public abstract class WaitAllParallelNodeData<T> : VisionNodeData<T> where T : IDisposable
{
    private int _resultCount = 0;
    protected virtual void OnParallelFromNodeDataInvoke(ISrcVisionNodeData<T> srcImageNodeData, IVisionNodeData<T> from, IFlowableDiagramData diagram)
    {

    }
    protected virtual FlowableResult<T> OnAllFrommParallelsInvoked(ISrcVisionNodeData<T> srcImageNodeData, IVisionNodeData<T> from, IFlowableDiagramData diagram)
    {
        return this.OK(from.Mat);
    }

    protected override FlowableResult<T> Invoke(ISrcVisionNodeData<T> srcImageNodeData, IVisionNodeData<T> from, IFlowableDiagramData diagram)
    {
        this.OnParallelFromNodeDataInvoke(srcImageNodeData, from, diagram);
        this._resultCount++;
        //  Do ：等待所有多线程节点执行完再执行
        if (this._resultCount == this.FromNodeDatas.OfType<IFlowableNodeData>().Where(x => x.InvokeMode == FlowableInvokeMode.Parallel).Count())
        {
            //  Do ：前序节点都执行完了
            this._resultCount = 0;
            return this.OnAllFrommParallelsInvoked(srcImageNodeData, from, diagram);
        }
        else
        {
            return this.Break(from.Mat);
        }
    }
}

