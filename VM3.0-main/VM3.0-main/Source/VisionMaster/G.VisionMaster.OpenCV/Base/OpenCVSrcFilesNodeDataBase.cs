namespace G.VisionMaster.OpenCV.Base;

public abstract class OpenCVSrcFilesNodeDataBase : SrcFilesVisionNodeData<Mat>
{
    protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
    {
        Mat mat = new Mat(this.SrcFilePath, ImreadModes.Color);
        this.PixelWidth = mat.Width;
        this.PixelHeight = mat.Height;
        this.ImageColorType = mat.Type();
        return this.OK(mat);
    }

    protected override void UpdateResultImageSource()
    {
        this.ResultImageSource = this.Mat.ToImageSource();
    }
    protected override bool IsValid(Mat t)
    {
        return t.IsValid();
    }
}
