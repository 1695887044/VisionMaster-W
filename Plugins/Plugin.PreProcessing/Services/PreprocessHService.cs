using HalconDotNet;
using Plugin.PreProcessing.Models;
using System;
using System.IO;

namespace Plugin.PreProcessing.Services
{
    /// <summary>
    /// Halcon 预处理算子的统一封装。
    ///
    /// 相对参考实现（旧 02Plugins\Plugin.PerProcessing\PretreatHelp.cs）修掉了四类问题：
    /// 1. 内存泄漏：旧版所有方法内部产生的中间 HObject 从头到尾不 Dispose，
    ///    连续跑几百轮方案后非托管内存只涨不落。这里中间对象一律 try/finally 释放。
    /// 2. 生命周期混淆：旧版算子出错时 "outImage = inImage"，把入参图当输出返回给上层，
    ///    上层一 Dispose 输出就把上游的图像也释放了，表现为莫名其妙的图像崩溃/黑图。
    ///    这里失败就抛异常，由插件层统一记录并保留原图。
    /// 3. 参数语义错：median_image 第三个参数 Margin 是"边缘扩充方式"（字符串），
    ///    旧版把"模板高度"传了进去；旧版选了 YUV 实际跑的却是 hsi，
    ///    这里按 trans_from_rgb 真实支持的 ColorSpaceName 逐项映射。
    /// 4. 算子接反：旧版 ViewModels 的 switch 里"灰度膨胀"调的是腐蚀、"灰度腐蚀"调的是膨胀。
    ///    这里一个算子一个类，从结构上就没有接反的可能。
    /// </summary>
    internal static class PreprocessHService
    {
        #region 通道 / 色彩空间

        /// <summary>取图像的通道数（拿不准输入是灰度图还是彩色图时先问它）</summary>
        public static int CountChannels(HImage image)
        {
            HOperatorSet.CountChannels(image, out HTuple count);
            return count.I;
        }

        /// <summary>取图像宽高（像素）</summary>
        public static void ImageSize(HImage image, out int width, out int height)
        {
            HOperatorSet.GetImageSize(image, out HTuple w, out HTuple h);
            width = w.I;
            height = h.I;
        }

        /// <summary>
        /// 复制一张独立的图（copy_image 会重新分配内存）。
        /// 用途：整条算子链都被禁用时，输出端口不能直接挂上游那张"别人的"图 ——
        /// 基类 Dispose() 会释放输出端口的承载值，那样一来上游的图就被我们顺手释放了。
        /// </summary>
        public static HImage Clone(HImage image)
        {
            HObject copied;
            HOperatorSet.CopyImage(image, out copied);
            try { return new HImage(copied); }
            finally { copied.Dispose(); }
        }

        /// <summary>三通道彩色图 → 单通道灰度图（rgb1_to_gray）</summary>
        public static HImage ToGray(HImage image)
        {
            HObject gray;
            HOperatorSet.Rgb1ToGray(image, out gray);
            try { return new HImage(gray); }
            finally { gray.Dispose(); }
        }

        /// <summary>
        /// 保证拿到单通道图：本来就单通道就原样返回同一个对象（不复制、不新增内存），
        /// 是彩色图才转成灰度。调用方用 ReferenceEquals 判断该不该释放返回值。
        /// </summary>
        public static HImage EnsureMono(HImage image)
        {
            if (CountChannels(image) <= 1) return image;
            return ToGray(image);
        }

        /// <summary>三通道分离后取其中一个通道</summary>
        public static HImage PickChannel(HImage image, ChannelIndex channel)
        {
            HObject c1, c2, c3;
            HOperatorSet.Decompose3(image, out c1, out c2, out c3);
            try
            {
                var picked = Pick(c1, c2, c3, (int)channel);
                return new HImage(picked);
            }
            finally
            {
                c1.Dispose();
                c2.Dispose();
                c3.Dispose();
            }
        }

        /// <summary>RGB → 其它色彩空间（HSV / HSI / CIELAB），再取指定通道</summary>
        public static HImage TransFromRgb(HImage image, string colorSpace, ChannelIndex channel)
        {
            HObject r, g, b;
            HOperatorSet.Decompose3(image, out r, out g, out b);
            HObject t1, t2, t3;
            try
            {
                HOperatorSet.TransFromRgb(r, g, b, out t1, out t2, out t3, colorSpace);
                try
                {
                    return new HImage(Pick(t1, t2, t3, (int)channel));
                }
                finally
                {
                    t1.Dispose();
                    t2.Dispose();
                    t3.Dispose();
                }
            }
            finally
            {
                r.Dispose();
                g.Dispose();
                b.Dispose();
            }
        }

        private static HObject Pick(HObject c1, HObject c2, HObject c3, int oneBased)
        {
            if (oneBased <= 1) return c1;
            if (oneBased == 2) return c2;
            return c3;
        }

        /// <summary>
        /// 取转换目标空间在 Halcon 里的字符串写法。
        /// 返回 null 表示"这个模式不走 trans_from_rgb"（转灰度 / 直接分通道）。
        /// </summary>
        public static string? ToColorSpaceName(ColorSpaceMode mode)
        {
            switch (mode)
            {
                case ColorSpaceMode.HSV: return "hsv";
                case ColorSpaceMode.HSI: return "hsi";
                case ColorSpaceMode.YUV: return "yuv";
                case ColorSpaceMode.CIELAB: return "cielab";
                default: return null;
            }
        }

        #endregion

        #region 几何调整

        /// <summary>镜像</summary>
        public static HImage Mirror(HImage image, MirrorMode mode)
        {
            string m;
            switch (mode)
            {
                case MirrorMode.Horizontal: m = "column"; break;    // 沿列轴翻转 = 左右镜像
                case MirrorMode.Vertical: m = "row"; break;         // 沿行轴翻转 = 上下镜像
                default: m = "diagonal"; break;                     // 主对角线翻转（宽高互换）
            }
            HObject result;
            HOperatorSet.MirrorImage(image, out result, m);
            try { return new HImage(result); }
            finally { result.Dispose(); }
        }

        /// <summary>整角度旋转（Halcon 的 rotate_image 不改变画布大小，旋转后超出部分被裁掉）</summary>
        public static HImage Rotate(HImage image, double degree)
        {
            HObject result;
            HOperatorSet.RotateImage(image, out result, degree, "constant");
            try { return new HImage(result); }
            finally { result.Dispose(); }
        }

        /// <summary>改变图像尺寸（在原画布上居中/补边，不是缩放）</summary>
        public static HImage ChangeFormat(HImage image, int width, int height)
        {
            HObject result;
            HOperatorSet.ChangeFormat(image, out result, width, height);
            try { return new HImage(result); }
            finally { result.Dispose(); }
        }

        /// <summary>
        /// 矩形裁剪：从 (row, col) 起取 width × height 个像素。
        ///
        /// 【实测坑】HDevelop 文档里 crop_part 写成 (Row, Column, Height, Width)，
        /// 但 HalconDotNet 的绑定形参是 (row, column, width, height) —— 第 3 个数管的是"宽度"。
        /// 实测：CropPart(row:80, col:50, p3:200, p4:70) 得到 width=200 height=70。
        /// 按文档的顺序传进来，会得到一张宽高互换的图，而且 Halcon 一声不吭。
        ///
        /// 【实测坑2】越界不报错：CropPart 起点/尺寸超出图像边界时照样返回请求的尺寸（越界处填 0）。
        /// 所以夹取必须在本层之上做，别指望 Halcon 兜底。
        /// </summary>
        public static HImage Crop(HImage image, int row, int col, int width, int height)
        {
            HObject result;
            HOperatorSet.CropPart(image, out result, row, col, width, height);
            try { return new HImage(result); }
            finally { result.Dispose(); }
        }

        /// <summary>
        /// 按比例缩放。入参沿用"行因子/列因子"的叫法（行 = 垂直方向 = 高度），
        /// 内部再映射到绑定的 (scaleWidth, scaleHeight) —— 实测 3.0 作用在宽度上、2.0 作用在高度上。
        /// 输出尺寸是四舍五入（640×0.333 → 213，480×0.333 → 160）。
        /// 因子 ≤ 0 会抛 #1301，调用方必须先夹成正数。
        /// </summary>
        public static HImage Zoom(HImage image, double rowFactor, double colFactor, ZoomInterpolation mode)
        {
            string interpolation;
            switch (mode)
            {
                case ZoomInterpolation.NearestNeighbor: interpolation = "nearest_neighbor"; break;
                case ZoomInterpolation.Bicubic: interpolation = "bicubic"; break;
                default: interpolation = "bilinear"; break;
            }
            HObject result;
            HOperatorSet.ZoomImageFactor(image, out result, colFactor, rowFactor, interpolation);
            try { return new HImage(result); }
            finally { result.Dispose(); }
        }

        /// <summary>
        /// 框外屏蔽：保留左上角 (row, col) 起、width × height 的矩形内的原像素，
        /// 矩形之外整片填成 gray。图像尺寸与通道数都不变（实测 640×480 byte 仍是 640×480 byte）。
        /// 行列用左上角而不是中心，和 <see cref="Crop"/> 保持一致 —— 两套坐标基准并存是写反的温床。
        ///
        /// 【实测坑】paint_region 的 Type 只认 "fill"，写成 "filled" 直接抛 #1302；
        /// 而 GrayValue 的长度必须等于通道数 —— 三通道图只给一个值会抛 #1401。
        /// </summary>
        public static HImage MaskOutside(HImage image, int row, int col,
            int width, int height, double gray)
        {
            int channels = CountChannels(image);

            HOperatorSet.GetDomain(image, out HObject domain);
            HObject box;
            HObject outside;
            try
            {
                // gen_rectangle1 是 (row1, column1, row2, column2) —— 闭区间，所以右下端要 -1
                HOperatorSet.GenRectangle1(out box, row, col, row + height - 1, col + width - 1);
                try
                {
                    HOperatorSet.Difference(domain, box, out outside);
                    try
                    {
                        HObject result;
                        HOperatorSet.PaintRegion(outside, image, out result,
                            new HTuple(Enumerable.Repeat(gray, channels).ToArray()), "fill");
                        try { return new HImage(result); }
                        finally { result.Dispose(); }
                    }
                    finally
                    {
                        outside.Dispose();
                    }
                }
                finally
                {
                    box.Dispose();
                }
            }
            finally
            {
                domain.Dispose();
            }
        }

        #endregion

        #region 滤波

        /// <summary>均值滤波：模板 W×H</summary>
        public static HImage Mean(HImage image, int width, int height)
        {
            HObject result;
            HOperatorSet.MeanImage(image, out result, width, height);
            try { return new HImage(result); }
            finally { result.Dispose(); }
        }

        /// <summary>
        /// 中值滤波。
        /// radius = 模板半径；margin = 图像边缘怎么扩充（mirrored / cyclic），
        /// 参考实现把模板高度传给了 margin，这里改回正确的语义。
        /// </summary>
        public static HImage Median(HImage image, MedianMaskType maskType, int radius, MaskMarginMode margin)
        {
            string mask;
            switch (maskType)
            {
                case MedianMaskType.Circle: mask = "circle"; break;
                case MedianMaskType.Diamond: mask = "diamond"; break;
                default: mask = "square"; break;
            }
            HObject result;
            HOperatorSet.MedianImage(image, out result,
                mask,
                radius,
                margin == MaskMarginMode.Cyclic ? "cyclic" : "mirrored");
            try { return new HImage(result); }
            finally { result.Dispose(); }
        }

        /// <summary>高斯平滑：size 必须是 ≥3 的奇数</summary>
        public static HImage Gauss(HImage image, int size)
        {
            HObject result;
            HOperatorSet.GaussImage(image, out result, size);
            try { return new HImage(result); }
            finally { result.Dispose(); }
        }

        #endregion

        #region 灰度形态学

        // 注意：Halcon 这一族算子的参数顺序都是 (Height, Width)，别顺手按 W,H 传

        public static HImage GrayDilation(HImage image, int width, int height)
        {
            HObject result;
            HOperatorSet.GrayDilationRect(image, out result, height, width);
            try { return new HImage(result); }
            finally { result.Dispose(); }
        }

        public static HImage GrayErosion(HImage image, int width, int height)
        {
            HObject result;
            HOperatorSet.GrayErosionRect(image, out result, height, width);
            try { return new HImage(result); }
            finally { result.Dispose(); }
        }

        /// <summary>开运算（先腐蚀后膨胀）：去掉亮点噪声</summary>
        public static HImage GrayOpening(HImage image, int width, int height)
        {
            HObject result;
            HOperatorSet.GrayOpeningRect(image, out result, height, width);
            try { return new HImage(result); }
            finally { result.Dispose(); }
        }

        /// <summary>闭运算（先膨胀后腐蚀）：去掉暗点噪声</summary>
        public static HImage GrayClosing(HImage image, int width, int height)
        {
            HObject result;
            HOperatorSet.GrayClosingRect(image, out result, height, width);
            try { return new HImage(result); }
            finally { result.Dispose(); }
        }

        #endregion

        #region 图像增强

        /// <summary>锐化（emphasize）：在 W×H 邻域内提升对比度，Factor 越大越锐</summary>
        public static HImage Emphasize(HImage image, int width, int height, double factor)
        {
            HObject result;
            HOperatorSet.Emphasize(image, out result, width, height, factor);
            try { return new HImage(result); }
            finally { result.Dispose(); }
        }

        /// <summary>局部对比度增强（illuminate）：Factor 取值 0~0.5</summary>
        public static HImage Illuminate(HImage image, int width, int height, double factor)
        {
            HObject result;
            HOperatorSet.Illuminate(image, out result, width, height, factor);
            try { return new HImage(result); }
            finally { result.Dispose(); }
        }

        /// <summary>线性亮度调节：out = in * multiplier + addend</summary>
        public static HImage ScaleIntensity(HImage image, double multiplier, double addend)
        {
            HObject result;
            HOperatorSet.ScaleImage(image, out result, multiplier, addend);
            try { return new HImage(result); }
            finally { result.Dispose(); }
        }

        /// <summary>反色</summary>
        public static HImage Invert(HImage image)
        {
            HObject result;
            HOperatorSet.InvertImage(image, out result);
            try { return new HImage(result); }
            finally { result.Dispose(); }
        }

        /// <summary>
        /// 全局直方图均衡（equ_histo_image）：把灰度分布拉伸到整个动态范围。
        /// 【实测】三通道彩色图原生支持（输出仍是 ch=3），不需要先 EnsureMono 拆通道。
        /// 注意它和"直方图规定化"不是一回事，跟局部均衡（equ_image）也不同 —— 后者需要模板尺寸。
        /// </summary>
        public static HImage EquHisto(HImage image)
        {
            HObject result;
            HOperatorSet.EquHistoImage(image, out result);
            try { return new HImage(result); }
            finally { result.Dispose(); }
        }

        #endregion

        #region 二值化

        /// <summary>
        /// 全局阈值二值化，输出 0/255 的 byte 图。
        /// 旧版用 GenImageConst + OverpaintRegion + GenImageProto + OverpaintRegion 四步凑出来，
        /// 其实 region_to_bin 一步就够，还顺带免掉了中间图像。
        /// </summary>
        public static HImage Threshold(HImage image, double low, double high, bool reverse)
        {
            HObject mono = EnsureMono(image);
            HObject region;
            try
            {
                HOperatorSet.Threshold(mono, out region, low, high);
                try
                {
                    HOperatorSet.GetImageSize(mono, out HTuple w, out HTuple h);
                    HObject bin;
                    // 与旧版一致：不反转时命中区域=255（白底黑图里的白目标），反转后命中区域=0
                    HOperatorSet.RegionToBin(region, out bin, reverse ? 0 : 255, reverse ? 255 : 0, w, h);
                    try { return new HImage(bin); }
                    finally { bin.Dispose(); }
                }
                finally
                {
                    region.Dispose();
                }
            }
            finally
            {
                if (!ReferenceEquals(mono, image)) mono.Dispose();
            }
        }

        /// <summary>
        /// 局部（动态）阈值二值化：以 W×H 邻域的均值为基准做判决，适合光照不均的现场图。
        /// stdDevScale / absThreshold 直接暴露给参数面板，不再像旧版那样把偏移量除以 100、
        /// 绝对阈值偷偷写死成 30。
        /// </summary>
        public static HImage VarThreshold(HImage image, int maskWidth, int maskHeight,
            double stdDevScale, double absThreshold, VarThresholdMode mode)
        {
            string lightDark;
            switch (mode)
            {
                case VarThresholdMode.Light: lightDark = "light"; break;
                case VarThresholdMode.Dark: lightDark = "dark"; break;
                case VarThresholdMode.Equal: lightDark = "equal"; break;
                default: lightDark = "not_equal"; break;
            }

            HObject mono = EnsureMono(image);
            HObject region;
            try
            {
                HOperatorSet.VarThreshold(mono, out region, maskWidth, maskHeight, stdDevScale, absThreshold, lightDark);
                try
                {
                    HOperatorSet.GetImageSize(mono, out HTuple w, out HTuple h);
                    HObject bin;
                    // 与 Threshold 保持同一套前景约定：命中的区域是白的（255）
                    HOperatorSet.RegionToBin(region, out bin, 255, 0, w, h);
                    try { return new HImage(bin); }
                    finally { bin.Dispose(); }
                }
                finally
                {
                    region.Dispose();
                }
            }
            finally
            {
                if (!ReferenceEquals(mono, image)) mono.Dispose();
            }
        }

        #endregion

        #region 落盘

        /// <summary>
        /// 把图像写成文件（扩展名决定格式）。
        ///
        /// 为什么不像 Core.Halcon 的 HalconBase.SaveImage 那样把带扩展名的全路径直接丢给 write_image？
        /// write_image 的语义是"格式由 Format 决定，文件名不带扩展名，缺了它自己补"。
        /// 传 "a.png" 时有的版本补成 a.png，有的补成 a.png.png —— 这里统一先剥掉扩展名再传，行为可控。
        /// </summary>
        public static void SaveImage(HImage image, string fullPath)
        {
            string extension = Path.GetExtension(fullPath);
            string? format = ToHalconFormat(extension);
            if (format == null)
                throw new NotSupportedException($"不支持的导出格式：{(string.IsNullOrEmpty(extension) ? "(无扩展名)" : extension)}");

            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            string withoutExtension = Path.Combine(
                directory ?? string.Empty,
                Path.GetFileNameWithoutExtension(fullPath));

            HOperatorSet.WriteImage(image, format, 0, withoutExtension);
        }

        /// <summary>扩展名 → write_image 的 Format 参数；不认识的返回 null 交给调用方报错</summary>
        private static string? ToHalconFormat(string extension)
        {
            switch (extension.TrimStart('.').ToLowerInvariant())
            {
                case "bmp": return "bmp";
                case "png": return "png";
                case "jpg":
                case "jpeg": return "jpg";
                case "tif":
                case "tiff": return "tiff";
                default: return null;
            }
        }

        #endregion
    }
}
