namespace G.VisionMaster.OpenCV.NodeDatas.Other;

public abstract class SeamlessCloneBase : OpenCVNodeDataBase
{
    private SeamlessCloneMethods _method = SeamlessCloneMethods.NormalClone;
    [DefaultValue(SeamlessCloneMethods.NormalClone)]
    [Display(Name = "Method", GroupName = VisionPropertyGroupNames.RunParameters)]
    public SeamlessCloneMethods Method
    {
        get { return _method; }
        set
        {
            _method = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    //public override IFlowableResult Invoke(Part previors, Node diagram)
    //{
    //    var path = this.DetectFilePath;
    //    Mat src = new Mat(path, ImreadModes.Color);
    //    Mat dst = this.GetFromMat(diagram);
    //    Mat src0 = src.Resize(dst.Size(), 0, 0, InterpolationFlags.Lanczos4);
    //    Mat mask = Mat.Zeros(src0.Size(), MatType.CV_8UC3);
    //    mask.Circle(200, 200, 100, Scalar.White, -1);
    //    Mat blend = new Mat();
    //    Cv2.SeamlessClone(src0, dst, mask, this.Point, blend, this.Method);
    //    this.Mat = blend;
    //    this.UpdateMatToView();
    //    return base.Invoke(previors, diagram);
    //}
}

[Display(Name = "无缝融合", GroupName = "基础函数", Description = "将一幅图像中的指定目标复制后粘贴到另一幅图像中，并自然的融合", Order = 80)]
public class SeamlessClone : SeamlessCloneBase, IOtherGroupableNodeData
{
    public SeamlessClone()
    {
        this.DetectFilePath = "Assets\\OpenCV\\cat.jpg";
    }

    private string _detectFilePath;
    [PropertyItem(typeof(OpenFileDialogPropertyItem))]
    [Display(Name = "检测图片路径", GroupName = VisionPropertyGroupNames.RunParameters)]
    public string DetectFilePath
    {
        get { return _detectFilePath; }
        set
        {
            _detectFilePath = value;
            RaisePropertyChanged();
        }
    }

    private System.Windows.Point _point = new System.Windows.Point(260, 270);
    [Display(Name = "放置位置", GroupName = VisionPropertyGroupNames.RunParameters)]
    public System.Windows.Point Point
    {
        get { return _point; }
        set
        {
            _point = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    public override void LoadDefault()
    {
        base.LoadDefault();
        this.Point = new System.Windows.Point(260, 270);
    }

    protected override async Task<IFlowableResult> BeforeInvokeAsync(IFlowableLinkData previors, IFlowableDiagramData diagram)
    {
        if (File.Exists(this.DetectFilePath) == false)
        {
            bool? r = await IocMessage.Form?.ShowEdit(this, x => x.Title = $"{this.Name}:请先选择文件", null, x =>
            {
                x.UsePropertyNames = nameof(DetectFilePath);
            });
            if (r != true)
                return this.Error("未设置源文件地址");
        }
        return await base.BeforeInvokeAsync(previors, diagram);
    }

    protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
    {
        //string path = this.DetectFilePath;
        //using Mat src = new Mat(path, ImreadModes.Color);
        //using Mat dst = from.Mat;
        //using Mat src0 = src.Resize(dst.Size(), 0, 0, InterpolationFlags.Lanczos4);
        //using Mat mask = Mat.Zeros(src0.Size(), MatType.CV_8UC3);
        //mask.Circle(200, 200, 100, Scalar.White, -1);
        //Mat blend = new Mat();
        //Cv2.SeamlessClone(src0, dst, mask, this.Point.ToCVPoint(), blend, this.Method);
        //return this.OK(blend);

        string path = this.DetectFilePath;
        if (!File.Exists(path))
            return this.Error(from.Mat, "请先设置融合图片");

        using Mat src = new Mat(path, ImreadModes.Color);
        using Mat dst = from.Mat;

        if (src.Width > dst.Width || src.Height > dst.Height)
            return this.Error(from.Mat, "融合图片不能大于源图像尺寸");
        //using Mat src0 = src.Resize(dst.Size(), 0, 0, InterpolationFlags.Lanczos4);
        using Mat mask = new Mat(src.Size(), MatType.CV_8UC1, Scalar.White);
        //mask.Circle(200, 200, 100, Scalar.White, -1);
        Point center = new Point(dst.Width / 2, dst.Height / 2);
        Mat blend = new Mat();
        Cv2.SeamlessClone(src, dst, mask, center, blend, this.Method);
        return this.OK(blend);

        //// 创建掩膜（假设要插入img2到img1中）
        //Mat mask = new Mat(from.Mat.Size(), MatType.CV_8UC1, Scalar.White);
        //Point center = new Point(src.Width / 2, src.Height / 2);

        //Mat result = new Mat();
        //Cv2.SeamlessClone(from.Mat, src, mask, center, result, SeamlessCloneMethods.NormalClone);
        //return this.OK(blend);
    }
}
