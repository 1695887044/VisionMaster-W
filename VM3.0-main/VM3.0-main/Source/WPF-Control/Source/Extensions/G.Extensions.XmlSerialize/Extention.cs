using G.Extensions.XmlSerialize;
using G.Services.Common.Serialize.Meta;
using Microsoft.Extensions.DependencyInjection;

namespace System
{
    public static class Extention
    {
        /// <summary>
        /// 注册
        /// </summary>
        /// <param name="service"></param>
        public static void AddXmlMetaSettingService(this IServiceCollection service)
        {
            service.AddSingleton<IMetaSettingService, XmlMetaSettingService>();
        }
    }
}
