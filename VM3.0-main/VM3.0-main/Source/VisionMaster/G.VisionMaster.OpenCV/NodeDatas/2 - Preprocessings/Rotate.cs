global using G.VisionMaster.NodeGroup.Groups.Preprocessings;

namespace G.VisionMaster.OpenCV.NodeDatas.Basic;
[Icon(FontIcons.Color)]
[Display(Name = "旋转图片", GroupName = "基础函数", Description = "会改变整个图像的像素数量和分辨率", Order = 3)]
public class Rotate : OpenCVNodeDataBase, IPreprocessingGroupableNodeData
{
    private RotateFlags _rotateFlags;
    [Display(Name = "旋转方式", GroupName = VisionPropertyGroupNames.RunParameters)]
    public RotateFlags RotateFlags
    {
        get { return _rotateFlags; }
        set
        {
            _rotateFlags = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
    {
        Mat result = new Mat();
        Cv2.Rotate(from.Mat, result, this.RotateFlags);
        return this.OK(result);
    }
}
