using G.ApplicationBases.Modules;
using G.Modules.About;
using G.Modules.Feedback;
using G.Modules.Guide;
using G.Modules.Help.ReleaseVersions;
using G.Modules.Help.Support;
using G.Modules.Help.Website;
using G.Modules.Help.WebSite;
using G.Modules.Setting;
using G.Modules.SplashScreen;
using Microsoft.Extensions.DependencyInjection;

namespace System
{
    public static class Extention
    {
        /// <summary>
        /// 注册
        /// </summary>
        /// <param name="service"></param>
        public static void AddDefaultModuleServices(this IServiceCollection services, Action<IDefaultModuleOptions> options = null)
        {
            DefaultModuleOptions opt = new DefaultModuleOptions();
            options?.Invoke(opt);
            services.AddAbout(opt.GetConfigOptions<Action<IAboutOptions>>());
            services.AddGuide(opt.GetConfigOptions<Action<IGuideOptions>>());
            services.AddSplashScreen(opt.GetConfigOptions<Action<ISplashScreenOptions>>());
            services.AddSetting(opt.GetConfigOptions<Action<ISettingViewOptions>>());
            services.AddReleaseVersions(opt.GetConfigOptions<Action<IReleaseVersionsOptions>>());
            services.AddSupport(opt.GetConfigOptions<Action<ISupportOptions>>());
            services.AddWebsite(opt.GetConfigOptions<Action<IWebsiteOptions>>());
            services.AddFeedBack(opt.GetConfigOptions<Action<IFeedbackOptions>>());
        }

        public static void UseDefaultModuleOptions(this IApplicationBuilder app, Action<IDefaultModuleOptions> options = null)
        {
            DefaultModuleOptions opt = new DefaultModuleOptions();
            options?.Invoke(opt);
            app.UseAboutOptions(opt.GetConfigOptions<Action<IAboutOptions>>());
            app.UseSplashScreenOptions(opt.GetConfigOptions<Action<ISplashScreenOptions>>());
            app.UseGuideOptions(opt.GetConfigOptions<Action<IGuideOptions>>());
            app.UseSettingViewOptions(opt.GetConfigOptions<Action<ISettingViewOptions>>());
            app.UseSettingSecurityOptions(opt.GetConfigOptions<Action<ISettingSecurityViewOption>>());
            app.UseReleaseVersions(opt.GetConfigOptions<Action<IReleaseVersionsOptions>>());
            app.UseSupport(opt.GetConfigOptions<Action<ISupportOptions>>());
            app.UseWebsite(opt.GetConfigOptions<Action<IWebsiteOptions>>());
            app.UseFeedBackOptions(opt.GetConfigOptions<Action<IFeedbackOptions>>());
        }
    }
}
