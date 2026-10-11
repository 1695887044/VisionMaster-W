using G.Services.Setting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace G.Modules.Help.ReleaseVersions;

public static class Extention
{
    /// <summary>
    /// 注册
    /// </summary>
    /// <param name="service"></param>
    public static void AddReleaseVersions(this IServiceCollection services, Action<IReleaseVersionsOptions> setupAction = null)
    {
        services.AddOptions();
        services.TryAdd(ServiceDescriptor.Singleton<IReleaseVersionsService, ReleaseVersionsService>());
        if (setupAction != null)
            services.Configure(setupAction);
    }

    /// <summary>
    /// 配置
    /// </summary>
    /// <param name="service"></param>
    public static void UseReleaseVersions(this IApplicationBuilder service, Action<IReleaseVersionsOptions> action = null)
    {
        action?.Invoke(ReleaseVersionsOptions.Instance);
        IocSetting.Instance.Add(ReleaseVersionsOptions.Instance);
    }
}
