using Core.Interfaces;
using HalconDotNet;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Plugin.ImageQuality
{
    /// <summary>
    /// 图像质量前置检查：采集之后、算法之前，回答"这张图能不能用"。
    ///
    /// 设计口径（评审结论 2026-10-27）：
    ///  · v1 只做对焦 / 曝光 / 对比度 / 饱和度四类指标 + 原始值 + 明细；
    ///  · 脏污检测与现有 BlobDetect / DlAnomaly / DlSegment / DlClassify / ImageDiff 五条路重复，移出 v1；
    ///  · 默认 metrics-only：不配阈值就只输出数，<see cref="IsUsable"/> 恒 true；
    ///  · 配了阈值也不调 Fail —— 质量分不达标不等于算法执行失败，拦截交给下游「逻辑判断」/「判定表」；
    ///  · 不做图像修改、不烘焙、不开窗、不写全局变量。
    /// </summary>
    [Display(Name = "图像质量检查", GroupName = "图像处理",
        Description = "采集后、算法前评估对焦/曝光/对比度/饱和度，输出原始指标与 0~1 归一化分，默认不判 OK/NG",
        ShortName = "\uf0e4")]
    [ParallelSafe]
    public partial class ImageQualityPlugin : VisionPluginBase
    {
        // ── 输入端口 ───────────────────────────────────────────────────────────

        /// <summary>待检查的图像。必接。</summary>
        public InputPort<HImage> Image { get; } = new(
            "Image", description: "待检查的图像。必接。")
        { IsRequired = true };

        /// <summary>只检查产品区域（可选）。不接则按图像自带域（全图）检查。</summary>
        public InputPort<HRegion> Roi { get; } = new(
            "Roi", description: "只检查区（可选）：只统计该区域内的指标；不接则按全图检查")
        { IsRequired = false };

        // ── 输出端口：原始值（给现场调阈值） ──────────────────────────────────

        /// <summary>对焦原始值：sobel_amp('sum_abs') 均值，已归一化到 8bit 等效梯度。</summary>
        public OutputPort<double> FocusRaw { get; } = new(
            "FocusRaw", description: "对焦原始值：sobel_amp('sum_abs') 均值（8bit 等效梯度）");

        /// <summary>曝光原始值：0 / 满量程两端的截断像素总占比（百分比 0~100）。</summary>
        public OutputPort<double> ExposureRaw { get; } = new(
            "ExposureRaw", description: "曝光原始值：0/满量程截断像素总占比（%，0~100）");

        /// <summary>对比度原始值：灰度 P95 − P5（0~255 尺度）。</summary>
        public OutputPort<double> ContrastRaw { get; } = new(
            "ContrastRaw", description: "对比度原始值：P95 − P5（0~255 尺度）");

        /// <summary>饱和度原始值：HSV 的 S 通道均值（0~255）；灰度图为 NaN。</summary>
        public OutputPort<double> SaturationRaw { get; } = new(
            "SaturationRaw", description: "饱和度原始值：HSV S 通道均值（0~255）；灰度图为 NaN");

        // ── 输出端口：归一化分（0~1，便于配阈值） ────────────────────────────

        /// <summary>对焦分：clamp(FocusRaw / FocusRef, 0, 1)。</summary>
        public OutputPort<double> FocusScore { get; } = new(
            "FocusScore", description: "对焦分 0~1 = FocusRaw / FocusRef（夹取）");

        /// <summary>曝光分：1 − 截断占比 / MaxTruncationPercent（夹取 0~1）。</summary>
        public OutputPort<double> ExposureScore { get; } = new(
            "ExposureScore", description: "曝光分 0~1 = 1 − 截断占比/MaxTruncationPercent（夹取）");

        /// <summary>对比度分：clamp(ContrastRaw / ContrastRef, 0, 1)。</summary>
        public OutputPort<double> ContrastScore { get; } = new(
            "ContrastScore", description: "对比度分 0~1 = ContrastRaw / ContrastRef（夹取）");

        /// <summary>饱和度分：clamp(SaturationRaw / SaturationRef, 0, 1)；灰度图为 0。</summary>
        public OutputPort<double> SaturationScore { get; } = new(
            "SaturationScore", description: "饱和度分 0~1 = SaturationRaw / SaturationRef（夹取）；灰度图为 0");

        // ── 输出端口：判定与明细 ──────────────────────────────────────────────

        /// <summary>图像是否彩色（3/4 通道）。饱和度只对彩色图有意义。</summary>
        public OutputPort<bool> HasColor { get; } = new(
            "HasColor", description: "是否彩色图（3/4 通道）；灰度图饱和度被跳过");

        /// <summary>
        /// 可选阈值判定的结论。默认 true（未启用阈值）；配了阈值且任一项越界为 false。
        /// **不改变 Success**：质量不达标不等于算法执行失败。
        /// </summary>
        public OutputPort<bool> IsUsable { get; } = new(
            "IsUsable", description: "阈值判定结论（默认 true；只有启用阈值才可能 false）。不改变 Success");

        /// <summary>阈值判定原因；未启用阈值时写 metrics-only。</summary>
        public OutputPort<string> Reason { get; } = new(
            "Reason", description: "阈值判定原因；未启用阈值时为 metrics-only");

        /// <summary>逐项明细（原始值 / 分数 / 直方图统计 / 跳过原因）。</summary>
        public OutputPort<string[]> Details { get; } = new(
            "Details", description: "逐项明细：原始值、分数、P5/P95、截断比例、跳过原因");

        // ── 步骤参数：归一化参考值 ────────────────────────────────────────────

        /// <summary>对焦参考值：FocusRaw 达到该值时对焦分记 1。按配方良品批次标定。</summary>
        [StepConfig, DefaultValue(200.0)]
        [Display(Name = "对焦参考值", Description = "FocusRaw 达到该值时对焦分记 1；按良品批次标定，换镜头/光源要重标")]
        public partial double FocusRef { get; set; }

        /// <summary>对比度参考值：ContrastRaw（0~255）达到该值时对比度分记 1。</summary>
        [StepConfig, DefaultValue(128.0)]
        [Display(Name = "对比度参考值", Description = "ContrastRaw（P95−P5，0~255）达到该值时对比度分记 1")]
        public partial double ContrastRef { get; set; }

        /// <summary>饱和度参考值：SaturationRaw（0~255）达到该值时饱和度分记 1。</summary>
        [StepConfig, DefaultValue(128.0)]
        [Display(Name = "饱和度参考值", Description = "SaturationRaw（HSV S 均值，0~255）达到该值时饱和度分记 1")]
        public partial double SaturationRef { get; set; }

        /// <summary>可接受的截断总占比（%）：达到该值时曝光分记 0。</summary>
        [StepConfig, DefaultValue(5.0)]
        [Display(Name = "最大截断比例(%)", Description = "截断总占比达到该值时曝光分记 0；默认 5%")]
        public partial double MaxTruncationPercent { get; set; }

        /// <summary>ROI 最小有效像素数：低于该值直接失败，避免用几个像素算指标。</summary>
        [StepConfig, DefaultValue(16)]
        [Display(Name = "ROI 最小像素数", Description = "有效像素低于该值时直接失败，避免小样本指标不可信")]
        public partial int MinRoiPixels { get; set; }

        // ── 步骤参数：可选阈值（默认关闭） ────────────────────────────────────

        /// <summary>是否启用阈值判定。关闭时只输出指标，IsUsable 恒 true。</summary>
        [StepConfig, DefaultValue(false)]
        [Display(Name = "启用阈值判定", Description = "关闭时只输出指标（metrics-only）；打开后按下面四组 Min/Max 判 IsUsable")]
        public partial bool EnableThreshold { get; set; }

        [StepConfig, DefaultValue(0.0)]
        [Display(Name = "对焦下限", Description = "FocusScore 下限（0~1）；仅启用阈值时生效")]
        public partial double FocusMin { get; set; }

        [StepConfig, DefaultValue(1.0)]
        [Display(Name = "对焦上限", Description = "FocusScore 上限（0~1）；仅启用阈值时生效")]
        public partial double FocusMax { get; set; }

        [StepConfig, DefaultValue(0.0)]
        [Display(Name = "曝光下限", Description = "ExposureScore 下限（0~1）；仅启用阈值时生效")]
        public partial double ExposureMin { get; set; }

        [StepConfig, DefaultValue(1.0)]
        [Display(Name = "曝光上限", Description = "ExposureScore 上限（0~1）；仅启用阈值时生效")]
        public partial double ExposureMax { get; set; }

        [StepConfig, DefaultValue(0.0)]
        [Display(Name = "对比度下限", Description = "ContrastScore 下限（0~1）；仅启用阈值时生效")]
        public partial double ContrastMin { get; set; }

        [StepConfig, DefaultValue(1.0)]
        [Display(Name = "对比度上限", Description = "ContrastScore 上限（0~1）；仅启用阈值时生效")]
        public partial double ContrastMax { get; set; }

        [StepConfig, DefaultValue(0.0)]
        [Display(Name = "饱和度下限", Description = "SaturationScore 下限（0~1）；灰度图跳过此项")]
        public partial double SaturationMin { get; set; }

        [StepConfig, DefaultValue(1.0)]
        [Display(Name = "饱和度上限", Description = "SaturationScore 上限（0~1）；灰度图跳过此项")]
        public partial double SaturationMax { get; set; }

        // ── 参数夹取 ───────────────────────────────────────────────────────────

        partial void OnFocusRefChanging(ref double value) => value = Math.Max(0.01, value);

        partial void OnContrastRefChanging(ref double value) => value = Math.Max(0.01, value);

        partial void OnSaturationRefChanging(ref double value) => value = Math.Max(0.01, value);

        partial void OnMaxTruncationPercentChanging(ref double value) => value = Math.Max(0.001, value);

        partial void OnMinRoiPixelsChanging(ref int value) => value = Math.Max(1, value);

        partial void OnFocusMinChanging(ref double value) => value = Math.Clamp(value, 0.0, 1.0);

        partial void OnFocusMaxChanging(ref double value) => value = Math.Clamp(value, 0.0, 1.0);

        partial void OnExposureMinChanging(ref double value) => value = Math.Clamp(value, 0.0, 1.0);

        partial void OnExposureMaxChanging(ref double value) => value = Math.Clamp(value, 0.0, 1.0);

        partial void OnContrastMinChanging(ref double value) => value = Math.Clamp(value, 0.0, 1.0);

        partial void OnContrastMaxChanging(ref double value) => value = Math.Clamp(value, 0.0, 1.0);

        partial void OnSaturationMinChanging(ref double value) => value = Math.Clamp(value, 0.0, 1.0);

        partial void OnSaturationMaxChanging(ref double value) => value = Math.Clamp(value, 0.0, 1.0);

        // ── 算法 ───────────────────────────────────────────────────────────────

        /// <inheritdoc />
        public override void RunAlgorithm(IExecutionContext context)
        {
            HImage? image = Image.ActualValue;
            if (image is null || !image.IsInitialized())
            {
                Fail("输入图像未连接（Image 端口为空）。请接「采图」或「读图」步骤的输出。");
                return;
            }

            HRegion? roi = Roi.ActualValue;
            HObject? roiObject = roi is { } r && r.IsInitialized() ? r : null;

            // 配置错误先拦：Min > Max 是填错了，不是图的问题；直接红，别算完再给一个看不懂的 false。
            if (EnableThreshold)
            {
                var invalid = new List<string>();
                if (FocusMin > FocusMax) invalid.Add("对焦");
                if (ExposureMin > ExposureMax) invalid.Add("曝光");
                if (ContrastMin > ContrastMax) invalid.Add("对比度");
                if (SaturationMin > SaturationMax) invalid.Add("饱和度");
                if (invalid.Count > 0)
                {
                    Fail($"阈值区间非法（Min > Max）：{string.Join("、", invalid)}。请修正参数后再运行。");
                    return;
                }
            }

            ImageQualityService.QualityMetrics metrics;
            try
            {
                metrics = ImageQualityService.Measure(
                    image, roiObject,
                    FocusRef, ContrastRef, SaturationRef, MaxTruncationPercent, MinRoiPixels);
            }
            catch (InvalidOperationException ex)
            {
                Fail(ex.Message);
                return;
            }

            // 先填原始值与分数：即使阈值判 false，现场也能拿到实测值。
            FocusRaw.Value = metrics.FocusRaw;
            ExposureRaw.Value = metrics.ExposureRaw;
            ContrastRaw.Value = metrics.ContrastRaw;
            SaturationRaw.Value = metrics.SaturationRaw;

            FocusScore.Value = metrics.FocusScore;
            ExposureScore.Value = metrics.ExposureScore;
            ContrastScore.Value = metrics.ContrastScore;
            SaturationScore.Value = metrics.SaturationScore;

            HasColor.Value = metrics.HasColor;

            var reasons = new List<string>();
            if (EnableThreshold)
            {
                CheckMetric("对焦", metrics.FocusScore, FocusMin, FocusMax, reasons);
                CheckMetric("曝光", metrics.ExposureScore, ExposureMin, ExposureMax, reasons);
                CheckMetric("对比度", metrics.ContrastScore, ContrastMin, ContrastMax, reasons);
                if (metrics.HasColor)
                {
                    CheckMetric("饱和度", metrics.SaturationScore, SaturationMin, SaturationMax, reasons);
                }
                else
                {
                    metrics.Details.Add("饱和度阈值: 已跳过（灰度图）");
                }
                metrics.Details.Add(DescribeThresholds());
            }
            else
            {
                metrics.Details.Add("阈值: 未启用（metrics-only；IsUsable 恒 true）");
            }

            bool usable = reasons.Count == 0;
            IsUsable.Value = usable;
            Reason.Value = EnableThreshold
                ? (usable ? "全部启用项在阈值内" : string.Join("；", reasons))
                : "未启用阈值判定（metrics-only）";

            if (!usable)
            {
                metrics.Details.Add("判定: " + string.Join("；", reasons));
            }

            Details.Value = metrics.Details.ToArray();

            // 注意：这里刻意不调 Fail。质量分不达标是"图不好"，不是"算法执行失败"；
            // 拦截由下游「逻辑判断」读 IsUsable，或「判定表」按配方阈值判。
        }

        private static void CheckMetric(
            string name, double value, double min, double max, List<string> reasons)
        {
            if (value < min - 1e-9 || value > max + 1e-9)
            {
                reasons.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0} {1:F3} 超出 [{2:F3}, {3:F3}]", name, value, min, max));
            }
        }

        private string DescribeThresholds() => string.Format(CultureInfo.InvariantCulture,
            "阈值: 启用；对焦[{0:F2},{1:F2}] 曝光[{2:F2},{3:F2}] 对比度[{4:F2},{5:F2}] 饱和度[{6:F2},{7:F2}]",
            FocusMin, FocusMax, ExposureMin, ExposureMax,
            ContrastMin, ContrastMax, SaturationMin, SaturationMax);
    }
}
