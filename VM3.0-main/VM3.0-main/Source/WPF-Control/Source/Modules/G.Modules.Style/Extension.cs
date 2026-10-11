using G.Modules.Style;
using G.Services.Setting;

namespace System;

public static partial class Extension
{
    public static IApplicationBuilder UseStyleOptions(this IApplicationBuilder builder, Action<IStyleOptions> option = null)
    {
        option?.Invoke(StyleOptions.Instance);
        IocSetting.Instance.Add(StyleOptions.Instance);
        return builder;
    }
}
