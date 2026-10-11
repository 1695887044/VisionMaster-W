namespace G.App.VisionMaster.OpenCV.NodeDatas.SrcImages;

[Display(Name = "人物图像源", GroupName = "数据源", Order = 0)]
public class PersonSrcImageFilesNodeData : OpenCVSrcFilesNodeDataBase, IZooSrcImageFilesNodeData
{
    public override void LoadDefault()
    {
        base.LoadDefault();
        this.SrcFilePaths = this.SrcFilePaths.Where(x => x.Contains("Person")).ToObservable();
    }
}
