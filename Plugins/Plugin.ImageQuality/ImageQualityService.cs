using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Plugin.ImageQuality
{
    /// <summary>
    /// 成像质量指标核心算法（纯 HALCON 调用，无状态、可单测）。
    ///
    /// 口径纪律：
    ///  · 所有指标都在「有效域」内算：接了 Roi 就先 reduce_domain，没接就用图像自带域；
    ///  · 灰度指标统一归一化到 0~255 尺度输出（uint2 用 255/65535 缩放），这样换位深不换阈值；
    ///  · 对焦用 sobel_amp('sum_abs') 的均值（本仓 pcb_focus 序列实测比 laplace 方差平滑）；
    ///  · 曝光用 0 / 满量程两端的截断像素占比（百分比，0~100）；
    ///  · 对比度用 P5~P95 展宽（0~255 尺度）；
    ///  · 饱和度用 HSV 的 S 通道均值（0~255）；灰度图不算，输出 NaN 并说明跳过。
    ///
    /// 不做的事：不修改输入图、不做图像增强、不写全局变量、不判 OK/NG（判定在插件层按可选阈值做）。
    /// </summary>
    internal static class ImageQualityService
    {
        /// <summary>一轮算出的全部指标与明细。</summary>
        internal sealed class QualityMetrics
        {
            public double FocusRaw;
            public double FocusScore;
            public double ExposureRaw;          // 截断总占比，百分比 0~100
            public double ExposureScore;
            public double ContrastRaw;          // P95 - P5，0~255 尺度
            public double ContrastScore;
            public double SaturationRaw = double.NaN;
            public double SaturationScore;
            public bool HasColor;

            public double TruncLowPercent;
            public double TruncHighPercent;
            public double MeanGray;
            public double MinGray;
            public double MaxGray;
            public double P5;
            public double P95;
            public double StdDev;

            public int Channels;
            public string ImageType = string.Empty;
            public long PixelCount;
            public readonly List<string> Details = new();
        }

        /// <summary>
        /// 计算全部质量指标。失败抛 <see cref="InvalidOperationException"/>（中文原因），
        /// 由插件层转成明确的 <c>Fail</c>，不返回假值。
        /// </summary>
        internal static QualityMetrics Measure(
            HImage image,
            HObject? roi,
            double focusRef,
            double contrastRef,
            double saturationRef,
            double maxTruncationPercent,
            int minRoiPixels)
        {
            var m = new QualityMetrics();

            HOperatorSet.CountChannels(image, out HTuple channelTuple);
            m.Channels = channelTuple.Length > 0 ? channelTuple[0].I : 0;
            HOperatorSet.GetImageType(image, out HTuple typeTuple);
            m.ImageType = typeTuple.Length > 0 ? typeTuple.S : string.Empty;

            if (m.ImageType != "byte" && m.ImageType != "uint2")
            {
                throw new InvalidOperationException(
                    $"图像类型 {m.ImageType} 不受支持；v1 只支持 byte / uint2（相机原始位深）。请在上游用「预处理」转成 byte 后再检查。");
            }
            if (m.Channels != 1 && m.Channels != 3 && m.Channels != 4)
            {
                throw new InvalidOperationException(
                    $"图像通道数为 {m.Channels}，v1 只支持 1（灰度）/ 3（RGB）/ 4（RGBA）。");
            }

            HImage? work = null;
            HImage? gray = null;
            HImage? colorByte = null;
            HObject? domain = null;
            HImage? real = null;
            HImage? edge = null;
            HObject? saturation = null;
            try
            {
                // ── 有效域：Roi 优先，其次图像自带域 ────────────────────────────
                if (roi != null && roi.IsInitialized())
                {
                    HOperatorSet.AreaCenter(roi, out HTuple roiArea, out _, out _);
                    double area = roiArea.Length > 0 ? roiArea[0].D : 0.0;
                    if (area < minRoiPixels)
                    {
                        throw new InvalidOperationException(
                            $"ROI 有效像素 {area:F0} 小于下限 {minRoiPixels}，指标不可信。请扩大 ROI，或断开 Roi 端口按全图检查。");
                    }
                    HOperatorSet.ReduceDomain(image, roi, out HObject reduced);
                    work = new HImage(reduced);
                }
                else
                {
                    HOperatorSet.CopyImage(image, out HObject copied);
                    work = new HImage(copied);
                }

                HOperatorSet.GetDomain(work, out HObject dom);
                domain = dom;
                HOperatorSet.AreaCenter(domain, out HTuple areaTuple, out _, out _);
                m.PixelCount = areaTuple.Length > 0 ? (long)Math.Round(areaTuple[0].D) : 0L;
                if (m.PixelCount < minRoiPixels)
                {
                    throw new InvalidOperationException(
                        $"有效像素 {m.PixelCount} 小于下限 {minRoiPixels}，指标不可信。");
                }

                HOperatorSet.GetImageSize(work, out HTuple widthTuple, out HTuple heightTuple);
                int width = widthTuple.Length > 0 ? widthTuple[0].I : 0;
                int height = heightTuple.Length > 0 ? heightTuple[0].I : 0;

                int sourceMax = m.ImageType == "byte" ? 255 : 65535;
                double sourceScale255 = 255.0 / sourceMax;

                // ── 灰度化：彩色统一转 byte 灰度；单通道保留原位深 ──────────────
                gray = ToGray(work, m.Channels, m.ImageType, sourceMax);
                HOperatorSet.GetImageType(gray, out HTuple grayTypeTuple);
                string grayType = grayTypeTuple.Length > 0 ? grayTypeTuple.S : "byte";
                if (grayType != "byte" && grayType != "uint2")
                {
                    throw new InvalidOperationException($"灰度化后图像类型 {grayType} 不受支持。");
                }
                int grayMax = grayType == "byte" ? 255 : 65535;
                double grayScale255 = 255.0 / grayMax;

                // ── 对焦：sobel_amp('sum_abs') 均值，归一化到 8bit 等效梯度 ──────
                HOperatorSet.ConvertImageType(gray, out HObject realObj, "real");
                real = new HImage(realObj);
                HOperatorSet.SobelAmp(real, out HObject edgeObj, "sum_abs", 3);
                edge = new HImage(edgeObj);
                HOperatorSet.Intensity(edge, edge, out HTuple focusMean, out _);
                double focusMeanNative = focusMean.Length > 0 ? focusMean[0].D : 0.0;
                m.FocusRaw = focusMeanNative * grayScale255;
                m.FocusScore = Clamp01(m.FocusRaw / Math.Max(focusRef, 0.01));

                // ── 曝光 / 对比度：灰度直方图（byte 256 箱，uint2 65536 箱） ────
                // 用 gray_histo_range 而不是 gray_histo：显式给 [0, 满量程] 与精确箱数，
                // 避免 uint2 图上 gray_histo 的箱数/分箱口径与预期不一致（实测 1 通道 uint2 会少箱）。
                int bins = grayType == "byte" ? 256 : 65536;
                HOperatorSet.GrayHistoRange(domain, gray, 0, grayMax, bins, out HTuple absHist, out HTuple _);
                if (absHist.Length < bins)
                {
                    throw new InvalidOperationException(
                        $"直方图箱数 {absHist.Length} 少于预期 {bins}（灰度类型 {grayType}）。");
                }

                double total = 0.0;
                for (int i = 0; i < bins; i++) total += absHist[i].D;
                if (total <= 0.0)
                {
                    throw new InvalidOperationException("有效像素为 0，无法计算曝光/对比度。");
                }

                double lowCount = absHist[0].D;
                double highCount = absHist[bins - 1].D;
                m.TruncLowPercent = 100.0 * lowCount / total;
                m.TruncHighPercent = 100.0 * highCount / total;
                m.ExposureRaw = m.TruncLowPercent + m.TruncHighPercent;
                m.ExposureScore = Clamp01(1.0 - m.ExposureRaw / Math.Max(maxTruncationPercent, 0.001));

                double weighted = 0.0;
                int minBin = -1, maxBin = -1;
                for (int i = 0; i < bins; i++)
                {
                    double count = absHist[i].D;
                    weighted += i * count;
                    if (count > 0.0)
                    {
                        if (minBin < 0) minBin = i;
                        maxBin = i;
                    }
                }
                double meanBin = weighted / total;

                double target5 = total * 0.05;
                double target95 = total * 0.95;
                double cumulative = 0.0;
                int p5Bin = 0, p95Bin = bins - 1;
                bool found5 = false, found95 = false;
                for (int i = 0; i < bins; i++)
                {
                    cumulative += absHist[i].D;
                    if (!found5 && cumulative >= target5)
                    {
                        p5Bin = i;
                        found5 = true;
                    }
                    if (!found95 && cumulative >= target95)
                    {
                        p95Bin = i;
                        found95 = true;
                        break;
                    }
                }
                if (!found5) p5Bin = 0;
                if (!found95) p95Bin = bins - 1;
                if (minBin < 0) minBin = 0;
                if (maxBin < 0) maxBin = 0;

                m.MeanGray = meanBin * grayScale255;
                m.MinGray = minBin * grayScale255;
                m.MaxGray = maxBin * grayScale255;
                m.P5 = p5Bin * grayScale255;
                m.P95 = p95Bin * grayScale255;
                m.ContrastRaw = m.P95 - m.P5;
                m.ContrastScore = Clamp01(m.ContrastRaw / Math.Max(contrastRef, 0.01));

                HOperatorSet.Intensity(domain, gray, out _, out HTuple grayDeviation);
                double stdDevNative = grayDeviation.Length > 0 ? grayDeviation[0].D : 0.0;
                m.StdDev = stdDevNative * grayScale255;

                // ── 饱和度：仅彩色图；灰度图输出 NaN 并说明跳过 ────────────────
                if (m.Channels == 1)
                {
                    m.HasColor = false;
                    m.SaturationRaw = double.NaN;
                    m.SaturationScore = 0.0;
                }
                else
                {
                    m.HasColor = true;
                    colorByte = ColorToByte(work, m.Channels, m.ImageType, sourceMax);

                    HOperatorSet.Decompose3(colorByte, out HObject red, out HObject green, out HObject blue);
                    try
                    {
                        HOperatorSet.TransFromRgb(red, green, blue,
                            out HObject hue, out HObject sat, out HObject value, "hsv");
                        try
                        {
                            saturation = sat;
                            HOperatorSet.Intensity(sat, sat, out HTuple satMean, out _);
                            m.SaturationRaw = satMean.Length > 0 ? satMean[0].D : 0.0;
                            m.SaturationScore = Clamp01(m.SaturationRaw / Math.Max(saturationRef, 0.01));
                        }
                        finally
                        {
                            hue.Dispose();
                            value.Dispose();
                        }
                    }
                    finally
                    {
                        red.Dispose();
                        green.Dispose();
                        blue.Dispose();
                    }
                }

                // ── 明细：给现场调阈值看原始值 ─────────────────────────────────
                m.Details.Add(string.Format(CultureInfo.InvariantCulture,
                    "图像: {0}×{1}, {2} 通道, 类型 {3}, 有效像素 {4}",
                    width, height, m.Channels, m.ImageType, m.PixelCount));
                m.Details.Add(string.Format(CultureInfo.InvariantCulture,
                    "对焦: raw={0:F2} score={1:F3} (sobel_amp sum_abs 均值, 参考 {2:F1})",
                    m.FocusRaw, m.FocusScore, focusRef));
                m.Details.Add(string.Format(CultureInfo.InvariantCulture,
                    "曝光: raw={0:F3}% score={1:F3} (截断 低={2:F3}% 高={3:F3}%, 均值={4:F1}, 范围={5:F0}..{6:F0})",
                    m.ExposureRaw, m.ExposureScore, m.TruncLowPercent, m.TruncHighPercent,
                    m.MeanGray, m.MinGray, m.MaxGray));
                m.Details.Add(string.Format(CultureInfo.InvariantCulture,
                    "对比度: raw={0:F2} score={1:F3} (P5={2:F1} P95={3:F1}, 标准差={4:F1}, 参考 {5:F1})",
                    m.ContrastRaw, m.ContrastScore, m.P5, m.P95, m.StdDev, contrastRef));
                m.Details.Add(m.HasColor
                    ? string.Format(CultureInfo.InvariantCulture,
                        "饱和度: raw={0:F2} score={1:F3} (HSV S 均值, 参考 {2:F1})",
                        m.SaturationRaw, m.SaturationScore, saturationRef)
                    : "饱和度: 跳过（灰度图；饱和度只对彩色图有意义）");

                return m;
            }
            finally
            {
                saturation?.Dispose();
                edge?.Dispose();
                real?.Dispose();
                colorByte?.Dispose();
                gray?.Dispose();
                domain?.Dispose();
                work?.Dispose();
            }
        }

        /// <summary>灰度化：1 通道原样拷贝；3/4 通道走 rgb1_to_gray / rgb3_to_gray。</summary>
        private static HImage ToGray(HImage image, int channels, string type, int sourceMax)
        {
            if (channels == 1)
            {
                HOperatorSet.CopyImage(image, out HObject copied);
                return new HImage(copied);
            }

            if (type == "byte")
            {
                if (channels == 3)
                {
                    HOperatorSet.Rgb1ToGray(image, out HObject grayObj);
                    return new HImage(grayObj);
                }

                HObject? r = null, g = null, b = null, gray4 = null;
                try
                {
                    HOperatorSet.AccessChannel(image, out r, 1);
                    HOperatorSet.AccessChannel(image, out g, 2);
                    HOperatorSet.AccessChannel(image, out b, 3);
                    HOperatorSet.Rgb3ToGray(r, g, b, out gray4);
                    return new HImage(gray4);
                }
                finally
                {
                    r?.Dispose();
                    g?.Dispose();
                    b?.Dispose();
                    gray4?.Dispose();
                }
            }

            // uint2 彩色：先把三通道线性缩到 0..255，再灰度化，避免 convert_image_type 直接截顶
            HImage? byteColor = null;
            try
            {
                byteColor = ColorToByte(image, channels, type, sourceMax);
                HOperatorSet.Rgb1ToGray(byteColor, out HObject grayObj);
                return new HImage(grayObj);
            }
            finally
            {
                byteColor?.Dispose();
            }
        }

        /// <summary>
        /// 彩色图转 byte：byte 原样拷贝；uint2 用 scale_image 线性缩放到 0~255（不截顶）。
        /// 4 通道只取前 3 通道。
        /// </summary>
        private static HImage ColorToByte(HImage image, int channels, string type, int sourceMax)
        {
            if (type == "byte")
            {
                HOperatorSet.CopyImage(image, out HObject copied);
                return new HImage(copied);
            }

            HObject? r = null, g = null, b = null;
            HImage? sr = null, sg = null, sb = null;
            HObject? cr = null, cg = null, cb = null;
            try
            {
                if (channels == 3)
                {
                    HOperatorSet.Decompose3(image, out r, out g, out b);
                }
                else
                {
                    HOperatorSet.AccessChannel(image, out r, 1);
                    HOperatorSet.AccessChannel(image, out g, 2);
                    HOperatorSet.AccessChannel(image, out b, 3);
                }

                double scale = 255.0 / sourceMax;
                HOperatorSet.ScaleImage(r, out HObject rScaled, scale, 0.0);
                sr = new HImage(rScaled);
                HOperatorSet.ScaleImage(g, out HObject gScaled, scale, 0.0);
                sg = new HImage(gScaled);
                HOperatorSet.ScaleImage(b, out HObject bScaled, scale, 0.0);
                sb = new HImage(bScaled);

                HOperatorSet.ConvertImageType(sr, out cr, "byte");
                HOperatorSet.ConvertImageType(sg, out cg, "byte");
                HOperatorSet.ConvertImageType(sb, out cb, "byte");

                HOperatorSet.Compose3(cr, cg, cb, out HObject composed);
                return new HImage(composed);
            }
            finally
            {
                r?.Dispose();
                g?.Dispose();
                b?.Dispose();
                sr?.Dispose();
                sg?.Dispose();
                sb?.Dispose();
                cr?.Dispose();
                cg?.Dispose();
                cb?.Dispose();
            }
        }

        private static double Clamp01(double value)
        {
            if (double.IsNaN(value)) return 0.0;
            if (value < 0.0) return 0.0;
            if (value > 1.0) return 1.0;
            return value;
        }
    }
}
