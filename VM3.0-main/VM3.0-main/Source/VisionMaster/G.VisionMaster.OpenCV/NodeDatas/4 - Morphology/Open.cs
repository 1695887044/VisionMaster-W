using G.VisionMaster.NodeGroup.Groups.Morphologys;

namespace G.VisionMaster.OpenCV.NodeDatas.Morphology;
[Icon(FontIcons.HomeGroup)]
[Display(Name = "开运算", GroupName = "形态学", Description = "腐蚀 + 膨胀，先腐蚀后膨胀，用于去除小物体或噪声", Order = 21)]
public class Open : MorphologyOpenCVNodeDataBase, IMorphologyGroupableNodeData
{
    protected override MorphTypes GetMorphType()
    {
        return MorphTypes.Open;
    }
}
