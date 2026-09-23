namespace VisionMaster.Scada
{
    /// <summary>
    /// 叠放次序的一次移动（"谁盖住谁"只有 <see cref="ScadaElement.ZIndex"/> 一个来源，
    /// 本枚举只是把"这个数该改成多少"说清楚）。
    ///
    /// 为什么是"四个方向"而不是"设成某个数值"：用户在属性面板上想表达的从来不是
    /// "我要 ZIndex = 7"，而是"把它挪到最上面 / 往上压一层"。让面板直接改数值，
    /// 等于把"整张画面的有序序列"这件只有画面自己知道的事推给用户去心算。
    ///
    /// 与 <see cref="ScadaActionType"/> 同样的落盘纪律：<b>本枚举不落盘</b>——
    /// 它是一次操作，不是一个状态，存进 .vms 没有意义。所以数值随便定，不承担兼容负担。
    /// </summary>
    public enum ScadaZMove
    {
        /// <summary>置顶：挪到所有图元之上</summary>
        ToFront = 1,

        /// <summary>上移一层：与紧挨着它上面的那个图元换位</summary>
        Forward = 2,

        /// <summary>下移一层：与紧挨着它下面的那个图元换位</summary>
        Backward = 3,

        /// <summary>置底：挪到所有图元之下</summary>
        ToBack = 4,
    }

    /// <summary>
    /// 叠放方向的展示名。
    ///
    /// 为什么展示名放在领域层：属性面板上的按钮提示、将来右键菜单、操作日志里那句
    /// "已把 [料箱] 置顶"是同一份知识。分两处写，就会出现"面板里叫『上移』、
    /// 日志里叫『Forward』"。与 <see cref="ScadaActionTypeExtensions.DisplayName"/> 同一个理由。
    /// </summary>
    public static class ScadaZMoveExtensions
    {
        /// <summary>给人看的名字（属性面板按钮提示、操作日志共用）</summary>
        public static string DisplayName(this ScadaZMove move) => move switch
        {
            ScadaZMove.ToFront => "置顶",
            ScadaZMove.Forward => "上移一层",
            ScadaZMove.Backward => "下移一层",
            ScadaZMove.ToBack => "置底",
            _ => $"未知叠放({(int)move})",
        };
    }
}
