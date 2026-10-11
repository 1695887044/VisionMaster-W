global using G.VisionMaster.NodeData.Base;

namespace G.VisionMaster.OpenCV.NodeDatas.Feature;
[Icon(FontIcons.GenericScan)]
[Display(Name = "BRISK特征提取", GroupName = "特征提取", Order = 0)]
public class BriskFeatureDetector : FeatureOpenCVNodeDataBase
{
    private bool _useRectangle = true;
    [Display(Name = "绘制矩形", GroupName = VisionPropertyGroupNames.RunParameters)]
    public bool UseRectangle
    {
        get { return _useRectangle; }
        set
        {
            _useRectangle = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private int _threshold = 30;
    [DefaultValue(30)]
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

    private int _octaves = 3;
    [PropertyItem(typeof(Int32SliderTextPropertyItem))]
    [Range(0, 8)]
    [Display(Name = "Octaves", GroupName = VisionPropertyGroupNames.RunParameters)]
    public int Octaves
    {
        get { return _octaves; }
        set
        {
            _octaves = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private float _patternScale = 1.0f;
    [Display(Name = "PatternScale", GroupName = VisionPropertyGroupNames.RunParameters)]
    public float PatternScale
    {
        get { return _patternScale; }
        set
        {
            _patternScale = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
    {
        using Mat gray = srcImageNodeData.Mat.CvtColor(ColorConversionCodes.BGR2GRAY);
        Mat dst = srcImageNodeData.Mat.Clone();
        using BRISK brisk = BRISK.Create(Threshold, Octaves);
        KeyPoint[] keypoints = brisk.Detect(gray);
        if (keypoints != null)
        {
            foreach (KeyPoint kpt in keypoints)
            {
                Scalar color = Scalar.RandomColor();
                float r = kpt.Size / 2;
                Cv2.Circle(dst, (Point)kpt.Pt, (int)r, color);
                Cv2.Line(dst,
                    (Point)new Point2f(kpt.Pt.X + r, kpt.Pt.Y + r),
                    (Point)new Point2f(kpt.Pt.X - r, kpt.Pt.Y - r),
                    color);
                Cv2.Line(dst,
                    (Point)new Point2f(kpt.Pt.X - r, kpt.Pt.Y + r),
                    (Point)new Point2f(kpt.Pt.X + r, kpt.Pt.Y - r),
                    color);
            }
        }
        this.FeatureCountResult = keypoints.Length;
        return this.OK(dst, keypoints.ToResultPresenter(), this.FeatureCountResult.ToDetectSuccessMessage());

    }
}
