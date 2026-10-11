using G.VisionMaster.NodeGroup.Groups.Morphologys;

namespace G.VisionMaster.OpenCV.NodeDatas.Morphology;
[Icon(FontIcons.HomeGroup)]
[Display(Name = "顶帽", GroupName = "形态学", Description = " 原图 - 开运算，原图像与开运算结果的差，用于提取比背景更亮的区域", Order = 40)]
public class TopHat : MorphologyOpenCVNodeDataBase, IMorphologyGroupableNodeData
{
    protected override MorphTypes GetMorphType()
    {
        return MorphTypes.TopHat;
    }
}
