using System;
using System.IO;
using System.Linq;
using System.Threading;
using Core.Interfaces;
using HalconDotNet;
using Plugin.DlClassify;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 深度学习分类插件（Plugin.DlClassify）的断言。分三段，各自独立可跑：
    ///
    ///   ① 后处理逻辑（受控数据，不需要模型）—— 缩放几何 / 布局校验 / 概率归一化 / TopK
    ///   ② ONNX Runtime 依赖链路（需要分类 ONNX 模型才真跑，否则跳过）
    ///   ③ 插件壳与端口契约（不需要模型）
    ///
    /// 为什么①是重点
    /// ---------
    /// 归一化与 TopK 写错的后果是**结果全错但程序不报错**：把已经 softmax 过的输出再做一次 softmax
    /// 会把分布压平（0.9 变 0.5），类别名会错位，而运行一切正常。这类错误无法靠"跑起来没崩"发现，
    /// 只能靠"已知输入 → 已知期望输出"来钉。
    /// </summary>
    internal static class ClassifyChecks
    {
        public static void Run()
        {
            Section("[深度学习分类] 后处理：缩放几何 / 布局校验 / softmax / TopK");
            RunPostProcessChecks();

            Section("[深度学习分类] ONNX Runtime 依赖链路");
            RunRuntimeChecks();

            Section("[深度学习分类] 插件壳与端口契约");
            RunPluginShellChecks();
        }

        // ==================================================================
        //  ① 后处理（受控数据）
        // ==================================================================

        private static void RunPostProcessChecks()
        {
            // ---- 缩放几何：Letterbox 宽图 / 高图（与 Yolo 同款几何）----
            var wide = ResizePlan.Compute(200, 100, 100, 100, ClassifyResizeMode.Letterbox);
            Check("letterbox 宽图 200×100→100×100：scale=0.5 scaled=100×50 pad=(0,25)",
                wide.ScaledWidth == 100 && wide.ScaledHeight == 50 && wide.PadX == 0 && wide.PadY == 25,
                $"scaled={wide.ScaledWidth}×{wide.ScaledHeight} pad=({wide.PadX},{wide.PadY})");

            var tall = ResizePlan.Compute(100, 200, 100, 100, ClassifyResizeMode.Letterbox);
            Check("letterbox 高图 100×200→100×100：scale=0.5 scaled=50×100 pad=(25,0)",
                tall.ScaledWidth == 50 && tall.ScaledHeight == 100 && tall.PadX == 25 && tall.PadY == 0,
                $"scaled={tall.ScaledWidth}×{tall.ScaledHeight} pad=({tall.PadX},{tall.PadY})");

            var stretch = ResizePlan.Compute(200, 100, 100, 100, ClassifyResizeMode.Stretch);
            Check("stretch 模式：直接铺满画布、无补边",
                stretch.ScaledWidth == 100 && stretch.ScaledHeight == 100 && stretch.PadX == 0 && stretch.PadY == 0,
                $"scaled={stretch.ScaledWidth}×{stretch.ScaledHeight} pad=({stretch.PadX},{stretch.PadY})");

            // ---- 布局嗅探：分类只接受 [1,C]（含 [1,1,C] / [C]），拒绝检测/分割形状 ----
            bool ok2 = ClassifyPostProcess.TrySniffLayout(new[] { 1, 3 }, out int c2, out _) && c2 == 3;
            Check("布局：[1,3] 识别为 3 类", ok2, $"classCount={c2}");

            bool okRank1 = ClassifyPostProcess.TrySniffLayout(new[] { 1000 }, out int c1, out _) && c1 == 1000;
            Check("布局：[1000]（无 batch 维）识别为 1000 类", okRank1, $"classCount={c1}");

            bool ok1x1 = ClassifyPostProcess.TrySniffLayout(new[] { 1, 1, 5 }, out int c3, out _) && c3 == 5;
            Check("布局：[1,1,5] 识别为 5 类", ok1x1, $"classCount={c3}");

            bool rejectDetect = !ClassifyPostProcess.TrySniffLayout(new[] { 1, 84, 8400 }, out _, out string detectErr);
            Check("【核心】布局：[1,84,8400]（检测）被拒绝，不拿检测输出当分类硬算",
                rejectDetect && detectErr.Contains("分类"), detectErr);

            bool rejectSegment = !ClassifyPostProcess.TrySniffLayout(new[] { 1, 2, 224, 224 }, out _, out string segErr);
            Check("【核心】布局：[1,2,224,224]（语义分割）被拒绝", rejectSegment, segErr);

            bool rejectSingle = !ClassifyPostProcess.TrySniffLayout(new[] { 1, 1 }, out _, out string singleErr);
            Check("布局：[1,1] 只有 1 类 → 拒绝（分类至少 2 类）", rejectSingle, singleErr);

            // ---- task 守卫 ----
            Check("task 守卫：classify 放行", ClassifyPostProcess.IsSupportedTask("classify", out _), "");
            Check("task 守卫：元数据缺失放行（老模型/第三方导出可能没写）",
                ClassifyPostProcess.IsSupportedTask("", out _) && ClassifyPostProcess.IsSupportedTask(null, out _), "");

            bool detectGuard = !ClassifyPostProcess.IsSupportedTask("detect", out string detectTaskErr);
            Check("【核心】task 守卫：detect 模型被明确拒绝（附中文任务名）",
                detectGuard && detectTaskErr.Contains("目标检测"), detectTaskErr);

            bool segmentGuard = !ClassifyPostProcess.IsSupportedTask("segment", out string segTaskErr);
            Check("task 守卫：segment 模型被明确拒绝", segmentGuard && segTaskErr.Contains("实例分割"), segTaskErr);

            // ---- 概率归一化：已是概率则原样、是 logits 则 softmax ----
            var alreadyProb = ClassifyPostProcess.NormalizeToProbabilities(new[] { 0.9f, 0.1f });
            Check("【核心】已是概率（和≈1）→ 原样保留，不再 softmax（否则分布被压平）",
                Math.Abs(alreadyProb[0] - 0.9) < 1e-6 && Math.Abs(alreadyProb[1] - 0.1) < 1e-6,
                string.Join(",", alreadyProb.Select(p => p.ToString("0.000"))));

            var fromLogits = ClassifyPostProcess.NormalizeToProbabilities(new[] { 2.0f, 1.0f });
            Check("logits（和≠1）→ softmax：和为 1 且第一类更高",
                Math.Abs(fromLogits.Sum() - 1.0) < 1e-6 && fromLogits[0] > fromLogits[1],
                string.Join(",", fromLogits.Select(p => p.ToString("0.000"))));

            var negative = ClassifyPostProcess.NormalizeToProbabilities(new[] { -1.0f, -2.0f });
            Check("全负 logits → softmax：和仍为 1（不因负值判成概率）",
                Math.Abs(negative.Sum() - 1.0) < 1e-6, string.Join(",", negative.Select(p => p.ToString("0.000"))));

            var huge = ClassifyPostProcess.NormalizeToProbabilities(new[] { 1000f, 999f });
            Check("数值稳定：大 logits（1000/999）softmax 不溢出、和为 1",
                huge.All(double.IsFinite) && Math.Abs(huge.Sum() - 1.0) < 1e-6,
                string.Join(",", huge.Select(p => p.ToString("0.000"))));

            // ---- TopK ----
            var probs = new[] { 0.1, 0.7, 0.2 };
            var top2 = ClassifyPostProcess.TopK(probs, new[] { "OK", "NG", "脏污" }, 2);
            Check("TopK：按概率降序取前 2 类（NG 0.7 / 脏污 0.2）",
                top2.Count == 2 && top2[0].ClassName == "NG" && Math.Abs(top2[0].Probability - 0.7) < 1e-6
                && top2[1].ClassName == "脏污",
                string.Join(" | ", top2.Select(s => s.ToString())));

            var noNames = ClassifyPostProcess.TopK(probs, null, 3);
            Check("模型没带类别名时用 class_N 占位，不编造名字",
                noNames.All(s => s.ClassName.StartsWith("class_")),
                string.Join(",", noNames.Select(s => s.ClassName)));

            var clampK = ClassifyPostProcess.TopK(probs, null, 99);
            Check("TopK 的 K 被夹到类别总数（99 → 3）", clampK.Count == 3, $"count={clampK.Count}");

            var zeroK = ClassifyPostProcess.TopK(probs, null, 0);
            Check("TopK 的 K 下界为 1（0 → 1）", zeroK.Count == 1, $"count={zeroK.Count}");

            // ---- 全量类别名（P1 回归）：必须与 Scores 同序，TopK<类别数也不许错名/占位混杂 ----
            var allNames = ClassifyPostProcess.ResolveClassNames(5, new[] { "OK", "NG", "scratch", "dent", "stain" });
            Check("【核心】ClassNames 与 Scores 同序且全为真实名（TopK<类别数也不许错名）",
                allNames.SequenceEqual(new[] { "OK", "NG", "scratch", "dent", "stain" }),
                string.Join(",", allNames));

            var partialNames = ClassifyPostProcess.ResolveClassNames(3, new[] { "OK" });
            Check("模型名字不全时：有的用真实名、缺的用 class_N 占位",
                partialNames[0] == "OK" && partialNames[1] == "class_1" && partialNames[2] == "class_2",
                string.Join(",", partialNames));

            var noModelNames = ClassifyPostProcess.ResolveClassNames(2, null);
            Check("模型没带名字 → 全 class_N 占位",
                noModelNames.SequenceEqual(new[] { "class_0", "class_1" }),
                string.Join(",", noModelNames));
        }

        // ==================================================================
        //  ② ONNX Runtime 依赖链路
        // ==================================================================

        private static void RunRuntimeChecks()
        {
            // ---- 入参守卫（不需要模型）----
            bool noPath = !ClassifySession.TryAcquire(null, out _, out string noPathErr);
            Check("未指定模型 → 明确报错（不抛异常）",
                noPath && noPathErr.Contains("尚未指定"), noPathErr);

            bool badPath = !ClassifySession.TryAcquire(@"C:\不存在的分类模型.onnx", out _, out string badPathErr);
            Check("模型路径不存在 → 明确报错且含「不存在」",
                badPath && badPathErr.Contains("不存在"), badPathErr);

            // ---- 真实模型（有则真跑，无则跳过）----
            string? model = ResolveTestModel();
            if (model == null)
            {
                Check("真实分类模型：会话加载 + 进程级缓存 + 引用计数释放", true,
                    "跳过：未找到分类模型（CLASSIFY_TEST_MODEL 未设置）");
                return;
            }

            int baseline = ClassifySession.CachedSessionCount;
            bool acquired = ClassifySession.TryAcquire(model, out var session, out string acquireErr);
            Check("加载真实分类模型成功", acquired && session != null, acquired ? "" : acquireErr);

            if (session != null)
            {
                var info = session.Info;
                Check("读到模型输入输出名字与形状",
                    !string.IsNullOrEmpty(info.InputName) && info.InputDimensions.Length > 0,
                    info.DescribeShapes());
                Check("分类模型元数据 task=classify（或缺失）",
                    string.IsNullOrEmpty(info.Task) || info.Task.Equals("classify", StringComparison.OrdinalIgnoreCase),
                    $"task={info.Task}");

                int afterFirst = ClassifySession.CachedSessionCount;
                ClassifySession.TryAcquire(model, out var second, out _);
                Check("同一模型第二次获取命中缓存（缓存数不变）",
                    ClassifySession.CachedSessionCount == afterFirst,
                    $"缓存会话数 {afterFirst} → {ClassifySession.CachedSessionCount}");

                second?.Dispose();
                session.Dispose();
            }

            Check("会话释放后引用计数归零、缓存回到基线",
                ClassifySession.CachedSessionCount == baseline,
                $"缓存会话数 {ClassifySession.CachedSessionCount}（应回到 {baseline}）");
        }

        // ==================================================================
        //  ③ 插件壳与端口契约
        // ==================================================================

        private static void RunPluginShellChecks()
        {
            // ---- 宿主可发现：GroupName 与扫描约定一致 ----
            var display = typeof(DlClassifyPlugin)
                .GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.DisplayAttribute), false)
                .Cast<System.ComponentModel.DataAnnotations.DisplayAttribute>()
                .FirstOrDefault();
            Check("插件带 [Display] 且归入「图像处理」组（否则宿主扫到也不会进算子列表）",
                display != null && display.GroupName == "图像处理" && !string.IsNullOrWhiteSpace(display.Name),
                display == null ? "没有 [Display]" : $"Name={display.Name} Group={display.GroupName}");

            // ---- 端口契约：只取本插件自己声明的输出端口 ----
            var plugin = new DlClassifyPlugin { InstanceName = "CLASSIFY_0" };
            string[] outputNames = plugin.GetType()
                .GetProperties(System.Reflection.BindingFlags.Public
                                | System.Reflection.BindingFlags.Instance
                                | System.Reflection.BindingFlags.DeclaredOnly)
                .Where(p => p.PropertyType.IsGenericType
                            && p.PropertyType.GetGenericTypeDefinition().Name.StartsWith("OutputPort"))
                .Select(p => p.Name)
                .OrderBy(n => n)
                .ToArray();
            Check("输出端口契约齐全（类别索引/名字/置信度/概率数组/类别名/文本摘要）",
                outputNames.SequenceEqual(new[] { "ClassNames", "Confidence", "PredictedClassId", "PredictedClassName", "Scores", "TopKText" }),
                string.Join(",", outputNames));

            // ---- 默认值（落盘契约）----
            Check("默认缩放模式 = Letterbox（不破坏长宽比的那一档）",
                plugin.ResizeMode == ClassifyResizeMode.Letterbox, plugin.ResizeMode.ToString());
            Check("默认 TopK = 3", plugin.TopK == 3, $"TopK={plugin.TopK}");

            // ---- partial 属性（源生成器）读写往返 ----
            plugin.ModelPath = "abc.onnx";
            Check("ModelPath 走源生成器 partial 属性（读写往返一致）",
                plugin.ModelPath == "abc.onnx", $"ModelPath={plugin.ModelPath}");

            // ---- 未选模型 → 明确失败，不抛异常 ----
            var image = MakeTestImage();
            plugin.ModelPath = "";
            plugin.Image.Value = image;
            Exception? boom = null;
            try { plugin.Execute(MakeContext(new StubLog())); } catch (Exception ex) { boom = ex; }

            Check("未选模型 → 报失败且原因指向配置（不抛异常）",
                boom == null && plugin.Success.Value is false
                && (plugin.ErrorMessage.Value as string ?? "").Contains("模型"),
                boom != null ? $"抛出异常：{boom.Message}" : $"Success={plugin.Success.Value} Error='{plugin.ErrorMessage.Value}'");

            // ---- 模型路径不存在 → 明确失败 ----
            plugin.ModelPath = @"C:\不存在的模型.onnx";
            plugin.Execute(MakeContext(new StubLog()));
            Check("模型路径不存在 → 报失败且原因含「不存在」",
                plugin.Success.Value is false
                && (plugin.ErrorMessage.Value as string ?? "").Contains("不存在"),
                $"Success={plugin.Success.Value} Error='{plugin.ErrorMessage.Value}'");

            plugin.Dispose();
            image.Dispose();
        }

        // ==================================================================
        //  辅助
        // ==================================================================

        private static HImage MakeTestImage()
        {
            HOperatorSet.GenImageConst(out HObject canvas, "byte", 64, 64);
            var image = new HImage(canvas);
            canvas.Dispose();
            return image;
        }

        private static ExecutionContext MakeContext(ILogService log) =>
            new(log, new FlowSession { FlowName = "深度学习分类断言" }, new WorkspaceContext(),
                new CancellationTokenSource().Token);

        /// <summary>分类测试模型路径（环境变量 CLASSIFY_TEST_MODEL）。没有则返回 null，调用方跳过而非判红。</summary>
        private static string? ResolveTestModel()
        {
            string? fromEnv = Environment.GetEnvironmentVariable("CLASSIFY_TEST_MODEL");
            return !string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv) ? fromEnv : null;
        }
    }
}
