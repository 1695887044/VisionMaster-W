using G.Extensions.ApplicationBase;
using G.Modules.Setting;
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

namespace G.Test.License
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : ApplicationBase
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            services.AddAdornerDialogMessage();
            services.AddWindowMessage();
            
            services.AddSetting();
            services.AddLicenseService(x =>
            {
                x.UseTrial = true;
                x.UseTipTrialOnLoad= false;
            });
        }

        protected override void Configure(IApplicationBuilder app)
        {
            base.Configure(app);
            app.UseLicense();
        }

        protected override Window CreateMainWindow(StartupEventArgs e)
        {
            return new MainWindow();
        }
    }
}
