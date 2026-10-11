using G.VisionMaster.OpenCV.Base;

namespace G.App.VisionMaster.OpenCV.NodeDatas.SrcImages;
[Display(Name = "Halcon图像源", GroupName = "数据源", Order = 0)]
public class OpenCVHalconSrcImageFilesNodeData : OpenCVSrcFilesNodeDataBase, IZooSrcImageFilesNodeData
{
    public override void LoadDefault()
    {
        base.LoadDefault();
        this.SrcFilePaths = this.SrcFilePaths.Where(x => x.Contains("BaseImages")).ToObservable();
    }
}
