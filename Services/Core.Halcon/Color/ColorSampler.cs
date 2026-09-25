using System;
using System.Collections.Generic;
using HalconDotNet;

namespace Core.Halcon.Color
{
    /// <summary>
    /// ROI 形状名。
    /// 用字符串而不是枚举：枚举值经步骤的 InputValues 往返会抛 InvalidCastException
    /// （InputPort 的 setter 对枚举只做硬转换，不从数字/文本解析），这个坑在「变量赋值·作用域」上踩过一次。
    /// </summary>
    public static class RoiShapeNames
    {
        public const string Rectangle = "Rectangle";
        public const string Circle = "Circle";
        public const string Ellipse = "Ellipse";

        /// <summary>认不认得这个形状名</summary>
        public static bool IsKnown(string shape)
            => shape == Rectangle || shape == Circle || shape == Ellipse;
    }

    /// <summary>
    /// 采样内核：把"点"或"区域"变成逐点 RGB。
    ///
    /// 分工：**内核负责"给点取色"，算子负责"点怎么来"** ——
    /// 颜色序列检查自己算采样线的点（沿短轴逐点扫），
    /// 区域颜色检查让内核把 ROI 变成区域再抽样。两者的取色走同一条路。
    ///
    /// 取色一律走元组版 GetGrayval：一次调用拿回整批点，比逐点调用快两个数量级；
    /// 也不必把通道包成 HImage 去管句柄（那条路上踩过 HALCON #4056 object-ID is NULL）。
    /// </summary>
    public static class ColorSampler
    {
        /// <summary>
        /// 给一串点（行、列各一个数组，一一对应），一次取回三通道灰阶。
        /// 返回的 channels[0/1/2] 分别是 R/G/B；灰度图只会有 1 个通道（由调用方决定要不要接受）。
        /// </summary>
        public static bool TryReadPixels(HImage image, double[] rows, double[] cols,
            out int[][] channels, out string error)
        {
            channels = Array.Empty<int[]>();
            error = string.Empty;

            if (image == null || !image.IsInitialized()) { error = "上游没有图像"; return false; }
            if (rows == null || cols == null || rows.Length == 0)
            {
                error = "采样点为空：请先框好采样区";
                return false;
            }
            if (rows.Length != cols.Length)
            {
                error = $"采样点的行/列个数不一致（{rows.Length} vs {cols.Length}）";
                return false;
            }

            image.GetImageSize(out int width, out int height);
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i] < 0 || rows[i] > height - 1 || cols[i] < 0 || cols[i] > width - 1)
                {
                    error = $"采样点超出图像范围（图像 {width}x{height}，取到 ({rows[i]:0}, {cols[i]:0})）：请把采样区画在图像内";
                    return false;
                }
            }

            int available = (int)image.CountChannels().D;
            if (available <= 0) { error = "图像没有通道"; return false; }
            int use = Math.Min(3, available);

            int count = rows.Length;
            var rowsTuple = new HTuple(rows);
            var colsTuple = new HTuple(cols);
            var result = new int[use][];

            for (int ci = 0; ci < use; ci++)
            {
                HOperatorSet.AccessChannel(image, out HObject channelObject, ci + 1);
                try
                {
                    HOperatorSet.GetGrayval(channelObject, rowsTuple, colsTuple, out HTuple values);
                    var buffer = new int[count];
                    for (int i = 0; i < count; i++) buffer[i] = (int)Math.Round(values[i].D);
                    result[ci] = buffer;
                }
                finally
                {
                    channelObject?.Dispose();
                }
            }

            channels = result;
            return true;
        }

        /// <summary>
        /// ROI（矩形 / 圆 / 椭圆）→ 区域内的抽样点。
        ///
        /// 为什么用 Halcon 区域当掩膜：**用方框去框圆形指示灯时，方框四个角是背景**。
        /// 不作掩膜的话，背景像素会参与颜色投票，主色可能直接变成"背景色"，
        /// 而且因为背景占比高，它还会"看起来很有把握"。用圆形区域就只剩灯上的像素。
        ///
        /// 点太多时按固定步长抽稀到 maxPoints 以内 —— 步长抽稀是均匀的，投票不会偏。
        /// ROI 超出图像的部分自动裁掉（不报错：框得松一点是常事）。
        /// </summary>
        public static bool TryBuildRoiPoints(HImage image, string shape, double[] pars, int maxPoints,
            out double[] rows, out double[] cols, out string error)
        {
            rows = Array.Empty<double>();
            cols = Array.Empty<double>();
            error = string.Empty;

            if (image == null || !image.IsInitialized()) { error = "上游没有图像"; return false; }
            if (!RoiShapeNames.IsKnown(shape))
            {
                error = $"认不出采样区形状「{shape}」：应为 "
                      + $"{RoiShapeNames.Rectangle} / {RoiShapeNames.Circle} / {RoiShapeNames.Ellipse}";
                return false;
            }
            if (pars == null || pars.Length < 5)
            {
                error = "采样区参数不完整（需要 5 个：中心行、中心列、角度、半长/半径、半宽）：请重新画一个框";
                return false;
            }
            if (maxPoints < 8) maxPoints = 8;

            HObject? raw = null, imageRect = null, clipped = null;
            try
            {
                switch (shape)
                {
                    case RoiShapeNames.Rectangle:
                        if (pars[3] <= 0 || pars[4] <= 0)
                        {
                            error = "矩形的半长/半宽必须大于 0：请重新画一个框";
                            return false;
                        }
                        HOperatorSet.GenRectangle2(out raw, pars[0], pars[1], pars[2], pars[3], pars[4]);
                        break;

                    case RoiShapeNames.Circle:
                        if (pars[2] <= 0)
                        {
                            error = "圆的半径必须大于 0：请重新画一个圆";
                            return false;
                        }
                        HOperatorSet.GenCircle(out raw, pars[0], pars[1], pars[2]);
                        break;

                    default:
                        if (pars[3] <= 0 || pars[4] <= 0)
                        {
                            error = "椭圆的两个半径必须大于 0：请重新画一个椭圆";
                            return false;
                        }
                        HOperatorSet.GenEllipse(out raw, pars[0], pars[1], pars[2], pars[3], pars[4]);
                        break;
                }

                image.GetImageSize(out int width, out int height);
                HOperatorSet.GenRectangle1(out imageRect, 0, 0, height - 1, width - 1);
                HOperatorSet.Intersection(raw, imageRect, out clipped);

                HOperatorSet.AreaCenter(clipped, out HTuple area, out _, out _);
                if (area.Length == 0 || area[0].D <= 0)
                {
                    error = "采样区与图像没有交集：请把框画在图像内";
                    return false;
                }

                HOperatorSet.GetRegionPoints(clipped, out HTuple regionRows, out HTuple regionCols);
                int total = regionRows.Length;
                if (total == 0) { error = "采样区里取不到点：请重新画一个框"; return false; }

                int stride = Math.Max(1, total / maxPoints);
                var rowList = new List<double>();
                var colList = new List<double>();
                for (int i = 0; i < total; i += stride)
                {
                    rowList.Add(regionRows[i].D);
                    colList.Add(regionCols[i].D);
                }

                rows = rowList.ToArray();
                cols = colList.ToArray();
                return true;
            }
            finally
            {
                raw?.Dispose();
                imageRect?.Dispose();
                clipped?.Dispose();
            }
        }

        /// <summary>取一串值的中位数（半整数下标取偏下的那个，够用且稳定）</summary>
        public static int Median(int[] values)
        {
            if (values == null || values.Length == 0) return 0;
            var sorted = (int[])values.Clone();
            Array.Sort(sorted);
            return sorted[sorted.Length / 2];
        }
    }
}
