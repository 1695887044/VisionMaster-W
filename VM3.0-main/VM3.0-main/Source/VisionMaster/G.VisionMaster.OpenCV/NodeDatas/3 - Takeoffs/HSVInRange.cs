using G.VisionMaster.NodeGroup.Groups.Takeoffs;

namespace G.VisionMaster.OpenCV.NodeDatas.Basic;
[Icon(FontIcons.Annotation)]
[Display(Name = "HSV二值分割", GroupName = "基础函数", Description = "根据设定的阈值范围，从图像中提取符合要求的像素区域，生成一个二值掩膜（Mask）", Order = 10)]
public class HSVInRange : OpenCVNodeDataBase, ITakeoffGroupableNodeData
{
    public override void LoadDefault()
    {
        base.LoadDefault();

        //// 定义红色的BGR范围（注意OpenCV默认是BGR格式）
        //Scalar lowerRed = new Scalar(0, 50, 50);    // B最小值, G最小值, R最小值
        //Scalar upperRed = new Scalar(10, 255, 200);  // B最大值, G最大值, R最大值

        //// 红色需要两个范围（色相在 0° 和 180° 附近）
        //Scalar lowerRed1 = new Scalar(0, 70, 30);    // 色相 0° 附近，饱和度和明度下限
        //Scalar upperRed1 = new Scalar(8, 255, 150);  // 色相 8° 范围，饱和度和明度上限

        //Scalar lowerRed2 = new Scalar(172, 70, 30);  // 色相 172° 附近（紫红色调）
        //Scalar upperRed2 = new Scalar(180, 255, 150); // 色相 180° 范围

        //// 定义绿色的HSV范围
        //Scalar lowerGreen = new Scalar(35, 50, 50);   // H最小值, S最小值, V最小值
        //Scalar upperGreen = new Scalar(85, 255, 255); // H最大值, S最大值, V最R最大值
        //this._lowerScalar = lowerGreen;
        //this._upperScalar = upperGreen;
    }

    private ImageColorPickerPresenter _ImageColorPickerPresenter = new ImageColorPickerPresenter();
    [Display(Name = "设置色相", GroupName = VisionPropertyGroupNames.RunParameters, Description = "从图片提取颜色或手动设置色相")]
    public ImageColorPickerPresenter ImageColorPickerPresenter
    {
        get { return _ImageColorPickerPresenter; }
        set
        {
            _ImageColorPickerPresenter = value;
            RaisePropertyChanged();
        }
    }

    private int _hRange;
    [PropertyItem(typeof(Int32SliderTextPropertyItem))]
    [Display(Name = "H色相生成范围", GroupName = VisionPropertyGroupNames.RunParameters, Description = "用于控制吸管工具生成上下限参数")]
    [DefaultValue(35)]
    [Range(0, 85)]
    public int hRange
    {
        get { return _hRange; }
        set
        {
            _hRange = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private int _sRange;
    [PropertyItem(typeof(Int32SliderTextPropertyItem))]
    [Display(Name = "S饱和度生成范围", GroupName = VisionPropertyGroupNames.RunParameters, Description = "用于控制吸管工具生成上下限参数")]
    [DefaultValue(30)]
    [Range(0, 255)]
    public int sRange
    {
        get { return _sRange; }
        set
        {
            _sRange = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private int _vRange;
    [PropertyItem(typeof(Int32SliderTextPropertyItem))]
    [Display(Name = "V明度生成范围", GroupName = VisionPropertyGroupNames.RunParameters, Description = "用于控制吸管工具生成上下限参数")]
    [DefaultValue(30)]
    [Range(0, 255)]
    public int vRange
    {
        get { return _vRange; }
        set
        {
            _vRange = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
    {
        Mat hsv = from.Mat.CvtColor(ColorConversionCodes.BGR2HSV);
        Mat mask = new Mat();
        Tuple<Scalar, Scalar> range = this.ImageColorPickerPresenter.Color.GetHSVRange(this.hRange, this.sRange, this.vRange);
        Scalar lowerScalar = range.Item1;
        Scalar upperScalar = range.Item2;
        // 需要前序流程颜色处理 ColorConversionCodes.BGR2HSV)
        Cv2.InRange(hsv, lowerScalar, upperScalar, mask);
        ////  Do ：反转黑白
        //Cv2.BitwiseNot(mask, mask);
        this.ImageColorPickerPresenter.ImageSource = from.Mat.ToImageSource();
        return this.OK(mask);
    }
}

