using System;

namespace Core.Interfaces
{
    /// <summary>
    /// 标定类型。
    /// </summary>
    public enum CalibrationKind
    {
        /// <summary>像素当量：只有 mm/px（没有机械坐标）。适合"只测毫米尺寸"的场景（卡尺/Blob 的当量来源）</summary>
        PixelScale = 0,

        /// <summary>
        /// 网格标定（习惯叫"九点标定"）：N×N 组 (机械坐标 ↔ 图像点) 求出的 2D 仿射变换，
        /// 支持像素 → 机械 / 机械 → 像素 双向换算。**注意它只覆盖标定平面**：
        /// 有高度差、镜头畸变、相机倾斜都会表现为残差（见 ResidualRmsPx / ResidualByRadiusBands）。
        /// </summary>
        NinePoint = 1
    }

    /// <summary>
    /// 标定结果：跨插件传递的契约（由「标定」插件产出，由「坐标变换」等下游消费）。
    ///
    /// 为什么放 Core.Interfaces
    /// ---------
    /// ① 跨插件传递必须走契约程序集（插件之间不互相引用）；
    /// ② 契约层**零 HALCON 依赖**（与 CameraFrame / HubImageItem 同一纪律）：本类全是基元类型，
    ///    JSON 往返安全（要随方案落盘、也可能进全局变量）。
    ///
    /// 坐标与角度约定（全仓库统一，写死避免静默错位）
    /// ---------
    /// · 像素坐标 (Row, Col)：Row 向下、Col 向右（与 HALCON 一致）；
    /// · 机械坐标 (X, Y)：单位毫米；轴方向/镜像/旋转**由矩阵自然吸收**，消费方不做额外假设；
    /// · 角度单位：度（与模板匹配输出一致）；
    /// · 多点数组排布：[x0, y0, x1, y1, ...]（x 在前、y 在后，成对）。
    ///
    /// 矩阵语义
    /// ---------
    /// <see cref="Matrix"/> 为 6 元 [a11 a12 a21 a22 a31 a32]，映射 **(Row, Col) → (X, Y)**：
    ///   X = a11·Row + a12·Col + a31
    ///   Y = a21·Row + a22·Col + a32
    /// 与 HALCON hom_mat2d 的 6 元排布一致（Px=Row, Py=Col）；这样将来直接喂 affine_trans_point_2d 不需要任何转换。
    /// PixelScale 模式下：a11 = a22 = MmPerPixel，其余为 0。
    ///
    /// 失配自证（消费方据此报错，绝不静默用旧标定）
    /// ---------
    /// · <see cref="SourceImageWidth"/> / <see cref="SourceImageHeight"/>：标定时的图像尺寸——
    ///   当前图尺寸与它不符 = 换了分辨率/换了相机 → 旧标定失效；
    /// · <see cref="CameraSerial"/>：这份标定属于哪台相机（多相机下机器可读的身份）；
    /// · <see cref="CreatedAtUtc"/> / <see cref="SourceTag"/>：创建时间与来源（标定图文件名等），供日志与人工核对。
    /// </summary>
    public sealed class CalibrationTransform
    {
        /// <summary>标定类型</summary>
        public CalibrationKind Kind { get; set; }

        /// <summary>
        /// 像素当量（mm/px）。
        /// PixelScale 模式=直接算出；NinePoint 模式=由矩阵两基向量长度反解的均值。
        /// </summary>
        public double MmPerPixel { get; set; }

        /// <summary>仿射 6 元 [a11 a12 a21 a22 a31 a32]，语义见类注释（映射 (Row,Col)→(X,Y)）</summary>
        public double[] Matrix { get; set; } = new double[6];

        /// <summary>标定时的图像宽（失配检测用）</summary>
        public int SourceImageWidth { get; set; }

        /// <summary>标定时的图像高（失配检测用）</summary>
        public int SourceImageHeight { get; set; }

        /// <summary>来源标签（标定图文件名等，人工核对用）</summary>
        public string SourceTag { get; set; } = "";

        /// <summary>相机序列号（多相机的"这份标定属于哪台"；一期日志/人工核对，二期做自动失配校验）</summary>
        public string CameraSerial { get; set; } = "";

        /// <summary>标定创建时间（UTC）</summary>
        public DateTime CreatedAtUtc { get; set; }

        /// <summary>残差均方根（像素）：标定点拟合后，各点"实测 - 反算"的距离 RMS</summary>
        public double ResidualRmsPx { get; set; }

        /// <summary>最大单点残差（像素）</summary>
        public double MaxResidualPx { get; set; }

        /// <summary>
        /// 按**径向带**统计的平均残差（像素），长度 3：
        /// [0]=中心带（归一化半径 ≤0.4）、[1]=中间带（0.4~0.7）、[2]=边缘带（>0.7）。
        /// 用途：区分"随机噪声"与"镜头畸变"——残差随半径单调增大（边缘 ≫ 中心）就是畸变的典型信号。
        /// 无点的带为 NaN。
        /// </summary>
        public double[] ResidualByRadiusBands { get; set; } = new double[3];
    }
}
