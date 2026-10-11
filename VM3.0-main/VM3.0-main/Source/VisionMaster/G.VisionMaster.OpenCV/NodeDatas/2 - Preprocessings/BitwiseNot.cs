global using G.Controls.Diagram.Presenter.DiagramDatas.Base;

namespace G.VisionMaster.OpenCV.NodeDatas.Basic;
[Icon(FontIcons.Color)]
[Display(Name = "反转黑白", GroupName = "基础函数", Description = "二指图片的效果反转既黑色变白色，白色变黑色", Order = 20)]
public class BitwiseNot : OpenCVNodeDataBase, IPreprocessingGroupableNodeData
{
    protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
    {
        if (from.Mat == null)
            return this.Error(null, "数据源为空");
        Mat src = from.Mat;
        Cv2.BitwiseNot(src, src);
        return this.OK(src);
    }
}

