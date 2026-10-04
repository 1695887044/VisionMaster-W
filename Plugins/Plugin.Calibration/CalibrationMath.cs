using System;

namespace Plugin.Calibration
{
    /// <summary>
    /// 标定算法核心（**纯托管**：不引用 HALCON 类型、不依赖宿主）。
    ///
    /// 为什么与界面/执行分离
    /// ---------
    /// ① 纯函数可以被断言完整覆盖（合成数据 → 求解 → 反算），不依赖视觉引擎；
    /// ② 无 HALCON 的机器上插件加载不受影响（不碰引擎就不可能在加载期炸）。
    ///
    /// 坐标与矩阵语义（与 Core.Interfaces.CalibrationTransform 的约定一致）
    /// ---------
    /// · 像素 (Row, Col)、机械 (X, Y)、多点数组 [x0,y0,x1,y1,...]；
    /// · 矩阵 6 元 [a11 a12 a21 a22 a31 a32]：X = a11·Row + a12·Col + a31；Y = a21·Row + a22·Col + a32。
    ///
    /// 数值做法（为什么先归一化再解）
    /// ---------
    /// 直接对 (Row≈几千, Col≈几千) 的法方程求解，条件数很差（量纲跨度大且原点远离点集中心）。
    /// 这里先减去质心、除以尺度（最大半径）把点压到单位圆内再解正规方程，
    /// 最后把 6 个系数还原回原坐标系——精度与稳定性都靠这一步，也顺带得到一个免量纲的共线判据。
    /// </summary>
    public static class CalibrationMath
    {
        /// <summary>
        /// 共线判定阈值：归一化法方程 det / trace³ 低于它视为退化（点近似共线 → 无法定出方向）。
        /// 参考量级：规规矩矩的 3 点三角形约 0.03；接近一条直线的点集会迅速掉到 1e-6 以下。
        /// </summary>
        internal const double CollinearDetRatioMin = 1e-4;

        /// <summary>径向带内边界（归一化半径 0.4）</summary>
        private const double BandInner = 0.4;

        /// <summary>径向带外边界（归一化半径 0.7）</summary>
        private const double BandOuter = 0.7;

        /// <summary>残差径向带数量（中心 / 中间 / 边缘）</summary>
        public const int BandCount = 3;

        #region 像素当量

        /// <summary>
        /// 两点像素距离 + 已知长度 → 像素当量（mm/px）。
        /// </summary>
        public static bool TryPixelScale(
            double row1, double col1, double row2, double col2, double knownLengthMm,
            out double mmPerPixel, out double pixelDistance, out string? error)
        {
            mmPerPixel = 0;
            pixelDistance = 0;
            error = null;

            if (!IsFinite(row1) || !IsFinite(col1) || !IsFinite(row2) || !IsFinite(col2))
            {
                error = "两点坐标含非法数值（NaN/Inf）——请检查表格或画布标记";
                return false;
            }

            if (!IsFinite(knownLengthMm) || knownLengthMm <= 0)
            {
                error = $"已知长度必须为正数（当前 {knownLengthMm}）";
                return false;
            }

            pixelDistance = Math.Sqrt((row2 - row1) * (row2 - row1) + (col2 - col1) * (col2 - col1));
            if (pixelDistance < 1e-6)
            {
                error = "两点像素距离为 0：请在画布上把 A/B 两个标记拖到标定物的两端（或手工填写表格里的 Row/Col）";
                return false;
            }

            mmPerPixel = knownLengthMm / pixelDistance;
            return true;
        }

        #endregion

        #region 九点（N×N）仿射

        /// <summary>
        /// 由 N 组点求仿射矩阵（最小二乘）。
        /// </summary>
        /// <param name="rows">图像点 Row 数组</param>
        /// <param name="cols">图像点 Col 数组</param>
        /// <param name="xs">机械坐标 X 数组（与图像点一一对应）</param>
        /// <param name="ys">机械坐标 Y 数组</param>
        /// <param name="matrix">输出 6 元矩阵 [a11 a12 a21 a22 a31 a32]</param>
        /// <param name="rmsPx">残差均方根（像素）</param>
        /// <param name="maxPx">最大单点残差（像素）</param>
        /// <param name="bandResiduals">径向带平均残差（长度 <see cref="BandCount"/>，无点的带为 NaN）</param>
        /// <param name="error">失败原因（中文，可直接进错误信息）</param>
        public static bool TrySolveAffine(
            double[] rows, double[] cols, double[] xs, double[] ys,
            out double[]? matrix, out double rmsPx, out double maxPx, out double[]? bandResiduals, out string? error)
        {
            matrix = null;
            rmsPx = 0;
            maxPx = 0;
            bandResiduals = null;
            error = null;

            if (rows == null || cols == null || xs == null || ys == null)
            {
                error = "标定点数组为空（内部取值错误）";
                return false;
            }

            int n = rows.Length;
            if (cols.Length != n || xs.Length != n || ys.Length != n)
            {
                error = "标定点数组长度不一致（内部取值错误）";
                return false;
            }

            if (n < 3)
            {
                error = $"有效标定点不足：至少需要 3 组，当前 {n} 组（默认 3×3=9 点，覆盖视野四角+中心；"
                      + "点数只是采样密度，可按需扩到 4×4/5×5）";
                return false;
            }

            for (int i = 0; i < n; i++)
            {
                if (!IsFinite(rows[i]) || !IsFinite(cols[i]) || !IsFinite(xs[i]) || !IsFinite(ys[i]))
                {
                    error = $"第 {i + 1} 组点含非法数值（NaN/Inf）——请检查该行的填写";
                    return false;
                }
            }

            // 质心 + 尺度（归一化用；尺度=最大半径，同时用作共线判据的基准）
            double cr = 0, cc = 0;
            for (int i = 0; i < n; i++) { cr += rows[i]; cc += cols[i]; }
            cr /= n; cc /= n;

            double scale = 0;
            for (int i = 0; i < n; i++)
            {
                double d = Math.Sqrt((rows[i] - cr) * (rows[i] - cr) + (cols[i] - cc) * (cols[i] - cc));
                if (d > scale) scale = d;
            }
            if (scale < 1e-9)
            {
                error = "标定点在图像上全部重合（尺度为 0）——请把点铺开成 N×N 网格";
                return false;
            }

            // 归一化法方程：A 的行 = [u, v, 1]
            double sUU = 0, sUV = 0, sVV = 0, sU = 0, sV = 0;
            double bxU = 0, bxV = 0, bx1 = 0;   // Σu·x, Σv·x, Σx
            double byU = 0, byV = 0, by1 = 0;   // Σu·y, Σv·y, Σy
            for (int i = 0; i < n; i++)
            {
                double u = (rows[i] - cr) / scale;
                double v = (cols[i] - cc) / scale;
                sUU += u * u; sUV += u * v; sVV += v * v; sU += u; sV += v;
                bxU += u * xs[i]; bxV += v * xs[i]; bx1 += xs[i];
                byU += u * ys[i]; byV += v * ys[i]; by1 += ys[i];
            }

            // M = AᵀA（对称 3×3）
            double m00 = sUU, m01 = sUV, m02 = sU;
            double m11 = sVV, m12 = sV;
            double m22 = n;

            double det = m00 * (m11 * m22 - m12 * m12)
                       - m01 * (m01 * m22 - m12 * m02)
                       + m02 * (m01 * m12 - m11 * m02);
            double trace = m00 + m11 + m22;
            if (det <= CollinearDetRatioMin * trace * trace * trace)
            {
                error = "标定点近似共线（无法定出平面方向）：请把点铺开成 N×N 网格，"
                      + "覆盖视野四角与中心，别让点落在一条直线上";
                return false;
            }

            // 克莱姆法则解两条独立的 3 元方程（X 与 Y 共用同一系数矩阵）
            if (!Solve3(m00, m01, m02, m11, m12, m22, bxU, bxV, bx1, det, out double a1, out double b1, out double c1)
             || !Solve3(m00, m01, m02, m11, m12, m22, byU, byV, by1, det, out double a2, out double b2, out double c2))
            {
                error = "求解线性方程组失败（标定点退化）";
                return false;
            }

            // 还原到原坐标系：X = a1·u + b1·v + c1，其中 u=(Row-cr)/scale、v=(Col-cc)/scale
            matrix = new[]
            {
                a1 / scale,                            // a11
                b1 / scale,                            // a12
                a2 / scale,                            // a21
                b2 / scale,                            // a22
                c1 - a1 * cr / scale - b1 * cc / scale,// a31
                c2 - a2 * cr / scale - b2 * cc / scale // a32
            };

            // 残差与径向带统计。
            // 单位换算别忘了：偏差算出来是机械坐标（mm），而对外契约全是像素
            //（ResidualRmsPx、阈值默认 1px、带诊断 0.5px）——按当量除一次 mm→px。
            // 缺这一步，"0.5mm 的错点 ≈ 25px"会被当成 0.5 的"像素"残差悄悄放行。
            double mmPerPx = DeriveMmPerPixel(matrix, out _);
            if (!(mmPerPx > 0))
            {
                error = "求解得到的矩阵尺度为 0（标定点退化）——请检查标定表";
                return false;
            }

            bandResiduals = new double[BandCount];
            var bandSum = new double[BandCount];
            var bandCount = new int[BandCount];
            double sumSq = 0;
            for (int i = 0; i < n; i++)
            {
                double ex = matrix[0] * rows[i] + matrix[1] * cols[i] + matrix[4];
                double ey = matrix[2] * rows[i] + matrix[3] * cols[i] + matrix[5];
                double d = Math.Sqrt((ex - xs[i]) * (ex - xs[i]) + (ey - ys[i]) * (ey - ys[i])) / mmPerPx;

                sumSq += d * d;
                if (d > maxPx) maxPx = d;

                double r = Math.Sqrt((rows[i] - cr) * (rows[i] - cr) + (cols[i] - cc) * (cols[i] - cc)) / scale;
                int band = r <= BandInner ? 0 : r <= BandOuter ? 1 : 2;
                bandSum[band] += d;
                bandCount[band]++;
            }

            for (int b = 0; b < BandCount; b++)
                bandResiduals[b] = bandCount[b] > 0 ? bandSum[b] / bandCount[b] : double.NaN;

            rmsPx = Math.Sqrt(sumSq / n);
            return true;
        }

        /// <summary>
        /// 由矩阵反解像素当量与各向异性：两基向量的长度即"沿 Row / 沿 Col 走 1 像素各是多少毫米"。
        /// </summary>
        /// <param name="anisotropyPercent">两向当量差的百分比（各向异性，顺时针提示而非失败）</param>
        public static double DeriveMmPerPixel(double[] matrix, out double anisotropyPercent)
        {
            anisotropyPercent = 0;
            if (matrix == null || matrix.Length < 6)
                return 0;

            double lenRow = Math.Sqrt(matrix[0] * matrix[0] + matrix[2] * matrix[2]); // 沿 Row 方向
            double lenCol = Math.Sqrt(matrix[1] * matrix[1] + matrix[3] * matrix[3]); // 沿 Col 方向
            double mean = (lenRow + lenCol) / 2;
            if (mean > 0 && lenRow > 0 && lenCol > 0)
                anisotropyPercent = Math.Abs(lenRow - lenCol) / mean * 100.0;
            return mean;
        }

        /// <summary>
        /// 径向带残差 → 诊断提示（空串 = 形态正常）。
        /// 典型畸变信号：残差随半径单调增大（边缘带 ≫ 中心带）。
        /// </summary>
        public static string DiagnoseRadialBands(double[] bands)
        {
            if (bands == null || bands.Length < BandCount)
                return string.Empty;

            double center = bands[0];
            double edge = bands[BandCount - 1];
            if (double.IsNaN(center) || double.IsNaN(edge) || center <= 0)
                return string.Empty;

            if (edge >= 2 * center && edge >= 0.5)
            {
                return $"残差随视野半径增大（边缘带 {edge:0.##}px ≈ 中心带 {center:0.##}px 的 {edge / center:0.#} 倍）："
                     + "疑似镜头畸变——仿射模型无法吸收，可考虑网格标定或标定板内参标定（见方案 §十一）";
            }

            return string.Empty;
        }

        #endregion

        #region 坐标换算

        /// <summary>像素 (Row, Col) → 机械 (X, Y)。</summary>
        public static bool TryMapPixelToXY(double[] matrix, double row, double col, out double x, out double y)
        {
            x = 0;
            y = 0;
            if (!IsUsableMatrix(matrix))
                return false;

            x = matrix[0] * row + matrix[1] * col + matrix[4];
            y = matrix[2] * row + matrix[3] * col + matrix[5];
            return true;
        }

        /// <summary>机械 (X, Y) → 像素 (Row, Col)（反向：2×2 线性部分求逆）。</summary>
        public static bool TryMapXYToPixel(double[] matrix, double x, double y, out double row, out double col)
        {
            row = 0;
            col = 0;
            if (!IsUsableMatrix(matrix))
                return false;

            double det = matrix[0] * matrix[3] - matrix[1] * matrix[2];
            if (Math.Abs(det) < 1e-12)
                return false;

            double dx = x - matrix[4];
            double dy = y - matrix[5];
            row = (matrix[3] * dx - matrix[1] * dy) / det;
            col = (-matrix[2] * dx + matrix[0] * dy) / det;
            return true;
        }

        /// <summary>矩阵是否可用（长度 6 & 全部有限 & 线性部分不退化）。</summary>
        public static bool IsUsableMatrix(double[] matrix)
        {
            if (matrix == null || matrix.Length < 6)
                return false;
            for (int i = 0; i < 6; i++)
                if (!IsFinite(matrix[i])) return false;

            return Math.Abs(matrix[0] * matrix[3] - matrix[1] * matrix[2]) >= 1e-12;
        }

        #endregion

        #region 内部

        /// <summary>对称 3×3 方程组的克莱姆法则求解（系数矩阵 [m00 m01 m02; m01 m11 m12; m02 m12 m22]）。</summary>
        private static bool Solve3(
            double m00, double m01, double m02, double m11, double m12, double m22,
            double r0, double r1, double r2, double det,
            out double x0, out double x1, out double x2)
        {
            x0 = x1 = x2 = 0;
            if (Math.Abs(det) < double.Epsilon)
                return false;

            // 用伴随矩阵法（对称矩阵的伴随仍对称）
            double c00 = m11 * m22 - m12 * m12;
            double c01 = -(m01 * m22 - m12 * m02);
            double c02 = m01 * m12 - m11 * m02;
            double c11 = m00 * m22 - m02 * m02;
            double c12 = -(m00 * m12 - m01 * m02);
            double c22 = m00 * m11 - m01 * m01;

            x0 = (c00 * r0 + c01 * r1 + c02 * r2) / det;
            x1 = (c01 * r0 + c11 * r1 + c12 * r2) / det;
            x2 = (c02 * r0 + c12 * r1 + c22 * r2) / det;
            return true;
        }

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        #endregion
    }
}
