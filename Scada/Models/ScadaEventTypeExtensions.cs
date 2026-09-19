namespace VisionMaster.Scada
{
    /// <summary>
    /// 组态事件的中文显示名（属性面板那一行、日志里那一句话都用它）。
    ///
    /// 为什么放在领域层而不是界面侧的转换器：<see cref="ScadaActionType"/> 的显示名同一个理由——
    /// "按下"这个词在面板上和在日志里必须是同一个词。写在 XAML 的 DataTemplate 里，
    /// 早晚会出现日志叫"点击"、面板叫"按下"，用户以为自己配错了事件。
    ///
    /// 与枚举数值一样，这里的名字改了不影响 .vms（文件里存的是数值），所以可以放心措辞调整。
    /// </summary>
    public static class ScadaEventTypeExtensions
    {
        /// <summary>显示名；未知道来的数值（高版本存的事件读进低版本）回落成"未知事件(N)"而不是抛异常</summary>
        public static string DisplayName(this ScadaEventType type) => type switch
        {
            ScadaEventType.Loaded => "加载完成",
            ScadaEventType.Unloaded => "卸载",
            ScadaEventType.Pressed => "按下",
            ScadaEventType.Released => "释放",
            ScadaEventType.ValueChanged => "值改变",
            _ => $"未知事件({(int)type})",
        };
    }
}
