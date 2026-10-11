using G.Services.AppPath;
using G.VisionMaster.OpenCV.NodeDatas.Src;
using System.Threading.Tasks;

namespace G.VisionMaster.OpenCV.Base;

[Icon(FontIcons.Camera)]
public abstract class VideoCaptureNodeDataBase : OpenCVSrcFilesNodeDataBase, IVideoCaptureNodeData
{
    private int _sleepMilliseconds = 0;
    [DefaultValue(0)]
    [Display(Name = "间隔时间", GroupName = VisionPropertyGroupNames.RunParameters)]
    public int SleepMilliseconds
    {
        get { return _sleepMilliseconds; }
        set
        {
            _sleepMilliseconds = value;
            RaisePropertyChanged();
        }
    }

    protected virtual async Task<bool?> InvokeFrameMatAsync(IFlowablePartData previors, IFlowableDiagramData diagram, Mat frameMat)
    {
        IFlowableDiagramData invokeable = diagram;
        Action<IPartData> invoking = x =>
        {
            //OpenCVNodeDataBase data = x.GetContent<OpenCVNodeDataBase>();
            //data.UseInfoLogger = false;
            //data.UseReview = false;
            //data.UseAnimation = false;
            //diagram.Dispatcher.Invoke(() =>
            //{
            //    invokeable?.OnInvokingPart(x);
            //});
        };

        Action<IPartData> invoked = x =>
        {
            if (this.State == FlowableState.Canceling)
                return;
            invokeable?.OnInvokedPart(x);
            //Thread.Sleep(1000);
        };
        invoking.Invoke(this);
        if (this.Mat != frameMat)
            this.Mat?.Dispose();
        this.Mat = frameMat;
        //this.SrcMat = this.Mat;
        UpdateResultImageSource();
        invoked.Invoke(this);
        IEnumerable<IFlowableNodeData> tos = this.GetToNodeDatas(diagram).OfType<IFlowableNodeData>();
        tos.GotoState(diagram, x => FlowableState.Wait);
        foreach (var to in tos)
        {
            IFlowableLinkData linkData = this.GetToLinkDatas(this.DiagramData).OfType<IFlowableLinkData>().Where(x => x.ToNodeID == to.ID)?.FirstOrDefault();
            bool? r = await to.Start(diagram, linkData);
            if (r == false)
                return false;
        }
        await Task.Delay(this.SleepMilliseconds);
        return true;
    }

    public async Task<IFlowableResult> InvokeVideoFlowable(IFlowableDiagramData diagram, Func<Task<IFlowableResult>> action)
    {
        IEnumerable<IVideoFlowable> videos = diagram.NodeDatas.OfType<IVideoFlowable>();
        foreach (IVideoFlowable video in videos)
        {
            video.Begin();
        }
        Task<IFlowableResult> r = action.Invoke();
        foreach (IVideoFlowable video in videos)
        {
            video.End();
        }
        return await r;
    }

    protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
    {
        return this.OK(null);
    }
}

