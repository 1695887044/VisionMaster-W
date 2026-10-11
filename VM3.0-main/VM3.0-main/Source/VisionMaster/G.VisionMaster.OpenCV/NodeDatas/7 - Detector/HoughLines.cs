using G.VisionMaster.ResultPresenter;
using G.VisionMaster.ResultPresenter.ResultPresenters;

namespace G.VisionMaster.OpenCV.NodeDatas.Detector;

//需要检测理论上的无限长直线
//进行学术研究或特定算法开发
//需要更精细控制霍夫变换参数
[Icon(FontIcons.LargeErase)]
[Display(Name = "直线识别", GroupName = "基础检测", Order = 1, Description = "标准直线识别，检测无限长的直线(ρ,θ参数)")]
public class HoughLines : OpenCVDetectorNodeDataBase, IDetectorGroupableNodeData
{
    private double _rho = 1.0;
    [Range(1.0, 10000.0)]
    [PropertyItem(typeof(DoubleSliderTextPropertyItem))]
    [DefaultValue(1.0)]
    [Display(Name = "距离分辨率（像素）", GroupName = VisionPropertyGroupNames.RunParameters, Description = "距离分辨率（像素），增大可减少计算量但降低精度")]
    public double Rho
    {
        get { return _rho; }
        set
        {
            _rho = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private double _theta = 180;
    [Range(1.0, 360)]
    [PropertyItem(typeof(DoubleSliderTextPropertyItem))]
    [Display(Name = "角度分辨率（角度）", GroupName = VisionPropertyGroupNames.RunParameters, Description = "减小可提高角度精度但增加计算量")]
    public double Theta
    {
        get { return _theta; }
        set
        {
            _theta = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private int _threshold = 100;
    [PropertyItem(typeof(Int32SliderTextPropertyItem))]
    [Range(100, 200)]
    [Display(Name = "投票阈值", GroupName = VisionPropertyGroupNames.RunParameters, Description = "决定被认定为直线的最小票数,值越大检测到的直线越少但更显著")]
    public int Threshold
    {
        get { return _threshold; }
        set
        {
            _threshold = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private double _srn = 0;
    [Display(Name = "细化距离分辨率", GroupName = VisionPropertyGroupNames.RunParameters, Description = "控制距离分辨率(rho)的细化程度")]
    public double Srn
    {
        get { return _srn; }
        set
        {
            _srn = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private double _stn = 0;
    [Display(Name = "细化角度分辨率", GroupName = VisionPropertyGroupNames.RunParameters, Description = "控制角度分辨率(theta)的细化程度")]
    public double Stn
    {
        get { return _stn; }
        set
        {
            _stn = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
    {
        Mat imgGray = from.Mat;
        //Mat imgStd = new Mat(this.SrcFilePath, ImreadModes.Color);
        LineSegmentPolar[] segStd = Cv2.HoughLines(imgGray, Rho, Math.PI / Theta, Threshold, Srn, Stn);
        Mat imgStd = this.GetPrviewMat(srcImageNodeData, from.Mat, imgGray);
        int limit = Math.Min(segStd.Length, 10);
        List<Tuple<Point, Point>> lines = new List<Tuple<Point, Point>>();
        for (int i = 0; i < limit; i++)
        {
            float rho = segStd[i].Rho;
            float theta = segStd[i].Theta;
            double a = Math.Cos(theta);
            double b = Math.Sin(theta);
            double x0 = a * rho;
            double y0 = b * rho;
            Point pt1 = new Point { X = (int)Math.Round(x0 + 1000 * -b), Y = (int)Math.Round(y0 + 1000 * a) };
            Point pt2 = new Point { X = (int)Math.Round(x0 - 1000 * -b), Y = (int)Math.Round(y0 - 1000 * a) };
            imgStd.Line(pt1, pt2, VisionSettings.Instance.OutputColor.ToScalar(), imgStd.ToThickness(), LineTypes.AntiAlias, 0);
            lines.Add(Tuple.Create(pt1, pt2));
        }
        this.MatchingCountResult = lines.Count();
        Controls.Diagram.Presenter.NodeDatas.Base.IResultPresenter resultPresenter = lines.ToDataGridValueResultPresenter(x => $"起点：{x.Item1.ToString()} 终点：{x.Item2.ToString()}", x => "直线数据");
        return this.OK(imgStd, resultPresenter, this.MatchingCountResult.ToDetectSuccessMessage());
    }
}
