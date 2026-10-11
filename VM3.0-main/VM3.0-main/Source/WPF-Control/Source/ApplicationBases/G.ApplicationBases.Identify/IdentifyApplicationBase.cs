using G.Extensions.ApplicationBase;
using Microsoft.Extensions.DependencyInjection;

namespace G.ApplicationBases.Identify
{
    public abstract partial class IdentifyApplicationBase : ApplicationBase
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            services.AddIdentifyDefaultServices();
        }

        protected override void Configure(IApplicationBuilder app)
        {
            base.Configure(app);
            app.UseIdentifyDefaultOptions();
        }
    }
}
