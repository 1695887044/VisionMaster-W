using G.Extensions.ApplicationBase;
using Microsoft.Extensions.DependencyInjection;

namespace G.ApplicationBases.Default
{
    public abstract partial class DefaultApplicationBase : ApplicationBase
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            services.AddApplicationServices();
        }

        protected override void Configure(IApplicationBuilder app)
        {
            base.Configure(app);
            app.UseApplicationOptions();
        }
    }
}
