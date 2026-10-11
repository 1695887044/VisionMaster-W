using G.VisionMaster.NodeGroup.Groups.Morphologys;

namespace G.VisionMaster.OpenCV.NodeDatas.Morphology;
[Icon(FontIcons.HomeGroup)]
[Display(Name = "梯度", GroupName = "形态学", Description = " 原图 - 腐蚀  ，求图形的边缘", Order = 50)]
public class Gradient : MorphologyOpenCVNodeDataBase, IMorphologyGroupableNodeData
{
    protected override MorphTypes GetMorphType()
    {
        return MorphTypes.Gradient;
    }
}
