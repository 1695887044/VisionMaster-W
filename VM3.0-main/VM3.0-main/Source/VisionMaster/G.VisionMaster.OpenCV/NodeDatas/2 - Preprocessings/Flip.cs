namespace G.VisionMaster.OpenCV.NodeDatas.Basic;
[Icon(FontIcons.Color)]
[Display(Name = "反转图片", GroupName = "基础函数", Description = "会改变整个图像的像素数量和分辨率", Order = 3)]
public class Flip : OpenCVNodeDataBase, IPreprocessingGroupableNodeData
{
    private FlipMode _flipMode;
    [Display(Name = "旋转方式", GroupName = VisionPropertyGroupNames.RunParameters)]
    public FlipMode FlipMode
    {
        get { return _flipMode; }
        set
        {
            _flipMode = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
    {
        Mat result = new Mat();
        Cv2.Flip(from.Mat, result, this.FlipMode);
        return this.OK(result);
    }
}
