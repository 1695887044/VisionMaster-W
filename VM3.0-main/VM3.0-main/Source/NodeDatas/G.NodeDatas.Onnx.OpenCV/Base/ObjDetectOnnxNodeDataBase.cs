using G.Controls.Form.Attributes;
using G.Controls.Form.PropertyItem.TextPropertyItems;
using G.VisionMaster.NodeData;
using System.Collections.Generic;
using System.ComponentModel;

namespace G.NodeDatas.Onnx.OpenCV.Base
{
    [Icon(FontIcons.FitPage)]
    public abstract class ObjDetectOnnxNodeDataBase : OnnxNodeDataBase
    {
        public override void LoadDefault()
        {
            base.LoadDefault();
            //[batch_size, num_boxes, (class_probs + bbox_coords)]
            this.InputSize = new System.Windows.Size(640, 640);
            this.OutputRowIndex = 1;
            this.OutputColumnIndex = 2;
        }
        private string _labelPath;
        //[Required]
        [Display(Name = "标签路径", GroupName = VisionPropertyGroupNames.RunParameters)]
        [PropertyItem(typeof(OpenFileDialogPropertyItem))]
        public string LabelPath
        {
            get { return _labelPath; }
            set
            {
                _labelPath = value;
                RaisePropertyChanged();
            }
        }

        private float _threshold = 0.25f;
        [PropertyItem(typeof(FloatSliderTextPropertyItem))]
        [DefaultValue(0.25f)]
        [Range(0.0f, 1.0f)]
        [Display(Name = "置信度阈值", GroupName = VisionPropertyGroupNames.RunParameters, Description = "置信度分数阈值，低于此值的框会被直接过滤")]
        public float Threshold
        {
            get { return _threshold; }
            set
            {
                _threshold = value;
                RaisePropertyChanged();
                this.UpdateInvokeCurrent();
            }
        }

        private float _nmsThreshold = 0.45f;
        [PropertyItem(typeof(FloatSliderTextPropertyItem))]
        [DefaultValue(0.45f)]
        [Range(0.0f, 1.0f)]
        [Display(Name = "NMS重叠阈值", GroupName = VisionPropertyGroupNames.RunParameters, Description = "NMS重叠阈值（IoU阈值），大于此值的框会被视为冗余而被抑制")]
        public float NmsThreshold
        {
            get { return _nmsThreshold; }
            set
            {
                _nmsThreshold = value;
                RaisePropertyChanged();
                this.UpdateInvokeCurrent();
            }
        }

        private BoxCoordinateMode _boxCoordinateMode = BoxCoordinateMode.AbsolutePixels;
        [Display(Name = "坐标系模式", GroupName = VisionPropertyGroupNames.RunParameters, Description = "定义检测框坐标的基准表示方式（绝对像素值或归一化比例）")]
        public BoxCoordinateMode BoxCoordinateMode
        {
            get { return _boxCoordinateMode; }
            set
            {
                _boxCoordinateMode = value;
                RaisePropertyChanged();
            }
        }

        private BoxGeometryType _boxGeometryType = BoxGeometryType.CenterWithSize;
        [Display(Name = "几何表示类型", GroupName = VisionPropertyGroupNames.RunParameters, Description = "定义检测框的几何表示类型")]
        public BoxGeometryType BoxGeometryType
        {
            get { return _boxGeometryType; }
            set
            {
                _boxGeometryType = value;
                RaisePropertyChanged();
            }
        }

        private bool _useScore = true;
        [DefaultValue(true)]
        [Display(Name = "绘制置信度", GroupName = VisionPropertyGroupNames.DisplayParameters, Description = "在输出图像绘制置信度")]
        public bool UseScore
        {
            get { return _useScore; }
            set
            {
                _useScore = value;
                RaisePropertyChanged();
            }
        }

        private int _matchingCountResult;
        [ReadOnly(true)]
        [Display(Name = "目标数量", GroupName = VisionPropertyGroupNames.ResultParameters, Description = "结果参数，此结果可应用再条件分支等作为判断参数")]
        public int MatchingCountResult
        {
            get { return _matchingCountResult; }
            set
            {
                _matchingCountResult = value;
                RaisePropertyChanged();
            }
        }

        private string _matchingMaxClassName;
        [ReadOnly(true)]
        [Display(Name = "最高置信度标签", GroupName = VisionPropertyGroupNames.ResultParameters, Description = "结果参数，此结果可应用再条件分支等作为判断参数")]
        public string MatchingMaxClassName
        {
            get { return _matchingMaxClassName; }
            set
            {
                _matchingMaxClassName = value;
                RaisePropertyChanged();
            }
        }

        private double _MaxConfidenceResult;
        [ReadOnly(true)]
        [Display(Name = "最高置信度", GroupName = VisionPropertyGroupNames.ResultParameters, Description = "结果参数，此结果可应用再条件分支等作为判断参数")]
        public double MaxConfidenceResult
        {
            get { return _MaxConfidenceResult; }
            set
            {
                _MaxConfidenceResult = value;
                RaisePropertyChanged();
            }
        }

        protected virtual IEnumerable<string> GetClassNames()
        {
            return this.LabelPath?.GetClassNames();
        }

        protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
        {
            //yolov5s - face.onnx yolov5s.onnx
            string modelPath = this.ModelPath;
            Mat image = from.Mat;
            List<DefectBox> boxes = image.DefectBoxes(modelPath, this.InputSize.ToCVSize(), this.BlobMean, this.BlobStd, this.OutputRowIndex, this.OutputColumnIndex, this.OutputConfidenceIndex, this.Threshold, this.NmsThreshold, this.BlobScaleFactor, this.BoxCoordinateMode, this.BoxGeometryType).ToList();
            Mat result = image.Clone();
            result.DrawDetectBoxes(VisionSettings.Instance.OutputColor, result.ToThickness(), boxes.ToArray());
            List<string> classNames = this.GetClassNames().ToList();
            IEnumerable<Tuple<DefectBox, string, double>> tuples = result.DrawDetectBoxLabels(boxes, VisionSettings.Instance.OutputColor, VisionSettings.Instance.OutputLabelColor, classNames, this.UseScore);
            this.MatchingCountResult = tuples.Count();
            this.MatchingMaxClassName = tuples.Count() == 0 ? null : tuples.MaxBy(x => x.Item3).Item2;
            this.MaxConfidenceResult = tuples.Count() == 0 ? 0 : tuples.Max(x => x.Item3);
            Controls.Diagram.Presenter.NodeDatas.Base.IResultPresenter resultPresenter = tuples.ToResultPresenter();
            return this.OK(result, resultPresenter, this.MatchingCountResult.ToDetectSuccessMessage());
        }
    }
}
