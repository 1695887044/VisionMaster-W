using G.ApplicationBases.Default;
using G.Extensions.ApplicationBase;
using G.Themes.FontSizes;
using Microsoft.Extensions.DependencyInjection;
using System.Configuration;
using System.Data;
using System.Windows;

namespace G.Test.DefaultApplicationBase;
/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : ApplicationBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddApplicationServices(x =>
        {
            x.UseModulesOptions(x =>
            {
                x.UseAboutOptions(x => x.ProductName = "Test");
                x.UseSplashScreenOptions(x => x.Product = "TTTTT");
            });
            x.UseThemeModuleOptions(x =>
            {
                x.UseThemeOptions(x =>
                {
                    x.FontSize = FontSizeThemeType.Large;
                });
            });
        });
    }
    protected override void Configure(IApplicationBuilder app)
    {
        base.Configure(app);
        app.UseApplicationOptions();
    }
    protected override Window CreateMainWindow(StartupEventArgs e)
    {
        return new MainWindow();
    }
}

