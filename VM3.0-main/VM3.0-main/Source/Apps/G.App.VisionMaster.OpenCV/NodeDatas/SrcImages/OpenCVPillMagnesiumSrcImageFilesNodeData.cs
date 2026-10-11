using G.Controls.Diagram.Presenter.Flowables;
using G.VisionMaster.OpenCV.Base;

namespace G.App.VisionMaster.OpenCV.NodeDatas.SrcImages;
[Display(Name = "药丸破裂数据源", GroupName = "数据源", Order = 0)]
public class OpenCVPillMagnesiumSrcImageFilesNodeData : OpenCVSrcFilesNodeDataBase, IZooSrcImageFilesNodeData
{
    protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
    {
        Mat mat = new Mat(this.SrcFilePath, ImreadModes.Color);
        return this.OK(mat);
    }

    protected override void UpdateResultImageSource()
    {
        this.ResultImageSource = this.Mat.ToImageSource();
    }

    public override void LoadDefault()
    {
        base.LoadDefault();
        this.SrcFilePaths = this.SrcFilePaths.Where(x => x.Contains("pill_magnesium_crack")).ToObservable();
    }
}

