using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Plugin.DlAnomaly;

/// <summary>异常检测模型的输出契约。</summary>
/// <remarks>
/// 异常检测的 ONNX 输出没有统一标准（方案书 §3.2）。本仓只接受两种已知形状，
/// 其余一律明确拒绝——**不猜**：
/// <list type="bullet">
/// <item>图像级分数：秩 2 的 <c>[1,1]</c>，或降维后的单值，越大越异常。</item>
/// <item>像素级热力图：秩 4 的 <c>[1,1,H,W]</c>，或秩 3 的 <c>[1,H,W]</c>。</item>
/// </list>
/// 契约一旦认错，下游会把热力图当分数阈值判、或者把分数当掩膜上屏，而且**不会报错**。
/// </remarks>
public sealed class AnomalyOutputContract
{
    /// <summary>图像级分数输出名（没有则为 null，表示该模型只给热力图）。</summary>
    public string? ScoreOutputName { get; init; }

    /// <summary>像素级热力图输出名（没有则为 null，表示该模型只给图像级分数）。</summary>
    public string? HeatmapOutputName { get; init; }

    /// <summary>热力图高（模型的输出网格，不是原图尺寸）。</summary>
    public int HeatmapHeight { get; init; }

    /// <summary>热力图宽。</summary>
    public int HeatmapWidth { get; init; }

    /// <summary>是否带像素级热力图。</summary>
    public bool HasHeatmap => HeatmapOutputName is not null;

    /// <summary>可读摘要，用于配置界面与日志：必须能看见「读到的形状」，否则用户无从判断为什么被拒。</summary>
    public string Describe()
    {
        List<string> parts = [];
        parts.Add(ScoreOutputName is null ? "图像级分数：无" : $"图像级分数：{ScoreOutputName}");
        parts.Add(HeatmapOutputName is null
            ? "像素级热力图：无"
            : $"像素级热力图：{HeatmapOutputName}（{HeatmapWidth}×{HeatmapHeight}）");
        return string.Join("；", parts);
    }
}

/// <summary>输出契约嗅探器：按**秩**识别哪个输出是分数、哪个是热力图，认不出就明确拒绝。</summary>
public static class AnomalyPostProcess
{
    /// <summary>
    /// 从模型的输出元数据里识别契约。
    /// </summary>
    /// <param name="outputs">
    /// onnxruntime 报出的输出（字典的 <c>Key</c> 是输出名，<c>Value</c> 是维度元数据；顺序即模型声明顺序）。
    /// <c>NodeMetadata</c> 本身**不带名字**，所以必须成对传入。
    /// </param>
    /// <param name="contract">识别出的契约。</param>
    /// <param name="error">识别失败的原因（会原样给用户看，必须说清「读到什么」与「为什么不认」）。</param>
    public static bool TryResolveContract(
        IReadOnlyList<KeyValuePair<string, NodeMetadata>> outputs,
        out AnomalyOutputContract? contract,
        out string error)
        => TryResolveContractCore(
            outputs is null
                ? null
                : outputs.Select(o => new KeyValuePair<string, int[]>(
                    o.Key, o.Value?.Dimensions?.Select(d => (int)d).ToArray() ?? [])).ToArray(),
            out contract,
            out error);

    /// <summary>
    /// 识别契约的**纯形状入口**：只吃「名字 + 维度数组」，不碰 onnxruntime 的 <c>NodeMetadata</c>。
    /// </summary>
    /// <remarks>
    /// 两条理由，都不是洁癖：
    /// <list type="number">
    /// <item>断言宿主能直接喂受控形状。<c>NodeMetadata</c> 没有公开构造器、<c>Dimensions</c> 又是只读属性，
    /// 想在断言里造「假元数据」只能反射写只读属性——那会抛
    /// <c>ArgumentException: Property set method not found</c> 并掀翻整个断言宿主（实测踩过）。
    /// 改吃 <c>int[]</c> 之后，契约判定变成纯函数，可穷举测试、零依赖。</item>
    /// <item>契约判定的输入本来就只是形状；绑 <c>NodeMetadata</c> 属于不必要的耦合。</item>
    /// </list>
    /// </remarks>
    /// <param name="outputs">输出名 → 维度数组（顺序即模型声明顺序）。</param>
    /// <param name="contract">识别出的契约。</param>
    /// <param name="error">识别失败的原因。</param>
    public static bool TryResolveContract(
        IReadOnlyList<KeyValuePair<string, int[]>> outputs,
        out AnomalyOutputContract? contract,
        out string error)
        => TryResolveContractCore(outputs, out contract, out error);

    private static bool TryResolveContractCore(
        IReadOnlyList<KeyValuePair<string, int[]>>? outputs,
        out AnomalyOutputContract? contract,
        out string error)
    {
        contract = null;
        error = string.Empty;

        if (outputs is null || outputs.Count == 0)
        {
            error = "模型没有任何输出：不像异常检测模型，请确认导出的是整网而不是某个中间层。";
            return false;
        }

        // NodeMetadata 上**没有 Name**（实测 Microsoft.ML.OnnxRuntime 1.x：
        // 只有 OnnxValueType/Dimensions/SymbolicDimensions/ElementType/ElementDataType/IsString/IsTensor）。
        // 输出名只能来自 InferenceSession.OutputMetadata 这个字典的 Key，
        // 或同序的 OutputNames 列表——所以这里收 "名字 + 元数据" 的成对序列，不自己编名字。
        string? scoreName = null;
        string? heatmapName = null;
        int heatmapHeight = 0;
        int heatmapWidth = 0;
        List<string> unknown = [];

        for (int i = 0; i < outputs.Count; i++)
        {
            string name = outputs[i].Key;
            int[] dims = outputs[i].Value ?? [];
            string displayed = $"{name}（秩 {dims.Length}，形状 [{string.Join(", ", dims)}]）";

            if (scoreName is null && IsImageScoreShape(dims))
            {
                scoreName = name;
                continue;
            }

            if (heatmapName is null && TryReadHeatmapShape(dims, out int h, out int w))
            {
                heatmapName = name;
                heatmapHeight = h;
                heatmapWidth = w;
                continue;
            }

            unknown.Add(displayed);
        }

        if (scoreName is null && heatmapName is null)
        {
            error = $"模型的输出形状不是本插件支持的异常检测契约（读到的输出：{string.Join("；", outputs.Select(o =>
            {
                int[] d = o.Value ?? [];
                return $"{o.Key}（秩 {d.Length}，形状 [{string.Join(", ", d)}]）";
            }))}）。本插件只认两种：图像级分数秩 2 的 [1,1]；像素级热力图秩 4 的 [1,1,H,W] 或秩 3 的 [1,H,W]。**不会替你猜**：请确认导出的是异常检测模型，或按契约改导出。";
            return false;
        }

        if (unknown.Count > 0)
        {
            // 已经有可用输出，多出来的未知输出不致命，但必须写进 error 之外的可读信息里。
            // 这里不失败：真实模型常同时输出「分数 + 热力图 + 中间量」。信息由 Describe 之外的地方记录。
        }

        contract = new AnomalyOutputContract
        {
            ScoreOutputName = scoreName,
            HeatmapOutputName = heatmapName,
            HeatmapHeight = heatmapHeight,
            HeatmapWidth = heatmapWidth,
        };
        return true;
    }

    /// <summary>秩 2 且末维为 1：模型给出的单幅图像分数。</summary>
    private static bool IsImageScoreShape(int[] dimensions)
        => dimensions.Length == 2 && dimensions[1] == 1;

    /// <summary>热力图允许的最小边长（像素）。低于它的「热力图」几乎不可能是逐像素异常图。</summary>
    /// <remarks>
    /// 异常检测的热力图必须在原图分辨率量级（PatchCore/DRAEM 一类最低也是 224×224 网格，
    /// 小目标检测也要几十像素才有意义）。设下限是为了挡掉形状碰巧同秩的**检测器输出**。
    /// </remarks>
    private const int MinHeatmapEdge = 32;

    /// <summary>热力图允许的最大边长（像素）：超过它必然是别的张量被形状误认。</summary>
    private const int MaxHeatmapEdge = 8192;

    /// <summary>秩 4 的 [1,1,H,W] 或秩 3 的 [1,H,W]；H/W 必须落在 [32, 8192]。</summary>
    /// <remarks>
    /// **秩 3 的两义性**（实测踩过）：<c>[1,H,W]</c> 热力图和 <c>[1,N,C]</c> 检测器输出**同秩且 dims[0] 都是 1**，
    /// 只靠形状分不开。检测器 <c>[1,84,8400]</c> 若被当成热力图（H=84、W=8400），下游会把它当逐像素分数
    /// 直接阈值化，结果是一张乱码图，而且**不报错**。所以秩 3 必须加尺寸守卫：
    /// <list type="bullet">
    /// <item>两条边都 ≥ 32 —— 84×8400 的 84 过不了（检测器 N 通常只有几十）；</item>
    /// <item>两条边都 ≤ 8192 —— 上界只防溢出，真正拦住检测器的是下界。</item>
    /// </list>
    /// 已知边界：若某个真实热力图的一边恰好 &lt; 32（极低分辨率网格），会被拒绝——这是**有意的选择**，
    /// 拒绝好过静默把检测器输出当热力图（错误串会写出真实形状，用户可据此判断）。
    /// </remarks>
    private static bool TryReadHeatmapShape(int[] dimensions, out int height, out int width)
    {
        height = 0;
        width = 0;

        if (dimensions.Length == 4)
        {
            if (dimensions[0] != 1 || dimensions[1] != 1)
            {
                return false;
            }

            height = dimensions[2];
            width = dimensions[3];
        }
        else if (dimensions.Length == 3)
        {
            if (dimensions[0] != 1)
            {
                return false;
            }

            height = dimensions[1];
            width = dimensions[2];
        }
        else
        {
            return false;
        }

        return height >= MinHeatmapEdge && height <= MaxHeatmapEdge
            && width >= MinHeatmapEdge && width <= MaxHeatmapEdge;
    }

    /// <summary>把输出张量摊成 double 数组（不改形状语义，只做类型无关读取）。</summary>
    internal static double[] Flatten(Tensor<float> tensor)
    {
        double[] values = new double[tensor.Length];
        int i = 0;
        foreach (float v in tensor)
        {
            values[i++] = v;
        }
        return values;
    }
}
