using G.VisionMaster.NodeGroup.Groups.TemplateMatchings;
using OpenCvSharp.XFeatures2D;

namespace G.VisionMaster.OpenCV.NodeDatas.Feature;

[Display(Name = "SURF特征匹配", GroupName = "特征匹配", Description = "局部特征 关注关键点周围区域 SIFT, SURF, ORB 图像匹配、物体识别", Order = 0)]
public class SurfBase64FeatureMatchingNodeData : OpenCVBase64MatchingNodeDataBase, ITemplateMatchingGroupableNodeData
{

    private MatcherType _matcherType;
    [Display(Name = "MatcherType", GroupName = VisionPropertyGroupNames.RunParameters)]
    public MatcherType MatcherType
    {
        get { return _matcherType; }
        set
        {
            _matcherType = value;
            RaisePropertyChanged();
        }
    }

    private NormTypes _normTypes = NormTypes.L2;
    [Display(Name = "NormType", GroupName = VisionPropertyGroupNames.RunParameters)]
    public NormTypes NormType
    {
        get { return _normTypes; }
        set
        {
            _normTypes = value;
            RaisePropertyChanged();
        }
    }

    private bool _crossCheck = false;
    [Display(Name = "CrossCheck", GroupName = VisionPropertyGroupNames.RunParameters)]
    public bool CrossCheck
    {
        get { return _crossCheck; }
        set
        {
            _crossCheck = value;
            RaisePropertyChanged();
        }
    }

    private double _hessianThreshold = 200;
    [Display(Name = "HessianThreshold", GroupName = VisionPropertyGroupNames.RunParameters)]
    public double HessianThreshold
    {
        get { return _hessianThreshold; }
        set
        {
            _hessianThreshold = value;
            RaisePropertyChanged();
        }
    }

    private int _nOctaves = 4;
    [Display(Name = "nOctaves", GroupName = VisionPropertyGroupNames.RunParameters)]
    public int nOctaves
    {
        get { return _nOctaves; }
        set
        {
            _nOctaves = value;
            RaisePropertyChanged();
        }
    }

    private int _nOctaveLayers = 2;
    [Display(Name = "nOctaveLayers", GroupName = VisionPropertyGroupNames.RunParameters)]
    public int nOctaveLayers
    {
        get { return _nOctaveLayers; }
        set
        {
            _nOctaveLayers = value;
            RaisePropertyChanged();
        }
    }

    private bool _extended;
    [Display(Name = "Extended", GroupName = VisionPropertyGroupNames.RunParameters)]
    public bool Extended
    {
        get { return _extended; }
        set
        {
            _extended = value;
            RaisePropertyChanged();
        }
    }

    private bool _upright = true;
    [Display(Name = "Upright", GroupName = VisionPropertyGroupNames.RunParameters)]
    public bool Upright
    {
        get { return _upright; }
        set
        {
            _upright = value;
            RaisePropertyChanged();
        }
    }

    protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
    {
        if (string.IsNullOrEmpty(this.Base64String))
            return this.OK(from.Mat, "运行完成，未绘制模板图片");
        byte[] bytes = Convert.FromBase64String(this.Base64String);
        Mat src1 = Cv2.ImDecode(bytes, ImreadModes.Color);
        Mat src2 = from.Mat;
        Mat gray1 = new Mat();
        Mat gray2 = new Mat();
        Cv2.CvtColor(src1, gray1, ColorConversionCodes.BGR2GRAY);
        Cv2.CvtColor(src2, gray2, ColorConversionCodes.BGR2GRAY);

        SURF surf = SURF.Create(this.HessianThreshold, this.nOctaves, this.nOctaveLayers, this.Upright);

        // Detect the keypoints and generate their descriptors using SIFT
        Mat<float> descriptors1 = new Mat<float>();
        Mat<float> descriptors2 = new Mat<float>();
        surf.DetectAndCompute(gray1, null, out KeyPoint[] keypoints1, descriptors1);
        surf.DetectAndCompute(gray2, null, out KeyPoint[] keypoints2, descriptors2);

        // Match descriptor vectors
        if (this.MatcherType == MatcherType.BFMatcher)
        {
            BFMatcher bfMatcher = new BFMatcher(this.NormType, this.CrossCheck);
            DMatch[] bfMatches = bfMatcher.Match(descriptors1, descriptors2);
            // Draw matches
            Mat bfView = new Mat();
            Cv2.DrawMatches(gray1, keypoints1, gray2, keypoints2, bfMatches, bfView);
            this.MatchingCountResult = keypoints1.Length;
            Controls.Diagram.Presenter.NodeDatas.Base.IResultPresenter resultPresenter = keypoints2.ToResultPresenter();
            return this.OK(bfView, resultPresenter, this.MatchingCountResult.ToDetectSuccessMessage());
        }
        if (this.MatcherType == MatcherType.FlannBasedMatcher)
        {
            FlannBasedMatcher flannMatcher = new FlannBasedMatcher();
            DMatch[] flannMatches = flannMatcher.Match(descriptors1, descriptors2);
            // Draw matches
            Mat flannView = new Mat();
            Cv2.DrawMatches(gray1, keypoints1, gray2, keypoints2, flannMatches, flannView);
            this.MatchingCountResult = keypoints1.Length;
            Controls.Diagram.Presenter.NodeDatas.Base.IResultPresenter resultPresenter = keypoints2.ToResultPresenter();
            return this.OK(flannView, resultPresenter, this.MatchingCountResult.ToDetectSuccessMessage());
        }

        return this.Error(null);
    }
}
