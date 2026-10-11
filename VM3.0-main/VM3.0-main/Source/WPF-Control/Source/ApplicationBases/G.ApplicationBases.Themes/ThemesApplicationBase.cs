using G.Extensions.ApplicationBase;
using Microsoft.Extensions.DependencyInjection;

namespace G.ApplicationBases.Themes
{
    public abstract partial class ThemesApplicationBase : ApplicationBase
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.AddDefaultThemeServices();
        }
        protected override void Configure(IApplicationBuilder app)
        {
            base.Configure(app);
            app.UseDefaultThemeOptions();
        }
    }
}
