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
    /// · 矩阵 6 元 [a11 a12 a21 a22 a31 a32]：X = a11·Row + a12·Col + a31；Y = a21·Row + a22·Col + a32；
    /// · 投影 9 元 [h11 h12 h13 h21 h22 h23 h31 h32 h33]（行主序，与 HALCON 同排布）：
    ///     w = h31·Row + h32·Col + h33；X = (h11·Row + h12·Col + h13)/w；Y = (h21·Row + h22·Col + h23)/w。
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

        /// <summary>
        /// 透视退化阈值：归一化设计矩阵 AᵀA 的「次小/最大」特征值比（= (σ次小/σ最大)²）低于它判退化。
        /// 为什么看「次小」：最小特征值 ≈ 0 是投影矩阵自身的自由度（结构零，任何 ≥4 点数据都有），
        /// 第二个近零特征值才表示"解不唯一/病态"（三点共线、点近似全共线、重复点、覆盖不足）。
        /// 合成数据扫描（2026-10-05，见施工报告）：退化构型实测 ≤ ~7e-17（含数值零的负噪声）；
        /// 正常 4~16 点 ≥ ~7e-2（细长条 1:32 也有 ~1e-4）。取 1e-9，两侧余量 ≥ 8 个数量级。
        /// 判别时对非正值（数值零的负噪声 / NaN）按退化处理——不能开方、也不能放过。
        /// </summary>
        internal const double DegenerateEigenRatioMin = 1e-9;

        /// <summary>h33 归一化下限：|h33| 低于它视作解退化（无法按 h33=1 归一）</summary>
        private const double MinH33 = 1e-9;

        /// <summary>投影矩阵行列式下限（与 <see cref="IsUsableProjective"/> 同一口径）</summary>
        private const double MinProjDet = 1e-12;

        /// <summary>投影分母（w）下限：|w| 低于它视作落在投影退化线上</summary>
        private const double MinProjW = 1e-12;

        /// <summary>归一化平均距离下限（两侧点全部重合时平均距离为 0）</summary>
        private const double MinNormalizeScale = 1e-12;

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
        /// <param name="bandResiduals">径向带平均残差（长度 <see cref="BandCount"/>，无点的带为 0）</param>
        /// <param name="bandCounts">各径向带参与统计的点数（长度 <see cref="BandCount"/>，与 <paramref name="bandResiduals"/> 一一对应）</param>
        /// <param name="error">失败原因（中文，可直接进错误信息）</param>
        public static bool TrySolveAffine(
            double[] rows, double[] cols, double[] xs, double[] ys,
            out double[]? matrix, out double rmsPx, out double maxPx,
            out double[]? bandResiduals, out int[]? bandCounts, out string? error)
        {
            matrix = null;
            rmsPx = 0;
            maxPx = 0;
            bandResiduals = null;
            bandCounts = null;
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
                    // 这里是"参与求解的第 i 个点"（调用方已剔除空行），不等于表格行号——
                    // 带行名的指名由插件侧 CollectPoints 做（两边都报，便于定位）
                    error = $"参与求解的第 {i + 1} 组点含非法数值（NaN/Inf）——请检查标定表该行的填写";
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
            bandCounts = new int[BandCount];
            var bandSum = new double[BandCount];
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
                bandCounts[band]++;
            }

            // 无点的带记 0（不是 NaN：NaN 会序列化成非标准 JSON），"有没有点"由 counts 表达
            for (int b = 0; b < BandCount; b++)
                bandResiduals[b] = bandCounts[b] > 0 ? bandSum[b] / bandCounts[b] : 0;

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
        /// <param name="bands">径向带平均残差（无点的带为 0）</param>
        /// <param name="bandCounts">
        /// 各带点数（可空）。给了就只在"中心带与边缘带都有点"时才判形态——
        /// 否则"某带没点"会被当成"残差为 0"而误报畸变（3 点标定很常见）。
        /// </param>
        /// <param name="minEdgeResidualPx">
        /// 触发提示的最小边缘带残差：残差整体很小时（如 0.05px）不值得提示"疑似畸变"。
        /// 现场把残差阈值显式调大后，这个门限也应跟着放宽（见插件侧调用）。
        /// </param>
        public static string DiagnoseRadialBands(
            double[]? bands, int[]? bandCounts = null, double minEdgeResidualPx = 0.5)
        {
            if (bands == null || bands.Length < BandCount)
                return string.Empty;

            double center = bands[0];
            double edge = bands[BandCount - 1];
            if (double.IsNaN(center) || double.IsNaN(edge) || center <= 0)
                return string.Empty;

            // 有计数时：两个带都必须真的有采样点，否则"空带=0"会被误读成"中心很小"
            if (bandCounts != null && bandCounts.Length >= BandCount
                && (bandCounts[0] <= 0 || bandCounts[BandCount - 1] <= 0))
                return string.Empty;

            if (edge >= 2 * center && edge >= Math.Max(0, minEdgeResidualPx))
            {
                return $"残差随视野半径增大（边缘带 {edge:0.##}px ≈ 中心带 {center:0.##}px 的 {edge / center:0.#} 倍）："
                     + "疑似镜头畸变——仿射模型无法吸收，可考虑网格标定或标定板内参标定（见方案 §十一）";
            }

            return string.Empty;
        }

        #endregion

        #region 透视（投影）标定（二期①，2026-10-05）

        /// <summary>
        /// 由 N 组点求投影（透视）矩阵（归一化 DLT + Jacobi 特征分解）。
        ///
        /// 数值做法（与仿射版同一纪律：先归一化再解）
        /// ---------
        /// ① 校验后对两侧坐标做 **Hartley 归一化**（质心移到原点 + 平均距离缩放到 √2 量级）；
        /// ② 组装 2n×9 设计矩阵 A 的 AᵀA（9×9 对称）——仓内没有 SVD/eigen 现成件，用 Jacobi 循环旋转法做特征分解；
        /// ③ 取**最小特征值**的特征向量（即 A 的最小奇异向量）为解，反归一化 H = Nt⁻¹·H̃·Ns；
        /// ④ 按带符号 h33 归一为 h33=1；退化/折返守卫与残差统计。
        ///
        /// 退化守卫的判别量（阈值见 <see cref="DegenerateEigenRatioMin"/>）：AᵀA「次小/最大」特征值比——
        /// 最小特征值 ≈ 0 是投影矩阵自身的自由度（任何 ≥4 点数据都有），第二个近零特征值才表示病态。
        ///
        /// 残差单位（P0 纪律，与仿射版同一根因）：反算偏差在机械坐标系（mm），必须**按该点的局部当量**
        /// （<see cref="DeriveMmPerPixelProjective"/>）除一次换回像素——透视下当量随位置变化，
        /// 用全局/中心当量会在边缘悄悄放行放大后的误差。
        /// </summary>
        /// <param name="rows">图像点 Row 数组</param>
        /// <param name="cols">图像点 Col 数组</param>
        /// <param name="xs">机械坐标 X 数组（与图像点一一对应）</param>
        /// <param name="ys">机械坐标 Y 数组</param>
        /// <param name="hom">输出 9 元投影矩阵 [h11 h12 h13 h21 h22 h23 h31 h32 h33]（h33=1）</param>
        /// <param name="rmsPx">残差均方根（像素；mm→px 按逐点局部当量）</param>
        /// <param name="maxPx">最大单点残差（像素）</param>
        /// <param name="bands">径向带平均残差（长度 <see cref="BandCount"/>，无点的带为 0）</param>
        /// <param name="counts">各径向带参与统计的点数（长度 <see cref="BandCount"/>）</param>
        /// <param name="error">失败原因（中文，可直接进错误信息）</param>
        public static bool TrySolveProjective(
            double[] rows, double[] cols, double[] xs, double[] ys,
            out double[]? hom, out double rmsPx, out double maxPx,
            out double[]? bands, out int[]? counts, out string? error)
        {
            hom = null;
            rmsPx = 0;
            maxPx = 0;
            bands = null;
            counts = null;
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

            if (n < 4)
            {
                error = $"透视标定至少需要 4 组点（推荐 9~16 组，覆盖视野四角+中心；当前 {n} 组）";
                return false;
            }

            for (int i = 0; i < n; i++)
            {
                if (!IsFinite(rows[i]) || !IsFinite(cols[i]) || !IsFinite(xs[i]) || !IsFinite(ys[i]))
                {
                    // 与仿射版同一口径：这里是"参与求解的第 i 个点"（空行剔除后与行号对不上），带行名的指名由插件侧做
                    error = $"参与求解的第 {i + 1} 组点含非法数值（NaN/Inf）——请检查标定表该行的填写";
                    return false;
                }
            }

            // ---- Hartley 归一化（两侧）：质心 + 平均距离 ----
            double cr = 0, cc = 0, cx = 0, cy = 0;
            for (int i = 0; i < n; i++) { cr += rows[i]; cc += cols[i]; cx += xs[i]; cy += ys[i]; }
            cr /= n; cc /= n; cx /= n; cy /= n;

            double meanDistS = 0, meanDistT = 0;
            for (int i = 0; i < n; i++)
            {
                meanDistS += Dist(rows[i], cols[i], cr, cc);
                meanDistT += Dist(xs[i], ys[i], cx, cy);
            }
            meanDistS /= n; meanDistT /= n;
            if (!(meanDistS > MinNormalizeScale) || !(meanDistT > MinNormalizeScale))
            {
                error = "标定点分布退化（近似共线/重合或覆盖不足），无法稳定求出投影变换："
                      + "标定点全部重合（归一化平均距离为 0），请把点铺开成 3×3/4×4 网格";
                return false;
            }
            double ss = Math.Sqrt(2) / meanDistS;   // Ns：图侧 (Row,Col) → 归一化
            double st = Math.Sqrt(2) / meanDistT;   // Nt：机侧 (X,Y) → 归一化

            var nu = new double[n]; var nv = new double[n];
            var nX = new double[n]; var nY = new double[n];
            for (int i = 0; i < n; i++)
            {
                nu[i] = (rows[i] - cr) * ss; nv[i] = (cols[i] - cc) * ss;
                nX[i] = (xs[i] - cx) * st; nY[i] = (ys[i] - cy) * st;
            }

            // ---- AᵀA（9×9 对称；只累加上三角再镜像）----
            // 每点两行：r1 = [u, v, 1, 0,0,0, −u·X, −v·X, −X]；r2 = [0,0,0, u, v, 1, −u·Y, −v·Y, −Y]
            var m = new double[81];
            for (int i = 0; i < n; i++)
            {
                var r1 = new[] { nu[i], nv[i], 1.0, 0, 0, 0, -nu[i] * nX[i], -nv[i] * nX[i], -nX[i] };
                var r2 = new[] { 0.0, 0, 0, nu[i], nv[i], 1.0, -nu[i] * nY[i], -nv[i] * nY[i], -nY[i] };
                for (int p = 0; p < 9; p++)
                    for (int q = p; q < 9; q++)
                        m[p * 9 + q] += r1[p] * r1[q] + r2[p] * r2[q];
            }
            for (int p = 0; p < 9; p++)
                for (int q = 0; q < p; q++)
                    m[p * 9 + q] = m[q * 9 + p];

            // ---- 特征分解（升序特征值 + 对应特征向量列）----
            if (!JacobiEigen9(m, out var eigenAsc, out var eigenVecs))
            {
                error = "求解失败（特征分解未收敛，内部数值异常）——请检查标定点分布";
                return false;
            }

            // ---- 退化守卫：次小特征值比（非正值/NaN 一律按退化处理——数值零的负噪声不能放过）----
            double e1 = eigenAsc[1], e8 = eigenAsc[8];
            double eigenRatio = e8 > 0 ? e1 / e8 : 0;
            if (!(eigenRatio > DegenerateEigenRatioMin))
            {
                error = "标定点分布退化（近似共线/重合或覆盖不足），无法稳定求出投影变换："
                      + $"归一化设计矩阵的次小特征值比 ≈ {eigenRatio:0.###e+0}（下限 {DegenerateEigenRatioMin:0.###e+0}）。"
                      + "请把点铺开成 3×3/4×4 网格，覆盖视野四角+中心，别让点落在一条直线上";
                return false;
            }

            // ---- 解：最小特征值（升序第 0 个）的特征向量 → H̃；反归一化 H = Nt⁻¹·H̃·Ns ----
            var qh = new double[9];
            for (int k = 0; k < 9; k++) qh[k] = eigenVecs[k * 9];

            double r00 = qh[0] * ss, r01 = qh[1] * ss, r02 = -qh[0] * ss * cr - qh[1] * ss * cc + qh[2];
            double r10 = qh[3] * ss, r11 = qh[4] * ss, r12 = -qh[3] * ss * cr - qh[4] * ss * cc + qh[5];
            double r20 = qh[6] * ss, r21 = qh[7] * ss, r22 = -qh[6] * ss * cr - qh[7] * ss * cc + qh[8];
            var h = new double[9];
            h[0] = r00 / st + cx * r20; h[1] = r01 / st + cx * r21; h[2] = r02 / st + cx * r22;
            h[3] = r10 / st + cy * r20; h[4] = r11 / st + cy * r21; h[5] = r12 / st + cy * r22;
            h[6] = r20; h[7] = r21; h[8] = r22;

            if (!(Math.Abs(h[8]) >= MinH33))
            {
                error = "投影矩阵求解退化（h33≈0，无法按 h33=1 归一化）——标定点分布可能退化，请检查（覆盖不足/近似共线/重合）";
                return false;
            }
            for (int k = 0; k < 9; k++)
            {
                h[k] /= h[8];   // 带符号 h33 归一（h33 = 1；其余项随符号翻转，等价映射）
                if (!IsFinite(h[k]))
                {
                    error = "投影矩阵求解失败（含非法数值 NaN/Inf）——请检查标定点分布";
                    return false;
                }
            }

            double det = h[0] * (h[4] * h[8] - h[5] * h[7])
                       - h[1] * (h[3] * h[8] - h[5] * h[6])
                       + h[2] * (h[3] * h[7] - h[4] * h[6]);
            if (!IsFinite(det) || Math.Abs(det) < MinProjDet)
            {
                error = "投影矩阵退化（行列式≈0）——请检查标定点分布（近似共线/重合/覆盖不足）";
                return false;
            }

            // ---- 折返守卫：det J = det(H)/w³ ⇒ 映射折返（雅可比符号不一致）⟺ 标定点处 w 符号不一致 ----
            bool hasPos = false, hasNeg = false;
            for (int i = 0; i < n; i++)
            {
                double w = h[6] * rows[i] + h[7] * cols[i] + h[8];
                if (!IsFinite(w) || Math.Abs(w) < MinProjW)
                {
                    error = "标定点落在投影退化线上（w≈0）——请检查标定点是否覆盖了不连续区域";
                    return false;
                }
                if (w > 0) hasPos = true; else hasNeg = true;
            }
            if (hasPos && hasNeg)
            {
                error = "标定点跨越投影退化线（映射折返、雅可比符号不一致）——请检查标定点数据是否对应错位或覆盖了翻转区域";
                return false;
            }

            // ---- 残差与径向带统计（px；mm→px 用逐点局部当量）----
            double scale = 0;   // 带统计的归一化基准（与仿射版一致：源点集最大半径）
            for (int i = 0; i < n; i++)
            {
                double d = Dist(rows[i], cols[i], cr, cc);
                if (d > scale) scale = d;
            }

            bands = new double[BandCount];
            counts = new int[BandCount];
            var bandSum = new double[BandCount];
            double sumSq = 0;
            for (int i = 0; i < n; i++)
            {
                double w = h[6] * rows[i] + h[7] * cols[i] + h[8];
                double xm = (h[0] * rows[i] + h[1] * cols[i] + h[2]) / w;
                double ym = (h[3] * rows[i] + h[4] * cols[i] + h[5]) / w;
                double local = DeriveMmPerPixelProjective(h, rows[i], cols[i], out _);
                if (!(local > 0))
                {
                    error = "求解出的投影矩阵在标定点处局部当量非法（≤0）——请检查标定点分布";
                    return false;
                }

                double dmm = Dist(xm, ym, xs[i], ys[i]);
                double dpx = dmm / local;

                sumSq += dpx * dpx;
                if (dpx > maxPx) maxPx = dpx;

                double r = scale > 0 ? Dist(rows[i], cols[i], cr, cc) / scale : 0;
                int band = r <= BandInner ? 0 : r <= BandOuter ? 1 : 2;
                bandSum[band] += dpx;
                counts[band]++;
            }

            // 无点的带记 0（不是 NaN：NaN 会序列化成非标准 JSON），"有没有点"由 counts 表达
            for (int b = 0; b < BandCount; b++)
                bands[b] = counts[b] > 0 ? bandSum[b] / counts[b] : 0;

            rmsPx = Math.Sqrt(sumSq / n);
            hom = h;
            return true;
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

        /// <summary>投影矩阵是否可用（长度 9 & 全部有限 & 行列式不退化）。</summary>
        public static bool IsUsableProjective(double[]? hom)
        {
            if (hom == null || hom.Length < 9)
                return false;
            for (int i = 0; i < 9; i++)
                if (!IsFinite(hom[i])) return false;

            double det = hom[0] * (hom[4] * hom[8] - hom[5] * hom[7])
                       - hom[1] * (hom[3] * hom[8] - hom[5] * hom[6])
                       + hom[2] * (hom[3] * hom[7] - hom[4] * hom[6]);
            return IsFinite(det) && Math.Abs(det) >= MinProjDet;
        }

        /// <summary>像素 (Row, Col) → 机械 (X, Y)（透视）。w≈0（落在投影退化线上）→ 明确失败。</summary>
        /// <param name="error">失败原因（中文）——w 守卫文案进断言与现场提示，不能静默返回 0</param>
        public static bool TryMapPixelToXYProj(
            double[] hom, double row, double col, out double x, out double y, out string? error)
        {
            x = 0;
            y = 0;
            error = null;

            if (!IsUsableProjective(hom))
            {
                error = "投影矩阵不可用（缺失或退化）";
                return false;
            }
            if (!IsFinite(row) || !IsFinite(col))
            {
                error = "像素坐标含非法数值（NaN/Inf）——请检查上游输入";
                return false;
            }

            double w = hom[6] * row + hom[7] * col + hom[8];
            if (!IsFinite(w) || Math.Abs(w) < MinProjW)
            {
                error = "像素点落在投影退化线上（w≈0，无法映射）";
                return false;
            }

            x = (hom[0] * row + hom[1] * col + hom[2]) / w;
            y = (hom[3] * row + hom[4] * col + hom[5]) / w;
            return IsFinite(x) && IsFinite(y);
        }

        /// <summary>机械 (X, Y) → 像素 (Row, Col)（透视；3×3 伴随矩阵求逆）。</summary>
        public static bool TryMapXYToPixelProj(
            double[] hom, double x, double y, out double row, out double col, out string? error)
        {
            row = 0;
            col = 0;
            error = null;

            if (!IsUsableProjective(hom))
            {
                error = "投影矩阵不可用（缺失或退化）";
                return false;
            }
            if (!IsFinite(x) || !IsFinite(y))
            {
                error = "机械坐标含非法数值（NaN/Inf）——请检查上游输入";
                return false;
            }

            // 3×3 伴随求逆：H⁻¹ = adj(H)/det（伴随 = 代数余子式矩阵转置）
            double c00 = hom[4] * hom[8] - hom[5] * hom[7];
            double c01 = hom[5] * hom[6] - hom[3] * hom[8];
            double c02 = hom[3] * hom[7] - hom[4] * hom[6];
            double c10 = hom[2] * hom[7] - hom[1] * hom[8];
            double c11 = hom[0] * hom[8] - hom[2] * hom[6];
            double c12 = hom[1] * hom[6] - hom[0] * hom[7];
            double c20 = hom[1] * hom[5] - hom[2] * hom[4];
            double c21 = hom[2] * hom[3] - hom[0] * hom[5];
            double c22 = hom[0] * hom[4] - hom[1] * hom[3];
            double det = hom[0] * c00 + hom[1] * c01 + hom[2] * c02;
            if (!IsFinite(det) || Math.Abs(det) < MinProjDet)
            {
                error = "投影矩阵不可逆（近似退化）";
                return false;
            }

            double pr = (c00 * x + c10 * y + c20) / det;
            double pc = (c01 * x + c11 * y + c21) / det;
            double pw = (c02 * x + c12 * y + c22) / det;
            if (!IsFinite(pw) || Math.Abs(pw) < MinProjW)
            {
                error = "机械点落在投影退化线上（w≈0，无法反算像素）";
                return false;
            }

            row = pr / pw;
            col = pc / pw;
            return IsFinite(row) && IsFinite(col);
        }

        /// <summary>
        /// 透视映射在 (row, col) 处的雅可比 J = [[∂X/∂Row, ∂X/∂Col], [∂Y/∂Row, ∂Y/∂Col]]。
        /// 可用恒等式：det J = det(H) / w³（折返守卫的符号判据由此而来）。
        /// </summary>
        public static bool JacobianProjective(double[] hom, double row, double col,
            out double dxDRow, out double dxDCol, out double dyDRow, out double dyDCol)
        {
            dxDRow = dxDCol = dyDRow = dyDCol = 0;

            if (!IsUsableProjective(hom))
                return false;

            double w = hom[6] * row + hom[7] * col + hom[8];
            if (!IsFinite(w) || Math.Abs(w) < MinProjW)
                return false;

            double numX = hom[0] * row + hom[1] * col + hom[2];
            double numY = hom[3] * row + hom[4] * col + hom[5];
            double w2 = w * w;
            dxDRow = (hom[0] * w - numX * hom[6]) / w2;
            dxDCol = (hom[1] * w - numX * hom[7]) / w2;
            dyDRow = (hom[3] * w - numY * hom[6]) / w2;
            dyDCol = (hom[4] * w - numY * hom[7]) / w2;
            return true;
        }

        /// <summary>
        /// 透视的**局部**像素当量（mm/px）：与仿射版 <see cref="DeriveMmPerPixel"/> 同定义、局部化——
        /// 当量 = (‖J·e_row‖ + ‖J·e_col‖) / 2（J 在该点的雅可比），即"沿 Row / 沿 Col 各走 1 像素"的均值。
        /// 投影矩阵不可用或落在退化线上 → 返回 0（调用方的 &gt;0 护栏照常生效）。
        /// </summary>
        /// <param name="anisotropyPercent">两向当量差的百分比（各向异性，顺时针提示而非失败）</param>
        public static double DeriveMmPerPixelProjective(
            double[] hom, double row, double col, out double anisotropyPercent)
        {
            anisotropyPercent = 0;
            if (!JacobianProjective(hom, row, col, out var dxdR, out var dxdC, out var dydR, out var dydC))
                return 0;

            double lenRow = Math.Sqrt(dxdR * dxdR + dydR * dydR);   // 沿 Row 方向
            double lenCol = Math.Sqrt(dxdC * dxdC + dydC * dydC);   // 沿 Col 方向
            double mean = (lenRow + lenCol) / 2;
            if (mean > 0 && lenRow > 0 && lenCol > 0)
                anisotropyPercent = Math.Abs(lenRow - lenCol) / mean * 100.0;
            return mean;
        }

        #endregion

        #region 内部

        /// <summary>
        /// 9×9 对称矩阵的 Jacobi 特征分解（循环旋转法；仓内无 SVD/eigen 现成件，故自带）。
        /// 输入 m：行主序 9×9 对称矩阵（只读，内部拷贝）；输出 eigenAsc：升序特征值；
        /// eigenVecs：行主序 9×9，**第 k 列 = eigenAsc[k] 对应的特征向量**。
        /// 收敛防御：扫描上限 <see cref="JacobiMaxSweeps"/> 轮；结束后非对角相对残差仍高于 1e-18 → 返回 false
        /// （调用方转"特征分解未收敛"——防死循环、也防伪解被当有效结果用）。
        /// </summary>
        private static bool JacobiEigen9(double[] m, out double[] eigenAsc, out double[] eigenVecs)
        {
            const int n = 9;

            var a = (double[])m.Clone();
            eigenVecs = new double[n * n];
            eigenAsc = new double[n];
            for (int i = 0; i < n; i++) eigenVecs[i * n + i] = 1.0;

            double frob2 = 0;
            for (int k = 0; k < n * n; k++) frob2 += a[k] * a[k];
            double tol = 1e-24 * frob2;   // 目标：非对角平方和压到相对 1e-24 以下

            for (int sweep = 0; sweep < JacobiMaxSweeps; sweep++)
            {
                double off = 0;
                for (int p = 0; p < n; p++)
                    for (int q = p + 1; q < n; q++) off += a[p * n + q] * a[p * n + q];
                if (off <= tol) break;

                for (int p = 0; p < n - 1; p++)
                {
                    for (int q = p + 1; q < n; q++)
                    {
                        double apq = a[p * n + q];
                        if (apq == 0) continue;

                        // 解 t² + 2θt − 1 = 0 的稳定小根（θ = (a_qq − a_pp) / (2·a_pq)）
                        double app = a[p * n + p], aqq = a[q * n + q];
                        double theta = (aqq - app) / (2 * apq);
                        double t = theta >= 0
                            ? 1.0 / (theta + Math.Sqrt(theta * theta + 1.0))
                            : -1.0 / (-theta + Math.Sqrt(theta * theta + 1.0));
                        double c = 1.0 / Math.Sqrt(t * t + 1.0);
                        double s = t * c;

                        for (int k = 0; k < n; k++)   // A ← A·J（列变换）
                        {
                            double akp = a[k * n + p], akq = a[k * n + q];
                            a[k * n + p] = c * akp - s * akq;
                            a[k * n + q] = s * akp + c * akq;
                        }
                        for (int k = 0; k < n; k++)   // A ← Jᵀ·A（行变换）
                        {
                            double apk = a[p * n + k], aqk = a[q * n + k];
                            a[p * n + k] = c * apk - s * aqk;
                            a[q * n + k] = s * apk + c * aqk;
                        }
                        for (int k = 0; k < n; k++)   // V ← V·J（累计特征向量）
                        {
                            double vkp = eigenVecs[k * n + p], vkq = eigenVecs[k * n + q];
                            eigenVecs[k * n + p] = c * vkp - s * vkq;
                            eigenVecs[k * n + q] = s * vkp + c * vkq;
                        }
                    }
                }
            }

            double offFinal = 0;
            for (int p = 0; p < n; p++)
                for (int q = p + 1; q < n; q++) offFinal += a[p * n + q] * a[p * n + q];
            if (frob2 > 0 && offFinal / frob2 > 1e-18)
                return false;   // 未收敛：宁失败勿伪解

            for (int i = 0; i < n; i++) eigenAsc[i] = a[i * n + i];

            // 特征值升序排序（特征向量列同步交换）
            for (int i = 0; i < n; i++)
            {
                int k = i;
                for (int j = i + 1; j < n; j++)
                    if (eigenAsc[j] < eigenAsc[k]) k = j;
                if (k != i)
                {
                    (eigenAsc[i], eigenAsc[k]) = (eigenAsc[k], eigenAsc[i]);
                    for (int r = 0; r < n; r++)
                        (eigenVecs[r * n + i], eigenVecs[r * n + k]) = (eigenVecs[r * n + k], eigenVecs[r * n + i]);
                }
            }
            return true;
        }

        /// <summary>Jacobi 扫描上限（防御：正常 9×9 约 6~8 轮即到机器精度）</summary>
        private const int JacobiMaxSweeps = 100;

        /// <summary>两点距离（新代码共用；仿射段原有的内联算法不动）。</summary>
        private static double Dist(double r1, double c1, double r2, double c2)
            => Math.Sqrt((r1 - r2) * (r1 - r2) + (c1 - c2) * (c1 - c2));

        /// <summary>对称 3×3 方程组的克莱姆法则求解（系数矩阵 [m00 m01 m02; m01 m11 m12; m02 m12 m22]）。</summary>
        private static bool Solve3(
            double m00, double m01, double m02, double m11, double m12, double m22,
            double r0, double r1, double r2, double det,
            out double x0, out double x1, out double x2)
        {
            x0 = x1 = x2 = 0;
            // 注：退化把关在调用方（CollinearDetRatioMin·trace³ 相对判据）；这里只兜底精确 0 的除零。
            // `det == 0` 与原来的 `Math.Abs(det) < double.Epsilon` 等价，直白写出避免误读成"近零守卫"。
            if (det == 0.0)
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
