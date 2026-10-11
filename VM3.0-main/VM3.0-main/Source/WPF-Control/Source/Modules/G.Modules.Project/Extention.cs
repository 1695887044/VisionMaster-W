global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.DependencyInjection.Extensions;
using G.Services.Setting;

namespace System;

public static class Extention
{
    /// <summary>
    /// 注册
    /// </summary>
    /// <param name="service"></param>
    public static IServiceCollection AddProject(this IServiceCollection services, Action<IProjectOptions> setupAction = null)
    {
        return services.AddProject<ProjectService>(setupAction);
    }

    /// <summary>
    /// 注册
    /// </summary>
    /// <param name="service"></param>
    public static IServiceCollection AddProject<T>(this IServiceCollection services, Action<IProjectOptions> setupAction = null) where T : class, IProjectService
    {
        services.AddOptions();
        services.TryAdd(ServiceDescriptor.Singleton<IProjectService, T>());
        services.TryAdd(ServiceDescriptor.Singleton<IProjectViewPresenter, ProjectViewPresenter>());
        //services.TryAdd(ServiceDescriptor.Singleton<ISplashLoad, ProjectLoadService>());

        if (setupAction != null)
            services.Configure(new Action<ProjectOptions>(setupAction));
        return services;
    }

    /// <summary>
    /// 配置
    /// </summary>
    /// <param name="service"></param>
    public static IApplicationBuilder UseProjectOptions(this IApplicationBuilder builder, Action<ProjectOptions> option = null)
    {
        IocSetting.Instance.Add(ProjectOptions.Instance);
        option?.Invoke(ProjectOptions.Instance);
        return builder;
    }
}
