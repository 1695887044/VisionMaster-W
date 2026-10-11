namespace G.VisionMaster.OpenCV.NodeDatas.Basic;

[Icon(FontIcons.Color)]
[Display(Name = "重复图片", GroupName = "基础函数", Description = "会改变整个图像的像素数量和分辨率", Order = 3)]
public class Repeat : OpenCVNodeDataBase, IPreprocessingGroupableNodeData
{

    private int _ny = 2;
    [PropertyItem(typeof(Int32SliderTextPropertyItem))]
    [DefaultValue(2)]
    [Range(1, 10)]
    [Display(Name = "Y重复个数", GroupName = VisionPropertyGroupNames.RunParameters)]

    public int ny
    {
        get { return _ny; }
        set
        {
            _ny = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }


    private int _nx = 2;
    [PropertyItem(typeof(Int32SliderTextPropertyItem))]
    [DefaultValue(2)]
    [Range(1, 10)]
    [Display(Name = "X重复个数", GroupName = VisionPropertyGroupNames.RunParameters)]

    public int nx
    {
        get { return _nx; }
        set
        {
            _nx = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }


    protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
    {
        Mat result = new Mat();
        Cv2.Repeat(from.Mat, this.nx, this.ny, result);
        return this.OK(result);
    }
}
