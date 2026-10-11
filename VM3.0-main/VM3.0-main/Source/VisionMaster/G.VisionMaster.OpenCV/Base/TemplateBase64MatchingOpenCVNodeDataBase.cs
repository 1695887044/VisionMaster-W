namespace G.VisionMaster.OpenCV.Base;

public abstract class OpenCVBase64MatchingNodeDataBase : Base64MatchingNodeData<Mat>
{
    protected override void UpdateResultImageSource()
    {
        this.ResultImageSource = this.Mat.ToImageSource();
    }
    protected override bool IsValid(Mat t)
    {
        return t.IsValid();
    }
}

