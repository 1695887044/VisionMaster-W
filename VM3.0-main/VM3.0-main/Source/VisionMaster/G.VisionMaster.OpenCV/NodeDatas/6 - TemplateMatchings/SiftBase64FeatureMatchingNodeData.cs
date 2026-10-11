using G.VisionMaster.NodeGroup.Groups.TemplateMatchings;
using OpenCvSharp.Features2D;

namespace G.VisionMaster.OpenCV.NodeDatas.Feature;
[Display(Name = "SIFTT特征匹配", GroupName = "特征匹配", Description = "SIFT（尺度不变特征变换）是计算机视觉领域最经典的特征提取算法之一，具有尺度、旋转、光照不变性，对视角变化和噪声也有较好的鲁棒性", Order = 0)]
public class SiftBase64FeatureMatchingNodeData : OpenCVBase64MatchingNodeDataBase, ITemplateMatchingGroupableNodeData
{

    private int _featureCountResult;
    [ReadOnly(true)]
    [Display(Name = "特征点数量", GroupName = VisionPropertyGroupNames.ResultParameters, Description = "结果参数，此结果可应用再条件分支等作为判断参数")]
    public int FeatureCountResult
    {
        get { return _featureCountResult; }
        set
        {
            _featureCountResult = value;
            RaisePropertyChanged();
        }
    }

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

    private int _nFeatures = 0;
    [Display(Name = "nFeatures", GroupName = VisionPropertyGroupNames.RunParameters)]
    public int nFeatures
    {
        get { return _nFeatures; }
        set
        {
            _nFeatures = value;
            RaisePropertyChanged();
        }
    }

    private int _nOctaveLayers = 3;
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

    private double _contrastThreshold = 0.04;
    [Display(Name = "ContrastThreshold", GroupName = VisionPropertyGroupNames.RunParameters)]
    public double ContrastThreshold
    {
        get { return _contrastThreshold; }
        set
        {
            _contrastThreshold = value;
            RaisePropertyChanged();
        }
    }

    private double _edgeThreshold = 10;
    [Display(Name = "EdgeThreshold", GroupName = VisionPropertyGroupNames.RunParameters)]
    public double EdgeThreshold
    {
        get { return _edgeThreshold; }
        set
        {
            _edgeThreshold = value;
            RaisePropertyChanged();
        }
    }

    private double _sigma = 1.6;
    [Display(Name = "Sigma", GroupName = VisionPropertyGroupNames.RunParameters)]
    public double Sigma
    {
        get { return _sigma; }
        set
        {
            _sigma = value;
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

    private double _minArea = 100.0;
    [DefaultValue(100.0)]
    [Display(Name = "最小面积", GroupName = VisionPropertyGroupNames.RunParameters)]
    public double MinArea
    {
        get { return _minArea; }
        set
        {
            _minArea = value;
            RaisePropertyChanged();
        }
    }

    private double _maxArea = double.MaxValue;
    [DefaultValue(double.MaxValue)]
    [Display(Name = "最大面积", GroupName = VisionPropertyGroupNames.RunParameters)]
    public double MaxArea
    {
        get { return _maxArea; }
        set
        {
            _maxArea = value;
            RaisePropertyChanged();
        }
    }

    protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
    {
        if (string.IsNullOrEmpty(this.Base64String))
            return this.OK(from.Mat, "运行完成，未绘制模板图片");
        byte[] bytes = Convert.FromBase64String(this.Base64String);
        Mat src = from.Mat;
        Mat src1 = Cv2.ImDecode(bytes, ImreadModes.Color);
        Mat src2 = from.Mat;
        Mat gray1 = new Mat();
        Mat gray2 = new Mat();
        Cv2.CvtColor(src1, gray1, ColorConversionCodes.BGR2GRAY);
        Cv2.CvtColor(src2, gray2, ColorConversionCodes.BGR2GRAY);

        SIFT sift = SIFT.Create(this.nFeatures, this.nOctaveLayers, this.ContrastThreshold, this.EdgeThreshold, this.Sigma);

        // Detect the keypoints and generate their descriptors using SIFT
        Mat<float> descriptors1 = new Mat<float>();
        Mat<float> descriptors2 = new Mat<float>();
        sift.DetectAndCompute(gray1, null, out KeyPoint[] keypoints1, descriptors1);
        sift.DetectAndCompute(gray2, null, out KeyPoint[] keypoints2, descriptors2);

        //keypoints1 = keypoints1.Where(kp => kp.Response > 0.01f).ToArray();
        //Mat filteredDescriptors = new Mat();
        //for (int i = 0; i < keypoints1.Length; i++)
        //{
        //    if (keypoints1[i].Response > 0.01f) // 与过滤条件一致
        //    {
        //        filteredDescriptors.PushBack(descriptors1.Row(i));
        //    }
        //}
        // Match descriptor vectors
        if (this.MatcherType == MatcherType.BFMatcher)
        {
            BFMatcher bfMatcher = new BFMatcher(this.NormType, this.CrossCheck);
            DMatch[] bfMatches = bfMatcher.Match(descriptors1, descriptors2);
            // Draw matches
            Mat bfView = new Mat();
            Cv2.DrawMatches(gray1, keypoints1, gray2, keypoints2, bfMatches, bfView, null, null, null, DrawMatchesFlags.DrawRichKeypoints);
            this.FeatureCountResult = keypoints1.Length;
            Controls.Diagram.Presenter.NodeDatas.Base.IResultPresenter resultPresenter = keypoints2.ToResultPresenter();
            return this.OK(bfView, resultPresenter, this.FeatureCountResult.ToDetectSuccessMessage());
        }
        if (this.MatcherType == MatcherType.FlannBasedMatcher)
        {
            FlannBasedMatcher flannMatcher = new FlannBasedMatcher();
            DMatch[] flannMatches = flannMatcher.Match(descriptors1, descriptors2);
            // Draw matches
            Mat flannView = new Mat();
            Cv2.DrawMatches(gray1, keypoints1, gray2, keypoints2, flannMatches, flannView);
            this.FeatureCountResult = keypoints1.Length;
            Controls.Diagram.Presenter.NodeDatas.Base.IResultPresenter resultPresenter = keypoints2.ToResultPresenter();
            return this.OK(flannView, resultPresenter, this.FeatureCountResult.ToDetectSuccessMessage());
        }
        return this.Error(from.Mat);
    }
}