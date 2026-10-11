using G.Extensions.ApplicationBase;
using G.Extensions.AppPath;
using G.Modules.Setting;
using G.Modules.Setting.Base;
using G.Services.Common;
using G.Services.Setting;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;

namespace G.Test.Setting
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : ApplicationBase
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            services.AddAdornerDialogMessage();
            services.AddSnackMessage();
            //services.AddWindowDialogMessage();
            services.AddSetting();
        }

        protected override void Configure(IApplicationBuilder app)
        {
            app.UseSettingDataOptions(x =>
            {
                x.Add(LoginSetting.Instance);
            });
            app.UseSettingViewOptions();
            app.UseSettingSecurityOptions();
            app.UseSettingDefaultOptions();
        }

        protected override Window CreateMainWindow(StartupEventArgs e)
        {
            return new MainWindow();
        }

        protected override void OnSplashScreen(StartupEventArgs e)
        {
            base.OnSplashScreen(e);

            IocSetting.Instance.Load(null, out var message);
        }
    }
}
