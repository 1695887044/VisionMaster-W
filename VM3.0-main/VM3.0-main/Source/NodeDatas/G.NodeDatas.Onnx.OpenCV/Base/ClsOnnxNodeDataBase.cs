using G.Controls.Form.Attributes;
using G.Controls.Form.PropertyItem.TextPropertyItems;
using G.Services.Message;
using G.VisionMaster.NodeData;
using G.VisionMaster.ResultPresenter;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;

namespace G.NodeDatas.Onnx.OpenCV.Base
{
    [Icon(FontIcons.Group)]
    public abstract class ClsOnnxNodeDataBase : OnnxNodeDataBase
    {
        public override void LoadDefault()
        {
            base.LoadDefault();
            //[batch_size, num_classes]
            this.InputSize = new System.Windows.Size(224, 224);
            this.OutputRowIndex = 0;
            this.OutputColumnIndex = 1;
        }

        private string _classNameResult;
        [ReadOnly(true)]
        [Display(Name = "分类结果", GroupName = VisionPropertyGroupNames.ResultParameters)]
        public string ClassNameResult
        {
            get { return _classNameResult; }
            set
            {
                _classNameResult = value;
                RaisePropertyChanged();
            }
        }

        private double _confidenceResult;
        [ReadOnly(true)]
        [Display(Name = "置信度", GroupName = VisionPropertyGroupNames.ResultParameters)]
        public double ConfidenceResult
        {
            get { return _confidenceResult; }
            set
            {
                _confidenceResult = value;
                RaisePropertyChanged();
            }
        }

        //private int _classCount = 1;
        //[DefaultValue(1)]
        //[Display(Name = "类型数量", GroupName = "数据")]
        //public int ClassCount
        //{
        //    get { return _classCount; }
        //    set
        //    {
        //        _classCount = value;
        //        RaisePropertyChanged();
        //    }
        //}

        private string _labelPath;
        //[Required]
        [Display(Name = "标签路径/数值", GroupName = "数据")]
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

        protected override async Task<IFlowableResult> BeforeInvokeAsync(IFlowableLinkData previors, IFlowableDiagramData diagram)
        {
            if (string.IsNullOrEmpty(this.LabelPath) || !File.Exists(this.ModelPath))
            {
                bool? r = await System.Windows.Application.Current.Dispatcher.Invoke(async () =>
                {
                    return await IocMessage.Form?.ShowEdit(this, x => x.Title = $"{this.Name}:请先选择文件", null, x =>
                    {
                        x.UsePropertyNames = $"{nameof(LabelPath)},{nameof(ModelPath)}";
                    });
                });

                if (r != true)
                    return this.Error("标签路径/数值文件不存在，请先选择文件");
            }
            return await base.BeforeInvokeAsync(previors, diagram);
        }

        protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
        {
            //gender_efficientnet_b2.onnx
            string modelPath = this.ModelPath;
            Mat image = from.Mat;
            List<string> classNames = this.LabelPath?.GetClassNames().ToList();
            IEnumerable<Tuple<string, float>> tuples = image.Classification(modelPath, this.InputSize.ToCVSize(),
                this.BlobMean,
                this.BlobStd,
                classNames,
                this.OutputRowIndex,
                this.OutputColumnIndex,
                this.BlobScaleFactor);
            string value = string.Join('，', tuples.OrderByDescending(x => x.Item2).Select(x => $"{x.Item1} {x.Item2.ToString("##.##")}"));
            this.ClassNameResult = tuples.MaxBy(x => x.Item2).Item1;
            this.ConfidenceResult = tuples.MaxBy(x => x.Item2).Item2;
            return this.OK(image, tuples.ToDataGridValueResultPresenter(x => x.Item2.ToString(), x => x.Item1), $"推测结果：{value}");
        }
    }
}
