using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
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
            RunPreviewRefreshContract();
            RunOperatorFailureContract();
            RunFftFilter();
        }

        // ==================================================================
        //  ⑤ 试运行桥接输入图 → 预览必须跟着刷新
        // ==================================================================

        /// <summary>
        /// 点「执行」后画面不刷新（真机 2026-10-20）：状态栏显示"成功"，画布还是黑的。
        ///
        /// 链路是 `PluginTestRunner.BridgeInputs` 把上游实际值写进**配置实例**的同名端口
        /// （走 `port.Value`），而本插件此前只在"视图就绪 / 参数变更 / 链变更"时取图 ——
        /// 端口灌值这条路径没有任何订阅，`_sourceImage` 一直是 null，`DisplayImage` 自然是黑的。
        /// 这里复刻那一步（直接给 SrcImage 灌一张图），钉住"灌完立刻能看到"。
        ///
        /// 注意：断言宿主没有 `Application`，走的是 `SchedulePreview` 的同步分支
        /// （有 Application 时是 200ms 防抖），所以这条在无头环境里也是确定的。
        /// </summary>
        private static void RunPreviewRefreshContract()
        {
            Section("[PRE] 试运行桥接输入图后预览要跟着刷新（点「执行」画面不能还是黑的）");

            var plugin = new PreProcessingPlugin { InstanceName = "预处理预览断言" };
            using var img = MakeImage(100, (20, 20, 60, 60, 200));
            try
            {
                plugin.OnViewLoaded();
                Check("视图就绪、上游还没给图 → 预览为空（画布显示「尚无预览」）",
                    plugin.DisplayImage == null,
                    plugin.DisplayImage == null ? "null" : "不该有图");

                // 与 BridgeInputs 同一个动作：配置实例的输入端口被灌入上游实际值
                plugin.SrcImage.Value = img;

                Check("灌入输入图后预览立刻取到这张图（不用再动一下参数）",
                    ReferenceEquals(plugin.DisplayImage, img),
                    plugin.DisplayImage == null ? "null（没刷新）"
                        : ReferenceEquals(plugin.DisplayImage, img) ? "同一张图" : "另一张图");
            }
            finally
            {
                plugin.Dispose();
            }
        }

        // ==================================================================
        //  ① 注册表：Key / 显示名 / 分组 / 顺序 / 总数
        // ==================================================================
        private static void RunRegistryContract()
        {
            var all = OperatorRegistry.All;
            // 25 → 30：新增算术运算算子组 5 个（灰度加常数/减常数/乘增益/转浮点图/灰度归一化）。
            // 该组只能在单图上做常数算术（链内算子的 OperatorStepItem 拿不到第二张图），
            // 双图逐像元差分归 Plugin.ImageDiff，不在这里。
            // 30 → 31：新增「FFT 频域滤波」（滤波组 Order 75，见 RunFftFilter）。
            Check("算子总数 = 31（原 30 + FFT 频域滤波 1）",
                all.Count == 31, $"Count={all.Count}");
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
            // 八族：算术运算（Order 90~94）按 Category Order 落在「灰度形态学」之后、
            // 「图像增强」（Order 100+）之前；其余顺序不变，边缘检测仍在最后。
            Check("分组顺序：八族按 Order 排，边缘检测在最后",
                categories.SequenceEqual(new[]
                {
                    "色彩与通道", "几何调整", "滤波", "灰度形态学", "算术运算", "图像增强", "二值化", "边缘检测"
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

            var sobel = new SobelEdgeOperator { MaskSize = 3, FilterType = SobelFilterType.SumSqrt };
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
            var sobel = new SobelEdgeOperator { MaskSize = 7, FilterType = SobelFilterType.SumSqrt };
            var saved = sobel.SaveParams();
            Check("SaveParams 含 MaskSize / FilterType（参数面板反射得到）",
                saved.ContainsKey(nameof(SobelEdgeOperator.MaskSize))
                && saved.ContainsKey(nameof(SobelEdgeOperator.FilterType)),
                string.Join(",", saved.Keys));
            Check("枚举按数值存盘（FilterType=SumSqrt → \"1\"）",
                saved[nameof(SobelEdgeOperator.FilterType)] == "1",
                saved[nameof(SobelEdgeOperator.FilterType)]);

            var restored = OperatorRegistry.Create("sobel_edge") as SobelEdgeOperator;
            restored!.LoadParams(saved);
            Check("Sobel 存盘往返：MaskSize=7 / FilterType=SumSqrt",
                restored.MaskSize == 7 && restored.FilterType == SobelFilterType.SumSqrt,
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
                Check("无失败步：FailedCount=0 / FailedOperators 为空串",
                    plugin.FailedCount.Value is 0 && (plugin.FailedOperators.Value as string) == "",
                    $"count={plugin.FailedCount.Value} text={plugin.FailedOperators.Value as string ?? "null"}");
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
        //  ⑥ 算子失败不再静默吞成成功（P0-6）
        // ==================================================================

        /// <summary>
        /// 失败口径：以前算子抛异常只写一条 Warn，Success 由基类预置 true，
        /// 下游拿到的是一张"少跑了几步"的图却看不到任何失败信号。
        /// 现在失败必须同时落到 FailedOperators / FailedCount；
        /// FailOnOperatorError=true 时整步判失败（默认 false 保持旧行为，避免既有流程升级后红掉）。
        ///
        /// 失败参数选「均值滤波 999×999」：RangeValidation 允许 1~999、Normalize 只抬下限，
        /// 而 HALCON mean_image 的硬限制是 MaskWidth &lt; width(Image)*2（100 宽图 → 上限 199），
        /// 实测抛 #3033 Filter size exceeds image size —— 一个"界面填得进去、执行必抛"的合法输入。
        /// </summary>
        private static void RunOperatorFailureContract()
        {
            Section("[PRE] 算子失败：默认跳过并记录 / 开关=true 判失败 / 预览黄色提示");

            using var image = MakeImage(100, (48, 48, 52, 52, 200));

            // ① 默认开关（false）：跳过失败步继续，但失败必须记录到端口
            var plugin = new PreProcessingPlugin { InstanceName = "预处理失败断言" };
            try
            {
                plugin.Chain = new List<OperatorStepItem>
                {
                    new()
                    {
                        OperatorKey = "mean",
                        Params = new Dictionary<string, string> { ["Width"] = "999", ["Height"] = "999" },
                    },
                };
                plugin.Initialize(new FakeStepData());
                plugin.SrcImage.Value = image;
                plugin.Execute(MakeContext(new StubLog()));

                string failed = plugin.FailedOperators.Value as string ?? "";
                Check("默认开关=false：算子失败仍判成功（跳过失败步继续）",
                    plugin.Success.Value is true, plugin.ErrorMessage.Value as string ?? "");
                Check("默认开关=false：FailedCount=1",
                    plugin.FailedCount.Value is 1, $"FailedCount={plugin.FailedCount.Value}");
                Check("默认开关=false：FailedOperators 形如「1:均值滤波: 原因」",
                    failed.StartsWith("1:均值滤波: ", StringComparison.Ordinal)
                    && failed.Contains("Filter size", StringComparison.Ordinal),
                    failed);
                Check("默认开关=false：Image 照常赋值（失败步透传原图，尺寸不变）",
                    plugin.Image.TypedValue is HImage passthrough
                    && passthrough.IsInitialized()
                    && PixelProbe.SizeOf(passthrough) == (ImgW, ImgH),
                    plugin.Image.TypedValue == null ? "null" : PixelProbe.SizeOf((HImage)plugin.Image.TypedValue).ToString());
            }
            finally
            {
                plugin.Dispose();
            }

            // ② 开关=true：任一算子失败 → 跑完整链记录全部失败后判失败，输出端口照常赋值
            var strict = new PreProcessingPlugin
            {
                InstanceName = "预处理失败断言（严格）",
                FailOnOperatorError = true,
            };
            try
            {
                strict.Chain = new List<OperatorStepItem>
                {
                    new()
                    {
                        OperatorKey = "mean",
                        Params = new Dictionary<string, string> { ["Width"] = "999", ["Height"] = "999" },
                    },
                    new()
                    {
                        OperatorKey = "illuminate",
                        Params = new Dictionary<string, string>
                        {
                            ["Width"] = "999", ["Height"] = "999", ["Factor"] = "0.7",
                        },
                    },
                };
                strict.Initialize(new FakeStepData());
                strict.SrcImage.Value = image;
                strict.Execute(MakeContext(new StubLog()));

                string failed = strict.FailedOperators.Value as string ?? "";
                string error = strict.ErrorMessage.Value as string ?? "";
                Check("开关=true：任一算子失败 → Success=false",
                    strict.Success.Value is false, error);
                Check("开关=true：ErrorMessage 含全部失败算子名（均值滤波 / 对比度）",
                    error.StartsWith("预处理算子失败：", StringComparison.Ordinal)
                    && error.Contains("均值滤波", StringComparison.Ordinal)
                    && error.Contains("对比度", StringComparison.Ordinal),
                    error);
                Check("开关=true：FailedCount=2（跑完整链，记录全部失败）",
                    strict.FailedCount.Value is 2, $"FailedCount={strict.FailedCount.Value}");
                Check("开关=true：FailedOperators 两条用「；」分隔",
                    failed.Contains("；", StringComparison.Ordinal)
                    && failed.StartsWith("1:均值滤波: ", StringComparison.Ordinal)
                    && failed.Contains("2:对比度: ", StringComparison.Ordinal),
                    failed);
                Check("开关=true：输出端口照常赋值（图 + 统计都在）",
                    strict.Image.TypedValue is HImage output
                    && output.IsInitialized()
                    && strict.StepCount.Value is 2
                    && strict.ActiveCount.Value is 2,
                    $"image={(strict.Image.TypedValue == null ? "null" : "ok")} step={strict.StepCount.Value} active={strict.ActiveCount.Value}");
            }
            finally
            {
                strict.Dispose();
            }

            // ③ 配置预览：记录型 logger 把失败交到界面（黄色信息条），不再传 logger:null
            var preview = new PreProcessingPlugin { InstanceName = "预处理预览失败断言" };
            try
            {
                preview.Chain = new List<OperatorStepItem>
                {
                    new()
                    {
                        OperatorKey = "mean",
                        Params = new Dictionary<string, string> { ["Width"] = "999", ["Height"] = "999" },
                    },
                };
                preview.Initialize(new FakeStepData());
                preview.OnViewLoaded();
                // 断言宿主没有 Application → 这条走 SchedulePreview 的同步分支（与 ⑤ 同一前提）
                preview.SrcImage.Value = image;

                Check("预览：失败算子暴露给视图（黄色信息条「N 个算子执行失败：…」）",
                    preview.PreviewFailure.StartsWith("1 个算子执行失败：", StringComparison.Ordinal)
                    && preview.PreviewFailure.Contains("均值滤波", StringComparison.Ordinal),
                    preview.PreviewFailure);
                Check("预览：失败原文也进了记录型 logger（悬停可见）",
                    (preview.PreviewWarningDetail ?? "").Contains("执行失败，已跳过", StringComparison.Ordinal),
                    preview.PreviewWarningDetail ?? "null");
            }
            finally
            {
                preview.Dispose();
            }
        }

        // ==================================================================
        //  ⑦ FFT 频域滤波
        // ==================================================================

        /// <summary>
        /// FFT 频域滤波（fft_filter）的判据。合成图 = 128×128 均匀背景 + 三条正弦光栅，
        /// 三条频率都落在整数 bin 上（频谱是干净的单根谱线，可以逐根读幅度）：
        ///   C = 3/128  ≈ 0.0234（低频，作参照物）、A = 16/128 = 0.125（被测光栅）、B = 32/128 = 0.25（高频）。
        /// 幅度 20 / 40 / 20 → 基准幅度比 A/C ≈ A/B ≈ 2。
        ///
        /// 【为什么用"谱线幅度比"而不是 max−min / 标准差】
        /// 算子最后一步是 RealToByteNormalized（min/max 线性映射到 0~255），任何"整体缩放 + 平移"
        /// 都会被它抹平：只要图里还剩一点正弦，输出就会被拉成满量程，用 max−min 判"衰减了多少"
        /// 永远得到 255（实测：高斯低通 0.05 把 0.125 处衰减到 1.3%，max−min 仍是 255）。
        /// 而**两条谱线的比值**在线性映射下不变，所以 A/C 才是"这条频率被衰减了多少"的可判据量。
        ///
        /// 【反证成对出现】每条"该被去掉"的判据都配一条"把滤波中心挪到别的频率 → 光栅必须留下"，
        /// 否则"整张图都被抹平"也能让前者变绿。
        /// </summary>
        private static void RunFftFilter()
        {
            Section("[PRE] FFT 频域滤波：频带选择性 / 反证 / HALCON 参考实现 / 尺寸与存盘");

            var descriptor = OperatorRegistry.Find("fft_filter");
            Check("FFT 频域滤波：Key/显示名/分组/Order 正确",
                descriptor != null && descriptor.DisplayName == "FFT 频域滤波"
                && descriptor.Category == "滤波" && descriptor.Order == 75,
                descriptor == null ? "未注册" : $"{descriptor.DisplayName}/{descriptor.Category}/{descriptor.Order}");

            var filters = OperatorRegistry.Categories.FirstOrDefault(c => c.Name == "滤波");
            Check("滤波组顺序：均值 → 中值 → 高斯 → FFT 频域滤波",
                filters != null && filters.Items.Select(d => d.DisplayName).SequenceEqual(new[]
                {
                    "均值滤波", "中值滤波", "高斯滤波", "FFT 频域滤波"
                }),
                filters == null ? "无「滤波」分组" : string.Join(" → ", filters.Items.Select(d => d.DisplayName)));

            using var bars = MakeFrequencyBars(FftW, FftH, 128.0,
                (20.0, FreqLow), (40.0, FreqTarget), (20.0, FreqHigh));

            // 合成图自检：这条红了说明图本身就不对，后面所有判据都作废
            double baselineAc = PeakRatio(bars, FreqTarget, FreqLow);
            double baselineAb = PeakRatio(bars, FreqTarget, FreqHigh);
            Check("合成光栅：A/C ≈ 2、A/B ≈ 2（谱线幅度比与设定幅度 40:20 一致）",
                Math.Abs(baselineAc - 2.0) <= 0.2 && Math.Abs(baselineAb - 2.0) <= 0.2,
                $"A/C={baselineAc:0.000} A/B={baselineAb:0.000}");

            // ① 低通：截止在目标频率之上 → 保留；截止在目标频率之下 → 衰减 ≥ 80%
            using (var result = new FftFilterOperator
            {
                FilterType = FftFilterType.Lowpass,
                FilterShape = FftFilterShape.Ideal,
                CutoffHigh = 0.20,
            }.Apply(bars))
            {
                double ratio = PeakRatio(result, FreqTarget, FreqLow);
                Check("【低通·理想】截止 0.20 > 目标 0.125 → 光栅保留（A/C ≥ 1.8）",
                    ratio >= 1.8, $"A/C={ratio:0.000}");
            }

            using (var result = new FftFilterOperator
            {
                FilterType = FftFilterType.Lowpass,
                FilterShape = FftFilterShape.Ideal,
                CutoffHigh = 0.05,
            }.Apply(bars))
            {
                double ratio = PeakRatio(result, FreqTarget, FreqLow);
                Check("【低通·理想】截止 0.05 < 目标 0.125 → 光栅衰减 ≥ 80%（A/C ≤ 0.4）",
                    ratio <= 0.4, $"A/C={ratio:0.000}");
            }

            // 高斯档：衰减是渐变的，只判"被压下去多少"，同时确认没把低频参照物一起抹掉
            using (var result = new FftFilterOperator
            {
                FilterType = FftFilterType.Lowpass,
                FilterShape = FftFilterShape.Gaussian,
                CutoffHigh = 0.05,
            }.Apply(bars))
            {
                double ratio = PeakRatio(result, FreqTarget, FreqLow);
                Check("【低通·高斯】截止 0.05 → 目标频率衰减 ≥ 80%（A/C ≤ 0.4）",
                    ratio <= 0.4, $"A/C={ratio:0.000}");
            }

            // ② 带阻：环带盖住目标频率 → 去掉；环带挪到别处 → 必须留下（反证）
            using (var result = new FftFilterOperator
            {
                FilterType = FftFilterType.Bandreject,
                FilterShape = FftFilterShape.Ideal,
                CutoffLow = 0.05,
                CutoffHigh = 0.20,
            }.Apply(bars))
            {
                double ratio = PeakRatio(result, FreqTarget, FreqLow);
                Check("【带阻·理想】环带 0.05~0.20 盖住 0.125 → 光栅衰减 ≥ 80%（A/C ≤ 0.4）",
                    ratio <= 0.4, $"A/C={ratio:0.000}");
            }

            using (var result = new FftFilterOperator
            {
                FilterType = FftFilterType.Bandreject,
                FilterShape = FftFilterShape.Ideal,
                CutoffLow = 0.25,
                CutoffHigh = 0.40,
            }.Apply(bars))
            {
                double ratio = PeakRatio(result, FreqTarget, FreqLow);
                Check("【反证·带阻】环带挪到 0.25~0.40 → 0.125 的光栅必须留下（A/C ≥ 1.8）",
                    ratio >= 1.8, $"A/C={ratio:0.000}");
            }

            // ③ 带通：只留目标频率 → A/B 变大；通带挪到别处 → 目标必须被去掉（反证）
            using (var result = new FftFilterOperator
            {
                FilterType = FftFilterType.Bandpass,
                FilterShape = FftFilterShape.Ideal,
                CutoffLow = 0.05,
                CutoffHigh = 0.20,
            }.Apply(bars))
            {
                double ratio = PeakRatio(result, FreqTarget, FreqHigh);
                Check("【带通·理想】通带 0.05~0.20 → 目标保留、0.25 的高频被去掉（A/B ≥ 10）",
                    ratio >= 10.0, $"A/B={ratio:0.000}");
            }

            using (var result = new FftFilterOperator
            {
                FilterType = FftFilterType.Bandpass,
                FilterShape = FftFilterShape.Ideal,
                CutoffLow = 0.25,
                CutoffHigh = 0.40,
            }.Apply(bars))
            {
                double ratio = PeakRatio(result, FreqTarget, FreqHigh);
                Check("【反证·带通】通带挪到 0.25~0.40 → 0.125 的光栅必须被去掉（A/B ≤ 0.1）",
                    ratio <= 0.1, $"A/B={ratio:0.000}");
            }

            // ④ 陷波：圈住目标频率 → 去掉；圈住别的频率 → 必须留下（反证）
            using (var result = new FftFilterOperator
            {
                FilterType = FftFilterType.Notch,
                FilterShape = FftFilterShape.Ideal,
                NotchRow = 0.0,
                NotchCol = FreqTarget,
                NotchRadius = 0.05,
                KeepDc = true,
            }.Apply(bars))
            {
                double ratio = PeakRatio(result, FreqTarget, FreqLow);
                Check("【陷波·理想】中心 (0, 0.125) 半径 0.05 → 光栅衰减 ≥ 80%（A/C ≤ 0.4）",
                    ratio <= 0.4, $"A/C={ratio:0.000}");
            }

            using (var result = new FftFilterOperator
            {
                FilterType = FftFilterType.Notch,
                FilterShape = FftFilterShape.Ideal,
                NotchRow = 0.0,
                NotchCol = FreqHigh,
                NotchRadius = 0.05,
                KeepDc = true,
            }.Apply(bars))
            {
                double ratio = PeakRatio(result, FreqTarget, FreqLow);
                double highRatio = PeakRatio(result, FreqHigh, FreqLow);
                Check("【反证·陷波】中心挪到 0.25 → 0.125 的光栅必须留下（A/C ≥ 1.8）且 0.25 被去掉",
                    ratio >= 1.8 && highRatio <= 0.4,
                    $"A/C={ratio:0.000} B/C={highRatio:0.000}");
            }

            using (var result = new FftFilterOperator
            {
                FilterType = FftFilterType.Notch,
                FilterShape = FftFilterShape.Gaussian,
                NotchRow = 0.0,
                NotchCol = FreqTarget,
                NotchRadius = 0.05,
                KeepDc = true,
            }.Apply(bars))
            {
                double ratio = PeakRatio(result, FreqTarget, FreqLow);
                Check("【陷波·高斯】中心 (0, 0.125) 半径 0.05 → 光栅衰减 ≥ 80%（A/C ≤ 0.4）",
                    ratio <= 0.4, $"A/C={ratio:0.000}");
            }

            // ⑤ 与 HALCON 参考实现对拍：掩膜用 HALCON 自带生成器，管线独立写一遍
            using (var reference = ReferenceFftLowpass(bars, 0.12, gaussian: true))
            using (var actual = new FftFilterOperator
            {
                FilterType = FftFilterType.Lowpass,
                FilterShape = FftFilterShape.Gaussian,
                CutoffHigh = 0.12,
            }.Apply(bars))
            {
                double diff = MeanAbsDiff(reference, actual);
                Check("参考实现对拍（高斯低通 0.12，gen_gauss_filter）平均绝对差 < 1.0",
                    diff < 1.0, $"meanAbsDiff={diff:0.0000}");
            }

            using (var reference = ReferenceFftLowpass(bars, 0.20, gaussian: false))
            using (var actual = new FftFilterOperator
            {
                FilterType = FftFilterType.Lowpass,
                FilterShape = FftFilterShape.Ideal,
                CutoffHigh = 0.20,
            }.Apply(bars))
            {
                double diff = MeanAbsDiff(reference, actual);
                Check("参考实现对拍（理想低通 0.20，gen_lowpass）平均绝对差 < 1.0",
                    diff < 1.0, $"meanAbsDiff={diff:0.0000}");
            }

            // ⑥ 尺寸 / 通道：与原图同宽高、单通道 byte（彩色输入先转灰度，与其它单通道算子一致）
            using (var result = new FftFilterOperator
            {
                FilterType = FftFilterType.Bandpass,
                FilterShape = FftFilterShape.Ideal,
                CutoffLow = 0.05,
                CutoffHigh = 0.20,
            }.Apply(bars))
            {
                HOperatorSet.CountChannels(result, out HTuple channels);
                HOperatorSet.GetImageType(result, out HTuple type);
                Check("输出尺寸不变（128×128）且单通道 byte",
                    PixelProbe.SizeOf(result) == (FftW, FftH) && channels.I == 1 && type.S == "byte",
                    $"{PixelProbe.SizeOf(result)} ch={channels.I} type={type.S}");
            }

            using (var color = MakeColorStep())
            using (var result = new FftFilterOperator
            {
                FilterType = FftFilterType.Lowpass,
                FilterShape = FftFilterShape.Gaussian,
                CutoffHigh = 0.10,
            }.Apply(color))
            {
                HOperatorSet.CountChannels(result, out HTuple channels);
                Check("彩色输入：输出仍是原尺寸、单通道（先 rgb1_to_gray）",
                    PixelProbe.SizeOf(result) == (ImgW, ImgH) && channels.I == 1,
                    $"{PixelProbe.SizeOf(result)} ch={channels.I}");
            }

            // ⑦ 参数收敛：只做文档化的夹取与低/高互换，不按图像尺寸悄悄改频率
            var clamped = new FftFilterOperator { CutoffLow = -0.2, CutoffHigh = 0.8, NotchRadius = -0.1, NotchCol = 0.9 };
            using (clamped.Apply(bars)) { }
            Check("参数收敛：截止 -0.2/0.8 → 0/0.5，陷波半径 -0.1 → 0，中心 0.9 → 0.5",
                Math.Abs(clamped.CutoffLow) < 1e-9 && Math.Abs(clamped.CutoffHigh - 0.5) < 1e-9
                && Math.Abs(clamped.NotchRadius) < 1e-9 && Math.Abs(clamped.NotchCol - 0.5) < 1e-9,
                $"low={clamped.CutoffLow:0.###} high={clamped.CutoffHigh:0.###} r={clamped.NotchRadius:0.###} col={clamped.NotchCol:0.###}");

            var swapped = new FftFilterOperator { CutoffLow = 0.30, CutoffHigh = 0.10 };
            using (swapped.Apply(bars)) { }
            Check("参数收敛：低/高截止填反 → 只交换（0.30/0.10 → 0.10/0.30）",
                Math.Abs(swapped.CutoffLow - 0.10) < 1e-9 && Math.Abs(swapped.CutoffHigh - 0.30) < 1e-9,
                $"low={swapped.CutoffLow:0.###} high={swapped.CutoffHigh:0.###}");

            // ⑧ 存盘往返：枚举按数值存（R84：FlatPropertyGrid 读 Description，存盘读数值）
            var saved = new FftFilterOperator
            {
                FilterType = FftFilterType.Bandreject,
                FilterShape = FftFilterShape.Butterworth,
                CutoffLow = 0.08,
                CutoffHigh = 0.22,
                NotchRow = -0.1,
                NotchCol = 0.3,
                NotchRadius = 0.04,
                KeepDc = false,
            }.SaveParams();

            Check("存盘：枚举按数值（Bandreject→\"3\"、Butterworth→\"2\"），布尔按 1/0",
                saved[nameof(FftFilterOperator.FilterType)] == "3"
                && saved[nameof(FftFilterOperator.FilterShape)] == "2"
                && saved[nameof(FftFilterOperator.KeepDc)] == "0",
                $"type={saved[nameof(FftFilterOperator.FilterType)]} shape={saved[nameof(FftFilterOperator.FilterShape)]} keepDc={saved[nameof(FftFilterOperator.KeepDc)]}");

            var restored = OperatorRegistry.Create("fft_filter") as FftFilterOperator;
            restored!.LoadParams(saved);
            Check("存盘往返：8 个参数全部还原",
                restored.FilterType == FftFilterType.Bandreject
                && restored.FilterShape == FftFilterShape.Butterworth
                && Math.Abs(restored.CutoffLow - 0.08) < 1e-9
                && Math.Abs(restored.CutoffHigh - 0.22) < 1e-9
                && Math.Abs(restored.NotchRow + 0.1) < 1e-9
                && Math.Abs(restored.NotchCol - 0.3) < 1e-9
                && Math.Abs(restored.NotchRadius - 0.04) < 1e-9
                && !restored.KeepDc,
                $"{restored.FilterType}/{restored.FilterShape}/{restored.CutoffLow}/{restored.CutoffHigh}/{restored.NotchRow}/{restored.NotchCol}/{restored.NotchRadius}/{restored.KeepDc}");
        }

        private const int FftW = 128;
        private const int FftH = 128;
        private const double FreqLow = 3.0 / FftW;      // ≈ 0.0234，作低频参照物
        private const double FreqTarget = 16.0 / FftW;  // 0.125，被测光栅
        private const double FreqHigh = 32.0 / FftW;    // 0.25，高频参照物

        /// <summary>
        /// 合成"均匀背景 + 若干条列方向正弦光栅"的 byte 图。
        /// 频率取 k/width 落在整数 bin 上，频谱才是干净的单根谱线（不泄漏到邻 bin）。
        /// </summary>
        private static HImage MakeFrequencyBars(int width, int height, double background,
            params (double Amplitude, double Frequency)[] bars)
        {
            var buffer = new byte[width * height];
            for (int row = 0; row < height; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    double value = background;
                    foreach (var bar in bars)
                        value += bar.Amplitude * Math.Sin(2 * Math.PI * bar.Frequency * col);
                    buffer[row * width + col] = (byte)Math.Clamp(Math.Round(value), 0, 255);
                }
            }

            GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                HOperatorSet.GenImage1(out HObject image, "byte", width, height, handle.AddrOfPinnedObject());
                try { return new HImage(image); }
                finally { image.Dispose(); }
            }
            finally
            {
                handle.Free();
            }
        }

        /// <summary>
        /// 读 (0, frequency) 这根谱线的幅值。
        /// fft_image 的直流在 (ceil(H/2), ceil(W/2))（实测 64→32、63→32、65→33）。
        /// </summary>
        private static double SpectrumPeak(HImage image, double frequency)
        {
            HOperatorSet.GetImageSize(image, out HTuple width, out HTuple height);
            int centerRow = (height.I + 1) / 2;
            int centerCol = (width.I + 1) / 2;
            int col = centerCol + (int)Math.Round(frequency * width.I);

            HOperatorSet.FftImage(image, out HObject spectrum);
            try
            {
                HOperatorSet.AbsImage(spectrum, out HObject magnitude);
                try
                {
                    HOperatorSet.GetGrayval(magnitude, centerRow, col, out HTuple value);
                    return value.D;
                }
                finally
                {
                    magnitude.Dispose();
                }
            }
            finally
            {
                spectrum.Dispose();
            }
        }

        /// <summary>两条谱线的幅度比；分母为 0（那条频率已被完全抹掉）时返回 +∞</summary>
        private static double PeakRatio(HImage image, double numerator, double denominator)
        {
            double top = SpectrumPeak(image, numerator);
            double bottom = SpectrumPeak(image, denominator);
            return bottom > 1e-9 ? top / bottom : double.PositiveInfinity;
        }

        /// <summary>
        /// HALCON 参考实现（独立写一遍，不经过 PreprocessHService）：
        /// fft_image → HALCON 自带掩膜（gen_gauss_filter / gen_lowpass）→ convol_fft
        /// → fft_generic('from_freq', 'real') → 与算子相同的 min/max 归一化。
        ///
        /// 掩膜交给 HALCON 的生成器算，所以这条比的是**整条管线**而不是同一个公式：
        /// 直流位置、共轭对称、反变换类型、归一化口径任何一处不一致都会让平均绝对差变大。
        ///
        /// fft_image_inv 不能用：实测 23.05 的 fft_image_inv 只输出 byte，负值被截成 0
        /// （-100~100 的图反变换回来是 0~100），会把高通/带通/陷波半波整流。
        /// </summary>
        private static HImage ReferenceFftLowpass(HImage source, double cutoff, bool gaussian)
        {
            HOperatorSet.GetImageSize(source, out HTuple width, out HTuple height);

            HOperatorSet.ConvertImageType(source, out HObject real, "real");
            HObject? mask = null, spectrum = null, filtered = null, inverse = null, scaled = null, bytes = null;
            try
            {
                if (gaussian)
                {
                    // gen_gauss_filter 的 Sigma 是空域标准差，频域响应 exp(-2π²σ²f²)；
                    // 要让"截止处 -6 dB"，σ = sqrt(ln2)/(π·sqrt2·截止)
                    double sigma = Math.Sqrt(Math.Log(2.0)) / (Math.PI * Math.Sqrt(2.0) * cutoff);
                    HOperatorSet.GenGaussFilter(out mask, sigma, sigma, 0, "none", "dc_center", width, height);
                }
                else
                {
                    // gen_lowpass 的 CutoffFrequency 以 Nyquist = 1.0 计，本算子的 0~0.5 要乘 2
                    HOperatorSet.GenLowpass(out mask, cutoff * 2.0, "none", "dc_center", width, height);
                }

                HOperatorSet.FftImage(real, out spectrum);
                HOperatorSet.ConvolFft(spectrum, mask, out filtered);
                HOperatorSet.FftGeneric(filtered, out inverse, "from_freq", 1, "sqrt", "dc_center", "real");

                HOperatorSet.MinMaxGray(inverse, inverse, 0, out HTuple min, out HTuple max, out _);
                HOperatorSet.ScaleImage(inverse, out scaled,
                    255.0 / (max.D - min.D), -min.D * 255.0 / (max.D - min.D));
                HOperatorSet.ConvertImageType(scaled, out bytes, "byte");
                return new HImage(bytes);
            }
            finally
            {
                bytes?.Dispose();
                scaled?.Dispose();
                inverse?.Dispose();
                filtered?.Dispose();
                spectrum?.Dispose();
                mask?.Dispose();
                real.Dispose();
            }
        }

        /// <summary>两张同尺寸 byte 图的平均绝对差（abs_diff_image + intensity 求均值）</summary>
        private static double MeanAbsDiff(HImage a, HImage b)
        {
            HOperatorSet.AbsDiffImage(a, b, out HObject diff, 1.0);
            try
            {
                HOperatorSet.GetDomain(diff, out HObject domain);
                try
                {
                    HOperatorSet.Intensity(domain, diff, out HTuple mean, out _);
                    return mean.D;
                }
                finally
                {
                    domain.Dispose();
                }
            }
            finally
            {
                diff.Dispose();
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
