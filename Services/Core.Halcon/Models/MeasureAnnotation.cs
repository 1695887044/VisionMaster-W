namespace Core.Halcon.Models
{
    /// <summary>
    /// 测量标注类型
    /// </summary>
    public enum MeasureType
    {
        /// <summary>线段标注（两点连线 + 中点文本，如距离测量）</summary>
        Line,

        /// <summary>角度标注（顶点 + 两臂端点 + 顶点处角度文本）</summary>
        Angle,

        /// <summary>纯文本标注（指定位置显示文字）</summary>
        Text
    }

    /// <summary>
    /// 测量标注（显示契约）：随图像每帧覆盖渲染，绘制逻辑集中在 HalconBase.RenderAll
    /// Points 参数按 Type 约定：
    /// - Line:  [row1, col1, row2, col2]
    /// - Angle: [顶点row, 顶点col, 臂1端row, 臂1端col, 臂2端row, 臂2端col]
    /// - Text:  [row, col]
    /// </summary>
    public sealed class MeasureAnnotation
    {
        /// <summary>标注类型</summary>
        public MeasureType Type { get; init; }

        /// <summary>几何参数（含义见类注释）</summary>
        public double[] Points { get; init; }

        /// <summary>文本内容（测量值/说明）</summary>
        public string Text { get; init; }

        /// <summary>Halcon 颜色名（如 green/red/yellow），默认绿色</summary>
        public string Color { get; init; } = "green";
    }
}
