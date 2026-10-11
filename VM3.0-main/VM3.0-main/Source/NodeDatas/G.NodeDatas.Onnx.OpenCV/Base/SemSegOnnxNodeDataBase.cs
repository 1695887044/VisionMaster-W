using G.VisionMaster.NodeData;
using System.Collections.Generic;
using System.Windows.Media;

namespace G.NodeDatas.Onnx.OpenCV.Base
{
    [Icon(FontIcons.HomeGroup)]
    public abstract class SemSegOnnxNodeDataBase : OnnxNodeDataBase
    {
        public override void LoadDefault()
        {
            base.LoadDefault();
            this.InputSize = new System.Windows.Size(192, 192);
            //[batch_size, num_classes, height, width]`
            this.OutputRowIndex = 1;
            this.OutputColumnIndex = 2;
            this._outputMaskIndexs = new Int32Collection(new int[] { 1 });
        }

        private Int32Collection _outputMaskIndexs = new Int32Collection();
        [Display(Name = "显示掩码索引", GroupName = VisionPropertyGroupNames.DisplayParameters, Description = "设置显示掩码索引，")]
        public Int32Collection OutputMaskIndexs
        {
            get { return _outputMaskIndexs; }
            set
            {
                _outputMaskIndexs = value;
                RaisePropertyChanged();
            }
        }

        protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
        {
            //human_segmentation_pphumanseg_2023mar.onnx
            string modelPath = this.ModelPath;
            Mat image = from.Mat;
            IEnumerable<Tuple<float, Mat>> tuples = image.SemanticSegmentation(modelPath, this.InputSize.ToCVSize(),
                this.BlobMean,
                this.BlobStd,
                this.OutputRowIndex,
                this.OutputColumnIndex,
                this.BlobScaleFactor);
            Mat result = image.Clone();

            IEnumerable<Tuple<float, Mat>> Filter()
            {
                int[] indexs = this.OutputMaskIndexs.Dispatcher.Invoke(() =>
                {
                    return this.OutputMaskIndexs.Select(x => x).ToArray();
                });
                if (indexs.Length == 0)
                {
                    foreach (Tuple<float, Mat> item in tuples)
                        yield return item;
                }
                else
                {
                    foreach (int maskIndex in indexs)
                    {
                        if (maskIndex < 0 || maskIndex >= tuples.Count())
                            continue;
                        yield return tuples.ElementAt(maskIndex);
                    }
                }
            }

            List<Tuple<float, Mat>> results = Filter().ToList();
            foreach (Tuple<float, Mat> item in results)
            {
                Mat mask = item.Item2;
                Mat srcMask = mask.ToSrcMask(image.Rows, image.Cols);
                // 创建彩色掩码(如红色)
                Color randomColor = ColorProvider.GetRandomColor();
                //Mat colorMask = new Mat(image.Size(), MatType.CV_8UC3, randomColor.ToScalar());
                //// 将彩色掩码应用到原图
                //Cv2.BitwiseAnd(colorMask, colorMask, result, srcMask); // 先获取彩色掩码区域
                //Cv2.AddWeighted(image, 0.7, result, 0.3, 0, result); // 与原图混合
                // 创建原图副本
                //Mat highlighted = image.Clone();
                // 将掩码区域设置为特定颜色
                result.SetTo(VisionSettings.Instance.OutputColor.ToScalar(), srcMask); // 绿色高亮
                //return this.OK(highlighted);
                // 可选：混合显示
                //Mat finalResult = new Mat();
                Cv2.AddWeighted(image, 0.5, result, 0.5, 0, result);
                //return this.OK(finalResult);

            }
            return this.OK(result);
        }
    }
}
