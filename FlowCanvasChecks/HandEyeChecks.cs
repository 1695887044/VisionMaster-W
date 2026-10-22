using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Core.Interfaces;
using Plugin.HandEye;
using Plugin.HandEye.Models;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 手眼标定插件（Plugin.HandEye）断言。
    ///
    /// 两层，各自独立可跑：
    ///   · **数学层**（<see cref="HandEyeMath"/>，纯托管）：合成往返 / 三类退化必须失败 /
    ///     噪声下的精度与质量指标 / 欧拉往返。全部**不碰 HALCON、不碰真机**。
    ///   · **插件层**（<see cref="HandEyePlugin"/>，真 <c>ExecutionContext</c>）：
    ///     CSV 12 列的每一种坏法都要**指名道姓**（行号 + 列号 + 原文）、组数门、
    ///     残差门、成功路径的产物形态。
    ///
    /// 为什么判据是"已知 X 的合成往返"而不是"残差小"
    /// ---------
    /// 2026-10-22 实测：相对运动取逆的那一侧写错时，解出的 X 旋转差 **1 rad**，
    /// 而它自报的旋转残差只有 **3.6e-8 rad** —— **残差小不代表解对**，因为错解对
    /// 错方程是自洽的。所以真值必须从合成数据里带进来，残差只能用来判"数据脏不脏"。
    ///
    /// 合成数据必须与求解器的物理模型同源
    /// ---------
    /// 靶标固定在基座上 ⇒ 任意位姿下 A_i·X·B_i 恒等于同一个常数 C。
    /// 因此造数据只能走 `B_i = X⁻¹·A_i⁻¹·C`；若图省事用 `A = X·B·X⁻¹`（绝对式），
    /// 得到的是**自洽但错误**的方程，求解器会一本正经地解出另一个 X。
    /// </summary>
    internal static class HandEyeChecks
    {
        #region 合成数据

        /// <summary>真值 X①：位置与姿态都不在坐标轴上（避开"恰好对称"的巧合）</summary>
        private static Mat4 TrueX1() => HandEyeMath.PoseFromXyzEulerZyxDeg(120.0, -35.0, 240.0, 12.0, -7.0, 40.0);

        /// <summary>真值 X②：与 X① 差得足够远，用来证明断言能分辨两个不同的解</summary>
        private static Mat4 TrueX2() => HandEyeMath.PoseFromXyzEulerZyxDeg(-80.0, 55.0, 130.0, -25.0, 18.0, -110.0);

        /// <summary>靶标在机器人基座坐标系里的位姿（固定不动；姿态取单位阵，少一层干扰）</summary>
        private static Mat4 FixedTarget() => HandEyeMath.PoseFromXyzEulerZyxDeg(500.0, 300.0, 100.0, 0.0, 0.0, 0.0);

        /// <summary>8 个机器人位姿（位置 mm / ZYX 欧拉度），姿态各方向都转过，保证旋转激励足够</summary>
        private static readonly double[][] RobotPoses =
        {
            new[] { 400.0, 100.0, 600.0,  10.0,  -5.0,  15.0 },
            new[] { 420.0, 120.0, 610.0,  18.0,   8.0, -22.0 },
            new[] { 380.0,  90.0, 590.0, -12.0,  14.0,  35.0 },
            new[] { 410.0, 140.0, 620.0,  25.0, -18.0,  10.0 },
            new[] { 390.0,  80.0, 605.0,  -8.0,  22.0, -30.0 },
            new[] { 430.0, 110.0, 585.0,  15.0,  30.0,  48.0 },
            new[] { 405.0, 130.0, 615.0, -20.0, -12.0,   5.0 },
            new[] { 395.0,  95.0, 595.0,  30.0,  10.0, -18.0 },
        };

        /// <summary>只有平移、姿态完全不变（旋转激励为 0，必须失败）</summary>
        private static readonly double[][] RobotPosesNoRotation =
        {
            new[] { 400.0, 100.0, 600.0, 0.0, 0.0, 0.0 },
            new[] { 420.0, 120.0, 610.0, 0.0, 0.0, 0.0 },
            new[] { 380.0,  90.0, 590.0, 0.0, 0.0, 0.0 },
            new[] { 410.0, 140.0, 620.0, 0.0, 0.0, 0.0 },
            new[] { 390.0,  80.0, 605.0, 0.0, 0.0, 0.0 },
            new[] { 430.0, 110.0, 585.0, 0.0, 0.0, 0.0 },
        };

        /// <summary>只绕同一根轴（Z）转（旋转轴共线，必须失败）</summary>
        private static readonly double[][] RobotPosesCollinear =
        {
            new[] { 400.0, 100.0, 600.0, 0.0, 0.0,   0.0 },
            new[] { 420.0, 120.0, 610.0, 0.0, 0.0,  15.0 },
            new[] { 380.0,  90.0, 590.0, 0.0, 0.0, -20.0 },
            new[] { 410.0, 140.0, 620.0, 0.0, 0.0,  30.0 },
            new[] { 390.0,  80.0, 605.0, 0.0, 0.0, -35.0 },
            new[] { 430.0, 110.0, 585.0, 0.0, 0.0,  10.0 },
            new[] { 405.0, 130.0, 615.0, 0.0, 0.0,  25.0 },
            new[] { 395.0,  95.0, 595.0, 0.0, 0.0, -15.0 },
        };

        /// <summary>
        /// 4 组但**旋转激励刻意拉开**：相对旋转分别绕 X / Y / Z 各 60°，三根轴两两垂直
        /// ⇒ 旋转信息矩阵满秩。用来证明"关掉从严后 4 组确实能解"，
        /// 而 `RobotPoses.Take(4)` 那 4 组的相对旋转几乎共面（奇异值比 1.1e-4），本来就不可解。
        /// </summary>
        private static readonly double[][] RobotPosesFourSpread =
        {
            new[] { 400.0, 100.0, 600.0,  0.0,  0.0,  0.0 },
            new[] { 420.0, 120.0, 610.0, 60.0,  0.0,  0.0 },
            new[] { 380.0,  90.0, 590.0,  0.0, 60.0,  0.0 },
            new[] { 410.0, 140.0, 620.0,  0.0,  0.0, 60.0 },
        };

        /// <summary>按物理模型造样本：靶标固定在基座 ⇒ A·X·B ≡ C（常数），故 B = X⁻¹·A⁻¹·C。
        /// **不要**改成 A = X·B·X⁻¹（绝对式），那是与求解器假设不同的方程。
        /// </summary>
        private static List<HandEyeMath.Sample> MakeSamples(Mat4 x, double[][] robotPoses, Mat4? target = null)
        {
            Mat4 c = target ?? FixedTarget();
            Mat4 xInv = Mat4.InverseRigid(x);
            var list = new List<HandEyeMath.Sample>(robotPoses.Length);
            foreach (double[] p in robotPoses)
            {
                Mat4 a = HandEyeMath.PoseFromXyzEulerZyxDeg(p[0], p[1], p[2], p[3], p[4], p[5]);
                Mat4 b = Mat4.Multiply(xInv, Mat4.Multiply(Mat4.InverseRigid(a), c));
                list.Add(new HandEyeMath.Sample(a, b));
            }
            return list;
        }

        private static double RotErrRad(Mat4 got, Mat4 want)
            => HandEyeMath.RotationAngle(Mat3.Multiply(got.Rotation, Mat3.Transpose(want.Rotation)));

        private static double TransErr(Mat4 got, Mat4 want)
            => Vec3.Subtract(got.Translation, want.Translation).Length;

        #endregion

        #region 入口

        public static void Run()
        {
            Section("[HE] 手眼标定（6-DOF 刚体变换，眼在手上 / 眼在手外）");

            MathLayer();
            EulerLayer();
            PluginLayer();
            ZBLayer();
            CsvLayer();
            PasteLayer();
            ProgressLayer();
            PersistLayer();
        }

        #endregion

        #region 数学层

        private static void MathLayer()
        {
            // ---- ① 合成往返：真值必须原样回来 ----
            var s1 = MakeSamples(TrueX1(), RobotPoses);
            bool ok1 = HandEyeMath.SolveAXEqualsXB(s1, out Mat4 x1, out HandEyeMath.Quality q1, out string? e1);

            Check("[HE] 数学①：8 组物理一致数据 → 求解成功",
                ok1, ok1 ? $"参与 {q1.UsedPairs} 组" : (e1 ?? ""));

            double rotErr1 = ok1 ? RotErrRad(x1, TrueX1()) : double.NaN;
            double transErr1 = ok1 ? TransErr(x1, TrueX1()) : double.NaN;

            Check("[HE] 数学①：解出的旋转与真值一致（< 1e-9 rad）",
                ok1 && rotErr1 < 1e-9, $"旋转误差 {rotErr1:0.###e+0} rad");

            Check("[HE] 数学①：解出的平移与真值一致（< 1e-9 mm）",
                ok1 && transErr1 < 1e-9, $"平移误差 {transErr1:0.###e+0} mm");

            Check("[HE] 数学①：参与求解的组数 = 样本数 − 1（首组作基准）",
                ok1 && q1.UsedPairs == s1.Count - 1, $"{q1.UsedPairs} / 期望 {s1.Count - 1}");

            // ---- ② 换一个真值也必须回来（防"恒等/固定返回值"这种假解）----
            var s2 = MakeSamples(TrueX2(), RobotPoses);
            bool ok2 = HandEyeMath.SolveAXEqualsXB(s2, out Mat4 x2, out HandEyeMath.Quality q2, out string? e2);

            Check("[HE] 数学②：换一个真值 X② 同样解得出",
                ok2 && RotErrRad(x2, TrueX2()) < 1e-6 && TransErr(x2, TrueX2()) < 1e-9,
                ok2 ? $"旋转误差 {RotErrRad(x2, TrueX2()):0.###e+0} rad / 平移误差 {TransErr(x2, TrueX2()):0.###e+0} mm"
                    : (e2 ?? ""));

            // 两个真值解出的 X 必须明显不同 —— 否则"解对"这个判据没有分辨力
            double between = ok1 && ok2 ? RotErrRad(x1, x2) : 0.0;
            Check("[HE] 数学②：两个真值解出的 X 明显不同（判据有分辨力，不是恒等返回）",
                ok1 && ok2 && between > 0.5, $"两个解之间差 {between:0.###} rad（期望 > 0.5）");

            // ---- ③ 只有平移、姿态不变 → 必须失败（旋转激励不足）----
            var sNoRot = MakeSamples(TrueX1(), RobotPosesNoRotation);
            bool okNoRot = HandEyeMath.SolveAXEqualsXB(sNoRot, out _, out _, out string? eNoRot);

            Check("[HE] 数学③：姿态完全不变 → 必须失败，且理由说清「旋转激励不足」",
                !okNoRot && eNoRot != null && eNoRot.Contains("旋转激励不足"), eNoRot ?? "（竟然成功了）");

            // ---- ④ 只绕同一根轴转 → 必须失败（旋转轴共线，方程降秩）----
            var sColl = MakeSamples(TrueX1(), RobotPosesCollinear);
            bool okColl = HandEyeMath.SolveAXEqualsXB(sColl, out _, out _, out string? eColl);

            Check("[HE] 数学④：旋转轴共线 → 必须失败，且理由指到共线/奇异",
                !okColl && eColl != null && (eColl.Contains("共线") || eColl.Contains("奇异")),
                eColl ?? "（竟然成功了）");

            // ---- ⑤ 样本不足 → 必须失败（数学下限 3 组）----
            var sFew = MakeSamples(TrueX1(), RobotPoses.Take(2).ToArray());
            bool okFew = HandEyeMath.SolveAXEqualsXB(sFew, out _, out _, out string? eFew);

            Check("[HE] 数学⑤：2 组样本 → 必须失败，且写明至少需要 3 组",
                !okFew && eFew != null && eFew.Contains("至少需要 3 组"), eFew ?? "（竟然成功了）");

            // ---- ⑥ 噪声下的精度，以及"质量指标真的在算" ----
            // 给机器人侧平移加 0.05 mm 的确定性抖动：解出来的 X 允许有误差，
            // 但自报的平移 RMS 必须**非零** —— 否则质量指标是写死的常量，界面上的
            // "残差 0.000"就是假的。
            var sNoise = MakeSamples(TrueX1(), RobotPoses);
            for (int i = 0; i < sNoise.Count; i++)
            {
                double jitter = (i % 2 == 0 ? 1.0 : -1.0) * 0.05;
                Mat4 a = sNoise[i].Robot;
                var t = a.Translation;
                Mat4 noisy = Mat4.FromRt(a.Rotation, new Vec3(t.X + jitter, t.Y - jitter, t.Z));
                sNoise[i] = new HandEyeMath.Sample(noisy, sNoise[i].Camera);
            }

            bool okNoise = HandEyeMath.SolveAXEqualsXB(sNoise, out Mat4 xNoise, out HandEyeMath.Quality qNoise, out string? eNoise);

            Check("[HE] 数学⑥：0.05 mm 抖动下解仍可用（平移误差 < 0.1 mm）",
                okNoise && TransErr(xNoise, TrueX1()) < 0.1,
                okNoise ? $"平移误差 {TransErr(xNoise, TrueX1()):0.####} mm" : (eNoise ?? ""));

            Check("[HE] 数学⑥：质量指标真的在算（噪声下平移 RMS 必须 > 0）",
                okNoise && qNoise.TranslationRms > 0.0,
                okNoise ? $"平移 RMS {qNoise.TranslationRms:0.####} mm" : (eNoise ?? ""));

            Check("[HE] 数学⑥：噪声下旋转仍准（< 1e-6 rad —— 抖动只加在平移上）",
                okNoise && RotErrRad(xNoise, TrueX1()) < 1e-6,
                okNoise ? $"旋转误差 {RotErrRad(xNoise, TrueX1()):0.###e+0} rad" : (eNoise ?? ""));
        }

        #endregion

        #region 欧拉角往返

        private static void EulerLayer()
        {
            // 手算常量：ZYX 里 rz = 90° 时，X 轴 (1,0,0) 必须转到 Y 轴 (0,1,0)
            Mat3 rz90 = HandEyeMath.EulerZyxDegToMat3(0.0, 0.0, 90.0);
            Vec3 ex = Mat3.Multiply(rz90, new Vec3(1.0, 0.0, 0.0));
            Check("[HE] 欧拉①：rz=90° 把 X 轴转到 Y 轴（ZYX 约定与符号的硬判据）",
                Math.Abs(ex.X) < 1e-12 && Math.Abs(ex.Y - 1.0) < 1e-12 && Math.Abs(ex.Z) < 1e-12,
                $"({ex.X:0.####}, {ex.Y:0.####}, {ex.Z:0.####})");

            // rx = 90° 时 Y 轴 (0,1,0) 必须转到 Z 轴 (0,0,1)
            Mat3 rx90 = HandEyeMath.EulerZyxDegToMat3(90.0, 0.0, 0.0);
            Vec3 ey = Mat3.Multiply(rx90, new Vec3(0.0, 1.0, 0.0));
            Check("[HE] 欧拉②：rx=90° 把 Y 轴转到 Z 轴",
                Math.Abs(ey.X) < 1e-12 && Math.Abs(ey.Y) < 1e-12 && Math.Abs(ey.Z - 1.0) < 1e-12,
                $"({ey.X:0.####}, {ey.Y:0.####}, {ey.Z:0.####})");

            // 往返：位姿 → 六个数 → 位姿，矩阵必须逐元素一致（写 CSV 靠这条）
            Mat4 pose = TrueX1();
            HandEyeMath.Mat4ToXyzEulerZyxDeg(pose, out double px, out double py, out double pz,
                out double prx, out double pry, out double prz);
            Mat4 back = HandEyeMath.PoseFromXyzEulerZyxDeg(px, py, pz, prx, pry, prz);
            double maxDiff = 0.0;
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                    maxDiff = Math.Max(maxDiff, Math.Abs(pose[r, c] - back[r, c]));

            Check("[HE] 欧拉③：位姿 → 六个数 → 位姿 往返一致（CSV 写回的正确性）",
                maxDiff < 1e-12, $"最大元素偏差 {maxDiff:0.###e+0}");

            // 万向锁（ry = 90°）也必须往返一致：角度不唯一，但矩阵必须回来
            Mat4 gimbal = HandEyeMath.PoseFromXyzEulerZyxDeg(10.0, 20.0, 30.0, 0.0, 90.0, 0.0);
            HandEyeMath.Mat4ToXyzEulerZyxDeg(gimbal, out _, out _, out _,
                out double grx, out double gry, out double grz);
            Mat4 gimbalBack = HandEyeMath.PoseFromXyzEulerZyxDeg(10.0, 20.0, 30.0, grx, gry, grz);
            double gimbalDiff = 0.0;
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    gimbalDiff = Math.Max(gimbalDiff, Math.Abs(gimbal.Rotation[r, c] - gimbalBack.Rotation[r, c]));

            Check("[HE] 欧拉④：万向锁（ry=90°）往返矩阵一致（角度不唯一，矩阵必须一致）",
                gimbalDiff < 1e-12, $"最大元素偏差 {gimbalDiff:0.###e+0}");
        }

        #endregion

        #region 插件层

        private static void PluginLayer()
        {
            var ctx = new ExecutionContext(
                new StubLog(), new FlowSession { FlowName = "手眼标定断言" },
                new WorkspaceContext(), CancellationToken.None);

            // ---- ⑦ 空表 ----
            var empty = NewPlugin(Array.Empty<HandEyeSample>());
            empty.RunAlgorithm(ctx);
            Check("[HE] 插件①：表格为空 → 失败，且说清去哪儿加数据",
                empty.Success.Value is false && Err(empty).Contains("还没有成对数据"), Err(empty));

            // ---- ⑧ 有一行没填（12 个值全 0）----
            var blank = RowsFrom(MakeSamples(TrueX1(), RobotPoses));
            blank.Insert(1, new HandEyeSample());
            var pBlank = NewPlugin(blank);
            pBlank.RunAlgorithm(ctx);
            Check("[HE] 插件②：有一行没填 → 失败，点名「第 2 行还没填」而不是含糊报退化",
                pBlank.Success.Value is false
                && Err(pBlank).Contains("第 2 行") && Err(pBlank).Contains("还没填"),
                Err(pBlank));

            // ---- ⑨ 成功路径的产物形态 ----
            var pGood = NewPlugin(RowsFrom(MakeSamples(TrueX1(), RobotPoses)));
            pGood.RunAlgorithm(ctx);

            bool isHe = pGood.Transform.Value is CalibrationTransform;
            CalibrationTransform? got = isHe ? (CalibrationTransform?)pGood.Transform.Value : null;

            Check("[HE] 插件③：8 行 → 成功，Kind = HandEye、矩阵 16 元、组数 8",
                isHe && got!.Kind == CalibrationKind.HandEye
                && got.HandEyeMatrix != null && got.HandEyeMatrix.Length == 16
                && got.HandEyeSampleCount == 8,
                isHe ? $"Kind={got!.Kind} 长度={got.HandEyeMatrix?.Length ?? -1} 组数={got.HandEyeSampleCount}"
                     : "Transform 为 null");

            Check("[HE] 插件④：4×4 与数学层解出的 X 一致（插件没在中间改过它）",
                got?.HandEyeMatrix != null && MatchesMatrix(got.HandEyeMatrix, TrueX1(), 1e-6),
                got?.HandEyeMatrix == null ? "无矩阵" : DescribeDiff(got.HandEyeMatrix, TrueX1()));

            Check("[HE] 插件⑤：三个指标端口都有值（残差与组数不是摆设）",
                pGood.UsedPairs.Value is int used && used == 7
                && pGood.TranslationRmsMm.Value is double tr && tr >= 0.0
                && pGood.RotationRmsDeg.Value is double rr && rr >= 0.0,
                $"组数={pGood.UsedPairs.Value} 平移RMS={pGood.TranslationRmsMm.Value} 旋转RMS(度)={pGood.RotationRmsDeg.Value}");

            // ---- ⑩ 组数门：4 行默认判失败（建议 6 组），关掉开关后放行 ----
            var pFourStrict = NewPlugin(
                RowsFrom(MakeSamples(TrueX1(), RobotPoses.Take(4).ToArray())),
                requireRecommended: true);
            pFourStrict.RunAlgorithm(ctx);
            Check("[HE] 插件⑥：4 行 + 默认从严 → 失败，文案点明「少于建议的 6 组」",
                pFourStrict.Success.Value is false && Err(pFourStrict).Contains("少于建议的 6 组"),
                Err(pFourStrict));

            var pFourLoose = NewPlugin(
                RowsFrom(MakeSamples(TrueX1(), RobotPosesFourSpread)),
                requireRecommended: false);
            pFourLoose.RunAlgorithm(ctx);
            Check("[HE] 插件⑦：4 行（旋转激励拉开）+ 关掉从严 → 成功，组数如实报 3",
                pFourLoose.Success.Value is true
                && pFourLoose.UsedPairs.Value is int u4 && u4 == 3,
                pFourLoose.Success.Value is true ? $"组数={pFourLoose.UsedPairs.Value}" : Err(pFourLoose));

            // ---- ⑪ 残差门：脏数据必须被拦，且文案指到「点位数据」 ----
            var noisyRows = RowsFrom(MakeSamples(TrueX1(), RobotPoses), withNoise: true);
            var pNoisy = NewPlugin(noisyRows, maxTrans: 1e-6);
            pNoisy.RunAlgorithm(ctx);
            Check("[HE] 插件⑧：平移残差超上限 → 失败，文案点明「点位数据可能含错点」",
                pNoisy.Success.Value is false
                && Err(pNoisy).Contains("超过上限") && Err(pNoisy).Contains("点位数据"),
                Err(pNoisy));

            // 同样的脏数据，把上限放宽到 1 mm 就该放行 —— 证明拦它的是门，不是别的东西
            var pNoisyLoose = NewPlugin(noisyRows, maxTrans: 1.0);
            pNoisyLoose.RunAlgorithm(ctx);
            Check("[HE] 插件⑨：同数据把上限放宽到 1 mm → 放行（拦它的是残差门，不是别的）",
                pNoisyLoose.Success.Value is true, Err(pNoisyLoose));

            // ---- ⑫ 失败时必须把上一轮的结果清掉 ----
            var pStale = NewPlugin(RowsFrom(MakeSamples(TrueX1(), RobotPoses)));
            pStale.RunAlgorithm(ctx);
            bool firstOk = pStale.Success.Value is true;
            pStale.Samples.Clear();
            pStale.RunAlgorithm(ctx);
            Check("[HE] 插件⑩：失败后 Transform 置空、指标归零（下游不会读到上一轮残留）",
                firstOk
                && pStale.Success.Value is false
                && pStale.Transform.Value is null
                && pStale.UsedPairs.Value is int zero && zero == 0,
                $"Transform={(pStale.Transform.Value == null ? "null" : "非 null")} 组数={pStale.UsedPairs.Value}");
        }

        /// <summary>
        /// 造插件要的行集合。落成 12 个数走 <see cref="HandEyeMath.Mat4ToXyzEulerZyxDeg"/>，
        /// 与界面手填的输入是同一形状 —— 只喂 <see cref="HandEyeMath.Sample"/> 就测不到
        /// "行 → 数学样本"这段换算（那正是 ZYX 约定最容易分叉的地方）。
        /// </summary>
        private static ObservableCollection<HandEyeSample> RowsFrom(
            IReadOnlyList<HandEyeMath.Sample> samples, bool withNoise = false)
        {
            var rows = new ObservableCollection<HandEyeSample>();
            for (int i = 0; i < samples.Count; i++)
            {
                Mat4 robot = samples[i].Robot;
                if (withNoise)
                {
                    double jitter = (i % 2 == 0 ? 1.0 : -1.0) * 0.05;
                    Vec3 t = robot.Translation;
                    robot = Mat4.FromRt(robot.Rotation, new Vec3(t.X + jitter, t.Y - jitter, t.Z));
                }

                AppendRow(rows, robot, samples[i].Camera);
            }
            return rows;
        }

        private static void AppendRow(ObservableCollection<HandEyeSample> rows, Mat4 robot, Mat4 camera)
        {
            HandEyeMath.Mat4ToXyzEulerZyxDeg(robot,
                out double rx, out double ry, out double rz,
                out double rrx, out double rry, out double rrz);
            HandEyeMath.Mat4ToXyzEulerZyxDeg(camera,
                out double cx, out double cy, out double cz,
                out double crx, out double cry, out double crz);

            var row = new HandEyeSample();
            row.SetRow(new[] { rx, ry, rz, rrx, rry, rrz, cx, cy, cz, crx, cry, crz });
            rows.Add(row);
        }

        private static HandEyePlugin NewPlugin(
            IEnumerable<HandEyeSample> rows,
            bool requireRecommended = true,
            double maxTrans = 1.0,
            double maxRot = 0.5,
            HandEyePoseKind pose = HandEyePoseKind.EyeInHand)
        {
            // ⚠ 这些属性都带 [DefaultValue]，但只有走 Initialize 才会被填；
            // 断言里直接 new 出来必须**逐个显式赋值**，否则默认是 0/false 而不是 1.0/true。
            var plugin = new HandEyePlugin
            {
                RequireRecommendedPairs = requireRecommended,
                MaxTranslationRmsMm = maxTrans,
                MaxRotationRmsDeg = maxRot,
                PoseKind = pose,
            };
            foreach (var r in rows)
                plugin.Samples.Add(r);
            return plugin;
        }

        private static string Err(HandEyePlugin plugin)
            => plugin.ErrorMessage.Value as string ?? string.Empty;

        #endregion

        #region 眼在手外（AX=ZB）

        /// <summary>
        /// 按 eye-to-hand 的物理模型造样本：靶标装在法兰上、相机固定 ⇒ A_i·Z = X·B_i，
        /// 故 B = X⁻¹·A·Z。<b>不要</b>套用眼在手上的 B = X⁻¹·A⁻¹·C —— 那是另一组数据，
        /// 拿它喂 AX=ZB 会解出一个自洽但错误的答案。
        /// </summary>
        private static List<HandEyeMath.Sample> MakeZBSamples(
            Mat4 x, Mat4 z, double[][] robotPoses, bool withNoise = false)
        {
            var list = new List<HandEyeMath.Sample>(robotPoses.Length);
            for (int i = 0; i < robotPoses.Length; i++)
            {
                double[] p = robotPoses[i];
                Mat4 a = HandEyeMath.PoseFromXyzEulerZyxDeg(p[0], p[1], p[2], p[3], p[4], p[5]);
                if (withNoise)
                {
                    double jitter = (i % 2 == 0 ? 1.0 : -1.0) * 0.05;
                    Vec3 t = a.Translation;
                    a = Mat4.FromRt(a.Rotation, new Vec3(t.X + jitter, t.Y - jitter, t.Z));
                }

                Mat4 b = Mat4.Multiply(Mat4.InverseRigid(x), Mat4.Multiply(a, z));
                list.Add(new HandEyeMath.Sample(a, b));
            }
            return list;
        }

        private static Mat4 TrueZ() => HandEyeMath.PoseFromXyzEulerZyxDeg(30.0, -80.0, 15.0, 5.0, 25.0, -12.0);

        private static void ZBLayer()
        {
            Mat4 xTrue = TrueX2();
            Mat4 zTrue = TrueZ();

            // ---- ① 合成往返：交付物 X 与中间量 Z 都要还原 ----
            var s = MakeZBSamples(xTrue, zTrue, RobotPoses);
            bool ok = HandEyeMath.SolveAXEqualsZB(s, out Mat4 xGot, out Mat4 zGot, out HandEyeMath.Quality q, out string? err);
            double rotErr = ok ? RotErrRad(xGot, xTrue) : double.NaN;
            double transErr = ok ? TransErr(xGot, xTrue) : double.NaN;
            Check("[HE] 眼在手外①：合成数据往返，解出的相机位姿与真值一致（< 1e-9）",
                ok && rotErr < 1e-9 && transErr < 1e-9,
                ok ? $"旋转误差 {rotErr:0.###e+0} rad / 平移误差 {transErr:0.###e+0} mm" : (err ?? "求解失败但没给原因"));

            double zRotErr = ok ? RotErrRad(zGot, zTrue) : double.NaN;
            double zTransErr = ok ? TransErr(zGot, zTrue) : double.NaN;
            Check("[HE] 眼在手外②：中间量 Z（靶标在法兰）也还原（< 1e-9）",
                ok && zRotErr < 1e-9 && zTransErr < 1e-9,
                ok ? $"Z 旋转误差 {zRotErr:0.###e+0} rad / 平移误差 {zTransErr:0.###e+0} mm" : (err ?? "求解失败但没给原因"));

            Check("[HE] 眼在手外③：参与组数 = 样本数 − 1（第一组作基准）",
                ok && q.UsedPairs == s.Count - 1,
                ok ? $"参与 {q.UsedPairs} 组（共 {s.Count} 组）" : (err ?? "求解失败但没给原因"));

            // ---- ② 噪声：0.05 mm 抖动下的精度 ----
            var sNoise = MakeZBSamples(xTrue, zTrue, RobotPoses, withNoise: true);
            bool okNoise = HandEyeMath.SolveAXEqualsZB(sNoise, out Mat4 xNoise, out Mat4 _, out HandEyeMath.Quality qNoise, out string? errNoise);
            Check("[HE] 眼在手外④：机器人侧 0.05 mm 抖动，相机位姿误差仍 < 0.1 mm",
                okNoise && TransErr(xNoise, xTrue) < 0.1,
                okNoise ? $"平移误差 {TransErr(xNoise, xTrue):0.####} mm / 残差 {qNoise.TranslationRms:0.####} mm" : (errNoise ?? "求解失败但没给原因"));

            // ---- ③ 退化必须失败（复用同一套判据，不是另写一份）----
            bool noRotOk = HandEyeMath.SolveAXEqualsZB(MakeZBSamples(xTrue, zTrue, RobotPosesNoRotation), out Mat4 _, out Mat4 _, out HandEyeMath.Quality _, out string? noRotErr);
            Check("[HE] 眼在手外⑤：机器人不动姿态 → 必须失败",
                !noRotOk && (noRotErr ?? "").Contains("旋转激励不足"),
                noRotOk ? "竟然解出来了" : (noRotErr ?? ""));

            bool collOk = HandEyeMath.SolveAXEqualsZB(MakeZBSamples(xTrue, zTrue, RobotPosesCollinear), out Mat4 _, out Mat4 _, out HandEyeMath.Quality _, out string? collErr);
            Check("[HE] 眼在手外⑥：旋转轴共线 → 必须失败",
                !collOk && ((collErr ?? "").Contains("共线") || (collErr ?? "").Contains("奇异")),
                collOk ? "竟然解出来了" : (collErr ?? ""));

            bool fewOk = HandEyeMath.SolveAXEqualsZB(MakeZBSamples(xTrue, zTrue, RobotPoses.Take(2).ToArray()), out Mat4 _, out Mat4 _, out HandEyeMath.Quality _, out string? fewErr);
            Check("[HE] 眼在手外⑦：只有 2 组 → 必须失败并说清要几组",
                !fewOk && (fewErr ?? "").Contains("至少需要 3 组"),
                fewOk ? "竟然解出来了" : (fewErr ?? ""));

            // ---- ④ 装法选错：这是本层最重要的一条 ----
            // 把 eye-to-hand 的数据喂给 eye-in-hand 求解器，它照样"求解成功"并给出一个数，
            // 只是那个数与真值差上百毫米。所以**不能靠"求解成功"判断装法选对**，
            // 唯一的兜底是残差门 —— 下面 ⑧ ⑨ 两条分别钉住"差得远"和"残差门拦得住"。
            bool xbOk = HandEyeMath.SolveAXEqualsXB(s, out Mat4 wrongX, out HandEyeMath.Quality wq, out string? xbErr);
            double wrongRotDeg = xbOk ? RotErrRad(wrongX, xTrue) * 180.0 / Math.PI : double.NaN;
            double wrongTrans = xbOk ? TransErr(wrongX, xTrue) : double.NaN;
            Check("[HE] 眼在手外⑧：把眼在手外的数据喂给眼在手上的求解器，解出的 X 与真值差很远（「求解成功」证明不了装法选对）",
                xbOk && (wrongRotDeg > 30.0 || wrongTrans > 10.0),
                xbOk ? $"旋转差 {wrongRotDeg:0.###}° / 平移差 {wrongTrans:0.###} mm" : $"连解都没解出：{xbErr}");

            double wrongResidualDeg = xbOk ? wq.RotationRmsRad * 180.0 / Math.PI : double.NaN;
            Check("[HE] 眼在手外⑨：装法选错时残差门拦得住（残差远超默认上限 0.5°）",
                xbOk && wrongResidualDeg > 0.5,
                xbOk ? $"它的残差 {wrongResidualDeg:0.###}° > 上限 0.5°" : $"连解都没解出：{xbErr}");

            // ---- ⑤ 插件层：装法路由真的生效 ----
            var ctx = new ExecutionContext(
                new StubLog(), new FlowSession { FlowName = "手眼标定断言（眼在手外）" },
                new WorkspaceContext(), CancellationToken.None);

            var pZb = NewPlugin(RowsFrom(MakeZBSamples(xTrue, zTrue, RobotPoses)), pose: HandEyePoseKind.EyeToHand);
            pZb.RunAlgorithm(ctx);
            if (pZb.Transform.Value is CalibrationTransform gotZb)
            {
                Check("[HE] 眼在手外⑩：插件按装法路由（PoseKind=EyeToHand 走 AX=ZB），产物标记眼在手外且矩阵与真值一致",
                    pZb.Success.Value is true
                        && gotZb.HandEyePose == HandEyePoseKind.EyeToHand
                        && gotZb.HandEyeMatrix != null
                        && MatchesMatrix(gotZb.HandEyeMatrix, xTrue, 1e-6),
                    pZb.Success.Value is false
                        ? Err(pZb)
                        : $"装法={gotZb.HandEyePose} " + DescribeDiff(gotZb.HandEyeMatrix!, xTrue));
            }
            else
            {
                Check("[HE] 眼在手外⑩：插件按装法路由（PoseKind=EyeToHand 走 AX=ZB），产物标记眼在手外且矩阵与真值一致",
                    false,
                    $"Transform 为空：{Err(pZb)}");
            }

            var pWrongPose = NewPlugin(RowsFrom(MakeZBSamples(xTrue, zTrue, RobotPoses)), pose: HandEyePoseKind.EyeInHand);
            pWrongPose.RunAlgorithm(ctx);
            Check("[HE] 眼在手外⑪：同一批数据若把装法选成「眼在手上」，插件必须失败而不是给出一个偏的解",
                pWrongPose.Success.Value is false
                    && pWrongPose.Transform.Value == null
                    && Err(pWrongPose).Contains("眼在手上"),
                pWrongPose.Success.Value is false
                    ? Err(pWrongPose)
                    : "竟然成功了：残差门太松或装法路由没生效");
        }

        #endregion

        #region CSV 导入导出

        /// <summary>
        /// CSV 是扩展功能 —— 数据住在表格（<c>Samples</c>）里，CSV 只负责把一批行搬进搬出。
        /// 这里只验「文本 ↔ 行」这一段，不碰文件（选文件、编码、覆盖确认归视图层）。
        /// </summary>
        private static void CsvLayer()
        {
            // ---- ① 解析：表头 / 空行 / # 注释都要让过去 ----
            var lines = new[]
            {
                HandEyeCsv.Header,
                "1,2,3,4,5,6,7,8,9,10,11,12",
                "",
                "# 现场备注：这一批是 10-22 上午采的",
                "13.5,-2,3.25,0,0,0,100,200,300,0,0,90",
            };

            bool parsedOk = HandEyeCsv.TryParse(lines, out List<HandEyeSample> parsed, out string? parseErr);
            Check("[HE] CSV①：表头 / 空行 / # 注释都跳过，两行数据都读进来",
                parsedOk && parsed.Count == 2,
                parsedOk ? $"行数={parsed.Count}" : parseErr ?? "解析失败但没给原因");

            bool placed = parsedOk && parsed.Count == 2
                && Math.Abs(parsed[0].RobotX - 1.0) < 1e-12
                && Math.Abs(parsed[0].CameraRz - 12.0) < 1e-12
                && Math.Abs(parsed[1].RobotX - 13.5) < 1e-12
                && Math.Abs(parsed[1].CameraZ - 300.0) < 1e-12
                && Math.Abs(parsed[1].CameraRz - 90.0) < 1e-12;
            Check("[HE] CSV②：12 个数各就各位（第 1 行 robotX=1 cameraRz=12，第 2 行 robotX=13.5 cameraZ=300 cameraRz=90）",
                placed, parsedOk && parsed.Count == 2 ? "首行与末列取值已核对" : "行数不对，跳过");

            // ---- ② 少一列必须点名行号与应有列数 ----
            bool colRejected = HandEyeCsv.TryParse(new[] { "1,2,3" }, out _, out string? colErr);
            Check("[HE] CSV③：只给 3 列 → 失败，文案点名「第 1 行有 3 列，应为 12 列」",
                !colRejected && colErr != null && colErr.Contains("3 列") && colErr.Contains("12"),
                colErr ?? "居然通过了");

            // ---- ③ 写了字母必须点名第几列、原值是什么 ----
            bool numRejected = HandEyeCsv.TryParse(
                new[] { "1,2,3,abc,5,6,7,8,9,10,11,12" }, out _, out string? numErr);
            Check("[HE] CSV④：第 4 列写 abc → 失败，文案点名「第 4 列」并带上原值",
                !numRejected && numErr != null && numErr.Contains("第 4 列") && numErr.Contains("abc"),
                numErr ?? "居然通过了");

            // ---- ④ 导出再导入：每行 12 个数逐位回来 ----
            var rows = RowsFrom(MakeSamples(TrueX1(), RobotPoses));
            string text = HandEyeCsv.Format(rows);
            var textLines = text.Split(new[] { "\r\n" }, StringSplitOptions.None);

            bool roundOk = HandEyeCsv.TryParse(textLines, out List<HandEyeSample> back, out string? roundErr);
            bool same = roundOk && back.Count == rows.Count;
            if (same)
            {
                for (int i = 0; i < rows.Count && same; i++)
                {
                    double[] a = rows[i].ToRow();
                    double[] b = back[i].ToRow();
                    for (int k = 0; k < a.Length; k++)
                    {
                        if (Math.Abs(a[k] - b[k]) > 1e-9) { same = false; break; }
                    }
                }
            }

            Check("[HE] CSV⑤：导出再导入，每行 12 个数逐位一致（不丢精度、不串列）",
                same, roundOk ? $"导出 {back.Count} 行 / 原 {rows.Count} 行" : roundErr ?? "解析失败");
        }

        #region 从 Excel 粘贴一片

        /// <summary>
        /// 表格是数据的主入口，而现场数据多半先在 Excel 里排好 —— 所以 Ctrl+V 粘一片是主线操作。
        /// 这一层钉三条规则：整片校验（错一格全不写）/ 空单元格保持原值 / 超宽丢弃并报数。
        /// </summary>
        private static void PasteLayer()
        {
            // ---- ① 单格不接管：交给单元格编辑器（逐格报错比整片失败精确，也有撤销） ----
            Check("[HE] 粘贴①：单格纯数字不算「一片」，不接管",
                !HandEyePaste.IsBlock("12.5")
                && !HandEyePaste.IsBlock("  12.5\r\n")
                && HandEyePaste.IsBlock("1\t2")
                && HandEyePaste.IsBlock("1\n2"),
                $"单格={HandEyePaste.IsBlock("12.5")} 制表符={HandEyePaste.IsBlock("1\t2")} 换行={HandEyePaste.IsBlock("1\n2")}");

            // ---- ② 从左上角粘 2 行 × 3 列 ----
            var p2 = NewPlugin(Array.Empty<HandEyeSample>());
            bool ok2 = HandEyePaste.TryApply(p2, "1\t2\t3\n4\t5\t6", 0, 0, out var out2, out string? err2);
            Check("[HE] 粘贴②：粘 2 行 × 3 列 → 自动补 2 行，12 个分量各就各位",
                ok2 && p2.Samples.Count == 2 && out2.RowsWritten == 2 && out2.CellsWritten == 6
                && Math.Abs(p2.Samples[0].RobotX - 1) < 1e-12 && Math.Abs(p2.Samples[0].RobotZ - 3) < 1e-12
                && Math.Abs(p2.Samples[1].RobotX - 4) < 1e-12 && Math.Abs(p2.Samples[1].RobotZ - 6) < 1e-12,
                ok2 ? $"行数={p2.Samples.Count} 写入={out2.RowsWritten} 行/{out2.CellsWritten} 格" : err2 ?? "失败但没给原因");

            // ---- ③ 从中间一格开始粘：起点行列都要生效（不是永远贴到左上角） ----
            // 第 5 列（0 基 4）= 机器人 Ry，第 6 列 = 机器人 Rz：粘的是 7 / 8，就落在后两个分量上。
            var p3 = NewPlugin(Array.Empty<HandEyeSample>());
            bool ok3 = HandEyePaste.TryApply(p3, "7\t8", 2, 4, out _, out string? err3);
            Check("[HE] 粘贴③：从第 3 行第 5 列开始粘 → 落在机器人 Ry / Rz 上（起点生效，不是永远贴左上角）",
                ok3 && p3.Samples.Count == 3
                && Math.Abs(p3.Samples[2].RobotRy - 7) < 1e-12
                && Math.Abs(p3.Samples[2].RobotRz - 8) < 1e-12,
                ok3 ? $"第 3 行 Ry={p3.Samples[2].RobotRy:0.###} Rz={p3.Samples[2].RobotRz:0.###}" : err3 ?? "失败但没给原因");

            // ---- ④ 空单元格保持原值：不当 0 写（"这一列没量"很常见，写 0 会变成有效但错误的样本） ----
            var p4 = NewPlugin(Array.Empty<HandEyeSample>());
            p4.Samples.Add(new HandEyeSample { RobotX = 99, RobotY = 88 });
            bool ok4 = HandEyePaste.TryApply(p4, "\t5", 0, 0, out _, out string? err4);
            Check("[HE] 粘贴④：空单元格保持原值（robotX 仍是 99，robotY 才被写成 5）",
                ok4 && Math.Abs(p4.Samples[0].RobotX - 99) < 1e-12 && Math.Abs(p4.Samples[0].RobotY - 5) < 1e-12,
                ok4 ? $"robotX={p4.Samples[0].RobotX:0.###} robotY={p4.Samples[0].RobotY:0.###}" : err4 ?? "失败但没给原因");

            // ---- ⑤ 比表宽：超出 12 列的部分丢弃并计数，不环绕到下一行 ----
            var p5 = NewPlugin(Array.Empty<HandEyeSample>());
            string wide = string.Join("\t", Enumerable.Range(1, 14).Select(v => v.ToString()));
            bool ok5 = HandEyePaste.TryApply(p5, wide, 0, 0, out var out5, out string? err5);
            Check("[HE] 粘贴⑤：给 14 列 → 只写前 12 个，丢弃 2 个并计入 Dropped（不环绕、不静默）",
                ok5 && out5.Dropped == 2 && p5.Samples.Count == 1
                && Math.Abs(p5.Samples[0].CameraRz - 12) < 1e-12
                && out5.CellsWritten == 12,
                ok5 ? $"丢弃={out5.Dropped} 写入={out5.CellsWritten} 末格={p5.Samples[0].CameraRz:0.###}" : err5 ?? "失败但没给原因");

            // ---- ⑥ 有一个格子不是数字 → 整片拒绝，一个都不写 ----
            var p6 = NewPlugin(Array.Empty<HandEyeSample>());
            double before = p6.Samples.Count;
            bool ok6 = HandEyePaste.TryApply(p6, "1\t2\n3\tabc", 0, 0, out _, out string? err6);
            bool untouched6 = p6.Samples.Count == (int)before;
            Check("[HE] 粘贴⑥：第 2 行第 2 列写 abc → 失败，文案点名行列与原文，且一个字都没写",
                !ok6 && untouched6
                && err6 != null && err6.Contains("第 2 行") && err6.Contains("第 2 列") && err6.Contains("abc"),
                (ok6 ? "居然通过了" : err6 ?? "失败但没给原因") + $"，行数 {p6.Samples.Count}");

            // ---- ⑦ 粘完结论必须作废：不能把上一次的矩阵继续挂在屏上 ----
            var p7 = NewPlugin(RowsFrom(MakeSamples(TrueX1(), RobotPoses)));
            p7.SolveForPreview();
            bool okBefore7 = p7.IsPreviewOk;
            HandEyePaste.TryApply(p7, "1\t2", 0, 0, out _, out _);
            Check("[HE] 粘贴⑦：粘贴改了数据之后，上一次的结论立即作废（不再显示为「完成」）",
                okBefore7 && !p7.IsPreviewOk,
                $"粘前={okBefore7} 粘后={p7.PreviewStatusText}");
        }

        #endregion

        #endregion

        #region 上次结论的持久化（数据指纹）

        /// <summary>
        /// 现场不是每次都标定：标一次、存档、之后只跑生产。所以「重开方案还能看见上次矩阵」
        /// 是需求，「重开方案看见一个已经不对应当前数据的矩阵」是事故。
        /// 这一层就是这两句话的判据。
        /// </summary>
        /// <summary>
        /// 采集进度（界面那条 6px 细条的数据源）。
        /// <para>
        /// 判据的核心是**口径**：进度只认"12 个分量不全为 0 的行"，不认行数。
        /// 否则"加了 7 行只填了 3 行"会显示成满格，而一求解就报第 4 行还没填 ——
        /// 进度条说没问题、求解说没数据，用户只会认为工具在骗他。
        /// </para>
        /// </summary>
        private static void ProgressLayer()
        {
            // ---- ① 空表：没有数据就没有进度可言 ----
            var empty = NewPlugin(Array.Empty<HandEyeSample>());
            Check("[HE] 进度①：空表 → 0 组、进度 0、未达建议，文案说「还没有成对数据」",
                empty.ValidSampleCount == 0 && empty.SampleProgress == 0.0 && !empty.HasEnoughSamples
                && empty.SampleCountHint.Contains("还没有成对数据"),
                $"有效={empty.ValidSampleCount} 进度={empty.SampleProgress:0.###} 文案={empty.SampleCountHint}");

            // ---- ② 3 行填了 + 1 行空着：空白行不算采集进度 ----
            var half = NewPlugin(Array.Empty<HandEyeSample>());
            for (int i = 0; i < 3; i++) half.Samples.Add(new HandEyeSample { RobotX = 100 + i });
            half.Samples.Add(new HandEyeSample());
            Check("[HE] 进度②：3 行填了 + 1 行空着 → 有效 3 / 6（半格），空白行单独点名",
                half.ValidSampleCount == 3 && half.BlankSampleCount == 1
                && Math.Abs(half.SampleProgress - 0.5) < 1e-12 && !half.HasEnoughSamples
                && half.SampleCountHint.Contains("3 / 6") && half.SampleCountHint.Contains("1 行没填"),
                $"有效={half.ValidSampleCount} 空白={half.BlankSampleCount} 进度={half.SampleProgress:0.###} 文案={half.SampleCountHint}");

            // ---- ③ 采够 6 组：满格，HasEnoughSamples 驱动进度条转成功色 ----
            var full = NewPlugin(Array.Empty<HandEyeSample>());
            for (int i = 0; i < 6; i++) full.Samples.Add(new HandEyeSample { RobotX = 200 + i });
            Check("[HE] 进度③：6 组有效 → 满格、HasEnoughSamples=true（进度条由主色转成功色）",
                full.ValidSampleCount == 6 && Math.Abs(full.SampleProgress - 1.0) < 1e-12 && full.HasEnoughSamples
                && full.SampleCountHint.Contains("达到建议"),
                $"有效={full.ValidSampleCount} 进度={full.SampleProgress:0.###} 文案={full.SampleCountHint}");

            // ---- ④ 目标刻度不跟「要求 ≥ 6 组」开关走 ----
            // 关掉从严只影响"少于 6 组要不要判失败"，不影响"建议采几组"。
            // 若把两者绑在一起，关掉从严后 3 行会显示满格，看起来像"已经采够了"。
            var loose = NewPlugin(Array.Empty<HandEyeSample>(), requireRecommended: false);
            for (int i = 0; i < 3; i++) loose.Samples.Add(new HandEyeSample { RobotX = 300 + i });
            Check("[HE] 进度④：关掉「要求 ≥ 6 组」也不改进度刻度（3 组仍是半格，不是满格）",
                Math.Abs(loose.SampleProgress - 0.5) < 1e-12 && !loose.HasEnoughSamples
                && loose.SampleProgressTarget == HandEyeMath.RecommendedPairs,
                $"进度={loose.SampleProgress:0.###} 目标={loose.SampleProgressTarget}");

            // ---- ⑤ 把空白行填上一个数 → 进度立刻涨（行级 PropertyChanged 生效） ----
            double progressBefore = half.SampleProgress;
            half.Samples[3].RobotX = 42;
            Check("[HE] 进度⑤：把空白行填上一个数 → 有效 4 组、进度跟着涨（行级通知生效）",
                half.ValidSampleCount == 4 && half.BlankSampleCount == 0
                && Math.Abs(half.SampleProgress - progressBefore - 1.0 / 6.0) < 1e-12,
                $"改前={progressBefore:0.###} 改后={half.SampleProgress:0.###} 有效={half.ValidSampleCount}");

            // ---- ⑥ 删掉一行有效数据 → 进度回落 ----
            double progressBefore2 = half.SampleProgress;
            half.Samples.RemoveAt(0);
            Check("[HE] 进度⑥：删掉一行有效数据 → 有效 3 组、进度回落",
                half.ValidSampleCount == 3 && half.SampleProgress < progressBefore2,
                $"删前={progressBefore2:0.###} 删后={half.SampleProgress:0.###} 有效={half.ValidSampleCount}");
        }

        private static void PersistLayer()
        {
            var step = new CheckStepData();

            // ---- ① 确认后结果真的进了方案（Samples + SavedResult 都在 InputValues 里） ----
            var rows = RowsFrom(MakeSamples(TrueX1(), RobotPoses));
            var p1 = NewPlugin(rows);
            p1.SolveForPreview();
            bool solved1 = p1.IsPreviewOk;
            p1.OnConfirm(step);

            bool hasSaved = step.InputValues.TryGetValue(nameof(HandEyePlugin.SavedResult), out object? savedObj)
                            && savedObj is string s1 && s1.Length > 0;
            bool hasSamples = step.InputValues.ContainsKey(nameof(HandEyePlugin.Samples));

            Check("[HE] 持久化①：点「确定」后结果与数据都进了方案（SavedResult 非空 + Samples 在册）",
                hasSaved && hasSamples,
                $"SavedResult {(hasSaved ? "有" : "无")} / Samples {(hasSamples ? "有" : "无")}");

            Check("[HE] 持久化①：这次求解本身是成功的（否则后面几条没意义）",
                solved1, p1.PreviewError);

            // ---- ② 重开方案：新实例灌回配置后直接就是「完成」且 16 格一致 ----
            var reopened = new HandEyePlugin();
            reopened.ApplyConfigValues(step);

            bool okMatrix = reopened.IsPreviewOk
                            && reopened.PreviewMatrixCells.Length == 16
                            && reopened.PreviewMatrixCells[0] == p1.PreviewMatrixCells[0]
                            && reopened.PreviewMatrixCells[5] == p1.PreviewMatrixCells[5]
                            && reopened.PreviewMatrixCells[15] == p1.PreviewMatrixCells[15]
                            && reopened.PreviewMatrixCells[3] == p1.PreviewMatrixCells[3];

            Check("[HE] 持久化②：重开方案立刻显示上次结果（不用再算一遍）",
                reopened.IsPreviewOk, $"状态={reopened.PreviewStatusText}");

            Check("[HE] 持久化②：16 格与关前逐格一致",
                okMatrix,
                $"重开 [{string.Join(" ", reopened.PreviewMatrixCells)}] / 关前 [{string.Join(" ", p1.PreviewMatrixCells)}]");

            Check("[HE] 持久化②：数据表也回来了（8 行）",
                reopened.Samples.Count == rows.Count, $"重开 {reopened.Samples.Count} 行 / 原 {rows.Count} 行");

            Check("[HE] 持久化②：文案说明「数据未变，未重算」",
                reopened.PreviewMeta.Contains("未重算", StringComparison.Ordinal), reopened.PreviewMeta);

            // ---- ③ 改一个数字 → 过期，且**不显示**那 16 个数 ----
            // 先记下方案里存着的第一行数值：改界面上的行**不能**连带改掉方案里那份
            // （否则点「取消」也留下了改动）。这条与下一条一起把"集合是引用"这个坑钉住。
            var storedRows = (ObservableCollection<HandEyeSample>)step.InputValues[nameof(HandEyePlugin.Samples)];
            double storedRobotX = storedRows[0].RobotX;

            reopened.Samples[0].RobotX += 1.0;
            bool stale = reopened.IsPreviewStale;
            bool hidden = reopened.PreviewMatrixCells.All(c => c == "—");

            Check("[HE] 持久化③：改界面上的行不会连带改掉方案里存着的那份（集合必须克隆）",
                Math.Abs(storedRows[0].RobotX - storedRobotX) < 1e-12,
                $"方案里 {storedRobotX:0.###} / 现在 {storedRows[0].RobotX:0.###}");

            Check("[HE] 持久化③：改一行数字 → 结论转为「已过期」", stale, $"状态={reopened.PreviewStatusText}");
            Check("[HE] 持久化③：过期时 16 个数全部隐藏（旧矩阵摆出来就是在骗人）",
                hidden, $"首格={reopened.PreviewMatrixCells[0]} 末格={reopened.PreviewMatrixCells[15]}");
            Check("[HE] 持久化③：过期说明里写清是哪一次标定",
                reopened.PreviewStaleText.Contains("上次标定", StringComparison.Ordinal), reopened.PreviewStaleText);

            // ---- ④ 改门槛同样作废（判定依据变了，结论也就不作数） ----
            var p2 = new HandEyePlugin();
            p2.ApplyConfigValues(step);
            bool fresh2 = p2.IsPreviewOk;
            p2.MaxTranslationRmsMm = 2.0;
            Check("[HE] 持久化④：改平移上限 → 同样过期（不只是改数据才作废）",
                fresh2 && p2.IsPreviewStale, $"改前={fresh2} / 改后状态={p2.PreviewStatusText}");

            // ---- ⑤ 失败态点确定 → 方案里不留任何结论 ----
            var p3 = NewPlugin(RowsFrom(MakeSamples(TrueX1(), RobotPoses)).Take(2).ToList());
            p3.SolveForPreview();
            bool failed3 = p3.IsPreviewFailed;
            var step3 = new CheckStepData();
            p3.OnConfirm(step3);

            bool noSaved = !step3.InputValues.TryGetValue(nameof(HandEyePlugin.SavedResult), out object? s3obj)
                           || s3obj is not string s3 || s3.Length == 0;

            Check("[HE] 持久化⑤：失败态点确定 → SavedResult 被清空（不留旧结论）",
                failed3 && noSaved, $"失败={failed3} / SavedResult={(noSaved ? "空" : "有")}");

            // ---- ⑥ 过期后重算再确认 → 回到「完成」 ----
            reopened.SolveForPreview();
            bool resolvOk = reopened.IsPreviewOk;
            var step6 = new CheckStepData();
            reopened.OnConfirm(step6);

            var reopened6 = new HandEyePlugin();
            reopened6.ApplyConfigValues(step6);

            Check("[HE] 持久化⑥：过期后重算再确认 → 重开又回到「完成」",
                resolvOk && reopened6.IsPreviewOk && reopened6.PreviewMatrixCells[0] != "—",
                $"重算={resolvOk} / 重开状态={reopened6.PreviewStatusText} / 首格={reopened6.PreviewMatrixCells[0]}");
        }

        #endregion

        private static bool MatchesMatrix(double[] flat, Mat4 want, double tol)
        {
            if (flat.Length != 16) return false;
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 4; c++)
                    if (Math.Abs(flat[r * 4 + c] - want[r, c]) > tol)
                        return false;
            return true;
        }

        private static string DescribeDiff(double[] flat, Mat4 want)
        {
            double max = 0.0;
            int at = -1;
            for (int r = 0; r < 4 && r * 4 + 3 < flat.Length; r++)
                for (int c = 0; c < 4; c++)
                {
                    double d = Math.Abs(flat[r * 4 + c] - want[r, c]);
                    if (d > max) { max = d; at = r * 4 + c; }
                }
            return at < 0 ? "长度不足 16" : $"最大偏差 {max:0.###e+0}（第 {at} 个元素）";
        }

    }
}
