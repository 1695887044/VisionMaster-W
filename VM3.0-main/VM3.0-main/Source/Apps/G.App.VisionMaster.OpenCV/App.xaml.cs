using G.App.VisionMaster.OpenCV.Projects;
using G.Extensions.ApplicationBase;
using G.Services.Setting;
using G.Themes.Colors;
using G.VisionMaster.NodeData;
using Microsoft.Extensions.DependencyInjection;

namespace G.App.VisionMaster.OpenCV;

public partial class App : ApplicationBase
{

    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddApplicationServices();
        services.AddProject<VisionProjectService>(x =>
        {
            x.Extenstion = ".json";
            x.JsonSerializerService = new NewtonsoftJsonSerializerService();
        });
    }

    protected override void Configure(IApplicationBuilder app)
    {
        base.Configure(app);

        app.UseSplashScreenOptions(x =>
        {
            x.ProductFontSize = 55;
            x.Product = "GVision";
            x.Sub = "OpenCV 3.0 版本";
        });
        IocSetting.Instance.Save(out _);
        app.UseApplicationOptions(x =>
        x.UseThemeModuleOptions(x =>
        {
            x.UseColorThemeOptions(x =>
            {
                x.ColorResource = x.ColorResources.OfType<DarkColorResource>().FirstOrDefault();
            });
        }));

        app.UseSettingDataOptions(x =>
        {
            x.Add(VisionSettings.Instance);
        });
    }

    protected override System.Windows.Window CreateMainWindow(StartupEventArgs e)
    {
        return new MainWindow();
    }
}
