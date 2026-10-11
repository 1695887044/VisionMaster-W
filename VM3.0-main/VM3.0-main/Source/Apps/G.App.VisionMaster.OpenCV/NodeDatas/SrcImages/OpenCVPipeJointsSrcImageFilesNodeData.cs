using G.VisionMaster.OpenCV.Base;

namespace G.App.VisionMaster.OpenCV.NodeDatas.SrcImages;
[Display(Name = "管道接头图像源", GroupName = "数据源", Order = 0)]
public class OpenCVPipeJointsSrcImageFilesNodeData : OpenCVSrcFilesNodeDataBase, IZooSrcImageFilesNodeData
{
    public override void LoadDefault()
    {
        base.LoadDefault();
        this.SrcFilePaths = this.SrcFilePaths.Where(x => x.Contains("multi_view_pipe_joints_cam")).ToObservable();
    }
}

