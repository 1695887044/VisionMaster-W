namespace G.App.VisionMaster.OpenCV.NodeDatas;

[Icon(FontIcons.Family)]
[Display(Name = "人类语义分割", GroupName = "分类模型", Description = "对每个像素进行分类，输出类别掩码", Order = 0)]
public class HumanSemSegOnnxNodeData : SemSegOnnxNodeDataBase
{
    public override void LoadDefault()
    {
        base.LoadDefault();
        this.ModelPath = "human_segmentation_pphumanseg_2023mar.onnx".ToOnnxPath();
    }
}
