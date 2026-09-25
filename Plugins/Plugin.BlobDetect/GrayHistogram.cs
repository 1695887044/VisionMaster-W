using System;
using System.Linq;
using HalconDotNet;

namespace Plugin.BlobDetect
{
    /// <summary>
    /// 灰度直方图数据（配置态展示用）。
    ///
    /// 为什么单独一个类、且不含任何界面代码：直方图是"算法侧的一次 Halcon 计算 + 一份纯数据"，
    /// 界面只负责把它画出来。把计算与绘制分开，配置界面和以后可能的其它消费方都能复用，
    /// 也便于在没有 WPF 的环境里单独验证计算是否正确。
    ///
    /// 【为什么要"按实际灰度范围分档"，而不是直接用 gray_histo 的原始数组】
    /// gray_histo 返回的是"每个可能灰度值一档"：byte 256 档、uint2 65536 档。
    /// 把 65536 档（或 12 位图实际用到的 4096 档）硬画进几百像素宽的控件里，
    /// 既画不出细节、又白算一遍。所以这里统一压到"覆盖实际灰度范围的 ≤256 档"：
    ///  · 实际范围来自 min_max_gray（不是按像素类型猜，见下）；
    ///  · byte 图整幅 0~255 时恰好 256 档，任何更窄的分布都能得到更细的相对分辨率。
    /// </summary>
    public sealed class GrayHistogram
    {
        /// <summary>控件显示档数上限：再细也画不出来（控件宽度通常只有几百像素）</summary>
        public const int MaxDisplayBuckets = 256;

        /// <summary>各档频数，已按峰值归一化到 [0,1]（控件只关心分布形状，绝对频数无意义）</summary>
        public double[] Bins { get; init; } = Array.Empty<double>();

        /// <summary>分档覆盖的最暗灰度（= 图像实际最小灰度）</summary>
        public double BinMin { get; init; }

        /// <summary>分档覆盖的最亮灰度（= 图像实际最大灰度）</summary>
        public double BinMax { get; init; }

        /// <summary>图像类型名，如 byte / uint2 / real</summary>
        public string ImageType { get; init; } = string.Empty;

        /// <summary>图像宽（像素）</summary>
        public int ImageWidth { get; init; }

        /// <summary>图像高（像素）</summary>
        public int ImageHeight { get; init; }

        /// <summary>实际档数</summary>
        public int BucketCount => Bins.Length;

        /// <summary>灰度范围文字，如 "102 ~ 206"（给界面直接绑，避免在 XAML 里拼字符串）</summary>
        public string RangeText => $"{FormatGray(BinMin)} ~ {FormatGray(BinMax)}";

        /// <summary>图像类型/位深文字，如 "byte（8 位）"</summary>
        public string TypeText => string.IsNullOrEmpty(ImageType)
            ? "未知"
            : $"{ImageType}（{BitDepthOf(ImageType)} 位）";

        private static string FormatGray(double v)
            => Math.Abs(v - Math.Round(v)) < 1e-9 ? v.ToString("0") : v.ToString("0.#");

        /// <summary>由图像类型名给出"名义位深"（只用于界面文字，不参与算法）</summary>
        private static string BitDepthOf(string type) => type switch
        {
            "byte" or "int1" => "8",
            "uint2" or "int2" => "16",
            "int4" or "real" => "32",
            _ => "?",
        };

        /// <summary>
        /// 计算灰度直方图。失败返回 false 并给出原因（调用方据此"不显示直方图 + 说明原因"，不弹框不崩）。
        /// </summary>
        /// <param name="gray">单通道灰度图（不负责任何释放，只读借用）</param>
        public static bool TryCompute(HObject gray, out GrayHistogram? result, out string error)
        {
            result = null;
            error = string.Empty;

            HObject? domain = null;
            try
            {
                HOperatorSet.GetDomain(gray, out domain);
                HOperatorSet.GetImageType(gray, out HTuple typeTuple);
                string imageType = typeTuple.Length > 0 ? typeTuple.S : string.Empty;

                // 实际灰度范围：用 min_max_gray 度量，而不是"按像素类型猜"。
                // 12 位相机常只用 0~4095，而 uint2 的存储范围是 0~65535，两者相差 16 倍，
                // 任何"按类型硬编码范围"的做法都会错。
                HOperatorSet.MinMaxGray(domain, gray, 0, out HTuple minTuple, out HTuple maxTuple, out HTuple _);
                double gMin = minTuple.Length > 0 ? minTuple[0].D : 0;
                double gMax = maxTuple.Length > 0 ? maxTuple[0].D : 0;
                if (double.IsNaN(gMin) || double.IsNaN(gMax))
                {
                    error = "灰度范围为无效值（图可能为空）";
                    return false;
                }
                if (gMax < gMin) (gMin, gMax) = (gMax, gMin);

                HOperatorSet.GetImageSize(gray, out HTuple wTuple, out HTuple hTuple);
                int width = wTuple.I, height = hTuple.I;

                // 分档数 = 覆盖实际灰度范围所需的档数，夹在 [8, 256]：
                // 下限 8 保证极窄范围（如 100~101）也画得出一小段分布；上限 256 避免 uint2 压进上千档。
                int buckets = (int)Math.Clamp(Math.Ceiling(gMax - gMin + 1), 8, MaxDisplayBuckets);

                double[] bins;
                if (imageType == "byte" || imageType == "uint2")
                {
                    // 按规格用 gray_histo：返回"每个灰度值一档"的频数（byte 256 项 / uint2 65536 项），
                    // 再按实际灰度范围重新聚合。只对 ≤16 位的整数图走这条路，
                    // 避免 int4/real 让 gray_histo 生成天文数字长度的数组。
                    HOperatorSet.GrayHisto(domain, gray, out HTuple absHisto, out HTuple _);
                    bins = Rebucket(absHisto.ToDArr(), gMin, gMax, buckets);
                }
                else
                {
                    // 其它类型（real / int4 等）：gray_histo 的"逐灰度值返回"不适用或代价不可接受，
                    // 改用 gray_histo_range 直接给出 [gMin, gMax] 上的 buckets 档分布
                    HOperatorSet.GrayHistoRange(domain, gray, gMin, gMax, buckets, out HTuple histo, out HTuple _);
                    bins = histo.ToDArr();
                }

                // 归一化到 [0,1]：最高档画到满高，其余按比例
                double peak = bins.Length > 0 ? bins.Max() : 0;
                if (peak > 0)
                {
                    for (int i = 0; i < bins.Length; i++) bins[i] /= peak;
                }

                result = new GrayHistogram
                {
                    Bins = bins,
                    BinMin = gMin,
                    BinMax = gMax,
                    ImageType = imageType,
                    ImageWidth = width,
                    ImageHeight = height,
                };
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                try { domain?.Dispose(); } catch { /* 中间对象释放失败不阻断 */ }
            }
        }

        /// <summary>
        /// 把"每个灰度值一档"的原始频数聚合到 [gMin, gMax] 上的 buckets 档。
        /// 范围外的灰度值直接丢弃（它们不在直方图的横轴范围内）。
        /// </summary>
        private static double[] Rebucket(double[] grayValueCounts, double gMin, double gMax, int buckets)
        {
            var bins = new double[buckets];
            // +1 让"整段范围"包含首尾两个灰度值本身（如 100~101 是 2 个灰度值而非 1 个）
            double span = gMax - gMin + 1;
            double binWidth = span / buckets;

            for (int gray = 0; gray < grayValueCounts.Length; gray++)
            {
                double count = grayValueCounts[gray];
                if (count == 0) continue;
                if (gray < gMin || gray > gMax) continue;

                int bucket = (int)((gray - gMin) / binWidth);
                if (bucket < 0) bucket = 0;
                if (bucket >= buckets) bucket = buckets - 1;   // 最亮灰度落在右边界时归并到最后一档
                bins[bucket] += count;
            }
            return bins;
        }
    }
}
