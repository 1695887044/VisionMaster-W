namespace VisionMaster.Scada
{
    /// <summary>
    /// 把一组图元统一成同一个尺寸的三种口径：统一宽、统一高、宽高一起统一。
    ///
    /// 为什么和 <see cref="ScadaAlign"/> 一样把"尺寸"和"间距"拆成两个枚举，而不是并成一个
    /// <c>ScadaArrange</c>：等尺寸改的是图元自己的 <see cref="ScadaElement.Width"/> /
    /// <see cref="ScadaElement.Height"/>，等间距改的是 <see cref="ScadaElement.X"/> /
    /// <see cref="ScadaElement.Y"/>，两组写入口连参数表都不一样（等间距还要多带一个像素值）。
    /// 硬并成一个枚举，就得让每一处调用点再分一次流。
    ///
    /// 与 <see cref="ScadaAlign"/> / <see cref="ScadaZMove"/> 同样的落盘纪律：本枚举不落盘，
    /// 它是一次操作不是一个状态，存进 .vms 没有意义，所以数值随便定，不承担兼容负担。
    ///
    /// 基准口径（与商业组态软件一致）：以选中集合里最大的那个为准。
    /// 等大小刻意按两根轴各自取最大，而不是取面积最大的那个图元的宽高：
    /// 这样"等大小"的结果恒等于"先等宽、再等高"，用户点一个按钮和点两个按钮得到同一张画面，
    /// 不会多出"宽高都不等于任何一个图元"的第三种结果。
    ///
    /// 缩放锚点：图元的 X / Y 不动，只改宽高，也就是以左上角为锚往右下长。
    /// 这与拖动、对齐都只改位置不改尺寸的分工一致。
    /// </summary>
    public enum ScadaSizeMatch
    {
        /// <summary>等宽：全部改成选中集合里最大的那个宽度</summary>
        Width = 1,

        /// <summary>等高：全部改成选中集合里最大的那个高度</summary>
        Height = 2,

        /// <summary>等大小：宽取最大宽、高取最大高（等价于先等宽再等高）</summary>
        Both = 3,
    }

    /// <summary>
    /// 等尺寸动作的展示名与数量下界。
    ///
    /// 为什么展示名放在领域层：工具栏按钮的提示、右键菜单项名、撤销按钮上那句
    /// "等尺寸 [3 个图元]：等宽"是同一份知识。分两处写就会出现"菜单里叫『等宽』、
    /// 撤销按钮上叫『Width』"。与 <see cref="ScadaAlignExtensions.DisplayName"/> 同一个理由。
    /// </summary>
    public static class ScadaSizeMatchExtensions
    {
        /// <summary>
        /// 等尺寸最少要几个图元：两个（一个图元没有"互相对齐尺寸"可言）。
        ///
        /// 三种口径的下界完全一样，所以写成常量而不是按值分支。
        /// 这个下界同时被菜单判灰（<c>CanMatchSelectedSize</c>）和领域层前置校验
        /// （<see cref="ScadaPage.TryMatchElementSize"/>）读，两处必须同源：
        /// 各写一遍就会出现"按钮亮着、点了却返回 false"。
        /// </summary>
        public const int MinimumCount = 2;

        /// <summary>给人看的名字（工具栏提示、右键菜单项名、撤销记录标题共用）</summary>
        public static string DisplayName(this ScadaSizeMatch match) => match switch
        {
            ScadaSizeMatch.Width => "等宽",
            ScadaSizeMatch.Height => "等高",
            ScadaSizeMatch.Both => "等大小",
            _ => $"未知等尺寸({(int)match})",
        };
    }
}
