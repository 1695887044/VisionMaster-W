using G.Services.Common.Theme;
using G.Services.Logger;

namespace G.Modules.Theme;
public class LoadThemeOptionsService : ILoadThemeOptionsService
{
    public bool Load(out string message)
    {
        try
        {
            return ThemeOptions.Instance.Load(out message);
        }
        catch (Exception ex)
        {
            message = ex.Message;
            IocLog.Instance?.Error(ex);
            return false;
        }
    }
}
