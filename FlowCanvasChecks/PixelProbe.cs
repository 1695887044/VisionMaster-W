using HalconDotNet;

namespace FlowCanvasChecks
{
    /// <summary>
    /// 像素级取样工具（断言专用）。
    ///
    /// 为什么要单独一份：标注图是"离屏窗口 dump_window_image 回读的 RGB 图"，而底图可能是
    /// byte / uint2 / 1~4 通道；`min_max_gray` 只吃单通道，且它的**第一个入参是区域不是图像**
    /// （必须传 domain，否则读出来的值是胡说八道）。这套换算在每个断言文件里各写一遍必然写歪
    /// （BlobDetectChecks 历史上就为此单开过一份）。四个插件的"标注底图保真"断言要的是同一把尺子，
    /// 所以尺子只此一份。
    /// </summary>
    internal static class PixelProbe
    {
        /// <summary>
        /// 转单通道：3 通道走 rgb1_to_gray，其余原样拷一份。
        /// 为什么单通道也拷：调用方一律 Dispose，省掉"这个句柄是不是我自己的"判断（拷一份的代价可忽略）。
        /// </summary>
        public static HObject Gray(HImage img)
        {
            HOperatorSet.CountChannels(img, out HTuple ch);
            if (ch.I == 3)
            {
                HOperatorSet.Rgb1ToGray(img, out HObject g);
                return g;
            }
            HOperatorSet.CopyImage(img, out HObject copy);
            return copy;
        }

        /// <summary>矩形带 [r1,c1]~[r2,c2] 内的灰度极值（含端点）。</summary>
        public static (double Min, double Max) Band(HImage img, int r1, int c1, int r2, int c2)
        {
            HObject gray = Gray(img);
            try
            {
                HOperatorSet.GenRectangle1(out HObject rect, r1, c1, r2, c2);
                try
                {
                    HOperatorSet.MinMaxGray(rect, gray, 0, out HTuple lo, out HTuple hi, out HTuple _);
                    return (lo.D, hi.D);
                }
                finally { rect.Dispose(); }
            }
            finally { gray.Dispose(); }
        }

        /// <summary>单点灰度。</summary>
        public static double At(HImage img, int row, int col) => Band(img, row, col, row, col).Max;

        /// <summary>图像尺寸；未初始化返回 (-1,-1)。</summary>
        public static (int W, int H) SizeOf(HImage img)
        {
            if (img == null || !img.IsInitialized()) return (-1, -1);
            HOperatorSet.GetImageSize(img, out HTuple w, out HTuple h);
            return (w.I, h.I);
        }
    }
}
