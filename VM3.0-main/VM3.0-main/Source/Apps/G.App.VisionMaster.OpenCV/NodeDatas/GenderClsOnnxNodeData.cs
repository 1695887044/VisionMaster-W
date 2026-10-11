namespace G.App.VisionMaster.OpenCV.NodeDatas;

[Display(Name = "性别分类", GroupName = "图像分类", Description = "识别图像中的主要物体类别（单标签或多标签）", Order = 10)]
public class GenderClsOnnxNodeData : ClsOnnxNodeDataBase
{
    public override void LoadDefault()
    {
        base.LoadDefault();
        this.ModelPath = "gender_efficientnet_b2.onnx".ToOnnxPath();
        this.LabelPath = "Female Male";
    }
}
