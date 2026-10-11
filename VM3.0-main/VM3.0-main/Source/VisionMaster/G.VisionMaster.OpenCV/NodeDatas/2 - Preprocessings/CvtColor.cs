namespace G.VisionMaster.OpenCV.NodeDatas.Basic;
[Icon(FontIcons.Color)]
[Display(Name = "色彩变换", GroupName = "基础函数", Description = "设置图片颜色", Order = 2)]
public class CvtColor : OpenCVNodeDataBase, IPreprocessingGroupableNodeData
{
    private ColorConversionCodes _colorConversionCode = ColorConversionCodes.BGR2GRAY;
    [DefaultValue(ColorConversionCodes.BGR2GRAY)]
    [Display(Name = "转换规则", GroupName = VisionPropertyGroupNames.RunParameters)]
    public ColorConversionCodes ColorConversionCode
    {
        get { return _colorConversionCode; }
        set
        {
            _colorConversionCode = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private int _dstCn = 0;
    [DefaultValue(0)]
    [Display(Name = "通道数", GroupName = VisionPropertyGroupNames.RunParameters)]
    public int DstCn
    {
        get { return _dstCn; }
        set
        {
            _dstCn = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
    {
        Mat mat = from.Mat.CvtColor(this.ColorConversionCode, this.DstCn);
        return this.OK(mat);
    }
}
