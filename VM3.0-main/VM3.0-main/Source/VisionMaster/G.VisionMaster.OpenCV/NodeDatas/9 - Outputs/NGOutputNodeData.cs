using G.VisionMaster.NodeGroup.Groups.Outputs;

namespace G.VisionMaster.OpenCV.NodeDatas.Other;

[Icon(FontIcons.EthernetError)]
[Display(Name = "NG", Description = "输出流程处理NG结果", Order = 10400)]
public class NGOutputNodeData : OpenCVNodeDataBase, IOutputGroupableNodeData
{
    protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
    {
        return this.Error(from.Mat, "NG");
    }
}

