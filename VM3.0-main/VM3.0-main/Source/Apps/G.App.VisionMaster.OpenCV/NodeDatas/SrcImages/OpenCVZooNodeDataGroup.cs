using G.Controls.Diagram.Datas;
using G.NodeDatas.Zoo;

namespace G.App.VisionMaster.OpenCV.NodeDatas.SrcImages;

public class OpenCVZooNodeDataGroup : ZooNodeDataGroup
{
    protected override IEnumerable<INodeData> CreateNodeDatas()
    {
        return this.GetType().Assembly.GetInstances<IZooSrcImageFilesNodeData>().OrderBy(x => x.Order);
    }
}
