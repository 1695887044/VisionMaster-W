using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using HalconDotNet;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Plugin.DlAnomaly
{
    /// <summary>异常检测参数（对应节点上的可调项）。</summary>
    public sealed class AnomalyParams
    {
        /// <summary>缩放模式：letterbox（等比补边，默认，防长宽比失真）或 stretch（拉伸）。</summary>
        public AnomalyResizeMode ResizeMode { get; set; } = AnomalyResizeMode.Letterbox;

        /// <summary>
        /// 模型输入边长（方形）。0 = 用模型元数据里的静态输入尺寸。
        /// 两者都拿不到必须报错 —— 猜一个尺寸会得到错位的预处理结果。
        /// </summary>
        public int InputSize { get; set; }

        /// <summary>热力图 → 原图的正向缩放插值（结果只用于上屏与调试，默认 nearest 更贴"每个格子的原始判定"）。</summary>
        public AnomalyInterpolation HeatmapInterpolation { get; set; } = AnomalyInterpolation.Nearest;

        /// <summary>
        /// 热力图归一化上界。&gt; 0 时把热力图按 [0, 该值] 映射到 0~255 便于上屏；
        /// 0 = 按本图实际最大/最小自适应（最直观，但不同图之间颜色不可比）。
        /// </summary>
        public double HeatmapMax { get; set; }
    }

    /// <summary>一次异常检测的结果。</summary>
    public sealed class AnomalyResult
    {
        /// <summary>图像级异常分数（模型没给分数时为 0）。</summary>
        public double Score { get; set; }

        /// <summary>是否有有效的图像级分数。</summary>
        public bool HasScore { get; set; }

        /// <summary>热力图峰值（模型没给热力图时为 0）。</summary>
        public double HeatmapMax { get; set; }

        /// <summary>热力图峰值所在行（热力图网格坐标；无热力图时为 -1）。</summary>
        public int HeatmapMaxRow { get; set; } = -1;

        /// <summary>热力图峰值所在列（热力图网格坐标；无热力图时为 -1）。</summary>
        public int HeatmapMaxCol { get; set; } = -1;

        /// <summary>热力图均值。</summary>
        public double HeatmapMean { get; set; }

        /// <summary>热力图最小/最大（自适应归一化用）。</summary>
        public double HeatmapMin { get; set; }

        /// <summary>模型是否给出了像素级热力图。</summary>
        public bool HasHeatmap => HeatmapImage != null;

        /// <summary>热力图上屏图（与原图同尺寸的 byte 单通道，0~255）。模型没给热力图时为空。</summary>
        public HImage? HeatmapImage { get; set; }

        /// <summary>热力图原始网格尺寸（模型输出的 H/W，不是原图尺寸）。</summary>
        public int HeatmapHeight { get; set; }

        public int HeatmapWidth { get; set; }

        public double ElapsedMs { get; set; }
    }

    /// <summary>
    /// 异常检测推理引擎：HImage → 预处理（缩放/补边）→ ONNX Runtime → 按契约解码。
    ///
    /// 【判据】只解码契约里认下的输出：图像级分数（秩 2 的 [1,1]）与像素级热力图（秩 4 [1,1,H,W] / 秩 3 [1,H,W]）。
    /// 认不出的形状在会话加载期就已经被拒（<see cref="AnomalyPostProcess.TryResolveContract"/>），
    /// 所以这里不需要"再猜一次"——**同一判据只实现一遍**。
    /// </summary>
    public sealed class AnomalyEngine
    {
        private readonly AnomalySession _session;

        public AnomalyModelInfo Info => _session.Info;

        public AnomalyEngine(AnomalySession session) => _session = session;

        /// <summary>对一张图做异常检测。失败时 error 说明原因，不抛异常。</summary>
        public bool TryDetect(HImage? image, AnomalyParams parameters, out AnomalyResult? result, out string error)
        {
            result = null;
            error = string.Empty;

            if (image == null || !image.IsInitialized())
            {
                error = "输入图像为空";
                return false;
            }

            if (!ResolveInputSize(parameters, out int inputWidth, out int inputHeight, out error)) return false;

            var owned = new List<IDisposable>();

            try
            {
                HOperatorSet.GetImageSize(image, out HTuple srcWidthTuple, out HTuple srcHeightTuple);
                int srcWidth = srcWidthTuple.I;
                int srcHeight = srcHeightTuple.I;
                if (srcWidth <= 0 || srcHeight <= 0)
                {
                    error = $"图像尺寸异常：{srcWidth}×{srcHeight}";
                    return false;
                }

                var plan = ResizePlan.Compute(srcWidth, srcHeight, inputWidth, inputHeight, parameters.ResizeMode);

                // ---- 1. 预处理成目标画布（byte，3 通道）----
                if (!TryBuildCanvas(image, plan, out HObject? canvas, out error)) return false;
                owned.Add(canvas!);

                // ---- 2. 画布 → NCHW float 张量 ----
                float[] tensorData = ToNchwFloat(canvas!, inputWidth, inputHeight);
                var inputTensor = new DenseTensor<float>(tensorData, new[] { 1, 3, inputHeight, inputWidth });
                var inputValue = NamedOnnxValue.CreateFromTensor(Info.InputName, inputTensor);

                try
                {
                    // ---- 3. 推理 ----
                    var stopwatch = Stopwatch.StartNew();
                    using var outputs = _session.Session.Run(new[] { inputValue });
                    stopwatch.Stop();

                    // IDisposableReadOnlyCollection<DisposableNamedOnnxValue> 的元素类型是
                    // DisposableNamedOnnxValue（Name/AsTensor 都从 NamedOnnxValue 继承），
                    // 直接按 Name 建索引；用 NamedOnnxValue 接会 CS1503（out 参数精确类型匹配）。
                    var byName = outputs
                        .GroupBy(o => o.Name, StringComparer.Ordinal)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

                    // ---- 4. 按契约解码（契约在加载期已确认过，这里只是取值 + 形状复核）----
                    bool hasScore = false;
                    double score = 0;
                    if (Info.Contract.ScoreOutputName is { } scoreName)
                    {
                        if (!byName.TryGetValue(scoreName, out DisposableNamedOnnxValue? scoreValue))
                        {
                            error = $"模型没有返回声明的图像级分数输出「{scoreName}」（实际返回：{string.Join("，", byName.Keys)}）。"
                                  + "模型与加载时的元数据不一致，请重新导出或重启流程。";
                            return false;
                        }

                        Tensor<float> scoreTensor = scoreValue.AsTensor<float>();
                        double[] flat = AnomalyPostProcess.Flatten(scoreTensor);
                        if (flat.Length < 1)
                        {
                            error = $"图像级分数输出「{scoreName}」是空张量，无法读取。";
                            return false;
                        }

                        score = flat[0];
                        hasScore = true;
                    }

                    double heatMin = 0;
                    double heatMax = 0;
                    double heatMean = 0;
                    int heatPeakRow = -1;
                    int heatPeakCol = -1;
                    HImage? heatmapImage = null;
                    if (Info.Contract.HeatmapOutputName is { } heatName)
                    {
                        if (!byName.TryGetValue(heatName, out DisposableNamedOnnxValue? heatValue))
                        {
                            error = $"模型没有返回声明的热力图输出「{heatName}」（实际返回：{string.Join("，", byName.Keys)}）。";
                            return false;
                        }

                        Tensor<float> heatTensor = heatValue.AsTensor<float>();
                        double[] flat = AnomalyPostProcess.Flatten(heatTensor);
                        int expected = Info.Contract.HeatmapHeight * Info.Contract.HeatmapWidth;
                        if (flat.Length != expected)
                        {
                            error = $"热力图张量长度 {flat.Length} 与声明的 {Info.Contract.HeatmapWidth}×{Info.Contract.HeatmapHeight}={expected} 不一致。";
                            return false;
                        }

                        if (flat.Length == 0)
                        {
                            error = "热力图输出为空张量。";
                            return false;
                        }

                        heatMin = flat.Min();
                        heatMax = flat.Max();
                        heatMean = flat.Average();

                        // 峰值位置：热力图网格坐标（不是原图坐标 —— 网格尺寸由模型决定，
                        // 由下游按比例换算；这里给出网格坐标是为了让"最大值在哪"可复现、可断言）。
                        int peakIndex = 0;
                        for (int i = 1; i < flat.Length; i++)
                        {
                            if (flat[i] > flat[peakIndex]) peakIndex = i;
                        }
                        heatPeakRow = peakIndex / Info.Contract.HeatmapWidth;
                        heatPeakCol = peakIndex % Info.Contract.HeatmapWidth;

                        double upper = parameters.HeatmapMax > 0 ? parameters.HeatmapMax : heatMax > heatMin ? heatMax : heatMin + 1.0;
                        heatmapImage = BuildHeatmapImage(flat, Info.Contract.HeatmapWidth, Info.Contract.HeatmapHeight, heatMin, upper);
                    }

                    result = new AnomalyResult
                    {
                        Score = score,
                        HasScore = hasScore,
                        HeatmapMin = heatMin,
                        HeatmapMax = heatMax,
                        HeatmapMaxRow = heatPeakRow,
                        HeatmapMaxCol = heatPeakCol,
                        HeatmapMean = heatMean,
                        HeatmapImage = heatmapImage,
                        HeatmapHeight = Info.Contract.HeatmapHeight,
                        HeatmapWidth = Info.Contract.HeatmapWidth,
                        ElapsedMs = stopwatch.Elapsed.TotalMilliseconds,
                    };
                    return true;
                }
                finally
                {
                    (inputValue as IDisposable)?.Dispose();
                }
            }
            catch (Exception ex)
            {
                error = $"异常检测推理失败：{ex.Message}";
                return false;
            }
            finally
            {
                foreach (var item in owned)
                {
                    try { item.Dispose(); }
                    catch { /* 释放失败不打断整体回收 */ }
                }
            }
        }

        /// <summary>
        /// 热力图 float 网格 → byte 图。归一化区间 [lower, upper] 夹到 0~255。
        /// 【为什么给 lower】有的模型输出负分（oriented score），直接按 [0,max] 会让整图发黑。
        /// </summary>
        private static HImage? BuildHeatmapImage(double[] values, int width, int height, double lower, double upper)
        {
            double span = upper - lower;
            if (span <= 1e-9) span = 1.0;

            var bytes = new byte[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                double t = (values[i] - lower) / span;
                if (double.IsNaN(t) || t < 0) t = 0;
                if (t > 1) t = 1;
                bytes[i] = (byte)Math.Round(t * 255.0);
            }

            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                HOperatorSet.GenImage1(out HObject image, "byte", width, height, handle.AddrOfPinnedObject());
                return new HImage(image);
            }
            finally
            {
                handle.Free();
            }
        }

        /// <summary>确定输入宽高：配置优先（方形），其次模型静态形状；都没有就报错（不猜）。</summary>
        private bool ResolveInputSize(AnomalyParams parameters, out int width, out int height, out string error)
        {
            width = 0;
            height = 0;
            error = string.Empty;

            if (parameters.InputSize > 0)
            {
                width = height = parameters.InputSize;
                return true;
            }

            if (Info.HasStaticInputShape)
            {
                width = Info.InputWidth;
                height = Info.InputHeight;
                return true;
            }

            error = "无法确定模型输入尺寸：模型是动态形状，且节点配置里没指定「输入尺寸」。"
                  + $"请填写一个值（异常检测模型常见为 224 或 256；本模型输入形状：[{string.Join(",", Info.InputDimensions.Select(d => d < 0 ? "N" : d.ToString()))}]）";
            return false;
        }

        /// <summary>
        /// 构造模型输入画布：转 byte → 转 3 通道 → 按模式缩放 → 居中补边。
        /// 三项纪律与 DlClassify 同源（各插件独立实现，理由见方案书 §5.1）：
        /// ① 单通道必须 compose3；② 补边 114 灰不是黑；③ overpaint 前画布必须无条件 3 通道（否则 #3122）。
        /// </summary>
        private bool TryBuildCanvas(HImage source, ResizePlan plan, out HObject? canvas, out string error)
        {
            canvas = null;
            error = string.Empty;

            var temps = new List<IDisposable>();
            try
            {
                HObject working = source;
                HOperatorSet.GetImageType(source, out HTuple typeTuple);
                if (!string.Equals(typeTuple.S, "byte", StringComparison.OrdinalIgnoreCase))
                {
                    HOperatorSet.ConvertImageType(source, out HObject converted, "byte");
                    temps.Add(converted);
                    working = converted;
                }

                HOperatorSet.CountChannels(working, out HTuple channelCount);
                int channels = channelCount.I;

                if (channels == 1)
                {
                    HOperatorSet.Compose3(working, working, working, out HObject composed);
                    temps.Add(composed);
                    working = composed;
                }
                else if (channels != 3)
                {
                    error = $"图像通道数为 {channels}，只支持 1 通道（自动补成 3）或 3 通道。请在上游用预处理节点转成灰度或 RGB";
                    return false;
                }

                if (plan.Mode == AnomalyResizeMode.Stretch)
                {
                    HOperatorSet.ZoomImageSize(working, out HObject stretched, plan.DstWidth, plan.DstHeight, "bilinear");
                    canvas = stretched;
                    temps.Remove(stretched);
                    return true;
                }

                HOperatorSet.ZoomImageSize(working, out HObject scaled, plan.ScaledWidth, plan.ScaledHeight, "bilinear");
                temps.Add(scaled);

                HOperatorSet.GenImageConst(out HObject sizeCarrier, "byte", plan.DstWidth, plan.DstHeight);
                temps.Add(sizeCarrier);
                HOperatorSet.GenImageProto(sizeCarrier, out HObject grayCanvas, ResizePlan.PadGrayValue);
                temps.Add(grayCanvas);

                HOperatorSet.Compose3(grayCanvas, grayCanvas, grayCanvas, out HObject canvasImage);
                temps.Add(canvasImage);

                HOperatorSet.GenRectangle1(
                    out HObject targetRect,
                    plan.PadY,
                    plan.PadX,
                    plan.PadY + plan.ScaledHeight - 1,
                    plan.PadX + plan.ScaledWidth - 1);
                temps.Add(targetRect);

                HOperatorSet.ChangeDomain(scaled, targetRect, out HObject positioned);
                temps.Add(positioned);

                HOperatorSet.OverpaintGray(canvasImage, positioned);

                temps.Remove(canvasImage);
                canvas = canvasImage;
                return true;
            }
            catch (Exception ex)
            {
                error = $"预处理（缩放/补边）失败：{ex.Message}";
                return false;
            }
            finally
            {
                foreach (var item in temps)
                {
                    try { item.Dispose(); }
                    catch { /* 忽略 */ }
                }
            }
        }

        /// <summary>3 通道 byte 图 → NCHW float（归一化 /255）。指针读取，理由同 DlClassify：122 万像素逐个取值太慢。</summary>
        private static float[] ToNchwFloat(HObject image3Channel, int width, int height)
        {
            var result = new float[3 * height * width];
            int planeSize = width * height;

            for (int c = 1; c <= 3; c++)
            {
                HOperatorSet.AccessChannel(image3Channel, out HObject plane, c);
                try
                {
                    HOperatorSet.GetImagePointer1(plane, out HTuple pointer, out _, out HTuple w, out HTuple h);

                    int pixelCount = w.I * h.I;
                    var bytes = new byte[pixelCount];
                    Marshal.Copy(new IntPtr(pointer.L), bytes, 0, pixelCount);

                    int offset = (c - 1) * planeSize;
                    for (int i = 0; i < pixelCount; i++)
                        result[offset + i] = bytes[i] / 255f;
                }
                finally
                {
                    plane.Dispose();
                }
            }

            return result;
        }
    }
}
