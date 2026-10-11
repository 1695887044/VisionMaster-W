global using System.Diagnostics;
namespace G.VisionMaster.OpenCV.NodeDatas.Feature;
[Icon(FontIcons.GenericScan)]
[Display(Name = "AKAZE特征提取", GroupName = "特征提取", Order = 0)]
public class AKazeFeatureDetector : FeatureOpenCVNodeDataBase
{
    private AKAZEDescriptorType _descriptorType = AKAZEDescriptorType.MLDB;
    [Display(Name = "DescriptorType", GroupName = VisionPropertyGroupNames.RunParameters)]
    public AKAZEDescriptorType DescriptorType
    {
        get { return _descriptorType; }
        set
        {
            _descriptorType = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private int _descriptorSize = 0;
    [Display(Name = "DescriptorSize", GroupName = VisionPropertyGroupNames.RunParameters)]
    public int DescriptorSize
    {
        get { return _descriptorSize; }
        set
        {
            _descriptorSize = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private int _descriptorChannels = 3;
    [Display(Name = "DescriptorChannels", GroupName = VisionPropertyGroupNames.RunParameters)]
    public int DescriptorChannels
    {
        get { return _descriptorChannels; }
        set
        {
            _descriptorChannels = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private float _threshold = 0.001f;
    [Display(Name = "Threshold", GroupName = VisionPropertyGroupNames.RunParameters)]
    public float Threshold
    {
        get { return _threshold; }
        set
        {
            _threshold = value;
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

    private int _nOctaveLayers = 4;
    [Display(Name = "nOctaveLayers", GroupName = VisionPropertyGroupNames.RunParameters)]
    public int nOctaveLayers
    {
        get { return _nOctaveLayers; }
        set
        {
            _nOctaveLayers = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private KAZEDiffusivityType _diffusivity = KAZEDiffusivityType.DiffPmG2;
    [Display(Name = "Diffusivity", GroupName = VisionPropertyGroupNames.RunParameters)]
    public KAZEDiffusivityType Diffusivity
    {
        get { return _diffusivity; }
        set
        {
            _diffusivity = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
    {
        Mat gray = from.Mat;
        using AKAZE akaze = AKAZE.Create(this.DescriptorType, this.DescriptorSize, this.DescriptorChannels, this.Threshold, this.nOctaves, this.nOctaveLayers, this.Diffusivity);
        //var kazeDescriptors = new Mat();
        using Mat akazeDescriptors = new Mat();
        KeyPoint[] akazeKeyPoints = null;
        TimeSpan akazeTime = MeasureTime(() =>
            akaze.DetectAndCompute(gray, null, out akazeKeyPoints, akazeDescriptors));
        Mat dstAkaze = new Mat();
        Cv2.DrawKeypoints(gray, akazeKeyPoints, dstAkaze);
        this.FeatureCountResult = akazeKeyPoints.Length;
        return this.OK(dstAkaze, akazeKeyPoints.ToResultPresenter(), this.FeatureCountResult.ToDetectSuccessMessage());
    }

    private TimeSpan MeasureTime(Action action)
    {
        Stopwatch watch = Stopwatch.StartNew();
        action();
        watch.Stop();
        return watch.Elapsed;
    }
}
