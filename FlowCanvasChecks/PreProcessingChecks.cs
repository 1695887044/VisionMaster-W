using System;
using System.Collections.Generic;
using System.Linq;
using Core.Interfaces;
using HalconDotNet;
using Plugin.PreProcessing;
using Plugin.PreProcessing.Models;
using Plugin.PreProcessing.Operators;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 图像预处理（Plugin.PreProcessing）新增算子的断言。
    ///
    /// 本批补的是"预处理内部"能单图进/单图出的四类经典算子：
    ///   灰度顶帽、灰度黑帽、Sobel 边缘、Laplace 边缘。
    ///
    /// 真值策略：全部用 HALCON 现场合成的确定性图（恒定背景 + 已知位置/灰度的
    /// 亮斑、暗斑、阶跃边），期望值可笔算，不依赖任何外部样图。
    /// 覆盖四层：
    ///   ① 注册表口径（Key/显示名/分组/顺序/总数）—— 加算子忘了贴特性会在这里红；
    ///   ② 像素级结果（顶帽=原图−开、黑帽=闭−原图、边缘图在边缘高/平坦区低）；
    ///   ③ 参数收敛与存盘往返（偶数模板 +1、超界夹取、枚举按数值落盘）；
    ///   ④ 快照 → 活链 → 真执行（`Chain` 灌值后 `Operators` 能重建并跑通）。
    /// </summary>
    internal static class PreProcessingChecks
    {
        private const int ImgW = 100;
        private const int ImgH = 100;

        public static void Run()
        {
            Section("[PRE] 图像预处理：新增算子注册 / 像素口径 / 参数收敛 / 存盘往返");

            RunRegistryContract();
            RunMorphologyPixels();
            RunEdgePixels();
            RunNormalizeAndPersistence();
            RunChainEndToEnd();
        }

        // ==================================================================
        //  ① 注册表：Key / 显示名 / 分组 / 顺序 / 总数
        // ==================================================================
        private static void RunRegistryContract()
        {
            var all = OperatorRegistry.All;
            Check("算子总数 = 25（原 21 + 顶帽/黑帽/Sobel/Laplace）",
                all.Count == 25, $"Count={all.Count}");
            Check("算子 Key 无重复",
                all.Select(d => d.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() == all.Count,
                string.Join(",", all.GroupBy(d => d.Key, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key)));

            var topHat = OperatorRegistry.Find("gray_top_hat");
            var blackHat = OperatorRegistry.Find("gray_black_hat");
            var sobel = OperatorRegistry.Find("sobel_edge");
            var laplace = OperatorRegistry.Find("laplace_edge");

            Check("灰度顶帽：Key/显示名/分组/Order 正确",
                topHat != null && topHat.DisplayName == "灰度顶帽"
                && topHat.Category == "灰度形态学" && topHat.Order == 115,
                topHat == null ? "未注册" : $"{topHat.DisplayName}/{topHat.Category}/{topHat.Order}");
            Check("灰度黑帽：Key/显示名/分组/Order 正确",
                blackHat != null && blackHat.DisplayName == "灰度黑帽"
                && blackHat.Category == "灰度形态学" && blackHat.Order == 116,
                blackHat == null ? "未注册" : $"{blackHat.DisplayName}/{blackHat.Category}/{blackHat.Order}");
            Check("Sobel 边缘：Key/显示名/分组/Order 正确",
                sobel != null && sobel.DisplayName == "Sobel 边缘"
                && sobel.Category == "边缘检测" && sobel.Order == 180,
                sobel == null ? "未注册" : $"{sobel.DisplayName}/{sobel.Category}/{sobel.Order}");
            Check("Laplace 边缘：Key/显示名/分组/Order 正确",
                laplace != null && laplace.DisplayName == "Laplace 边缘"
                && laplace.Category == "边缘检测" && laplace.Order == 181,
                laplace == null ? "未注册" : $"{laplace.DisplayName}/{laplace.Category}/{laplace.Order}");

            var categories = OperatorRegistry.Categories.Select(c => c.Name).ToList();
            Check("分组顺序：七族按 Order 排，边缘检测在最后",
                categories.SequenceEqual(new[]
                {
                    "色彩与通道", "几何调整", "滤波", "灰度形态学", "图像增强", "二值化", "边缘检测"
                }),
                string.Join(" → ", categories));

            var morphology = OperatorRegistry.Categories.First(c => c.Name == "灰度形态学")
                .Items.Select(d => d.DisplayName).ToList();
            Check("灰度形态学组内顺序：膨胀→腐蚀→开→闭→顶帽→黑帽",
                morphology.SequenceEqual(new[]
                {
                    "灰度膨胀", "灰度腐蚀", "灰度开运算", "灰度闭运算", "灰度顶帽", "灰度黑帽"
                }),
                string.Join(" → ", morphology));

            var edge = OperatorRegistry.Categories.First(c => c.Name == "边缘检测")
                .Items.Select(d => d.DisplayName).ToList();
            Check("边缘检测组内顺序：Sobel→Laplace",
                edge.SequenceEqual(new[] { "Sobel 边缘", "Laplace 边缘" }),
                string.Join(" → ", edge));

            Check("原有算子未被误删（抽查 gray_opening / threshold / crop_box）",
                OperatorRegistry.Find("gray_opening") != null
                && OperatorRegistry.Find("threshold") != null
                && OperatorRegistry.Find("crop_box") != null,
                "");
            Check("不认识的 Key 仍返回 null（旧方案降级路径不回归）",
                OperatorRegistry.Create("no_such_operator") == null, "");
        }

        // ==================================================================
        //  ② 像素级：顶帽 / 黑帽
        // ==================================================================
        private static void RunMorphologyPixels()
        {
            // 亮斑 5×5（灰度 200）在 100 背景上；模板 15×15 远大于亮斑 → 开运算把亮斑清掉，
            // 顶帽 = 原图 − 开 = 亮斑处 100、平坦处 0。
            using (var brightSpot = MakeImage(100, (48, 48, 52, 52, 200)))
            {
                var op = new GrayTopHatOperator { Width = 15, Height = 15 };
                using var result = op.Apply(brightSpot);
                double center = PixelProbe.At(result, 50, 50);
                double flat = PixelProbe.At(result, 10, 10);
                Check("【像素】顶帽：亮斑中心 ≈ 100（200 − 开运算 100）",
                    Math.Abs(center - 100) <= 2, center.ToString("0.##"));
                Check("【像素】顶帽：平坦背景 ≈ 0（开运算没清掉背景）",
                    Math.Abs(flat) <= 2, flat.ToString("0.##"));
            }

            // 暗斑 5×5（灰度 100）在 200 背景上；模板 15×15 → 闭运算把暗斑填平，
            // 黑帽 = 闭 − 原图 = 暗斑处 100、平坦处 0。
            using (var darkSpot = MakeImage(200, (48, 48, 52, 52, 100)))
            {
                var op = new GrayBlackHatOperator { Width = 15, Height = 15 };
                using var result = op.Apply(darkSpot);
                double center = PixelProbe.At(result, 50, 50);
                double flat = PixelProbe.At(result, 10, 10);
                Check("【像素】黑帽：暗斑中心 ≈ 100（闭运算 200 − 100）",
                    Math.Abs(center - 100) <= 2, center.ToString("0.##"));
                Check("【像素】黑帽：平坦背景 ≈ 0（闭运算没改动背景）",
                    Math.Abs(flat) <= 2, flat.ToString("0.##"));
            }

            // 反证：模板小于亮斑时，开运算保留亮斑 → 顶帽应接近 0（证明不是"随便返回原图"）
            using (var bigSpot = MakeImage(100, (40, 40, 60, 60, 200)))
            {
                var op = new GrayTopHatOperator { Width = 5, Height = 5 };
                using var result = op.Apply(bigSpot);
                double center = PixelProbe.At(result, 50, 50);
                Check("【反证】顶帽模板小于亮斑 → 中心 ≈ 0（亮斑被开运算保留）",
                    Math.Abs(center) <= 2, center.ToString("0.##"));
            }
        }

        // ==================================================================
        //  ③ 像素级：Sobel / Laplace 边缘图
        // ==================================================================
        private static void RunEdgePixels()
        {
            // 垂直阶跃边：左半 0、右半 255（列 50 起）。边缘带应高、平坦区应接近 0。
            using var step = MakeImage(0, (0, 50, ImgH - 1, ImgW - 1, 255));

            var sobel = new SobelEdgeOperator { MaskSize = 3, FilterType = SobelFilterType.SumAbs };
            using (var result = sobel.Apply(step))
            {
                var edge = PixelProbe.Band(result, 50, 48, 50, 52);
                var flat = PixelProbe.Band(result, 50, 10, 50, 20);
                Check("【像素】Sobel：阶跃边处幅值高（≥ 100）",
                    edge.Max >= 100, $"edgeMax={edge.Max:0.##}");
                Check("【像素】Sobel：平坦区接近 0（≤ 5）",
                    flat.Max <= 5, $"flatMax={flat.Max:0.##}");
            }

            var laplace = new LaplaceEdgeOperator { MaskSize = 3, FilterMask = LaplaceFilterMask.N4 };
            using (var result = laplace.Apply(step))
            {
                var edge = PixelProbe.Band(result, 50, 48, 50, 52);
                var flat = PixelProbe.Band(result, 50, 10, 50, 20);
                Check("【像素】Laplace：阶跃边处绝对响应高（≥ 50）",
                    edge.Max >= 50, $"edgeMax={edge.Max:0.##}");
                Check("【像素】Laplace：平坦区接近 0（≤ 5）",
                    flat.Max <= 5, $"flatMax={flat.Max:0.##}");
            }

            // 彩色输入必须先转单通道：输出通道数必须是 1，否则下游阈值/Blob 会拿到三张边缘图。
            using var color = MakeColorStep();
            using (var result = sobel.Apply(color))
            {
                HOperatorSet.CountChannels(result, out HTuple channels);
                Check("【像素】Sobel 对彩色输入输出单通道（先 rgb1_to_gray）",
                    channels.I == 1, $"channels={channels.I}");
            }
        }

        // ==================================================================
        //  ④ 参数收敛 + 存盘往返
        // ==================================================================
        private static void RunNormalizeAndPersistence()
        {
            using var image = MakeImage(100, (48, 48, 52, 52, 200));

            var evenSobel = new SobelEdgeOperator { MaskSize = 4 };
            using (evenSobel.Apply(image)) { }
            Check("Sobel 偶数模板 4 → 收敛为 5",
                evenSobel.MaskSize == 5, evenSobel.MaskSize.ToString());

            var tooLarge = new LaplaceEdgeOperator { MaskSize = 12 };
            using (tooLarge.Apply(image)) { }
            Check("Laplace 模板 12 → 收敛为 11（夹到上限）",
                tooLarge.MaskSize == 11, tooLarge.MaskSize.ToString());

            var tooSmall = new SobelEdgeOperator { MaskSize = 0 };
            using (tooSmall.Apply(image)) { }
            Check("Sobel 模板 0 → 收敛为 3（夹到下限）",
                tooSmall.MaskSize == 3, tooSmall.MaskSize.ToString());

            var topHat = new GrayTopHatOperator { Width = 0, Height = -5 };
            using (topHat.Apply(image)) { }
            Check("顶帽模板宽 0/高 −5 → 收敛为 1×1",
                topHat.Width == 1 && topHat.Height == 1, $"{topHat.Width}×{topHat.Height}");

            // 存盘：枚举按数值写（与 OperatorEnums.cs 的"数值即格式"约定一致），LoadParams 能还原。
            var sobel = new SobelEdgeOperator { MaskSize = 7, FilterType = SobelFilterType.Sum };
            var saved = sobel.SaveParams();
            Check("SaveParams 含 MaskSize / FilterType（参数面板反射得到）",
                saved.ContainsKey(nameof(SobelEdgeOperator.MaskSize))
                && saved.ContainsKey(nameof(SobelEdgeOperator.FilterType)),
                string.Join(",", saved.Keys));
            Check("枚举按数值存盘（FilterType=Sum → \"1\"）",
                saved[nameof(SobelEdgeOperator.FilterType)] == "1",
                saved[nameof(SobelEdgeOperator.FilterType)]);

            var restored = OperatorRegistry.Create("sobel_edge") as SobelEdgeOperator;
            restored!.LoadParams(saved);
            Check("Sobel 存盘往返：MaskSize=7 / FilterType=Sum",
                restored.MaskSize == 7 && restored.FilterType == SobelFilterType.Sum,
                $"{restored.MaskSize}/{restored.FilterType}");

            var topHatSaved = new GrayTopHatOperator { Width = 9, Height = 13 }.SaveParams();
            var topHatRestored = OperatorRegistry.Create("gray_top_hat") as GrayTopHatOperator;
            topHatRestored!.LoadParams(topHatSaved);
            Check("顶帽存盘往返：Width=9 / Height=13",
                topHatRestored.Width == 9 && topHatRestored.Height == 13,
                $"{topHatRestored.Width}×{topHatRestored.Height}");
        }

        // ==================================================================
        //  ⑤ 快照 → 活链 → 真执行
        // ==================================================================
        private static void RunChainEndToEnd()
        {
            using var image = MakeImage(100, (48, 48, 52, 52, 200));
            var plugin = new PreProcessingPlugin { InstanceName = "预处理断言" };
            try
            {
                plugin.Chain = new List<OperatorStepItem>
                {
                    new()
                    {
                        OperatorKey = "gray_top_hat",
                        Params = new Dictionary<string, string> { ["Width"] = "15", ["Height"] = "15" },
                    },
                    new()
                    {
                        OperatorKey = "sobel_edge",
                        Params = new Dictionary<string, string> { ["MaskSize"] = "3", ["FilterType"] = "0" },
                    },
                };
                plugin.Initialize(new FakeStepData());

                Check("快照 → 活链：2 步且类型正确",
                    plugin.Operators.Count == 2
                    && plugin.Operators[0] is GrayTopHatOperator
                    && plugin.Operators[1] is SobelEdgeOperator,
                    string.Join(" → ", plugin.Operators.Select(o => o.GetType().Name)));

                plugin.SrcImage.Value = image;
                var log = new StubLog();
                plugin.Execute(MakeContext(log));

                Check("链执行成功（无异常/无 Fail）",
                    plugin.Success.Value is true, plugin.ErrorMessage.Value as string ?? "");
                Check("统计端口：StepCount=2 / ActiveCount=2",
                    plugin.StepCount.Value is 2 && plugin.ActiveCount.Value is 2,
                    $"step={plugin.StepCount.Value} active={plugin.ActiveCount.Value}");
                Check("ActiveOperators 含新增算子名（按执行顺序）",
                    (plugin.ActiveOperators.Value as string ?? "").Contains("灰度顶帽")
                    && (plugin.ActiveOperators.Value as string ?? "").Contains("Sobel 边缘"),
                    plugin.ActiveOperators.Value as string ?? "");
                Check("输出端口 Image 已赋值且尺寸不变",
                    plugin.Image.TypedValue is HImage output
                    && output.IsInitialized()
                    && PixelProbe.SizeOf(output) == (ImgW, ImgH),
                    plugin.Image.TypedValue == null ? "null" : PixelProbe.SizeOf((HImage)plugin.Image.TypedValue).ToString());
            }
            finally
            {
                plugin.Dispose();
            }
        }

        // ==================================================================
        //  合成图工具
        // ==================================================================
        private static HImage MakeImage(int background, params (int R1, int C1, int R2, int C2, int Gray)[] rects)
        {
            HOperatorSet.GenImageConst(out HObject proto, "byte", ImgW, ImgH);
            HOperatorSet.GenImageProto(proto, out HObject current, background);
            proto.Dispose();
            bool disposed = false;
            try
            {
                foreach (var r in rects)
                {
                    HOperatorSet.GenRectangle1(out HObject region, r.R1, r.C1, r.R2, r.C2);
                    try
                    {
                        HOperatorSet.PaintRegion(region, current, out HObject painted, r.Gray, "fill");
                        current.Dispose();
                        current = painted;
                    }
                    finally { region.Dispose(); }
                }
                var image = new HImage(current);
                current.Dispose();
                disposed = true;
                return image;
            }
            finally
            {
                if (!disposed) current.Dispose();
            }
        }

        /// <summary>合成一张三通道阶跃图（R=左 0 右 255，G/B=0），用于验证边缘算子先转单通道。</summary>
        private static HImage MakeColorStep()
        {
            HOperatorSet.GenImageConst(out HObject rProto, "byte", ImgW, ImgH);
            HOperatorSet.GenImageProto(rProto, out HObject r, 0);
            rProto.Dispose();
            HOperatorSet.GenRectangle1(out HObject rect, 0, 50, ImgH - 1, ImgW - 1);
            HOperatorSet.PaintRegion(rect, r, out HObject rPainted, 255, "fill");
            rect.Dispose();
            r.Dispose();

            HOperatorSet.GenImageConst(out HObject gProto, "byte", ImgW, ImgH);
            HOperatorSet.GenImageProto(gProto, out HObject g, 0);
            gProto.Dispose();
            HOperatorSet.GenImageConst(out HObject bProto, "byte", ImgW, ImgH);
            HOperatorSet.GenImageProto(bProto, out HObject b, 0);
            bProto.Dispose();

            HOperatorSet.Compose3(rPainted, g, b, out HObject composed);
            rPainted.Dispose();
            g.Dispose();
            b.Dispose();
            var image = new HImage(composed);
            composed.Dispose();
            return image;
        }

        private static ExecutionContext MakeContext(ILogService log) =>
            new(log, new FlowSession { FlowName = "预处理断言" }, new WorkspaceContext(),
                new System.Threading.CancellationTokenSource().Token);
    }
}
