using System;

namespace Core.Interfaces
{
    /// <summary>
    /// 网格标定（<see cref="CalibrationKind.Mesh"/>）的**共享映射数学**（纯基元运算，零 HALCON）。
    ///
    /// 为什么放在契约程序集：生产端（「标定」插件）与消费端（「坐标变换」等）必须用**同一份**
    /// 三角化与重心插值规则——两处各写一遍迟早分叉（分叉 = 同一份标定在两个插件里算出不同坐标，
    /// 这种静默不一致正是本仓库反复立规要杜绝的）。本类无任何可变状态，不涉及"两份静态状态"问题。
    ///
    /// 模型（分段仿射）：
    /// · 节点按行主序存 [Row, Col, X, Y]（组下标 = r·N + c）；
    /// · 每个单元格 (r,c) 拆成两个三角形：A=(p00,p01,p10)、B=(p01,p11,p10)（沿 p01–p10 对角线）；
    /// · 点落在哪个三角形，就用该三角形的仿射（重心坐标插值）换算——**节点处精确通过**；
    /// · 网格外（凸包外）不插值：**明确失败**，绝不外推。
    ///
    /// 与内参标定的关系：网格是"局部把畸变摊平"的工程近似（格越密越准），
    /// 正解是标定板内参标定（相机参数 + 畸变系数）；两者互不替代，见主文档 1.2。
    /// </summary>
    public static class CalibrationMesh
    {
        /// <summary>每个节点的数值个数：[Row, Col, X, Y]</summary>
        public const int ValuesPerNode = 4;

        /// <summary>判定"在三角形内"的容差（重心坐标，无量纲；边界点会被相邻三角形共同接受）</summary>
        private const double BarycentricEps = 1e-9;

        /// <summary>三角形退化的相对判据（|有向面积| 与顶点尺度平方的比值）</summary>
        private const double DegenerateAreaRatio = 1e-12;

        /// <summary>
        /// 网格是否可用（节点数组合法、两方向三角形都不退化）。
        /// 失败时给出中文原因（消费方直接上屏，不要自己造文案）。
        /// </summary>
        public static bool IsUsable(double[]? nodes, int n, out string? error)
        {
            error = null;
            if (nodes == null || nodes.Length == 0)
            {
                error = "网格标定数据缺失（MeshNodes 为空）：请重新运行「标定」步骤（网格标定模式）";
                return false;
            }
            if (n < 2)
            {
                error = $"网格规模非法（MeshSize={n}，至少 2）：标定数据可能已损坏，请重新运行「标定」步骤（网格标定模式）";
                return false;
            }
            // 用 long 比长度：MeshSize 被损坏成极大值时 int 乘法会回绕，可能"通过"长度校验随后下标溢出
            if ((long)n * n * ValuesPerNode != nodes.Length)
            {
                error = $"网格节点数与规模不符（MeshSize={n} 要求 {(long)n * n * ValuesPerNode} 个数，实际 {nodes.Length}）："
                    + "标定数据可能已损坏，请重新运行「标定」步骤（网格标定模式）";
                return false;
            }
            for (int i = 0; i < nodes.Length; i++)
            {
                if (double.IsNaN(nodes[i]) || double.IsInfinity(nodes[i]))
                {
                    error = $"网格节点含非法数值（NaN/Inf，第 {i / ValuesPerNode + 1} 个节点）：请检查标定表该行";
                    return false;
                }
            }

            // 两方向都要能定位：像素空间（正向）与机械空间（反向）的每个三角形都不许退化
            for (int r = 0; r + 1 < n; r++)
            {
                for (int c = 0; c + 1 < n; c++)
                {
                    int a = Idx(r, c, n), b = Idx(r, c + 1, n), d = Idx(r + 1, c, n), e = Idx(r + 1, c + 1, n);
                    if (!TriangleOk(nodes, a, b, d) || !TriangleOk(nodes, b, e, d))
                    {
                        error = $"第 {r + 1} 行第 {c + 1} 列的网格退化（像素点共线或重合）："
                            + "请检查该格四行的图像点是否取重/取错（网格标定要求格内不共线）";
                        return false;
                    }
                    if (!MachineTriangleOk(nodes, a, b, d) || !MachineTriangleOk(nodes, b, e, d))
                    {
                        error = $"第 {r + 1} 行第 {c + 1} 列的机械坐标三角形退化（反向换算不可用）：请检查该格四点的机械坐标";
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// 像素 (Row, Col) → 机械 (X, Y)：定位所在三角形后重心插值。
        /// 落在网格覆盖范围之外 → false（网格是插值，不外推）。
        /// </summary>
        public static bool TryMapPixelToXY(
            double[] nodes, int n, double row, double col, out double x, out double y, out string? error)
        {
            x = 0;
            y = 0;
            if (!IsUsable(nodes, n, out error)) return false;

            if (!TryLocate(nodes, n, row, col, byMachine: false, out int a, out int b, out int c, out double l0, out double l1, out double l2))
            {
                error = $"像素点 ({row:0.##}, {col:0.##}) 不在网格覆盖范围内：网格标定只在 {n}×{n} 节点构成的区域内有效"
                    + "（不外推）。请把该点移入标定范围，或扩大取点覆盖";
                return false;
            }

            x = l0 * nodes[a * ValuesPerNode + 2] + l1 * nodes[b * ValuesPerNode + 2] + l2 * nodes[c * ValuesPerNode + 2];
            y = l0 * nodes[a * ValuesPerNode + 3] + l1 * nodes[b * ValuesPerNode + 3] + l2 * nodes[c * ValuesPerNode + 3];
            return true;
        }

        /// <summary>机械 (X, Y) → 像素 (Row, Col)：与正向同一套三角化的反向换算（点必须在机械网格内）</summary>
        public static bool TryMapXYToPixel(
            double[] nodes, int n, double x, double y, out double row, out double col, out string? error)
        {
            row = 0;
            col = 0;
            if (!IsUsable(nodes, n, out error)) return false;

            if (!TryLocate(nodes, n, x, y, byMachine: true, out int a, out int b, out int c, out double l0, out double l1, out double l2))
            {
                error = $"机械坐标 ({x:0.###}, {y:0.###}) 不在网格覆盖范围内：网格标定只在 {n}×{n} 节点构成的区域内有效"
                    + "（不外推）。请确认目标点在标定视野内，或扩大取点覆盖";
                return false;
            }

            row = l0 * nodes[a * ValuesPerNode] + l1 * nodes[b * ValuesPerNode] + l2 * nodes[c * ValuesPerNode];
            col = l0 * nodes[a * ValuesPerNode + 1] + l1 * nodes[b * ValuesPerNode + 1] + l2 * nodes[c * ValuesPerNode + 1];
            return true;
        }

        /// <summary>
        /// 像素点处的**局部仿射**（所在三角形的雅可比）：machine = J·(row,col) + t。
        /// 角度换算与"局部当量"都用它——网格的 J 随位置变化（这正是它吸收畸变的方式）。
        /// </summary>
        public static bool TryLocalAffine(
            double[] nodes, int n, double row, double col,
            out double j11, out double j12, out double j21, out double j22, out double t1, out double t2, out string? error)
        {
            j11 = j12 = j21 = j22 = t1 = t2 = 0;
            if (!IsUsable(nodes, n, out error)) return false;

            if (!TryLocate(nodes, n, row, col, byMachine: false, out int a, out int b, out int c, out _, out _, out _))
            {
                error = $"像素点 ({row:0.##}, {col:0.##}) 不在网格覆盖范围内（局部仿射不可用）：请把该点移入标定范围";
                return false;
            }

            double r0 = nodes[a * ValuesPerNode], c0 = nodes[a * ValuesPerNode + 1];
            double r1 = nodes[b * ValuesPerNode], c1 = nodes[b * ValuesPerNode + 1];
            double r2 = nodes[c * ValuesPerNode], c2 = nodes[c * ValuesPerNode + 1];
            double x0 = nodes[a * ValuesPerNode + 2], y0 = nodes[a * ValuesPerNode + 3];
            double x1 = nodes[b * ValuesPerNode + 2], y1 = nodes[b * ValuesPerNode + 3];
            double x2 = nodes[c * ValuesPerNode + 2], y2 = nodes[c * ValuesPerNode + 3];

            double det = (r1 - r0) * (c2 - c0) - (c1 - c0) * (r2 - r0);
            if (Math.Abs(det) < double.Epsilon)
            {
                error = "网格三角形退化（无法算局部仿射）：请检查标定点是否取重/共线";
                return false;
            }

            // machine = A·pixel + t（由三点定出的仿射；两列分别是 ∂m/∂Row 与 ∂m/∂Col）
            j11 = ((x1 - x0) * (c2 - c0) - (x2 - x0) * (c1 - c0)) / det;
            j12 = ((x2 - x0) * (r1 - r0) - (x1 - x0) * (r2 - r0)) / det;
            j21 = ((y1 - y0) * (c2 - c0) - (y2 - y0) * (c1 - c0)) / det;
            j22 = ((y2 - y0) * (r1 - r0) - (y1 - y0) * (r2 - r0)) / det;
            t1 = x0 - j11 * r0 - j12 * c0;
            t2 = y0 - j21 * r0 - j22 * c0;
            return true;
        }

        /// <summary>
        /// 全网格"平均当量"（mm/px）：各三角形 sqrt(|det J|)（两轴几何平均）的**中位数**。
        /// 用来填 <see cref="CalibrationTransform.MmPerPixel"/>——网格模式下当量随位置变化，
        /// 中位数对局部畸变不敏感；顺带让"只吃当量"的下游（卡尺/Blob）也能用网格标定。
        /// </summary>
        public static bool TryMeanMmPerPixel(double[] nodes, int n, out double mmPerPixel, out string? error)
        {
            mmPerPixel = 0;
            if (!IsUsable(nodes, n, out error)) return false;

            int triCount = 2 * (n - 1) * (n - 1);
            var scales = new double[triCount];
            int k = 0;
            for (int r = 0; r + 1 < n; r++)
            {
                for (int c = 0; c + 1 < n; c++)
                {
                    int p00 = Idx(r, c, n), p01 = Idx(r, c + 1, n), p10 = Idx(r + 1, c, n), p11 = Idx(r + 1, c + 1, n);
                    scales[k++] = TriangleScale(nodes, p00, p01, p10);
                    scales[k++] = TriangleScale(nodes, p01, p11, p10);
                }
            }
            Array.Sort(scales);
            mmPerPixel = triCount % 2 == 1
                ? scales[triCount / 2]
                : 0.5 * (scales[triCount / 2 - 1] + scales[triCount / 2]);
            return true;
        }

        #region 内部

        /// <summary>组下标：节点 (r,c) 在 nodes 中的起始下标</summary>
        private static int Idx(int r, int c, int n) => r * n + c;

        /// <summary>像素空间三角形是否非退化</summary>
        private static bool TriangleOk(double[] nodes, int a, int b, int c)
        {
            double area = Area(
                nodes[a * ValuesPerNode], nodes[a * ValuesPerNode + 1],
                nodes[b * ValuesPerNode], nodes[b * ValuesPerNode + 1],
                nodes[c * ValuesPerNode], nodes[c * ValuesPerNode + 1]);
            double scale = Edge2(nodes[a * ValuesPerNode], nodes[a * ValuesPerNode + 1], nodes[b * ValuesPerNode], nodes[b * ValuesPerNode + 1])
                + Edge2(nodes[a * ValuesPerNode], nodes[a * ValuesPerNode + 1], nodes[c * ValuesPerNode], nodes[c * ValuesPerNode + 1])
                + Edge2(nodes[b * ValuesPerNode], nodes[b * ValuesPerNode + 1], nodes[c * ValuesPerNode], nodes[c * ValuesPerNode + 1]);
            return Math.Abs(area) > DegenerateAreaRatio * Math.Max(scale, 1e-12);
        }

        /// <summary>机械空间三角形是否非退化（反向换算的定位空间）</summary>
        private static bool MachineTriangleOk(double[] nodes, int a, int b, int c)
        {
            double area = Area(
                nodes[a * ValuesPerNode + 2], nodes[a * ValuesPerNode + 3],
                nodes[b * ValuesPerNode + 2], nodes[b * ValuesPerNode + 3],
                nodes[c * ValuesPerNode + 2], nodes[c * ValuesPerNode + 3]);
            double scale = Edge2(nodes[a * ValuesPerNode + 2], nodes[a * ValuesPerNode + 3], nodes[b * ValuesPerNode + 2], nodes[b * ValuesPerNode + 3])
                + Edge2(nodes[a * ValuesPerNode + 2], nodes[a * ValuesPerNode + 3], nodes[c * ValuesPerNode + 2], nodes[c * ValuesPerNode + 3])
                + Edge2(nodes[b * ValuesPerNode + 2], nodes[b * ValuesPerNode + 3], nodes[c * ValuesPerNode + 2], nodes[c * ValuesPerNode + 3]);
            return Math.Abs(area) > DegenerateAreaRatio * Math.Max(scale, 1e-12);
        }

        /// <summary>三角形有向面积（顶点顺序敏感）</summary>
        private static double Area(double x0, double y0, double x1, double y1, double x2, double y2)
            => 0.5 * ((x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0));

        /// <summary>两点距离平方</summary>
        private static double Edge2(double x0, double y0, double x1, double y1)
        {
            double dx = x1 - x0, dy = y1 - y0;
            return dx * dx + dy * dy;
        }

        /// <summary>三角形 mm/px 尺度：两轴的几何平均 sqrt(|det J|)</summary>
        private static double TriangleScale(double[] nodes, int a, int b, int c)
        {
            double r0 = nodes[a * ValuesPerNode], c0 = nodes[a * ValuesPerNode + 1];
            double r1 = nodes[b * ValuesPerNode], c1 = nodes[b * ValuesPerNode + 1];
            double r2 = nodes[c * ValuesPerNode], c2 = nodes[c * ValuesPerNode + 1];
            double x0 = nodes[a * ValuesPerNode + 2], y0 = nodes[a * ValuesPerNode + 3];
            double x1 = nodes[b * ValuesPerNode + 2], y1 = nodes[b * ValuesPerNode + 3];
            double x2 = nodes[c * ValuesPerNode + 2], y2 = nodes[c * ValuesPerNode + 3];

            double det = (r1 - r0) * (c2 - c0) - (c1 - c0) * (r2 - r0);
            if (Math.Abs(det) < double.Epsilon) return double.NaN;
            double j11 = ((x1 - x0) * (c2 - c0) - (x2 - x0) * (c1 - c0)) / det;
            double j12 = ((x2 - x0) * (r1 - r0) - (x1 - x0) * (r2 - r0)) / det;
            double j21 = ((y1 - y0) * (c2 - c0) - (y2 - y0) * (c1 - c0)) / det;
            double j22 = ((y2 - y0) * (r1 - r0) - (y1 - y0) * (r2 - r0)) / det;
            return Math.Sqrt(Math.Abs(j11 * j22 - j12 * j21));
        }

        /// <summary>
        /// 定位：返回包含该点的三角形三个节点下标与重心坐标（λ0,λ1,λ2）。
        /// byMachine=false → 在像素空间定位（正向换算）；true → 在机械空间定位（反向换算）。
        /// </summary>
        private static bool TryLocate(
            double[] nodes, int n, double u, double v, bool byMachine,
            out int a, out int b, out int c, out double l0, out double l1, out double l2)
        {
            int off = byMachine ? 2 : 0;   // 定位空间在 [Row,Col,X,Y] 里的起点
            for (int r = 0; r + 1 < n; r++)
            {
                for (int cc = 0; cc + 1 < n; cc++)
                {
                    int p00 = Idx(r, cc, n), p01 = Idx(r, cc + 1, n), p10 = Idx(r + 1, cc, n), p11 = Idx(r + 1, cc + 1, n);

                    if (Barycentric(nodes, p00, p01, p10, off, u, v, out l0, out l1, out l2))
                    {
                        a = p00; b = p01; c = p10;
                        return true;
                    }
                    if (Barycentric(nodes, p01, p11, p10, off, u, v, out l0, out l1, out l2))
                    {
                        a = p01; b = p11; c = p10;
                        return true;
                    }
                }
            }
            a = b = c = 0;
            l0 = l1 = l2 = 0;
            return false;
        }

        /// <summary>重心坐标（源空间 = off 指定的两轴）；三点必须在坐标内按逆/顺时针一致排列，否则面积为负——本函数用带号面积统一处理</summary>
        private static bool Barycentric(
            double[] nodes, int a, int b, int c, int off, double u, double v,
            out double l0, out double l1, out double l2)
        {
            double u0 = nodes[a * ValuesPerNode + off], v0 = nodes[a * ValuesPerNode + off + 1];
            double u1 = nodes[b * ValuesPerNode + off], v1 = nodes[b * ValuesPerNode + off + 1];
            double u2 = nodes[c * ValuesPerNode + off], v2 = nodes[c * ValuesPerNode + off + 1];

            double det = (v1 - v2) * (u0 - u2) + (u2 - u1) * (v0 - v2);
            if (Math.Abs(det) < double.Epsilon)
            {
                l0 = l1 = l2 = 0;
                return false;
            }

            l0 = ((v1 - v2) * (u - u2) + (u2 - u1) * (v - v2)) / det;
            l1 = ((v2 - v0) * (u - u2) + (u0 - u2) * (v - v2)) / det;
            l2 = 1 - l0 - l1;

            double tol = BarycentricEps;
            return l0 >= -tol && l1 >= -tol && l2 >= -tol;
        }

        #endregion
    }
}
