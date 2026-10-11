namespace G.App.VisionMaster.OpenCV.NodeDatas;

[Icon(FontIcons.EmojiTabPeople)]
[Display(Name = "Yolov5人脸检测", GroupName = "Yolov5人脸检测", Description = "检测图像中多个物体的类别和位置（边界框）", Order = 0)]
public class Yolov5FaceOnnxNodeData : ObjDetectOnnxNodeDataBase
{
    public override void LoadDefault()
    {
        base.LoadDefault();
        this.ModelPath = "yolov5s-face.onnx".ToOnnxPath();
        this.LabelPath = "Face";
        this.InputSize = new System.Windows.Size(640, 640);
        this.OutputRowIndex = 1;
        this.OutputColumnIndex = 2;
        this.OutputConfidenceIndex = 3;
        //tensor: float32[1, 3, 640, 640]
        //1 25200 16
    }
}
