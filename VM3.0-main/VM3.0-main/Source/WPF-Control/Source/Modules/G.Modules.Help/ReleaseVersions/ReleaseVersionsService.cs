using G.Modules.Help.Base;

namespace G.Modules.Help.ReleaseVersions;

public class ReleaseVersionsService : ShowHelpServiceBase, IReleaseVersionsService
{
    public override void Show()
    {
        ReleaseVersionsOptions.Instance.Uri.ShowProcess();
    }
}
