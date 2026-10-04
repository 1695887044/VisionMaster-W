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
            "当前标定只有像素当量、不含机械坐标：坐标换算请用九点标定（像素当量模式只给 mm/px）";

        /// <summary>标定缺失/类型不对/矩阵损坏 → false（失配第一、二类）。</summary>
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

            if (!IsUsableMatrix(t.Matrix))
            {
                error = "标定数据损坏（矩阵缺失或退化）：请重新运行「标定」步骤";
                return false;
            }

            return true;
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

        /// <summary>像素 (Row, Col) → 机械 (X, Y)。</summary>
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

            var m = t!.Matrix;
            x = m[0] * row + m[1] * col + m[4];
            y = m[2] * row + m[3] * col + m[5];
            return true;
        }

        /// <summary>机械 (X, Y) → 像素 (Row, Col)（2×2 线性部分求逆；退化 → 明确失败）。</summary>
        public static bool TryMapMechanicalToPixel(
            CalibrationTransform? t, double x, double y, out double row, out double col, out string? error)
        {
            row = 0;
            col = 0;

            if (!CheckCalibrationUsable(t, out error))
                return false;

            var m = t!.Matrix;
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
        /// </summary>
        public static bool TryPixelAngleToMechanical(
            CalibrationTransform? t, double pixelAngleDeg, out double mechanicalAngleDeg, out string? error)
        {
            mechanicalAngleDeg = 0;

            if (!CheckCalibrationUsable(t, out error))
                return false;

            if (!IsFinite(pixelAngleDeg))
            {
                error = "像素角度无效（NaN/Inf）——请检查上游角度来源";
                return false;
            }

            var m = t!.Matrix;

            // ① 图像 x 轴 (Row=0, Col=1) 的映射方向
            double dx0 = m[1];
            double dy0 = m[3];
            if (Math.Sqrt(dx0 * dx0 + dy0 * dy0) < 1e-12)
            {
                error = "标定矩阵退化（图像 x 轴映射为 0）：请重新标定";
                return false;
            }

            // ② 转 θ 后的方向向量 (Row=sinθ, Col=cosθ) 经线性部分映射
            double rad = pixelAngleDeg * Math.PI / 180.0;
            double sin = Math.Sin(rad);
            double cos = Math.Cos(rad);
            double dx = m[0] * sin + m[1] * cos;
            double dy = m[2] * sin + m[3] * cos;

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
