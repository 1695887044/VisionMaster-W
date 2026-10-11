using OpenCvSharp.XFeatures2D;

namespace G.VisionMaster.OpenCV.NodeDatas.Feature;
[Icon(FontIcons.GenericScan)]
[Display(Name = "FREAK特征提取", GroupName = "特征提取", Order = 0)]
public class FreakFeatureDetector : FeatureOpenCVNodeDataBase
{
    private int _nFeatures = 1000;
    [Display(Name = "nFeatures", GroupName = VisionPropertyGroupNames.RunParameters)]
    public int nFeatures
    {
        get { return _nFeatures; }
        set
        {
            _nFeatures = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private float _scaleFactor = 1.2f;
    [Display(Name = "ScaleFactor", GroupName = VisionPropertyGroupNames.RunParameters)]
    public float ScaleFactor
    {
        get { return _scaleFactor; }
        set
        {
            _scaleFactor = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private int _nLevels = 8;
    [Display(Name = "nLevels", GroupName = VisionPropertyGroupNames.RunParameters)]
    public int nLevels
    {
        get { return _nLevels; }
        set
        {
            _nLevels = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }
    private int _edgeThreshold = 31;
    [Display(Name = "EdgeThreshold", GroupName = VisionPropertyGroupNames.RunParameters)]
    public int EdgeThreshold
    {
        get { return _edgeThreshold; }
        set
        {
            _edgeThreshold = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private int _firstLevel = 0;
    [Display(Name = "FirstLevel", GroupName = VisionPropertyGroupNames.RunParameters)]
    public int FirstLevel
    {
        get { return _firstLevel; }
        set
        {
            _firstLevel = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private int _wtaK = 2;
    [Display(Name = "WtaK", GroupName = VisionPropertyGroupNames.RunParameters)]
    public int WtaK
    {
        get { return _wtaK; }
        set
        {
            _wtaK = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private ORBScoreType _scoreType = ORBScoreType.Harris;
    [Display(Name = "ScoreType", GroupName = VisionPropertyGroupNames.RunParameters)]
    public ORBScoreType ScoreType
    {
        get { return _scoreType; }
        set
        {
            _scoreType = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private int _patchSize = 31;
    [Display(Name = "PatchSize", GroupName = VisionPropertyGroupNames.RunParameters)]
    public int PatchSize
    {
        get { return _patchSize; }
        set
        {
            _patchSize = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private int _fastThreshold = 20;
    [Display(Name = "FastThreshold", GroupName = VisionPropertyGroupNames.RunParameters)]
    public int FastThreshold
    {
        get { return _fastThreshold; }
        set
        {
            _fastThreshold = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private bool _orientationNormalized = true;
    [Display(Name = "OrientationNormalized", GroupName = VisionPropertyGroupNames.RunParameters)]
    public bool OrientationNormalized
    {
        get { return _orientationNormalized; }
        set
        {
            _orientationNormalized = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private bool _scaleNormalized = true;
    [Display(Name = "ScaleNormalized", GroupName = VisionPropertyGroupNames.RunParameters)]
    public bool ScaleNormalized
    {
        get { return _scaleNormalized; }
        set
        {
            _scaleNormalized = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private float _patternScale = 22.0f;
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

    private int _nOctaves = 4;
    [Display(Name = "nOctaves", GroupName = VisionPropertyGroupNames.RunParameters)]
    public int nOctaves
    {
        get { return _nOctaves; }
        set
        {
            _nOctaves = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
    {
        using Mat gray = from.Mat.CvtColor(ColorConversionCodes.BGR2GRAY);
        Mat dst = from.Mat.Clone();
        // ORB
        using ORB orb = ORB.Create(this.nFeatures, this.ScaleFactor, this.nLevels, this.EdgeThreshold, this.FirstLevel, this.WtaK, this.ScoreType, this.PatchSize, this.FastThreshold);
        KeyPoint[] keypoints = orb.Detect(gray);

        // FREAK
        using FREAK freak = FREAK.Create(this.OrientationNormalized, this.ScaleNormalized, this.PatternScale, this.nOctaves);
        using Mat freakDescriptors = new Mat();
        freak.Compute(gray, ref keypoints, freakDescriptors);

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
            this.FeatureCountResult = keypoints.Length;
        }
        return this.OK(dst, keypoints.ToResultPresenter(), this.FeatureCountResult.ToDetectSuccessMessage());
    }
}
