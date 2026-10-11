using G.Extensions.ApplicationBase;
using Microsoft.Extensions.DependencyInjection;

namespace G.ApplicationBases.Messages
{
    public abstract partial class MessageApplicationBase : ApplicationBase
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            services.AddDefaultMessages();
        }
    }
}
