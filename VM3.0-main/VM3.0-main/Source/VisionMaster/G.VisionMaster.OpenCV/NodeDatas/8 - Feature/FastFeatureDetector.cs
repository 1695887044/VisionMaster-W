namespace G.VisionMaster.OpenCV.NodeDatas.Feature;
[Icon(FontIcons.GenericScan)]
[Display(Name = "FAST特征提取", GroupName = "特征提取", Order = 0)]
public class FastFeatureDetector : FeatureOpenCVNodeDataBase
{
    private bool _nonmaxSupression = true;
    [Display(Name = "NonmaxSupression", GroupName = VisionPropertyGroupNames.RunParameters)]
    public bool NonmaxSupression
    {
        get { return _nonmaxSupression; }
        set
        {
            _nonmaxSupression = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private int _threshold;
    [PropertyItem(typeof(Int32SliderTextPropertyItem))]
    [Range(0, 500)]
    [Display(Name = "Threshold", GroupName = VisionPropertyGroupNames.RunParameters)]
    public int Threshold
    {
        get { return _threshold; }
        set
        {
            _threshold = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    //public override IFlowableResult Invoke(Part previors, Node diagram)
    //{
    //    var imgSrc = GetFromMat(diagram);
    //    //using Mat imgSrc = new Mat(ImagePath.Lenna, ImreadModes.Color);
    //    Mat imgGray = new Mat();
    //    Mat imgDst = imgSrc.Clone();
    //    Cv2.CvtColor(imgSrc, imgGray, ColorConversionCodes.BGR2GRAY, 0);
    //    KeyPoint[] keypoints = Cv2.FAST(imgGray, 50, true);
    //    foreach (KeyPoint kp in keypoints)
    //    {
    //        imgDst.Circle((Point)kp.Pt, 3, Scalar.Red, -1, LineTypes.AntiAlias, 0);
    //    }
    //    UpdateMatToView(imgDst);
    //    return base.Invoke(previors, diagram);
    //}

    protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
    {
        Mat imgSrc = from.Mat;
        //using Mat imgSrc = new Mat(ImagePath.Lenna, ImreadModes.Color);
        using Mat imgGray = new Mat();
        Mat dst = imgSrc.Clone();
        Cv2.CvtColor(imgSrc, imgGray, ColorConversionCodes.BGR2GRAY, 0);
        KeyPoint[] keypoints = Cv2.FAST(imgGray, 50, true);
        foreach (KeyPoint kp in keypoints)
        {
            dst.Circle((Point)kp.Pt, 3, Scalar.RandomColor(), -1, LineTypes.AntiAlias, 0);
        }
        this.FeatureCountResult = keypoints.Length;
        return this.OK(dst, keypoints.ToResultPresenter(), this.FeatureCountResult.ToDetectSuccessMessage());

    }
}
