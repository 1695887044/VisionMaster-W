
namespace G.App.VisionMaster.OpenCV.NodeDatas.SrcImages;
[Display(Name = "车门图像源", GroupName = "数据源", Order = 0)]

public class OpenCVCardoorSrcImageFilesNodeData : OpenCVSrcFilesNodeDataBase, IZooSrcImageFilesNodeData
{
    public override void LoadDefault()
    {
        base.LoadDefault();
        this.SrcFilePaths = this.SrcFilePaths.Where(x => x.Contains("car_door")).ToObservable();
    }
}

