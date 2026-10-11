using G.Extensions.ApplicationBase;
using Microsoft.Extensions.DependencyInjection;

namespace G.Templates.Indentify;
public partial class App : ApplicationBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddApplicationServices();
        services.AddIdentifyDefaultServices();
    }

    protected override void Configure(IApplicationBuilder app)
    {
        app.UseApplicationOptions();
        app.UseIdentifyDefaultOptions();
    }

    protected override Window CreateMainWindow(StartupEventArgs e)
    {
        return new MainWindow();
    }
}
