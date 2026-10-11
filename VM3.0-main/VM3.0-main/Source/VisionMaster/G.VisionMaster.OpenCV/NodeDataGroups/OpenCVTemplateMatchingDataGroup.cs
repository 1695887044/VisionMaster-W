using G.VisionMaster.NodeGroup.Groups.TemplateMatchings;

namespace G.VisionMaster.OpenCV.NodeDataGroups;

public class OpenCVTemplateMatchingDataGroup : TemplateMatchingDataGroup
{
    protected override IEnumerable<INodeData> CreateNodeDatas()
    {
        return this.GetType().Assembly.GetInstances<ITemplateMatchingGroupableNodeData>().OrderBy(x => x.Order);
    }
}
