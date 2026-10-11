using G.VisionMaster.NodeData.Base.Conditions;
using G.VisionMaster.NodeGroup.Groups.Conditions;

namespace G.VisionMaster.OpenCV.NodeDatas.Image;
[Icon(FontIcons.Dial6)]
[Display(Name = "条件分支", GroupName = "判断条件", Description = "设置像素阈值，根据阈值执行不同路径逻辑", Order = 20)]
public class OpenCVConditionNodeData : ConditionNodeData<Mat>, IOnDiagramDeserialized, IConditionGroupableNodeData
{
    protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
    {
        return new FlowableResult<Mat>(from?.Mat) { State = FlowableResultState.OK };
    }

    protected override void UpdateResultImageSource()
    {
        this.UpdateResultImageSource(this.Mat);
    }

    protected void UpdateResultImageSource(Mat mat)
    {
        this.ResultImageSource = mat.ToImageSource();
    }
    protected override bool IsValid(Mat t)
    {
        return t.IsValid();
    }
}

