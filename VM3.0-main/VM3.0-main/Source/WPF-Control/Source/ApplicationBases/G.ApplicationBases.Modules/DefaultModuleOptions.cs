using G.Extensions.ApplicationBase;
using G.Modules.About;
using G.Modules.Feedback;
using G.Modules.Guide;
using G.Modules.Help.ReleaseVersions;
using G.Modules.Help.Support;
using G.Modules.Help.WebSite;
using G.Modules.Setting;
using G.Modules.SplashScreen;

namespace G.ApplicationBases.Modules
{

    public class DefaultModuleOptions : CacheActionOptionsBase, IDefaultModuleOptions
    {
        public void UseAboutOptions(Action<IAboutOptions> action)
        {
            this.ConfigOptions(action);
        }

        public void UseGuideOptions(Action<IGuideOptions> action)
        {
            this.ConfigOptions(action);
        }

        public void UseReleaseVersionsOptions(Action<IReleaseVersionsOptions> action)
        {
            this.ConfigOptions(action);
        }

        public void UseSettingSecurityViewOptions(Action<ISettingSecurityViewOption> action)
        {
            this.ConfigOptions(action);
        }

        public void UseSettingViewOptions(Action<ISettingViewOptions> action)
        {
            this.ConfigOptions(action);
        }

        public void UseSplashScreenOptions(Action<ISplashScreenOptions> action)
        {
            this.ConfigOptions(action);
        }

        public void UseSupportOptions(Action<ISupportOptions> action)
        {
            this.ConfigOptions(action);
        }

        public void UseWebsiteOptions(Action<IWebsiteOptions> action)
        {
            this.ConfigOptions(action);
        }

        public void UseFeedbackOptions(Action<IFeedbackOptions> action)
        {
            this.ConfigOptions(action);
        }

    }
}
