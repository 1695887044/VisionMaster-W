using G.ApplicationBases.Modules;
using G.ApplicationBases.Themes;

namespace G.ApplicationBases.Default
{
    public interface IDefaultApplicationOptions
    {
        void UseModulesOptions(Action<IDefaultModuleOptions> action);

        void UseThemeModuleOptions(Action<IDefaultThemeOptions> action);
    }
}
