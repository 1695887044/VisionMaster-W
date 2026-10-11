namespace G.VisionMaster.OpenCV.NodeDatas.Detector;

[Icon(FontIcons.QRCode)]
[Display(Name = "二维码识别", GroupName = "基础检测", Order = 3)]
public class QRCode : OpenCVDetectorNodeDataBase, IDetectorGroupableNodeData
{
    private string _qrCodeResult;
    [ReadOnly(true)]
    [Display(Name = "二维码结果", GroupName = VisionPropertyGroupNames.ResultParameters, Description = "结果参数，此结果可应用再条件分支等作为判断参数")]
    public string QrCodeResult
    {
        get { return _qrCodeResult; }
        set
        {
            _qrCodeResult = value;
            RaisePropertyChanged();
        }
    }
    protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
    {
        Mat image = from.Mat.Clone();
        // 2. 创建二维码检测器
        QRCodeDetector qrDecoder = new QRCodeDetector();
        // 3. 检测二维码
        Point2f[] points;
        using (Mat straightQrCode = new Mat())
        {
            bool detected = qrDecoder.Detect(image, out points);
            if (detected && points != null)
            {
                //    bool detected = qrDecoder.DetectAndDecode(image, out points, out data);
                // 4. 解码二维码
                string data = qrDecoder.Decode(image, points, straightQrCode);
                this.QrCodeResult = data;
                this.MatchingCountResult = 1;
                if (!string.IsNullOrEmpty(data))
                {
                    // 5. 在图像上绘制二维码边界
                    for (int i = 0; i < points.Length; i++)
                    {
                        Cv2.Line(image, (Point)points[i], (Point)points[(i + 1) % points.Length],
                                VisionSettings.Instance.OutputColor.ToScalar(), image.ToThickness());
                    }
                    if (points.Length < 2)
                    {
                        return this.OK(image, "解码结果: " + data);
                    }
                    Controls.Diagram.Presenter.NodeDatas.Base.IResultPresenter resultPresenter = points.ToResultPresenter(data);
                    return this.OK(image, resultPresenter, "解码结果: " + data);
                }
                else
                {
                    return this.OK(image, "检测到二维码但解码失败");
                }
            }
            else
            {
                this.MatchingCountResult = 0;
                return this.OK(image, "未检测到二维码");
            }
        }
    }
}

