using G.VisionMaster.NodeGroup.Groups.TemplateMatchings;

namespace G.VisionMaster.OpenCV.NodeDatas.Detector;

[Display(Name = "模板匹配", GroupName = "基础检测", Description = "模板匹配对于简单的对象定位非常有效，但对于旋转、缩放或透视变换的对象可能效果不佳", Order = 0)]
public class TemplateBase64MatchingNodeData : OpenCVBase64MatchingNodeDataBase, ITemplateMatchingGroupableNodeData
{
    private TemplateMatchModes _templateMatchModes = TemplateMatchModes.CCoeffNormed;
    [Display(Name = "匹配类型", GroupName = VisionPropertyGroupNames.RunParameters)]
    public TemplateMatchModes TemplateMatchModes
    {
        get { return _templateMatchModes; }
        set
        {
            _templateMatchModes = value;
            RaisePropertyChanged();
            this.UpdateInvokeCurrent();
        }
    }

    protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
    {
        if (string.IsNullOrEmpty(this.Base64String))
            return this.OK(from.Mat, "运行完成，未绘制模板图片");
        byte[] bytes = Convert.FromBase64String(this.Base64String);
        Mat src = from.Mat;
        using (Mat template = Cv2.ImDecode(bytes, ImreadModes.Color))
        {
            using Mat result = new Mat();
            // 获取模板图像的尺寸
            int resultCols = src.Cols - template.Cols + 1;
            int resultRows = src.Rows - template.Rows + 1;
            result.Create(resultRows, resultCols, MatType.CV_32FC1);
            // 进行模板匹配
            Cv2.MatchTemplate(src, template, result, this.TemplateMatchModes);
            //// 归一化结果
            //Cv2.Normalize(result, result, 0, 1, NormTypes.MinMax, -1);
            // 找到最佳匹配位置
            Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out Point maxLoc);
            // 设置匹配阈值
            double threshold = 0.8;
            Mat view = from.Mat.Clone();
            if (maxVal >= threshold)
            {
                // 在源图像上绘制矩形框
                Rect rect2F = new Rect(maxLoc.X, maxLoc.Y, template.Cols, template.Rows);
                Cv2.Rectangle(view, maxLoc, new Point(maxLoc.X + template.Cols, maxLoc.Y + template.Rows), Colors.Chartreuse.ToScalar(), view.ToThickness());

                this.MatchingCountResult = 1;
                this.Confidence = maxVal;
                Controls.Diagram.Presenter.NodeDatas.Base.IResultPresenter resultPresenter = rect2F.ToResultPresenter($"置信度：{Math.Round(maxVal, 2)}");
                return this.OK(view, resultPresenter, this.MatchingCountResult.ToDetectSuccessMessage());
            }
            else
            {
                this.MatchingCountResult = 0;
                this.Confidence = 0.0;
                return this.OK(view, "没有匹配到模板");
            }
        }
    }

}

