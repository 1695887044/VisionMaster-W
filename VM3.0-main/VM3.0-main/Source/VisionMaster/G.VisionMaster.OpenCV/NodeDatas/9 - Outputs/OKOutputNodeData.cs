using G.VisionMaster.NodeGroup.Groups.Outputs;

namespace G.VisionMaster.OpenCV.NodeDatas.Other;

[Icon(FontIcons.Ethernet)]
[Display(Name = "OK", Description = "输出流程处理OK结果", Order = 10400)]
public class OKOutputNodeData : OpenCVNodeDataBase, IOutputGroupableNodeData
{
    protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
    {
        return this.OK(from.Mat, "OK");
    }
}

