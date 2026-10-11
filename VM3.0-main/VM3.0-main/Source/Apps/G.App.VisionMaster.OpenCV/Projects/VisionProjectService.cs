namespace G.App.VisionMaster.OpenCV.Projects;

/// <summary>
/// 表示视觉项目服务类。
/// </summary>
public class VisionProjectService : ProjectServiceBase<VisionProjectItem>, ILoginedSplashLoadable
{
    /// <summary>
    /// 初始化 <see cref="VisionProjectService"/> 类的新实例。
    /// </summary>
    /// <param name="options">项目选项。</param>
    public VisionProjectService(IOptions<ProjectOptions> options) : base(options)
    {

    }

    /// <summary>
    /// 创建一个新的视觉项目项。
    /// </summary>
    /// <returns>返回创建的视觉项目项。</returns>
    public override VisionProjectItem Create()
    {
        VisionProjectItem n = new VisionProjectItem();
        n.InitData();
        return n;
    }
}
