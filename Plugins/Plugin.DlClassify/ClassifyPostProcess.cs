using System;
using System.Collections.Generic;
using System.Linq;

namespace Plugin.DlClassify
{
    /// <summary>
    /// 预处理缩放模式。
    ///
    /// 【为什么要有这个开关】分类模型的训练预处理有两种主流做法，二者在"非方形图"上结果不同：
    ///   · Letterbox（等比缩放 + 补边，与 Plugin.Yolo 同款）—— 不改变长宽比，最稳；
    ///   · Stretch（直接拉成方形）—— 部分自训练/老导出用这种，图上目标会被压扁。
    /// 选错的表现是**精度静默下降**（不报错、不崩溃，只是分类置信度偏低或偶发错分），
    /// 这类问题在现场极难定位，所以把选择权显式交给用户，而不是替它猜。
    /// 默认 Letterbox：它是"不破坏几何"的那一档，出错面最小。
    /// </summary>
    public enum ClassifyResizeMode
    {
        /// <summary>等比缩放 + 居中补 114 灰边（不改变长宽比）</summary>
        Letterbox,

        /// <summary>直接拉伸到目标尺寸（改变长宽比）</summary>
        Stretch,
    }

    /// <summary>一个类别的得分（类别索引 + 名字 + 概率 0~1）</summary>
    public sealed class ClassScore
    {
        public int ClassId { get; init; }
        public string ClassName { get; init; } = string.Empty;
        public double Probability { get; init; }

        public override string ToString() => $"{ClassName} {Probability:0.000}";
    }

    /// <summary>一次分类的完整结果</summary>
    public sealed class ClassifyResult
    {
        /// <summary>概率最高的类别索引（0 基）</summary>
        public required int PredictedClassId { get; init; }

        /// <summary>概率最高的类别名（模型元数据没带类别名时是 class_N 占位）</summary>
        public required string PredictedClassName { get; init; }

        /// <summary>最高类别的概率 0~1</summary>
        public required double Confidence { get; init; }

        /// <summary>全部类别的概率（与类别索引同序），供下游做多标签/阈值判定</summary>
        public required double[] Scores { get; init; }

        /// <summary>
        /// 全部类别名（与 Scores 同序；模型元数据没带时为空数组）。
        /// 【为什么必须带全量】只靠 TopK 回填名字，会在"TopK &lt; 类别数"时把未进 TopK 的类别名
        /// 错填成 class_N（即使模型带了名字）—— 下游按 ClassNames[Scores 下标] 取名就会取到错名。
        /// </summary>
        public required string[] ClassNames { get; init; }

        /// <summary>概率降序的前 K 个类别（K = 配置的 TopK，且不超过类别总数）</summary>
        public required IReadOnlyList<ClassScore> TopK { get; init; }

        /// <summary>推理耗时（毫秒）</summary>
        public required double ElapsedMs { get; init; }
    }

    /// <summary>
    /// 预处理缩放几何：把原图映射到模型输入画布。
    ///
    /// 【为什么单独抽出来】这是纯几何数学，与"插件如何组织端口/会话"无关。
    /// 抽成独立类后可以脱离 ONNX 用受控数据验证 —— 缩放/补边算错的表现是
    /// "分类置信度莫名其妙地低"，不会抛异常，必须能单独钉住。
    /// </summary>
    public sealed class ResizePlan
    {
        public required int SrcWidth { get; init; }
        public required int SrcHeight { get; init; }
        public required int DstWidth { get; init; }
        public required int DstHeight { get; init; }
        public required ClassifyResizeMode Mode { get; init; }

        /// <summary>实际绘制内容的尺寸（Letterbox 下小于画布；Stretch 下等于画布）</summary>
        public required int ScaledWidth { get; init; }
        public required int ScaledHeight { get; init; }

        /// <summary>内容在画布上的左上角偏移（Letterbox 居中补边；Stretch 恒为 0）</summary>
        public required int PadX { get; init; }
        public required int PadY { get; init; }

        /// <summary>Letterbox 的补边灰度。114 是 YOLO/Ultralytics 训练时的默认填充值 —— 换成 0 会引入模型没见过的黑边</summary>
        public const int PadGrayValue = 114;

        public static ResizePlan Compute(int srcWidth, int srcHeight, int dstWidth, int dstHeight, ClassifyResizeMode mode)
        {
            if (mode == ClassifyResizeMode.Stretch)
            {
                return new ResizePlan
                {
                    SrcWidth = srcWidth,
                    SrcHeight = srcHeight,
                    DstWidth = dstWidth,
                    DstHeight = dstHeight,
                    Mode = mode,
                    ScaledWidth = dstWidth,
                    ScaledHeight = dstHeight,
                    PadX = 0,
                    PadY = 0,
                };
            }

            float scale = Math.Min((float)dstWidth / srcWidth, (float)dstHeight / srcHeight);
            int scaledWidth = Math.Max(1, (int)Math.Round(srcWidth * scale));
            int scaledHeight = Math.Max(1, (int)Math.Round(srcHeight * scale));

            return new ResizePlan
            {
                SrcWidth = srcWidth,
                SrcHeight = srcHeight,
                DstWidth = dstWidth,
                DstHeight = dstHeight,
                Mode = mode,
                ScaledWidth = scaledWidth,
                ScaledHeight = scaledHeight,
                PadX = (dstWidth - scaledWidth) / 2,
                PadY = (dstHeight - scaledHeight) / 2,
            };
        }

        public string Describe() => Mode == ClassifyResizeMode.Stretch
            ? $"stretch {SrcWidth}×{SrcHeight} → {DstWidth}×{DstHeight}"
            : $"letterbox {SrcWidth}×{SrcHeight} → {ScaledWidth}×{ScaledHeight} 补边 ({PadX},{PadY}) 到 {DstWidth}×{DstHeight}";
    }

    /// <summary>
    /// 后处理：布局识别 → 概率归一化 → TopK。全部是**纯函数**，不依赖 ONNX Runtime，
    /// 所以能脱离模型单独用受控张量验证 —— 这一层恰恰是最容易写错且不报错的地方。
    /// </summary>
    public static class ClassifyPostProcess
    {
        /// <summary>
        /// 用模型元数据里的 <c>task</c> 字段判断这是不是分类模型。
        ///
        /// 【为什么必须校验】检测/分割模型的输出张量形状与分类不同，按分类解读会得到错误结果却
        /// **不报错**（例如把 [1,84,8400] 检测输出当分类读，会得到一堆无意义的"概率"）。
        /// Ultralytics 导出时会把 task 写进元数据（detect/segment/pose/obb/classify），这条路能拦下形状拦不住的。
        /// 元数据缺失时**放行**而不是拒绝：老模型/第三方导出可能没写 task，
        /// 因为"没写"而拒绝会让本来可用的模型用不了，那种情况只能靠输出形状自洽性兜底。
        /// </summary>
        public static bool IsSupportedTask(string? task, out string error)
        {
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(task)) return true;

            string normalized = task.Trim().ToLowerInvariant();
            if (normalized == "classify") return true;

            string friendly = normalized switch
            {
                "detect" => "目标检测",
                "segment" => "实例分割",
                "semantic" => "语义分割",
                "pose" => "姿态/关键点",
                "obb" => "旋转框",
                "depth" => "深度估计",
                _ => task.Trim(),
            };

            error = $"这个模型的任务类型是「{friendly}」（元数据 task={task.Trim()}），"
                  + "而本节点只做**图像分类**。不同任务的输出格式完全不同，按分类解读会得到错误结果却不报错，"
                  + "所以这里直接拒绝。请改用 classify 任务导出模型：yolo export model=你的模型.pt format=onnx task=classify";
            return false;
        }

        /// <summary>
        /// 识别分类输出张量的布局，并算出类别数。
        ///
        /// 【为什么不能只按"最后一个维度"猜】分类输出常见为 <c>[1, C]</c>（也可能被导出成
        /// <c>[1, 1, C]</c>）。而语义分割输出是 <c>[1, C, H, W]</c>、检测是 <c>[1, 4+C, N]</c> ——
        /// 形状相近，按"最后一维=类别数"硬读会把分割/检测也当分类放行，输出无意义结果却不报错。
        /// 所以这里按秩（维度个数）严格区分：只接受 2 维（或带 batch 的 [1,C] / [1,1,C]）。
        /// </summary>
        public static bool TrySniffLayout(int[] dims, out int classCount, out string error)
        {
            classCount = 0;
            error = string.Empty;

            // 去掉前导的 batch=1 维
            var shape = dims.Where(d => d > 0).ToArray();
            while (shape.Length > 1 && shape[0] == 1)
                shape = shape[1..];

            if (shape.Length != 1)
            {
                error = $"无法识别的输出形状 [{string.Join(",", dims.Select(d => d < 0 ? "N" : d.ToString()))}]："
                      + "期望分类模型的 [1, C]（C = 类别数）。"
                      + "若这是检测 [1,4+C,N] 或分割 [1,C,H,W] 模型，请改用对应的节点（YOLO 目标检测 / 分割）。";
                return false;
            }

            classCount = shape[0];
            if (classCount < 2)
            {
                error = $"输出类别数 {classCount} 不合理（分类模型至少 2 类）。"
                      + "请确认这是分类模型，且导出时没有裁剪输出。";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 把模型原始输出归一化成"每类概率、和为 1"。
        ///
        /// 【为什么要判断而不是无脑 softmax】Ultralytics 导出的分类模型，输出是**原始 logits**
        /// （softmax 在预测器里做，不在图里）；但也有部分导出/第三方模型已经把 softmax 做进图里。
        /// 对已经 softmax 过的输出再做一次 softmax 会把分布**压平**（原本 0.9 变 0.5 上下），
        /// 表现是"分类结果看着能跑、置信度却毫无区分度"，属于典型的静默错误。
        /// 判据：全在 [0,1] 且和约等于 1 → 视为概率；否则视为 logits 做 softmax。
        /// </summary>
        public static double[] NormalizeToProbabilities(float[] raw)
        {
            if (raw == null || raw.Length == 0) return Array.Empty<double>();

            double sum = 0;
            double min = double.MaxValue;
            double max = double.MinValue;
            foreach (var v in raw)
            {
                sum += v;
                if (v < min) min = v;
                if (v > max) max = v;
            }

            bool looksLikeProbabilities = min >= -1e-6 && max <= 1.0 + 1e-6 && Math.Abs(sum - 1.0) < 1e-3;
            if (looksLikeProbabilities)
                return raw.Select(v => (double)v).ToArray();

            return Softmax(raw);
        }

        /// <summary>数值稳定的 softmax（先减去最大值，避免 exp 溢出）</summary>
        public static double[] Softmax(float[] logits)
        {
            if (logits == null || logits.Length == 0) return Array.Empty<double>();

            float max = logits.Max();
            var exp = new double[logits.Length];
            double sum = 0;
            for (int i = 0; i < logits.Length; i++)
            {
                exp[i] = Math.Exp(logits[i] - max);
                sum += exp[i];
            }

            if (sum <= 0) return exp; // 理论上不会发生（exp 恒正），兜底不除零
            for (int i = 0; i < exp.Length; i++)
                exp[i] /= sum;
            return exp;
        }

        /// <summary>
        /// 取概率最高的前 K 个类别（K 会被夹到 [1, 类别数]）。
        /// 类别名缺失时用 class_N 占位 —— **不编造名字**（按 COCO/ImageNet 顺序硬套会让用户以为识别对了）。
        /// </summary>
        public static List<ClassScore> TopK(double[] probabilities, string[]? classNames, int k)
        {
            var result = new List<ClassScore>();
            if (probabilities == null || probabilities.Length == 0) return result;

            int take = Math.Clamp(k, 1, probabilities.Length);
            for (int i = 0; i < probabilities.Length; i++)
            {
                result.Add(new ClassScore
                {
                    ClassId = i,
                    ClassName = NameOf(classNames, i),
                    Probability = probabilities[i],
                });
            }

            return result
                .OrderByDescending(s => s.Probability)
                .ThenBy(s => s.ClassId) // 同分时按索引稳定排序，保证结果可复现
                .Take(take)
                .ToList();
        }

        /// <summary>
        /// 生成与 <see cref="ClassifyResult.Scores"/> 同序的**完整**类别名数组（长度 = 类别数）。
        /// 模型带了名字就用，缺/空则用 class_N 占位（**不编造**，也**不因只取 TopK 而错填**）。
        /// 这是 ClassNames 端口的唯一名字来源——不要用 TopK（只含前 K 条）去回填全量，那会给出错名。
        /// </summary>
        public static string[] ResolveClassNames(int classCount, string[]? modelNames)
        {
            if (classCount <= 0) return Array.Empty<string>();

            var names = new string[classCount];
            for (int i = 0; i < classCount; i++)
                names[i] = NameOf(modelNames, i);
            return names;
        }

        private static string NameOf(string[]? names, int classId) =>
            names != null && classId >= 0 && classId < names.Length && names[classId].Length > 0
                ? names[classId]
                : $"class_{classId}";
    }
}
