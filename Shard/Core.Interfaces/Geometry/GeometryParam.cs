using System;

namespace Core.Interfaces
{
    /// <summary>
    /// 几何基元种类。端口传的是同一类型 <see cref="GeometryParam"/>，靠 <see cref="GeometryParam.Kind"/> 分派。
    /// </summary>
    public enum GeometryKind
    {
        /// <summary>点：Row / Col</summary>
        Point = 0,

        /// <summary>直线：Row / Col（线上一点）+ Phi（**方向角**，度，[0,180) 归一化）</summary>
        Line = 1,

        /// <summary>圆：Row / Col（圆心）+ Radius</summary>
        Circle = 2,

        /// <summary>椭圆：Row / Col（中心）+ Phi（长轴方向，度，0~180）+ Radius（长半轴）/ Radius2（短半轴）</summary>
        Ellipse = 3,

        /// <summary>轮廓：Points[]（[r0,c0,r1,c1,...]）；Row/Col 为包围盒中心摘要</summary>
        Contour = 4
    }

    /// <summary>
    /// 几何基元：跨插件传递的一等数据（由「几何查找」等产出，由「几何计算」等消费）。
    ///
    /// 设计要点（与《几何查找与几何计算·方案说明书》一致）：
    /// · **单类 + Kind 分派**（不做继承体系）：JSON 往返安全、端口连线类型检查只认一个类型。
    ///   代价是"线端口能连到圆输入"编译期不报，由消费方按 Kind 运行期校验并明确失败。
    /// · **不可变**：属性 init-only，一律用工厂方法构造；下游变换产新对象，不就地改（防别名污染上游）。
    /// · **契约层零 HALCON 依赖**：全是基元类型（同 CalibrationTransform）。
    /// · **坐标系**：图像像素系 (Row, Col)；**角度单位度**、Phi 归一化 [0,180)。基元**不乘**像素当量。
    /// </summary>
    public sealed class GeometryParam
    {
        public GeometryKind Kind { get; init; }

        /// <summary>基准点：点 / 圆心 / 线中点 / 椭圆中心</summary>
        public double Row { get; init; }
        public double Col { get; init; }

        /// <summary>度，[0,180) 归一化（线的**方向角** / 椭圆长轴方向）</summary>
        public double Phi { get; init; }

        /// <summary>圆半径 / 椭圆长半轴</summary>
        public double Radius { get; init; }

        /// <summary>椭圆短半轴</summary>
        public double Radius2 { get; init; }

        /// <summary>轮廓/多点（[r0,c0,r1,c1,...]）；非轮廓基元为 null</summary>
        public double[]? Points { get; init; }

        /// <summary>供 JSON 反序列化使用；正常构造请走工厂方法。</summary>
        public GeometryParam()
        {
        }

        public static GeometryParam Point(double row, double col)
            => new() { Kind = GeometryKind.Point, Row = row, Col = col };

        public static GeometryParam Line(double row, double col, double phi)
            => new() { Kind = GeometryKind.Line, Row = row, Col = col, Phi = NormalizePhi(phi) };

        public static GeometryParam Circle(double row, double col, double radius)
            => new() { Kind = GeometryKind.Circle, Row = row, Col = col, Radius = radius };

        public static GeometryParam Ellipse(double row, double col, double phi, double major, double minor)
            => new() { Kind = GeometryKind.Ellipse, Row = row, Col = col, Phi = NormalizePhi(phi), Radius = major, Radius2 = minor };

        public static GeometryParam Contour(double[] points, double row = 0, double col = 0)
            => new() { Kind = GeometryKind.Contour, Points = points ?? Array.Empty<double>(), Row = row, Col = col };

        /// <summary>
        /// 把角度归一化到 [0,180)：线的"方向"与"反方向"等价（法线无向），
        /// +180/-180 与 0 同义——与卡尺 Angle 的口径一致，消灭下游补角差 180° 的静默错。
        /// </summary>
        public static double NormalizePhi(double deg)
        {
            if (!double.IsFinite(deg)) return deg;
            var d = deg % 180.0;
            if (d < 0) d += 180.0;
            // 浮点下 -1e-15 + 180 会等于 180；必须再折一次，否则越出 [0,180)
            if (d >= 180.0) d -= 180.0;
            // 消掉 -0.0，保证契约里的 0 是正零
            return d == 0.0 ? 0.0 : d;
        }

        public override string ToString() => Kind switch
        {
            GeometryKind.Point => $"点({Row:F2},{Col:F2})",
            GeometryKind.Line => $"线(过 {Row:F2},{Col:F2} φ={Phi:F2}°)",
            GeometryKind.Circle => $"圆({Row:F2},{Col:F2} r={Radius:F2})",
            GeometryKind.Ellipse => $"椭圆({Row:F2},{Col:F2} {Radius:F2}×{Radius2:F2} φ={Phi:F2}°)",
            GeometryKind.Contour => $"轮廓({(Points?.Length ?? 0) / 2} 点)",
            _ => Kind.ToString()
        };
    }
}
