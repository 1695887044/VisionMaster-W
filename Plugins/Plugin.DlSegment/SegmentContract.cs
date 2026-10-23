using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;

namespace Plugin.DlSegment
{
    /// <summary>
    /// 语义分割的模型输入/输出契约。
    ///
    /// 【判据】输入必须是 4 维 NCHW（[1,3,H,W] 或 [1,1,H,W]）；输出必须是 4 维 [1,C,H,W]，且 C ≥ 2。
    /// 理由：逐像素 argmax 要求每个像素上都有一组类别的打分。
    /// 只给一张 [1,1,H,W] 的二值掩膜、或给 [N,C] 这种检测头输出，都属于"不是语义分割模型"——
    /// 一律明确拒绝并把读到的真实形状写进错误串，**不猜**。
    ///
    /// 实例分割（YOLOv8/v11-seg 的 [1,4+C+32,N] + [1,32,160,160] 原型）不在本插件的契约里，
    /// 见 docs/深度学习/深度学习插件（分类-异常-分割）方案说明书.md §4.1：那需要检测头复用 Yolo 解码，
    /// "约等于再做一个半 Yolo"，属独立立项。
    /// </summary>
    public sealed class SegmentOutputContract
    {
        /// <summary>类别数（输出通道数 C）。</summary>
        public int ClassCount { get; init; }

        /// <summary>输出特征图高度。</summary>
        public int Height { get; init; }

        /// <summary>输出特征图宽度。</summary>
        public int Width { get; init; }

        /// <summary>类别名（模型元数据给了就用，没给就 class_0…，不编造语义）。</summary>
        public IReadOnlyList<string> ClassNames { get; init; } = Array.Empty<string>();

        public string Describe() =>
            $"{ClassCount} 类，特征图 {Height}×{Width}，类别名 {string.Join(",", ClassNames)}";
    }

    /// <summary>语义分割的契约嗅探与逐像素解码（纯托管，无 HALCON 依赖）。</summary>
    public static class SegmentPostProcess
    {
        /// <summary>类别数下限：至少要有"背景 + 一类目标"两路打分，否则不是分割模型。</summary>
        private const int MinClassCount = 2;

        /// <summary>特征图边长上限，挡住把检测头输出 [1,N,C] 当成 [1,C,H,W] 的误判。</summary>
        private const int MaxFeatureEdge = 8192;

        /// <summary>
        /// 任务标签是否属于语义分割。元数据里的 task 标签缺失时不做否决
        /// （很多导出的 onnx 没有这个字段），只在显式写了别的任务时才拒。
        /// </summary>
        public static bool IsSupportedTask(string? task, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(task)) return true;

            string value = task.Trim();
            if (value.Equals("segment", StringComparison.OrdinalIgnoreCase)
                || value.Equals("segmentation", StringComparison.OrdinalIgnoreCase)
                || value.Equals("semantic", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            error = $"模型元数据声明的任务是「{task}」，本插件只做语义分割（task=segment/segmentation/semantic）。" +
                    "请改用分割模型，或换用对应的插件。";
            return false;
        }

        /// <summary>
        /// 从模型输出元数据里认出契约。
        /// 入参是「输出名 + 形状」的成对序列——**故意不收 NodeMetadata**：
        /// ① 判定只依赖形状，绑 SDK 元数据类型是不必要的耦合；
        /// ② NodeMetadata 没有公开构造器、Dimensions 是只读属性，断言无法构造它。
        /// </summary>
        public static bool TryResolveContract(
            IReadOnlyList<KeyValuePair<string, NodeMetadata>> outputs,
            out SegmentOutputContract? contract,
            out string error)
        {
            KeyValuePair<string, int[]>[]? shapes = outputs?.Select(o =>
                new KeyValuePair<string, int[]>(
                    o.Key,
                    o.Value?.Dimensions?.Select(d => (int)d).ToArray() ?? Array.Empty<int>())).ToArray();
            return TryResolveContract(shapes!, out contract, out error);
        }

        /// <summary>纯形状入口（断言与单元测试用这个，不碰 ONNX SDK 类型）。</summary>
        public static bool TryResolveContract(
            IReadOnlyList<KeyValuePair<string, int[]>> outputs,
            out SegmentOutputContract? contract,
            out string error)
            => TryResolveContractCore(outputs, out contract, out error);

        private static bool TryResolveContractCore(
            IReadOnlyList<KeyValuePair<string, int[]>>? outputs,
            out SegmentOutputContract? contract,
            out string error)
        {
            contract = null;
            error = string.Empty;

            if (outputs is null || outputs.Count == 0)
            {
                error = "模型没有任何输出——不是可用的分割模型。";
                return false;
            }

            var rejected = new List<string>();
            foreach (KeyValuePair<string, int[]> pair in outputs)
            {
                int[] dims = pair.Value ?? Array.Empty<int>();
                if (TryReadFeatureDims(dims, out int channels, out int height, out int width))
                {
                    contract = new SegmentOutputContract
                    {
                        ClassCount = channels,
                        Height = height,
                        Width = width,
                        ClassNames = BuildDefaultClassNames(channels)
                    };
                    return true;
                }

                rejected.Add($"{pair.Key}（秩 {dims.Length}，形状 [{string.Join(", ", dims)}]）");
            }

            error = "分割输出形状不认识。读到的输出：" + string.Join("；", rejected) + "。" +
                    "本插件只认 4 维 [1,C,H,W] 且 C ≥ 2 的语义分割输出（C 是类别数，含背景）。" +
                    "实例分割（[1,4+C+32,N] + 掩膜原型）、二值掩膜 [1,1,H,W] 都不在本插件契约内。" +
                    "换模型或换插件，**不会替你猜**。";
            return false;
        }

        /// <summary>认 [1,C,H,W]；C≥2 且 H/W 在 (0, MaxFeatureEdge] 内。</summary>
        public static bool TryReadFeatureDims(int[] dims, out int channels, out int height, out int width)
        {
            channels = 0;
            height = 0;
            width = 0;

            if (dims is null || dims.Length != 4) return false;
            if (dims[0] != 1) return false;

            channels = dims[1];
            height = dims[2];
            width = dims[3];

            if (channels < MinClassCount) return false;
            if (height <= 0 || height > MaxFeatureEdge) return false;
            if (width <= 0 || width > MaxFeatureEdge) return false;
            return true;
        }

        /// <summary>把模型元数据里的 names（逗号分隔）拆成类别名；没有就给 class_0…（不编造语义）。</summary>
        public static IReadOnlyList<string> ParseClassNames(string? names, int classCount)
        {
            var list = new List<string>();
            if (!string.IsNullOrWhiteSpace(names))
            {
                foreach (string part in names.Split(',', ';'))
                {
                    string name = part.Trim();
                    if (name.Length > 0) list.Add(name);
                }
            }

            // 名字数量与类别数不一致时以 class_N 补齐/截断，避免张冠李戴。
            if (list.Count != classCount) return BuildDefaultClassNames(classCount);
            return list;
        }

        private static IReadOnlyList<string> BuildDefaultClassNames(int classCount)
        {
            var list = new List<string>(classCount);
            for (int i = 0; i < classCount; i++) list.Add($"class_{i}");
            return list;
        }
    }
}
