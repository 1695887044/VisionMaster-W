using G.Services.Setting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace G.Modules.Help.Support;

public static class Extention
{
    /// <summary>
    /// 注册
    /// </summary>
    /// <param name="service"></param>
    public static void AddSupport(this IServiceCollection services, Action<ISupportOptions> setupAction = null)
    {
        services.AddOptions();
        services.TryAdd(ServiceDescriptor.Singleton<ISupportService, SupportService>());
        if (setupAction != null)
            services.Configure(setupAction);
    }

    /// <summary>
    /// 配置
    /// </summary>
    /// <param name="service"></param>
    public static void UseSupport(this IApplicationBuilder service, Action<ISupportOptions> action = null)
    {
        action?.Invoke(SupportOptions.Instance);
        IocSetting.Instance.Add(SupportOptions.Instance);
    }
}
