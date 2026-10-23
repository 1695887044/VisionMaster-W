using HalconDotNet;
using System;

namespace Plugin.ImageDiff
{
    /// <summary>
    /// 差分核心算法（纯 HALCON 调用，无状态、可单测）。
    ///
    /// 设计纪律（与 Plugin.PreProcessing/Services/PreprocessHService 同一套）：
    ///  · 每个中间 <see cref="HObject"/> 都在 try/finally 里 Dispose，失败路径不泄漏；
    ///  · 出错抛中文异常，由插件层转成"明确失败"，不返回入参图冒充结果；
    ///  · 尺寸/通道不一致由调用方（插件层）按 <see cref="SizeMismatchPolicy"/> 处理，
    ///    本类只接受已对齐的两幅单通道图。
    /// </summary>
    internal static class ImageDiffService
    {
        /// <summary>
        /// 两图尺寸与通道数（用于守卫与日志）。
        /// </summary>
        internal static (int Width, int Height, int Channels) Shape(HImage image)
        {
            HOperatorSet.GetImageSize(image, out HTuple w, out HTuple h);
            HOperatorSet.CountChannels(image, out HTuple c);
            return ((int)w.D, (int)h.D, (int)c.D);
        }

        /// <summary>
        /// 转灰度（多通道走 rgb1_to_gray；单通道原样返回**新拷贝**，便于调用方统一释放）。
        /// </summary>
        internal static HImage ToGray(HImage image)
        {
            HOperatorSet.CountChannels(image, out HTuple channels);
            if ((int)channels.D >= 3)
            {
                HOperatorSet.Rgb1ToGray(image, out HObject gray);
                return new HImage(gray);
            }
            if ((int)channels.D == 1)
            {
                HOperatorSet.CopyImage(image, out HObject copy);
                return new HImage(copy);
            }
            throw new InvalidOperationException(
                $"图像通道数为 {channels.D}（既不是灰度也不是三通道），无法做差分。请在上游统一成灰度或 RGB。");
        }

        /// <summary>
        /// 造一张与模板同尺寸同类型的常数图（用于把 double 常数变成第二操作数）。
        /// HalconDotNet 的 add_image/sub_image/mult_image 第二操作数必须是 HObject，不能直接传数值。
        /// </summary>
        private static HImage ConstantLike(HImage reference, double value)
        {
            HOperatorSet.GetImageSize(reference, out HTuple w, out HTuple h);
            string type = "byte";
            try
            {
                HOperatorSet.GetImageType(reference, out HTuple t);
                if (t.Length > 0 && !string.IsNullOrEmpty(t.S)) type = t.S;
            }
            catch
            {
                // 取类型失败按 byte 处理：差分链的常见起点就是 8 位图
            }

            HObject? proto = null;
            try
            {
                HOperatorSet.GenImageConst(out proto, type, w, h);
                HOperatorSet.GenImageProto(proto, out HObject body, value);
                return new HImage(body);
            }
            finally
            {
                proto?.Dispose();
            }
        }

        /// <summary>
        /// 逐像元求差并按 <see cref="DiffAlgorithm"/> 与 <see cref="DiffPolarity"/> 收成一个"差异强度"图。
        ///
        /// 返回的是 **byte 单通道差异图**（0 = 无差异，255 = 最大差异）。为什么统一成 byte：
        /// 下游要阈值化、要连通域、要上屏，三者对 byte 的口径最稳；相对差/有符号差都在内部归一化。
        /// </summary>
        /// <param name="reference">基准图（灰度）</param>
        /// <param name="test">被测图（灰度，尺寸与基准一致）</param>
        /// <param name="algorithm">差分算法</param>
        /// <param name="polarity">极性（只影响"哪些方向算差异"）</param>
        /// <param name="ambientFloor">相对差的分母下限（仅 Relative 用）</param>
        /// <param name="signedDisplay">有符号差显示时的零点（仅 Signed 用，通常 128）</param>
        internal static HImage ComputeDifference(
            HImage reference,
            HImage test,
            DiffAlgorithm algorithm,
            DiffPolarity polarity,
            double ambientFloor,
            double signedDisplay)
        {
            HImage? refReal = null;
            HImage? testReal = null;
            HImage? posDiff = null;   // 变亮：test − ref
            HImage? negDiff = null;   // 变暗：ref − test
            try
            {
                // 统一抬到 real 域：byte 图直接相减会**饱和截断**（0-30 会变成 0 而不是 30），
                // 结果看似正常其实全错——这是差分插件最容易踩的坑，必须转 real 再算。
                HOperatorSet.ConvertImageType(reference, out HObject r, "real");
                refReal = new HImage(r);
                HOperatorSet.ConvertImageType(test, out HObject t, "real");
                testReal = new HImage(t);

                HOperatorSet.SubImage(testReal, refReal, out HObject pos, 1.0, 0.0);
                posDiff = new HImage(pos);
                HOperatorSet.SubImage(refReal, testReal, out HObject neg, 1.0, 0.0);
                negDiff = new HImage(neg);

                HObject selected;
                switch (polarity)
                {
                    case DiffPolarity.Brighter:
                        selected = posDiff;
                        break;
                    case DiffPolarity.Darker:
                        selected = negDiff;
                        break;
                    default:
                    {
                        // 双侧：取两方向的最大值（= |差|）。为什么不用 abs_image：
                        // abs_image 只有 3 参重载且对 real 域行为一致，但两侧最大值语义更直观，
                        // 也便于后面按极性单独取值时复用同一份 pos/neg。
                        HOperatorSet.MaxImage(posDiff, negDiff, out HObject mx);
                        selected = mx;
                        break;
                    }
                }

                if (algorithm == DiffAlgorithm.Relative)
                {
                    // |a−b| / max(ref, floor) × 100 → 截到 0..255
                    // 分母下限 ambientFloor 防"参考图接近全黑处"放大成噪声。
                    HImage? floor = null;
                    HImage? safeRef = null;
                    HImage? ratio = null;
                    try
                    {
                        floor = ConstantLike(refReal, ambientFloor);
                        HOperatorSet.MaxImage(refReal, floor, out HObject sr);
                        safeRef = new HImage(sr);
                        HOperatorSet.DivImage(selected, safeRef, out HObject rv, 100.0, 0.0);
                        ratio = new HImage(rv);
                        HOperatorSet.ConvertImageType(ratio, out HObject b, "byte");
                        return new HImage(b);
                    }
                    finally
                    {
                        floor?.Dispose();
                        safeRef?.Dispose();
                        ratio?.Dispose();
                    }
                }

                if (algorithm == DiffAlgorithm.Signed)
                {
                    // 有符号差：先把差平移到 signedDisplay 附近，让人肉眼能分正负；判定仍用绝对值。
                    HImage? bias = null;
                    HImage? signedImg = null;
                    try
                    {
                        bias = ConstantLike(posDiff, signedDisplay);
                        // signed = (posDifff − negDiff) + bias，等价于 (test − ref) + bias
                        HOperatorSet.SubImage(posDiff, negDiff, out HObject delta, 1.0, 0.0);
                        HImage? deltaImg = null;
                        try
                        {
                            deltaImg = new HImage(delta);
                            HOperatorSet.AddImage(deltaImg, bias, out HObject sv, 1.0, 0.0);
                            signedImg = new HImage(sv);
                            HOperatorSet.ConvertImageType(signedImg, out HObject b, "byte");
                            return new HImage(b);
                        }
                        finally
                        {
                            deltaImg?.Dispose();
                        }
                    }
                    finally
                    {
                        bias?.Dispose();
                        signedImg?.Dispose();
                    }
                }

                // Absolute：selected 已是实数差，直接截到 byte（超过 255 的饱和到 255）。
                // selected 要么是 posDiff/negDiff 本身（外层 finally 释放），要么是本作用域新建的
                // MaxImage 结果（下面 finally 释放）——两条路径都要释放且只能释放一次。
                bool selectedIsOwned = polarity == DiffPolarity.Both;
                HImage target = selectedIsOwned ? new HImage(selected) : (polarity == DiffPolarity.Brighter ? posDiff! : negDiff!);
                try
                {
                    HOperatorSet.ConvertImageType(target, out HObject absByte, "byte");
                    return new HImage(absByte);
                }
                finally
                {
                    if (selectedIsOwned) target.Dispose();
                }
            }
            finally
            {
                refReal?.Dispose();
                testReal?.Dispose();
                posDiff?.Dispose();
                negDiff?.Dispose();
            }
        }

        /// <summary>
        /// 按阈值把差异图变成"缺陷候选区域"，并按 <see cref="DiffAggregation"/> 返回统计结果。
        /// 返回的 region 由调用方释放。
        /// </summary>
        internal static (HObject Region, double TotalArea, int Count, double MaxComponentArea) Segment(
            HImage diffImage,
            double threshold,
            DiffAggregation aggregation,
            double minArea,
            HObject? maskRegion)
        {
            HObject? thresholded = null;
            HObject? masked = null;
            HObject? selected = null;
            double totalArea;
            int count;
            double maxArea;
            try
            {
                HOperatorSet.Threshold(diffImage, out thresholded, threshold, 255.0);

                HObject effective = thresholded;
                if (maskRegion != null)
                {
                    HOperatorSet.Intersection(thresholded, maskRegion, out masked);
                    effective = masked;
                }

                // 面积统计：先按 8 连通拆成独立区域，逐片算面积，再按聚合口径给出结果。
                // 为什么一定要 Connection 一次：Threshold 出来的不是连通域（同一片差异里的像素
                // 属于同一个 region 但 HOperatorSet.AreaCenter 只给一个总面积），
                // 不拆开就永远数不出"几处"、也拿不到"最大单片面积"。
                HOperatorSet.Connection(effective, out HObject components);
                HObject? bigEnough = null;
                try
                {
                    // 面积下限按"单片面积"筛，而不是 opening：
                    // opening 会把细长划痕（宽度小于圆盘直径）整条抹掉，而划痕恰恰是差分最常见的缺陷。
                    HObject filtered = components;
                    if (minArea > 1)
                    {
                        HOperatorSet.SelectShape(components, out bigEnough, "area", "and", minArea, double.MaxValue);
                        filtered = bigEnough;
                    }

                    HOperatorSet.CountObj(filtered, out HTuple countObj);
                    count = (int)countObj.D;

                    HOperatorSet.AreaCenter(filtered, out HTuple areas, out _, out _);
                    totalArea = 0.0;
                    maxArea = 0.0;
                    for (int i = 0; i < areas.Length; i++)
                    {
                        double a = areas[i].D;
                        totalArea += a;
                        if (a > maxArea) maxArea = a;
                    }

                    // Total 口径：片数归为 1，下游只看总面积，避免"几百片噪点"刷屏
                    if (aggregation == DiffAggregation.Total)
                    {
                        count = count > 0 ? 1 : 0;
                    }

                    HOperatorSet.CopyObj(filtered, out selected, 1, -1);
                    return (selected, totalArea, count, maxArea);
                }
                finally
                {
                    components.Dispose();
                    bigEnough?.Dispose();
                }
            }
            finally
            {
                thresholded?.Dispose();
                masked?.Dispose();
            }
        }

        /// <summary>
        /// 把差异强度图的极值取出来（用于输出"最大差异值"与判断"是否有差异"）。
        /// </summary>
        internal static (double Min, double Max) Range(HImage image, HObject? region)
        {
            HObject scope = region ?? image;
            HOperatorSet.MinMaxGray(scope, image, 0.0, out HTuple min, out HTuple max, out _);
            return (min.D, max.D);
        }

        /// <summary>
        /// 把差异图叠成一张"原图 + 差异标注区"的图（供上屏与输出端口）。
        /// 返回新图，调用方负责释放。
        /// </summary>
        /// <remarks>
        /// 这里<b>不画文字</b>：<c>HOperatorSet.SetColor</c>/<c>DispText</c> 是<b>窗口句柄</b> API
        /// （第一个参数是 HTuple 窗口、第 3 个是 coordSystem），对 <see cref="HObject"/> 图像调不出来。
        /// 标题与统计指标由调用方通过 <c>PublishPreview(image, viewIndex, title, info, annotations)</c>
        /// 传给上屏层，由显示层负责绘制 —— 这样也顺带满足 R39（文字宽度只有烘焙层能量）。
        /// </remarks>
        internal static HImage RenderOverlay(HImage baseImage, HObject? regions)
        {
            HImage? colorBase = null;
            try
            {
                HOperatorSet.CopyImage(baseImage, out HObject copy);
                HImage copied = new HImage(copy);
                try
                {
                    // Compose3 的三个入参都必须是**单通道**图，否则 HALCON 报 #1503
                    // "Wrong number of values of object parameter 3 in operator compose3"。
                    // 插件主流程进来的是 ToGray 之后的单通道图，但 RenderOverlay 是 internal 公共入口，
                    // 不能假设上游一定已经灰度化 —— 这里自己兜一层（三通道转灰度，单通道直接拷）。
                    if (ToGray(copied) is { } grayBase)
                    {
                        colorBase = grayBase;
                    }
                }
                finally
                {
                    copied.Dispose();
                }
                // 三个通道各自是独立对象：HALCON 的 compose3 不接受同一个对象实例重复作为多个通道
                // （会报 #1503），必须各自再拷一份单通道图。
                HImage chR = ToGray(colorBase!);
                HImage chG = ToGray(colorBase!);
                HImage? overlay = null;
                try
                {
                    if (regions != null)
                    {
                        // ★ 空区域必须先判掉：regions 里一个对象都没有时（良品、无差异，最常见的情况），
                        //   HOperatorSet.PaintRegion 内部索引越界，HalconDotNet 抛 IndexOutOfRangeException，
                        //   随后被上层包成 "HALCON error #1503: Wrong number of values of object parameter 3
                        //   in operator compose3" 这种完全指不到真正原因的错误。
                        //   判据：HObject.CountObj() == 0 就跳过整个叠色步骤，直接输出灰度底图。
                        HOperatorSet.CountObj(regions, out HTuple regionCount);
                        if (regionCount.Length > 0 && regionCount[0].I > 0)
                        {
                            // 造「区域掩膜」：底图全 0，regions 内部刷成 255。
                            // 底图必须全 0 —— PaintRegion 只改区域内的像素，区域外保持底图原值，
                            // 用 GenImageProto(...,255.0) 会让整张图都是 255，叠上去等于没画。
                            // 底图用 chR：它已经是「与底图同尺寸同类型的单通道图」，位深跟着原图走。
                            HOperatorSet.GenImageProto(chR, out HObject proto, 0.0);
                            HImage protoImg = new HImage(proto);
                            HImage? mask = null;
                            try
                            {
                                HOperatorSet.PaintRegion(regions, protoImg, out HObject paintedH, 255.0, "fill");
                                mask = new HImage(paintedH);
                                // Compose3(red, green, blue)：原图给 R/G，掩膜给 B →
                                // 差异区 (ref, ref, 255) 偏青，非差异区 (ref, ref, 0) 保持原灰度。
                                // 不把掩膜压进 R 通道：红色通道被拉满会吃掉缺陷本身的明暗细节。
                                HOperatorSet.Compose3(chR, chG, mask, out HObject comp);
                                overlay = new HImage(comp);
                            }
                            finally
                            {
                                mask?.Dispose();
                                protoImg.Dispose();
                            }
                        }
                    }

                    // 统一在返回前拷一份：无区域时 overlay 为空，就把灰度底图拷出去。
                    // 这样调用方拿到的永远是独立对象，不会被本方法的 finally 释放掉。
                    HImage source = overlay ?? chR;
                    HOperatorSet.CopyImage(source, out HObject owned);
                    return new HImage(owned);
                }
                finally
                {
                    overlay?.Dispose();
                    chR.Dispose();
                    chG.Dispose();
                }
            }
            finally
            {
                colorBase?.Dispose();
            }
        }
    }
}
