using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Core.Interfaces;
using HalconDotNet;
using Plugin.ImageAlign;
using Plugin.ImageDiff;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 图像对齐（Plugin.ImageAlign）的断言。
    ///
    /// 真值策略：用 HALCON 造**已知变换**（平移/旋转/缩放/非等比/透视），把基准图 warp 成当前图，
    /// 再把对应点喂给插件；插件应恢复“当前→基准”的逆变换，把图搬回基准。
    /// 判据：4 角点重投影、对齐图平均绝对差、正逆矩阵往返、有效区比例。
    ///
    /// 关键事实：**4 点透视无冗余，残差恒 0**。要检测错点必须用验证点（第 5 对）；
    /// 本文件用“挪错一个拟合点 20px + 验证点”来钉住这条，而不是假装 4 点残差能发现问题。
    ///
    /// 下游收益用真 ImageDiff 跑：不对齐差异大；对齐 + ValidRegion 后差异面积下降 ≥80%；
    /// 把矩阵故意错 2px，差异面积必须回升。
    /// </summary>
    internal static class ImageAlignChecks
    {
        private const int W = 240;
        private const int H = 240;

        public static void Run()
        {
            Section("[Align] 图像对齐插件（已知变换 + 正逆往返 + 有效区 + 下游收益）");

            if (!ProbeHalconUsable())
                return;

            RunKnownTransforms();
            RunPointCounts();
            RunRansac();
            RunGuards();
            RunTemplatePose();
            RunDownstreamBenefit();
            RunContract();
            RunViewLogic();
        }

        // ==================================================================
        //  已知变换：平移/旋转/缩放/非等比/透视（4 点）
        // ==================================================================

        private static void RunKnownTransforms()
        {
            Section("[Align] A1 已知变换（4 点透视恢复）");

            try
            {
                RunKnown("平移", HomTranslate(10, 15), 4, AlignMode.PointPairs, validation: true);
                RunKnown("旋转", HomRotate(0.20, H / 2.0, W / 2.0), 4, AlignMode.PointPairs, validation: true);
                RunKnown("等比缩放", HomScale(1.10, 1.10, H / 2.0, W / 2.0), 4, AlignMode.PointPairs, validation: true);
                RunKnown("非等比缩放", HomScale(1.08, 0.94, H / 2.0, W / 2.0), 4, AlignMode.PointPairs, validation: true);
                RunKnown("透视", HomPerspective(), 4, AlignMode.ManualFourPoint, validation: true);
            }
            catch (Exception ex)
            {
                Check("[Align] A1 已知变换执行不抛异常", false, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void RunKnown(string name, HTuple homRefToCur, int count, AlignMode mode, bool validation)
        {
            using HImage reference = MakeReference();
            using HImage current = Warp(reference, homRefToCur);

            double[] refRows = { 20, 20, H - 20, H - 20, H / 2.0 };
            double[] refCols = { 20, W - 20, 20, W - 20, W / 2.0 };
            var curRows = new double[5];
            var curCols = new double[5];
            for (int i = 0; i < 5; i++)
            {
                var (qr, qc) = MapPoint(homRefToCur, refRows[i], refCols[i]);
                curRows[i] = qr;
                curCols[i] = qc;
            }

            var plugin = NewPlugin();
            try
            {
                plugin.Mode = mode;
                plugin.RefPointsText = JoinPoints(refRows, refCols, count);
                plugin.CurPointsText = JoinPoints(curRows, curCols, count);
                plugin.ValidationPointText = validation
                    ? FormattableString.Invariant($"{refRows[4]},{refCols[4]},{curRows[4]},{curCols[4]}")
                    : string.Empty;
                plugin.Image.Value = current;
                plugin.Execute(NewContext());

                Check($"[Align][A1] {name}：执行成功", plugin.Success.Value is true,
                    $"Success={plugin.Success.Value} 文案={plugin.ErrorMessage.Value}");
                if (plugin.Success.Value is not true) return;

                AssertAligned(name, plugin, reference, refRows, refCols, curRows, curCols, count, validation);

                // 直接与 HALCON 现算的逆矩阵对拍（h33 归一后逐元）
                if (plugin.HomMat2D.Value is HTuple got)
                {
                    HOperatorSet.HomMat2dInvert(homRefToCur, out HTuple expected);
                    double diff = MaxAbsDiffNormalized(ToHom9(expected), got);
                    Check($"[Align][A1] {name}：HomMat2D 与 HALCON 现算逆矩阵对拍 ≤1e-6",
                        diff <= 1e-6, $"maxdiff={diff:0.###e+0}");
                }
            }
            finally
            {
                plugin.Dispose();
            }
        }

        private static void AssertAligned(
            string name, ImageAlignPlugin plugin, HImage reference,
            double[] refRows, double[] refCols, double[] curRows, double[] curCols,
            int count, bool validation)
        {
            if (plugin.HomMat2D.Value is not HTuple hom || hom.Length != 9
                || plugin.InverseHomMat2D.Value is not HTuple inv || inv.Length != 9)
            {
                Check($"[Align][A1] {name}：正逆矩阵都是 9 元", false,
                    $"Hom={(plugin.HomMat2D.Value as HTuple)?.Length} Inv={(plugin.InverseHomMat2D.Value as HTuple)?.Length}");
                return;
            }

            double maxRepro = 0;
            for (int i = 0; i < count; i++)
            {
                var (qr, qc) = MapPoint(hom, curRows[i], curCols[i]);
                maxRepro = Math.Max(maxRepro, Distance(qr, qc, refRows[i], refCols[i]));
            }
            Check($"[Align][A1] {name}：{count} 点重投影 ≤0.3px", maxRepro <= 0.3,
                $"max={maxRepro:F4}px");

            var (ar, ac) = MapPoint(hom, curRows[0], curCols[0]);
            var (br, bc) = MapPoint(inv, ar, ac);
            double roundTrip = Distance(br, bc, curRows[0], curCols[0]);
            Check($"[Align][A1] {name}：正逆矩阵往返 ≤0.1px", roundTrip <= 0.1,
                $"err={roundTrip:F6}px");

            if (plugin.ValidRegion.Value is HRegion valid && plugin.AlignedImage.Value is HImage aligned)
            {
                double mean = MeanAbsDiff(aligned, reference, valid);
                Check($"[Align][A1] {name}：有效区内对齐图平均绝对差 ≤2 灰度", mean <= 2.0,
                    $"mean={mean:F3}");
            }
            else
            {
                Check($"[Align][A1] {name}：AlignedImage/ValidRegion 有值", false,
                    $"Aligned={(plugin.AlignedImage.Value == null ? "null" : "有")} Valid={(plugin.ValidRegion.Value == null ? "null" : "有")}");
            }

            double ratio = plugin.ValidAreaRatio.Value is double r ? r : double.NaN;
            Check($"[Align][A1] {name}：有效区比例在 (0,1] 且非全画幅", ratio > 0 && ratio <= 1.0001 && ratio < 0.999,
                $"ratio={ratio:F4}");

            double residual = plugin.Residual.Value is double rr ? rr : double.NaN;
            if (validation)
            {
                Check($"[Align][A1] {name}：验证点残差 ≤0.3px", residual <= 0.3,
                    $"residual={residual:F4}px");
            }
        }

        // ==================================================================
        //  2 点刚性 / 3 点仿射
        // ==================================================================

        private static void RunPointCounts()
        {
            Section("[Align] A2 2 点刚性 / 3 点仿射");

            try
            {
                RunKnown("2 点刚性（旋转）", HomRotate(0.18, H / 2.0, W / 2.0), 2, AlignMode.PointPairs, validation: true);
                RunKnown("3 点仿射（非等比）", HomScale(1.06, 0.95, H / 2.0, W / 2.0), 3, AlignMode.PointPairs, validation: true);

                var plugin = NewPlugin();
                try
                {
                    using HImage reference = MakeReference();
                    using HImage current = Warp(reference, HomTranslate(6, 9));
                    plugin.Image.Value = current;
                    plugin.RefPointsText = "20,20; 20,220";
                    plugin.CurPointsText = "26,29; 26,229";
                    plugin.Mode = AlignMode.PointPairs;
                    plugin.Execute(NewContext());
                    Check("[Align][A2] 2 点 → UsedPointCount=2、Success=true",
                        plugin.Success.Value is true && plugin.UsedPointCount.Value is 2,
                        $"Success={plugin.Success.Value} count={plugin.UsedPointCount.Value} 文案={plugin.ErrorMessage.Value}");
                }
                finally { plugin.Dispose(); }
            }
            catch (Exception ex)
            {
                Check("[Align] A2 点数模式执行不抛异常", false, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        // ==================================================================
        //  >4 点 RANSAC：剔除错配点
        // ==================================================================

        private static void RunRansac()
        {
            Section("[Align] A4 >4 点 RANSAC（剔除错配点）");

            try
            {
                using HImage reference = MakeReference();
                using HImage current = Warp(reference, HomTranslate(10, 15));

                double[] refRows = { 20, 20, 220, 220, 120, 120, 20, 220 };
                double[] refCols = { 20, 220, 20, 220, 20, 220, 120, 120 };
                var curRows = new double[8];
                var curCols = new double[8];
                for (int i = 0; i < 8; i++)
                {
                    var (qr, qc) = MapPoint(HomTranslate(10, 15), refRows[i], refCols[i]);
                    curRows[i] = qr;
                    curCols[i] = qc;
                }
                // 2 个错配点：第 2 个 row+30、第 5 个 col+30；其余 6 个正确
                curRows[1] += 30;
                curCols[4] += 30;

                var plugin = NewPlugin();
                try
                {
                    plugin.Mode = AlignMode.PointPairs;
                    plugin.RefPointsText = JoinPoints(refRows, refCols, 8);
                    plugin.CurPointsText = JoinPoints(curRows, curCols, 8);
                    plugin.Image.Value = current;
                    plugin.Execute(NewContext());

                    Check("[Align][A4] 8 点含 2 个错配 → 执行成功、内点 6 / 外点 2",
                        plugin.Success.Value is true
                        && plugin.UsedPointCount.Value is 8
                        && plugin.InlierCount.Value is 6
                        && plugin.OutlierCount.Value is 2,
                        $"Success={plugin.Success.Value} used={plugin.UsedPointCount.Value} in={plugin.InlierCount.Value} out={plugin.OutlierCount.Value} 文案={plugin.ErrorMessage.Value}");

                    if (plugin.Success.Value is true && plugin.HomMat2D.Value is HTuple hom)
                    {
                        double max = 0;
                        for (int i = 0; i < 8; i++)
                        {
                            if (i == 1 || i == 4) continue;   // 跳过两个错配点
                            var (qr, qc) = MapPoint(hom, curRows[i], curCols[i]);
                            max = Math.Max(max, Distance(qr, qc, refRows[i], refCols[i]));
                        }
                        Check("[Align][A4] RANSAC 后 6 个正确点重投影 ≤0.3px", max <= 0.3,
                            $"max={max:F4}px");
                        double residual = plugin.Residual.Value is double r ? r : double.NaN;
                        Check("[Align][A4] RANSAC 内点 RMS 残差 ≤0.3px", residual <= 0.3,
                            $"residual={residual:F4}px");
                    }
                }
                finally { plugin.Dispose(); }

                // 6 点全对 → 内点 6 / 外点 0
                var allGood = NewPlugin();
                try
                {
                    allGood.Mode = AlignMode.PointPairs;
                    allGood.RefPointsText = JoinPoints(refRows, refCols, 6);
                    var goodRows = new double[6];
                    var goodCols = new double[6];
                    for (int i = 0; i < 6; i++)
                    {
                        var (qr, qc) = MapPoint(HomTranslate(10, 15), refRows[i], refCols[i]);
                        goodRows[i] = qr;
                        goodCols[i] = qc;
                    }
                    allGood.CurPointsText = JoinPoints(goodRows, goodCols, 6);
                    allGood.Image.Value = current;
                    allGood.Execute(NewContext());
                    Check("[Align][A4] 6 点全对 → 内点 6 / 外点 0",
                        allGood.Success.Value is true
                        && allGood.InlierCount.Value is 6
                        && allGood.OutlierCount.Value is 0,
                        $"Success={allGood.Success.Value} in={allGood.InlierCount.Value} out={allGood.OutlierCount.Value} 文案={allGood.ErrorMessage.Value}");
                }
                finally { allGood.Dispose(); }

                // 只有 3 个正确点 + 5 个错配 → 一致点不足 5 个，必须失败
                var tooFew = NewPlugin();
                try
                {
                    var badRows = new double[8];
                    var badCols = new double[8];
                    for (int i = 0; i < 8; i++)
                    {
                        var (qr, qc) = MapPoint(HomTranslate(10, 15), refRows[i], refCols[i]);
                        badRows[i] = qr;
                        badCols[i] = qc;
                    }
                    for (int i = 3; i < 8; i++)
                    {
                        // 错配点必须“互不一致”：如果 5 个错配点整体平移一致，RANSAC 会正确地
                        // 把它们当成 5 个内点（它们确实能用一个变换解释），测不到“一致点不足”。
                        badRows[i] += 30 + (i * 17) % 40;
                        badCols[i] += 30 + (i * 23) % 40;
                    }
                    tooFew.Mode = AlignMode.PointPairs;
                    tooFew.InlierThreshold = 0.5;   // 收紧阈值，避免第 5 个点碰巧落进 2px
                    tooFew.RefPointsText = JoinPoints(refRows, refCols, 8);
                    tooFew.CurPointsText = JoinPoints(badRows, badCols, 8);
                    tooFew.Image.Value = current;
                    tooFew.Execute(NewContext());
                    string msg = tooFew.ErrorMessage.Value?.ToString() ?? string.Empty;
                    Check("[Align][A4] 只有 3 个正确点 + 5 个错配 → RANSAC 一致点不足，失败",
                        tooFew.Success.Value is false && msg.Contains("RANSAC"),
                        $"Success={tooFew.Success.Value} in={tooFew.InlierCount.Value} out={tooFew.OutlierCount.Value} 文案={msg}");
                }
                finally { tooFew.Dispose(); }
            }
            catch (Exception ex)
            {
                Check("[Align] A4 RANSAC 执行不抛异常", false, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        // ==================================================================
        //  守卫：手动四点 3 对 / 重合 / 共线 / 验证点错点 / 尺寸 / 有效区空
        // ==================================================================

        private static void RunGuards()
        {
            Section("[Align] C 守卫（点数/退化/验证点/尺寸/有效区）");

            try
            {
                using (HImage reference = MakeReference())
                using (HImage current = Warp(reference, HomTranslate(5, 5)))
                {
                    RunExpectFail("手动四点 3 对", p =>
                    {
                        p.Image.Value = current;
                        p.Mode = AlignMode.ManualFourPoint;
                        p.RefPointsText = "20,20; 20,220; 220,20";
                        p.CurPointsText = "25,25; 25,225; 225,25";
                    }, "4 对点");
                }

                using (HImage reference = MakeReference())
                {
                    RunExpectFail("两点重合", p =>
                    {
                        p.Image.Value = reference;
                        p.Mode = AlignMode.PointPairs;
                        p.RefPointsText = "20,20; 20,20";
                        p.CurPointsText = "30,30; 30,30";
                    }, "重合");

                    RunExpectFail("三点共线", p =>
                    {
                        p.Image.Value = reference;
                        p.Mode = AlignMode.PointPairs;
                        p.RefPointsText = "20,20; 20,80; 20,140";
                        p.CurPointsText = "30,30; 30,90; 30,150";
                    }, "共线");
                }

                using (HImage reference = MakeReference())
                using (HImage current = Warp(reference, HomTranslate(10, 12)))
                {
                    RunExpectFail("4 点错一个 + 验证点", p =>
                    {
                        p.Image.Value = current;
                        p.Mode = AlignMode.ManualFourPoint;
                        p.RefPointsText = "20,20; 20,220; 220,20; 220,220";
                        // 第 1 个当前点故意多挪 20px；其余 3 点正确
                        p.CurPointsText = "50,32; 30,232; 230,32; 230,232";
                        p.ValidationPointText = "120,120,130,132";
                    }, "验证点残差");
                }

                using (HImage reference = MakeReference())
                using (HImage small = MakeUniform(100, 200, 200))
                using (HImage current = Warp(reference, HomTranslate(5, 5)))
                {
                    RunExpectFail("基准图尺寸不一致", p =>
                    {
                        p.Image.Value = current;
                        p.ReferenceImage.Value = small;
                        p.Mode = AlignMode.PointPairs;
                        p.RefPointsText = "20,20; 20,220";
                        p.CurPointsText = "25,25; 25,225";
                    }, "尺寸");
                }

                using (HImage reference = MakeReference())
                {
                    RunExpectFail("有效区为空", p =>
                    {
                        p.Image.Value = reference;
                        p.Mode = AlignMode.TemplatePose;
                        p.UpstreamHomMat2D.Value = new HTuple(1, 0, 10000, 0, 1, 10000, 0, 0, 1);
                    }, "有效区为空");
                }
            }
            catch (Exception ex)
            {
                Check("[Align] C 守卫执行不抛异常", false, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void RunExpectFail(string name, Action<ImageAlignPlugin> configure, string expectedText)
        {
            var plugin = NewPlugin();
            try
            {
                configure(plugin);
                plugin.Execute(NewContext());
                string msg = plugin.ErrorMessage.Value?.ToString() ?? string.Empty;
                Check($"[Align][C] {name} → Success=false 且文案含「{expectedText}」",
                    plugin.Success.Value is false && msg.Contains(expectedText),
                    $"Success={plugin.Success.Value} 文案={msg}");
            }
            finally
            {
                plugin.Dispose();
            }
        }

        // ==================================================================
        //  上游位姿：PoseRow/Column/Angle 与上游 HomMat2D
        // ==================================================================

        private static void RunTemplatePose()
        {
            Section("[Align] A3 上游位姿（Pose / 上游 HomMat2D）");

            try
            {
                HOperatorSet.VectorAngleToRigid(
                    H / 2.0, W / 2.0, 0.0,
                    H / 2.0 + 12, W / 2.0 - 8, 0.15,
                    out HTuple homRefToCur);
                using HImage reference = MakeReference();
                using HImage current = Warp(reference, homRefToCur);

                var plugin = NewPlugin();
                try
                {
                    plugin.Mode = AlignMode.TemplatePose;
                    plugin.TemplateRefRow = H / 2.0;
                    plugin.TemplateRefColumn = W / 2.0;
                    plugin.PoseRow.Value = H / 2.0 + 12;
                    plugin.PoseColumn.Value = W / 2.0 - 8;
                    plugin.PoseAngle.Value = 0.15;
                    plugin.PoseScore.Value = 0.93;
                    plugin.Image.Value = current;
                    plugin.Execute(NewContext());

                    Check("[Align][A3] Pose 模式：执行成功、UsedPointCount=0、Score=PoseScore",
                        plugin.Success.Value is true
                        && plugin.UsedPointCount.Value is 0
                        && plugin.Score.Value is double s && Math.Abs(s - 0.93) < 1e-9,
                        $"Success={plugin.Success.Value} count={plugin.UsedPointCount.Value} score={plugin.Score.Value} 文案={plugin.ErrorMessage.Value}");

                    if (plugin.Success.Value is true
                        && plugin.AlignedImage.Value is HImage aligned
                        && plugin.ValidRegion.Value is HRegion valid)
                    {
                        double mean = MeanAbsDiff(aligned, reference, valid);
                        Check("[Align][A3] Pose 模式：有效区内对齐图平均绝对差 ≤2 灰度", mean <= 2.0,
                            $"mean={mean:F3}");
                    }

                    if (plugin.HomMat2D.Value is HTuple hom && plugin.InverseHomMat2D.Value is HTuple inv)
                    {
                        double rt = RoundTrip(hom, inv, H / 2.0, W / 2.0);
                        Check("[Align][A3] Pose 模式：正逆往返 ≤0.1px", rt <= 0.1, $"err={rt:F6}px");
                    }
                }
                finally { plugin.Dispose(); }

                // 上游 HomMat2D（参考→当前）直接输入
                var plugin2 = NewPlugin();
                try
                {
                    plugin2.Mode = AlignMode.TemplatePose;
                    plugin2.UpstreamHomMat2D.Value = homRefToCur;
                    plugin2.Image.Value = current;
                    plugin2.Execute(NewContext());
                    Check("[Align][A3] 上游 HomMat2D 模式：执行成功",
                        plugin2.Success.Value is true,
                        $"Success={plugin2.Success.Value} 文案={plugin2.ErrorMessage.Value}");
                    if (plugin2.Success.Value is true
                        && plugin2.AlignedImage.Value is HImage aligned2
                        && plugin2.ValidRegion.Value is HRegion valid2)
                    {
                        double mean = MeanAbsDiff(aligned2, reference, valid2);
                        Check("[Align][A3] 上游 HomMat2D：有效区内对齐图平均绝对差 ≤2 灰度", mean <= 2.0,
                            $"mean={mean:F3}");
                    }
                }
                finally { plugin2.Dispose(); }
            }
            catch (Exception ex)
            {
                Check("[Align] A3 上游位姿执行不抛异常", false, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        // ==================================================================
        //  下游收益：不对齐 vs 对齐 + ValidRegion；错 2px 回升
        // ==================================================================

        private static void RunDownstreamBenefit()
        {
            Section("[Align] R 下游收益（ImageDiff 面积下降 / 错 2px 回升）");

            try
            {
                using HImage reference = MakeChecker(20);
                using HImage current = Warp(reference, HomTranslate(10, 15));

                double rawArea = DiffArea(reference, current, null);
                Check("[Align][R1] 不对齐时 ImageDiff 差异面积明显大于 0", rawArea > 100,
                    $"rawArea={rawArea:F0}");

                var plugin = NewPlugin();
                HImage? aligned = null;
                HRegion? valid = null;
                HTuple? hom = null;
                try
                {
                    plugin.Mode = AlignMode.PointPairs;
                    plugin.RefPointsText = "20,20; 20,220; 220,20; 220,220";
                    plugin.CurPointsText = "30,35; 30,235; 230,35; 230,235";
                    plugin.Image.Value = current;
                    plugin.Execute(NewContext());
                    Check("[Align][R1] 对齐执行成功", plugin.Success.Value is true,
                        $"Success={plugin.Success.Value} 文案={plugin.ErrorMessage.Value}");
                    if (plugin.Success.Value is not true) return;

                    aligned = plugin.AlignedImage.Value as HImage;
                    valid = plugin.ValidRegion.Value as HRegion;
                    hom = plugin.HomMat2D.Value as HTuple;
                    if (aligned == null || valid == null || hom == null)
                    {
                        Check("[Align][R1] 对齐输出齐备", false, "Aligned/Valid/Hom 有 null");
                        return;
                    }

                    double alignedArea = DiffArea(reference, aligned, valid);
                    Check("[Align][R1b] 对齐 + ValidRegion 后差异面积下降 ≥80%",
                        alignedArea <= rawArea * 0.2,
                        $"raw={rawArea:F0} aligned={alignedArea:F0} 降幅={(rawArea > 0 ? 1 - alignedArea / rawArea : 0):P1}");

                    HOperatorSet.HomMat2dTranslate(hom, 2.0, 0.0, out HTuple wrongHom);
                    using HImage wrongAligned = Warp(current, wrongHom);
                    double wrongArea = DiffArea(reference, wrongAligned, valid);
                    Check("[Align][R1c] 矩阵故意错 2px → 差异面积回升",
                        wrongArea > alignedArea * 1.5 && wrongArea > alignedArea + 50,
                        $"aligned={alignedArea:F0} wrong2px={wrongArea:F0}");
                }
                finally
                {
                    plugin.Dispose();
                }
            }
            catch (Exception ex)
            {
                Check("[Align] R 下游收益执行不抛异常", false, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        // ==================================================================
        //  端口/配置/手册契约
        // ==================================================================

        private static void RunContract()
        {
            Section("[Align] D 端口/配置/手册契约");

            var plugin = NewPlugin();
            try
            {
                Check("[Align][D1] 输入端口：Image 必填；ReferenceImage/RefPoints/CurPoints/HomMat2D/Pose* 可选",
                    plugin.Image.IsRequired
                    && !plugin.ReferenceImage.IsRequired
                    && !plugin.RefPoints.IsRequired
                    && !plugin.CurPoints.IsRequired
                    && !plugin.UpstreamHomMat2D.IsRequired
                    && !plugin.PoseRow.IsRequired
                    && !plugin.PoseColumn.IsRequired
                    && !plugin.PoseAngle.IsRequired
                    && !plugin.PoseScore.IsRequired,
                    "逐项核对");

                Check("[Align][D1b] 输出端口类型：AlignedImage HImage、ValidRegion HRegion、正逆 HTuple、Score/Residual/Ratio double、Count int、IsOk bool、Reason string、Details string[]",
                    plugin.AlignedImage.DataType == typeof(HImage)
                    && plugin.ValidRegion.DataType == typeof(HRegion)
                    && plugin.HomMat2D.DataType == typeof(HTuple)
                    && plugin.InverseHomMat2D.DataType == typeof(HTuple)
                    && plugin.Score.DataType == typeof(double)
                    && plugin.Residual.DataType == typeof(double)
                    && plugin.ValidAreaRatio.DataType == typeof(double)
                    && plugin.UsedPointCount.DataType == typeof(int)
                    && plugin.InlierCount.DataType == typeof(int)
                    && plugin.OutlierCount.DataType == typeof(int)
                    && plugin.IsOk.DataType == typeof(bool)
                    && plugin.Reason.DataType == typeof(string)
                    && plugin.Details.DataType == typeof(string[]),
                    "逐项核对");

                Check("[Align][D2] 默认值：PointPairs / Bilinear / ResidualLimit=5 / ScoreRef=2 / InlierThreshold=2 / 阈值关闭",
                    plugin.Mode == AlignMode.PointPairs
                    && plugin.Interpolation == AlignInterpolation.Bilinear
                    && Math.Abs(plugin.ResidualLimit - 5.0) < 1e-9
                    && Math.Abs(plugin.ScoreRef - 2.0) < 1e-9
                    && Math.Abs(plugin.InlierThreshold - 2.0) < 1e-9
                    && plugin.EnableThreshold is false
                    && Math.Abs(plugin.MinScore) < 1e-9
                    && Math.Abs(plugin.MinValidAreaRatio) < 1e-9,
                    $"Mode={plugin.Mode} Interp={plugin.Interpolation} ResLimit={plugin.ResidualLimit} ScoreRef={plugin.ScoreRef} InlierTh={plugin.InlierThreshold} Enable={plugin.EnableThreshold}");

                var display = typeof(ImageAlignPlugin)
                    .GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.DisplayAttribute), false)
                    .OfType<System.ComponentModel.DataAnnotations.DisplayAttribute>()
                    .FirstOrDefault();
                Check("[Align][D3] [Display] 名称=图像对齐、分组=图像处理",
                    display?.Name == "图像对齐" && display?.GroupName == "图像处理",
                    $"Name={display?.Name} GroupName={display?.GroupName}");

                Check("[Align][D4] [ParallelSafe] 已标注", typeof(ImageAlignPlugin)
                    .GetCustomAttributes(typeof(ParallelSafeAttribute), false).Any(), "核对");

                Check("[Align][D5] 配置项恰好 13 个", plugin.GetConfigProperties().Count == 13,
                    $"数量={plugin.GetConfigProperties().Count}");

                plugin.ResidualLimit = -1;
                plugin.ScoreRef = 0;
                plugin.InlierThreshold = -1;
                plugin.MinScore = 2;
                plugin.MinValidAreaRatio = -1;
                Check("[Align][D6] 参数夹取：ResidualLimit→0、ScoreRef→0.01、InlierThreshold→0.01、MinScore→1、MinValidAreaRatio→0",
                    Math.Abs(plugin.ResidualLimit) < 1e-9
                    && Math.Abs(plugin.ScoreRef - 0.01) < 1e-9
                    && Math.Abs(plugin.InlierThreshold - 0.01) < 1e-9
                    && Math.Abs(plugin.MinScore - 1.0) < 1e-9
                    && Math.Abs(plugin.MinValidAreaRatio) < 1e-9,
                    $"ResLimit={plugin.ResidualLimit} ScoreRef={plugin.ScoreRef} InlierTh={plugin.InlierThreshold} MinScore={plugin.MinScore} MinRatio={plugin.MinValidAreaRatio}");

                var helpResources = typeof(ImageAlignPlugin).Assembly.GetManifestResourceNames()
                    .Where(n => n.Contains(".Help.", StringComparison.Ordinal)).ToArray();
                Check("[Align][D7] 程序集内嵌 Help 手册 8 页", helpResources.Length == 8,
                    $"资源数={helpResources.Length}");

                var missing = NewPlugin();
                try
                {
                    missing.Execute(NewContext());
                    string msg = missing.ErrorMessage.Value?.ToString() ?? string.Empty;
                    Check("[Align][D8] 图像未接 → Success=false 且文案指出 Image",
                        missing.Success.Value is false && msg.Contains("Image"),
                        $"Success={missing.Success.Value} 文案={msg}");
                }
                finally { missing.Dispose(); }
            }
            finally
            {
                plugin.Dispose();
            }
        }

        // ==================================================================
        //  画布视图逻辑（不实例化 WPF 视图，只测取点/序列化）
        // ==================================================================

        private static void RunViewLogic()
        {
            Section("[Align] V 画布视图逻辑（取点回填 / 序列化 / 反序列化）");

            try
            {
                Check("[Align][V1] ImageAlignPlugin 实现 IPluginCustomViewProvider",
                    typeof(ImageAlignPlugin).GetInterfaces().Contains(typeof(IPluginCustomViewProvider)),
                    "接口检查");

                var plugin = NewPlugin();
                try
                {
                    plugin.PointRows.Add(new AlignPointRow
                    {
                        Index = 1,
                        RefRow = 20,
                        RefCol = 30,
                        CurRow = 40,
                        CurCol = 50,
                    });
                    var stub = new StubStepConfigData();
                    plugin.OnConfirm(stub);

                    string refText = stub.InputValues.TryGetValue("RefPointsText", out var rv) ? rv?.ToString() ?? "" : "";
                    string curText = stub.InputValues.TryGetValue("CurPointsText", out var cv) ? cv?.ToString() ?? "" : "";
                    Check("[Align][V1] OnConfirm 把点行序列化为 RefPointsText/CurPointsText",
                        refText == "20,30" && curText == "40,50",
                        $"ref='{refText}' cur='{curText}'");

                    plugin.Initialize(stub);
                    Check("[Align][V2] Initialize 把文本反序列化回点行",
                        plugin.PointRows.Count == 1
                        && Math.Abs(plugin.PointRows[0].RefRow - 20) < 1e-9
                        && Math.Abs(plugin.PointRows[0].RefCol - 30) < 1e-9
                        && Math.Abs(plugin.PointRows[0].CurRow - 40) < 1e-9
                        && Math.Abs(plugin.PointRows[0].CurCol - 50) < 1e-9,
                        $"count={plugin.PointRows.Count}");

                    plugin.SelectedPointRow = plugin.PointRows[0];
                    plugin.StartPick(true);
                    plugin.ApplyPickedPoint(11.5, 22.5);
                    Check("[Align][V3] 取参考点：写回选中行的 RefRow/RefCol，并结束取点",
                        Math.Abs(plugin.PointRows[0].RefRow - 11.5) < 1e-9
                        && Math.Abs(plugin.PointRows[0].RefCol - 22.5) < 1e-9
                        && plugin.IsPicking is false,
                        $"R={plugin.PointRows[0].RefRow} C={plugin.PointRows[0].RefCol} picking={plugin.IsPicking}");

                    plugin.StartPick(false);
                    plugin.ApplyPickedPoint(31.5, 42.5);
                    Check("[Align][V4] 取当前点：写回选中行的 CurRow/CurCol",
                        Math.Abs(plugin.PointRows[0].CurRow - 31.5) < 1e-9
                        && Math.Abs(plugin.PointRows[0].CurCol - 42.5) < 1e-9,
                        $"R={plugin.PointRows[0].CurRow} C={plugin.PointRows[0].CurCol}");

                    plugin.ClearPoints();
                    Check("[Align][V5] 清空点对 → PointRows.Count=0、SelectedPointRow=null",
                        plugin.PointRows.Count == 0 && plugin.SelectedPointRow == null,
                        $"count={plugin.PointRows.Count}");
                }
                finally
                {
                    plugin.Dispose();
                }
            }
            catch (Exception ex)
            {
                Check("[Align] V 画布视图逻辑执行不抛异常", false, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        // ==================================================================
        //  工具
        // ==================================================================

        private static bool ProbeHalconUsable()
        {
            try
            {
                using HImage probe = MakeUniform(1, 4, 4);
                HOperatorSet.MinMaxGray(probe, probe, 0.0, out HTuple min, out HTuple _, out HTuple _);
                Check("[Align] HALCON 原生库可用", min.Length > 0 && Math.Abs(min.D - 1) < 1e-9,
                    $"min={min.D}");
                return true;
            }
            catch (Exception ex)
            {
                Check("[Align] HALCON 原生库可用", false, $"{ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private static ImageAlignPlugin NewPlugin() => new() { InstanceName = "图像对齐_断言" };

        private static ExecutionContext NewContext() => new(
            new StubLog(), new FlowSession { FlowName = "图像对齐断言" },
            new WorkspaceContext(), new CancellationTokenSource().Token);

        private static HImage MakeUniform(int gray, int width = W, int height = H)
        {
            HOperatorSet.GenImageConst(out HObject proto, "byte", width, height);
            try
            {
                HOperatorSet.GenImageProto(proto, out HObject image, gray);
                return new HImage(image);
            }
            finally { proto.Dispose(); }
        }

        /// <summary>低频平滑图案：对齐重采样的平均绝对差不会像棋盘那样被边缘放大。</summary>
        private static HImage MakeReference()
        {
            using HImage baseImage = MakeUniform(100);
            using HImage s1 = StampRect(baseImage, 30, 30, 90, 90, 40);
            using HImage s2 = StampRect(s1, 140, 50, 210, 120, 200);
            using HImage s3 = StampRect(s2, 60, 150, 120, 210, 220);
            HOperatorSet.SmoothImage(s3, out HObject smooth, "gauss", 2.0);
            return new HImage(smooth);
        }

        private static HImage MakeChecker(int cell)
        {
            HOperatorSet.GenCheckerRegion(out HObject region, W, H, cell, cell);
            try
            {
                HOperatorSet.RegionToBin(region, out HObject bin, 200, 30, W, H);
                return new HImage(bin);
            }
            finally { region.Dispose(); }
        }

        private static HImage StampRect(HImage canvas, int row1, int col1, int row2, int col2, int gray)
        {
            HOperatorSet.GenRectangle1(out HObject rect, row1, col1, row2, col2);
            try
            {
                HOperatorSet.PaintRegion(rect, canvas, out HObject painted, gray, "fill");
                return new HImage(painted);
            }
            finally { rect.Dispose(); }
        }

        private static HImage Warp(HImage image, HTuple hom)
        {
            HTuple hom9 = ToHom9(hom);
            HOperatorSet.ProjectiveTransImage(image, out HObject result, hom9, "bilinear", "false", "false");
            return new HImage(result);
        }

        private static HTuple HomTranslate(double row, double col)
        {
            HOperatorSet.HomMat2dIdentity(out HTuple hom);
            HOperatorSet.HomMat2dTranslate(hom, row, col, out HTuple result);
            return result;
        }

        private static HTuple HomRotate(double angle, double row, double col)
        {
            HOperatorSet.HomMat2dIdentity(out HTuple hom);
            HOperatorSet.HomMat2dRotate(hom, angle, row, col, out HTuple result);
            return result;
        }

        private static HTuple HomScale(double sx, double sy, double row, double col)
        {
            HOperatorSet.HomMat2dIdentity(out HTuple hom);
            HOperatorSet.HomMat2dScale(hom, sx, sy, row, col, out HTuple result);
            return result;
        }

        private static HTuple HomPerspective()
        {
            double[] refRows = { 20, 20, H - 20, H - 20 };
            double[] refCols = { 20, W - 20, 20, W - 20 };
            double[] curRows = { 26, 16, H - 16, H - 24 };
            double[] curCols = { 18, W - 24, 24, W - 16 };
            HOperatorSet.VectorToProjHomMat2d(
                new HTuple(refRows), new HTuple(refCols), new HTuple(curRows), new HTuple(curCols),
                "normalized_dlt",
                new HTuple(), new HTuple(), new HTuple(), new HTuple(), new HTuple(), new HTuple(),
                out HTuple hom, out HTuple _);
            return hom;
        }

        private static string JoinPoints(double[] rows, double[] cols, int count)
        {
            var parts = new string[count];
            for (int i = 0; i < count; i++)
                parts[i] = FormattableString.Invariant($"{rows[i]},{cols[i]}");
            return string.Join("; ", parts);
        }

        private static (double Row, double Col) MapPoint(HTuple hom, double row, double col)
        {
            HTuple hom9 = ToHom9(hom);
            HOperatorSet.ProjectiveTransPoint2d(hom9, row, col, 1.0,
                out HTuple qx, out HTuple qy, out HTuple qw);
            double w = qw.D;
            if (Math.Abs(w) < 1e-12) throw new InvalidOperationException("点映射 w≈0。");
            return (qx.D / w, qy.D / w);
        }

        private static double RoundTrip(HTuple hom, HTuple inv, double row, double col)
        {
            var (ar, ac) = MapPoint(hom, row, col);
            var (br, bc) = MapPoint(inv, ar, ac);
            return Distance(br, bc, row, col);
        }

        private static double MeanAbsDiff(HImage aligned, HImage reference, HRegion valid)
        {
            HObject? a = null, r = null, diff = null;
            try
            {
                HOperatorSet.ReduceDomain(aligned, valid, out a);
                HOperatorSet.ReduceDomain(reference, valid, out r);
                HOperatorSet.AbsDiffImage(a, r, out diff, 1.0);
                HOperatorSet.Intensity(diff, diff, out HTuple mean, out _);
                return mean.Length > 0 ? mean[0].D : double.NaN;
            }
            finally
            {
                a?.Dispose();
                r?.Dispose();
                diff?.Dispose();
            }
        }

        private static double DiffArea(HImage reference, HImage test, HRegion? mask)
        {
            var plugin = new ImageDiffPlugin { InstanceName = "对齐下游断言" };
            try
            {
                plugin.Reference.Value = reference;
                plugin.Test.Value = test;
                if (mask != null) plugin.MaskRegion.Value = mask;
                plugin.Threshold = 30;
                plugin.MinArea = 1;
                plugin.DisplayViewIndex = 0;
                plugin.Execute(NewContext());
                return plugin.DiffArea.Value is double d ? d : double.NaN;
            }
            finally
            {
                plugin.Dispose();
            }
        }

        private static HTuple ToHom9(HTuple hom)
        {
            if (hom.Length == 9) return hom;
            if (hom.Length == 6)
            {
                return new HTuple(
                    hom[0].D, hom[1].D, hom[2].D,
                    hom[3].D, hom[4].D, hom[5].D,
                    0.0, 0.0, 1.0);
            }
            throw new InvalidOperationException($"矩阵长度 {hom.Length} 不支持。");
        }

        private static double MaxAbsDiffNormalized(HTuple a, HTuple b)
        {
            double sa = a[8].D == 0 ? 1.0 : a[8].D;
            double sb = b[8].D == 0 ? 1.0 : b[8].D;
            double max = 0;
            for (int i = 0; i < 9; i++)
                max = Math.Max(max, Math.Abs(a[i].D / sa - b[i].D / sb));
            return max;
        }

        private static double Distance(double r1, double c1, double r2, double c2)
        {
            double dr = r1 - r2, dc = c1 - c2;
            return Math.Sqrt(dr * dr + dc * dc);
        }

        private sealed class StubStepConfigData : IStepConfigData
        {
            public Guid StepId => Guid.Empty;
            public string Icon => string.Empty;
            public string StepName => "图像对齐";
            public string Description => string.Empty;
            public Dictionary<string, object> InputValues { get; } = new();
            public List<DynamicPortInfo> OutputPortDefinitions { get; set; } = new();

            public void SetInputValue(string key, object value) => InputValues[key] = value;
            public void RemoveInputValue(string key) => InputValues.Remove(key);
            public bool IsLinked(string inputPortName) => false;
            public string GetLinkedAddress(string inputPortName) => null!;
            public LinkReference GetLink(string inputPortName) => null!;
            public void SetLink(string inputPortName, LinkReference link) { }
            public void RemoveLink(string inputPortName) { }
        }
    }
}
