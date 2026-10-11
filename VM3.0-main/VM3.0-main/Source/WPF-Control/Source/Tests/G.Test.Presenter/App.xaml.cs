using G.Extensions.ApplicationBase;
using G.Modules.Messages.Dialog;
using G.Services.Common;
using G.Services.Message.Dialog;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows;

namespace G.Test.Presenter
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : ApplicationBase
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<IDialogMessageService, AdornerDialogMessageService>();
            services.AddAbout();
        }

        protected override Window CreateMainWindow(StartupEventArgs e)
        {
            return new MainWindow();
        }
    }
}
