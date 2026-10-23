using System;

namespace Plugin.DlAnomaly
{
    /// <summary>
    /// 预处理缩放模式。
    /// 【为什么要有这个开关】异常检测的训练预处理有两种主流做法，在非方形图上结果不同：
    /// Letterbox（等比缩放 + 补边）不改变长宽比、最稳；Stretch（直接拉成方形）会把目标压扁。
    /// 选错的表现是**分数静默偏移**（不报错、不崩溃），现场极难定位，所以显式交给用户选。
    /// 默认 Letterbox —— "不破坏几何"的那一档，出错面最小。
    /// </summary>
    public enum AnomalyResizeMode
    {
        /// <summary>等比缩放 + 居中补 114 灰边（不改变长宽比）</summary>
        Letterbox,

        /// <summary>直接拉伸到目标尺寸（改变长宽比）</summary>
        Stretch,
    }

    /// <summary>热力图上屏的插值档位（只影响显示，不影响分数）。</summary>
    public enum AnomalyInterpolation
    {
        /// <summary>最近邻：保留模型原始网格块状感，适合看"哪一格被判异常"</summary>
        Nearest,

        /// <summary>双线性：更平滑，适合看异常区域的形态边界</summary>
        Bilinear,
    }

    /// <summary>
    /// 预处理缩放几何：把原图映射到模型输入画布。
    /// 【为什么单独抽出来】纯几何数学，与 ONNX 无关，可以脱离模型用受控数据验证。
    /// 缩放/补边算错的表现是"异常分数莫名其妙地偏移"，不抛异常，必须能单独钉住。
    /// </summary>
    public sealed class ResizePlan
    {
        public required int SrcWidth { get; init; }
        public required int SrcHeight { get; init; }
        public required int DstWidth { get; init; }
        public required int DstHeight { get; init; }
        public required AnomalyResizeMode Mode { get; init; }

        /// <summary>实际绘制内容的尺寸（Letterbox 下小于画布；Stretch 下等于画布）</summary>
        public required int ScaledWidth { get; init; }
        public required int ScaledHeight { get; init; }

        /// <summary>内容在画布上的左上角偏移（Letterbox 居中补边；Stretch 恒为 0）</summary>
        public required int PadX { get; init; }
        public required int PadY { get; init; }

        /// <summary>
        /// Letterbox 的补边灰度。114 是 YOLO/Ultralytics 训练时的默认填充值。
        /// 换成 0 会引入模型没见过的黑边 —— 黑边是"强边缘"，在异常检测里可能直接被判成异常。
        /// </summary>
        public const int PadGrayValue = 114;

        public static ResizePlan Compute(int srcWidth, int srcHeight, int dstWidth, int dstHeight, AnomalyResizeMode mode)
        {
            if (mode == AnomalyResizeMode.Stretch)
            {
                return new ResizePlan
                {
                    SrcWidth = srcWidth,
                    SrcHeight = srcHeight,
                    DstWidth = dstWidth,
                    DstHeight = dstHeight,
                    Mode = mode,
                    ScaledWidth = dstWidth,
                    ScaledHeight = dstHeight,
                    PadX = 0,
                    PadY = 0,
                };
            }

            float scale = Math.Min((float)dstWidth / srcWidth, (float)dstHeight / srcHeight);
            int scaledWidth = Math.Max(1, (int)Math.Round(srcWidth * scale));
            int scaledHeight = Math.Max(1, (int)Math.Round(srcHeight * scale));

            return new ResizePlan
            {
                SrcWidth = srcWidth,
                SrcHeight = srcHeight,
                DstWidth = dstWidth,
                DstHeight = dstHeight,
                Mode = mode,
                ScaledWidth = scaledWidth,
                ScaledHeight = scaledHeight,
                PadX = (dstWidth - scaledWidth) / 2,
                PadY = (dstHeight - scaledHeight) / 2,
            };
        }

        public string Describe() => Mode == AnomalyResizeMode.Stretch
            ? $"stretch {SrcWidth}×{SrcHeight} → {DstWidth}×{DstHeight}"
            : $"letterbox {SrcWidth}×{SrcHeight} → {ScaledWidth}×{ScaledHeight} 补边 ({PadX},{PadY}) 到 {DstWidth}×{DstHeight}";
    }
}
