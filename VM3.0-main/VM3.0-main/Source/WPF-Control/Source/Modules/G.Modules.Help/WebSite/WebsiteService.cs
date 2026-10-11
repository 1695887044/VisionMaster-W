using G.Modules.Help.Base;

namespace G.Modules.Help.WebSite;

public class WebsiteService : IWebsiteService
{
    public void Show()
    {
        WebsiteOptions.Instance.Uri.ShowProcess();
    }
}
