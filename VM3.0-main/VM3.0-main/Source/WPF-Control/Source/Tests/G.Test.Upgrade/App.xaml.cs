
using G.Extensions.ApplicationBase;
using G.Services.Setting;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows;

namespace G.Test.Upgrade
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : ApplicationBase
    {
        protected override Window CreateMainWindow(StartupEventArgs e)
        {
            return new MainWindow();
        }

        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            
            services.AddSetting();
            services.AddWindowMessage();
            services.AddWindowDialogMessage();
            services.AddSplashScreen();
            //  Do ：注册软件更新页面
            services.AddAutoUpgrade(x =>
            {
                //x.Uri = "https://gitee.com/G/wpf-auto-update/raw/master/Install/Diagram/AutoUpdate.xml";
                x.Uri = "https://gitee.com/G/wpf-auto-update/raw/master/Install/Movie/Movie.xml";
                x.UseIEDownload = false;
            });
        }

        protected override void Configure(IApplicationBuilder app)
        {
            base.Configure(app);
            app.UseUpgrade();
        }
    }
}

