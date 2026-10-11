global using G.NodeDatas.Onnx.OpenCV;
global using G.NodeDatas.Onnx.OpenCV.Base;
global using G.VisionMaster.NodeData.Base;
global using G.VisionMaster.OpenCV;
global using OpenCvSharp;
using G.Controls.Diagram.Presenter.Flowables;
using G.VisionMaster.NodeData;
using G.VisionMaster.ResultPresenter;
using System.ComponentModel;

namespace G.App.VisionMaster.OpenCV.NodeDatas;

//age_classes = ["0-10", "11-20", "21-30", "31-40", "41-50", "51-60", "61-70", "71-80", "80+"]
[Display(Name = "年龄推测", GroupName = "推测模型", Description = "age_efficientnet_b2.onnx 是一个基于 EfficientNet-B2 架构的 ONNX 格式模型，专为年龄预测任务设计，支持输入图像分类或回归输出具体年龄值。", Order = 10)]
public class AgeInferOnnxNodeData : InferOnnxNodeDataBase
{
    private double _ageResult;
    [ReadOnly(true)]
    [Display(Name = "推测年龄结果", GroupName = VisionPropertyGroupNames.ResultParameters, Description = "结果参数，此结果可应用再条件分支等作为判断参数")]
    public double AgeResult
    {
        get { return _ageResult; }
        set
        {
            _ageResult = value;
            RaisePropertyChanged();
        }
    }

    public override void LoadDefault()
    {
        base.LoadDefault();
        this.ModelPath = "age_efficientnet_b2.onnx".ToOnnxPath();
        this.InputSize = new System.Windows.Size(224, 224);
        this.OutputRowIndex = 0;
        this.OutputColumnIndex = 1;
        this.OutputConfidenceIndex = -1;
        this.BlobMean = new Scalar(0.485, 0.456, 0.406);
        this.BlobStd = new Scalar(0.229, 0.224, 0.225);
    }

    protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
    {
        string modelPath = this.ModelPath;
        Mat image = from.Mat;
        IEnumerable<float> values = image.InferValues(modelPath, this.InputSize.ToCVSize(), this.BlobMean, this.BlobStd, this.OutputRowIndex, this.OutputColumnIndex, this.BlobScaleFactor);
        string value = string.Join(',', values.Select(x => Math.Round(x, 0)));
        this.AgeResult = values.FirstOrDefault();
        return this.OK(image, values.ToDataGridValueResultPresenter(x => x.ToString(), x => "推测结果值"), $"推测结果:{value}岁");
    }
}
