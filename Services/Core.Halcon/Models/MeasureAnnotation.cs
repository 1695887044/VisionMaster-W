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
        Text,

        /// <summary>
        /// 折线标注（N 点连线 + 可选端点序号）：2026-10-08 新增。
        /// 此前 BeadInspect 画 14 点参考路径要拼 2(N-1)+2N ≈ 54 个 Line/Text 对象，
        /// 每次改点整表重建；一个 Polyline 一个对象画完，焊缝/轮廓/匹配路径同受益。
        /// </summary>
        Polyline,

        /// <summary>点标注（小十字标记 + 可选文本）：匹配中心、标定点、取点回显用</summary>
        Point,
    }

    /// <summary>
    /// 测量标注（显示契约）：随图像每帧覆盖渲染，绘制逻辑集中在 HalconBase.RenderAll
    /// Points 参数按 Type 约定：
    /// - Line:     [row1, col1, row2, col2]
    /// - Angle:    [顶点row, 顶点col, 臂1端row, 臂1端col, 臂2端row, 臂2端col]
    /// - Text:     [row, col]
    /// - Polyline: [r1, c1, r2, c2, ..., rN, cN]（N ≥ 2）
    /// - Point:    [row, col]（标记十字 ±6px，Text 有值时文本画在点右上）
    /// </summary>
    public sealed class MeasureAnnotation
    {
        /// <summary>标注类型</summary>
        public MeasureType Type { get; init; }

        /// <summary>几何参数（含义见类注释）</summary>
        public double[] Points { get; init; } = Array.Empty<double>();

        /// <summary>文本内容（测量值/说明）</summary>
        public string Text { get; init; } = string.Empty;

        /// <summary>Halcon 颜色名（如 green/red/yellow），默认绿色</summary>
        public string Color { get; init; } = "green";

        /// <summary>
        /// Polyline 专用：是否在每个顶点画小十字并标序号（默认否——长路径标号会糊成一片）。
        /// </summary>
        public bool ShowVertices { get; init; }
    }
}
