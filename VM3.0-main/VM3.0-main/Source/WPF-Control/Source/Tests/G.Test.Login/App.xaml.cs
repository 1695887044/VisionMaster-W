using G.Extensions.ApplicationBase;
using G.Extensions.Mail;
using G.Modules.Login;
using G.Modules.Setting;
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

namespace G.Test.Login
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : ApplicationBase
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            
            services.AddSetting();
            services.AddWindowMessage();
            services.AddAdornerDialogMessage();
            //services.AddLoginViewPresenter();
            services.AddRegisterLoginViewPresenter();
            services.AddTestLoginService();
            services.AddTestRegistorService();
            services.AddMail();
        }

        protected override Window CreateMainWindow(StartupEventArgs e)
        {
            return new MainWindow();
        }

        protected override void Configure(IApplicationBuilder app)
        {
            base.Configure(app);
            app.UseLoginOptions();
            app.UseRegistorOptions();
            app.UseMailOptions();
        }

        protected override void OnSplashScreen(StartupEventArgs e)
        {
            base.OnSplashScreen(e);
        }

        protected override void OnLogin()
        {
            base.OnLogin();

        }
    }
}
