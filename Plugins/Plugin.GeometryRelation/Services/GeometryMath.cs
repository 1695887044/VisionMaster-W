using Core.Interfaces;
using System;

namespace Plugin.GeometryRelation.Services
{
    /// <summary>
    /// 纯托管几何运算（不调 HALCON，可被断言完整覆盖）。
    ///
    /// 约定（与《几何查找与几何计算·方案说明书》一致）：
    /// · 点 = (Row, Col)，Row 向下、Col 向右；
    /// · 直线的方向角 Phi（度，[0,180)）：方向单位向量 (dRow, dCol) = (sin φ, cos φ)——
    ///   φ=0 → 沿 +Col（向右）；φ=90 → 沿 +Row（向下）；
    /// · **无向线夹角** 返回 [0,90]（两条无向直线之间的真实夹角；不做 [0,180) 的射线取向口径）。
    /// </summary>
    public static class GeometryMath
    {
        private const double DegToRad = Math.PI / 180.0;

        /// <summary>方向角（度）→ 方向单位向量 (dRow, dCol)</summary>
        public static (double dRow, double dCol) Direction(double phiDeg)
        {
            var rad = phiDeg * DegToRad;
            return (Math.Sin(rad), Math.Cos(rad));
        }

        /// <summary>方向角（度）→ [0,180) 归一化（与 GeometryParam.NormalizePhi 同口径）</summary>
        public static double NormalizePhi(double deg) => GeometryParam.NormalizePhi(deg);

        /// <summary>点-点距离</summary>
        public static double DistancePointPoint(GeometryParam a, GeometryParam b)
        {
            var dr = a.Row - b.Row;
            var dc = a.Col - b.Col;
            return Math.Sqrt(dr * dr + dc * dc);
        }

        /// <summary>点到直线距离（直线由 (Row,Col) 与方向角 Phi 决定）</summary>
        public static double DistancePointLine(GeometryParam point, GeometryParam line)
        {
            var (dr, dc) = Direction(line.Phi);
            var vr = point.Row - line.Row;
            var vc = point.Col - line.Col;
            // |v × d| = |vr·dc − vc·dr|
            return Math.Abs(vr * dc - vc * dr);
        }

        /// <summary>两无向直线的夹角，[0,90] 度</summary>
        public static double AngleBetweenLines(GeometryParam l1, GeometryParam l2)
        {
            var d = Math.Abs(l1.Phi - l2.Phi) % 180.0;
            return d > 90.0 ? 180.0 - d : d;
        }

        /// <summary>两线是否平行（夹角 ≤ 容差）</summary>
        public static bool AreParallel(GeometryParam l1, GeometryParam l2, double angleToleranceDeg)
            => AngleBetweenLines(l1, l2) <= angleToleranceDeg;

        /// <summary>两线是否垂直（|夹角 − 90| ≤ 容差）</summary>
        public static bool ArePerpendicular(GeometryParam l1, GeometryParam l2, double angleToleranceDeg)
            => Math.Abs(AngleBetweenLines(l1, l2) - 90.0) <= angleToleranceDeg;

        /// <summary>线-线求交；近平行（|det| 过小）返回 false（明确失败，不硬算）</summary>
        public static bool TryIntersectLines(GeometryParam l1, GeometryParam l2, out double row, out double col)
        {
            var (d1r, d1c) = Direction(l1.Phi);
            var (d2r, d2c) = Direction(l2.Phi);
            var det = d1r * d2c - d1c * d2r;
            if (Math.Abs(det) < 1e-9) { row = col = 0; return false; }

            var pr = l2.Row - l1.Row;
            var pc = l2.Col - l1.Col;
            var t = (pr * d2c - pc * d2r) / det;
            row = l1.Row + t * d1r;
            col = l1.Col + t * d1c;
            return true;
        }

        /// <summary>
        /// 线-圆求交。count = 交点数（0=不相交，1=相切，2=两交点）；
        /// 返回第一个交点（按参数 t 升序）。返回 false 表示无交点。
        /// </summary>
        public static bool TryIntersectLineCircle(GeometryParam line, GeometryParam circle,
            out double row, out double col, out int count)
        {
            var (dr, dc) = Direction(line.Phi);
            var fr = line.Row - circle.Row;
            var fc = line.Col - circle.Col;
            // |f + t·d|² = R²  →  t² + 2(f·d)t + (|f|²−R²) = 0
            var b = 2 * (fr * dr + fc * dc);
            var c = fr * fr + fc * fc - circle.Radius * circle.Radius;
            var disc = b * b - 4 * c;
            if (disc < 0) { row = col = 0; count = 0; return false; }

            var sqrt = Math.Sqrt(disc);
            var t = (-b - sqrt) / 2;   // 取较小 t
            row = line.Row + t * dr;
            col = line.Col + t * dc;
            count = sqrt < 1e-9 ? 1 : 2;
            return true;
        }

        /// <summary>
        /// 圆-圆求交。同心/相离/内含返回 false；返回第一个交点。
        /// </summary>
        public static bool TryIntersectCircles(GeometryParam c1, GeometryParam c2,
            out double row, out double col, out int count)
        {
            var dr = c2.Row - c1.Row;
            var dc = c2.Col - c1.Col;
            var d = Math.Sqrt(dr * dr + dc * dc);
            row = col = 0;
            count = 0;
            if (d < 1e-9) return false;                                   // 同心
            if (d > c1.Radius + c2.Radius + 1e-9) return false;           // 相离
            if (d < Math.Abs(c1.Radius - c2.Radius) - 1e-9) return false; // 内含

            var a = (c1.Radius * c1.Radius - c2.Radius * c2.Radius + d * d) / (2 * d);
            var h2 = c1.Radius * c1.Radius - a * a;
            var h = h2 > 0 ? Math.Sqrt(h2) : 0;
            var pr = c1.Row + a * dr / d;
            var pc = c1.Col + a * dc / d;
            // 交点 = P ± h·perp(dir)
            row = pr + h * (-dc / d);
            col = pc + h * (dr / d);
            count = h < 1e-9 ? 1 : 2;
            return true;
        }

        /// <summary>点到直线的垂足</summary>
        public static (double row, double col) PerpendicularFoot(GeometryParam point, GeometryParam line)
        {
            var (dr, dc) = Direction(line.Phi);
            var vr = point.Row - line.Row;
            var vc = point.Col - line.Col;
            var t = vr * dr + vc * dc;    // 投影长度
            return (line.Row + t * dr, line.Col + t * dc);
        }

        /// <summary>过两点构造直线（锚点=中点，方向角由两点连线确定）</summary>
        public static GeometryParam LineThroughTwoPoints(GeometryParam a, GeometryParam b)
        {
            var row = (a.Row + b.Row) / 2.0;
            var col = (a.Col + b.Col) / 2.0;
            var phi = Math.Atan2(b.Row - a.Row, b.Col - a.Col) / DegToRad;  // atan2(dRow,dCol)
            return GeometryParam.Line(row, col, phi);
        }

        #region 残差（P1-B A2）

        /// <summary>
        /// 单点到基元的**像素距离**（残差）。
        /// · Line：点到直线距离 |v × d|（d = (sin φ, cos φ)，行主口径）；
        /// · Circle：| 点到圆心距离 − R |；
        /// · Ellipse：把点投到长轴坐标系后代入椭圆方程，用"代数残差 × 短半轴"近似像素距离——
        ///   这是**近似值**（法向距离与径向量不成比例），Help 与断言都按近似口径写，不当精确值用；
        /// · Point：点-点距离；
        /// · 其它（Contour 等）：返回 NaN，**由调用方明确失败**，不许静默给 0。
        /// </summary>
        public static double PointResidual(GeometryParam geometry, double row, double col)
        {
            switch (geometry.Kind)
            {
                case GeometryKind.Point:
                    return Math.Sqrt((row - geometry.Row) * (row - geometry.Row)
                                   + (col - geometry.Col) * (col - geometry.Col));

                case GeometryKind.Line:
                    return DistancePointLine(GeometryParam.Point(row, col), geometry);

                case GeometryKind.Circle:
                {
                    var dr = row - geometry.Row;
                    var dc = col - geometry.Col;
                    return Math.Abs(Math.Sqrt(dr * dr + dc * dc) - geometry.Radius);
                }

                case GeometryKind.Ellipse:
                {
                    var phi = geometry.Phi * DegToRad;
                    var dr = Math.Sin(phi);
                    var dc = Math.Cos(phi);
                    var pr = Math.Cos(phi);
                    var pc = -Math.Sin(phi);
                    var vr = row - geometry.Row;
                    var vc = col - geometry.Col;
                    var u = vr * dr + vc * dc;      // 长轴分量
                    var v = vr * pr + vc * pc;      // 短轴分量
                    var major = Math.Max(geometry.Radius, 1e-9);
                    var minor = Math.Max(geometry.Radius2, 1e-9);
                    var norm = Math.Sqrt((u / major) * (u / major) + (v / minor) * (v / minor));
                    return Math.Abs(norm - 1.0) * minor;
                }

                default:
                    return double.NaN;
            }
        }

        /// <summary>
        /// 一组点对基元的残差统计：RMS（像素）、最大（像素）、参与点数。
        /// points 形如 [r0,c0,r1,c1,...]；长度奇数或为 0 时 count=0 且 rms/max 为 NaN。
        /// 任一元素非有限值 → 跳过该点并在 skipped 里计数（不静默当成 0 参与平均）。
        /// </summary>
        public static (double Rms, double Max, int Count, int Skipped) ResidualStats(
            GeometryParam geometry, double[]? points)
        {
            if (points == null || points.Length < 2 || (points.Length & 1) == 1)
                return (double.NaN, double.NaN, 0, 0);

            var count = points.Length / 2;
            var sumSq = 0.0;
            var max = 0.0;
            var used = 0;
            var skipped = 0;

            for (var i = 0; i < count; i++)
            {
                var row = points[2 * i];
                var col = points[2 * i + 1];
                if (double.IsNaN(row) || double.IsInfinity(row)
                    || double.IsNaN(col) || double.IsInfinity(col))
                {
                    skipped++;
                    continue;
                }

                var d = PointResidual(geometry, row, col);
                if (double.IsNaN(d) || double.IsInfinity(d))
                {
                    skipped++;
                    continue;
                }

                sumSq += d * d;
                if (d > max) max = d;
                used++;
            }

            if (used == 0) return (double.NaN, double.NaN, 0, skipped);
            return (Math.Sqrt(sumSq / used), max, used, skipped);
        }

        #endregion

        /// <summary>三点构造外接圆；三点共线返回 false（明确失败）</summary>
        public static bool TryCircleThroughThreePoints(GeometryParam a, GeometryParam b, GeometryParam c,
            out GeometryParam circle)
        {
            circle = null!;
            var d = 2 * (a.Row * (b.Col - c.Col) + b.Row * (c.Col - a.Col) + c.Row * (a.Col - b.Col));
            if (Math.Abs(d) < 1e-9) return false;   // 共线

            var a2 = a.Row * a.Row + a.Col * a.Col;
            var b2 = b.Row * b.Row + b.Col * b.Col;
            var c2 = c.Row * c.Row + c.Col * c.Col;
            var ux = (a2 * (b.Col - c.Col) + b2 * (c.Col - a.Col) + c2 * (a.Col - b.Col)) / d;
            var uy = (a2 * (c.Row - b.Row) + b2 * (a.Row - c.Row) + c2 * (b.Row - a.Row)) / d;
            var r = Math.Sqrt((ux - a.Row) * (ux - a.Row) + (uy - a.Col) * (uy - a.Col));
            circle = GeometryParam.Circle(ux, uy, r);
            return true;
        }
    }
}
