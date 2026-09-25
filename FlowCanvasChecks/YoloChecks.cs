using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Core.Interfaces;
using HalconDotNet;
using Plugin.Yolo;
using VisionMaster.Models;
using VisionMaster.Services;
using static FlowCanvasChecks.Program;
using ExecutionContext = VisionMaster.Services.ExecutionContext;

namespace FlowCanvasChecks
{
    /// <summary>
    /// YOLO 插件（Plugin.Yolo）的断言。分三段，各自独立可跑：
    ///
    ///   ① 后处理逻辑（受控张量，不需要模型）—— 精确、可重复，是最该测的一层
    ///   ② ONNX Runtime 依赖链路（需要任一 ONNX 模型）
    ///   ③ 真实模型的守卫栏（需要任一 ONNX 模型）—— 证明"布局不认识就报错、不猜"
    ///
    /// 为什么①是重点
    /// ---------
    /// 解码 / NMS / 坐标换算这三步写错的后果是**结果全错但程序不报错**：
    /// 框整体偏移、该留的被压掉、类别错位。这类错误无法靠"跑起来没崩"发现，
    /// 只能靠"已知输入 → 已知期望输出"来钉。用受控张量恰好能做到这点：
    /// 期望值是我们自己植进去的，模型精度不掺和进来。
    /// </summary>
    internal static class YoloChecks
    {
        public static void Run()
        {
            Section("[YOLO] 后处理：letterbox / 布局校验 / 解码 / NMS / 坐标换算");
            RunPostProcessChecks();

            Section("[YOLO] ONNX Runtime 依赖链路与真实模型");
            RunRuntimeChecks();

            Section("[YOLO] 最佳目标裁剪（跨接线断层的关键）与插件壳");
            RunCropChecks();
            RunPluginShellChecks();
        }

        // ==================================================================
        //  裁剪：下游裁剪算子只吃参数、不吃端口，"裁好再输出"是唯一的解法
        // ==================================================================

        private static void RunCropChecks()
        {
            // 受控图：200×100 全黑，在 行10..19 / 列30..49 涂一块白。
            // 这样"裁到了哪里"可以通过"裁出来的图里有没有白"来验证 —— 比只看尺寸更能证明位置对。
            HOperatorSet.GenImageConst(out HObject canvas, "byte", 200, 100);
            HOperatorSet.GenRectangle1(out HObject whiteRect, 10, 30, 19, 49);
            HOperatorSet.PaintRegion(whiteRect, canvas, out HObject painted, 255, "fill");
            var testImage = new HImage(painted);
            whiteRect.Dispose();
            painted.Dispose();
            canvas.Dispose();

            // 框正好覆盖白块：x1=30 y1=10 x2=50 y2=20 → 应为 20×10 且含白
            var onTarget = new Detection { ClassId = 0, ClassName = "T", Confidence = 0.9f, X1 = 30, Y1 = 10, X2 = 50, Y2 = 20 };
            var cropped = YoloCrop.CropBest(testImage, new[] { onTarget }, 0, out string note);
            Check("裁剪：尺寸按框的宽高算出（20×10）",
                cropped != null && IsSize(cropped, 20, 10),
                cropped == null ? "未裁出" : $"{SizeOf(cropped)}{note}");

            Check("【核心】裁剪：裁到的是框所指的位置（白块在裁剪图内）",
                cropped != null && MaxGray(cropped) > 200,
                cropped == null ? "未裁出" : $"最大灰度 {MaxGray(cropped)}（应接近 255）");
            cropped?.Dispose();

            // 框在别处（图左上角，那里是黑的）→ 尺寸同样是 20×10，但内容应全黑。
            // 这一条证明"位置"是对的：只测尺寸的话，把行列写反也能过。
            var offTarget = new Detection { ClassId = 0, ClassName = "T", Confidence = 0.9f, X1 = 0, Y1 = 0, X2 = 20, Y2 = 10 };
            var blankCrop = YoloCrop.CropBest(testImage, new[] { offTarget }, 0, out _);
            Check("【核心】裁剪：框外的区域裁出来是全黑（证明行列没写反）",
                blankCrop != null && IsSize(blankCrop, 20, 10) && MaxGray(blankCrop) == 0,
                blankCrop == null ? "未裁出" : $"{SizeOf(blankCrop)} 最大灰度 {MaxGray(blankCrop)}（应为 0）");
            blankCrop?.Dispose();

            // 边距：+10 像素 → 尺寸各边扩 10。框在图内，**不应**发生越界夹取（note 应为空）。
            // 注意别把"没有夹取"误判成"夹取没留痕" —— 这两种情况的区别正是这条断言要钉的。
            var withMargin = YoloCrop.CropBest(testImage, new[] { onTarget }, 10, out string marginNote);
            Check("裁剪：加 10 像素边距后尺寸变大（40×30），且框仍在图内时不该出现夹取提示",
                withMargin != null && IsSize(withMargin, 40, 30) && marginNote.Length == 0,
                withMargin == null ? "未裁出" : $"{SizeOf(withMargin)} note='{marginNote}'");
            withMargin?.Dispose();

            // 边距把框推出图外（行 -30、列 20 起）→ 必须夹取并留痕。
            // 这条与上一条配对：一条证明"没越界时不乱报"，一条证明"越界时必须报"。
            var marginOut = YoloCrop.CropBest(testImage, new[] { onTarget }, 40, out string marginOutNote);
            Check("【核心】边距把框推出图像时被夹取并留痕（不越界不报、越界必报）",
                marginOut != null && marginOutNote.Contains("超出图像范围"),
                marginOut == null ? "未裁出" : $"{SizeOf(marginOut)} note='{marginOutNote}'");
            marginOut?.Dispose();

            // 严重越界：框远大于图像 → 夹到整幅图，且必须给出提示
            var huge = new Detection { ClassId = 0, ClassName = "T", Confidence = 0.9f, X1 = -50, Y1 = -50, X2 = 9999, Y2 = 9999 };
            var hugeCrop = YoloCrop.CropBest(testImage, new[] { huge }, 0, out string hugeNote);
            Check("【核心】越界框被夹进图像范围（crop_part 越界不报错，必须自己夹）",
                hugeCrop != null && IsSize(hugeCrop, 200, 100) && hugeNote.Contains("超出图像范围"),
                hugeCrop == null ? "未裁出" : $"{SizeOf(hugeCrop)} note='{hugeNote}'");
            hugeCrop?.Dispose();

            // 没检测到目标 → 不裁，返回 null（下游会拿到空而不是一张莫名其妙的图）
            var none = YoloCrop.CropBest(testImage, Array.Empty<Detection>(), 0, out _);
            Check("没检测到目标时不做裁剪（返回空而不是随便裁一块）", none == null, none == null ? "" : "居然裁出了图");

            testImage.Dispose();
        }

        private static string SizeOf(HImage image)
        {
            HOperatorSet.GetImageSize(image, out HTuple w, out HTuple h);
            return $"{w.I}×{h.I}";
        }

        private static bool IsSize(HImage image, int width, int height)
        {
            HOperatorSet.GetImageSize(image, out HTuple w, out HTuple h);
            return w.I == width && h.I == height;
        }

        private static double MaxGray(HImage image)
        {
            HOperatorSet.MinMaxGray(image, image, 0, out _, out HTuple max, out _);
            return max.D;
        }

        // ==================================================================
        //  插件壳
        // ==================================================================

        private static void RunPluginShellChecks()
        {
            // ---- 插件能被宿主发现：GroupName 必须与宿主扫描约定一致 ----
            var display = typeof(YoloPlugin)
                .GetCustomAttributes(typeof(System.ComponentModel.DataAnnotations.DisplayAttribute), false)
                .Cast<System.ComponentModel.DataAnnotations.DisplayAttribute>()
                .FirstOrDefault();
            Check("插件带 [Display] 且归入「图像处理」组（否则宿主扫到也不会进算子列表）",
                display != null && display.GroupName == "图像处理" && !string.IsNullOrWhiteSpace(display.Name),
                display == null ? "没有 [Display]" : $"Name={display.Name} Group={display.GroupName}");

            // ---- 端口契约：名字与形态就是图纸里存的东西，改不得 ----
            // 只取本插件自己声明的端口（DeclaredOnly）：基类的 Success / ErrorMessage 也是 OutputPort，
            // 不排除掉会混进来，让"契约有没有被改动"这件事看不清楚。
            var plugin = new YoloPlugin { InstanceName = "YOLO_0" };
            string[] outputNames = plugin.GetType()
                .GetProperties(System.Reflection.BindingFlags.Public
                                | System.Reflection.BindingFlags.Instance
                                | System.Reflection.BindingFlags.DeclaredOnly)
                .Where(p => p.PropertyType.IsGenericType
                            && p.PropertyType.GetGenericTypeDefinition().Name.StartsWith("OutputPort"))
                .Select(p => p.Name)
                .OrderBy(n => n)
                .ToArray();
            Check("输出端口契约齐全（数组 + 文本 + 裁剪图）",
                outputNames.SequenceEqual(new[] { "BestCrop", "Boxes", "BoxesText", "ClassIds", "ClassNames", "Confidences", "Count" }),
                string.Join(",", outputNames));

            // ---- 未选模型 → 明确失败，不抛异常 ----
            var image = MakeCropTestImage();
            var log = new StubLog();
            plugin.Image.Value = image;
            Exception? boom = null;
            try { plugin.Execute(MakeContext(log)); } catch (Exception ex) { boom = ex; }

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

            // ---- 类别过滤填错 → 明确失败，不静默当成"不过滤" ----
            plugin.ModelPath = ResolveTestModel() ?? @"C:\不存在的模型.onnx";
            plugin.ClassFilterText = "0,abc";
            plugin.Execute(MakeContext(new StubLog()));
            Check("类别过滤填了非法值 → 报失败并说明该填什么（不静默忽略）",
                plugin.Success.Value is false
                && (plugin.ErrorMessage.Value as string ?? "").Contains("类别索引"),
                $"Success={plugin.Success.Value} Error='{plugin.ErrorMessage.Value}'");

            plugin.Dispose();
            image.Dispose();

            // ---- 模型路径改变要换会话（否则"改了配置不生效"，最难查的那类问题）----
            string? model = ResolveTestModel();
            if (model == null)
            {
                Check("会话随模型路径切换（需要 ONNX 模型）", true, "跳过：未找到测试模型");
                return;
            }

            int baseline = YoloSession.CachedSessionCount;
            var p1 = new YoloPlugin { InstanceName = "Y1", ModelPath = model, ClassFilterText = "" };
            var img1 = MakeCropTestImage();
            p1.Image.Value = img1;
            p1.Execute(MakeContext(new StubLog()));

            // 这个模型是 v2 网格布局，会被守卫栏拦下 —— 但**会话应当已经建立并缓存在进程级**
            Check("会话进入进程级缓存（供其它流程复用同一份模型）",
                YoloSession.CachedSessionCount > baseline,
                $"缓存会话数 {baseline} → {YoloSession.CachedSessionCount}");

            p1.Dispose();
            Check("插件 Dispose 后会话引用计数归零、缓存释放",
                YoloSession.CachedSessionCount == baseline,
                $"缓存会话数 {YoloSession.CachedSessionCount}（应回到 {baseline}）");

            img1.Dispose();
        }

        /// <summary>造一张小图（比用真实图更快、也不依赖外部资源）</summary>
        private static HImage MakeCropTestImage()
        {
            HOperatorSet.GenImageConst(out HObject canvas, "byte", 200, 100);
            var image = new HImage(canvas);
            canvas.Dispose();
            return image;
        }

        private static ExecutionContext MakeContext(ILogService log) =>
            new(log, new FlowSession { FlowName = "YOLO 插件断言" }, new WorkspaceContext(),
                new CancellationTokenSource().Token);

        // ==================================================================
        //  ① 后处理逻辑（受控张量）
        // ==================================================================

        private static void RunPostProcessChecks()
        {
            // ---- letterbox 几何：宽图与高图两种情形 ----
            // 宽图（200×100 → 100×100）：受宽度限制，上下补边
            var wide = LetterboxInfo.Compute(200, 100, 100, 100);
            Check("letterbox（宽图）：缩放系数与补边正确",
                Math.Abs(wide.Scale - 0.5f) < 1e-6 && wide.ScaledWidth == 100 && wide.ScaledHeight == 50
                && wide.PadX == 0 && wide.PadY == 25,
                $"scale={wide.Scale} scaled={wide.ScaledWidth}×{wide.ScaledHeight} pad=({wide.PadX},{wide.PadY})");

            // 高图（100×200 → 100×100）：受高度限制，左右补边
            var tall = LetterboxInfo.Compute(100, 200, 100, 100);
            Check("letterbox（高图）：缩放系数与补边正确",
                Math.Abs(tall.Scale - 0.5f) < 1e-6 && tall.ScaledWidth == 50 && tall.ScaledHeight == 100
                && tall.PadX == 25 && tall.PadY == 0,
                $"scale={tall.Scale} scaled={tall.ScaledWidth}×{tall.ScaledHeight} pad=({tall.PadX},{tall.PadY})");

            // ---- 坐标换算：letterbox 坐标 → 原图坐标 ----
            // 这是最容易出错的静默 bug：忘了减补边，所有框会整体偏移一个 pad。
            float mx1 = 40, my1 = 40, mx2 = 60, my2 = 60;
            wide.MapBackToOriginal(ref mx1, ref my1, ref mx2, ref my2);
            Check("【核心】坐标换算回原图：减去补边再除以缩放系数",
                Math.Abs(mx1 - 80) < 0.01 && Math.Abs(my1 - 30) < 0.01
                && Math.Abs(mx2 - 120) < 0.01 && Math.Abs(my2 - 70) < 0.01,
                $"letterbox(40,40,60,60) → 原图({mx1:0.#},{my1:0.#},{mx2:0.#},{my2:0.#})，期望 (80,30,120,70)");

            // ---- 越界夹取：模型给出越界框时不能把越界坐标透给下游 ----
            float ox1 = -50, oy1 = -50, ox2 = 9999, oy2 = 9999;
            wide.MapBackToOriginal(ref ox1, ref oy1, ref ox2, ref oy2);
            Check("越界框被夹进原图范围（下游裁剪才不会拿到负坐标）",
                ox1 >= 0 && oy1 >= 0 && ox2 <= 199 && oy2 <= 99,
                $"→ ({ox1:0.#},{oy1:0.#},{ox2:0.#},{oy2:0.#})，应落在 [0,200)×[0,100) 内");

            // ---- 布局校验：v8 认、v5 拒、非检测布局拒 ----
            bool v8Ok = YoloPostProcess.TrySniffLayout(new[] { 1, 84, 8400 }, out var layout, out int cls, out _);
            Check("布局校验：YOLOv8 的 [1,84,8400] 被识别（80 类）",
                v8Ok && layout == YoloLayout.V8Transposed && cls == 80,
                v8Ok ? $"layout={layout} 类别数={cls}" : "未识别");

            bool v5Rejected = !YoloPostProcess.TrySniffLayout(new[] { 1, 25200, 85 }, out _, out _, out string v5Error);
            Check("【核心】YOLOv5 风格的 [1,25200,85] 被明确拒绝（不按 v8 硬解）",
                v5Rejected && v5Error.Contains("v5"), v5Error);

            // ---- 形状拦不住的那类模型：靠 task 元数据兜底 ----
            // 分割模型输出 [1,116,8400]（116 = 4 + 80 类 + 32 个 mask 系数）。
            // 在**形状上**它与检测模型无法区分：都是"通道在前、通道数远小于锚点数"。
            // 所以形状校验会把它当成 112 类的检测模型放行 —— 这是实测确认的已知歧义，
            // 必须由 task 元数据兜底（见下一条）。
            bool shapeCannotTell = YoloPostProcess.TrySniffLayout(
                new[] { 1, 116, 8400 }, out _, out int segClassGuess, out _);
            Check("已知歧义：分割模型的形状在几何上与检测无法区分（会被误读成 112 类）",
                shapeCannotTell && segClassGuess == 112,
                $"形状校验只能给出 {segClassGuess} 类 —— 这正是必须靠 task 元数据兜底的原因");

            bool taskGuard = !YoloPostProcess.IsSupportedTask("segment", out string taskError);
            Check("【核心】task 元数据守卫：segment 模型被明确拒绝（形状拦不住的它能拦）",
                taskGuard && taskError.Contains("detect"), taskError);

            bool poseGuard = !YoloPostProcess.IsSupportedTask("pose", out _);
            bool detectPass = YoloPostProcess.IsSupportedTask("detect", out _);
            bool noMetaPass = YoloPostProcess.IsSupportedTask("", out _);
            Check("task 守卫：pose 也拒；detect 放行、元数据缺失时放行（不因缺元数据就拒绝可用模型）",
                poseGuard && detectPass && noMetaPass,
                $"pose拒={poseGuard} detect放行={detectPass} 无元数据放行={noMetaPass}");

            // ---- 解码：植 3 个已知目标（含 1 对重叠同类 + 1 个异类同框）----
            const int classCount = 3;
            const int anchorCount = 4;
            var tensor = BuildSyntheticV8Tensor(classCount, anchorCount);

            var decoded = YoloPostProcess.DecodeV8Transposed(tensor, classCount, anchorCount, 0.25f,
                new[] { "缺陷", "异物", "划伤" });
            Check("解码：植 3 个目标解出 3 个（第 4 个锚点分数低于阈值被丢）",
                decoded.Count == 3, $"解出 {decoded.Count} 个：{string.Join(" | ", decoded)}");

            var first = decoded.FirstOrDefault(d => d.ClassId == 2);
            Check("解码：类别索引 / 类别名 / 置信度正确",
                first != null && first.ClassName == "划伤" && Math.Abs(first.Confidence - 0.90f) < 0.001,
                first == null ? "没解出类别 2" : $"{first.ClassName} conf={first.Confidence:0.00}");

            Check("解码：框按 cxcywh 还原为左上右下（letterbox 坐标系）",
                first != null && Math.Abs(first.X1 - 40) < 0.01 && Math.Abs(first.Y1 - 40) < 0.01
                && Math.Abs(first.X2 - 60) < 0.01 && Math.Abs(first.Y2 - 60) < 0.01,
                first == null ? "无" : $"({first.X1:0.#},{first.Y1:0.#},{first.X2:0.#},{first.Y2:0.#})，期望 (40,40,60,60)");

            // ---- NMS：同类压掉重叠的、异类同框必须保留 ----
            var afterNms = YoloPostProcess.Nms(decoded, 0.45f);
            Check("【核心】NMS 同类去重：2 个重叠的「划伤」只留置信度高的 1 个",
                afterNms.Count(d => d.ClassId == 2) == 1
                && afterNms.First(d => d.ClassId == 2).Confidence > 0.89f,
                string.Join(" | ", afterNms.Where(d => d.ClassId == 2)));
            Check("【核心】NMS 按类别分别做：「异物」与「划伤」框完全重叠也互不压制",
                afterNms.Any(d => d.ClassId == 0) && afterNms.Any(d => d.ClassId == 2),
                $"结果类别：{string.Join(",", afterNms.Select(d => d.ClassName))}");

            // ---- 端到端（纯后处理）：解码 → 换算 → NMS → 原图坐标 ----
            var mapped = afterNms.Select(d =>
            {
                float x1 = d.X1, y1 = d.Y1, x2 = d.X2, y2 = d.Y2;
                wide.MapBackToOriginal(ref x1, ref y1, ref x2, ref y2);
                return new Detection
                {
                    ClassId = d.ClassId,
                    ClassName = d.ClassName,
                    Confidence = d.Confidence,
                    X1 = x1,
                    Y1 = y1,
                    X2 = x2,
                    Y2 = y2,
                };
            }).ToList();

            var top = mapped[0];
            Check("【核心】端到端（受控张量）：最终框落在原图正确位置",
                Math.Abs(top.X1 - 80) < 0.01 && Math.Abs(top.Y1 - 30) < 0.01
                && Math.Abs(top.X2 - 120) < 0.01 && Math.Abs(top.Y2 - 70) < 0.01,
                $"最高置信目标：{top}");

            // ---- 演示"输出用法"：这些就是端口上真实会拿到的值 ----
            Check("输出用法演示（端口值形态）", true, DescribePorts(mapped, wide));

            // ---- 类别名缺失时不编造 ----
            var noNames = YoloPostProcess.DecodeV8Transposed(tensor, classCount, anchorCount, 0.25f, null);
            Check("模型没带类别名时用 class_N 占位，不编造名字",
                noNames.All(d => d.ClassName.StartsWith("class_")),
                string.Join(",", noNames.Select(d => d.ClassName).Distinct()));

            // ---- 空输入与全低分：不崩、返回空 ----
            var empty = YoloPostProcess.DecodeV8Transposed(new float[classCount * anchorCount], classCount, anchorCount, 0.25f, null);
            Check("全零张量 → 解出 0 个（不崩）", empty.Count == 0, $"解出 {empty.Count} 个");
        }

        /// <summary>
        /// 造一个 [4+C, N] 的受控张量（通道优先，与 ONNX 输出展平后一致）。
        /// 植三个目标：
        ///   锚点0：类别2 置信 0.90，框中心(50,50) 20×20
        ///   锚点1：类别2 置信 0.80，框中心(52,52) 20×20  ← 与锚点0 同类高度重叠，应被 NMS 压掉
        ///   锚点2：类别0 置信 0.70，框中心(50,50) 20×20  ← 与锚点0 异类同框，必须保留
        ///   锚点3：全低分，应被阈值丢弃
        /// </summary>
        private static float[] BuildSyntheticV8Tensor(int classCount, int anchorCount)
        {
            int channels = classCount + 4;
            var data = new float[channels * anchorCount];

            void Plant(int anchor, int classId, float confidence, float cx, float cy, float w, float h)
            {
                data[0 * anchorCount + anchor] = cx;
                data[1 * anchorCount + anchor] = cy;
                data[2 * anchorCount + anchor] = w;
                data[3 * anchorCount + anchor] = h;
                data[(4 + classId) * anchorCount + anchor] = confidence;
            }

            Plant(0, 2, 0.90f, 50, 50, 20, 20);
            Plant(1, 2, 0.80f, 52, 52, 20, 20);
            Plant(2, 0, 0.70f, 50, 50, 20, 20);
            // 锚点3 留空（全 0）

            return data;
        }

        /// <summary>把检测结果格式化成端口值的形态 —— 这就是下游节点真正会收到的东西</summary>
        private static string DescribePorts(IReadOnlyList<Detection> detections, LetterboxInfo letterbox)
        {
            string classIds = string.Join(",", detections.Select(d => d.ClassId));
            string classNames = string.Join(",", detections.Select(d => d.ClassName));
            string confidences = string.Join(",", detections.Select(d => d.Confidence.ToString("0.000")));
            string boxes = string.Join(",", detections.Select(d =>
                $"{d.X1:0.#},{d.Y1:0.#},{d.X2:0.#},{d.Y2:0.#}"));
            string text = string.Join(";", detections.Select(d =>
                $"{d.ClassName}:{d.Confidence:0.00}@[{d.X1:0.#},{d.Y1:0.#},{d.X2:0.#},{d.Y2:0.#}]"));

            return $"\r\n      Count={detections.Count}"
                 + $"\r\n      ClassIds={classIds}"
                 + $"\r\n      ClassNames={classNames}"
                 + $"\r\n      Confidences={confidences}"
                 + $"\r\n      Boxes={boxes}（每 4 个一组 x1,y1,x2,y2）"
                 + $"\r\n      BoxesText={text}"
                 + $"\r\n      letterbox: scale={letterbox.Scale} pad=({letterbox.PadX},{letterbox.PadY})";
        }

        // ==================================================================
        //  ②③ 真实模型：运行链路 + 守卫栏
        // ==================================================================

        private static void RunRuntimeChecks()
        {
            string? modelPath = ResolveTestModel();
            if (modelPath == null)
            {
                Check("ONNX Runtime 依赖链路（需要任一 ONNX 模型）", true,
                    "跳过：未找到测试模型。设置环境变量 YOLO_TEST_MODEL 指向任意 .onnx 即可启用本组断言");
                return;
            }

            Check("找到测试模型", true, $"{Path.GetFileName(modelPath)}  {new FileInfo(modelPath).Length / 1024 / 1024} MB");

            bool acquired = YoloSession.TryAcquire(modelPath, out var session, out string error);
            Check("【核心】ONNX Runtime 原生库可加载、会话可创建（验证依赖投递链路）",
                acquired, acquired ? session!.Info.DescribeShapes() : error);

            if (!acquired || session == null)
            {
                Check("后续断言（共享/引用计数/守卫栏）", true, "上一测未通过，跳过");
                return;
            }

            using (session)
            {
                var info = session.Info;
                Check("读到了输入/输出名字与形状",
                    !string.IsNullOrEmpty(info.InputName) && info.InputDimensions.Length > 0,
                    info.DescribeShapes());
                Check("模型元数据可读（类别名 / 任务类型；缺失不算失败，说明该模型未写元数据）", true,
                    $"task='{info.Task}' 类别数={info.ClassNames.Length}");

                // ---- 会话共享与引用计数 ----
                int before = YoloSession.CachedSessionCount;
                YoloSession.TryAcquire(modelPath, out var second, out _);
                Check("【核心】同一模型第二次获取命中缓存，未重复加载",
                    YoloSession.CachedSessionCount == before,
                    $"缓存会话数 {before} → {YoloSession.CachedSessionCount}（应保持不变）");
                second?.Dispose();

                // ---- 真实推理：跑通预处理 → 推理 → 拿到输出张量 ----
                // 只跑一次：结果既用于"链路是否通"，也用于守卫栏断言（跑两遍是白浪费一次推理）
                int[] realDims = RunRealInference(modelPath, session);

                // ---- 守卫栏：这个模型不是 v8 检测布局，必须被明确拒绝 ----
                // 这条是"不静默猜"原则在真实非目标模型上的验证：如果守卫栏失效，
                // 我们会拿一个 v2 网格张量按 v8 解读，输出一堆"看着像框"的垃圾而不报错。
                // 形状取自模型元数据（不必多跑一次推理 —— 元数据里就有声明形状）。
                if (realDims.Length > 0)
                {
                    bool rejected = !YoloPostProcess.TrySniffLayout(
                        realDims, out _, out _, out string sniffError);
                    Check("【核心】真实非目标模型（v2 网格布局）被守卫栏明确拒绝，且原因可读",
                        rejected && sniffError.Length > 0,
                        rejected ? sniffError.Replace("\r\n", " ") : "居然通过了校验（说明布局校验失效）");
                }
            }

            bool bad = YoloSession.TryAcquire(@"C:\不存在的模型.onnx", out _, out string badError);
            Check("模型不存在 → 明确报错而不是静默成功",
                !bad && badError.Contains("不存在"), bad ? "居然返回了成功" : badError);
        }

        /// <summary>真实推理一次，返回模型声明的输出形状（供守卫栏断言复用）</summary>
        private static int[] RunRealInference(string modelPath, YoloSession session)
        {
            string? imagePath = ResolveTestImage();
            if (imagePath == null)
            {
                Check("真实推理链路（需要一张测试图）", true, "跳过：未找到测试图");
                return session.Info.OutputDimensions;
            }

            HOperatorSet.ReadImage(out HObject raw, imagePath);
            var image = new HImage(raw);
            raw?.Dispose();

            try
            {
                var engine = new YoloEngine(session);
                bool ok = engine.TryDetect(image, new YoloParams { InputSize = 416 }, out var result, out string error);

                // 这个模型是 v2 网格布局，必然被守卫栏拦下 —— 所以"失败"才是预期结果，
                // 关键看错误信息是否说清了原因（而不是一句空泛的"识别失败"）。
                Check("真实推理链路跑通：预处理（letterbox）→ 推理 → 拿到输出张量",
                    !ok && error.Contains("输出形状"),
                    ok ? $"居然解出了 {result!.Count} 个目标（布局校验可能失效）"
                       : error.Replace("\r\n", " "));

                return session.Info.OutputDimensions;
            }
            catch (Exception ex)
            {
                Check("真实推理链路", false, $"抛出异常：{ex.GetType().Name}: {ex.Message}");
                return Array.Empty<int>();
            }
            finally
            {
                image.Dispose();
            }
        }

        private static string? ResolveTestImage()
        {
            string? fromEnv = Environment.GetEnvironmentVariable("YOLO_TEST_IMAGE");
            if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv)) return fromEnv;

            string? root = Environment.GetEnvironmentVariable("HALCONROOT");
            if (!string.IsNullOrWhiteSpace(root))
            {
                string p = Path.Combine(root!, "examples", "images", "datacode", "ecc200", "ecc200_cpu_001.png");
                if (File.Exists(p)) return p;
            }

            string local = @"e:\VM\VisionMaster-W-master\Plugins\Plugin.ImageScript\ScriptAssets\images\pcb_color.png";
            return File.Exists(local) ? local : null;
        }

        /// <summary>找测试模型：环境变量优先，再退回开发期已知的临时位置；都找不到返回 null</summary>
        private static string? ResolveTestModel()
        {
            string? fromEnv = Environment.GetEnvironmentVariable("YOLO_TEST_MODEL");
            if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv)) return fromEnv;

            string temp = Path.GetTempPath();
            foreach (var candidate in new[]
            {
                Path.Combine(temp, "dlprobe", "out", "tinyyolov2.onnx"),
                Path.Combine(temp, "dlprobe", "out", "roundtrip.onnx"),
            })
            {
                if (File.Exists(candidate)) return candidate;
            }

            return null;
        }
    }
}
