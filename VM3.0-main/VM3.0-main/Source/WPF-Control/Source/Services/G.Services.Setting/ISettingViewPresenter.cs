using G.Common.Interfaces;

namespace G.Services.Setting;

public interface ISettingViewPresenter : ITitleable
{
    void SwitchTo(Type type);
    void RefreshSettingData();
    Task<bool> Show(Type switchType = null);
}
