using System;
using System.Collections.Generic;
using System.Text;

namespace Plugin.HandEye
{
    /// <summary>
    /// 手眼标定数学内核（**纯 System，无 HALCON / 无 WPF / 无第三方库**）。
    ///
    /// 为什么自己写而不是调 HALCON <c>calibrate_hand_eye</c>
    /// ---------
    /// HALCON 那条路要求先有 camera setup model + 标定板在每帧的观测位姿（<c>set_calib_data_observ_pose</c>），
    /// 即"必须先做内参标定、且靶标必须是标定板"。现场更常见的是：机器人走 N 个位姿，每个位姿下用
    /// **已有的定位算子**（模板匹配/圆查找）量同一个靶标，得到成对的 (机器人位姿, 靶标在相机中的位姿)。
    /// 这条路只需要成对数据，且能用合成数据反证（R38），所以自研。
    ///
    /// 解法
    /// ---------
    /// 相对运动形式：由绝对位姿构造 A' = A_j⁻¹·A_i、B' = B_j·B_i⁻¹，得到齐次方程
    ///     A' · X = X · B'        （eye-in-hand：X = T_gripper←camera）
    /// 取对数（李代数）后旋转部分变成  α = R_X · β，用 Park & Martin (1994) 的闭式解：
    ///     M = Σ β·αᵀ,  M = U·Σ·Vᵀ  ⇒  R_X = V·Uᵀ（再修正 det 为 +1）
    /// 平移部分在 R_X 已知后是一次最小二乘：(R_A − I)·t_X = R_X·t_B − t_A。
    ///
    /// 退化判据（**必须明确失败，不许返回"看起来合理"的矩阵**）
    /// ---------
    /// ① 位姿少于 <see cref="MinPairs"/> 组；
    /// ② 旋转激励不足：所有 β 里**最大**的旋转角小于 <see cref="MinRotationSpreadRad"/>（只平移 / 每次都回到同一姿态）；
    /// ③ 旋转轴几乎共线：M 的最小奇异值 / 最大奇异值 小于 <see cref="MinSingularRatio"/>；
    /// ④ 求解结果非有限数或 det(R) 与 +1 相差超过 <see cref="MaxDetError"/>。
    ///
    /// 这几条对应现场最常见的"标定做不出来却给了一个数"：机器人只在平面内平移、或每次都回到同一姿态。
    /// </summary>
    public static class HandEyeMath
    {
        #region 阈值（公开常量：断言与界面提示共用同一份数字，避免两处漂移）

        /// <summary>最少位姿组数（3 组是旋转求解的理论下限，工程上 6 组起才有冗余）</summary>
        public const int MinPairs = 3;

        /// <summary>工程建议的最少组数（界面提示用；不参与判定）</summary>
        public const int RecommendedPairs = 6;

        /// <summary>旋转激励下限（弧度）：所有相对旋转里最大的那个都不到 5° 视为"没转够"，不可解</summary>
        public const double MinRotationSpreadRad = 5.0 * Math.PI / 180.0;

        /// <summary>M 的最小/最大奇异值之比下限：低于此值说明旋转轴几乎共线（如只在同一平面内绕同一轴转）</summary>
        public const double MinSingularRatio = 1e-3;

        /// <summary>det(R) 允许偏差</summary>
        public const double MaxDetError = 1e-6;

        #endregion

        #region 公开结果类型

        /// <summary>一组标定样本：同一时刻的机器人位姿与靶标在相机中的位姿（都是 4×4 齐次矩阵）</summary>
        public readonly struct Sample
        {
            /// <summary>机器人位姿（T_base←gripper 或 T_base←flange，取决于标定模型）</summary>
            public readonly Mat4 Robot;

            /// <summary>靶标在相机坐标系中的位姿（T_camera←target）</summary>
            public readonly Mat4 Camera;

            public Sample(Mat4 robot, Mat4 camera)
            {
                Robot = robot;
                Camera = camera;
            }
        }

        /// <summary>求解质量：残差与条件数，供界面显示与断言判定</summary>
        public readonly struct Quality
        {
            /// <summary>旋转残差均方根（弧度）</summary>
            public readonly double RotationRmsRad;

            /// <summary>旋转残差最大值（弧度）</summary>
            public readonly double RotationMaxRad;

            /// <summary>平移残差均方根（与输入位姿同单位，通常 mm）</summary>
            public readonly double TranslationRms;

            /// <summary>平移残差最大值</summary>
            public readonly double TranslationMax;

            /// <summary>旋转求解的条件指标：M 的最小/最大奇异值之比（越小越病态）</summary>
            public readonly double SingularRatio;

            /// <summary>实际参与求解的相对运动组数</summary>
            public readonly int UsedPairs;

            public Quality(double rotationRmsRad, double rotationMaxRad, double translationRms, double translationMax, double singularRatio, int usedPairs)
            {
                RotationRmsRad = rotationRmsRad;
                RotationMaxRad = rotationMaxRad;
                TranslationRms = translationRms;
                TranslationMax = translationMax;
                SingularRatio = singularRatio;
                UsedPairs = usedPairs;
            }
        }

        #endregion

        #region 主入口

        /// <summary>
        /// 求解 A·X = X·B（eye-in-hand）。
        ///
        /// 输入是**绝对位姿**成对数据；内部统一以第 0 组为基准构造相对运动，
        /// 因此"机器人是否在同一个基坐标系下"只影响 A 的绝对值、不影响相对量（前提是所有 A 同基准、所有 B 同基准）。
        /// </summary>
        /// <param name="samples">标定样本，至少 <see cref="MinPairs"/> 组</param>
        /// <param name="x">求得的 X（失败时为 <see cref="Mat4.Identity"/>）</param>
        /// <param name="quality">质量指标（失败时为零值）</param>
        /// <param name="error">失败原因（中文，直接可上屏；成功时为 null）</param>
        /// <returns>成功返回 true</returns>
        public static bool SolveAXEqualsXB(IReadOnlyList<Sample> samples, out Mat4 x, out Quality quality, out string? error)
        {
            x = Mat4.Identity;
            quality = default;
            error = null;

            if (!TryBuildRelativeXB(samples, out List<Mat4> relA, out List<Mat4> relB, out error))
            {
                return false;
            }

            return SolveRelative(relA, relB, out x, out quality, out error);
        }

        /// <summary>
        /// eye-in-hand 的相对运动：A_rel = A_0⁻¹·A_i，B_rel = B_0·B_i⁻¹。
        ///
        /// 推导：靶标固定在基座上 ⇒ A_i·X·B_i = const（A_i = T_base←gripper，B_i = T_camera←target，
        /// X = T_gripper←camera）。取 i 与 0 相消得
        ///     (A_0⁻¹·A_i)·X = X·(B_0·B_i⁻¹)
        /// **两边都必须是"后项取逆"的同一侧**；写成 B_i·B_0⁻¹ 会得到一个自洽但错误的方程
        /// （实测：合成数据上解出的 X 旋转差 1 rad，而残差只有 3.6e-8 —— 残差小不代表解对，
        ///  这正是必须用"已知 X 往返"而不是"残差小"来验收的原因）。
        /// </summary>
        private static bool TryBuildRelativeXB(
            IReadOnlyList<Sample> samples,
            out List<Mat4> relA,
            out List<Mat4> relB,
            out string? error)
        {
            relA = new List<Mat4>();
            relB = new List<Mat4>();
            error = null;

            if (samples == null || samples.Count < MinPairs)
            {
                error = $"样本不足：至少需要 {MinPairs} 组（当前 {(samples?.Count ?? 0)} 组）";
                return false;
            }

            int n = samples.Count;
            Mat4 a0Inv = Mat4.InverseRigid(samples[0].Robot);
            Mat4 b0 = samples[0].Camera;

            for (int i = 1; i < n; i++)
            {
                relA.Add(Mat4.Multiply(a0Inv, samples[i].Robot));
                relB.Add(Mat4.Multiply(b0, Mat4.InverseRigid(samples[i].Camera)));
            }

            return true;
        }

        /// <summary>
        /// eye-to-hand（眼在手外）的相对运动：A_rel = A_0⁻¹·A_i，**B_rel = B_0⁻¹·B_i**。
        ///
        /// 注意它和 eye-in-hand 的 B 侧**不是同一个式子**（那里是 B_0·B_i⁻¹）。推导：
        /// 靶标装在法兰上、相机固定 ⇒ A_i·Z = X·B_i（Z = T_flange←target，X = T_base←camera）。
        /// 由 A_0·Z = X·B_0 得 A_0⁻¹·X = Z·B_0⁻¹，代回 A_0⁻¹·A_i·Z = A_0⁻¹·X·B_i 得
        ///     (A_0⁻¹·A_i)·Z = Z·(B_0⁻¹·B_i)
        /// 写成 B_0·B_i⁻¹ 也能"解出个数"，但那是另一个方程的答案：合成数据上旋转差 2.7 rad、
        /// 残差 0.94 rad（实测 2026-10-23）。**两个装法的 B 侧差一个取逆位置，是这套数学最容易写错的一处。**
        /// </summary>
        private static bool TryBuildRelativeZB(
            IReadOnlyList<Sample> samples,
            out List<Mat4> relA,
            out List<Mat4> relB,
            out string? error)
        {
            relA = new List<Mat4>();
            relB = new List<Mat4>();
            error = null;

            if (samples == null || samples.Count < MinPairs)
            {
                error = $"样本不足：至少需要 {MinPairs} 组（当前 {(samples?.Count ?? 0)} 组）";
                return false;
            }

            int n = samples.Count;
            Mat4 a0Inv = Mat4.InverseRigid(samples[0].Robot);
            Mat4 b0Inv = Mat4.InverseRigid(samples[0].Camera);

            for (int i = 1; i < n; i++)
            {
                relA.Add(Mat4.Multiply(a0Inv, samples[i].Robot));
                relB.Add(Mat4.Multiply(b0Inv, samples[i].Camera));
            }

            return true;
        }

        /// <summary>
        /// 解相对运动形式的 A_rel·W = W·B_rel。
        ///
        /// 两种装法在这里汇合：eye-in-hand 求出的 W 就是 X（T_gripper←camera）；
        /// eye-to-hand 求出的 W 是 Z（T_flange←target），再换算成 X。
        /// 退化检查（旋转激励 / 旋转轴共线 / 平移病态 / det(R)）与残差统计只写这一份，
        /// **所以"什么算解不出来"对两种装法是同一套判据**，不会各写一份慢慢漂开。
        /// </summary>
        private static bool SolveRelative(
            List<Mat4> relA,
            List<Mat4> relB,
            out Mat4 x,
            out Quality quality,
            out string? error)
        {
            x = Mat4.Identity;
            quality = default;
            error = null;

            if (relA.Count == 0)
            {
                error = $"样本不足：至少需要 {MinPairs} 组（当前 1 组）";
                return false;
            }
            // 旋转激励检查：**至少要有一组相对旋转"转得够多"**。
            //
            // 判据刻意用 Max 而不是 (Max − Min)：转轴是否张开由下面的奇异值比把关，这里只回答
            // "有没有转"。用角度区间会误杀完全可解的数据 —— 例如绕 X / Y / Z 各 60° 取三点，
            // 三个相对旋转的角度一模一样，(Max − Min) = 0，但它恰恰是激励最充分的样本；
            // 现场对称取点（±45° 两轴）同理。反过来"角度有差但都绕同一根轴"由奇异值比拦，
            // 不会被这条放过。
            var angles = new double[relB.Count];
            for (int i = 0; i < relB.Count; i++)
            {
                angles[i] = RotationAngle(relB[i].Rotation);
            }
            double maxAngle = Max(angles);
            if (maxAngle < MinRotationSpreadRad)
            {
                error = $"旋转激励不足：相机侧最大旋转只有 {maxAngle * 180.0 / Math.PI:0.###}°（要求 ≥ {MinRotationSpreadRad * 180.0 / Math.PI:0.###}°）。" +
                        "请让机器人以不同姿态取点（只平移或每次都回到同一姿态无法解出旋转）";
                return false;
            }

            // 旋转：α = R_X · β，Park-Martin 闭式解 R_X = V·Uᵀ
            var beta = new double[relB.Count][];
            var alpha = new double[relA.Count][];
            for (int i = 0; i < relB.Count; i++)
            {
                if (!TryRotationLog(relB[i].Rotation, out beta[i], out string? e1))
                {
                    error = $"第 {i + 1} 组相对旋转无法取对数：{e1}";
                    return false;
                }
                if (!TryRotationLog(relA[i].Rotation, out alpha[i], out string? e2))
                {
                    error = $"第 {i + 1} 组机器人侧旋转无法取对数：{e2}";
                    return false;
                }
            }

            // M = Σ β·αᵀ
            var m = new double[3, 3];
            for (int i = 0; i < beta.Length; i++)
            {
                for (int r = 0; r < 3; r++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        m[r, c] += beta[i][r] * alpha[i][c];
                    }
                }
            }

            if (!Svd3(m, out double[,] u, out double[] s, out double[,] v))
            {
                error = "旋转求解失败：SVD 不收敛（数据可能含 NaN/Inf）";
                return false;
            }

            double singularRatio = s[2] > 1e-15 ? s[0] / s[2] : double.PositiveInfinity;
            if (double.IsNaN(singularRatio) || double.IsInfinity(singularRatio))
            {
                error = "旋转求解失败：旋转信息矩阵奇异（最小奇异值为 0），所有样本的相对旋转共线";
                return false;
            }
            if (s[2] / s[0] < MinSingularRatio)
            {
                error = $"旋转轴几乎共线：奇异值比 {s[2] / s[0]:0.######e+0} 低于下限 {MinSingularRatio:0.###e+0}。" +
                        "请让机器人绕**至少两个不平行的轴**转动";
                return false;
            }

            // R = V·Uᵀ
            var rx = new Mat3();
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    double sum = 0;
                    for (int k = 0; k < 3; k++)
                    {
                        sum += v[r, k] * u[c, k];
                    }
                    rx[r, c] = sum;
                }
            }

            if (rx.Determinant < 0)
            {
                // 反射修正：翻转 V 的最后一列（对应最小奇异值方向）
                for (int r = 0; r < 3; r++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        double sum = 0;
                        for (int k = 0; k < 3; k++)
                        {
                            double vk = (k == 2) ? -v[r, k] : v[r, k];
                            sum += vk * u[c, k];
                        }
                        rx[r, c] = sum;
                    }
                }
            }

            double detErr = Math.Abs(rx.Determinant - 1.0);
            if (detErr > MaxDetError)
            {
                error = $"旋转矩阵非法：det(R) = {rx.Determinant:0.######}（应接近 1，偏差 {detErr:0.######e+0}）";
                return false;
            }

            // 平移：(R_A − I)·t_X = R_X·t_B − t_A，按 3N 行堆叠做最小二乘
            var ata = new double[3, 3];
            var atb = new double[3];
            for (int i = 0; i < relA.Count; i++)
            {
                Mat3 ra = relA[i].Rotation;
                Vec3 ta = relA[i].Translation;
                Vec3 tb = relB[i].Translation;
                Vec3 rxtb = Mat3.Multiply(rx, tb);

                for (int r = 0; r < 3; r++)
                {
                    var row = new double[3];
                    for (int c = 0; c < 3; c++)
                    {
                        row[c] = ra[r, c] - (r == c ? 1.0 : 0.0);
                    }
                    double rhs = rxtb[r] - ta[r];
                    for (int a = 0; a < 3; a++)
                    {
                        for (int b = 0; b < 3; b++)
                        {
                            ata[a, b] += row[a] * row[b];
                        }
                        atb[a] += row[a] * rhs;
                    }
                }
            }

            if (!TrySolve3x3(ata, atb, out Vec3 tx))
            {
                error = "平移求解失败：(R_A − I) 堆叠矩阵奇异，样本的平移方向不足以确定平移（让机器人在三个方向上都有位移）";
                return false;
            }

            if (!IsFinite(rx) || !IsFinite(tx))
            {
                error = "求解结果含非有限数（NaN/Inf）";
                return false;
            }

            x = Mat4.FromRt(rx, tx);

            // 残差：把 X 代回每一组相对运动，比较 A·X 与 X·B
            double rotSumSq = 0, rotMax = 0, trSumSq = 0, trMax = 0;
            int used = 0;
            for (int i = 0; i < relA.Count; i++)
            {
                Mat4 left = Mat4.Multiply(relA[i], x);
                Mat4 right = Mat4.Multiply(x, relB[i]);

                double rotErr = RotationAngle(Mat3.Multiply(Mat3.Transpose(left.Rotation), right.Rotation));
                Vec3 d = Vec3.Subtract(left.Translation, right.Translation);
                double trErr = d.Length;

                rotSumSq += rotErr * rotErr;
                trSumSq += trErr * trErr;
                if (rotErr > rotMax) rotMax = rotErr;
                if (trErr > trMax) trMax = trErr;
                used++;
            }

            quality = new Quality(
                Math.Sqrt(rotSumSq / used),
                rotMax,
                Math.Sqrt(trSumSq / used),
                trMax,
                s[2] / s[0],
                used);
            return true;
        }

        /// <summary>
        /// 求解 A·Z = X·B（eye-to-hand，中文"眼在手外"：相机固定、靶标装在法兰上）。
        ///
        /// **记号**（和 AX=XB 不是同一套，别混）：
        ///   · A_i = T_base←flange  机器人给的法兰位姿
        ///   · B_i = T_camera←target 相机看到的靶标位姿
        ///   · Z   = T_flange←target 靶标在法兰上的安装位姿（固定不变、未知）
        ///   · X   = T_base←camera   要交付的那个 4×4（相机在基座下的位姿）
        ///
        /// **化归**（这是本方法只多 30 行、却能复用整套 Park-Martin 的原因）：
        /// 靶标在世界里的位姿有两种算法 —— 走机器人是 A_i·Z，走相机是 X·B_i，两者必须相等：
        ///     A_i·Z = X·B_i
        /// 拿第 0 组消掉绝对值：
        ///     (A_0⁻¹·A_i)·Z = Z·(B_0·B_i⁻¹)
        /// 右边这半截与 <see cref="SolveAXEqualsXB"/> 内部构造的相对运动**逐字相同**，
        /// 于是"眼在手外"就是"把 AX=XB 解出来的那个 X 叫 Z"，再用 Z 换算出真正的 X：
        ///     X = A_0·Z·B_0⁻¹
        ///
        /// **为什么不用 HALCON 的 calibrate_hand_eye**：见 Help/05-dev-solver-theory.md 第 5 节。
        /// </summary>
        /// <param name="samples">成对数据，至少 <see cref="MinPairs"/> 组</param>
        /// <param name="cameraInBase">求得的 X = T_base←camera（失败时为 Identity）</param>
        /// <param name="targetOnFlange">求得的 Z = T_flange←target（失败时为 Identity；只用于诊断与复核）</param>
        /// <param name="quality">质量指标（失败时为零值）</param>
        /// <param name="error">失败原因（中文，直接可上屏；成功时为 null）</param>
        /// <returns>成功返回 true</returns>
        public static bool SolveAXEqualsZB(
            IReadOnlyList<Sample> samples,
            out Mat4 cameraInBase,
            out Mat4 targetOnFlange,
            out Quality quality,
            out string? error)
        {
            cameraInBase = Mat4.Identity;
            targetOnFlange = Mat4.Identity;
            quality = default;
            error = null;

            if (samples == null || samples.Count < MinPairs)
            {
                error = $"样本不足：至少需要 {MinPairs} 组（当前 {(samples?.Count ?? 0)} 组）";
                return false;
            }

            // 第一步：解 Z。相对运动取 eye-to-hand 那一套（B 侧是 B_0⁻¹·B_i，不是 B_0·B_i⁻¹）；
            // 退化检查（旋转激励 / 共线 / 平移病态 / det）与 AX=XB 共用同一份 SolveRelative，
            // 所以两种装法"什么算解不出来"是同一套判据。
            if (!TryBuildRelativeZB(samples, out List<Mat4> relA, out List<Mat4> relB, out error))
            {
                return false;
            }

            if (!SolveRelative(relA, relB, out Mat4 z, out Quality coreQuality, out string? solveError))
            {
                error = solveError;
                return false;
            }

            // 第二步：Z → X。用第 0 组（与上面消元选的基准一致；数据自洽时任意 i 等价）。
            Mat4 x = Mat4.Multiply(Mat4.Multiply(samples[0].Robot, z), Mat4.InverseRigid(samples[0].Camera));

            double detErr = Math.Abs(x.Rotation.Determinant - 1.0);
            if (detErr > MaxDetError)
            {
                error = $"换算出相机位姿后旋转矩阵非法：det(R) = {x.Rotation.Determinant:0.######}（应接近 1，偏差 {detErr:0.######e+0}）";
                return false;
            }

            if (!IsFinite(x.Rotation) || !IsFinite(x.Translation))
            {
                error = "求解结果含非有限数（NaN/Inf）";
                return false;
            }

            // 第三步：**绝对式重新验算**，不复用第一步的残差。
            //     E_i = (X·B_i)⁻¹·(A_i·Z) 应为单位阵
            // 这一步专门给"Z → X 换算"兜底：相对式恒等式在 Z 上成立，换算式若写错（例如把
            // B_0⁻¹ 写成 B_0），相对式残差照样接近 0，但绝对式会立刻炸开。
            // 同理必须从 i = 1 起算：i = 0 那组是换算的基准，残差恒为 0，算进去会把 RMS 拉低。
            double rotSumSq = 0, rotMax = 0, trSumSq = 0, trMax = 0;
            int used = 0;

            for (int i = 1; i < samples.Count; i++)
            {
                Mat4 left = Mat4.Multiply(samples[i].Robot, z);   // A_i·Z：靶标在世界里（走机器人）
                Mat4 right = Mat4.Multiply(x, samples[i].Camera); // X·B_i：靶标在世界里（走相机）
                Mat4 e = Mat4.Multiply(Mat4.InverseRigid(right), left);

                double rotErr = RotationAngle(e.Rotation);
                double trErr = e.Translation.Length;

                rotSumSq += rotErr * rotErr;
                trSumSq += trErr * trErr;
                if (rotErr > rotMax) rotMax = rotErr;
                if (trErr > trMax) trMax = trErr;
                used++;
            }

            if (used == 0)
            {
                error = $"样本不足：至少需要 {MinPairs} 组才谈得上验算（当前 {samples.Count} 组）";
                return false;
            }

            quality = new Quality(
                Math.Sqrt(rotSumSq / used),
                rotMax,
                Math.Sqrt(trSumSq / used),
                trMax,
                coreQuality.SingularRatio,
                used);

            cameraInBase = x;
            targetOnFlange = z;
            return true;
        }

        #endregion

        #region 旋转工具

        /// <summary>
        /// 机器人控制器最常见的姿态表达：**ZYX 欧拉角（度）**。
        /// 旋转矩阵 = Rz(rz)·Ry(ry)·Rx(rx)（先绕 Z、再绕 Y、最后绕 X，即 yaw-pitch-roll）。
        ///
        /// **只支持这一种约定**：ABB / KUKA / FANUC / 埃斯顿都能用 ZYX 表达，但各自的
        /// 内旋外旋、零位、角度符号方向并不统一。接一个新品牌时，必须先用
        /// "只绕单轴转 90°"的实测数据核对三个符号，**不要照文档填**——
        /// 符号错了的表现是"标定残差正常、抓取点整体镜像"，靠残差查不出来。
        /// </summary>
        public static Mat3 EulerZyxDegToMat3(double rxDeg, double ryDeg, double rzDeg)
        {
            double k = Math.PI / 180.0;
            double cx = Math.Cos(rxDeg * k), sx = Math.Sin(rxDeg * k);
            double cy = Math.Cos(ryDeg * k), sy = Math.Sin(ryDeg * k);
            double cz = Math.Cos(rzDeg * k), sz = Math.Sin(rzDeg * k);

            // Rz·Ry·Rx（行主序 3×3）
            var m = new Mat3();
            m[0, 0] = cz * cy; m[0, 1] = cz * sy * sx - sz * cx; m[0, 2] = cz * sy * cx + sz * sx;
            m[1, 0] = sz * cy; m[1, 1] = sz * sy * sx + cz * cx; m[1, 2] = sz * sy * cx - cz * sx;
            m[2, 0] = -sy;     m[2, 1] = cy * sx;                m[2, 2] = cy * cx;
            return m;
        }

        /// <summary>由"位置(mm) + ZYX 欧拉角(度)"构造齐次位姿</summary>
        public static Mat4 PoseFromXyzEulerZyxDeg(
            double x, double y, double z, double rxDeg, double ryDeg, double rzDeg)
            => Mat4.FromRt(EulerZyxDegToMat3(rxDeg, ryDeg, rzDeg), new Vec3(x, y, z));

        /// <summary>
        /// <see cref="EulerZyxDegToMat3"/> 的逆：旋转矩阵 → ZYX 欧拉角（度）。
        ///
        /// 存在的理由是**往返自证**：标定出来的 4×4 要能被写回 CSV、也要能被人在界面上
        /// 读成"姿态角"，否则"解对了但写出去错了"这类错没有闸能拦。
        /// 万向锁（|R[2,0]| ≈ 1，即 ry = ±90°）时欧拉角不唯一：本实现取 rx = 0，
        /// 把剩下的旋转全部记到 rz 上 —— **往返矩阵仍然一致**，只是三个角度不唯一。
        /// </summary>
        public static bool TryMat3ToEulerZyxDeg(
            Mat3 r, out double rxDeg, out double ryDeg, out double rzDeg, out string? error)
        {
            rxDeg = ryDeg = rzDeg = 0.0;
            error = null;

            if (r == null)
            {
                error = "旋转矩阵为 null";
                return false;
            }

            double sinY = -r[2, 0];
            if (sinY > 1.0) sinY = 1.0;
            if (sinY < -1.0) sinY = -1.0;

            double ry = Math.Asin(sinY);
            double rx, rz;

            if (Math.Abs(sinY) > 0.999999)
            {
                // 万向锁：ry = ±90°，rx 与 rz 只剩一个自由度，约定 rx = 0
                rx = 0.0;
                rz = -Math.Atan2(r[0, 1], r[1, 1]);
            }
            else
            {
                rx = Math.Atan2(r[2, 1], r[2, 2]);
                rz = Math.Atan2(r[1, 0], r[0, 0]);
            }

            double k = 180.0 / Math.PI;
            rxDeg = rx * k;
            ryDeg = ry * k;
            rzDeg = rz * k;
            return true;
        }

        /// <summary>齐次位姿 → "位置(mm) + ZYX 欧拉角(度)"（写 CSV、上屏显示共用）</summary>
        public static void Mat4ToXyzEulerZyxDeg(
            Mat4 m,
            out double x, out double y, out double z,
            out double rxDeg, out double ryDeg, out double rzDeg)
        {
            Vec3 t = m.Translation;
            x = t.X;
            y = t.Y;
            z = t.Z;
            TryMat3ToEulerZyxDeg(m.Rotation, out rxDeg, out ryDeg, out rzDeg, out _);
        }

        public static double RotationAngle(Mat3 r)
        {
            double trace = r[0, 0] + r[1, 1] + r[2, 2];
            double c = (trace - 1.0) * 0.5;
            if (c > 1.0) c = 1.0;
            if (c < -1.0) c = -1.0;
            return Math.Acos(c);
        }

        /// <summary>旋转矩阵 → 旋转向量（轴×角）。接近 π 时走反对称部分不成立的备用分支。</summary>
        public static bool TryRotationLog(Mat3 r, out double[] w, out string? error)
        {
            w = new double[3];
            error = null;

            // NaN/Inf 必须在进入 π 分支前拦住：RotationAngle 会把 NaN 传下去，
            // 而 π 分支里的 Math.Sign(NaN) 直接抛 ArithmeticException。
            if (!IsFinite(r))
            {
                error = "旋转矩阵含非法数值（NaN/Inf）——请检查该组位姿数据";
                return false;
            }

            double angle = RotationAngle(r);
            if (angle < 1e-12)
            {
                return true; // 零旋转
            }

            if (angle < Math.PI - 1e-6)
            {
                double k = angle / (2.0 * Math.Sin(angle));
                w[0] = k * (r[2, 1] - r[1, 2]);
                w[1] = k * (r[0, 2] - r[2, 0]);
                w[2] = k * (r[1, 0] - r[0, 1]);
                return true;
            }

            // 接近 π：反对称部分退化，用 (R + I)/2 = a·aᵀ 取轴
            double xx = (r[0, 0] + 1.0) * 0.5;
            double yy = (r[1, 1] + 1.0) * 0.5;
            double zz = (r[2, 2] + 1.0) * 0.5;
            int maxIdx = 0;
            double maxVal = xx;
            if (yy > maxVal) { maxIdx = 1; maxVal = yy; }
            if (zz > maxVal) { maxIdx = 2; maxVal = zz; }
            if (maxVal <= 1e-12)
            {
                error = "旋转接近 180° 且无法确定旋转轴（矩阵退化）";
                return false;
            }

            double axisMax = Math.Sqrt(maxVal);
            double ax = (r[2, 1] - r[1, 2]) / (2.0 * Math.Sin(angle));
            double ay = (r[0, 2] - r[2, 0]) / (2.0 * Math.Sin(angle));
            double az = (r[1, 0] - r[0, 1]) / (2.0 * Math.Sin(angle));

            // 反对称部分在 π 附近不可靠，改用对角元符号恢复
            double sx = Math.Sign(r[2, 1] - r[1, 2]);
            double sy = Math.Sign(r[0, 2] - r[2, 0]);
            double sz = Math.Sign(r[1, 0] - r[0, 1]);

            var a = new double[3];
            a[0] = maxIdx == 0 ? axisMax : (r[0, 1] + r[1, 0]) / (4.0 * axisMax);
            a[1] = maxIdx == 1 ? axisMax : (r[0, 2] + r[2, 0]) / (4.0 * axisMax);
            a[2] = maxIdx == 2 ? axisMax : (r[1, 2] + r[2, 1]) / (4.0 * axisMax);

            // 定符号：与反对称部分同向
            double dot = a[0] * sx + a[1] * sy + a[2] * sz;
            if (dot < 0)
            {
                a[0] = -a[0];
                a[1] = -a[1];
                a[2] = -a[2];
            }

            double len = Math.Sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2]);
            if (len < 1e-12)
            {
                // 最后兜底：用反对称近似（此时角度≈π，轴向对精度影响有限）
                a[0] = ax; a[1] = ay; a[2] = az;
                len = Math.Sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2]);
                if (len < 1e-12)
                {
                    error = "旋转接近 180° 且轴向无法恢复";
                    return false;
                }
            }

            w[0] = a[0] / len * angle;
            w[1] = a[1] / len * angle;
            w[2] = a[2] / len * angle;
            return true;
        }

        /// <summary>旋转向量 → 旋转矩阵（Rodrigues），合成测试数据用</summary>
        public static Mat3 RotationExp(Vec3 w)
        {
            double angle = w.Length;
            if (angle < 1e-15)
            {
                return Mat3.Identity;
            }
            double x = w.X / angle, y = w.Y / angle, z = w.Z / angle;
            double c = Math.Cos(angle), s = Math.Sin(angle), t = 1 - c;

            var r = new Mat3();
            r[0, 0] = t * x * x + c;
            r[0, 1] = t * x * y - s * z;
            r[0, 2] = t * x * z + s * y;
            r[1, 0] = t * x * y + s * z;
            r[1, 1] = t * y * y + c;
            r[1, 2] = t * y * z - s * x;
            r[2, 0] = t * x * z - s * y;
            r[2, 1] = t * y * z + s * x;
            r[2, 2] = t * z * z + c;
            return r;
        }

        #endregion

        #region 线性代数（3×3：Jacobi 对称特征分解 → SVD / 线性方程）

        /// <summary>3×3 奇异值分解 M = U·Σ·Vᵀ（经 MᵀM 的 Jacobi 特征分解；只用于本文件的旋转求解）</summary>
        internal static bool Svd3(double[,] m, out double[,] u, out double[] s, out double[,] v)
        {
            var mtm = new double[3, 3];
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    double sum = 0;
                    for (int k = 0; k < 3; k++)
                    {
                        sum += m[k, i] * m[k, j];
                    }
                    mtm[i, j] = sum;
                }
            }

            if (!JacobiEigenSymmetric(mtm, out double[] lambda, out v))
            {
                u = new double[3, 3];
                s = new double[3];
                return false;
            }

            // 按特征值降序排列
            var order = new[] { 0, 1, 2 };
            Array.Sort(order, (a, b) => lambda[b].CompareTo(lambda[a]));

            s = new double[3];
            var vSorted = new double[3, 3];
            for (int k = 0; k < 3; k++)
            {
                s[k] = Math.Sqrt(Math.Max(lambda[order[k]], 0.0));
                for (int r = 0; r < 3; r++)
                {
                    vSorted[r, k] = v[r, order[k]];
                }
            }
            v = vSorted;

            // U = M·V·Σ⁻¹
            u = new double[3, 3];
            for (int k = 0; k < 3; k++)
            {
                if (s[k] < 1e-14)
                {
                    // 退化方向补一组正交基（用单位向量兜底，随后正交化）
                    u[0, k] = k == 0 ? 1 : 0;
                    u[1, k] = k == 1 ? 1 : 0;
                    u[2, k] = k == 2 ? 1 : 0;
                    continue;
                }
                for (int r = 0; r < 3; r++)
                {
                    double sum = 0;
                    for (int j = 0; j < 3; j++)
                    {
                        sum += m[r, j] * v[j, k];
                    }
                    u[r, k] = sum / s[k];
                }
            }

            return true;
        }

        /// <summary>对称矩阵的 Jacobi 特征分解（A = V·diag(λ)·Vᵀ）</summary>
        internal static bool JacobiEigenSymmetric(double[,] a, out double[] eigenValues, out double[,] eigenVectors)
        {
            int n = 3;
            var m = (double[,])a.Clone();
            var v = new double[n, n];

            // 非有限输入会在下面的旋转里让 Math.Sign(NaN) 抛异常（.NET 行为）；
            // 这里是 SVD 的唯一入口，先挡一次，返回 false 让调用方走"数据含 NaN"的失败文案。
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    if (double.IsNaN(m[i, j]) || double.IsInfinity(m[i, j]))
                    {
                        eigenValues = new double[n];
                        eigenVectors = new double[n, n];
                        return false;
                    }
                }
            }

            for (int i = 0; i < n; i++)
            {
                v[i, i] = 1.0;
            }

            for (int sweep = 0; sweep < 100; sweep++)
            {
                double off = 0;
                for (int p = 0; p < n - 1; p++)
                {
                    for (int q = p + 1; q < n; q++)
                    {
                        off += m[p, q] * m[p, q];
                    }
                }
                if (off < 1e-30)
                {
                    break;
                }

                for (int p = 0; p < n - 1; p++)
                {
                    for (int q = p + 1; q < n; q++)
                    {
                        if (Math.Abs(m[p, q]) < 1e-18)
                        {
                            continue;
                        }
                        double theta = (m[q, q] - m[p, p]) / (2.0 * m[p, q]);
                        double t = Math.Sign(theta == 0 ? 1.0 : theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1.0));
                        double c = 1.0 / Math.Sqrt(t * t + 1.0);
                        double s = t * c;

                        for (int k = 0; k < n; k++)
                        {
                            double mkp = m[k, p];
                            double mkq = m[k, q];
                            m[k, p] = c * mkp - s * mkq;
                            m[k, q] = s * mkp + c * mkq;
                        }
                        for (int k = 0; k < n; k++)
                        {
                            double mpk = m[p, k];
                            double mqk = m[q, k];
                            m[p, k] = c * mpk - s * mqk;
                            m[q, k] = s * mpk + c * mqk;
                        }
                        for (int k = 0; k < n; k++)
                        {
                            double vkp = v[k, p];
                            double vkq = v[k, q];
                            v[k, p] = c * vkp - s * vkq;
                            v[k, q] = s * vkp + c * vkq;
                        }
                    }
                }
            }

            eigenValues = new double[n];
            for (int i = 0; i < n; i++)
            {
                eigenValues[i] = m[i, i];
            }
            eigenVectors = v;

            for (int i = 0; i < n; i++)
            {
                if (double.IsNaN(eigenValues[i]) || double.IsInfinity(eigenValues[i]))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>3×3 线性方程组 A·x = b（高斯消元带部分主元）</summary>
        internal static bool TrySolve3x3(double[,] a, double[] b, out Vec3 x)
        {
            x = new Vec3();
            var m = new double[3, 4];
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    m[r, c] = a[r, c];
                }
                m[r, 3] = b[r];
            }

            for (int col = 0; col < 3; col++)
            {
                int pivot = col;
                for (int r = col + 1; r < 3; r++)
                {
                    if (Math.Abs(m[r, col]) > Math.Abs(m[pivot, col]))
                    {
                        pivot = r;
                    }
                }
                if (Math.Abs(m[pivot, col]) < 1e-12)
                {
                    return false;
                }
                if (pivot != col)
                {
                    for (int c = 0; c < 4; c++)
                    {
                        double tmp = m[col, c];
                        m[col, c] = m[pivot, c];
                        m[pivot, c] = tmp;
                    }
                }
                for (int r = col + 1; r < 3; r++)
                {
                    double f = m[r, col] / m[col, col];
                    for (int c = col; c < 4; c++)
                    {
                        m[r, c] -= f * m[col, c];
                    }
                }
            }

            var res = new double[3];
            for (int r = 2; r >= 0; r--)
            {
                double sum = m[r, 3];
                for (int c = r + 1; c < 3; c++)
                {
                    sum -= m[r, c] * res[c];
                }
                res[r] = sum / m[r, r];
            }

            x = new Vec3(res[0], res[1], res[2]);
            return IsFinite(x);
        }

        #endregion

        #region 小工具

        internal static bool IsFinite(Mat3 r)
        {
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    if (double.IsNaN(r[i, j]) || double.IsInfinity(r[i, j]))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        internal static bool IsFinite(Vec3 v)
            => !double.IsNaN(v.X) && !double.IsInfinity(v.X)
            && !double.IsNaN(v.Y) && !double.IsInfinity(v.Y)
            && !double.IsNaN(v.Z) && !double.IsInfinity(v.Z);

        private static double Max(double[] a)
        {
            double m = a[0];
            for (int i = 1; i < a.Length; i++)
            {
                if (a[i] > m) m = a[i];
            }
            return m;
        }

        private static double Min(double[] a)
        {
            double m = a[0];
            for (int i = 1; i < a.Length; i++)
            {
                if (a[i] < m) m = a[i];
            }
            return m;
        }

        #endregion
    }

    #region 值类型

    /// <summary>三维向量</summary>
    public readonly struct Vec3
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public Vec3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>按分量下标取值（0=X, 1=Y, 2=Z），供矩阵堆叠代码使用</summary>
        public double this[int i] => i == 0 ? X : (i == 1 ? Y : Z);

        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

        public static Vec3 Subtract(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static Vec3 operator *(Vec3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);

        public override string ToString() => $"({X:0.####}, {Y:0.####}, {Z:0.####})";
    }

    /// <summary>3×3 矩阵（行主序，索引器 [行, 列]）</summary>
    public sealed class Mat3
    {
        private readonly double[] _m = new double[9];

        public double this[int r, int c]
        {
            get => _m[r * 3 + c];
            set => _m[r * 3 + c] = value;
        }

        public static Mat3 Identity
        {
            get
            {
                var r = new Mat3();
                r[0, 0] = 1; r[1, 1] = 1; r[2, 2] = 1;
                return r;
            }
        }

        public double Determinant =>
            this[0, 0] * (this[1, 1] * this[2, 2] - this[1, 2] * this[2, 1])
          - this[0, 1] * (this[1, 0] * this[2, 2] - this[1, 2] * this[2, 0])
          + this[0, 2] * (this[1, 0] * this[2, 1] - this[1, 1] * this[2, 0]);

        public static Mat3 Multiply(Mat3 a, Mat3 b)
        {
            var r = new Mat3();
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    double sum = 0;
                    for (int k = 0; k < 3; k++)
                    {
                        sum += a[i, k] * b[k, j];
                    }
                    r[i, j] = sum;
                }
            }
            return r;
        }

        public static Mat3 Transpose(Mat3 a)
        {
            var r = new Mat3();
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    r[i, j] = a[j, i];
                }
            }
            return r;
        }

        public static Vec3 Multiply(Mat3 a, Vec3 v) => new(
            a[0, 0] * v.X + a[0, 1] * v.Y + a[0, 2] * v.Z,
            a[1, 0] * v.X + a[1, 1] * v.Y + a[1, 2] * v.Z,
            a[2, 0] * v.X + a[2, 1] * v.Y + a[2, 2] * v.Z);
    }

    /// <summary>4×4 齐次变换矩阵（行主序，索引器 [行, 列]）</summary>
    public sealed class Mat4
    {
        private readonly double[] _m = new double[16];

        public double this[int r, int c]
        {
            get => _m[r * 4 + c];
            set => _m[r * 4 + c] = value;
        }

        public static Mat4 Identity
        {
            get
            {
                var r = new Mat4();
                r[0, 0] = 1; r[1, 1] = 1; r[2, 2] = 1; r[3, 3] = 1;
                return r;
            }
        }

        public Mat3 Rotation
        {
            get
            {
                var r = new Mat3();
                for (int i = 0; i < 3; i++)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        r[i, j] = this[i, j];
                    }
                }
                return r;
            }
        }

        public Vec3 Translation => new(this[0, 3], this[1, 3], this[2, 3]);

        public static Mat4 FromRt(Mat3 r, Vec3 t)
        {
            var m = new Mat4();
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    m[i, j] = r[i, j];
                }
            }
            m[0, 3] = t.X;
            m[1, 3] = t.Y;
            m[2, 3] = t.Z;
            m[3, 3] = 1.0;
            return m;
        }

        public static Mat4 Multiply(Mat4 a, Mat4 b)
        {
            var r = new Mat4();
            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < 4; j++)
                {
                    double sum = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        sum += a[i, k] * b[k, j];
                    }
                    r[i, j] = sum;
                }
            }
            return r;
        }

        /// <summary>刚体逆：T⁻¹ = [Rᵀ, −Rᵀ·t]</summary>
        public static Mat4 InverseRigid(Mat4 a)
        {
            Mat3 rt = Mat3.Transpose(a.Rotation);
            Vec3 t = a.Translation;
            Vec3 nt = Mat3.Multiply(rt, t) * -1.0;
            return FromRt(rt, nt);
        }

        public double[] ToArray()
        {
            var r = new double[16];
            Array.Copy(_m, r, 16);
            return r;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 4; i++)
            {
                sb.Append('[');
                for (int j = 0; j < 4; j++)
                {
                    sb.Append(this[i, j].ToString("0.######").PadLeft(12));
                }
                sb.AppendLine(" ]");
            }
            return sb.ToString();
        }
    }

    #endregion
}
