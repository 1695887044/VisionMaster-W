using System;
using System.Linq;
using Core.Interfaces;
using HalconDotNet;
using Plugin.ImageDiff;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 图像差分（Plugin.ImageDiff）的断言。
    ///
    /// 为什么要补这一份：该插件 2026-10-23 新建，全套能力此前只有"编译通过 + 目视上屏"，
    /// 冒烟套件里搜 "ImageDiff" 零匹配——等于又一个长期没有自动化守护的算子。
    /// 插件自己的 `Help/12-dev-invariants.md` §E 已列出建议断言与反向判据，本文件按那份清单实现。
    ///
    /// 真值策略（与 BlobDetectChecks 同源）：**不依赖任何外部样图**，全部用 HALCON 现场合成
    /// 确定性图像（背景灰度恒定 + 已知位置的灰度块），期望值可以笔算，换机器不会漂移。
    /// 断言的是"算术口径、极性取舍、real 域防截断、连通域拆分与面积筛选、判定上限、守卫与端口契约"，
    /// 不是"某张照片上恰好检出 N 片"这种会随调参变化的东西。
    ///
    /// 反向判据（R38）：A1 一定会因为"把 real 域那段删掉"而变红——这是本文件存在的意义。
    /// 若哪天有人为省事把 ComputeDifference 改回 byte 域直接相减，A1 的强度值会变而不是 170。
    /// </summary>
    internal static class ImageDiffCheck
    {
        // 合成图口径：200×200，基准背景 30，被测图在 rows 20..49 / cols 20..49 有一块灰度 200 的方形差异
        private const int ImgW = 200;
        private const int ImgH = 200;
        private const int RefGray = 30;
        private const int TestBgGray = 30;
        private const int DiffGray = 200;
        private const int DiffRow1 = 20, DiffCol1 = 20, DiffRow2 = 49, DiffCol2 = 49;
        /// <summary>上面那块方形的像素数（30×30）= 差异总面积期望值</summary>
        private const int DiffSquarePixels = (DiffRow2 - DiffRow1 + 1) * (DiffCol2 - DiffCol1 + 1);

        public static void Run()
        {
            Section("[Diff] 图像差分插件（合成确定性图像验证）");

            if (!ProbeHalconUsable())
                return;

            RunCoreDifference();
            RunPolarity();
            RunSizeGuardAndPolicies();
            RunJudgementLimits();
            RunMaskAndAggregation();
            RunPortAndConfigContract();
        }

        // ==================================================================
        //  [A1][A2][A3] 核心差分：real 域防截断 / 绝对差口径 / 面积与片数
        // ==================================================================

        private static void RunCoreDifference()
        {
            Section("[Diff] A1~A3 核心差分（real 域 / 绝对差 / 面积与片数）");

            using HImage reference = MakeUniform(RefGray);
            using HImage tube = MakeWithSquare();
            var plugin = NewPlugin();
            try
            {
                plugin.Reference.Value = reference;
                plugin.Test.Value = tube;
                plugin.Algorithm = DiffAlgorithm.Absolute;
                plugin.Polarity = DiffPolarity.Both;
                plugin.Threshold = 30;
                plugin.MinArea = 20;
                plugin.DisplayViewIndex = 0;   // 断言里不开窗：离屏窗口在本宿主不渲染，开窗只增加耗时与假红面
                plugin.Execute(NewContext());

                // ---- A1：real 域防截断（本文件的反向判据锚点） ----
                // 差异强度必须正好是 |200 − 30| = 170。byte 域直接相减拿不到这个可笔算的值。
                double maxValue = plugin.MaxDiffValue.Value is double mv ? mv : -1;
                Check("[Diff][A1] 差异强度 = |200 − 30| = 170（先抬到 real 域再相减，byte 直接减会饱和）",
                    Math.Abs(maxValue - (DiffGray - RefGray)) < 0.51,
                    $"MaxDiffValue={maxValue:0.##}（期望 {DiffGray - RefGray}）");

                // ---- A2：面积与片数 ----
                double area = plugin.DiffArea.Value is double a ? a : -1;
                int count = plugin.DiffCount.Value is int c ? c : -1;
                Check("[Diff][A2] 差异总面积 = 30×30 = 900 px",
                    Math.Abs(area - DiffSquarePixels) <= 1.0,
                    $"DiffArea={area:0.##}（期望 {DiffSquarePixels}）");
                Check("[Diff][A2] 差异片数 = 1（单块差异只算一片）",
                    count == 1,
                    $"DiffCount={count}（期望 1）");

                double largest = plugin.MaxDiffComponentArea.Value is double m ? m : -1;
                Check("[Diff][A2b] 最大单片面积 = 总面积（只有一片）",
                    Math.Abs(largest - area) <= 1.0,
                    $"MaxDiffComponentArea={largest:0.##} vs DiffArea={area:0.##}");

                // ---- A3：DiffImage 是张真图，灰度范围合法 ----
                if (plugin.DiffImage.Value is HImage diffImage)
                {
                    HOperatorSet.MinMaxGray(diffImage, diffImage, 0.0,
                        out HTuple dMin, out HTuple dMax, out HTuple _);
                    Check("[Diff][A3] DiffImage 有图、取值范围落在 [0,255] 且峰值 = 170",
                        dMin.Length > 0 && dMax.Length > 0
                        && dMin.D >= -0.01 && dMax.D <= 255.01
                        && Math.Abs(dMax.D - (DiffGray - RefGray)) < 0.51,
                        $"min={dMin.D:0.##} max={dMax.D:0.##}");
                }
                else
                {
                    Check("[Diff][A3] DiffImage 有图、灰度峰值 = 170", false, "DiffImage 为 null");
                }

                Check("[Diff][A3b] OverlayImage 有图（底图副本 + 差异区着色，供上屏）",
                    plugin.OverlayImage.Value is HImage,
                    plugin.OverlayImage.Value == null ? "为 null" : "ok");
            }
            catch (Exception ex)
            {
                Check("[Diff] A1~A3 核心差分执行不抛异常", false, $"{ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                plugin.Dispose();
            }
        }

        // ==================================================================
        //  [A4] 极性：变亮 / 变暗 的取舍必须真的不同
        // ==================================================================

        private static void RunPolarity()
        {
            Section("[Diff] A4 极性取舍（变亮 / 变暗 / 双侧）");

            using HImage reference = MakeUniform(RefGray);
            using HImage brighter = MakeWithSquare();                        // 被测多一块亮块
            using HImage darker = MakeUniform(TestBgGray - RefGray);         // 被测整体更暗（0）
            try
            {
                double brighterOnly = RunCore(reference, brighter, DiffPolarity.Brighter, out _);
                double darkerOnly = RunCore(reference, brighter, DiffPolarity.Darker, out _);
                Check("[Diff][A4a] 被测更亮时：只算变亮 → 检出；只算变暗 → 零差异",
                    brighterOnly > 100 && darkerOnly < 0.51,
                    $"变亮区={brighterOnly:0.##} 变暗区={darkerOnly:0.##}");

                double onDarker = RunCore(reference, darker, DiffPolarity.Darker, out double maxVal);
                Check("[Diff][A4b] 被测整体更暗时：只算变暗 → 检出，强度 = |30 − 0| = 30",
                    onDarker > 10000 && Math.Abs(maxVal - RefGray) < 0.51,
                    $"变暗区={onDarker:0.##} 峰值={maxVal:0.##}（期望 {RefGray}）");

                double both = RunCore(reference, darker, DiffPolarity.Both, out _);
                Check("[Diff][A4c] 双侧口径与单侧等价（只有一种极性时）",
                    Math.Abs(both - onDarker) <= 1.0,
                    $"Both={both:0.##} Darker={onDarker:0.##}");
            }
            catch (Exception ex)
            {
                Check("[Diff] A4 极性执行不抛异常", false, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        // ==================================================================
        //  [C1][C2] 尺寸守卫：默认硬失败，非默认才缩放/裁剪
        // ==================================================================

        private static void RunSizeGuardAndPolicies()
        {
            Section("[Diff] C1/C2 尺寸失配守卫（默认硬失败）");

            using HImage reference = MakeUniform(RefGray);
            try
            {
                // ---- C1：默认 Fail → 明确失败且不产出 ----
                using HImage mismatched = MakeUniform(TestBgGray, 120, 100);
                var plugin = NewPlugin();
                try
                {
                    plugin.Reference.Value = reference;
                    plugin.Test.Value = mismatched;
                    plugin.SizeMismatch = SizeMismatchPolicy.Fail;
                    plugin.DisplayViewIndex = 0;
                    plugin.Execute(NewContext());

                    string msg = plugin.ErrorMessage.Value?.ToString() ?? "";
                    Check("[Diff][C1] 尺寸失配 + 策略=硬失败 → Success=false，文案说清两个尺寸",
                        plugin.Success.Value is false
                        && msg.Contains("200") && msg.Contains("120") && msg.Contains("不一致"),
                        $"Success={plugin.Success.Value} 文案={msg}");
                    // 注意：OutputPort<double>.Value 默认是 0.0，不是 null。
                    // 守卫在算之前 return，两个端口从没被写过 → 只能是初值。
                    double areaOnFail = plugin.DiffArea.Value is double failedArea ? failedArea : -1;
                    Check("[Diff][C1b] 守卫在算之前：失败时没有差异图、面积为初值 0",
                        plugin.DiffImage.Value == null && areaOnFail < 0.001,
                        $"DiffImage={(plugin.DiffImage.Value == null ? "null" : "有值")} DiffArea={areaOnFail:0.##}");
                }
                finally
                {
                    plugin.Dispose();
                }

                // ---- C2：ScaleTest → 成功 ----
                using (HImage small = MakeUniform(DiffGray, 120, 100))
                {
                    var scaled = NewPlugin();
                    try
                    {
                        scaled.Reference.Value = reference;
                        scaled.Test.Value = small;
                        scaled.SizeMismatch = SizeMismatchPolicy.ScaleTest;
                        scaled.Threshold = 30;
                        scaled.DisplayViewIndex = 0;
                        scaled.Execute(NewContext());

                        double area = scaled.DiffArea.Value is double a ? a : -1;
                        Check("[Diff][C2] 策略=缩放被测图 → 成功且差异面积非零",
                            scaled.Success.Value is true && area > 100,
                            $"Success={scaled.Success.Value} DiffArea={area:0.##} 文案={scaled.ErrorMessage.Value}");
                    }
                    finally
                    {
                        scaled.Dispose();
                    }
                }

                // ---- C2b：CropCommon → 成功 ----
                using (HImage small = MakeUniform(DiffGray, 120, 100))
                {
                    var cropped = NewPlugin();
                    try
                    {
                        cropped.Reference.Value = reference;
                        cropped.Test.Value = small;
                        cropped.SizeMismatch = SizeMismatchPolicy.CropCommon;
                        cropped.Threshold = 30;
                        cropped.DisplayViewIndex = 0;
                        cropped.Execute(NewContext());

                        Check("[Diff][C2b] 策略=裁公共区 → 成功（只看左上 120×100）",
                            cropped.Success.Value is true,
                            $"Success={cropped.Success.Value} 文案={cropped.ErrorMessage.Value}");
                    }
                    finally
                    {
                        cropped.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Check("[Diff] C1/C2 尺寸守卫执行不抛异常", false, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        // ==================================================================
        //  [C3][C4][C5] 判定上限
        // ==================================================================

        private static void RunJudgementLimits()
        {
            Section("[Diff] C3~C5 判定上限（总面积 / 最大单片 / 最大强度）");

            using HImage reference = MakeUniform(RefGray);
            using HImage tube = MakeWithSquare();
            try
            {
                // 基线：三条上限都不设 → 不判 NG。
                // 语义提醒：HasDiff 是"判定结论（是否 NG）"，不是"有没有差异像素"——
                // 三个上限全为 0 时 reasons 为空 → HasDiff=false → 不调 Fail → Success=true。
                // 有差异这件事由 DiffArea/DiffCount 表达（900 px / 1 片）。
                var baseline = NewPlugin();
                try
                {
                    baseline.Reference.Value = reference;
                    baseline.Test.Value = tube;
                    baseline.Threshold = 30;
                    baseline.MinArea = 20;
                    baseline.DisplayViewIndex = 0;
                    baseline.Execute(NewContext());

                    double baselineArea = baseline.DiffArea.Value is double ba ? ba : -1;
                    Check("[Diff][C3a] 不设上限 → 不判 NG（Success=true、HasDiff=false），但差异本身照实产出",
                        baseline.Success.Value is true
                        && baseline.HasDiff.Value is false
                        && baseline.DiffCount.Value is int bc && bc == 1
                        && Math.Abs(baselineArea - DiffSquarePixels) <= 1.0,
                        $"Success={baseline.Success.Value} HasDiff={baseline.HasDiff.Value} count={baseline.DiffCount.Value} area={baselineArea:0.##}");
                }
                finally
                {
                    baseline.Dispose();
                }

                CheckLimit("C3", "总面积上限 100 < 实测 900 → 判 NG", p => p.MaxTotalArea = 100);
                CheckLimit("C4", "最大单片上限 100 < 实测 900 → 判 NG", p => p.MaxComponentArea = 100);
                CheckLimit("C5", "最大强度上限 100 < 实测 170 → 判 NG（强度不受 Threshold 影响）", p => p.MaxValueLimit = 100);
            }
            catch (Exception ex)
            {
                Check("[Diff] C3~C5 判定上限执行不抛异常", false, $"{ex.GetType().Name}: {ex.Message}");
            }

            void CheckLimit(string id, string title, Action<ImageDiffPlugin> configure)
            {
                var plugin = NewPlugin();
                try
                {
                    plugin.Reference.Value = reference;
                    plugin.Test.Value = tube;
                    plugin.Threshold = 30;
                    plugin.MinArea = 20;
                    plugin.DisplayViewIndex = 0;
                    configure(plugin);
                    plugin.Execute(NewContext());

                    Check($"[Diff][{id}] {title}",
                        plugin.Success.Value is false
                        && plugin.HasDiff.Value is true
                        && (plugin.ErrorMessage.Value?.ToString() ?? "").Contains("NG"),
                        $"Success={plugin.Success.Value} HasDiff={plugin.HasDiff.Value} 文案={plugin.ErrorMessage.Value}");
                }
                finally
                {
                    plugin.Dispose();
                }
            }
        }

        // ==================================================================
        //  [B2][B3] 掩膜与聚合口径
        // ==================================================================

        private static void RunMaskAndAggregation()
        {
            Section("[Diff] B2/B3 掩膜屏蔽与聚合口径");

            using HImage reference = MakeUniform(RefGray);
            try
            {
                // ---- B2：掩膜只框住空白区、差异块落在框外 → 零差异 ----
                // MaskRegion 的语义是"只算框内"（差异区与它求交），不是"排除框内"。
                // 所以要让掩膜真的把差异裁掉，必须把框放在差异块之外；
                // 框盖在差异块上时面积不变，那是 B2c 的反证。
                using (HImage full = MakeWithSquare())
                {
                    var masked = NewPlugin();
                    try
                    {
                        using HRegion mask = MakeMaskRegion(100, 100, ImgH - 1, ImgW - 1);
                        masked.Reference.Value = reference;
                        masked.Test.Value = full;
                        masked.MaskRegion.Value = mask;
                        masked.Threshold = 30;
                        masked.MinArea = 20;
                        masked.DisplayViewIndex = 0;
                        masked.Execute(NewContext());

                        double area = masked.DiffArea.Value is double a ? a : -1;
                        int count = masked.DiffCount.Value is int c ? c : -1;
                        Check("[Diff][B2] 掩膜框住空白区、差异块在框外 → 总面积 0、片数 0",
                            masked.Success.Value is true && area < 0.51 && count == 0,
                            $"Success={masked.Success.Value} area={area:0.##} count={count}");
                    }
                    finally
                    {
                        masked.Dispose();
                    }
                }

                // ---- B2c：掩膜盖在差异块上 → 面积不变（证明是"求交"不是"排除"） ----
                using (HImage full = MakeWithSquare())
                {
                    var covered = NewPlugin();
                    try
                    {
                        using HRegion mask = MakeMaskRegion(DiffRow1 - 5, DiffCol1 - 5, DiffRow2 + 5, DiffCol2 + 5);
                        covered.Reference.Value = reference;
                        covered.Test.Value = full;
                        covered.MaskRegion.Value = mask;
                        covered.Threshold = 30;
                        covered.MinArea = 20;
                        covered.DisplayViewIndex = 0;
                        covered.Execute(NewContext());

                        double area = covered.DiffArea.Value is double a ? a : -1;
                        Check("[Diff][B2c] 掩膜盖在差异块上 → 面积不变（只算框内，不是排除框内）",
                            covered.Success.Value is true && Math.Abs(area - DiffSquarePixels) <= 1.0,
                            $"Success={covered.Success.Value} area={area:0.##} 期望={DiffSquarePixels}");
                    }
                    finally
                    {
                        covered.Dispose();
                    }
                }

                // ---- B2b：掩膜只盖下半 → 面积按未盖部分减少（证明交集逐像素生效） ----
                using (HImage full = MakeWithSquare())
                {
                    var half = NewPlugin();
                    try
                    {
                        using HRegion mask = MakeMaskRegion(35, DiffCol1 - 5, DiffRow2 + 5, DiffCol2 + 5);
                        half.Reference.Value = reference;
                        half.Test.Value = full;
                        half.MaskRegion.Value = mask;
                        half.Threshold = 30;
                        half.MinArea = 20;
                        half.DisplayViewIndex = 0;
                        half.Execute(NewContext());

                        double area = half.DiffArea.Value is double a ? a : -1;
                        int expected = 15 * (DiffCol2 - DiffCol1 + 1);   // 剩下 15 行 × 30 列
                        Check("[Diff][B2b] 掩膜只盖下半 → 面积 = 15 行 × 30 列（交集逐像素生效）",
                            Math.Abs(area - expected) <= 1.0,
                            $"area={area:0.##} 期望={expected}");
                    }
                    finally
                    {
                        half.Dispose();
                    }
                }

                // ---- B3：聚合=总面积 → 片数记 1 ----
                using (HImage full = MakeWithSquare())
                {
                    var total = NewPlugin();
                    try
                    {
                        total.Reference.Value = reference;
                        total.Test.Value = full;
                        total.Aggregation = DiffAggregation.Total;
                        total.Threshold = 30;
                        total.MinArea = 20;
                        total.DisplayViewIndex = 0;
                        total.Execute(NewContext());

                        int count = total.DiffCount.Value is int c ? c : -1;
                        double area = total.DiffArea.Value is double a ? a : -1;
                        Check("[Diff][B3] 聚合=总面积口径 → 片数记 1、面积仍 900",
                            count == 1 && Math.Abs(area - DiffSquarePixels) <= 1.0,
                            $"count={count} area={area:0.##}");
                    }
                    finally
                    {
                        total.Dispose();
                    }
                }

                // ---- B3b：两块分离差异 → 2 片 ----
                using (HImage two = MakeTwoSquares())
                {
                    var comp = NewPlugin();
                    try
                    {
                        comp.Reference.Value = reference;
                        comp.Test.Value = two;
                        comp.Aggregation = DiffAggregation.Components;
                        comp.Threshold = 30;
                        comp.MinArea = 20;
                        comp.DisplayViewIndex = 0;
                        comp.Execute(NewContext());

                        int count = comp.DiffCount.Value is int c ? c : -1;
                        double area = comp.DiffArea.Value is double a ? a : -1;
                        Check("[Diff][B3b] 两块分离差异 → 片数 2、总面积 = 两倍单块（connection 真的拆开）",
                            count == 2 && Math.Abs(area - DiffSquarePixels * 2) <= 2.0,
                            $"count={count} area={area:0.##} 期望面积={DiffSquarePixels * 2}");
                    }
                    finally
                    {
                        comp.Dispose();
                    }
                }

                // ---- B3c：面积下限筛掉小块（证明按面积筛，不是形态学腐蚀） ----
                using (HImage two = MakeTwoSquares(secondIsSmall: true))
                {
                    var filtered = NewPlugin();
                    try
                    {
                        filtered.Reference.Value = reference;
                        filtered.Test.Value = two;
                        filtered.Aggregation = DiffAggregation.Components;
                        filtered.Threshold = 30;
                        filtered.MinArea = 400;         // 落在 30×30=900 与 8×8=64 之间
                        filtered.DisplayViewIndex = 0;
                        filtered.Execute(NewContext());

                        int count = filtered.DiffCount.Value is int c ? c : -1;
                        double area = filtered.DiffArea.Value is double a ? a : -1;
                        Check("[Diff][B3c] 面积下限 400 筛掉 8×8 小块 → 片数 1、面积 900",
                            count == 1 && Math.Abs(area - DiffSquarePixels) <= 1.0,
                            $"count={count} area={area:0.##}");
                    }
                    finally
                    {
                        filtered.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Check("[Diff] B2/B3 掩膜与聚合执行不抛异常", false, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        // ==================================================================
        //  [D] 端口与配置契约
        // ==================================================================

        private static void RunPortAndConfigContract()
        {
            Section("[Diff] D1~D6 端口与配置契约");

            var plugin = NewPlugin();
            try
            {
                Check("[Diff][D1] 输入端口：Reference/Test 必填、MaskRegion 可选，类型各自正确",
                    plugin.Reference.IsRequired && plugin.Test.IsRequired && !plugin.MaskRegion.IsRequired
                    && plugin.Reference.DataType == typeof(HImage)
                    && plugin.Test.DataType == typeof(HImage)
                    && plugin.MaskRegion.DataType == typeof(HRegion),
                    $"Ref必填={plugin.Reference.IsRequired} Test必填={plugin.Test.IsRequired} Mask必填={plugin.MaskRegion.IsRequired}");

                Check("[Diff][D1b] 输出端口类型：图 / 片数 int / 面积 double / 强度 double / 判定 bool",
                    plugin.DiffImage.DataType == typeof(HImage)
                    && plugin.OverlayImage.DataType == typeof(HImage)
                    && plugin.DiffCount.DataType == typeof(int)
                    && plugin.DiffArea.DataType == typeof(double)
                    && plugin.MaxDiffComponentArea.DataType == typeof(double)
                    && plugin.MaxDiffValue.DataType == typeof(double)
                    && plugin.HasDiff.DataType == typeof(bool),
                    "逐项核对");

                Check("[Diff][D2] 默认值：绝对差 / 双侧 / 连通域 / 硬失败 / 阈值 30 / 面积下限 20",
                    plugin.Algorithm == DiffAlgorithm.Absolute
                    && plugin.Polarity == DiffPolarity.Both
                    && plugin.Aggregation == DiffAggregation.Components
                    && plugin.SizeMismatch == SizeMismatchPolicy.Fail
                    && Math.Abs(plugin.Threshold - 30) < 1e-9
                    && Math.Abs(plugin.MinArea - 20) < 1e-9,
                    $"算法={plugin.Algorithm} 极性={plugin.Polarity} 聚合={plugin.Aggregation} 策略={plugin.SizeMismatch} 阈值={plugin.Threshold} 下限={plugin.MinArea}");

                var display = typeof(ImageDiffPlugin)
                    .GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.DisplayAttribute), false)
                    .OfType<System.ComponentModel.DataAnnotations.DisplayAttribute>()
                    .FirstOrDefault();
                Check("[Diff][D3] [Display] 名称=图像差分、分组=图像处理",
                    display?.Name == "图像差分" && display?.GroupName == "图像处理",
                    $"Name={display?.Name} GroupName={display?.GroupName}");

                // ---- D4：输入缺失 → 明确失败 ----
                plugin.DisplayViewIndex = 0;
                plugin.Execute(NewContext());
                string msg = plugin.ErrorMessage.Value?.ToString() ?? "";
                Check("[Diff][D4] 两张图都没接 → Success=false 且文案指出端口（不抛异常）",
                    plugin.Success.Value is false && msg.Contains("Reference"),
                    $"Success={plugin.Success.Value} 文案={msg}");

                // ---- D5：夹取钩子 ----
                plugin.Threshold = 999;
                plugin.DisplayViewIndex = 99;
                plugin.AmbientFloor = -5;
                Check("[Diff][D5] 参数夹取：Threshold→255、DisplayViewIndex→9、AmbientFloor→0",
                    Math.Abs(plugin.Threshold - 255) < 1e-9
                    && plugin.DisplayViewIndex == 9
                    && Math.Abs(plugin.AmbientFloor) < 1e-9,
                    $"Threshold={plugin.Threshold} DisplayViewIndex={plugin.DisplayViewIndex} AmbientFloor={plugin.AmbientFloor}");

                // ---- D6：手册内嵌（R41 的运行时旁证） ----
                var helpResources = typeof(ImageDiffPlugin).Assembly.GetManifestResourceNames()
                    .Where(n => n.Contains(".Help.", StringComparison.Ordinal))
                    .ToArray();
                Check("[Diff][D6] 程序集内嵌 Help 手册 8 页（R41/R42）",
                    helpResources.Length == 8,
                    $"资源数={helpResources.Length}");
            }
            finally
            {
                plugin.Dispose();
            }
        }

        // ==================================================================
        //  工具
        // ==================================================================

        /// <summary>HALCON 原生库可用性：不可用则整段跳过（并说明理由，R38 要求跳过必须能被反证）</summary>
        private static bool ProbeHalconUsable()
        {
            try
            {
                using HImage probe = MakeUniform(1, 4, 4);
                HOperatorSet.MinMaxGray(probe, probe, 0.0, out HTuple min, out HTuple _, out HTuple _);
                Check("[Diff] HALCON 原生库可用（合成图可造、灰度可读）",
                    min.Length > 0 && Math.Abs(min.D - 1) < 1e-9,
                    $"min={min.D}");
                return true;
            }
            catch (Exception ex)
            {
                Check("[Diff] HALCON 原生库可用", false, $"{ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private static ImageDiffPlugin NewPlugin() => new() { InstanceName = "图像差分_断言" };

        private static ExecutionContext NewContext() => new(
            new StubLog(), new FlowSession { FlowName = "图像差分断言" },
            new WorkspaceContext(), new CancellationTokenSource().Token);

        /// <summary>跑一遍核心差分，返回差异总面积（maxValue 输出强度峰）</summary>
        private static double RunCore(HImage reference, HImage test, DiffPolarity polarity, out double maxValue)
        {
            var plugin = NewPlugin();
            try
            {
                plugin.Reference.Value = reference;
                plugin.Test.Value = test;
                plugin.Algorithm = DiffAlgorithm.Absolute;
                plugin.Polarity = polarity;
                plugin.Threshold = 30;
                plugin.MinArea = 20;
                plugin.DisplayViewIndex = 0;
                plugin.Execute(NewContext());

                maxValue = plugin.MaxDiffValue.Value is double v ? v : -1;
                return plugin.DiffArea.Value is double a ? a : -1;
            }
            finally
            {
                plugin.Dispose();
            }
        }

        /// <summary>纯色图</summary>
        private static HImage MakeUniform(int gray, int width = ImgW, int height = ImgH)
        {
            HOperatorSet.GenImageConst(out HObject proto, "byte", width, height);
            try
            {
                HOperatorSet.GenImageProto(proto, out HObject img, gray);
                return new HImage(img);
            }
            finally
            {
                proto.Dispose();
            }
        }

        /// <summary>背景 30，在 rows/cols 20..49 画一块 200 的方片</summary>
        private static HImage MakeWithSquare()
            => StampRect(MakeUniform(TestBgGray), DiffRow1, DiffCol1, DiffRow2, DiffCol2, DiffGray);

        /// <summary>两块分离的差异（第二块 8×8 可选，用于面积下限筛选）</summary>
        private static HImage MakeTwoSquares(bool secondIsSmall = false)
        {
            const int r1 = 120, c1 = 120;
            int size = secondIsSmall ? 8 : 30;
            using HImage one = StampRect(MakeUniform(TestBgGray), DiffRow1, DiffCol1, DiffRow2, DiffCol2, DiffGray);
            return StampRect(one, r1, c1, r1 + size - 1, c1 + size - 1, DiffGray);
        }

        /// <summary>
        /// 在图上盖一块矩形灰度。
        /// <para>
        /// 用 <c>paint_region</c> 而不是逐像素：它按 region 掩膜**就地**把第二张图的值并进第一张图
        /// （第一张图是原对象，改完仍然是原对象；越界自动裁剪）。
        /// </para>
        /// <para>
        /// 这里踩过一个坑：最初写成 <c>MaxImage(canvas, painted, out merged)</c> 再想办法写回
        /// <paramref name="canvas"/>，但 <c>max_image</c> 只在 <c>out</c> 里产生**新对象**，
        /// 而 <c>copy_image</c> 也只有 2 参、不写回目标——结果画布从头到尾没变，
        /// 断言里所有测试图都退化成纯色背景，表现为"差异面积恒 0"。直接就地 paint 最简单也最正确。
        /// </para>
        /// </summary>
        private static HImage StampRect(HImage canvas, int row1, int col1, int row2, int col2, int gray)
        {
            HOperatorSet.GetImageSize(canvas, out HTuple w, out HTuple h);
            int imgW = w.I, imgH = h.I;
            int r2 = Math.Min(row2, imgH - 1), c2 = Math.Min(col2, imgW - 1);
            if (row1 > r2 || col1 > c2)
            {
                // 空矩形：原样拷一份返回，保持"函数总是返回新对象"的契约。
                HOperatorSet.CopyImage(canvas, out HObject same);
                return new HImage(same);
            }

            HOperatorSet.GenRectangle1(out HObject rect, row1, col1, r2, c2);
            try
            {
                // 探针实测（D:\C#\VM-artifacts\probe）：paint_region 的 image 入参**不会被就地改写**，
                // 结果只在 out 参数里。所以这里必须 return 那个新对象，不能指望 canvas 被改。
                HOperatorSet.PaintRegion(rect, canvas, out HObject painted, gray, "fill");
                return new HImage(painted);
            }
            finally
            {
                rect.Dispose();
            }
        }

        /// <summary>矩形掩膜区域（作为 MaskRegion 端口值）</summary>
        private static HRegion MakeMaskRegion(int row1, int col1, int row2, int col2)
        {
            HOperatorSet.GenRectangle1(out HObject rect, row1, col1, row2, col2);
            return new HRegion(rect);
        }
    }
}
