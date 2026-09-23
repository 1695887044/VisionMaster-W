namespace VisionMaster.Scada
{
    /// <summary>
    /// 一组图元的排列动作：六种对齐 + 两种分布。
    ///
    /// 为什么把"对齐"和"分布"放在同一个枚举里，而不是拆成 <c>ScadaAlign</c> + <c>ScadaDistribute</c>：
    /// 对用户来说这是同一件事——"把选中的这一堆摆整齐"，菜单上是同一个子菜单里的八个项，
    /// 撤销栈里也是同一句"对齐 [3 个图元]：水平分布"。拆成两个枚举就得在每一处调用点上
    /// 再分一次流，而分出来的两条路除了算式不同，前置校验、作用域、写入口完全一样。
    ///
    /// 与 <see cref="ScadaZMove"/> 同样的落盘纪律：<b>本枚举不落盘</b>——
    /// 它是一次操作，不是一个状态，存进 .vms 没有意义。所以数值随便定，不承担兼容负担。
    ///
    /// 基准口径（与商业组态软件一致，也是用户唯一不会觉得意外的那一种）：
    /// 六种对齐都以<b>选中集合的整体包围盒</b>为基准——左对齐是"都贴到最靠左那个的左边缘"，
    /// 水平居中是"都贴到包围盒的水平中轴"。绝不是"都贴到某一个图元的边"：
    /// 那样用户得先猜"谁是基准"，而包围盒是选完之后看一眼就知道的东西。
    /// </summary>
    public enum ScadaAlign
    {
        /// <summary>左对齐：全部贴到包围盒的左边缘</summary>
        Left = 1,

        /// <summary>水平居中：全部贴到包围盒的水平中轴（各自按自己的宽度居中）</summary>
        HorizontalCenter = 2,

        /// <summary>右对齐：全部贴到包围盒的右边缘</summary>
        Right = 3,

        /// <summary>顶对齐：全部贴到包围盒的上边缘</summary>
        Top = 4,

        /// <summary>垂直居中：全部贴到包围盒的垂直中轴（各自按自己的高度居中）</summary>
        VerticalCenter = 5,

        /// <summary>底对齐：全部贴到包围盒的下边缘</summary>
        Bottom = 6,

        /// <summary>水平分布：首尾不动，中间几个按"等间隙"重新排开</summary>
        DistributeHorizontal = 7,

        /// <summary>垂直分布：首尾不动，中间几个按"等间隙"重新排开</summary>
        DistributeVertical = 8,
    }

    /// <summary>
    /// 排列动作的展示名。
    ///
    /// 为什么展示名放在领域层：右键菜单上的项名、撤销按钮上那句"对齐 [3 个图元]：左对齐"、
    /// 将来的操作日志是同一份知识。分两处写就会出现"菜单里叫『水平分布』、撤销按钮上叫
    /// 『DistributeHorizontal』"。与 <see cref="ScadaZMoveExtensions.DisplayName"/> 同一个理由。
    /// </summary>
    public static class ScadaAlignExtensions
    {
        /// <summary>给人看的名字（右键菜单项名、撤销记录标题共用）</summary>
        public static string DisplayName(this ScadaAlign align) => align switch
        {
            ScadaAlign.Left => "左对齐",
            ScadaAlign.HorizontalCenter => "水平居中",
            ScadaAlign.Right => "右对齐",
            ScadaAlign.Top => "顶对齐",
            ScadaAlign.VerticalCenter => "垂直居中",
            ScadaAlign.Bottom => "底对齐",
            ScadaAlign.DistributeHorizontal => "水平分布",
            ScadaAlign.DistributeVertical => "垂直分布",
            _ => $"未知排列({(int)align})",
        };

        /// <summary>
        /// 这个动作最少要几个图元才有意义。
        ///
        /// 对齐两个就够（谁跟谁对齐），分布则至少要三个——两个图元"分布"的结果就是原地不动，
        /// 判成可用只会让用户点了没反应。这个下界同时被菜单判灰（<c>CanAlignSelected</c>）
        /// 和领域层前置校验（<see cref="ScadaPage.TryAlignElements"/>）读，两处必须同源：
        /// 各写一遍就会出现"菜单亮着、点了却返回 false"。
        /// </summary>
        public static int MinimumCount(this ScadaAlign align) => align switch
        {
            ScadaAlign.DistributeHorizontal or ScadaAlign.DistributeVertical => 3,
            _ => 2,
        };
    }
}
