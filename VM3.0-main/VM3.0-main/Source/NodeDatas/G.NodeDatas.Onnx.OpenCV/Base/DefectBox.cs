namespace G.NodeDatas.Onnx.OpenCV.Base
{
    public struct DefectBox
    {
        public int ClassId { get; set; }
        public Rect2f Box { get; set; }
        public float Score { get; set; }
    }

    /// <summary>
    /// 定义检测框坐标的基准表示方式（绝对像素值或归一化比例）
    /// </summary>
    public enum BoxCoordinateMode
    {
        /// <summary> 绝对像素坐标（如 [x_min, y_min, x_max, y_max]） </summary>
        AbsolutePixels,

        /// <summary> 归一化比例坐标（YOLO格式，如 [x_center, y_center, width, height]∈[0,1]） </summary>
        NormalizedRatio
    }

    /// <summary>
    /// 定义检测框的几何表示类型
    /// </summary>
    public enum BoxGeometryType
    {
        /// <summary> 角点坐标表示（如四边形四个顶点 [x1,y1, x2,y2, x3,y3, x4,y4]） </summary>
        CornerPoints,
        /// <summary> 中心点+宽高表示（如 [cx, cy, width, height]） </summary>
        CenterWithSize,
        /// <summary> 左上点+宽高表示（如 [x, y, width, height]） </summary>
        PointWithSize,
        /// <summary> 极坐标表示（如 [cx, cy, radius, angle]） </summary>
        PolarWithAngle
    }
}
