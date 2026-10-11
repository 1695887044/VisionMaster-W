using G.VisionMaster.NodeGroup.Groups.Conditions;

namespace G.VisionMaster.OpenCV.NodeDatas.Image;
[Icon(FontIcons.Dial6)]
[Display(Name = "判断图片像素大小", GroupName = "判断条件", Description = "设置像素阈值，根据阈值执行不同路径逻辑", Order = 20)]
public class PixelThresholdIfConditionNodeData : OpenCVNodeDataBase, IConditionGroupableNodeData
{
    protected override IEnumerable<IPortData> CreatePortDatas()
    {
        {
            IFlowablePortData port = CreatePortData();
            port.Dock = Dock.Top;
            port.PortType = PortType.Input;

            yield return port;
        }
        {
            IFlowablePortData port = CreatePortData();
            port.Dock = Dock.Left;
            port.PortType = PortType.OutPut;
            port.Name = "像素小于";
            port.Description = "像素小于阈值";
            yield return port;
        }
        {
            IFlowablePortData port = CreatePortData();
            port.Dock = Dock.Right;
            port.PortType = PortType.OutPut;
            port.Name = "像素大于";
            port.Description = "像素大于阈值";
            yield return port;
        }
    }

    private int _pixel = 500;
    [Display(Name = "像素阈值", GroupName = VisionPropertyGroupNames.RunParameters)]
    public int Pixel
    {
        get { return _pixel; }
        set
        {
            _pixel = value;
            RaisePropertyChanged();
        }
    }

    protected override IEnumerable<Tuple<IFlowablePortData, Predicate<IFlowableLinkData>>> GetFlowablePortDatas(IFlowableDiagramData diagramData)
    {
        IOpenCVNodeData srcImageNodeData = diagramData.GetStartNodeDatas().OfType<IOpenCVNodeData>().FirstOrDefault();
        IEnumerable<Tuple<IFlowablePortData, Predicate<IFlowableLinkData>>> ports = base.GetFlowablePortDatas(diagramData);
        bool r = srcImageNodeData.Mat.Width > this.Pixel || srcImageNodeData.Mat.Height > this.Pixel;
        return r ? ports.Where(p => p.Item1.Name == "像素大于") : ports.Where(p => p.Item1.Name == "像素小于");
    }

    protected override FlowableResult<Mat> Invoke(ISrcVisionNodeData<Mat> srcImageNodeData, IVisionNodeData<Mat> from, IFlowableDiagramData diagram)
    {
        return this.OK(from.Mat);
    }
}
