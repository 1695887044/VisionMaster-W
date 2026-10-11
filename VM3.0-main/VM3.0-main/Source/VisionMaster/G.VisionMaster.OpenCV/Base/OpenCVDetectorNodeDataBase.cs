namespace G.VisionMaster.OpenCV.Base;
[Icon(FontIcons.Photo)]
public abstract class OpenCVDetectorNodeDataBase : OpenCVNodeDataBase
{
    private PreviewType _detectorPreviewType = PreviewType.Src;
    [Display(Name = "输出预览类型", GroupName = VisionPropertyGroupNames.DisplayParameters, Description = "设置从原图输出匹配结果还是上一结果中输出")]
    public PreviewType DetectorPreviewType
    {
        get { return _detectorPreviewType; }
        set
        {
            _detectorPreviewType = value;
            RaisePropertyChanged();
        }
    }

    private int _matchingCountResult;
    [ReadOnly(true)]
    [Display(Name = "匹配数量", GroupName = VisionPropertyGroupNames.ResultParameters, Description = "结果参数，此结果可应用再条件分支等作为判断参数")]
    public int MatchingCountResult
    {
        get { return _matchingCountResult; }
        set
        {
            _matchingCountResult = value;
            RaisePropertyChanged();
        }
    }

    protected virtual Mat GetPrviewMat(ISrcVisionNodeData<Mat> srcImageNodeData, Mat from, Mat result)
    {
        if (this.DetectorPreviewType == PreviewType.Previous)
            return from?.Clone();
        return this.DetectorPreviewType == PreviewType.Result ? result.Clone() : (srcImageNodeData.Mat?.Clone());
    }
}

public enum PreviewType
{
    [Display(Name = "原图")]
    Src = 0,
    [Display(Name = "前图")]
    Previous = 1,
    [Display(Name = "识别结果")]
    Result = 2
}
