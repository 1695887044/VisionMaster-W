namespace G.VisionMaster.OpenCV.NodeDatas.Basic;

/// <summary>
/// 图像增强：
/// 通过乘法或加权乘法增强图像的亮度或对比度。
/// 图像融合：
/// 将多幅图像融合成一幅图像。
/// 颜色校正：
/// 通过除法或加权除法调整图像的亮度和对比度。
/// 图像恢复：
/// 在去噪或图像恢复中，除法运算可以用于去除不均匀光照。
/// </summary>
[Icon(FontIcons.Color)]
[Display(Name = "乘除运算(亮度)", GroupName = "基础函数", Description = "设置图片亮度", Order = 51)]
public class MultiplayDivide : OpenCVNodeDataBase, IPreprocessingGroupableNodeData
{
    private double _value = 1.2;
    [PropertyItem(typeof(DoubleSliderTextPropertyItem))]
    [Range(0.0, 100.0)]
    [DefaultValue(1.2)]
    [Display(Name = "数值", GroupName = VisionPropertyGroupNames.RunParameters)]
    public double Value
    {
        get { return _value; }
        set
        {
            _value = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
    {
        return this.OK(from.Mat * this.Value);
    }
}
