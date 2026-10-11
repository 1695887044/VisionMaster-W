global using G.Common.Attributes;
global using G.Controls.Diagram.Presenter.DiagramDatas.Base;
global using G.Controls.Diagram.Presenter.Flowables;
global using G.Extensions.FontIcon;
global using G.VisionMaster.NodeData.Base;
global using G.VisionMaster.OpenCV;
global using G.VisionMaster.OpenCV.Base;
using G.VisionMaster.NodeData;
using G.VisionMaster.ResultPresenter;
using System.ComponentModel;

namespace G.NodeDatas.Onnx.OpenCV.Base
{
    [Icon(FontIcons.GuestUser)]
    public abstract class InferOnnxNodeDataBase : OnnxNodeDataBase
    {
        public override void LoadDefault()
        {
            base.LoadDefault();
            this.InputSize = new System.Windows.Size(224, 224);
            this.OutputRowIndex = 0;
            this.OutputColumnIndex = 1;
        }

        private string _valueResult;
        [ReadOnly(true)]
        [Display(Name = "推测结果", GroupName = VisionPropertyGroupNames.ResultParameters, Description = "结果参数，此结果可应用再条件分支等作为判断参数")]
        public string ValueResult
        {
            get { return _valueResult; }
            set
            {
                _valueResult = value;
                RaisePropertyChanged();
            }
        }

        protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
        {
            //age_efficientnet_b2.onnx
            string modelPath = this.ModelPath;
            Mat image = from.Mat;
            System.Collections.Generic.IEnumerable<float> values = image.InferValues(modelPath, this.InputSize.ToCVSize(), this.BlobMean, this.BlobStd, this.OutputRowIndex, this.OutputColumnIndex, this.BlobScaleFactor);
            string value = string.Join(',', values.Select(x => Math.Round(x, 2)));
            this.ValueResult = value;
            return this.OK(image, values.ToDataGridValueResultPresenter(x => x.ToString(), x => "推测结果值"), $"推测结果:{value}");
        }
    }
}
