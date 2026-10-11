global using G.Common.Attributes;
global using G.Extensions.FontIcon;

namespace G.VisionMaster.OpenCV.Base;
[Icon(FontIcons.FitPage)]
public abstract class FeatureOpenCVNodeDataBase : OpenCVNodeDataBase, IFeatureDetectorOpenCVNodeData
{
    private int _featureCountResult;
    [ReadOnly(true)]
    [Display(Name = "特征点数量", GroupName = VisionPropertyGroupNames.ResultParameters, Description = "结果参数，此结果可应用再条件分支等作为判断参数")]
    public int FeatureCountResult
    {
        get { return _featureCountResult; }
        set
        {
            _featureCountResult = value;
            RaisePropertyChanged();
        }
    }
}
