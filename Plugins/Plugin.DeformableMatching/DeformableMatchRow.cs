namespace Plugin.DeformableMatching
{
    /// <summary>结果表行：表格按分数降序显示；端口数组保持 HALCON 返回顺序。</summary>
    public sealed class DeformableMatchRow
    {
        public int Index { get; init; }
        public double Score { get; init; }
        public double Row { get; init; }
        public double Column { get; init; }
        public bool IsBest { get; init; }
    }
}
