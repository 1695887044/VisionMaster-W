using G.Extensions.ApplicationBase;
using Microsoft.Extensions.DependencyInjection;

namespace G.Templates.Project;
public partial class App : ApplicationBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddApplicationServices();
        services.AddProject<DesignProjectService>();
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
