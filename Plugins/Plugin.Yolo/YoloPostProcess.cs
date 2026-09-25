using System;
using System.Collections.Generic;
using System.Linq;

namespace Plugin.Yolo
{
    /// <summary>一次检测的结果（坐标已换算回**原图**像素）</summary>
    public sealed class Detection
    {
        /// <summary>类别索引（与模型元数据里的类别名一一对应）</summary>
        public int ClassId { get; init; }

        /// <summary>类别名。模型元数据没带类别名时，这里是 "class_3" 这样的占位名，不会假装知道</summary>
        public string ClassName { get; init; } = string.Empty;

        /// <summary>置信度 0~1</summary>
        public float Confidence { get; init; }

        /// <summary>左上角 X（原图像素）</summary>
        public float X1 { get; init; }

        public float Y1 { get; init; }

        /// <summary>右下角 X（原图像素）</summary>
        public float X2 { get; init; }

        public float Y2 { get; init; }

        public float Width => X2 - X1;

        public float Height => Y2 - Y1;

        public override string ToString() =>
            $"{ClassName}({Confidence:0.00}) [{X1:0},{Y1:0},{X2:0},{Y2:0}]";
    }

    /// <summary>
    /// letterbox 几何：把原图等比缩放后居中贴到方形画布上，四周补灰边。
    ///
    /// 【为什么必须做 letterbox，不能直接拉伸】
    /// 模型是在"等比缩放 + 补边"的图上训练的。若我们直接把图拉成 640×640，
    /// 目标的长宽比就被改变了 —— 模型见过的形状和现在喂进去的不一样，精度会**静默下降**：
    /// 不报错、不崩溃，只是漏检变多、框变歪。这类问题在现场极难定位，
    /// 所以预处理必须与训练一致，且这一步要能被单独验证。
    ///
    /// 【为什么坐标要换算回去】模型输出的是"letterbox 图上的像素坐标"。
    /// 直接把这种坐标当成原图坐标用，会整体偏移（偏移量就是补边宽度）并带上缩放误差。
    /// </summary>
    public sealed class LetterboxInfo
    {
        public required int SrcWidth { get; init; }
        public required int SrcHeight { get; init; }
        public required int DstWidth { get; init; }
        public required int DstHeight { get; init; }

        /// <summary>等比缩放系数（原图 → 缩放后）</summary>
        public required float Scale { get; init; }

        public required int ScaledWidth { get; init; }
        public required int ScaledHeight { get; init; }

        /// <summary>左侧补边宽度（像素）</summary>
        public required int PadX { get; init; }

        /// <summary>上侧补边高度（像素）</summary>
        public required int PadY { get; init; }

        /// <summary>补边灰度。114 是 YOLO 训练时的默认填充值 —— 换成 0 会让模型把黑边当成图像内容</summary>
        public const int PadGrayValue = 114;

        public static LetterboxInfo Compute(int srcWidth, int srcHeight, int dstWidth, int dstHeight)
        {
            float scale = Math.Min((float)dstWidth / srcWidth, (float)dstHeight / srcHeight);
            int scaledWidth = Math.Max(1, (int)Math.Round(srcWidth * scale));
            int scaledHeight = Math.Max(1, (int)Math.Round(srcHeight * scale));

            return new LetterboxInfo
            {
                SrcWidth = srcWidth,
                SrcHeight = srcHeight,
                DstWidth = dstWidth,
                DstHeight = dstHeight,
                Scale = scale,
                ScaledWidth = scaledWidth,
                ScaledHeight = scaledHeight,
                PadX = (dstWidth - scaledWidth) / 2,
                PadY = (dstHeight - scaledHeight) / 2,
            };
        }

        /// <summary>
        /// letterbox 图上的坐标 → 原图坐标，并夹进原图范围。
        /// 夹取是必要的：模型可能给出略微越界的框（尤其贴边目标），不夹会导致下游裁剪报错。
        /// </summary>
        public void MapBackToOriginal(ref float x1, ref float y1, ref float x2, ref float y2)
        {
            x1 = (x1 - PadX) / Scale;
            y1 = (y1 - PadY) / Scale;
            x2 = (x2 - PadX) / Scale;
            y2 = (y2 - PadY) / Scale;

            x1 = Math.Clamp(x1, 0f, SrcWidth - 1f);
            y1 = Math.Clamp(y1, 0f, SrcHeight - 1f);
            x2 = Math.Clamp(x2, 0f, SrcWidth - 1f);
            y2 = Math.Clamp(y2, 0f, SrcHeight - 1f);
        }
    }

    /// <summary>模型的输出张量布局。不同 YOLO 版本的布局不同，读错的表现是"结果全错但不报错"</summary>
    public enum YoloLayout
    {
        /// <summary>无法识别 —— 不猜，直接报错让用户确认</summary>
        Unknown,

        /// <summary>YOLOv8/v11 检测：<c>[1, 4+C, N]</c>，通道维在前、无 objectness</summary>
        V8Transposed,
    }

    /// <summary>
    /// 后处理：布局识别 → 解码 → NMS → 坐标换算。全部是**纯函数**，不依赖 ONNX Runtime，
    /// 所以可以脱离模型单独用受控张量验证 —— 这一层恰恰是最容易写错且不报错的地方。
    /// </summary>
    public static class YoloPostProcess
    {
        /// <summary>
        /// 识别输出张量的布局，并算出类别数。
        ///
        /// 【为什么必须校验而不是假定】YOLOv5 是 <c>[1, N, 5+C]</c>（带 objectness），
        /// v8/v11 是 <c>[1, 4+C, N]</c>（不带）。两者维度顺序**恰好相反**：
        /// 把 v8 的张量按 v5 解读，程序不会报任何错，只会输出一堆乱框。
        /// 判据是"哪个维度小"——通道数（几十~几百）必然远小于锚点数（几千~几万）。
        /// </summary>
        public static bool TrySniffLayout(int[] dims, out YoloLayout layout, out int classCount, out string error)
        {
            layout = YoloLayout.Unknown;
            classCount = 0;
            error = string.Empty;

            // 去掉 batch 维
            var shape = dims.Where(d => d > 0).ToArray();
            if (shape.Length == 3 && shape[0] == 1)
                shape = shape[1..];

            if (shape.Length != 2)
            {
                error = $"无法识别的输出形状 [{string.Join(",", dims.Select(d => d < 0 ? "N" : d.ToString()))}]："
                      + "期望检测模型的 [1, 4+C, N]（YOLOv8/v11）。"
                      + "若这是分割/姿态/分类模型，第一期不支持；请改用检测模型";
                return false;
            }

            int a = shape[0];
            int b = shape[1];

            // 通道维必然远小于锚点维。取小者为通道数。
            int channels = Math.Min(a, b);
            bool transposed = a < b; // 通道在前 = v8 风格

            if (!transposed)
            {
                error = $"输出形状 [{a},{b}] 看起来是 YOLOv5 风格的 [N, 5+C]（通道在后、带 objectness），"
                      + "第一期只支持 YOLOv8/v11 的 [4+C, N] 布局。"
                      + "若确实是 v5 模型，请用 v8/v11 重新导出，或联系开发补支持";
                return false;
            }

            classCount = channels - 4;
            if (classCount <= 0)
            {
                error = $"输出通道数 {channels} 不合理（应大于 4，形如 4+类别数）。"
                      + "请确认这是检测模型，且导出时没有裁剪输出";
                return false;
            }

            layout = YoloLayout.V8Transposed;
            return true;
        }

        /// <summary>
        /// 用模型元数据里的 <c>task</c> 字段判断这是不是检测模型。
        ///
        /// 【为什么光看输出形状不够 —— 这是实测出来的】
        /// YOLOv8-seg 的输出是 <c>[1, 116, 8400]</c>（116 = 4 + 80 类 + 32 个 mask 系数）。
        /// 在形状上它与检测模型**无法区分**：都是"通道在前、通道数远小于锚点数"。
        /// 于是形状校验会把它当成 112 类的检测模型放行，然后输出一堆垃圾框而**不报错** ——
        /// 正是本插件最想避免的那类静默错误。
        /// Ultralytics 导出时会把 task 写进元数据（detect / segment / pose / obb / classify），
        /// 所以这条路能拦下形状拦不住的。
        ///
        /// 元数据缺失时**放行**而不是拒绝：老模型或第三方导出的模型可能没写 task，
        /// 因为"没写"而拒绝会让本来可用的模型用不了。那种情况只能靠输出形状与类别数自洽性兜底。
        /// </summary>
        public static bool IsSupportedTask(string? task, out string error)
        {
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(task)) return true;

            string normalized = task.Trim().ToLowerInvariant();
            if (normalized == "detect") return true;

            string friendly = normalized switch
            {
                "segment" => "实例分割",
                "pose" => "姿态/关键点",
                "obb" => "旋转框",
                "classify" => "图像分类",
                "semantic" => "语义分割",
                "depth" => "深度估计",
                _ => task.Trim(),
            };

            error = $"这个模型的任务类型是「{friendly}」（元数据 task={task.Trim()}），"
                  + "而本节点第一期只做**目标检测**。不同任务的输出格式完全不同，"
                  + "按检测解读会得到错误结果却不报错，所以这里直接拒绝。"
                  + "请改用 detect 任务导出模型：yolo export model=你的模型.pt format=onnx";
            return false;
        }

        /// <summary>
        /// 解码 YOLOv8/v11 检测输出。<paramref name="data"/> 是展平的 <c>[4+C, N]</c>（行主序，通道优先）。
        /// 输出的框坐标在 **letterbox 图**的像素坐标系里，还需要 <see cref="LetterboxInfo.MapBackToOriginal"/> 换算。
        /// </summary>
        public static List<Detection> DecodeV8Transposed(
            float[] data,
            int classCount,
            int anchorCount,
            float confidenceThreshold,
            string[]? classNames
        )
        {
            var result = new List<Detection>();
            int channels = classCount + 4;

            if (data.Length < (long)channels * anchorCount) return result;

            for (int i = 0; i < anchorCount; i++)
            {
                // 先找该锚点的最高分类得分；低于阈值直接跳过，省掉后面的框计算
                int bestClass = -1;
                float bestScore = 0f;
                for (int c = 0; c < classCount; c++)
                {
                    float score = data[(4 + c) * anchorCount + i];
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestClass = c;
                    }
                }

                if (bestClass < 0 || bestScore < confidenceThreshold) continue;

                // YOLOv8 的导出图里已包含解码（含 DFL），输出即"输入尺寸下的中心点 + 宽高"像素值，
                // 不需要再做 sigmoid / exp / 锚点换算 —— 多做一步会得到完全错误的框。
                float cx = data[0 * anchorCount + i];
                float cy = data[1 * anchorCount + i];
                float w = data[2 * anchorCount + i];
                float h = data[3 * anchorCount + i];

                result.Add(new Detection
                {
                    ClassId = bestClass,
                    ClassName = NameOf(classNames, bestClass),
                    Confidence = bestScore,
                    X1 = cx - w / 2f,
                    Y1 = cy - h / 2f,
                    X2 = cx + w / 2f,
                    Y2 = cy + h / 2f,
                });
            }

            return result;
        }

        /// <summary>
        /// 非极大值抑制。**按类别分别做**（YOLO 的标准做法）：
        /// 一个"人"和一个"自行车"框高度重叠是正常的（人骑着车），不该互相压制；
        /// 只有同类别的重叠框才需要去重。
        /// </summary>
        public static List<Detection> Nms(IReadOnlyList<Detection> detections, float iouThreshold)
        {
            var kept = new List<Detection>();

            foreach (var group in detections.GroupBy(d => d.ClassId))
            {
                var sorted = group.OrderByDescending(d => d.Confidence).ToList();

                while (sorted.Count > 0)
                {
                    var best = sorted[0];
                    kept.Add(best);
                    sorted.RemoveAt(0);

                    sorted.RemoveAll(other => Iou(best, other) > iouThreshold);
                }
            }

            return kept.OrderByDescending(d => d.Confidence).ToList();
        }

        /// <summary>交并比</summary>
        public static float Iou(Detection a, Detection b)
        {
            float interLeft = Math.Max(a.X1, b.X1);
            float interTop = Math.Max(a.Y1, b.Y1);
            float interRight = Math.Min(a.X2, b.X2);
            float interBottom = Math.Min(a.Y2, b.Y2);

            float interWidth = interRight - interLeft;
            float interHeight = interBottom - interTop;
            if (interWidth <= 0 || interHeight <= 0) return 0f;

            float interArea = interWidth * interHeight;
            float union = a.Width * a.Height + b.Width * b.Height - interArea;
            return union <= 0 ? 0f : interArea / union;
        }

        /// <summary>
        /// 类别名：元数据里读到了就用，读不到用 "class_N" 占位。
        /// **不编造名字**（比如按 COCO 顺序硬套）—— 那会让用户以为识别对了，
        /// 而实际上模型的类别体系可能完全不同。
        /// </summary>
        private static string NameOf(string[]? names, int classId) =>
            names != null && classId >= 0 && classId < names.Length && names[classId].Length > 0
                ? names[classId]
                : $"class_{classId}";
    }
}
