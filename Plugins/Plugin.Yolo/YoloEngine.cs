using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using HalconDotNet;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Plugin.Yolo
{
    /// <summary>检测参数（对应节点上的可调项）</summary>
    public sealed class YoloParams
    {
        /// <summary>置信度阈值：低于它的候选直接丢弃。默认 0.25 与 Ultralytics 默认一致</summary>
        public float ConfidenceThreshold { get; set; } = 0.25f;

        /// <summary>NMS 的 IoU 阈值：同类框重叠超过它就只留置信度高的那个</summary>
        public float NmsIoU { get; set; } = 0.45f;

        /// <summary>最多保留多少个目标（按置信度排序后截断）。0 = 不限</summary>
        public int MaxDetections { get; set; } = 100;

        /// <summary>
        /// 只保留这些类别（空 = 不过滤）。用于"我只关心缺陷这一类"的场景，
        /// 既减少下游数据量，也避免无关目标干扰判定。
        /// </summary>
        public int[]? ClassFilter { get; set; }

        /// <summary>
        /// letterbox 的目标边长。0 = 用模型元数据里的静态输入尺寸。
        /// 两者都拿不到时必须报错 —— 猜一个尺寸会得到偏移的框。
        /// </summary>
        public int InputSize { get; set; }
    }

    /// <summary>一次检测的完整结果</summary>
    public sealed class YoloResult
    {
        /// <summary>最终目标（已 NMS、已换算回原图坐标、已按置信度降序）</summary>
        public required IReadOnlyList<Detection> Detections { get; init; }

        /// <summary>letterbox 几何（排障用：框偏了先看这里）</summary>
        public required LetterboxInfo Letterbox { get; init; }

        /// <summary>推理耗时（毫秒）。CPU 版 YOLOv8n 在 640 输入下大致几十毫秒量级</summary>
        public required double ElapsedMs { get; init; }

        public int Count => Detections.Count;
    }

    /// <summary>
    /// YOLO 推理引擎：HImage → 预处理（letterbox）→ ONNX Runtime → 解码 / NMS / 坐标换算 → 结果。
    ///
    /// 会话的生命周期不归这里管
    /// ---------
    /// <see cref="YoloSession"/> 是进程级共享 + 引用计数的，由插件层 Acquire/Dispose。
    /// 本类只是"用"这个会话，不负责它的生死 —— 否则多个插件实例会各自释放，把别人在用的会话拆掉。
    /// </summary>
    public sealed class YoloEngine
    {
        private readonly YoloSession _session;

        public YoloModelInfo Info => _session.Info;

        public YoloEngine(YoloSession session) => _session = session;

        /// <summary>
        /// 对一张图做检测。失败时 <paramref name="error"/> 说明原因，不抛异常。
        /// </summary>
        public bool TryDetect(HImage? image, YoloParams parameters, out YoloResult? result, out string error)
        {
            result = null;
            error = string.Empty;

            if (image == null || !image.IsInitialized())
            {
                error = "输入图像为空";
                return false;
            }

            // 元数据里的任务类型先拦：分割/姿态模型的输出**在形状上与检测无法区分**，
            // 等解码完再发现就晚了（那时已经输出了一堆看着正常的垃圾框）。放在推理之前，省一次白跑。
            if (!YoloPostProcess.IsSupportedTask(Info.Task, out error)) return false;

            int inputSize = ResolveInputSize(parameters, out error);
            if (inputSize <= 0) return false;

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

                var letterbox = LetterboxInfo.Compute(srcWidth, srcHeight, inputSize, inputSize);

                // ---- 1. 预处理成 letterbox 画布（byte，3 通道）----
                if (!TryBuildLetterboxImage(image, letterbox, out HObject? canvas, out error)) return false;
                owned.Add(canvas!);

                // ---- 2. 画布 → NCHW float 张量 ----
                float[] tensorData = ToNchwFloat(canvas!, inputSize, inputSize);

                var inputTensor = new DenseTensor<float>(tensorData, new[] { 1, 3, inputSize, inputSize });

                // 输入项要显式释放：它内部持有 OrtValue（原生资源），靠 GC 回收会延迟释放。
                // 注意 NamedOnnxValue 的静态类型不是 IDisposable（实现类才是），所以要 as 一次。
                var inputValue = NamedOnnxValue.CreateFromTensor(Info.InputName, inputTensor);

                try
                {
                    // ---- 3. 推理 ----
                    var stopwatch = Stopwatch.StartNew();
                    using var outputs = _session.Session.Run(new[] { inputValue });
                    stopwatch.Stop();

                    var outputValue = outputs.First();
                    var outputTensor = outputValue.AsTensor<float>();
                    int[] dims = outputTensor.Dimensions.ToArray();
                    float[] data = outputTensor.ToArray();

                    // ---- 4. 布局校验：读错布局会给出"看着正常但全错"的框 ----
                    if (!YoloPostProcess.TrySniffLayout(dims, out _, out int classCount, out error))
                    {
                        // 输出形状写成一行，方便现场把这句话直接贴回来定位
                        error += $"\r\n模型：{Info.ModelPath}\r\n实际输出形状：[{string.Join(",", dims)}]";
                        return false;
                    }

                    int anchorCount = dims[^1];

                    // ---- 5. 解码 → 坐标换算 → NMS → 类别过滤 → 截断 ----
                    var candidates = YoloPostProcess.DecodeV8Transposed(
                        data, classCount, anchorCount, parameters.ConfidenceThreshold, Info.ClassNames);

                    var mapped = candidates.Select(d =>
                    {
                        float x1 = d.X1, y1 = d.Y1, x2 = d.X2, y2 = d.Y2;
                        letterbox.MapBackToOriginal(ref x1, ref y1, ref x2, ref y2);
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

                    var afterNms = YoloPostProcess.Nms(mapped, parameters.NmsIoU);

                    if (parameters.ClassFilter is { Length: > 0 })
                    {
                        var wanted = new HashSet<int>(parameters.ClassFilter);
                        afterNms = afterNms.Where(d => wanted.Contains(d.ClassId)).ToList();
                    }

                    if (parameters.MaxDetections > 0 && afterNms.Count > parameters.MaxDetections)
                        afterNms = afterNms.Take(parameters.MaxDetections).ToList();

                    result = new YoloResult
                    {
                        Detections = afterNms,
                        Letterbox = letterbox,
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
                error = $"YOLO 推理失败：{ex.Message}";
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

        /// <summary>确定 letterbox 目标边长：配置优先，其次模型静态形状；都没有就报错（不猜）</summary>
        private int ResolveInputSize(YoloParams parameters, out string error)
        {
            error = string.Empty;

            if (parameters.InputSize > 0) return parameters.InputSize;

            if (Info.HasStaticInputShape) return Info.InputWidth;

            error = "无法确定模型输入尺寸：模型是动态形状，且节点配置里没指定「输入尺寸」。"
                  + $"请填写一个值（常见为 640；本模型输入形状 {Info.DescribeShapes()}）";
            return 0;
        }

        /// <summary>
        /// 构造 letterbox 图：等比缩放 → 居中贴到填 114 的画布上。
        ///
        /// 【为什么用 change_domain + overpaint_gray 而不是 tile_images_offset】
        /// tile_images_offset 的背景填充值是固定的（0 = 黑），而 YOLO 训练时补的是 114 灰。
        /// 黑边会让模型把边界当成强边缘，贴边目标的框会系统性偏移。
        /// 所以先造一张 114 的画布，再把缩放图的**定义域**换成目标位置的矩形（change_domain，
        /// 值不变、域变），最后把值覆写到画布上。
        /// </summary>
        private bool TryBuildLetterboxImage(
            HImage source, LetterboxInfo letterbox, out HObject? canvas, out string error)
        {
            canvas = null;
            error = string.Empty;

            var temps = new List<IDisposable>();
            try
            {
                HOperatorSet.CountChannels(source, out HTuple channelCount);
                int channels = channelCount.I;

                HObject working = source;
                if (channels == 1)
                {
                    // 单通道补成 3 通道：模型要 3 通道输入，缺了会抛通道数错误（实测过）
                    HOperatorSet.Compose3(source, source, source, out HObject composed);
                    temps.Add(composed);
                    working = composed;
                }
                else if (channels != 3)
                {
                    error = $"图像通道数为 {channels}，只支持 1 通道（自动补成 3）或 3 通道。"
                          + "请在上游用预处理节点转成灰度或 RGB";
                    return false;
                }

                // 等比缩放到 letterbox 内尺寸
                HOperatorSet.ZoomImageSize(
                    working, out HObject scaled, letterbox.ScaledWidth, letterbox.ScaledHeight, "bilinear");
                temps.Add(scaled);

                // 114 灰画布。
                // 【实测踩到】overpaint_gray 要求源图与目标图**通道数一致**，否则抛
                // HALCON #3122「Number of channels in the input parameters are different」。
                // 【第一次修错了】原以为"原图是 3 通道才需要把画布补成 3 通道"——
                // 错在判断依据：上面已把单通道输入 Compose3 成 3 通道，
                // 所以**送到 overpaint 的源图恒为 3 通道**，画布就必须无条件补成 3 通道。
                // 只按"原图通道数"判断时，单通道输入（工业相机最常见的灰度图）必然踩这个错。
                HOperatorSet.GenImageConst(out HObject sizeCarrier, "byte", letterbox.DstWidth, letterbox.DstHeight);
                temps.Add(sizeCarrier);
                HOperatorSet.GenImageProto(sizeCarrier, out HObject grayCanvas, LetterboxInfo.PadGrayValue);
                temps.Add(grayCanvas);

                HOperatorSet.Compose3(grayCanvas, grayCanvas, grayCanvas, out HObject canvasImage);
                temps.Add(canvasImage);

                // 把缩放图的域换到居中位置，再覆写到画布
                HOperatorSet.GenRectangle1(
                    out HObject targetRect,
                    letterbox.PadY,
                    letterbox.PadX,
                    letterbox.PadY + letterbox.ScaledHeight - 1,
                    letterbox.PadX + letterbox.ScaledWidth - 1);
                temps.Add(targetRect);

                HOperatorSet.ChangeDomain(scaled, targetRect, out HObject positioned);
                temps.Add(positioned);

                HOperatorSet.OverpaintGray(canvasImage, positioned);

                // 画布要交出去，从待释放列表里摘掉
                temps.Remove(canvasImage);
                canvas = canvasImage;
                return true;
            }
            catch (Exception ex)
            {
                error = $"预处理（letterbox）失败：{ex.Message}";
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

        /// <summary>
        /// 把 3 通道 byte 图转成 NCHW 的 float 数组，并归一化到 0~1。
        ///
        /// 【为什么用指针而不是 get_grayval】640×640×3 是 122 万个像素，逐个取值的调用开销
        /// 会远超推理本身。GetImagePointer1 直接拿到底层缓冲，一次 Marshal.Copy 拷完。
        ///
        /// 【归一化为什么是 /255】YOLO 训练时把像素归一到 0~1。这里少除或多除，
        /// 模型照样能跑、也不报错，只是精度静默劣化 —— 属于必须在代码里写清并验证的一步。
        /// </summary>
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
