namespace VisionMaster.Scada
{
    /// <summary>
    /// 把一组图元按"相邻间距都一样"重排的两个方向。
    ///
    /// 与 <see cref="ScadaAlign.DistributeHorizontal"/> / <see cref="ScadaAlign.DistributeVertical"/>
    /// 的分工（两者互为反面）：
    /// 分布是"两端不动、把现有总长摊匀"，间距是算出来的；
    /// 等间距是"间距由用户给定、从第一个往后排"，总长是算出来的。
    /// 正因为"两端不动"这条在等间距上根本不成立（总长被 Σ边长 + (n-1) × 间距 锁死），
    /// 才把它立成独立动作，而不是给分布加一个可选参数。
    ///
    /// 与 <see cref="ScadaAlign"/> 同样的落盘纪律：本枚举不落盘，数值随便定，不承担兼容负担。
    /// </summary>
    public enum ScadaSpacing
    {
        /// <summary>水平等间距：沿 X 轴，相邻图元之间留一样宽的空隙</summary>
        Horizontal = 1,

        /// <summary>垂直等间距：沿 Y 轴，相邻图元之间留一样高的空隙</summary>
        Vertical = 2,
    }

    /// <summary>
    /// 等间距动作的展示名与数量下界。理由同 <see cref="ScadaSizeMatchExtensions"/>。
    /// </summary>
    public static class ScadaSpacingExtensions
    {
        /// <summary>
        /// 等间距最少要两个图元：一个图元没有"间距"可言。
        ///
        /// 注意这里的下界比"分布"低一级（分布要三个）：分布要拿首尾两个当固定端，
        /// 只剩一个中间项才算"分布"；等间距的两端都可以动，两个就够。
        /// 这个下界同时被菜单判灰和领域层前置校验（<see cref="ScadaPage.TrySpaceElements"/>）读，两处必须同源。
        /// </summary>
        public const int MinimumCount = 2;

        /// <summary>给人看的名字（工具栏提示、右键菜单项名、撤销记录标题共用）</summary>
        public static string DisplayName(this ScadaSpacing spacing) => spacing switch
        {
            ScadaSpacing.Horizontal => "水平等间距",
            ScadaSpacing.Vertical => "垂直等间距",
            _ => $"未知等间距({(int)spacing})",
        };
    }
}
