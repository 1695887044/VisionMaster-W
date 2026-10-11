using G.Controls.Form.Attributes;
using G.Controls.Form.PropertyItem.TextPropertyItems;
using G.Services.Message;
using G.VisionMaster.NodeData;
using G.VisionMaster.OpenCV.TypeConverters;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;

namespace G.NodeDatas.Onnx.OpenCV.Base
{

    [Icon(FontIcons.SmartcardVirtual)]
    public abstract class OnnxNodeDataBase : OpenCVNodeDataBase, IOpenCVDnnNodeData
    {
        private System.Windows.Size _inputSize = new System.Windows.Size(640, 640);
        [Display(Name = "归一化尺寸", GroupName = VisionPropertyGroupNames.RunParameters)]
        public System.Windows.Size InputSize
        {
            get { return _inputSize; }
            set
            {
                _inputSize = value;
                RaisePropertyChanged();
            }
        }

        private float _blobScaleFactor = 255.0f;
        [DefaultValue(255.0f)]
        [Display(Name = "归一化比例", GroupName = VisionPropertyGroupNames.RunParameters, Description = "每个预测框对应的特征/属性数量")]
        public float BlobScaleFactor
        {
            get { return _blobScaleFactor; }
            set
            {
                _blobScaleFactor = value;
                RaisePropertyChanged();
            }
        }

        private Scalar _blobMean = new Scalar();
        [TypeConverter(typeof(ScalarTypeConverter))]
        [Display(Name = "归一化均值", GroupName = VisionPropertyGroupNames.RunParameters, Description = "用于各通道减去的值，以降低光照的影响")]
        public Scalar BlobMean
        {
            get { return _blobMean; }
            set
            {
                _blobMean = value;
                RaisePropertyChanged();
            }
        }

        private Scalar _blobStd = new Scalar();
        [TypeConverter(typeof(ScalarTypeConverter))]
        [Display(Name = "归一化标准差", GroupName = VisionPropertyGroupNames.RunParameters, Description = "使输入数据的分布更稳定，从而提升模型的训练效果和泛化能力")]
        public Scalar BlobStd
        {
            get { return _blobStd; }
            set
            {
                _blobStd = value;
                RaisePropertyChanged();
            }
        }

        private string _modelPath;
        [Required]
        [Display(Name = "模型路径", GroupName = VisionPropertyGroupNames.RunParameters)]
        [PropertyItem(typeof(OpenFileDialogPropertyItem))]
        public string ModelPath
        {
            get { return _modelPath; }
            set
            {
                _modelPath = value;
                RaisePropertyChanged();
            }
        }

        private int _outputRowIndex = 1;
        [DefaultValue(1)]
        [Display(Name = "结果行位置", GroupName = VisionPropertyGroupNames.RunParameters, Description = "用于获取结果数据中行数据的位置")]
        public int OutputRowIndex
        {
            get { return _outputRowIndex; }
            set
            {
                _outputRowIndex = value;
                RaisePropertyChanged();
            }
        }

        private int _outputColumnIndex = 2;
        [DefaultValue(2)]
        [Display(Name = "结果列位置", GroupName = VisionPropertyGroupNames.RunParameters, Description = "用于获取结果数据中列数据的位置")]
        public int OutputColumnIndex
        {
            get { return _outputColumnIndex; }
            set
            {
                _outputColumnIndex = value;
                RaisePropertyChanged();
            }
        }

        private int _outPutConfidenceIndex = -1;
        [DefaultValue(-1)]
        [Display(Name = "置信度位置", GroupName = VisionPropertyGroupNames.RunParameters, Description = "用于获取结果数据中置信度列的位置，一般Yolov模型是4，其他模型-1，根据模型设置")]
        public int OutputConfidenceIndex
        {
            get { return _outPutConfidenceIndex; }
            set
            {
                _outPutConfidenceIndex = value;
                RaisePropertyChanged();
            }
        }
        protected override async Task<IFlowableResult> BeforeInvokeAsync(IFlowableLinkData previors, IFlowableDiagramData diagram)
        {
            if (!File.Exists(this.ModelPath))
            {
                bool? r = await System.Windows.Application.Current.Dispatcher.Invoke(async () =>
                {
                    return await IocMessage.Form?.ShowEdit(this, x => x.Title = x.Title = $"{this.Name}:请先选择文件", null, x =>
                    {
                        x.UsePropertyNames = $"{nameof(ModelPath)}";
                    });
                });

                if (r != true)
                    return this.Error("训练模型不存在请先选择模型");
            }
            return await base.BeforeInvokeAsync(previors, diagram);
        }
    }
}
