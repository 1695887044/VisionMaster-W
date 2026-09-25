using System;
using System.Collections.Generic;
using HalconDotNet;

namespace Plugin.Yolo
{
    /// <summary>
    /// 按检测框裁剪图像。
    ///
    /// 【为什么从插件壳里抽出来】这是纯图像处理数学，与"插件如何组织端口"无关。
    /// 抽成独立类后可以脱离插件壳单独用受控数据验证 —— 尤其是"越界怎么夹取"这一段，
    /// 它错了不会抛异常、只会给出一张尺寸看着合理但内容错位的图，必须能单独测。
    ///
    /// 【为什么必须自己夹取边界】实测 crop_part **越界不报错**：
    /// 请求超出图像范围的宽高，它照样返回一张图，多出来的部分是垃圾数据
    /// （只有 Row 为负才抛 #1301）。指望 Halcon 兜底就会得到一张"看着正常、实际错位"的图。
    ///
    /// 夹取顺序：先夹位置再算可用尺寸，否则会算出负的宽度。
    /// </summary>
    public static class YoloCrop
    {
        /// <summary>裁剪置信度最高的目标（列表需已按置信度降序）。没有目标时返回 null</summary>
        public static HImage? CropBest(
            HImage source, IReadOnlyList<Detection> detections, int margin, out string note)
        {
            note = string.Empty;
            if (detections.Count == 0) return null;

            return TryCrop(source, detections[0], margin, out HImage? crop, out note) ? crop : null;
        }

        /// <summary>
        /// 裁剪单个目标。<paramref name="note"/> 在发生越界夹取时给出说明（供上层写日志）。
        /// </summary>
        public static bool TryCrop(
            HImage? source, Detection detection, int margin, out HImage? crop, out string note)
        {
            crop = null;
            note = string.Empty;

            if (source == null || !source.IsInitialized()) return false;

            HOperatorSet.GetImageSize(source, out HTuple widthTuple, out HTuple heightTuple);
            int imageWidth = widthTuple.I;
            int imageHeight = heightTuple.I;
            if (imageWidth <= 0 || imageHeight <= 0) return false;

            // 按框的外接矩形 + 边距，先取整成像素
            int requestedRow = (int)Math.Round(detection.Y1) - margin;
            int requestedCol = (int)Math.Round(detection.X1) - margin;
            int requestedHeight = (int)Math.Round(detection.Y2 - detection.Y1) + margin * 2;
            int requestedWidth = (int)Math.Round(detection.X2 - detection.X1) + margin * 2;

            // 先夹位置：左上角必须在图内
            int row = Math.Clamp(requestedRow, 0, imageHeight - 1);
            int col = Math.Clamp(requestedCol, 0, imageWidth - 1);

            // 再按夹好的位置算可用尺寸，保证不越界
            int height = Math.Clamp(requestedHeight, 1, imageHeight - row);
            int width = Math.Clamp(requestedWidth, 1, imageWidth - col);

            bool clamped = row != requestedRow || col != requestedCol
                           || width != requestedWidth || height != requestedHeight;

            if (clamped)
            {
                note = $"（注意：检测框超出图像范围，已夹取为 行{row} 列{col} {width}×{height}）";
            }

            try
            {
                HOperatorSet.CropPart(source, out HObject cropped, row, col, width, height);
                crop = new HImage(cropped);
                return true;
            }
            catch (Exception ex)
            {
                note = $"（裁剪失败：{ex.Message}）";
                return false;
            }
        }
    }
}
