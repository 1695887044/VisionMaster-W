namespace G.VisionMaster.OpenCV.NodeDatas.Basic;
[Icon(FontIcons.Color)]
[Display(Name = "二值化", GroupName = "基础函数", Description = "降噪成黑白色", Order = 3)]
public class Threshold : OpenCVNodeDataBase, IPreprocessingGroupableNodeData
{
    private double _thresh = 125.0;
    [PropertyItem(typeof(DoubleSliderTextPropertyItem))]
    [DefaultValue(125.0)]
    [Range(0.0, 255.0)]
    [Display(Name = "阈值", GroupName = VisionPropertyGroupNames.RunParameters)]
    public double Thresh
    {
        get { return _thresh; }
        set
        {
            _thresh = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private double _maxval = 255;
    [PropertyItem(typeof(DoubleSliderTextPropertyItem))]
    [Range(0.0, 255.0)]
    [Display(Name = "最大阈值", GroupName = VisionPropertyGroupNames.RunParameters)]
    public double Maxval
    {
        get { return _maxval; }
        set
        {
            _maxval = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private ThresholdTypes _thresholdTypes = ThresholdTypes.Binary;
    [Display(Name = "阈值类型", GroupName = VisionPropertyGroupNames.RunParameters)]
    public ThresholdTypes ThresholdType
    {
        get { return _thresholdTypes; }
        set
        {
            _thresholdTypes = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }
    protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
    {
        Mat mat = from.Mat.Threshold(this.Thresh, this.Maxval, this.ThresholdType);
        return this.OK(mat);
    }
}
