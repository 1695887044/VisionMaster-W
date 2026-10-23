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

        /// <summary>
        /// 灰度顶帽：原图 − 开运算，只保留被开运算清掉的**亮结构**（亮斑、毛刺、亮点）。
        ///
        /// 为什么用"开运算 + 减"而不是 GrayTophat：后者要传一个结构化元素区域（SE），
        /// SE 的参考点/形状口径与 Rect 族不完全一致；这里复用已经实测过的 GrayOpeningRect，
        /// 语义与 gray_tophat 等价，且与四个灰度形态学算子的"矩形模板 W×H"参数完全对齐。
        /// </summary>
        public static HImage GrayTopHat(HImage image, int width, int height)
        {
            HImage opened = GrayOpening(image, width, height);
            try { return SubImage(image, opened); }
            finally { opened.Dispose(); }
        }

        /// <summary>
        /// 灰度黑帽：闭运算 − 原图，只保留被闭运算填掉的**暗结构**（暗点、裂纹、黑斑）。
        /// 实现口径同 <see cref="GrayTopHat"/>（复用 GrayClosingRect）。
        /// </summary>
        public static HImage GrayBlackHat(HImage image, int width, int height)
        {
            HImage closed = GrayClosing(image, width, height);
            try { return SubImage(closed, image); }
            finally { closed.Dispose(); }
        }

        /// <summary>逐像素相减（mult=1, add=0）；byte 图负值会被 Halcon 截到 0，顶帽/黑帽本身非负，不受影响</summary>
        private static HImage SubImage(HImage minuend, HImage subtrahend)
        {
            HObject result;
            HOperatorSet.SubImage(minuend, subtrahend, out result, 1, 0);
            try { return new HImage(result); }
            finally { result.Dispose(); }
        }

        #endregion

        #region 边缘检测

        /// <summary>
        /// Sobel 梯度幅值边缘图。filterType 只认 "sum_abs" / "sum"；
        /// maskSize 合法值是 3/5/7/9/11（算子层已收敛为正奇数）。
        /// </summary>
        public static HImage SobelAmp(HImage image, SobelFilterType filterType, int maskSize)
        {
            string type = filterType == SobelFilterType.Sum ? "sum" : "sum_abs";
            HObject result;
            HOperatorSet.SobelAmp(image, out result, type, maskSize);
            try { return new HImage(result); }
            finally { result.Dispose(); }
        }

        /// <summary>
        /// Laplace 二阶差分绝对边缘图。filterMask 只认 "n_4" / "n_8"；
        /// maskSize 合法值是 3/5/7/9/11（算子层已收敛为正奇数）。
        /// </summary>
        public static HImage Laplace(HImage image, LaplaceFilterMask filterMask, int maskSize)
        {
            string mask = filterMask == LaplaceFilterMask.N8 ? "n_8" : "n_4";
            HObject result;
            HOperatorSet.Laplace(image, out result, "absolute", maskSize, mask);
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

        #region 算术运算（图像 × 图像）

        /// <summary>
        /// 逐像素算术的两个输入必须"同形同型"：尺寸一致、通道数一致。
        /// 为什么在这里做守卫而不是靠 Halcon：sub_image / add_image / mult_image 在尺寸不符时
        /// 是**报错**（#1201 之类，还算友好），但通道数不符（3 通道对 1 通道）在部分版本上会
        /// 按"每个通道各自配一张"去广播 —— 结果是一张看起来正常、语义整个错掉的图。
        /// 差分这类用途恰恰最怕静默错，所以一律先判后算，不符直接抛中文异常。
        /// </summary>
        public static void EnsureSameShape(HImage a, HImage b, string operationName)
        {
            ImageSize(a, out int aw, out int ah);
            ImageSize(b, out int bw, out int bh);
            if (aw != bw || ah != bh)
            {
                throw new InvalidOperationException(
                    $"{operationName}：两张图像尺寸不一致（{aw}×{ah} vs {bw}×{bh}）。" +
                    "算术运算要求逐像素一一对应，请先裁剪/缩放或改用「固定框裁剪」对齐尺寸");
            }

            int ca = CountChannels(a);
            int cb = CountChannels(b);
            if (ca != cb)
            {
                throw new InvalidOperationException(
                    $"{operationName}：两张图像通道数不一致（{ca} vs {cb}）。" +
                    "彩色图与灰度图不能直接做逐像素算术（Halcon 会按通道广播，得到语义错误的图），" +
                    "请先把两者统一成灰度（如加一步「转灰度」算子）或统一成彩色");
            }
        }

        /// <summary>
        /// 逐像素相加。mult/add 是 Halcon 的线性项：out = (a + b) * mult + add。
        /// <paramref name="mult"/> 传 1、<paramref name="add"/> 传 0 即纯加法。
        /// </summary>
        public static HImage AddImage(HImage a, HImage b, double mult, double add)
        {
            EnsureSameShape(a, b, "图像相加");
            HObject result;
            HOperatorSet.AddImage(a, b, out result, mult, add);
            try { return new HImage(result); }
            finally { result.Dispose(); }
        }

        /// <summary>
        /// 逐像素相减：out = (a − b) * mult + add。
        ///
        /// 【重要】默认参数下 byte 图**负值被截到 0**（Halcon 的 byte 无符号）——
        /// 所以"目标比基准暗多少"这种带符号的差分必须走 <see cref="AbsDiffImage"/> 或先转 real。
        /// 使用者要带符号结果时，用 mult/add 把结果平移到灰阶中点（如 add = 128）。
        /// </summary>
        public static HImage SubImage(HImage a, HImage b, double mult, double add)
        {
            EnsureSameShape(a, b, "图像相减");
            HObject result;
            HOperatorSet.SubImage(a, b, out result, mult, add);
            try { return new HImage(result); }
            finally { result.Dispose(); }
        }

        /// <summary>
        /// 逐像素绝对差：|a − b|，结果非负，是"两张图差多少"最安全的口径
        /// （不会像 sub_image 那样把负差静默截成 0）。
        /// </summary>
        /// <remarks>
        /// HalconDotNet 的 abs_diff_image 只有 4 参重载（image1, image2, imageAbsDiff, mult），
        /// **没有 add**——与 sub_image 的 5 参签名不对称，照文档写会 CS1501。
        /// </remarks>
        public static HImage AbsDiffImage(HImage a, HImage b, double mult)
        {
            EnsureSameShape(a, b, "图像绝对差");
            HObject result;
            HOperatorSet.AbsDiffImage(a, b, out result, mult);
            try { return new HImage(result); }
            finally { result.Dispose(); }
        }

        /// <summary>逐像素相乘：out = (a × b) * mult + add。byte × byte 很容易溢出到 255，mult 常用 1/255 之类的缩放</summary>
        public static HImage MultImage(HImage a, HImage b, double mult, double add)
        {
            EnsureSameShape(a, b, "图像相乘");
            HObject result;
            HOperatorSet.MultImage(a, b, out result, mult, add);
            try { return new HImage(result); }
            finally { result.Dispose(); }
        }

        /// <summary>
        /// 逐像素相除：out = (a / b) * mult + add。
        /// <paramref name="b"/> 里有 0 时 Halcon 的行为是"饱和到最大值"，不抛异常 ——
        /// 所以这里显式把"除数为 0"体检一遍，报告有多少像素会被污染，避免静默出一堆白点。
        /// </summary>
        public static HImage DivImage(HImage a, HImage b, double mult, double add, out int zeroDivisorCount)
        {
            EnsureSameShape(a, b, "图像相除");

            // 统计除数为 0 的像素数：不是错误（Halcon 会饱和），但必须让用户看见
            HObject zeroRegion;
            HOperatorSet.Threshold(b, out zeroRegion, 0, 0);
            try
            {
                // HalconDotNet 没有 count_region 的托管名 CountRegion；区域面积统一用 area_center
                HOperatorSet.AreaCenter(zeroRegion, out HTuple area, out _, out _);
                zeroDivisorCount = area.Length > 0 ? (int)area.D : 0;
            }
            finally
            {
                zeroRegion.Dispose();
            }

            HObject result;
            HOperatorSet.DivImage(a, b, out result, mult, add);
            try { return new HImage(result); }
            finally { result.Dispose(); }
        }

        /// <summary>逐像素取较小值（min_image），常用于"夹住过亮的背景"</summary>
        public static HImage MinImage(HImage a, HImage b)
        {
            EnsureSameShape(a, b, "图像取较小值");
            HObject result;
            HOperatorSet.MinImage(a, b, out result);
            try { return new HImage(result); }
            finally { result.Dispose(); }
        }

        /// <summary>逐像素取较大值（max_image），常用于"取两张图里更亮的那份"</summary>
        public static HImage MaxImage(HImage a, HImage b)
        {
            EnsureSameShape(a, b, "图像取较大值");
            HObject result;
            HOperatorSet.MaxImage(a, b, out result);
            try { return new HImage(result); }
            finally { result.Dispose(); }
        }

        /// <summary>
        /// 逐像素与常数相加（add_image_const），不需要第二张图 ——
        /// 这正是 PreProcessing 链内算子能做的"常数版"算术（链内算子是单图变换）。
        /// </summary>
        public static HImage AddConst(HImage image, double value)
            => ConstantArithmetic(image, value, AddKernel);

        /// <summary>逐像素与常数相减：out = image − value（byte 图负值截到 0）</summary>
        public static HImage SubConst(HImage image, double value)
            => ConstantArithmetic(image, value, SubKernel);

        /// <summary>逐像素与常数相乘（mult_image 的常数形态）：out = image × value，高光易饱和到 255</summary>
        public static HImage MultImageConst(HImage image, double value)
            => ConstantArithmetic(image, value, MultKernel);

        private static void AddKernel(HObject image, HObject constImage, out HObject result, double mult, double add)
            => HOperatorSet.AddImage(image, constImage, out result, mult, add);

        private static void SubKernel(HObject image, HObject constImage, out HObject result, double mult, double add)
            => HOperatorSet.SubImage(image, constImage, out result, mult, add);

        private static void MultKernel(HObject image, HObject constImage, out HObject result, double mult, double add)
            => HOperatorSet.MultImage(image, constImage, out result, mult, add);

        /// <summary>
        /// 常数算术的统一外壳：HalconDotNet 的 add_image/sub_image/mult_image **没有常数重载**
        /// （第二个 HObject 参数不接受 HTuple，照文档写会 CS1503），
        /// 所以这里用 gen_image_const + gen_image_proto 造一张与输入同尺寸同类型的常数图，
        /// 再走双图算子。常数图与结果图都在 finally 里释放。
        /// </summary>
        private static HImage ConstantArithmetic(
            HImage image,
            double value,
            ConstantArithmeticKernel kernel)
        {
            HOperatorSet.GetImageSize(image, out HTuple w, out HTuple h);
            string type = GetImageTypeName(image);

            HObject proto;
            HObject constImage;
            HOperatorSet.GenImageConst(out proto, type, w, h);
            try
            {
                HOperatorSet.GenImageProto(proto, out constImage, value);
            }
            finally
            {
                proto.Dispose();
            }

            try
            {
                HObject result;
                kernel(image, constImage, out result, 1, 0);
                try { return new HImage(result); }
                finally { result.Dispose(); }
            }
            finally
            {
                constImage.Dispose();
            }
        }

        /// <summary>常数算术的算子选择器（第一个参数是输入图，第二个是造出来的常数图）</summary>
        private delegate void ConstantArithmeticKernel(
            HObject image, HObject constImage, out HObject result, double mult, double add);

        /// <summary>取图像像素类型的 Halcon 名（byte / int1 / uint2 / real …），失败回落 byte</summary>
        private static string GetImageTypeName(HImage image)
        {
            try
            {
                HOperatorSet.GetImageType(image, out HTuple type);
                return type.Length > 0 ? type.S : "byte";
            }
            catch
            {
                return "byte";
            }
        }

        /// <summary>绝对值（abs_image）：把"带符号的 real 差分"变成可直接上屏的亮度图</summary>
        public static HImage AbsImage(HImage image)
        {
            HObject result;
            HOperatorSet.AbsImage(image, out result);
            try { return new HImage(result); }
            finally { result.Dispose(); }
        }

        /// <summary>
        /// 转 real 图（convert_image_type 到 'real'）。
        /// 用途：做带符号差分前先把 byte 升上去，否则负差会被截断成 0 —— 这是差分类功能最常见的静默错误。
        /// </summary>
        public static HImage ToReal(HImage image)
        {
            HObject result;
            HOperatorSet.ConvertImageType(image, out result, "real");
            try { return new HImage(result); }
            finally { result.Dispose(); }
        }

        /// <summary>
        /// 把 real 图按 min/max 线性映射回 [0, 255] 的 byte 图（scale_image 一族），
        /// 用于"差分结果直接上屏"：real 图的原始动态范围常常远超 byte，不归一化就是一片白或一片黑。
        /// </summary>
        public static HImage RealToByteNormalized(HImage image)
        {
            // 取真实极值：min_max_gray 的 Percent=0 即"不做百分位裁剪"
            HOperatorSet.MinMaxGray(image, image, 0, out HTuple min, out HTuple max, out _);
            double lo = min.D;
            double hi = max.D;

            HObject scaled;
            if (System.Math.Abs(hi - lo) < 1e-9)
            {
                // 全图一个值：没有任何对比信息，直接给一张全 0 的图（比塞 random 噪声更可判读）
                HOperatorSet.GenImageConst(out scaled, "byte", 1, 1);
                try { return new HImage(scaled); }
                finally { scaled.Dispose(); }
            }

            HObject result;
            HOperatorSet.ScaleImage(image, out result, 255.0 / (hi - lo), -lo * 255.0 / (hi - lo));
            try { return new HImage(result); }
            finally { result.Dispose(); }
        }

        /// <summary>
        /// 按阈值把差分图变成缺陷区域（选中"差分超过阈值"的像素）。
        /// 返回的 HImage 是 0/255 的 byte 二值图，可直接接下游 Blob / 判定。
        /// </summary>
        public static HImage ThresholdBinary(HImage image, double low, double high, bool reverse)
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
                    HOperatorSet.RegionToBin(region, out bin, reverse ? 0 : 255, reverse ? 255 : 0, w, h);
                    try { return new HImage(bin); }
                    finally { bin.Dispose(); }
                }
                finally { region.Dispose(); }
            }
            finally
            {
                if (!ReferenceEquals(mono, image)) mono.Dispose();
            }
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
