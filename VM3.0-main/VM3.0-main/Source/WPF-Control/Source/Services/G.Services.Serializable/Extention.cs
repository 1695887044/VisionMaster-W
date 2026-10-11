using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace G.Services.Serializable;

public static class Extention
{
    /// <summary>
    /// 注册
    /// </summary>
    /// <param name="service"></param>
    public static IServiceCollection AddTextJsonSerializerService(this IServiceCollection services)
    {
        services.TryAdd(ServiceDescriptor.Singleton<IJsonSerializerService, TextJsonSerializerService>());
        return services;
    }
}