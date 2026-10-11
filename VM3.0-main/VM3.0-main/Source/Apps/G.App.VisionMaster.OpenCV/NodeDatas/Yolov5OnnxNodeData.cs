
using G.Controls.Diagram.Presenter.Flowables;
using G.VisionMaster.NodeData;

namespace G.App.VisionMaster.OpenCV.NodeDatas;

[Display(Name = "Yolov5目标识别", GroupName = "Yolov5多目标检测", Description = "YOLOv5 目标检测模型导出为 ONNX格式后的版本，具有跨平台、高性能的特点，便于在各种环境中部署", Order = 0)]
public class Yolov5OnnxNodeData : ObjDetectOnnxNodeDataBase
{
    public override void LoadDefault()
    {
        base.LoadDefault();
        this.ModelPath = "yolov5s.onnx".ToOnnxPath();
        this.LabelPath = "lable.txt".ToOnnxPath();
        this.InputSize = new System.Windows.Size(640, 640);
        this.OutputRowIndex = 1;
        this.OutputColumnIndex = 2;
        this.OutputConfidenceIndex = 3;
    }

    protected override async Task<IFlowableResult> BeforeInvokeAsync(IFlowableLinkData previors, IFlowableDiagramData diagram)
    {
        if (!File.Exists(this.ModelPath) || !File.Exists(this.LabelPath))
        {
            bool? r = await System.Windows.Application.Current.Dispatcher.Invoke(async () =>
            {
                return await IocMessage.Form?.ShowEdit(this, x => x.Title = $"{this.Name}:请先选择文件", null, x =>
                {
                    x.UsePropertyNames = $"{nameof(ModelPath)},{nameof(LabelPath)}";
                });
            });

            if (r != true)
                return this.Error("训练模型不存在");
        }
        return await base.BeforeInvokeAsync(previors, diagram);
    }

    protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
    {
        string modelPath = this.ModelPath;
        Mat image = from.Mat;
        IEnumerable<DefectBox> boxes = image.DefectBoxes(modelPath, this.InputSize.ToCVSize(), this.BlobMean, this.BlobStd, this.OutputRowIndex, this.OutputColumnIndex, this.OutputConfidenceIndex, this.Threshold, this.NmsThreshold, this.BlobScaleFactor, this.BoxCoordinateMode, this.BoxGeometryType);
        Mat result = image.Clone();
        result.DrawDetectBoxes(VisionSettings.Instance.OutputColor, result.ToThickness(), boxes.ToArray());
        List<string> classNames = this.GetClassNames().ToList();
        IEnumerable<Tuple<DefectBox, string, double>> tuples = result.DrawDetectBoxLabels(boxes, VisionSettings.Instance.OutputColor, VisionSettings.Instance.OutputLabelColor, classNames, this.UseScore);
        this.MatchingCountResult = tuples.Count();
        this.MatchingMaxClassName = tuples.Count() == 0 ? null : tuples.MaxBy(x => x.Item3).Item2;
        this.MaxConfidenceResult = tuples.Count() == 0 ? 0 : tuples.Max(x => x.Item3);
        Controls.Diagram.Presenter.NodeDatas.Base.IResultPresenter resultPresenter = tuples.ToResultPresenter();
        return this.OK(result, resultPresenter);
    }
}
