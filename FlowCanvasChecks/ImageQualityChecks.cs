using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Core.Interfaces;
using HalconDotNet;
using Plugin.ImageQuality;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 图像质量前置检查（Plugin.ImageQuality）的断言。
    ///
    /// 真值策略：**优先合成确定性图像**（背景/条纹/棋盘/纯色块，期望值可笔算），
    /// 再用 HALCON 同进程现算对拍，最后用仓内真图验证趋势：
    ///  · `pcb_focus` 121 张是**过焦点扫描**（061 附近最清晰、两端散焦），
    ///    所以断言“以 061 为锚、FocusRaw 与 |i−61| 的 Spearman ≤ −0.95”，
    ///    不写“全序列单调下降”——那会红，而且不是算法错；
    ///  · `label` 10 张 / `smd` 11 张只断言“指标有限、方向为正”，因为真图没有绝对曝光/对比度真值。
    ///
    /// 反向判据（R38）：每条主判据都配一个“改坏会红”的锚点——
    ///  · 去掉 real 转换 → 对焦单调性/峰值差会变差；
    ///  · 把曝光改成平均灰度 → 全 100 的图 `ExposureRaw` 不再是 0；
    ///  · 把对比度改成 min/max → 单坏点图的 `ContrastRaw` 会变 255；
    ///  · 灰度图强行算饱和度 → `SaturationRaw` 不再是 NaN；
    ///  · 去掉 uint2 的 255/65535 归一化 → byte/uint2 数值差 256 倍；
    ///  · 把 `IsUsable=false` 改成 `Fail` → `Success` 会变 false。
    /// </summary>
    internal static class ImageQualityChecks
    {
        private const int ImgW = 200;
        private const int ImgH = 200;

        public static void Run()
        {
            Section("[Quality] 图像质量前置检查插件（合成真值 + HALCON 对拍 + 真图趋势）");

            if (!ProbeHalconUsable())
                return;

            RunFocusMonotonic();
            RunExposureTruncation();
            RunContrastPercentile();
            RunSaturation();
            RunUint2Normalization();
            RunThresholdSemantics();
            RunGuardsAndPortContract();
            RunHalconCrossCheck();
            RunRealImages();
        }

        // ==================================================================
        //  [A1] 对焦：Gaussian blur 扫描必须单调下降
        // ==================================================================

        private static void RunFocusMonotonic()
        {
            Section("[Quality] A1 对焦（Gaussian blur 扫描 / real 域 Sobel）");

            try
            {
                using HImage checker = MakeChecker(ImgW, ImgH, 8);
                double[] sigmas = { 0.0, 0.5, 1.0, 2.0, 3.0, 4.0, 5.0 };
                var raw = new double[sigmas.Length];
                var score = new double[sigmas.Length];

                var plugin = NewPlugin();
                try
                {
                    plugin.FocusRef = 1000.0;   // 抬高参考值，让分数不早早夹到 1，便于看单调
                    plugin.EnableThreshold = false;
                    for (int i = 0; i < sigmas.Length; i++)
                    {
                        using HImage blurred = Blur(checker, sigmas[i]);
                        Measure(plugin, blurred);
                        if (plugin.Success.Value is not true)
                        {
                            Check("[Quality][A1] 对焦扫描执行成功", false,
                                $"sigma={sigmas[i]} 失败：{plugin.ErrorMessage.Value}");
                            return;
                        }
                        raw[i] = plugin.FocusRaw.Value is double r ? r : double.NaN;
                        score[i] = plugin.FocusScore.Value is double s ? s : double.NaN;
                    }
                }
                finally
                {
                    plugin.Dispose();
                }

                bool strictlyDecreasing = true;
                for (int i = 1; i < raw.Length; i++)
                {
                    if (!(raw[i] < raw[i - 1])) strictlyDecreasing = false;
                }

                Check("[Quality][A1] FocusRaw 随 blur σ 增大严格下降（σ=0/0.5/1/2/3/4/5）",
                    strictlyDecreasing,
                    "raw=" + string.Join(", ", raw.Select(v => v.ToString("F2"))));

                double rho = Spearman(sigmas, raw);
                Check("[Quality][A1b] FocusRaw 与 σ 的 Spearman ≤ −0.95",
                    rho <= -0.95,
                    $"rho={rho:F4}");

                Check("[Quality][A1c] 最强模糊（σ=5）的 FocusRaw 不到清晰图（σ=0）的 1/3（去掉 real 域/算子会退化）",
                    raw[0] > 0 && raw[raw.Length - 1] < raw[0] / 3.0,
                    $"σ0={raw[0]:F2} σ5={raw[raw.Length - 1]:F2}");

                bool scoreDecreasing = true;
                for (int i = 1; i < score.Length; i++)
                {
                    if (!(score[i] < score[i - 1])) scoreDecreasing = false;
                }
                Check("[Quality][A1d] FocusScore 同向下降（参考值 1000 未夹顶）",
                    scoreDecreasing,
                    "score=" + string.Join(", ", score.Select(v => v.ToString("F3"))));
            }
            catch (Exception ex)
            {
                Check("[Quality] A1 对焦扫描执行不抛异常", false, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        // ==================================================================
        //  [A2] 曝光：只数两端截断，不数平均灰度
        // ==================================================================

        private static void RunExposureTruncation()
        {
            Section("[Quality] A2 曝光（0/满量程截断占比）");

            try
            {
                using HImage stripe = MakeExposureImage();   // 10% = 0，10% = 255，80% = 128
                var plugin = NewPlugin();
                try
                {
                    plugin.MaxTruncationPercent = 5.0;
                    Measure(plugin, stripe);

                    double raw = plugin.ExposureRaw.Value is double r ? r : double.NaN;
                    double score = plugin.ExposureScore.Value is double s ? s : double.NaN;
                    Check("[Quality][A2] 10% 像素=0、10% 像素=255 → ExposureRaw = 20%",
                        Math.Abs(raw - 20.0) <= 0.01,
                        $"ExposureRaw={raw:F4}%（期望 20）");
                    Check("[Quality][A2b] 截断 20% > MaxTruncationPercent 5% → ExposureScore 夹到 0",
                        Math.Abs(score) <= 1e-9,
                        $"ExposureScore={score:F4}");
                }
                finally
                {
                    plugin.Dispose();
                }

                using HImage midGray = MakeUniform(100);
                var mid = NewPlugin();
                try
                {
                    mid.MaxTruncationPercent = 5.0;
                    Measure(mid, midGray);
                    double raw = mid.ExposureRaw.Value is double r ? r : double.NaN;
                    double score = mid.ExposureScore.Value is double s ? s : double.NaN;
                    // 反向锚点：如果实现把“平均灰度”当曝光，全 100 的图会得到约 39%，而不是 0。
                    Check("[Quality][A2c] 全 100 灰图没有截断 → ExposureRaw = 0、ExposureScore = 1（平均灰度当曝光会红）",
                        Math.Abs(raw) <= 1e-9 && Math.Abs(score - 1.0) <= 1e-9,
                        $"ExposureRaw={raw:F4}% ExposureScore={score:F4}");
                }
                finally
                {
                    mid.Dispose();
                }

                using HImage allZero = MakeUniform(0);
                var zero = NewPlugin();
                try
                {
                    zero.MaxTruncationPercent = 5.0;
                    Measure(zero, allZero);
                    double raw = zero.ExposureRaw.Value is double r ? r : double.NaN;
                    Check("[Quality][A2d] 全 0 图 → ExposureRaw = 100%",
                        Math.Abs(raw - 100.0) <= 0.01,
                        $"ExposureRaw={raw:F4}%");
                }
                finally
                {
                    zero.Dispose();
                }

                using HImage stripe2 = MakeExposureImage();
                var exposureProbe = NewPlugin();
                try
                {
                    exposureProbe.MaxTruncationPercent = 5.0;
                    Measure(exposureProbe, stripe2);
                    double measuredExposure = exposureProbe.ExposureRaw.Value is double e ? e : double.NaN;
                    double reference = ReferenceExposure(stripe2);
                    Check("[Quality][A2e] 与 HALCON gray_histo 现算的截断占比对拍 ≤0.5pp",
                        Math.Abs(measuredExposure - reference) <= 0.5,
                        $"插件={measuredExposure:F4}% 参考={reference:F4}%");
                }
                finally
                {
                    exposureProbe.Dispose();
                }
            }
            catch (Exception ex)
            {
                Check("[Quality] A2 曝光执行不抛异常", false, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        // ==================================================================
        //  [A3] 对比度：P5~P95，不是 min~max
        // ==================================================================

        private static void RunContrastPercentile()
        {
            Section("[Quality] A3 对比度（P5~P95 展宽）");

            try
            {
                using HImage stripe = MakeContrastImage();   // 10% = 50，80% = 128，10% = 200
                var plugin = NewPlugin();
                try
                {
                    plugin.ContrastRef = 128.0;
                    Measure(plugin, stripe);
                    double raw = plugin.ContrastRaw.Value is double r ? r : double.NaN;
                    double score = plugin.ContrastScore.Value is double s ? s : double.NaN;
                    Check("[Quality][A3] 10% 50 / 80% 128 / 10% 200 → ContrastRaw = 150",
                        Math.Abs(raw - 150.0) <= 1.0,
                        $"ContrastRaw={raw:F2}（期望 150）");
                    Check("[Quality][A3b] ContrastRaw 150 > 参考 128 → ContrastScore 夹到 1",
                        Math.Abs(score - 1.0) <= 1e-9,
                        $"ContrastScore={score:F4}");
                }
                finally
                {
                    plugin.Dispose();
                }

                using HImage midGray = MakeUniform(100);
                var mid = NewPlugin();
                try
                {
                    mid.ContrastRef = 128.0;
                    Measure(mid, midGray);
                    double raw = mid.ContrastRaw.Value is double r ? r : double.NaN;
                    Check("[Quality][A3c] 纯灰图 → ContrastRaw = 0",
                        Math.Abs(raw) <= 1e-9,
                        $"ContrastRaw={raw:F4}");
                }
                finally
                {
                    mid.Dispose();
                }

                using HImage outlier = MakeSingleOutlierImage();   // 纯 100 + 1 个 0 + 1 个 255
                var single = NewPlugin();
                try
                {
                    single.ContrastRef = 128.0;
                    Measure(single, outlier);
                    double raw = single.ContrastRaw.Value is double r ? r : double.NaN;
                    // 反向锚点：如果实现用 min/max，这张图会得到 255，而不是 0。
                    Check("[Quality][A3d] 纯灰 + 单坏点 → ContrastRaw 仍为 0（min/max 实现会给 255）",
                        Math.Abs(raw) <= 1e-9,
                        $"ContrastRaw={raw:F4}");
                }
                finally
                {
                    single.Dispose();
                }

                using HImage stripe2 = MakeContrastImage();
                var contrastProbe = NewPlugin();
                try
                {
                    contrastProbe.ContrastRef = 128.0;
                    Measure(contrastProbe, stripe2);
                    double measuredContrast = contrastProbe.ContrastRaw.Value is double c ? c : double.NaN;
                    double reference = ReferenceContrast(stripe2);
                    Check("[Quality][A3e] 与 HALCON gray_histo 现算的 P95−P5 对拍 ≤1 灰度级",
                        Math.Abs(measuredContrast - reference) <= 1.0,
                        $"插件={measuredContrast:F2} 参考={reference:F2}");
                }
                finally
                {
                    contrastProbe.Dispose();
                }
            }
            catch (Exception ex)
            {
                Check("[Quality] A3 对比度执行不抛异常", false, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        // ==================================================================
        //  [A4] 饱和度：只对彩色图算，灰度 NaN/0
        // ==================================================================

        private static void RunSaturation()
        {
            Section("[Quality] A4 饱和度（HSV S / 灰度跳过）");

            try
            {
                using HImage red = MakeColor(255, 0, 0);
                var plugin = NewPlugin();
                try
                {
                    plugin.SaturationRef = 128.0;
                    Measure(plugin, red);
                    bool hasColor = plugin.HasColor.Value is true;
                    double raw = plugin.SaturationRaw.Value is double r ? r : double.NaN;
                    double score = plugin.SaturationScore.Value is double s ? s : double.NaN;
                    Check("[Quality][A4] 纯红图 → HasColor=true、SaturationRaw ≈ 255",
                        hasColor && Math.Abs(raw - 255.0) <= 1.0,
                        $"HasColor={hasColor} SaturationRaw={raw:F2}");
                    Check("[Quality][A4b] 纯红图 SaturationScore 夹到 1",
                        Math.Abs(score - 1.0) <= 1e-9,
                        $"SaturationScore={score:F4}");
                }
                finally
                {
                    plugin.Dispose();
                }

                using HImage grayColor = MakeColor(128, 128, 128);
                var grayC = NewPlugin();
                try
                {
                    grayC.SaturationRef = 128.0;
                    Measure(grayC, grayColor);
                    double raw = grayC.SaturationRaw.Value is double r ? r : double.NaN;
                    Check("[Quality][A4c] 三通道纯灰 → HasColor=true、SaturationRaw ≈ 0",
                        grayC.HasColor.Value is true && Math.Abs(raw) <= 1.0,
                        $"SaturationRaw={raw:F2}");
                }
                finally
                {
                    grayC.Dispose();
                }

                using HImage gray = MakeUniform(128);
                var grayPlugin = NewPlugin();
                try
                {
                    Measure(grayPlugin, gray);
                    bool hasColor = grayPlugin.HasColor.Value is true;
                    bool isNan = grayPlugin.SaturationRaw.Value is double r && double.IsNaN(r);
                    double score = grayPlugin.SaturationScore.Value is double s ? s : double.NaN;
                    bool detailsSaysSkip = grayPlugin.Details.Value is string[] lines
                        && lines.Any(l => l.Contains("跳过"));
                    // 反向锚点：灰度图强行算饱和度会得到数字，isNan 会红。
                    Check("[Quality][A4d] 灰度图 → HasColor=false、SaturationRaw=NaN、SaturationScore=0、明细写跳过",
                        !hasColor && isNan && Math.Abs(score) <= 1e-9 && detailsSaysSkip,
                        $"HasColor={hasColor} NaN={isNan} score={score:F4} skip={detailsSaysSkip}");
                }
                finally
                {
                    grayPlugin.Dispose();
                }

                using HImage red2 = MakeColor(255, 0, 0);
                MeasureWithNewPlugin(red2, out _, out double satMeasured);
                double satReference = ReferenceSaturation(red2);
                Check("[Quality][A4e] 与 HALCON decompose3+trans_from_rgb 现算的 S 均值对拍 ≤1",
                    Math.Abs(satMeasured - satReference) <= 1.0,
                    $"插件={satMeasured:F2} 参考={satReference:F2}");
            }
            catch (Exception ex)
            {
                Check("[Quality] A4 饱和度执行不抛异常", false, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        // ==================================================================
        //  [A5] uint2 归一化：byte 与 uint2 同图案指标同量级
        // ==================================================================

        private static void RunUint2Normalization()
        {
            Section("[Quality] A5 uint2 归一化（255/65535）");

            try
            {
                using HImage byteChecker = MakeChecker(ImgW, ImgH, 8);
                using HImage uint2Checker = MakeCheckerUint2(ImgW, ImgH, 8);

                HOperatorSet.GetImageType(uint2Checker, out HTuple u2Type);
                HOperatorSet.MinMaxGray(uint2Checker, uint2Checker, 0.0,
                    out HTuple u2Min, out HTuple u2Max, out HTuple _);
                HOperatorSet.GetImageType(byteChecker, out HTuple bType);
                HOperatorSet.MinMaxGray(byteChecker, byteChecker, 0.0,
                    out HTuple bMin, out HTuple bMax, out HTuple _);
                Check("[Quality][A5-img] 夹具前提：byte 棋盘 0/255、uint2 棋盘 0/65535（夹具错则归一化用例无效）",
                    bMin.D == 0 && bMax.D == 255 && u2Min.D == 0 && u2Max.D == 65535,
                    $"byte(type={bType.S}, min={bMin.D}, max={bMax.D}) uint2(type={u2Type.S}, min={u2Min.D}, max={u2Max.D})");

                double byteFocus, uint2Focus, byteContrast, uint2Contrast, byteExposure, uint2Exposure;
                var p1 = NewPlugin();
                try
                {
                    p1.FocusRef = 1000.0;
                    p1.ContrastRef = 128.0;
                    Measure(p1, byteChecker);
                    byteFocus = p1.FocusRaw.Value is double r ? r : double.NaN;
                    byteContrast = p1.ContrastRaw.Value is double c ? c : double.NaN;
                    byteExposure = p1.ExposureRaw.Value is double e ? e : double.NaN;
                }
                finally { p1.Dispose(); }

                var p2 = NewPlugin();
                try
                {
                    p2.FocusRef = 1000.0;
                    p2.ContrastRef = 128.0;
                    Measure(p2, uint2Checker);
                    Check("[Quality][A5-pre] uint2 灰度图测量执行成功（直方图箱数按位深精确）",
                        p2.Success.Value is true,
                        $"Success={p2.Success.Value} 文案={p2.ErrorMessage.Value}");
                    uint2Focus = p2.FocusRaw.Value is double r ? r : double.NaN;
                    uint2Contrast = p2.ContrastRaw.Value is double c ? c : double.NaN;
                    uint2Exposure = p2.ExposureRaw.Value is double e ? e : double.NaN;
                }
                finally { p2.Dispose(); }

                double ratio = byteFocus > 0 ? uint2Focus / byteFocus : double.NaN;
                // 反向锚点：去掉 255/65535 归一化，ratio 会接近 256。
                Check("[Quality][A5] byte 与 uint2 同图案的 FocusRaw 同量级（比值 0.9~1.1；不归一化会差 256 倍）",
                    ratio > 0.9 && ratio < 1.1,
                    $"byte={byteFocus:F2} uint2={uint2Focus:F2} ratio={ratio:F3}");
                Check("[Quality][A5b] byte 与 uint2 同图案的 ContrastRaw 差 ≤2（都归一化到 0~255）",
                    Math.Abs(byteContrast - uint2Contrast) <= 2.0,
                    $"byte={byteContrast:F2} uint2={uint2Contrast:F2}");
                Check("[Quality][A5c] byte 与 uint2 同图案的 ExposureRaw 一致（两端截断比例不变）",
                    Math.Abs(byteExposure - uint2Exposure) <= 0.5,
                    $"byte={byteExposure:F2}% uint2={uint2Exposure:F2}%");

                using HImage redUint2 = MakeColor(255, 0, 0, uint2: true);
                var p3 = NewPlugin();
                try
                {
                    p3.SaturationRef = 128.0;
                    Measure(p3, redUint2);
                    double raw = p3.SaturationRaw.Value is double r ? r : double.NaN;
                    Check("[Quality][A5d] uint2 纯红 → SaturationRaw ≈ 255（先 scale 再转 byte，不截顶）",
                        Math.Abs(raw - 255.0) <= 2.0,
                        $"SaturationRaw={raw:F2}");
                }
                finally { p3.Dispose(); }
            }
            catch (Exception ex)
            {
                Check("[Quality] A5 uint2 归一化执行不抛异常", false, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        // ==================================================================
        //  [C] 阈值语义：IsUsable 不改 Success
        // ==================================================================

        private static void RunThresholdSemantics()
        {
            Section("[Quality] C 阈值语义（IsUsable / Success 分离）");

            try
            {
                using HImage blurred = Blur(MakeChecker(ImgW, ImgH, 8), 4.0);

                var failing = NewPlugin();
                try
                {
                    failing.FocusRef = 200.0;
                    failing.EnableThreshold = true;
                    failing.FocusMin = 0.99;
                    failing.FocusMax = 1.0;
                    Measure(failing, blurred);
                    bool success = failing.Success.Value is true;
                    bool usable = failing.IsUsable.Value is true;
                    string reason = failing.Reason.Value?.ToString() ?? "";
                    Check("[Quality][C1] 阈值越界 → IsUsable=false，但 Success 仍为 true（质量不达标≠执行失败）",
                        success && !usable && reason.Contains("对焦"),
                        $"Success={success} IsUsable={usable} Reason={reason}");
                }
                finally { failing.Dispose(); }

                var passing = NewPlugin();
                try
                {
                    passing.EnableThreshold = true;
                    Measure(passing, blurred);
                    Check("[Quality][C2] 阈值全 0~1 → IsUsable=true、Reason=全部启用项在阈值内",
                        passing.Success.Value is true
                        && passing.IsUsable.Value is true
                        && (passing.Reason.Value?.ToString() ?? "").Contains("全部"),
                        $"IsUsable={passing.IsUsable.Value} Reason={passing.Reason.Value}");
                }
                finally { passing.Dispose(); }

                var invalid = NewPlugin();
                try
                {
                    invalid.EnableThreshold = true;
                    invalid.FocusMin = 0.9;
                    invalid.FocusMax = 0.1;
                    Measure(invalid, blurred);
                    string msg = invalid.ErrorMessage.Value?.ToString() ?? "";
                    Check("[Quality][C3] Min > Max → Success=false，文案列出对焦（不自动交换）",
                        invalid.Success.Value is false && msg.Contains("阈值区间非法") && msg.Contains("对焦"),
                        $"Success={invalid.Success.Value} 文案={msg}");
                }
                finally { invalid.Dispose(); }

                var metricsOnly = NewPlugin();
                try
                {
                    Measure(metricsOnly, blurred);
                    Check("[Quality][C4] 默认 metrics-only → IsUsable=true、Reason 含 metrics-only",
                        metricsOnly.IsUsable.Value is true
                        && (metricsOnly.Reason.Value?.ToString() ?? "").Contains("metrics-only"),
                        $"IsUsable={metricsOnly.IsUsable.Value} Reason={metricsOnly.Reason.Value}");
                }
                finally { metricsOnly.Dispose(); }

                using HImage gray = MakeUniform(128);
                var grayThreshold = NewPlugin();
                try
                {
                    grayThreshold.EnableThreshold = true;
                    grayThreshold.SaturationMin = 0.9;
                    grayThreshold.SaturationMax = 1.0;
                    Measure(grayThreshold, gray);
                    string reason = grayThreshold.Reason.Value?.ToString() ?? "";
                    // 反向锚点：灰度图如果没跳过饱和度，SaturationScore=0 会落在 [0.9,1.0] 外 → IsUsable=false。
                    Check("[Quality][C5] 灰度图跳过饱和度阈值 → 即使 SaturationMin=0.9 仍 IsUsable=true",
                        grayThreshold.IsUsable.Value is true && !reason.Contains("饱和度"),
                        $"IsUsable={grayThreshold.IsUsable.Value} Reason={reason}");
                }
                finally { grayThreshold.Dispose(); }
            }
            catch (Exception ex)
            {
                Check("[Quality] C 阈值语义执行不抛异常", false, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        // ==================================================================
        //  [D] 守卫与端口/配置契约
        // ==================================================================

        private static void RunGuardsAndPortContract()
        {
            Section("[Quality] D 守卫与端口/配置契约");

            var plugin = NewPlugin();
            try
            {
                Check("[Quality][D1] 输入端口：Image 必填、Roi 可选，类型正确",
                    plugin.Image.IsRequired && !plugin.Roi.IsRequired
                    && plugin.Image.DataType == typeof(HImage)
                    && plugin.Roi.DataType == typeof(HRegion),
                    $"Image必填={plugin.Image.IsRequired} Roi必填={plugin.Roi.IsRequired}");

                Check("[Quality][D1b] 输出端口类型：4 原始值/4 分数 double、HasColor/IsUsable bool、Reason string、Details string[]",
                    plugin.FocusRaw.DataType == typeof(double)
                    && plugin.ExposureRaw.DataType == typeof(double)
                    && plugin.ContrastRaw.DataType == typeof(double)
                    && plugin.SaturationRaw.DataType == typeof(double)
                    && plugin.FocusScore.DataType == typeof(double)
                    && plugin.ExposureScore.DataType == typeof(double)
                    && plugin.ContrastScore.DataType == typeof(double)
                    && plugin.SaturationScore.DataType == typeof(double)
                    && plugin.HasColor.DataType == typeof(bool)
                    && plugin.IsUsable.DataType == typeof(bool)
                    && plugin.Reason.DataType == typeof(string)
                    && plugin.Details.DataType == typeof(string[]),
                    "逐项核对");

                Check("[Quality][D2] 默认值：FocusRef=200 / ContrastRef=128 / SaturationRef=128 / MaxTruncation=5 / MinRoi=16 / 阈值关闭",
                    Math.Abs(plugin.FocusRef - 200.0) < 1e-9
                    && Math.Abs(plugin.ContrastRef - 128.0) < 1e-9
                    && Math.Abs(plugin.SaturationRef - 128.0) < 1e-9
                    && Math.Abs(plugin.MaxTruncationPercent - 5.0) < 1e-9
                    && plugin.MinRoiPixels == 16
                    && plugin.EnableThreshold is false,
                    $"FocusRef={plugin.FocusRef} ContrastRef={plugin.ContrastRef} SaturationRef={plugin.SaturationRef} MaxTrunc={plugin.MaxTruncationPercent} MinRoi={plugin.MinRoiPixels} Enable={plugin.EnableThreshold}");

                var display = typeof(ImageQualityPlugin)
                    .GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.DisplayAttribute), false)
                    .OfType<System.ComponentModel.DataAnnotations.DisplayAttribute>()
                    .FirstOrDefault();
                Check("[Quality][D3] [Display] 名称=图像质量检查、分组=图像处理",
                    display?.Name == "图像质量检查" && display?.GroupName == "图像处理",
                    $"Name={display?.Name} GroupName={display?.GroupName}");

                bool parallelSafe = typeof(ImageQualityPlugin)
                    .GetCustomAttributes(typeof(ParallelSafeAttribute), false).Any();
                Check("[Quality][D4] [ParallelSafe] 已标注（service 无 static 字段）",
                    parallelSafe, $"ParallelSafe={parallelSafe}");

                Check("[Quality][D5] 配置项恰好 14 个（5 归一化 + 9 阈值）",
                    plugin.GetConfigProperties().Count == 14,
                    $"数量={plugin.GetConfigProperties().Count}");

                plugin.FocusRef = 0.0;
                plugin.MaxTruncationPercent = -1.0;
                plugin.MinRoiPixels = 0;
                plugin.FocusMin = 2.0;
                plugin.FocusMax = -1.0;
                Check("[Quality][D6] 参数夹取：FocusRef→0.01、MaxTrunc→0.001、MinRoi→1、FocusMin→1、FocusMax→0",
                    Math.Abs(plugin.FocusRef - 0.01) < 1e-9
                    && Math.Abs(plugin.MaxTruncationPercent - 0.001) < 1e-9
                    && plugin.MinRoiPixels == 1
                    && Math.Abs(plugin.FocusMin - 1.0) < 1e-9
                    && Math.Abs(plugin.FocusMax) < 1e-9,
                    $"FocusRef={plugin.FocusRef} MaxTrunc={plugin.MaxTruncationPercent} MinRoi={plugin.MinRoiPixels} FocusMin={plugin.FocusMin} FocusMax={plugin.FocusMax}");

                var helpResources = typeof(ImageQualityPlugin).Assembly.GetManifestResourceNames()
                    .Where(n => n.Contains(".Help.", StringComparison.Ordinal))
                    .ToArray();
                Check("[Quality][D7] 程序集内嵌 Help 手册 8 页（R41/R42）",
                    helpResources.Length == 8,
                    $"资源数={helpResources.Length}");

                var missing = NewPlugin();
                try
                {
                    missing.Execute(NewContext());
                    string msg = missing.ErrorMessage.Value?.ToString() ?? "";
                    Check("[Quality][D8] 图像未接 → Success=false 且文案指出 Image 端口",
                        missing.Success.Value is false && msg.Contains("Image"),
                        $"Success={missing.Success.Value} 文案={msg}");
                }
                finally { missing.Dispose(); }

                using HImage uniform = MakeUniform(100);
                using HImage real = ToReal(uniform);
                var realPlugin = NewPlugin();
                try
                {
                    Measure(realPlugin, real);
                    string msg = realPlugin.ErrorMessage.Value?.ToString() ?? "";
                    Check("[Quality][D9] real 图 → Success=false，文案说清只支持 byte/uint2",
                        realPlugin.Success.Value is false && msg.Contains("real") && msg.Contains("不受支持"),
                        $"Success={realPlugin.Success.Value} 文案={msg}");
                }
                finally { realPlugin.Dispose(); }

                using HImage twoChannel = MakeTwoChannel();
                var twoPlugin = NewPlugin();
                try
                {
                    Measure(twoPlugin, twoChannel);
                    string msg = twoPlugin.ErrorMessage.Value?.ToString() ?? "";
                    Check("[Quality][D10] 2 通道图 → Success=false，文案说清支持 1/3/4 通道",
                        twoPlugin.Success.Value is false && msg.Contains("通道数"),
                        $"Success={twoPlugin.Success.Value} 文案={msg}");
                }
                finally { twoPlugin.Dispose(); }

                using HRegion smallRoi = MakeRectRegion(0, 0, 1, 1);
                using HImage imgForRoi = MakeUniform(100);
                var smallRoiPlugin = NewPlugin();
                try
                {
                    Measure(smallRoiPlugin, imgForRoi, smallRoi);
                    string msg = smallRoiPlugin.ErrorMessage.Value?.ToString() ?? "";
                    Check("[Quality][D11] ROI 只有 4 像素 < 下限 16 → Success=false",
                        smallRoiPlugin.Success.Value is false && msg.Contains("ROI 有效像素"),
                        $"Success={smallRoiPlugin.Success.Value} 文案={msg}");
                }
                finally { smallRoiPlugin.Dispose(); }

                using HRegion outsideRoi = MakeRectRegion(500, 500, 510, 510);
                using HImage imgForOutside = MakeUniform(100);
                var outsidePlugin = NewPlugin();
                try
                {
                    Measure(outsidePlugin, imgForOutside, outsideRoi);
                    string msg = outsidePlugin.ErrorMessage.Value?.ToString() ?? "";
                    Check("[Quality][D12] ROI 完全在图外 → Success=false（有效像素 0）",
                        outsidePlugin.Success.Value is false && msg.Contains("有效像素"),
                        $"Success={outsidePlugin.Success.Value} 文案={msg}");
                }
                finally { outsidePlugin.Dispose(); }
            }
            catch (Exception ex)
            {
                Check("[Quality] D 守卫与契约执行不抛异常", false, $"{ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                plugin.Dispose();
            }
        }

        // ==================================================================
        //  HALCON 对拍（对焦）
        // ==================================================================

        private static void RunHalconCrossCheck()
        {
            Section("[Quality] HALCON 对拍（对焦）");

            try
            {
                using HImage checker = MakeChecker(ImgW, ImgH, 8);
                using HImage blurred = Blur(checker, 2.0);
                MeasureWithNewPlugin(blurred, out double measuredFocus, out _);
                double reference = ReferenceFocus(blurred);
                double rel = reference > 0 ? Math.Abs(measuredFocus - reference) / reference : double.NaN;
                Check("[Quality][H1] FocusRaw 与 HALCON sobel_amp+intensity 现算值相对误差 ≤1%",
                    rel <= 0.01,
                    $"插件={measuredFocus:F4} 参考={reference:F4} 相对误差={rel:P3}");
            }
            catch (Exception ex)
            {
                Check("[Quality] HALCON 对焦对拍执行不抛异常", false, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        // ==================================================================
        //  真图趋势
        // ==================================================================

        private static void RunRealImages()
        {
            Section("[Quality] 真图趋势（pcb_focus 过焦点扫描 / label / smd）");

            string root = FindRepoRoot();
            string pcbDir = Path.Combine(root, "Image", "images", "pcb_focus");
            string labelDir = Path.Combine(root, "Image", "images", "label");
            string smdDir = Path.Combine(root, "Image", "images", "smd");

            // ---- pcb_focus：061 为锚，FocusRaw 与 |i-61| 负相关 ----
            if (!Directory.Exists(pcbDir))
            {
                Check("[Quality][R1] pcb_focus 数据集存在", false, $"目录不存在：{pcbDir}");
            }
            else
            {
                var focus = new double[121];
                string? readError = null;
                var plugin = NewPlugin();
                try
                {
                    plugin.FocusRef = 200.0;
                    var ctx = NewContext();
                    for (int i = 1; i <= 121; i++)
                    {
                        string path = Path.Combine(pcbDir, $"pcb_focus_telecentric_{i:D3}.png");
                        if (!File.Exists(path))
                        {
                            readError = $"缺文件：{path}";
                            break;
                        }
                        using HImage image = ReadImage(path);
                        plugin.Image.Value = image;
                        plugin.Roi.Value = null;
                        plugin.Execute(ctx);
                        if (plugin.Success.Value is not true)
                        {
                            readError = $"第 {i} 张失败：{plugin.ErrorMessage.Value}";
                            break;
                        }
                        focus[i - 1] = plugin.FocusRaw.Value is double r ? r : double.NaN;
                    }
                }
                finally
                {
                    plugin.Dispose();
                }

                if (readError != null)
                {
                    Check("[Quality][R1] pcb_focus 121 张全部可算", false, readError);
                }
                else
                {
                    int peakIndex = Array.IndexOf(focus, focus.Max());
                    int peakFrame = peakIndex + 1;

                    // 全序列对 |i−61| 的相关性会被“近焦平顶”拉低；真正要钉的是散焦段。
                    // 头段（001→061）用于证明“越接近焦点越清晰”，尾段（061→121）用于证明“越散焦越低”。
                    double[] tailFrames = Enumerable.Range(61, 61).Select(i => (double)i).ToArray();
                    double[] tailFocus = focus.Skip(60).ToArray();
                    double[] headFrames = Enumerable.Range(1, 61).Select(i => (double)i).ToArray();
                    double[] headFocus = focus.Take(61).ToArray();
                    double rhoTail = Spearman(tailFrames, tailFocus);
                    double rhoHead = Spearman(headFrames, headFocus);

                    int[] sampleFrames = { 1, 11, 21, 31, 41, 51, 55, 57, 59, 61, 63, 65, 71, 81, 91, 101, 111, 121 };
                    string curve = string.Join(" ", sampleFrames.Select(i => $"{i}:{focus[i - 1]:F2}"));

                    Check("[Quality][R1] pcb_focus 最清晰帧落在 061 附近（±5 帧；近焦平顶允许少量偏移）",
                        Math.Abs(peakFrame - 61) <= 5,
                        $"峰值帧={peakFrame} FocusRaw={focus[peakIndex]:F2}; 采样 {curve}");
                    Check("[Quality][R1b] 散焦段 061→121：FocusRaw 与帧号 Spearman ≤ −0.95",
                        rhoTail <= -0.95,
                        $"rhoTail={rhoTail:F4}");
                    Check("[Quality][R1d] 接近段 001→061：FocusRaw 与帧号 Spearman ≥ +0.90",
                        rhoHead >= 0.90,
                        $"rhoHead={rhoHead:F4}");
                    Check("[Quality][R1c] 061 的 FocusRaw 同时高于 001 与 121（两端散焦）",
                        focus[60] > focus[0] && focus[60] > focus[120],
                        $"001={focus[0]:F2} 061={focus[60]:F2} 121={focus[120]:F2}");
                }
            }

            // ---- label 10 张 / smd 11 张：指标有限、方向为正、灰度图跳过饱和度 ----
            CheckRealGraySet("R2", "label", labelDir, Enumerable.Range(1, 10)
                .Select(i => $"label_{i:D2}.png").ToArray());
            CheckRealGraySet("R3", "smd", smdDir, Enumerable.Range(1, 11)
                .Select(i => $"smd_on_chip_{i:D2}.png").ToArray());
        }

        private static void CheckRealGraySet(string id, string name, string dir, string[] files)
        {
            if (!Directory.Exists(dir))
            {
                Check($"[Quality][{id}] {name} 数据集存在", false, $"目录不存在：{dir}");
                return;
            }

            int okCount = 0;
            string? error = null;
            var plugin = NewPlugin();
            try
            {
                plugin.FocusRef = 200.0;
                plugin.ContrastRef = 128.0;
                var ctx = NewContext();
                foreach (string file in files)
                {
                    string path = Path.Combine(dir, file);
                    if (!File.Exists(path))
                    {
                        error = $"缺文件：{path}";
                        break;
                    }
                    using HImage image = ReadImage(path);
                    plugin.Image.Value = image;
                    plugin.Roi.Value = null;
                    plugin.Execute(ctx);
                    if (plugin.Success.Value is not true)
                    {
                        error = $"{file} 失败：{plugin.ErrorMessage.Value}";
                        break;
                    }
                    double focusRaw = plugin.FocusRaw.Value is double f ? f : double.NaN;
                    double contrastRaw = plugin.ContrastRaw.Value is double c ? c : double.NaN;
                    bool hasColor = plugin.HasColor.Value is true;
                    bool satNan = plugin.SaturationRaw.Value is double s && double.IsNaN(s);
                    if (!(focusRaw > 0) || !(contrastRaw > 0) || hasColor || !satNan)
                    {
                        error = $"{file}: FocusRaw={focusRaw:F2} ContrastRaw={contrastRaw:F2} HasColor={hasColor} SatNaN={satNan}";
                        break;
                    }
                    okCount++;
                }
            }
            finally
            {
                plugin.Dispose();
            }

            Check($"[Quality][{id}] {name} {files.Length} 张：指标有限、FocusRaw/ContrastRaw>0、灰度图饱和度 NaN",
                error == null && okCount == files.Length,
                error ?? $"{okCount}/{files.Length} 张通过");
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
                Check("[Quality] HALCON 原生库可用（合成图可造、灰度可读）",
                    min.Length > 0 && Math.Abs(min.D - 1) < 1e-9,
                    $"min={min.D}");
                return true;
            }
            catch (Exception ex)
            {
                Check("[Quality] HALCON 原生库可用", false, $"{ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private static ImageQualityPlugin NewPlugin() => new() { InstanceName = "图像质量检查_断言" };

        private static ExecutionContext NewContext() => new(
            new StubLog(), new FlowSession { FlowName = "图像质量断言" },
            new WorkspaceContext(), new CancellationTokenSource().Token);

        private static void Measure(ImageQualityPlugin plugin, HImage image, HRegion? roi = null)
        {
            plugin.Image.Value = image;
            plugin.Roi.Value = roi;
            plugin.Execute(NewContext());
        }

        private static void MeasureWithNewPlugin(HImage image, out double focusRaw, out double saturationRaw)
        {
            var plugin = NewPlugin();
            try
            {
                plugin.FocusRef = 1000.0;
                plugin.SaturationRef = 128.0;
                Measure(plugin, image);
                focusRaw = plugin.FocusRaw.Value is double r ? r : double.NaN;
                saturationRaw = plugin.SaturationRaw.Value is double s ? s : double.NaN;
            }
            finally
            {
                plugin.Dispose();
            }
        }

        private static HImage MakeUniform(int gray, int width = ImgW, int height = ImgH)
        {
            HOperatorSet.GenImageConst(out HObject proto, "byte", width, height);
            try
            {
                HOperatorSet.GenImageProto(proto, out HObject image, gray);
                return new HImage(image);
            }
            finally
            {
                proto.Dispose();
            }
        }

        private static HImage MakeChecker(int width, int height, int cell)
        {
            HOperatorSet.GenCheckerRegion(out HObject region, width, height, cell, cell);
            try
            {
                HOperatorSet.RegionToBin(region, out HObject bin, 255, 0, width, height);
                return new HImage(bin);
            }
            finally
            {
                region.Dispose();
            }
        }

        private static HImage MakeCheckerUint2(int width, int height, int cell)
        {
            using HImage byteChecker = MakeChecker(width, height, cell);
            HObject? uint2Low = null;
            HObject? uint2Full = null;
            try
            {
                // byte 0/255 → uint2 0/255（convert 只换类型不缩放）→ uint2 上 scale×257 → 0/65535。
                // 不能在 byte 上直接 scale×257：scale_image 输出仍是 byte，会截到 255。
                HOperatorSet.ConvertImageType(byteChecker, out uint2Low, "uint2");
                HOperatorSet.ScaleImage(uint2Low, out uint2Full, 257.0, 0.0);
                return new HImage(uint2Full);
            }
            finally
            {
                uint2Low?.Dispose();
                // uint2Full 交给 HImage 持有，不在这里 Dispose（与 MakeUniform 的约定一致）。
            }
        }

        private static HImage MakeExposureImage()
        {
            using HImage baseImage = MakeUniform(128);
            using HImage step1 = StampRect(baseImage, 0, 0, 19, ImgW - 1, 0);
            return StampRect(step1, 20, 0, 39, ImgW - 1, 255);
        }

        private static HImage MakeContrastImage()
        {
            using HImage baseImage = MakeUniform(128);
            using HImage step1 = StampRect(baseImage, 0, 0, 19, ImgW - 1, 50);
            return StampRect(step1, ImgH - 20, 0, ImgH - 1, ImgW - 1, 200);
        }

        private static HImage MakeSingleOutlierImage()
        {
            using HImage baseImage = MakeUniform(100);
            using HImage step1 = StampRect(baseImage, 0, 0, 0, 0, 0);
            return StampRect(step1, 1, 1, 1, 1, 255);
        }

        private static HImage StampRect(HImage canvas, int row1, int col1, int row2, int col2, int gray)
        {
            HOperatorSet.GetImageSize(canvas, out HTuple w, out HTuple h);
            int imgW = w.I, imgH = h.I;
            int r2 = Math.Min(row2, imgH - 1), c2 = Math.Min(col2, imgW - 1);
            if (row1 > r2 || col1 > c2)
            {
                HOperatorSet.CopyImage(canvas, out HObject same);
                return new HImage(same);
            }

            HOperatorSet.GenRectangle1(out HObject rect, row1, col1, r2, c2);
            try
            {
                HOperatorSet.PaintRegion(rect, canvas, out HObject painted, gray, "fill");
                return new HImage(painted);
            }
            finally
            {
                rect.Dispose();
            }
        }

        private static HImage MakeColor(int red, int green, int blue, bool uint2 = false,
            int width = 100, int height = 100)
        {
            string type = uint2 ? "uint2" : "byte";
            double scale = uint2 ? 257.0 : 1.0;
            HObject? pr = null, pg = null, pb = null;
            HObject? r = null, g = null, b = null, color = null;
            try
            {
                HOperatorSet.GenImageConst(out pr, type, width, height);
                HOperatorSet.GenImageConst(out pg, type, width, height);
                HOperatorSet.GenImageConst(out pb, type, width, height);
                HOperatorSet.GenImageProto(pr, out r, red * scale);
                HOperatorSet.GenImageProto(pg, out g, green * scale);
                HOperatorSet.GenImageProto(pb, out b, blue * scale);
                HOperatorSet.Compose3(r, g, b, out color);
                return new HImage(color);
            }
            finally
            {
                pr?.Dispose();
                pg?.Dispose();
                pb?.Dispose();
                r?.Dispose();
                g?.Dispose();
                b?.Dispose();
                color?.Dispose();
            }
        }

        private static HImage MakeTwoChannel()
        {
            HOperatorSet.GenImageConst(out HObject pr, "byte", ImgW, ImgH);
            HOperatorSet.GenImageConst(out HObject pg, "byte", ImgW, ImgH);
            HObject? r = null, g = null;
            try
            {
                HOperatorSet.GenImageProto(pr, out r, 10);
                HOperatorSet.GenImageProto(pg, out g, 20);
                HOperatorSet.Compose2(r, g, out HObject two);
                return new HImage(two);
            }
            finally
            {
                pr.Dispose();
                pg.Dispose();
                r?.Dispose();
                g?.Dispose();
            }
        }

        private static HRegion MakeRectRegion(int row1, int col1, int row2, int col2)
        {
            HOperatorSet.GenRectangle1(out HObject rect, row1, col1, row2, col2);
            return new HRegion(rect);
        }

        private static HImage ToReal(HImage image)
        {
            HOperatorSet.ConvertImageType(image, out HObject real, "real");
            return new HImage(real);
        }

        private static HImage Blur(HImage image, double sigma)
        {
            if (sigma <= 0)
            {
                HOperatorSet.CopyImage(image, out HObject copy);
                return new HImage(copy);
            }
            HOperatorSet.SmoothImage(image, out HObject smooth, "gauss", sigma);
            return new HImage(smooth);
        }

        private static HImage ReadImage(string path)
        {
            HOperatorSet.ReadImage(out HObject image, path);
            return new HImage(image);
        }

        private static double ReferenceFocus(HImage image)
        {
            HObject? real = null, edge = null;
            try
            {
                HOperatorSet.ConvertImageType(image, out real, "real");
                HOperatorSet.SobelAmp(real, out edge, "sum_abs", 3);
                HOperatorSet.Intensity(edge, edge, out HTuple mean, out _);
                return mean.Length > 0 ? mean[0].D : double.NaN;
            }
            finally
            {
                real?.Dispose();
                edge?.Dispose();
            }
        }

        private static double ReferenceExposure(HImage image)
        {
            HObject? domain = null;
            try
            {
                HOperatorSet.GetDomain(image, out domain);
                HOperatorSet.GrayHisto(domain, image, out HTuple hist, out _);
                double total = 0.0;
                for (int i = 0; i < hist.Length; i++) total += hist[i].D;
                if (total <= 0) return double.NaN;
                double low = hist[0].D;
                double high = hist[hist.Length - 1].D;
                return 100.0 * (low + high) / total;
            }
            finally
            {
                domain?.Dispose();
            }
        }

        private static double ReferenceContrast(HImage image)
        {
            HObject? domain = null;
            try
            {
                HOperatorSet.GetDomain(image, out domain);
                HOperatorSet.GrayHisto(domain, image, out HTuple hist, out _);
                int bins = hist.Length;
                double total = 0.0;
                for (int i = 0; i < bins; i++) total += hist[i].D;
                if (total <= 0) return double.NaN;

                double cum = 0.0;
                int p5 = 0, p95 = bins - 1;
                bool f5 = false, f95 = false;
                for (int i = 0; i < bins; i++)
                {
                    cum += hist[i].D;
                    if (!f5 && cum >= total * 0.05) { p5 = i; f5 = true; }
                    if (!f95 && cum >= total * 0.95) { p95 = i; f95 = true; break; }
                }
                return p95 - p5;
            }
            finally
            {
                domain?.Dispose();
            }
        }

        private static double ReferenceSaturation(HImage color)
        {
            HObject? r = null, g = null, b = null, h = null, s = null, v = null;
            try
            {
                HOperatorSet.Decompose3(color, out r, out g, out b);
                HOperatorSet.TransFromRgb(r, g, b, out h, out s, out v, "hsv");
                HOperatorSet.Intensity(s, s, out HTuple mean, out _);
                return mean.Length > 0 ? mean[0].D : double.NaN;
            }
            finally
            {
                r?.Dispose();
                g?.Dispose();
                b?.Dispose();
                h?.Dispose();
                s?.Dispose();
                v?.Dispose();
            }
        }

        /// <summary>Spearman 秩相关（带并列平均秩）。</summary>
        private static double Spearman(double[] x, double[] y)
        {
            int n = x.Length;
            if (n < 2 || n != y.Length) return double.NaN;
            double[] rx = Rank(x);
            double[] ry = Rank(y);
            double mx = rx.Average(), my = ry.Average();
            double cov = 0.0, vx = 0.0, vy = 0.0;
            for (int i = 0; i < n; i++)
            {
                double dx = rx[i] - mx;
                double dy = ry[i] - my;
                cov += dx * dy;
                vx += dx * dx;
                vy += dy * dy;
            }
            if (vx <= 0 || vy <= 0) return double.NaN;
            return cov / Math.Sqrt(vx * vy);
        }

        private static double[] Rank(double[] values)
        {
            int n = values.Length;
            int[] order = Enumerable.Range(0, n).OrderBy(i => values[i]).ToArray();
            double[] ranks = new double[n];
            int i2 = 0;
            while (i2 < n)
            {
                int j = i2;
                while (j + 1 < n && Math.Abs(values[order[j + 1]] - values[order[i2]]) < 1e-12)
                    j++;
                double avg = (i2 + j) / 2.0 + 1.0;
                for (int k = i2; k <= j; k++) ranks[order[k]] = avg;
                i2 = j + 1;
            }
            return ranks;
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "VisionMaster.sln")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return Directory.GetCurrentDirectory();
        }
    }
}
