using G.NodeDatas.Onnx.OpenCV.Base;

namespace G.NodeDatas.Onnx.OpenCV.NodeDatas
{

    [Display(Name = "目标检测(通用)", GroupName = "目标检测", Description = "检测图像中多个物体的类别和位置（边界框）", Order = 20)]
    public class ObjDetectOnnxNodeData : ObjDetectOnnxNodeDataBase
    {
        public override void LoadDefault()
        {
            base.LoadDefault();
            this.ModelPath = "yolov5s-face.onnx".ToOnnxPath();
            this.LabelPath = "lable.txt".ToOnnxPath();
            this.InputSize = new System.Windows.Size(640, 640);
            //this.LablePath = @"D:\GitHub\WPF-VisionMaster\Source\NodeDatas\G.NodeDatas.OpenCvDnn.Yolov5\Assets\lable.txt";
        }
    }
}
