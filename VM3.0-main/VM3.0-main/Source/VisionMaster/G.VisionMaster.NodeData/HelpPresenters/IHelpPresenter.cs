namespace G.VisionMaster.NodeData.HelpPresenters;
public interface IHelpPresenter
{
}

public class HelpPresenter : IHelpPresenter
{
    public string Name { get; set; } = "更多 >>";
    public string Content { get; set; } = "开发文档地址";
    public string Url { get; set; } = "https://G.github.io/WPF-Control-Docs/";
}
