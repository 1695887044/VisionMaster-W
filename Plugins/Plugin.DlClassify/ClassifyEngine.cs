using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using HalconDotNet;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Plugin.DlClassify
{
    /// <summary>分类参数（对应节点上的可调项）</summary>
    public sealed class ClassifyParams
    {
        /// <summary>缩放模式：letterbox（等比补边）或 stretch（拉伸）</summary>
        public ClassifyResizeMode ResizeMode { get; set; } = ClassifyResizeMode.Letterbox;

        /// <summary>
        /// 模型输入边长。0 = 用模型元数据里的静态输入尺寸。
        /// 两者都拿不到时必须报错 —— 猜一个尺寸会得到错位的预处理结果。
        /// </summary>
        public int InputSize { get; set; }

        /// <summary>结果里保留前 K 个类别（K 会被夹到 [1, 类别数]）</summary>
        public int TopK { get; set; } = 3;
    }

    /// <summary>
    /// 分类推理引擎：HImage → 预处理（缩放 + 补边）→ ONNX Runtime → 概率归一化 → TopK。
    ///
    /// 会话的生命周期不归这里管（见 <see cref="ClassifySession"/>），本类只是"用"它。
    /// </summary>
    public sealed class ClassifyEngine
    {
        private readonly ClassifySession _session;

        public ClassifyModelInfo Info => _session.Info;

        public ClassifyEngine(ClassifySession session) => _session = session;

        /// <summary>
        /// 对一张图做分类。失败时 error 说明原因，不抛异常。
        /// </summary>
        public bool TryClassify(HImage? image, ClassifyParams parameters, out ClassifyResult? result, out string error)
        {
            result = null;
            error = string.Empty;

            if (image == null || !image.IsInitialized())
            {
                error = "输入图像为空";
                return false;
            }

            // 任务类型先拦：检测/分割模型的输出在形状上与分类相近，等解码完再发现就晚了。
            if (!ClassifyPostProcess.IsSupportedTask(Info.Task, out error)) return false;

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

                // 输入项要显式释放：它内部持有 OrtValue（原生资源），靠 GC 回收会延迟释放。
                // NamedOnnxValue 的静态类型不是 IDisposable（实现类才是），所以要 as 一次。
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

                    // ---- 4. 布局校验：读错布局会给出"看着正常但全错"的结果 ----
                    if (!ClassifyPostProcess.TrySniffLayout(dims, out int classCount, out error))
                    {
                        error += $"\r\n模型：{Info.ModelPath}\r\n实际输出形状：[{string.Join(",", dims)}]";
                        return false;
                    }

                    // 张量长度应等于类别数（[1, C] 展平后就是 C 个元素）
                    if (data.Length < classCount)
                    {
                        error = $"输出张量长度 {data.Length} 小于类别数 {classCount}，模型输出与元数据不一致。"
                              + $"\r\n实际输出形状：[{string.Join(",", dims)}]";
                        return false;
                    }

                    // ---- 5. 概率归一化 → TopK ----
                    var probabilities = ClassifyPostProcess.NormalizeToProbabilities(data.Take(classCount).ToArray());
                    var topK = ClassifyPostProcess.TopK(probabilities, Info.ClassNames, parameters.TopK);

                    var best = topK[0];
                    result = new ClassifyResult
                    {
                        PredictedClassId = best.ClassId,
                        PredictedClassName = best.ClassName,
                        Confidence = best.Probability,
                        Scores = probabilities,
                        ClassNames = Info.ClassNames,
                        TopK = topK,
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
                error = $"分类推理失败：{ex.Message}";
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

        /// <summary>确定输入宽高：配置优先（方形），其次模型静态形状（可非方形）；都没有就报错（不猜）</summary>
        private bool ResolveInputSize(ClassifyParams parameters, out int width, out int height, out string error)
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
                  + $"请填写一个值（分类模型常见为 224；本模型输入形状 {Info.DescribeShapes()}）";
            return false;
        }

        /// <summary>
        /// 构造模型输入画布：转 byte → 转 3 通道 → 按模式缩放 → 居中补边。
        ///
        /// 【为什么单通道要 compose3】分类模型要 3 通道输入，缺了会抛通道数错误（实测过）。
        /// 【为什么补边用 114 灰而不是黑】见 ResizePlan.PadGrayValue —— 黑边是模型没见过的强边缘。
        /// 【为什么 overpaint 前画布无条件补 3 通道】实测 overpaint_gray 要求源图与目标图通道数一致，
        /// 否则抛 #3122；源图已 compose3 恒为 3 通道，所以画布必须无条件 3 通道（与 Plugin.Yolo 同款坑）。
        /// </summary>
        private bool TryBuildCanvas(HImage source, ResizePlan plan, out HObject? canvas, out string error)
        {
            canvas = null;
            error = string.Empty;

            var temps = new List<IDisposable>();
            try
            {
                // 统一成 byte：GetImagePointer1 按 byte 取缓冲，非 byte 图（uint2/real 等）会取错。
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

                if (plan.Mode == ClassifyResizeMode.Stretch)
                {
                    HOperatorSet.ZoomImageSize(working, out HObject stretched, plan.DstWidth, plan.DstHeight, "bilinear");
                    temps.Add(stretched);

                    canvas = stretched;
                    temps.Remove(stretched);
                    return true;
                }

                // ---- Letterbox：等比缩放 + 居中贴到 114 灰画布 ----
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

                // change_domain：值不变、域变（把缩放图搬到居中位置），再覆写到画布
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

        /// <summary>
        /// 把 3 通道 byte 图转成 NCHW 的 float 数组，并归一化到 0~1。
        ///
        /// 【为什么用指针而不是 get_grayval】640×640×3 是 122 万个像素，逐个取值的调用开销会远超推理本身。
        /// 【归一化为什么是 /255】分类训练时把像素归一到 0~1。少除或多除，模型照样能跑、也不报错，
        /// 只是精度静默劣化 —— 属于必须在代码里写清并验证的一步。
        /// 【通道顺序】HALCON 的 3 通道按 R,G,B 存，与 Ultralytics 期望的 RGB 一致，故通道 1/2/3 直接映射。
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
