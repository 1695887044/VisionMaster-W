using G.Controls.Diagram.Presenter.DiagramDatas.Base;
using G.VisionMaster.NodeData;
using System.Collections.ObjectModel;

namespace G.VisionMaster.DiagramData;
/// <summary>
/// 表示一个包含OpenCV结果图像和消息的接口。
/// </summary>
public interface IVisionDiagramData : INodeDataGroupsDiagramData, IFlowableDiagramData, IResultImageSourceDiagramData
{
    /// <summary>
    /// 获取或设置消息的集合。
    /// </summary>
    ObservableCollection<IVisionMessage> Messages { get; set; }

    void Stop();

    bool? RunModeResult { get; }
}