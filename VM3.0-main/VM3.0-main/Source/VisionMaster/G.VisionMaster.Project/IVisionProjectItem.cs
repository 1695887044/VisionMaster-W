using G.Services.Project;
using G.VisionMaster.DiagramData;
using System.Collections.ObjectModel;

namespace G.VisionMaster.Project;

/// <summary>
/// 表示一个视觉项目项的接口。
/// </summary>
public interface IVisionProjectItem : IProjectItem
{
    /// <summary>
    /// 获取或设置OpenCV图表数据的集合。
    /// </summary>
    ObservableCollection<IVisionDiagramData> DiagramDatas { get; set; }

    /// <summary>
    /// 获取或设置选定的OpenCV图表数据。
    /// </summary>
    IVisionDiagramData SelectedDiagramData { get; set; }
}
