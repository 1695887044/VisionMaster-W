using G.ApplicationBases.Modules;
using G.ApplicationBases.Themes;
using G.Extensions.ApplicationBase;

namespace G.ApplicationBases.Default
{
    public class DefaultApplicationOptions : CacheActionOptionsBase, IDefaultApplicationOptions
    {
        public void UseModulesOptions(Action<IDefaultModuleOptions> action)
        {
            this.ConfigOptions(action);
        }

        public void UseThemeModuleOptions(Action<IDefaultThemeOptions> action)
        {
            this.ConfigOptions(action);
        }
    }
}
