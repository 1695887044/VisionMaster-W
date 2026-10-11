namespace G.VisionMaster.OpenCV.NodeDataGroups;

[Icon(FontIcons.GenericScan)]
[Display(Name = "特征识别模块", Description = "特征提取是计算机视觉和图像处理中的核心步骤，它的主要作用是将原始数据（如图像）转换为更能代表问题本质的特征表示，从而显著提高后续处理的效果和效率。", Order = 10800)]
public class FeatureDetectorDataGroup : NodeDataGroupBase, IImageDataGroup
{
    protected override IEnumerable<INodeData> CreateNodeDatas()
    {
        return typeof(IFeatureDetectorOpenCVNodeData).Assembly.GetInstances<IFeatureDetectorOpenCVNodeData>().OrderBy(x => x.Order);
    }
}
