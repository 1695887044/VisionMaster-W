namespace G.VisionMaster.OpenCV.NodeDatas.Detector;

[Icon(FontIcons.LargeErase)]
[Display(Name = "识别连通区域", GroupName = "基础检测", Order = 3)]
public class RenderBlobs : OpenCVDetectorNodeDataBase, IDetectorGroupableNodeData
{
    private PixelConnectivity _PixelConnectivity = PixelConnectivity.Connectivity4;
    [DefaultValue(PixelConnectivity.Connectivity4)]
    [Display(Name = "像素连通性", GroupName = VisionPropertyGroupNames.RunParameters, Description = "像素连通性是指在图像处理操作中如何定义像素之间的连接关系")]
    public PixelConnectivity PixelConnectivity
    {
        get { return _PixelConnectivity; }
        set
        {
            _PixelConnectivity = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private ConnectedComponentsAlgorithmsTypes _ConnectedComponentsAlgorithmsTypes = ConnectedComponentsAlgorithmsTypes.Default;
    [DefaultValue(ConnectedComponentsAlgorithmsTypes.Default)]
    [Display(Name = "连通算法类型", GroupName = VisionPropertyGroupNames.RunParameters)]
    public ConnectedComponentsAlgorithmsTypes ConnectedComponentsAlgorithmsTypes
    {
        get { return _ConnectedComponentsAlgorithmsTypes; }
        set
        {
            _ConnectedComponentsAlgorithmsTypes = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private double _minArea = 100.0;
    [PropertyItem(typeof(DoubleSliderTextPropertyItem))]
    [Range(0.0, 1000.0)]
    [DefaultValue(100.0)]
    [Display(Name = "最小面积", GroupName = VisionPropertyGroupNames.RunParameters)]
    public double MinArea
    {
        get { return _minArea; }
        set
        {
            _minArea = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private double _maxArea = 10000000.0;
    [PropertyItem(typeof(DoubleSliderTextPropertyItem))]
    [Range(0.0, 10000000.0)]
    [DefaultValue(10000000.0)]
    [Display(Name = "最大面积", GroupName = VisionPropertyGroupNames.RunParameters)]
    public double MaxArea
    {
        get { return _maxArea; }
        set
        {
            _maxArea = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private bool _useRenderBlobs = true;
    [DefaultValue(true)]
    [Display(Name = "渲染联通区域", GroupName = VisionPropertyGroupNames.DisplayParameters)]
    public bool UseRenderBlobs
    {
        get { return _useRenderBlobs; }
        set
        {
            _useRenderBlobs = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
    {
        return this.InvokeMat(srcImageNodeData, from.Mat, from.Mat, diagram);
    }

    protected FlowableResult<Mat> InvokeMat(ISrcVisionNodeData<Mat> srcImageNodeData, Mat fromMat, Mat mask, IFlowableDiagramData diagram)
    {
        Mat preMat = mask;
        ConnectedComponents cc = Cv2.ConnectedComponentsEx(preMat, this.PixelConnectivity, this.ConnectedComponentsAlgorithmsTypes);
        if (cc.LabelCount <= 1)
        {
            this.MatchingCountResult = 0;
            return this.OK(fromMat, "没有识别出联通区域");
        }
        using Mat labelview = preMat.EmptyClone();
        Mat pMat = this.GetPrviewMat(srcImageNodeData, fromMat, labelview);
        if (this.UseRenderBlobs)
            cc.RenderBlobs(pMat);
        List<ConnectedComponents.Blob> finds = cc.Blobs.Skip(1).Where(x => x.Area > this.MinArea && x.Area < this.MaxArea).ToList();
        if (finds.Count == 0)
        {
            this.MatchingCountResult = 0;
            return this.OK(fromMat, "没有识别出联通区域");
        }
        //Mat result = new Mat();
        //Mat maskBlob = new Mat();
        //cc.FilterByBlobs(pMat, maskBlob, finds);
        //Cv2.AddWeighted(pMat, 0.1, maskBlob, 0.9, 15, result);
        foreach (ConnectedComponents.Blob blob in finds)
        {
            pMat.Rectangle(blob.Rect, VisionSettings.Instance.OutputColor.ToScalar(), pMat.ToThickness());
        }
        this.MatchingCountResult = finds.Count;
        Controls.Diagram.Presenter.NodeDatas.Base.IResultPresenter resultPresenter = finds.ToResultPresenter();
        return this.OK(pMat, resultPresenter, this.MatchingCountResult.ToDetectSuccessMessage());
    }

}