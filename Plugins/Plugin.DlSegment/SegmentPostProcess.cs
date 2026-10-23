using System;

namespace Plugin.DlSegment
{
    /// <summary>
    /// 缩放计划：把任意尺寸的源图放进模型的固定输入尺寸。
    ///
    /// 【判据】与 Plugin.DlClassify / Plugin.DlAnomaly 的同名类型同源（方案书 §5.1 刻意重复），
    /// 补边灰度固定 114：黑边（0）在模型眼里是训练时从没见过的强边缘，
    /// 会把边界附近像素的类别打分拉偏；114 是 ImageNet 归一化后的中灰，多数框架的 letterbox 都用它。
    /// </summary>
    public sealed class SegmentResizePlan
    {
        public int SrcWidth { get; init; }
        public int SrcHeight { get; init; }
        public int DstWidth { get; init; }
        public int DstHeight { get; init; }
        public SegmentResizeMode Mode { get; init; }

        /// <summary>缩放后的图像尺寸（letterbox 下小于等于目标；stretch 下等于目标）。</summary>
        public int ScaledWidth { get; init; }
        public int ScaledHeight { get; init; }

        /// <summary>补边偏移（左上角起点）。letterbox 下居中；stretch 下恒为 0。</summary>
        public int PadX { get; init; }
        public int PadY { get; init; }

        /// <summary>补边灰度（0~255）。全仓统一 114。</summary>
        public const int PadGrayValue = 114;

        public static SegmentResizePlan Compute(
            int srcWidth, int srcHeight, int dstWidth, int dstHeight, SegmentResizeMode mode)
        {
            if (srcWidth <= 0 || srcHeight <= 0 || dstWidth <= 0 || dstHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(srcWidth), "尺寸必须为正");
            }

            if (mode == SegmentResizeMode.Stretch)
            {
                return new SegmentResizePlan
                {
                    SrcWidth = srcWidth,
                    SrcHeight = srcHeight,
                    DstWidth = dstWidth,
                    DstHeight = dstHeight,
                    Mode = mode,
                    ScaledWidth = dstWidth,
                    ScaledHeight = dstHeight,
                    PadX = 0,
                    PadY = 0
                };
            }

            float scale = Math.Min((float)dstWidth / srcWidth, (float)dstHeight / srcHeight);
            int scaledWidth = Math.Max(1, (int)Math.Round(srcWidth * scale));
            int scaledHeight = Math.Max(1, (int)Math.Round(srcHeight * scale));

            // 四舍五入可能把缩放后尺寸顶出目标 1 像素，这里夹回，否则 ChangeDomain 画框会越界。
            if (scaledWidth > dstWidth) scaledWidth = dstWidth;
            if (scaledHeight > dstHeight) scaledHeight = dstHeight;

            return new SegmentResizePlan
            {
                SrcWidth = srcWidth,
                SrcHeight = srcHeight,
                DstWidth = dstWidth,
                DstHeight = dstHeight,
                Mode = mode,
                ScaledWidth = scaledWidth,
                ScaledHeight = scaledHeight,
                PadX = (dstWidth - scaledWidth) / 2,
                PadY = (dstHeight - scaledHeight) / 2
            };
        }

        public string Describe() => Mode == SegmentResizeMode.Stretch
            ? $"Stretch {SrcWidth}×{SrcHeight} → {DstWidth}×{DstHeight}"
            : $"Letterbox {SrcWidth}×{SrcHeight} → {ScaledWidth}×{ScaledHeight} @({PadX},{PadY}) 画布 {DstWidth}×{DstHeight}";
    }
}
