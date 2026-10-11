using G.Modules.About;
using G.Modules.Guide;
using G.Modules.Help.ReleaseVersions;
using G.Modules.Help.Support;
using G.Modules.Help.WebSite;
using G.Modules.Setting;
using G.Modules.SplashScreen;

namespace G.ApplicationBases.Modules
{
    public interface IDefaultModuleOptions
    {
        void UseAboutOptions(Action<IAboutOptions> action);
        void UseGuideOptions(Action<IGuideOptions> action);
        void UseReleaseVersionsOptions(Action<IReleaseVersionsOptions> action);
        void UseSettingViewOptions(Action<ISettingViewOptions> action);
        void UseSplashScreenOptions(Action<ISplashScreenOptions> action);
        void UseSupportOptions(Action<ISupportOptions> action);
        void UseWebsiteOptions(Action<IWebsiteOptions> action);
        void UseSettingSecurityViewOptions(Action<ISettingSecurityViewOption> action);
    }
}
