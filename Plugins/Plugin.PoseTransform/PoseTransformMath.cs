using System;
using Core.Interfaces;

namespace Plugin.PoseTransform
{
    /// <summary>
    /// 坐标变换的**纯托管数学**（零 HALCON 依赖 → 断言可直接构造输入跑，不依赖引擎）。
    ///
    /// 契约来源（与「标定」插件共用，方案 §约定）
    /// ---------
    /// · <see cref="CalibrationTransform.Matrix"/> = 6 元 [a11 a12 a21 a22 a31 a32]，映射 (Row, Col) → (X, Y)：
    ///     X = a11·Row + a12·Col + a31
    ///     Y = a21·Row + a22·Col + a32
    /// · 角度单位=度；轴交换/镜像/旋转**全部由矩阵吸收**，本类不做任何"方向校正"。
    ///
    /// 为什么这些公式在本插件再写一遍（不在插件间共享一个实现）
    /// ---------
    /// 插件是独立模块、只引用契约程序集（互相不引用是仓库纪律）；
    /// 公式语义由 <see cref="CalibrationTransform"/> 的契约注释锁死，两个插件各自实现、
    /// 各自的断言套件把同一组已知矩阵钉住——这是"契约锁语义"而不是"共享一个 DLL"。
    /// </summary>
    public static class PoseTransformMath
    {
        #region 失配与有效性判定（三类失配 + 位姿/像素点守卫）

        /// <summary>未接标定：错误文案固定（断言锁住"下一步"指引）。</summary>
        public const string MissingCalibrationMessage =
            "未接标定：请把「标定」插件的 Transform 输出接到本步骤";

        /// <summary>PixelScale 标定没有机械坐标系：当机械坐标用时必须明确失败。</summary>
        public const string PixelScaleCannotMapMessage =
            "当前标定只有像素当量、不含机械坐标：坐标换算请用九点标定 / 透视标定 / 网格标定（像素当量模式只给 mm/px）";

        /// <summary>标定缺失/类型不对/矩阵损坏 → false（失配第一、二类；按 Kind 分支，未知类型绝不静默放行）。</summary>
        public static bool CheckCalibrationUsable(CalibrationTransform? t, out string? error)
        {
            error = null;

            if (t == null)
            {
                error = MissingCalibrationMessage;
                return false;
            }

            if (t.Kind == CalibrationKind.PixelScale)
            {
                error = PixelScaleCannotMapMessage;
                return false;
            }

            switch (t.Kind)
            {
                case CalibrationKind.NinePoint:
                    if (!IsUsableMatrix(t.Matrix))
                    {
                        error = "标定数据损坏（矩阵缺失或退化）：请重新运行「标定」步骤";
                        return false;
                    }
                    return true;

                case CalibrationKind.Perspective:
                    if (!IsUsableProjective(t.ProjectiveMatrix))
                    {
                        error = "标定数据损坏（投影矩阵缺失或退化）：请重新运行「标定」步骤";
                        return false;
                    }
                    return true;

                case CalibrationKind.Mesh:
                    // 网格的三角化/插值规则放契约程序集（Core.Interfaces.CalibrationMesh）：
                    // 生产端与消费端必须是同一份实现，分叉 = 同一份标定算出两套坐标。
                    // 文案尾巴由 CalibrationMesh 自带（含"重新运行「标定」步骤"），这里不追加、避免重复。
                    if (!CalibrationMesh.IsUsable(t.MeshNodes, t.MeshSize, out error))
                    {
                        error ??= "标定数据损坏（网格缺失或退化）：请重新运行「标定」步骤";
                        return false;
                    }
                    return true;

                default:
                    // 防"忘记分支"静默放行：未知 Kind 明确拒绝（消费方联动断言钉住）
                    error = $"不支持的标定类型（{t.Kind}）：请升级软件或重新标定";
                    return false;
            }
        }

        /// <summary>
        /// 失配第三类：当前图尺寸 ≠ 标定时图尺寸（换分辨率/换相机后旧标定必须自证失效）。
        /// 标定快照没记尺寸（两个 0）时无从比对 → 放行（由现场核对），不静默报错。
        /// </summary>
        public static bool CheckImageSizeMatch(CalibrationTransform t, int width, int height, out string? error)
        {
            error = null;

            if (t.SourceImageWidth <= 0 || t.SourceImageHeight <= 0)
                return true;
            if (t.SourceImageWidth == width && t.SourceImageHeight == height)
                return true;

            error = $"标定与当前图像尺寸不符（标定时 {t.SourceImageWidth}×{t.SourceImageHeight}，当前 {width}×{height}）"
                  + "——换分辨率/换相机后需重新标定";
            return false;
        }

        /// <summary>
        /// 位姿有效性（FollowRoi）：NaN/Inf 与"全 0"都必须失败——
        /// 匹配失败时下游常拿到 0/NaN，静默变换会产出"看起来正常"的错 ROI。
        /// </summary>
        public static bool IsValidPose(double row, double col, double angleDeg, out string? error)
        {
            error = null;

            if (!IsFinite(row) || !IsFinite(col) || !IsFinite(angleDeg))
            {
                error = "位姿无效（NaN/Inf）——请检查模板匹配是否成功（或上游位姿来源）";
                return false;
            }

            if (row == 0 && col == 0 && angleDeg == 0)
            {
                error = "位姿为 (0,0,0)：多半是模板匹配失败或位姿未接——请检查上游位姿来源";
                return false;
            }

            return true;
        }

        /// <summary>待换算像素点有效性（ToMechanical）：NaN/Inf 与 (0,0) 同上（匹配失败的典型输出）。</summary>
        public static bool IsUsablePixelPoint(double row, double col, out string? error)
        {
            error = null;

            if (!IsFinite(row) || !IsFinite(col))
            {
                error = "待换算的像素坐标无效（NaN/Inf）——请检查上游（模板匹配是否成功）";
                return false;
            }

            if (row == 0 && col == 0)
            {
                error = "待换算的像素点为 (0,0)：多半是模板匹配失败或未接——请检查上游位姿来源";
                return false;
            }

            return true;
        }

        #endregion

        #region 点换算（正 / 反）

        /// <summary>像素 (Row, Col) → 机械 (X, Y)（九点：仿射；透视：投影除法）。</summary>
        public static bool TryMapPixelToMechanical(
            CalibrationTransform? t, double row, double col, out double x, out double y, out string? error)
        {
            x = 0;
            y = 0;

            if (!CheckCalibrationUsable(t, out error))
                return false;

            if (!IsFinite(row) || !IsFinite(col))
            {
                error = "像素坐标无效（NaN/Inf）——请检查上游（模板匹配是否成功）";
                return false;
            }

            var tc = t!;
            if (tc.Kind == CalibrationKind.Perspective)
            {
                var h = tc.ProjectiveMatrix!;   // CheckCalibrationUsable 已保证可用
                double w = h[6] * row + h[7] * col + h[8];
                if (!IsFinite(w) || Math.Abs(w) < 1e-12)
                {
                    error = "像素点落在投影退化线上（w≈0，无法映射）";
                    return false;
                }

                x = (h[0] * row + h[1] * col + h[2]) / w;
                y = (h[3] * row + h[4] * col + h[5]) / w;
                return true;
            }

            if (tc.Kind == CalibrationKind.Mesh)
            {
                // 分段仿射：定位所在三角形后重心插值（节点处精确通过）；**网格外明确失败，不外推**
                return CalibrationMesh.TryMapPixelToXY(tc.MeshNodes!, tc.MeshSize, row, col, out x, out y, out error);
            }

            var m = tc.Matrix;
            x = m[0] * row + m[1] * col + m[4];
            y = m[2] * row + m[3] * col + m[5];
            return true;
        }

        /// <summary>机械 (X, Y) → 像素 (Row, Col)（九点：2×2 求逆；透视：3×3 伴随求逆 + 齐次除法）。</summary>
        public static bool TryMapMechanicalToPixel(
            CalibrationTransform? t, double x, double y, out double row, out double col, out string? error)
        {
            row = 0;
            col = 0;

            if (!CheckCalibrationUsable(t, out error))
                return false;

            var tc = t!;
            if (tc.Kind == CalibrationKind.Perspective)
            {
                var h = tc.ProjectiveMatrix!;   // CheckCalibrationUsable 已保证可用
                // 3×3 伴随求逆：H⁻¹ = adj(H)/det
                double c00 = h[4] * h[8] - h[5] * h[7];
                double c01 = h[5] * h[6] - h[3] * h[8];
                double c02 = h[3] * h[7] - h[4] * h[6];
                double c10 = h[2] * h[7] - h[1] * h[8];
                double c11 = h[0] * h[8] - h[2] * h[6];
                double c12 = h[1] * h[6] - h[0] * h[7];
                double c20 = h[1] * h[5] - h[2] * h[4];
                double c21 = h[2] * h[3] - h[0] * h[5];
                double c22 = h[0] * h[4] - h[1] * h[3];
                double detH = h[0] * c00 + h[1] * c01 + h[2] * c02;
                if (!IsFinite(detH) || Math.Abs(detH) < 1e-12)
                {
                    error = "标定矩阵不可逆（投影退化）：请重新标定";
                    return false;
                }

                double pr = (c00 * x + c10 * y + c20) / detH;
                double pc = (c01 * x + c11 * y + c21) / detH;
                double pw = (c02 * x + c12 * y + c22) / detH;
                if (!IsFinite(pw) || Math.Abs(pw) < 1e-12)
                {
                    error = "机械点落在投影退化线上（w≈0，无法反算像素）";
                    return false;
                }

                row = pr / pw;
                col = pc / pw;
                return true;
            }

            if (tc.Kind == CalibrationKind.Mesh)
            {
                // 反向同样是分段仿射（机械空间定位 + 重心插值）——与正向严格互逆（同一三角形内是同一个仿射）
                if (!IsFinite(x) || !IsFinite(y))
                {
                    error = "机械坐标无效（NaN/Inf）：请检查上游（或标定数据）";
                    return false;
                }
                return CalibrationMesh.TryMapXYToPixel(tc.MeshNodes!, tc.MeshSize, x, y, out row, out col, out error);
            }

            var m = tc.Matrix;
            double det = m[0] * m[3] - m[1] * m[2];
            if (!IsFinite(det) || Math.Abs(det) < 1e-12)
            {
                error = "标定矩阵不可逆（近似共线）：请重新标定";
                return false;
            }

            if (!IsFinite(x) || !IsFinite(y))
            {
                error = "机械坐标无效（NaN/Inf）：请检查标定矩阵是否损坏";
                return false;
            }

            double dx = x - m[4];
            double dy = y - m[5];
            row = (m[3] * dx - m[1] * dy) / det;
            col = (-m[2] * dx + m[0] * dy) / det;
            return true;
        }

        #endregion

        #region 角度（像素系 → 机械系）

        /// <summary>
        /// 像素系角度（度）→ 机械系角度（度）。
        ///
        /// 配方（方案 §六·4，且刻意不推三角函数）：
        /// ① 取图像 x 轴方向 (Row=0, Col=1) 经矩阵**线性部分**映射，得基准方向角 φ0；
        /// ② 对象在图像里转 θ：方向向量 (Row=sinθ, Col=cosθ) 同样映射，得 φ(θ)；
        /// ③ 机械角 = φ(θ) − φ0（归一化到 (−180, 180]）——对象没转时严格为 0。
        ///
        /// 镜像/轴交换标定会让转角**取反**：这是矩阵的自然结果（手性翻转），
        /// 不做任何额外"校正"（方案 §二·5：负负得正类错误都出在"再校正一次"）。
        ///
        /// **透视标定下角度与位置相关**：基准与对象方向都用**同一点 p** 的局部雅可比 J(p)
        /// （v0 = J·(0,1)ᵀ、vθ = J·(sinθ,cosθ)ᵀ）；九点标定忽略 row/col（全局线性），行为与旧版一致。
        /// </summary>
        public static bool TryPixelAngleToMechanical(
            CalibrationTransform? t, double pixelAngleDeg, double row, double col,
            out double mechanicalAngleDeg, out string? error)
        {
            mechanicalAngleDeg = 0;

            if (!CheckCalibrationUsable(t, out error))
                return false;

            if (!IsFinite(pixelAngleDeg))
            {
                error = "像素角度无效（NaN/Inf）——请检查上游角度来源";
                return false;
            }

            double rad = pixelAngleDeg * Math.PI / 180.0;
            double sin = Math.Sin(rad);
            double cos = Math.Cos(rad);
            double dx0, dy0, dx, dy;

            var tc = t!;
            if (tc.Kind is CalibrationKind.Perspective or CalibrationKind.Mesh)
            {
                // 透视/网格：角度与位置相关——基准与对象方向都用**同一点 p** 的局部雅可比 J(p)
                if (!IsFinite(row) || !IsFinite(col))
                {
                    error = "像素位置无效（NaN/Inf）：透视/网格角度换算需要有效位置（局部雅可比随点变化）";
                    return false;
                }

                double ja, jb, jc, jd;
                if (tc.Kind == CalibrationKind.Perspective)
                {
                    if (!JacobianAt(tc.ProjectiveMatrix!, row, col, out ja, out jb, out jc, out jd))
                    {
                        error = "标定矩阵退化（局部雅可比取不到，投影退化线上）：请重新标定";
                        return false;
                    }
                }
                else
                {
                    // 网格：所在三角形的仿射雅可比（与点换算用同一套定位）
                    if (!CalibrationMesh.TryLocalAffine(
                            tc.MeshNodes!, tc.MeshSize, row, col,
                            out ja, out jb, out jc, out jd, out _, out _, out var meshAngleError))
                    {
                        error = meshAngleError;
                        return false;
                    }
                }

                dx0 = jb;                  // 基准方向 = 图像 x 轴经 J(p) 的像
                dy0 = jd;
                dx = ja * sin + jb * cos;  // 转 θ 后的方向向量经同一 J(p) 的像
                dy = jc * sin + jd * cos;
            }
            else
            {
                var m = tc.Matrix;
                // ① 图像 x 轴 (Row=0, Col=1) 的映射方向
                dx0 = m[1];
                dy0 = m[3];
                // ② 转 θ 后的方向向量 (Row=sinθ, Col=cosθ) 经线性部分映射
                dx = m[0] * sin + m[1] * cos;
                dy = m[2] * sin + m[3] * cos;
            }

            if (Math.Sqrt(dx0 * dx0 + dy0 * dy0) < 1e-12)
            {
                error = "标定矩阵退化（图像 x 轴映射为 0）：请重新标定";
                return false;
            }

            // ③ 方向角之差 = 机械系转角
            mechanicalAngleDeg = NormalizeDegrees(
                Math.Atan2(dy, dx) * 180.0 / Math.PI
                - Math.Atan2(dy0, dx0) * 180.0 / Math.PI);
            return true;
        }

        /// <summary>归一化到 (−180, 180]（度）。</summary>
        public static double NormalizeDegrees(double degrees)
        {
            degrees %= 360.0;
            if (degrees > 180.0) degrees -= 360.0;
            if (degrees <= -180.0) degrees += 360.0;
            return degrees;
        }

        #endregion

        #region 内部

        // ---- 透视（投影，二期①）：口径与「标定」侧 CalibrationMath 对齐，独立实现（插件间不互引） ----

        /// <summary>
        /// 透视矩阵是否可用（长度 ≥9 &amp; 全部有限 &amp; |det H| ≥ 1e-12）。
        /// 口径与「标定」侧 CalibrationMath.IsUsableProjective 一致。
        /// </summary>
        public static bool IsUsableProjective(double[]? hom)
        {
            if (hom == null || hom.Length < 9)
                return false;

            for (int i = 0; i < 9; i++)
                if (!IsFinite(hom[i]))
                    return false;

            double det = hom[0] * (hom[4] * hom[8] - hom[5] * hom[7])
                       - hom[1] * (hom[3] * hom[8] - hom[5] * hom[6])
                       + hom[2] * (hom[3] * hom[7] - hom[4] * hom[6]);
            return IsFinite(det) && Math.Abs(det) >= 1e-12;
        }

        /// <summary>
        /// 透视映射在 (row, col) 处的**局部雅可比** J = [[a, b], [c, d]]
        /// （a=∂X/∂Row、b=∂X/∂Col、c=∂Y/∂Row、d=∂Y/∂Col；口径与标定侧 JacobianProjective 一致）。
        /// 投影矩阵不可用或落在退化线上（w≈0）→ false。
        /// </summary>
        public static bool JacobianAt(double[] hom, double row, double col,
            out double a, out double b, out double c, out double d)
        {
            a = b = c = d = 0;

            if (!IsUsableProjective(hom))
                return false;

            double w = hom[6] * row + hom[7] * col + hom[8];
            if (!IsFinite(w) || Math.Abs(w) < 1e-12)
                return false;

            double numX = hom[0] * row + hom[1] * col + hom[2];
            double numY = hom[3] * row + hom[4] * col + hom[5];
            double w2 = w * w;
            a = (hom[0] * w - numX * hom[6]) / w2;
            b = (hom[1] * w - numX * hom[7]) / w2;
            c = (hom[3] * w - numY * hom[6]) / w2;
            d = (hom[4] * w - numY * hom[7]) / w2;
            return true;
        }

        /// <summary>矩阵是否可用：长度≥6、全部有限、线性部分不退化。</summary>
        public static bool IsUsableMatrix(double[]? matrix)
        {
            if (matrix == null || matrix.Length < 6)
                return false;

            for (int i = 0; i < 6; i++)
                if (!IsFinite(matrix[i]))
                    return false;

            double det = matrix[0] * matrix[3] - matrix[1] * matrix[2];
            return IsFinite(det) && Math.Abs(det) >= 1e-12;
        }

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        #endregion
    }
}
