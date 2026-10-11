using G.Controls.Diagram.Presenter.DiagramDatas.Base;
using G.Controls.Diagram.Presenter.Extensions;
using G.Controls.Diagram.Presenter.Flowables;
using G.Controls.Diagram.Presenter.NodeDatas.Base;
using G.VisionMaster.NodeData.ResultImages;
using System.Text.Json.Serialization;

namespace G.VisionMaster.NodeData.Base;

public abstract class VisionNodeData<T> : DemoNodeDataBase, IVisionNodeData<T> where T : IDisposable
{
    ~VisionNodeData()
    {
        this.Dispose();
    }

    [JsonIgnore]
    [Browsable(false)]
    public List<IVisionResultImage<T>> ResultImages => this.GetResultImages().ToList();

    [JsonIgnore]
    public virtual T Mat { get; set; }

    private bool _useInvokedPart = true;
    [DefaultValue(true)]
    [Display(Name = "启用输出历史记录", GroupName = VisionPropertyGroupNames.DisplayParameters, Description = "用于控制是否输出到历史记录和预览图像")]
    public bool UseInvokedPart
    {
        get { return _useInvokedPart; }
        set
        {
            _useInvokedPart = value;
            RaisePropertyChanged();
        }
    }

    protected virtual IEnumerable<IVisionResultImage<T>> GetResultImages()
    {
        yield return new VisionResultImage<T>() { Name = this.Name + "- 图像", Image = this.Mat };
    }

    public override IFlowableResult Invoke(IFlowableLinkData previors, IFlowableDiagramData diagram)
    {
        ISrcVisionNodeData<T> srcData = diagram.GetStartNodeDatas().OfType<ISrcVisionNodeData<T>>().FirstOrDefault();
        IVisionNodeData<T> fromData = this.GetFromNodeData<IVisionNodeData<T>>(diagram, previors);
        return this.InvokeAction(() => this.Invoke(srcData, fromData ?? srcData, this.DiagramData as IFlowableDiagramData));
    }
    protected void UpdateInvokeCurrent()
    {
        if (this.DiagramData == null)
            return;
        if (this.DiagramData is IFlowableDiagramData flowable && flowable.State == DiagramFlowableState.Running)
            return;
        ISrcVisionNodeData<T> srcData = this.DiagramData.GetStartNodeDatas().OfType<ISrcVisionNodeData<T>>().FirstOrDefault();
        INodeData from = this.FromNodeDatas.FirstOrDefault();
        if (this.FromNodeDatas.Count() == 0)
            from = this;
        if (this.FromNodeDatas.Count() > 1)
            return;
        if (from is IVisionNodeData<T> visionNodeData)
        {
            if (visionNodeData.Mat == null)
                return;
            if (!this.IsValid(visionNodeData.Mat))
                return;
            this.InvokeAction(() => this.Invoke(srcData, visionNodeData, this.DiagramData as IFlowableDiagramData));
            if (this.DiagramData is IResultImageSourceDiagramData imageSourceDiagramData)
                imageSourceDiagramData.ResultImageSource = this.ResultImageSource;
        }
    }

    protected abstract bool IsValid(T t);

    private FlowableResult<T> InvokeAction(Func<FlowableResult<T>> invoke)
    {
        this.ResultPresenter = null;
        FlowableResult<T> result = invoke.Invoke();
        if (this.Mat != null && !this.Mat.Equals(result.Value))
            this.Mat?.Dispose();
        this.Mat = result.Value;
        if (this.UseResultImageSource)
        {
            this.UpdateResultImageSource();
            Thread.Sleep(this.PreviewMillisecondsDelay);
        }
        if (this.ResultPresenter == null)
            this.ResultPresenter = this.CreateResultPresenter();
        return result;
    }

    protected abstract FlowableResult<T> Invoke(ISrcVisionNodeData<T> srcImageNodeData, IVisionNodeData<T> from, IFlowableDiagramData diagram);

    protected abstract void UpdateResultImageSource();

    //protected abstract ImageSource ToImageSource(T t);

    public override void Dispose()
    {
        base.Dispose();
        foreach (IVisionResultImage<T> item in this.ResultImages)
        {
            item.Image?.Dispose();
        }
    }

    protected virtual FlowableResult<T> OK(T mat, string message = "运行成功")
    {
        this.Message = message;
        return new FlowableResult<T>(mat, message) { State = FlowableResultState.OK };
    }

    protected virtual FlowableResult<T> OK(T mat, IResultPresenter resultPresenter, string message = "运行成功")
    {
        this.Message = message;
        this.ResultPresenter = resultPresenter;
        return new FlowableResult<T>(mat, message) { State = FlowableResultState.OK };
    }

    protected virtual FlowableResult<T> Error(T mat, string message = "运行错误")
    {
        this.Message = message;
        return new FlowableResult<T>(mat, message) { State = FlowableResultState.Error };
    }

    protected virtual FlowableResult<T> Break(T mat, string message = "不满足条件返回")
    {
        this.Message = message;
        return new FlowableResult<T>(mat, message) { State = FlowableResultState.Break };
    }
}

