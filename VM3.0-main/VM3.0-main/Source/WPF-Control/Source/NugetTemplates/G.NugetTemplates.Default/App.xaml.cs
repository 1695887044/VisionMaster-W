using G.Extensions.ApplicationBase;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;

namespace G.NugetTemplates.Default;
/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : ApplicationBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddApplicationServices();
    }

    protected override void Configure(IApplicationBuilder app)
    {
        app.UseApplicationOptions();
    }

    protected override Window CreateMainWindow(StartupEventArgs e)
    {
        return new MainWindow();
    }
}
