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
            CsvLayer();
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
            double maxRot = 0.5)
        {
            // ⚠ 这些属性都带 [DefaultValue]，但只有走 Initialize 才会被填；
            // 断言里直接 new 出来必须**逐个显式赋值**，否则默认是 0/false 而不是 1.0/true。
            var plugin = new HandEyePlugin
            {
                RequireRecommendedPairs = requireRecommended,
                MaxTranslationRmsMm = maxTrans,
                MaxRotationRmsDeg = maxRot,
                PoseKind = HandEyePoseKind.EyeInHand,
            };
            foreach (var r in rows)
                plugin.Samples.Add(r);
            return plugin;
        }

        private static string Err(HandEyePlugin plugin)
            => plugin.ErrorMessage.Value as string ?? string.Empty;

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
