using G.Modules.Help.Base;

namespace G.Modules.Help.Support;

public class SupportService : ISupportService
{
    public void Show()
    {
        SupportOptions.Instance.Uri.ShowProcess();
    }
}
