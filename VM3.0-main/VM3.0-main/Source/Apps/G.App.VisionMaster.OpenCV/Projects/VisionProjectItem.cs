
using G.VisionMaster.DiagramData;
using G.VisionMaster.Project;

namespace G.App.VisionMaster.OpenCV.Projects;
public class VisionProjectItem : VisionProjectItemBase
{
    /// <summary>
    /// 创建一个新的OpenCV图表数据实例。
    /// </summary>
    /// <returns>返回创建的OpenCV图表数据实例。</returns>
    protected override IVisionDiagramData CreateDiagramData()
    {
        return new OpenCVVisionDiagramData() { Width = 1000, Height = 1500 };
    }
}
