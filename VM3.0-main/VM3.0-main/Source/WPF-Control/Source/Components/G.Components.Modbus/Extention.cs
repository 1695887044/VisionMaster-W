using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace G.Components.Modbus;

public static class Extention
{
    /// <summary>
    /// 注册
    /// </summary>
    /// <param name="service"></param>
    public static void AddModbus(this IServiceCollection services)
    {
        //services.AddOptions();
        services.TryAdd(ServiceDescriptor.Singleton<ISerializableModbusDataService, SerializableModbusDataService>());
        //if (setupAction != null)
        //    services.Configure(new Action<AboutOptions>(setupAction));
    }

    ///// <summary>
    ///// 配置
    ///// </summary>
    ///// <param name="service"></param>
    //public static void UseAboutOptions(this IApplicationBuilder service, Action<IAboutOptions> action = null)
    //{
    //    action?.Invoke(AboutOptions.Instance);
    //    IocSetting.Instance.Add(AboutOptions.Instance);
    //}
}
