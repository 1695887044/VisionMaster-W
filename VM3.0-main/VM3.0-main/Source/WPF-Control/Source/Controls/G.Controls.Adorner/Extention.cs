namespace G.Controls.Adorner;

public static class Extention
{

    /// <summary>
    /// 配置
    /// </summary>
    /// <param name="service"></param>
    public static void UseAdorner(this IApplicationBuilder service, Action<AdornerSetting> action = null)
    {
        action?.Invoke(AdornerSetting.Instance);
        IocSetting.Instance.Add(AdornerSetting.Instance);
    }
}
