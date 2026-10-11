using G.VisionMaster.NodeGroup.Groups.Others;
using OpenCvSharp.DnnSuperres;

namespace G.VisionMaster.OpenCV.NodeDatas.Other;

[Icon(FontIcons.DialShape1)]
[Display(Name = "超分辨率处理", GroupName = "基础函数", Description = "超分辨率处理是一项强大的技术，能够显著提升图像和视频的质量", Order = 60)]
public class DnnSuperres : OpenCVNodeDataBase, IOtherGroupableNodeData
{
    private string _algo = "fsrcnn";
    [DefaultValue("fsrcnn")]
    [Display(Name = "算法类型", GroupName = VisionPropertyGroupNames.RunParameters)]
    public string Algo
    {
        get { return _algo; }
        set
        {
            _algo = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private int _scale = 4;
    [DefaultValue(4)]
    [Display(Name = "缩放系数", GroupName = VisionPropertyGroupNames.RunParameters)]
    public int Scale
    {
        get { return _scale; }
        set
        {
            _scale = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    private string _modelFileName = "Data/Model/FSRCNN_x4.pb";
    [DefaultValue("Data/Model/FSRCNN_x4.pb")]
    [ReadOnly(true)]
    [Display(Name = "模型文件", GroupName = VisionPropertyGroupNames.RunParameters)]
    public string ModelFileName
    {
        get { return _modelFileName; }
        set
        {
            _modelFileName = value;
            RaisePropertyChanged();
        }
    }

    //public override IFlowableResult Invoke(Part previors, Node diagram)
    //{
    //    var src = this.GetFromMat(diagram);
    //    var dnn = new DnnSuperResImpl("fsrcnn", 4);
    //    string path = GetDataPath(this.ModelFileName);
    //    dnn.ReadModel(path);
    //    //using var src = new Mat(ImagePath.Mandrill, ImreadModes.Color);
    //    var dst = new Mat();
    //    dnn.Upsample(src, dst);
    //    this.UpdateMatToView(dst);
    //    return base.Invoke(previors, diagram);
    //}

    protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
    {
        Mat src = from.Mat;
        using DnnSuperResImpl dnn = new DnnSuperResImpl(this.Algo, this.Scale);
        string path = this.ModelFileName.ToDataPath();
        dnn.ReadModel(path);
        //using var src = new Mat(ImagePath.Mandrill, ImreadModes.Color);
        Mat dst = new Mat();
        dnn.Upsample(src, dst);
        return this.OK(dst);
    }
}

