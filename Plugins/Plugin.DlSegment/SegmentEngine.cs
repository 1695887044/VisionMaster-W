using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using Core.Interfaces;
using HalconDotNet;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Plugin.DlSegment
{
    /// <summary>一次分割推理的参数。</summary>
    public sealed class SegmentParams
    {
        public SegmentResizeMode ResizeMode { get; init; } = SegmentResizeMode.Letterbox;

        /// <summary>模型输入边长的显式指定；0 = 用模型静态形状。</summary>
        public int InputSize { get; init; }

        public SegmentInterpolation Interpolation { get; init; } = SegmentInterpolation.Nearest;

        public SegmentRegionMode RegionMode { get; init; } = SegmentRegionMode.AllClasses;

        public int ClassIndex { get; init; }
    }

    /// <summary>一次分割推理的结果。</summary>
    public sealed class SegmentResult
    {
        public required SegResultData Data { get; init; }
    }

    /// <summary>逐像素解码后的分割结果。</summary>
    public sealed class SegResultData
    {
        /// <summary>类别图（byte 单通道，像素值 = 类别序号，已还原到原图尺寸）。</summary>
        public HImage? ClassMap { get; init; }

        /// <summary>前景区域（按 RegionMode 取出的像素集合，坐标系是原图）。</summary>
        public HImage? RegionMask { get; init; }

        /// <summary>前景区域对象（供下游做面积/形状测量）。</summary>
        public HObject? Region { get; init; }

        public int RegionCount { get; init; }

        public int Width { get; init; }

        public int Height { get; init; }

        /// <summary>各类像素数，下标 = 类别序号。</summary>
        public double[] ClassPixelCounts { get; init; } = Array.Empty<double>();

        /// <summary>各类像素占全图比例（0~1），下标 = 类别序号。</summary>
        public double[] ClassRatios { get; init; } = Array.Empty<double>();

        /// <summary>面积最大的类（不含背景类的第 0 类时仍会返回真实最大类）。</summary>
        public int DominantClassIndex { get; init; }

        public string DominantClassName { get; init; } = string.Empty;

        public double ForegroundRatio { get; init; }

        public long ElapsedMs { get; init; }

        public int FeatureWidth { get; init; }

        public int FeatureHeight { get; init; }
    }

    /// <summary>
    /// 推理引擎：预处理 → 会话推理 → 逐像素 argmax → 还原到原图尺寸。
    ///
    /// 【判据】这个类只产生"类别图 + 统计"，不做 OK/NG 判定。
    /// 判定归下游比较/逻辑节点，与 DlClassify / DlAnomaly 同一规矩：模型插件不自称判定器。
    /// </summary>
    public sealed class SegmentEngine
    {
        private readonly SegmentSession _session;

        public SegmentEngine(SegmentSession session) => _session = session;

        public SegmentModelInfo Info => _session.Info;

        public bool TrySegment(HImage? image, SegmentParams parameters, out SegResultData? result, out string error)
        {
            result = null;
            error = string.Empty;

            if (image is null || !image.IsInitialized())
            {
                error = "输入图像为空：请把上游图像节点的输出连到本节点的 Image。";
                return false;
            }

            if (!TryResolveInputSize(parameters.InputSize, out int inputWidth, out int inputHeight, out error))
            {
                return false;
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();

            HObject? canvas = null;
            HObject? classRegion = null;

            try
            {
                HOperatorSet.GetImageSize(image, out HTuple widthTuple, out HTuple heightTuple);
                int srcWidth = widthTuple.I;
                int srcHeight = heightTuple.I;

                SegmentResizePlan plan = SegmentResizePlan.Compute(srcWidth, srcHeight, inputWidth, inputHeight, parameters.ResizeMode);

                if (!TryBuildCanvas(image, plan, out canvas, out error)) return false;

                float[] data = ToNchwFloat(canvas, inputWidth, inputHeight);

                SegmentOutputContract contract = Info.Contract;
                var tensor = new DenseTensor<float>(data, new[] { 1, 3, inputHeight, inputWidth });
                // NamedOnnxValue 是抽象基类、**不实现 IDisposable**（写 using 报 CS1674），
                // 必须用 as IDisposable 在 finally 里释放（与 Plugin.DlClassify / Plugin.DlAnomaly 同法）。
                NamedOnnxValue inputValue = NamedOnnxValue.CreateFromTensor(Info.InputName, tensor);

                using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = _session.Session.Run(new[] { inputValue });
                (inputValue as IDisposable)?.Dispose();

                DisposableNamedOnnxValue? output = outputs.FirstOrDefault(o =>
                    string.Equals(o.Name, Info.OutputName, StringComparison.Ordinal));
                if (output is null)
                {
                    error = $"模型没有返回预期的输出「{Info.OutputName}」：实际返回 {string.Join(",", outputs.Select(o => o.Name))}。";
                    return false;
                }

                Tensor<float> logits = output.AsTensor<float>();
                int[] dims = logits.Dimensions.ToArray();
                if (!SegmentPostProcess.TryReadFeatureDims(dims, out int channels, out int featureHeight, out int featureWidth))
                {
                    error = $"分割输出形状不认识：输出「{Info.OutputName}」是秩 {dims.Length}，形状 [{string.Join(", ", dims)}]。";
                    return false;
                }

                float[] flat = logits.ToArray();
                int expected = channels * featureHeight * featureWidth;
                if (flat.Length != expected)
                {
                    error = $"分割输出长度不一致：期望 {expected}（{channels}×{featureHeight}×{featureWidth}），实际 {flat.Length}。";
                    return false;
                }

                if (!TryArgMax(flat, channels, featureHeight, featureWidth, out byte[] classMapBytes, out double[] classCounts, out error))
                {
                    return false;
                }

                // 类别图还原到原图尺寸。必须 nearest：类别序号是离散值，插值会造出不存在的类。
                if (!TryRestoreToSource(classMapBytes, featureWidth, featureHeight, srcWidth, srcHeight,
                        parameters.Interpolation, out HImage? classMap, out error))
                {
                    return false;
                }

                HOperatorSet.RegionToBin(classMap, out classRegion, 1, 0, srcWidth, srcHeight);
                HOperatorSet.Connection(classRegion, out HObject connected);
                HOperatorSet.CountObj(connected, out HTuple regionCountTuple);
                int regionCount = regionCountTuple.Length > 0 ? regionCountTuple[0].I : 0;
                connected.Dispose();

                double total = (double)srcWidth * srcHeight;
                double[] ratios = new double[channels];
                for (int i = 0; i < channels; i++) ratios[i] = total > 0 ? classCounts[i] / total : 0;

                int dominant = 0;
                for (int i = 1; i < channels; i++)
                {
                    if (classCounts[i] > classCounts[dominant]) dominant = i;
                }

                double foreground = 0;
                if (parameters.RegionMode == SegmentRegionMode.AllClasses)
                {
                    for (int i = 1; i < channels; i++) foreground += classCounts[i];
                }
                else if (parameters.ClassIndex >= 0 && parameters.ClassIndex < channels)
                {
                    foreground = classCounts[parameters.ClassIndex];
                }

                watch.Stop();

                result = new SegResultData
                {
                    ClassMap = classMap,
                    RegionMask = classMap,
                    Region = classRegion,
                    RegionCount = regionCount,
                    Width = srcWidth,
                    Height = srcHeight,
                    ClassPixelCounts = classCounts,
                    ClassRatios = ratios,
                    DominantClassIndex = dominant,
                    DominantClassName = contract.ClassNames.Count > dominant ? contract.ClassNames[dominant] : $"class_{dominant}",
                    ForegroundRatio = total > 0 ? foreground / total : 0,
                    ElapsedMs = watch.ElapsedMilliseconds,
                    FeatureWidth = featureWidth,
                    FeatureHeight = featureHeight
                };

                // 所有权转移：结果对象持有，交给调用方在管线收尾时释放。
                canvas = null;
                classRegion = null;
                return true;
            }
            catch (HOperatorException ex)
            {
                error = $"分割后处理 HALCON 失败：{ex.Message}";
                return false;
            }
            catch (Exception ex)
            {
                error = $"分割推理失败：{ex.GetType().Name}: {ex.Message}";
                return false;
            }
            finally
            {
                canvas?.Dispose();
                classRegion?.Dispose();
            }
        }

        private bool TryResolveInputSize(int configured, out int width, out int height, out string error)
        {
            error = string.Empty;
            if (configured > 0)
            {
                width = configured;
                height = configured;
                return true;
            }

            if (Info.HasStaticInputShape)
            {
                width = Info.InputWidth;
                height = Info.InputHeight;
                return true;
            }

            width = 0;
            height = 0;
            error = "无法确定模型输入尺寸：模型输入是动态形状，而配置里的「输入尺寸」是 0。" +
                    "请在预处理参数里填一个具体边长（语义分割模型常见 256 / 512 / 1024）。";
            return false;
        }

        /// <summary>把源图变成 3 通道 byte 画布，并按 plan 缩放 + 补边。</summary>
        private static bool TryBuildCanvas(HImage source, SegmentResizePlan plan, out HObject? canvas, out string error)
        {
            canvas = null;
            error = string.Empty;

            HObject? working = null;
            HObject? grayCanvas = null;
            HObject? scaled = null;
            HObject? positioned = null;
            HObject? sized = null;

            try
            {
                // 非 byte 先转 byte：模型输入是 0~255 的 ubyte，real 图直接指针读取会读出乱值。
                // HImage **没有 GetImageTypeName()**（CS1061），要用 HOperatorSet.GetImageType 取字符串。
                HOperatorSet.GetImageType(source, out HTuple typeTuple);
                string typeName = typeTuple.Length > 0 ? typeTuple[0].S : "byte";
                if (!string.Equals(typeName, "byte", StringComparison.OrdinalIgnoreCase))
                {
                    HOperatorSet.ConvertImageType(source, out HObject converted, "byte");
                    working = converted;
                }
                else
                {
                    HOperatorSet.CopyImage(source, out HObject copied);
                    working = copied;
                }

                HOperatorSet.CountChannels(working, out HTuple channelTuple);
                int channels = channelTuple.Length > 0 ? channelTuple[0].I : 1;

                if (channels == 1)
                {
                    HOperatorSet.Compose3(working, working, working, out HObject composed);
                    sized = composed;
                }
                else if (channels == 3)
                {
                    HOperatorSet.CopyImage(working, out HObject copied);
                    sized = copied;
                }
                else
                {
                    error = $"输入图像通道数为 {channels}，只支持 1 通道（自动补成 3）或 3 通道。";
                    return false;
                }

                HOperatorSet.ZoomImageSize(sized, out scaled, plan.ScaledWidth, plan.ScaledHeight, "bilinear");

                if (plan.Mode == SegmentResizeMode.Stretch)
                {
                    canvas = scaled;
                    scaled = null;
                    return true;
                }

                // Letterbox：先铺一张 3 通道的 114 灰画布，再把缩放图贴到 (PadY, PadX)。
                HOperatorSet.GenImageConst(out HObject sizeCarrier, "byte", plan.DstWidth, plan.DstHeight);
                HOperatorSet.GenImageProto(sizeCarrier, out grayCanvas, SegmentResizePlan.PadGrayValue);
                sizeCarrier.Dispose();

                HOperatorSet.Compose3(grayCanvas, grayCanvas, grayCanvas, out HObject canvasImage);
                positioned = canvasImage;

                HOperatorSet.GenRectangle1(out HObject targetRect,
                    plan.PadY, plan.PadX, plan.PadY + plan.ScaledHeight - 1, plan.PadX + plan.ScaledWidth - 1);
                HOperatorSet.ChangeDomain(scaled, targetRect, out HObject scaledPositioned);
                targetRect.Dispose();

                // overpaint_gray 要求通道数一致（否则 #3122），画布已无条件 3 通道。
                // 注意：overpaint_gray **只有两参且就地修改入参**（没有 out 版本，写三参报 CS1501），
                // 所以合并结果就在 positioned 上，不能再把它当"未被修改的底图"另存一份释放。
                HOperatorSet.OverpaintGray(positioned, scaledPositioned);
                scaledPositioned.Dispose();

                canvas = positioned;
                positioned = null;
                return true;
            }
            finally
            {
                working?.Dispose();
                sized?.Dispose();
                grayCanvas?.Dispose();
                scaled?.Dispose();
                positioned?.Dispose();
            }
        }

        /// <summary>HImage(3 通道 byte) → NCHW float（归一化 /255）。</summary>
        private static float[] ToNchwFloat(HObject image3Channel, int width, int height)
        {
            int plane = width * height;
            var data = new float[plane * 3];

            for (int c = 0; c < 3; c++)
            {
                HOperatorSet.AccessChannel(image3Channel, out HObject channel, c + 1);
                try
                {
                    HOperatorSet.GetImagePointer1(channel, out HTuple pointer, out _, out HTuple roiWidth, out HTuple roiHeight);
                    int planeWidth = roiWidth.I;
                    int planeHeight = roiHeight.I;
                    var buffer = new byte[planeWidth * planeHeight];
                    Marshal.Copy(pointer.IP, buffer, 0, buffer.Length);

                    int offset = c * plane;
                    for (int i = 0; i < buffer.Length && i < plane; i++)
                    {
                        // /255 少除或多除模型都会照跑，只是精度静默劣化。
                        data[offset + i] = buffer[i] / 255f;
                    }
                }
                finally
                {
                    channel.Dispose();
                }
            }

            return data;
        }

        /// <summary>逐像素 argmax：每条像素在所有类别通道里取最大值下标。</summary>
        private static bool TryArgMax(
            float[] logits, int channels, int height, int width, out byte[] classMap, out double[] classCounts, out string error)
        {
            classMap = Array.Empty<byte>();
            classCounts = new double[channels];
            error = string.Empty;

            int plane = height * width;
            if (channels > 255)
            {
                error = $"类别数 {channels} 超过 255，类别图无法用 byte 承载。请换用类别更少的模型，或改用实例分割插件。";
                return false;
            }

            var map = new byte[plane];
            var counts = new double[channels];

            for (int pixel = 0; pixel < plane; pixel++)
            {
                int best = 0;
                float bestValue = logits[pixel];
                for (int c = 1; c < channels; c++)
                {
                    float value = logits[c * plane + pixel];
                    if (value > bestValue)
                    {
                        bestValue = value;
                        best = c;
                    }
                }

                map[pixel] = (byte)best;
                counts[best]++;
            }

            classMap = map;
            classCounts = counts;
            return true;
        }

        /// <summary>把特征图尺寸的类别图还原到原图尺寸。</summary>
        private static bool TryRestoreToSource(
            byte[] classMap, int featureWidth, int featureHeight, int srcWidth, int srcHeight,
            SegmentInterpolation interpolation, out HImage? restored, out string error)
        {
            restored = null;
            error = string.Empty;

            GCHandle handle = default;
            HObject? raw = null;

            try
            {
                handle = GCHandle.Alloc(classMap, GCHandleType.Pinned);
                HOperatorSet.GenImage1(out raw, "byte", featureWidth, featureHeight, handle.AddrOfPinnedObject());

                if (featureWidth == srcWidth && featureHeight == srcHeight)
                {
                    HOperatorSet.CopyImage(raw, out HObject same);
                    restored = new HImage(same);
                    return true;
                }

                string method = interpolation == SegmentInterpolation.Nearest ? "nearest_neighbor" : "bilinear";
                HOperatorSet.ZoomImageSize(raw, out HObject zoomed, srcWidth, srcHeight, method);
                restored = new HImage(zoomed);
                return true;
            }
            catch (Exception ex)
            {
                error = $"类别图还原失败：{ex.Message}";
                return false;
            }
            finally
            {
                raw?.Dispose();
                if (handle.IsAllocated) handle.Free();
            }
        }

        /// <summary>把各类像素数格式化成可读文本（上屏与日志共用）。</summary>
        public static string DescribeCounts(SegResultData data, IReadOnlyList<string> classNames)
        {
            var parts = new List<string>();
            for (int i = 0; i < data.ClassPixelCounts.Length; i++)
            {
                string name = i < classNames.Count ? classNames[i] : $"class_{i}";
                parts.Add($"{name}={data.ClassPixelCounts[i].ToString("0", CultureInfo.InvariantCulture)}");
            }

            return string.Join("，", parts);
        }
    }
}
