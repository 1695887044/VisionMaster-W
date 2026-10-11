using G.VisionMaster.ResultPresenter.ResultPresenters;

namespace G.VisionMaster.OpenCV.NodeDatas.Feature;
[Icon(FontIcons.GenericScan)]
[Display(Name = "MSER特征提取", GroupName = "特征提取", Order = 0)]
public class MserFeatureDetector : FeatureOpenCVNodeDataBase
{
    private int _delta = 5;
    [Display(Name = "Delta", GroupName = VisionPropertyGroupNames.RunParameters)]
    public int Delta
    {
        get { return _delta; }
        set
        {
            _delta = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private int _minArea = 60;
    [Display(Name = "MinArea", GroupName = VisionPropertyGroupNames.RunParameters)]
    public int MinArea
    {
        get { return _minArea; }
        set
        {
            _minArea = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private int _maxArea = 14400;
    [Display(Name = "MaxArea", GroupName = VisionPropertyGroupNames.RunParameters)]
    public int MaxArea
    {
        get { return _maxArea; }
        set
        {
            _maxArea = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private double _maxVariation = 0.25;
    [Display(Name = "MaxVariation", GroupName = VisionPropertyGroupNames.RunParameters)]
    public double MaxVariation
    {
        get { return _maxVariation; }
        set
        {
            _maxVariation = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private double _minDiversity = 0.2;
    [Display(Name = "MinDiversity", GroupName = VisionPropertyGroupNames.RunParameters)]
    public double MinDiversity
    {
        get { return _minDiversity; }
        set
        {
            _minDiversity = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private int _maxEvolution = 200;
    [Display(Name = "MaxEvolution", GroupName = VisionPropertyGroupNames.RunParameters)]
    public int MaxEvolution
    {
        get { return _maxEvolution; }
        set
        {
            _maxEvolution = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private double _areaThreshold = 1.01;
    [Display(Name = "AreaThreshold", GroupName = VisionPropertyGroupNames.RunParameters)]
    public double AreaThreshold
    {
        get { return _areaThreshold; }
        set
        {
            _areaThreshold = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private double _minMargin = 0.003;
    [Display(Name = "MinMargin", GroupName = VisionPropertyGroupNames.RunParameters)]
    public double MinMargin
    {
        get { return _minMargin; }
        set
        {
            _minMargin = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private int _edgeBlurSize = 5;
    [Display(Name = "EdgeBlurSize", GroupName = VisionPropertyGroupNames.RunParameters)]
    public int EdgeBlurSize
    {
        get { return _edgeBlurSize; }
        set
        {
            _edgeBlurSize = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
    {
        using Mat gray = from.Mat.Clone();
        //Mat dst = new Mat(srcImageNodeData.SrcFilePath, ImreadModes.Color);
        Mat dst = srcImageNodeData.Mat.Clone();
        MSER mser = MSER.Create(this.Delta, this.MinArea, this.MaxArea, this.MaxVariation, this.MinDiversity, this.MaxEvolution, this.AreaThreshold, this.MinMargin, this.EdgeBlurSize);
        mser.DetectRegions(gray, out Point[][] contours, out _);
        foreach (Point[] pts in contours)
        {
            Scalar color = Scalar.RandomColor();
            foreach (Point p in pts)
            {
                dst.Circle(p, 1, color);
            }
        }
        this.FeatureCountResult = contours.Length;
        return this.OK(dst, contours.Select(x => x.ToWindowRect()).ToRectangleDataGridResultPresenter(x => "位置信息"), this.FeatureCountResult.ToDetectSuccessMessage());
    }
}
