using Core.Events;
using Core.Halcon.Models;
using Core.Interfaces;
using HalconDotNet;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Plugin.ImageDiff;

namespace Plugin.ImageDiff
{
    /// <summary>
    /// 图像差分：把「被测图」与「基准图」逐像元相减，找出不一样的地方。
    ///
    /// 为什么这是一个独立插件而不是 PreProcessing 的一个算子：
    /// PreProcessing 的链内算子（OperatorStepItem）只有 OperatorKey + Enabled + 字符串参数字典，
    /// **拿不到第二张图**，所以"图像 − 图像"在链里结构上做不出来；而逐像元差分的价值全在
    /// "两张图对齐后比较"上，故单独成步骤，两个输入端口各接一路图。
    ///
    /// 口径纪律（与 Plugin.CaliperMeasure / Plugin.BlobDetect 的 S1 守卫同一思路）：
    /// 两图尺寸/通道不一致**默认硬失败**，绝不静默缩放——静默缩放会得到一张
    /// "看起来正常、数值全错"的差分图，这是这类插件最难排查的坏法。
    /// </summary>
    [Display(Name = "图像差分", GroupName = "图像处理",
        Description = "把被测图与基准图逐像元相减，按阈值找出差异区域并输出差异面积/片数/最大差异值",
        ShortName = "\uf24e")]
    [ParallelSafe]
    public partial class ImageDiffPlugin : VisionPluginBase
    {
        // ── 输入端口 ───────────────────────────────────────────────────────────

        /// <summary>基准图（金样/上一批合格品）。缺失时直接失败，不猜。</summary>
        public InputPort<HImage> Reference { get; } = new(
            "Reference", description: "基准图（金样）。必接。")
        { IsRequired = true };

        /// <summary>被测图。</summary>
        public InputPort<HImage> Test { get; } = new(
            "Test", description: "被测图。必接。")
        { IsRequired = true };

        /// <summary>
        /// 忽略区：落在该区域内的差异不计入结果（工装压痕、丝印固定位置等）。
        /// 不接则全图参与比较。
        /// </summary>
        public InputPort<HRegion> MaskRegion { get; } = new(
            "MaskRegion", description: "忽略区（可选）：区内差异不计入结果")
        { IsRequired = false };

        // ── 输出端口 ───────────────────────────────────────────────────────────

        /// <summary>差异强度图（byte 单通道，0 = 无差异，255 = 最大差异）。</summary>
        public OutputPort<HImage> DiffImage { get; } = new("DiffImage", description: "差异强度图（0~255）");

        /// <summary>标注图（基准灰度 + 差异区着色 + 判定文字）。</summary>
        public OutputPort<HImage> OverlayImage { get; } = new("OverlayImage", description: "标注图（差异区着色 + 判定文字）");

        /// <summary>差异片数（连通域数；Total 口径下为 0/1）。</summary>
        public OutputPort<int> DiffCount { get; } = new("DiffCount", description: "差异片数");

        /// <summary>差异总面积（像素）。</summary>
        public OutputPort<double> DiffArea { get; } = new("DiffArea", description: "差异总面积（像素）");

        /// <summary>最大单片差异面积（像素）。</summary>
        public OutputPort<double> MaxDiffComponentArea { get; } = new("MaxDiffComponentArea", description: "最大单片差异面积（像素）");

        /// <summary>最大差异强度（0~255）。</summary>
        public OutputPort<double> MaxDiffValue { get; } = new("MaxDiffValue", description: "最大差异强度（0~255）");

        /// <summary>判定：差异是否超限（= Success 取反的语义化输出）。</summary>
        public OutputPort<bool> HasDiff { get; } = new("HasDiff", description: "是否判定为有差异（NG）");

        // ── 步骤参数 ───────────────────────────────────────────────────────────

        /// <summary>差分算法。换算法会改口径，必须重新调阈值。</summary>
        [StepConfig, DefaultValue(DiffAlgorithm.Absolute)]
        public partial DiffAlgorithm Algorithm { get; set; }

        /// <summary>极性：只算变亮、只算变暗，还是双侧都算。</summary>
        [StepConfig, DefaultValue(DiffPolarity.Both)]
        public partial DiffPolarity Polarity { get; set; }

        /// <summary>差异聚合方式。</summary>
        [StepConfig, DefaultValue(DiffAggregation.Components)]
        public partial DiffAggregation Aggregation { get; set; }

        /// <summary>两图尺寸/通道不一致时的处理。</summary>
        [StepConfig, DefaultValue(SizeMismatchPolicy.Fail)]
        public partial SizeMismatchPolicy SizeMismatch { get; set; }

        /// <summary>差异阈值（0~255）。差异强度高于此值的像元算"不一样"。</summary>
        [StepConfig, DefaultValue(30)]
        public partial double Threshold { get; set; }

        /// <summary>面积下限（像素）：单片小于此值的差异当作噪点丢弃。</summary>
        [StepConfig, DefaultValue(20)]
        public partial double MinArea { get; set; }

        /// <summary>总面积上限（像素）：超过即判 NG。0 = 不按总面积判。</summary>
        [StepConfig, DefaultValue(0.0)]
        public partial double MaxTotalArea { get; set; }

        /// <summary>单片面积上限（像素）：任一单片超过即判 NG。0 = 不按单片判。</summary>
        [StepConfig, DefaultValue(0.0)]
        public partial double MaxComponentArea { get; set; }

        /// <summary>最大差异强度上限（0~255）：超过即判 NG。0 = 不按强度判。</summary>
        [StepConfig, DefaultValue(0.0)]
        public partial double MaxValueLimit { get; set; }

        /// <summary>
        /// 相对差的环境亮度下限（仅 Algorithm=Relative 生效）。
        /// 分母 = max(参考图灰度, 本值)，防止参考图接近全黑处把噪声放大成全白。
        /// </summary>
        [StepConfig, DefaultValue(8.0)]
        public partial double AmbientFloor { get; set; }

        /// <summary>有符号差的显示零点（仅 Algorithm=Signed 生效，通常 128）。</summary>
        [StepConfig, DefaultValue(128.0)]
        public partial double SignedDisplay { get; set; }

        /// <summary>结果上屏的窗口序号（1~9），0 = 不上屏。</summary>
        [StepConfig, DefaultValue(1)]
        public partial int DisplayViewIndex { get; set; }

        // ── 参数夹取 ───────────────────────────────────────────────────────────
        // 用一个 partial 钩子集中夹：视图里填错数、老方案存了越界值都收在这里，
        // 比在每个分支里各写一遍难漏。

        partial void OnThresholdChanging(ref double value) => value = Math.Clamp(value, 0.0, 255.0);

        partial void OnMinAreaChanging(ref double value) => value = Math.Max(0.0, value);

        partial void OnMaxTotalAreaChanging(ref double value) => value = Math.Max(0.0, value);

        partial void OnMaxComponentAreaChanging(ref double value) => value = Math.Max(0.0, value);

        partial void OnMaxValueLimitChanging(ref double value) => value = Math.Clamp(value, 0.0, 255.0);

        partial void OnAmbientFloorChanging(ref double value) => value = Math.Max(0.0, value);

        partial void OnSignedDisplayChanging(ref double value) => value = Math.Clamp(value, 0.0, 255.0);

        partial void OnDisplayViewIndexChanging(ref int value) => value = Math.Clamp(value, 0, 9);

        // ── 算法 ───────────────────────────────────────────────────────────────

        /// <inheritdoc />
        public override void RunAlgorithm(IExecutionContext context)
        {
            // ① 取输入。Base 已经把 null 端口的描述写进 ErrorMessage，这里只要判。
            //    ★ 端口取值一律走 ActualValue（强类型）；Value 是"手动值"的弱类型入口，
            //      端口已链接上游时它可能是默认值 —— 见 InputPort.cs:225 的注释。
            HImage? reference = Reference.ActualValue;
            HImage? test = Test.ActualValue;

            if (reference is null || !reference.IsInitialized())
            {
                Fail("基准图未连接（Reference 端口为空）。差分必须有一个基准：请先接「采图」或「读图」步骤的图，或接基准库步骤。");
                return;
            }

            if (test is null || !test.IsInitialized())
            {
                Fail("被测图未连接（Test 端口为空）。");
                return;
            }

            var (refW, refH, refC) = ImageDiffService.Shape(reference);
            var (testW, testH, testC) = ImageDiffService.Shape(test);

            // ② 尺寸/通道守卫（默认硬失败）。判据放在这里而不是 service 里：
            //    这是"策略"（用户可选的），不是算法。
            string? mismatch = null;
            if (refW != testW || refH != testH)
            {
                mismatch = $"两图尺寸不一致：基准 {refW}×{refH}，被测 {testW}×{testH}";
            }

            if (mismatch is null && refC != testC)
            {
                // 通道数不同但尺寸一样：1 通道 vs 3 通道在 ToGray 后是同一域，
                // 可以放行；3 vs 2 这种罕见组合交 ToGray 抛异常。
                bool grayable = (refC == 1 && testC >= 3) || (testC == 1 && refC >= 3);
                if (!grayable)
                {
                    mismatch = $"两图通道数不一致：基准 {refC} 通道，被测 {testC} 通道";
                }
            }

            HImage? referenceAdjusted = null;
            HImage? testAdjusted = null;
            try
            {
                if (mismatch is not null)
                {
                    switch (SizeMismatch)
                    {
                        case SizeMismatchPolicy.Fail:
                            Fail($"{mismatch}。差分是逐像元运算，尺寸不一致必须先在前面把图对齐（用「模板匹配」做位姿对齐，或「标定」做几何校正）；本步骤不会替你静默缩放。");
                            return;

                        case SizeMismatchPolicy.ScaleTest:
                        {
                            // 缩放被测图到基准尺寸：只适用于"两图只差整体缩放"且能接受插值误差的场合。
                            HOperatorSet.ZoomImageSize(test, out HObject zoomed, refW, refH, "bilinear");
                            testAdjusted = new HImage(zoomed);
                            context.Logger?.Warn($"{InstanceName} {mismatch} → 已按 SizeMismatch=缩放被测图 处理（双线性插值，会引入插值误差）");
                            break;
                        }

                        case SizeMismatchPolicy.CropCommon:
                        {
                            int w = Math.Min(refW, testW);
                            int h = Math.Min(refH, testH);
                            if (w < 8 || h < 8)
                            {
                                Fail($"{mismatch}，公共区域只有 {w}×{h}，太小无法比较。");
                                return;
                            }
                            HOperatorSet.CropRectangle1(reference, out HObject rc, 0, 0, h - 1, w - 1);
                            referenceAdjusted = new HImage(rc);
                            HOperatorSet.CropRectangle1(test, out HObject tc, 0, 0, h - 1, w - 1);
                            testAdjusted = new HImage(tc);
                            context.Logger?.Warn($"{InstanceName} {mismatch} → 已按 SizeMismatch=裁到公共区域 处理（取 {w}×{h}，未做插值）");
                            break;
                        }
                    }
                }

                HImage refUse = referenceAdjusted ?? reference;
                HImage testUse = testAdjusted ?? test;

                // ③ 转灰度 → 差分。
                HImage? refGray = null;
                HImage? testGray = null;
                HImage? diff = null;
                HObject? diffRegions = null;
                try
                {
                    refGray = ImageDiffService.ToGray(refUse);
                    testGray = ImageDiffService.ToGray(testUse);

                    diff = ImageDiffService.ComputeDifference(
                        refGray, testGray, Algorithm, Polarity, AmbientFloor, SignedDisplay);

                    // ④ 分割 + 统计。
                    HRegion? maskPort = MaskRegion.ActualValue;
                    HObject? mask = maskPort is { } m && m.IsInitialized() ? m : null;

                    var seg = ImageDiffService.Segment(diff, Threshold, Aggregation, MinArea, mask);
                    diffRegions = seg.Region;

                    var range = ImageDiffService.Range(diff, null);

                    DiffImage.Value = diff;
                    DiffCount.Value = seg.Count;
                    DiffArea.Value = seg.TotalArea;
                    MaxDiffComponentArea.Value = seg.MaxComponentArea;
                    MaxDiffValue.Value = range.Max;

                    // ⑤ 判定：三个上限各自独立，任一超限即 NG。0 = 该项不参与判定。
                    var reasons = new List<string>();
                    if (MaxTotalArea > 0 && seg.TotalArea > MaxTotalArea)
                    {
                        reasons.Add($"总面积 {seg.TotalArea:F0} > 上限 {MaxTotalArea:F0}");
                    }
                    if (MaxComponentArea > 0 && seg.MaxComponentArea > MaxComponentArea)
                    {
                        reasons.Add($"最大单片 {seg.MaxComponentArea:F0} > 上限 {MaxComponentArea:F0}");
                    }
                    if (MaxValueLimit > 0 && range.Max > MaxValueLimit)
                    {
                        reasons.Add($"最大差异强度 {range.Max:F0} > 上限 {MaxValueLimit:F0}");
                    }

                    bool hasDiff = reasons.Count > 0;
                    HasDiff.Value = hasDiff;
                    if (hasDiff)
                    {
                        Fail($"图像差分判定 NG：{string.Join("；", reasons)}");
                    }

                    // ⑥ 标注图 + 上屏。
                    string title = hasDiff
                        ? $"NG  差异 {seg.TotalArea:F0} px / {seg.Count} 片"
                        : $"OK  差异 {seg.TotalArea:F0} px / {seg.Count} 片";
                    HImage overlay = ImageDiffService.RenderOverlay(refUse, diffRegions);
                    OverlayImage.Value = overlay;

                    if (DisplayViewIndex > 0)
                    {
                        // ★ 五参重载是 (image, viewIndex, title, info, annotations)，
                        //   没有 params 数组版本 —— 第四个坑位必须是 IEnumerable<ImageInfoRow>。
                        var info = new List<ImageInfoRow>
                        {
                            new("算法", AlgorithmText(Algorithm)),
                            new("阈值", Threshold.ToString("F0", CultureInfo.CurrentCulture)),
                            new("差异片数", seg.Count.ToString(CultureInfo.CurrentCulture)),
                            new("差异总面积", $"{seg.TotalArea:F0} px"),
                            new("最大单片", $"{seg.MaxComponentArea:F0} px"),
                            new("最大强度", range.Max.ToString("F0", CultureInfo.CurrentCulture)),
                        };
                        try { this.PublishPreview(overlay, DisplayViewIndex, title, info, null); }
                        catch { /* 显示层异常不拖垮流程（与 GeometryRelation/ResultDisplay 一致） */ }
                    }
                }
                finally
                {
                    refGray?.Dispose();
                    testGray?.Dispose();
                    // diff 已经交给 DiffImage 端口，由 AutoDisposeRoundOutputs 在下一轮释放；
                    // 这里不能再 Dispose，否则端口上的图会变成野指针。
                    diffRegions?.Dispose();
                }
            }
            finally
            {
                referenceAdjusted?.Dispose();
                testAdjusted?.Dispose();
            }
        }

        /// <summary>算法中文名（日志与上屏共用一份，避免两处文案走偏）。</summary>
        internal static string AlgorithmText(DiffAlgorithm algorithm) => algorithm switch
        {
            DiffAlgorithm.Absolute => "绝对差",
            DiffAlgorithm.Signed => "有符号差",
            DiffAlgorithm.Relative => "相对差",
            _ => algorithm.ToString(),
        };
    }
}
