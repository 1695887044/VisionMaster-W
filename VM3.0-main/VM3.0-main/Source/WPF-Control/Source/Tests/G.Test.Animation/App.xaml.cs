using G.Extensions.ApplicationBase;
using G.Modules.Messages.Dialog;
using G.Modules.Messages.Form;
using G.Services.Common;
using G.Services.Message.Dialog;
using G.Services.Message.Form;
using G.Styles;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;

namespace G.Test.Animation
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : ApplicationBase
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IDialogMessageService, AdornerDialogMessageService>();
            services.AddSingleton<IFormMessageService, FormMessageService>();
            services.AddNoticeMessage();
            services.AddSnackMessage();
            services.AddAbout();
        }

        protected override Window CreateMainWindow(StartupEventArgs e)
        {
            return new MainWindow();
        }
    }
}
