using System;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HalconDotNet;

namespace VisionMaster.Helpers
{
    /// <summary>
    /// Halcon 图像 → WPF 位图的单向转换。
    ///
    /// 为什么要单独开一个助手，而不是在图元控件里转
    /// ---------
    /// 图元控件库（VM.Scada.Controls）<b>刻意不引用 HalconDotNet</b>：画面领域层要保持"不认识
    /// 视觉库"的干净边界，否则以后换算法库要连画面一起改。于是"HImage 的像素 → WPF 位图"
    /// 这件事只能在宿主侧做一次，做在 <c>RegistryScadaValueSource</c> 的取值出口上，
    /// 图元只认 <see cref="ImageSource"/>。
    ///
    /// 线程约定（重要）
    /// ---------
    /// 转换发生在<b>流程线程</b>（变量值被流程改完，ValueChanged 就在那条线程上触发）。
    /// 因此产出的 <see cref="BitmapSource"/> 一律 <c>Freeze()</c> 后再交出去——
    /// 冻结后位图不可变、可跨线程读，UI 线程拿它画图不必再切线程，也不会踩 WPF 的
    /// "调用线程无法访问此对象"。
    ///
    /// 支持范围（刻意收窄）
    /// ---------
    /// 只处理视觉流程里真正常见的两类：单通道灰度图、三通道彩色图。其余（多光谱、方向图、
    /// 复数图）返回 null，图元据此显示"无图像"，而不是抛异常把整页画面打断。
    /// </summary>
    public static class HalconImageHelper
    {
        /// <summary>
        /// 把 Halcon 图像转成 WPF 位图。任何转换不了的情况（通道数不支持、像素类型不认识、
        /// 指针为空）都返回 null，不抛异常——调用点是在数据泵的取值路径上，一个坏图不该让整页崩掉。
        /// </summary>
        public static BitmapSource? ToBitmapSource(HImage? image)
        {
            if (image == null)
                return null;

            try
            {
                HOperatorSet.CountChannels(image, out HTuple channels);
                int count = channels.I;

                return count switch
                {
                    1 => FromSingleChannel(image),
                    3 => FromThreeChannels(image),
                    _ => null,
                };
            }
            catch (HalconException)
            {
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 缩略图专用快路径：**先在 HALCON 侧等比缩小，再转位图**。
        ///
        /// 为什么要单独开一条
        /// ---------
        /// 全尺寸转位图的代价是"每帧 × 每输出端口"都要付的：为得到一张最长边 160px 的缩略图，
        /// 先把整张图（500 万像素彩图约 15MB）搬进托管堆。高频采图下这笔分配与随之而来的
        /// GC 压力完全没必要——先把图缩小几十倍再转，转换量直接降到千分之几。
        ///
        /// 为什么不影响任何算法精度
        /// ---------
        /// 本方法只服务缩略图（<see cref="Core.Halcon.Models.ImageFrame.Thumbnail"/>），
        /// 而缩略图**只用于图像集网格渲染**：大图显示、导出、标注、取色走的都是
        /// <see cref="Core.Halcon.Models.ImageFrame.Image"/>（全尺寸原图）。
        /// 因此这条路径与视觉算法结果**没有任何交集**。
        ///
        /// 为什么只对 byte / int2 / uint2 走快路径
        /// ---------
        /// int4 / real 的显示走 <c>StretchToGray8</c>：量程（min/max）是在**被转换的那张图**上
        /// 统计的。先缩小会让缩略图的量程取自缩小后的图，对比度与全图不一致——这类图多是
        /// 视差 / 深度 / 浮点结果图，为省一点开销引入"缩略图和大图看起来不一样"的困惑不值得。
        /// 故这两类（以及非常规通道数）返回 null，由调用方回落全尺寸路径。
        ///
        /// 插值为什么必须是 bilinear
        /// ---------
        /// HALCON 默认 "constant" 相当于最近邻：缩十几倍以上时细线条会**整条消失**、出现摩尔纹——
        /// 那不是"缩小"，是失真。bilinear 取邻域加权，才是缩略图该有的样子。
        /// （若该 HALCON 版本不接受此插值串，异常会被吞掉并回落全尺寸路径，行为不会变坏。）
        /// </summary>
        /// <returns>成功返回缩略图位图；不适用（int4/real/非常规通道）或失败返回 null</returns>
        public static BitmapSource? ToThumbnailSource(HImage? image, int maxSize)
        {
            if (image == null)
                return null;

            try
            {
                HOperatorSet.CountChannels(image, out HTuple channels);
                int count = channels.I;
                if (count != 1 && count != 3)
                    return null;

                // 读尺寸与像素类型（顺带做守卫：宽高非法或指针为空时不必往下走）
                string type;
                int w, h;
                if (count == 1)
                {
                    HOperatorSet.GetImagePointer1(image, out HTuple p, out HTuple t,
                                                  out HTuple ww, out HTuple hh);
                    if (p.IP == IntPtr.Zero) return null;
                    type = t.S; w = ww.I; h = hh.I;
                }
                else
                {
                    HOperatorSet.GetImagePointer3(image, out HTuple pr, out HTuple pg, out HTuple pb,
                                                  out HTuple t, out HTuple ww, out HTuple hh);
                    if (pr.IP == IntPtr.Zero || pg.IP == IntPtr.Zero || pb.IP == IntPtr.Zero) return null;
                    type = t.S; w = ww.I; h = hh.I;
                }

                if (w <= 0 || h <= 0)
                    return null;

                // int4 / real 的显示依赖本图 min/max 拉伸，缩放会改变量程 → 不走快路径
                if (!string.Equals(type, "byte", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(type, "int2", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(type, "uint2", StringComparison.OrdinalIgnoreCase))
                    return null;

                int max = Math.Max(32, maxSize);
                double longest = Math.Max(w, h);

                // 原图本来就不超过目标尺寸：直接转，不做无意义的缩放
                if (longest <= max)
                    return ToBitmapSource(image);

                double scale = max / longest;
                int targetW = Math.Max(1, (int)Math.Round(w * scale));
                int targetH = Math.Max(1, (int)Math.Round(h * scale));

                HImage? zoomed = null;
                try
                {
                    zoomed = image.ZoomImageSize(targetW, targetH, "bilinear");
                    return ToBitmapSource(zoomed);
                }
                finally
                {
                    // 缩放结果是一份**新的非托管内存**，用完必须归还，否则每帧漏一张图
                    if (zoomed != null && zoomed.IsInitialized())
                        zoomed.Dispose();
                }
            }
            catch (HalconException)
            {
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 单通道图：按 Halcon 的像素类型分派。
        /// byte / uint2 直接搬运（灰度级语义一致），int4 / real 取值范围不固定，
        /// 按本图自己的最小最大值线性拉伸到 0~255 再显示（这类图多是视差图、深度图、浮点结果图）。
        /// </summary>
        private static BitmapSource? FromSingleChannel(HImage image)
        {
            HOperatorSet.GetImagePointer1(image, out HTuple pointer, out HTuple type,
                                          out HTuple width, out HTuple height);

            int w = width.I;
            int h = height.I;
            IntPtr data = pointer.IP;
            if (w <= 0 || h <= 0 || data == IntPtr.Zero)
                return null;

            switch (type.S)
            {
                case "byte":
                {
                    var buffer = new byte[w * h];
                    Marshal.Copy(data, buffer, 0, buffer.Length);
                    return Create(w, h, PixelFormats.Gray8, buffer, w);
                }

                case "int2":
                case "uint2":
                {
                    var buffer = new byte[w * h * 2];
                    Marshal.Copy(data, buffer, 0, buffer.Length);
                    return Create(w, h, PixelFormats.Gray16, buffer, w * 2);
                }

                case "int4":
                {
                    var raw = new int[w * h];
                    Marshal.Copy(data, raw, 0, raw.Length);
                    return StretchToGray8(w, h, raw);
                }

                case "real":
                {
                    var raw = new float[w * h];
                    Marshal.Copy(data, raw, 0, raw.Length);
                    return StretchToGray8(w, h, raw);
                }

                default:
                    return null;
            }
        }

        /// <summary>
        /// 三通道彩色图：Halcon 把三个平面分开存（red/green/blue 各一根指针），
        /// 而 WPF 的 Bgr24 要求三个分量逐像素交织。这里做一次交织重排。
        /// 非 byte 类型的三通道图（少见）不处理。
        /// </summary>
        private static BitmapSource? FromThreeChannels(HImage image)
        {
            HOperatorSet.GetImagePointer3(image, out HTuple red, out HTuple green, out HTuple blue,
                                          out HTuple type, out HTuple width, out HTuple height);

            if (!string.Equals(type.S, "byte", StringComparison.OrdinalIgnoreCase))
                return null;

            int w = width.I;
            int h = height.I;
            IntPtr pr = red.IP;
            IntPtr pg = green.IP;
            IntPtr pb = blue.IP;
            if (w <= 0 || h <= 0 || pr == IntPtr.Zero || pg == IntPtr.Zero || pb == IntPtr.Zero)
                return null;

            int pixels = w * h;
            var r = new byte[pixels];
            var g = new byte[pixels];
            var b = new byte[pixels];
            Marshal.Copy(pr, r, 0, pixels);
            Marshal.Copy(pg, g, 0, pixels);
            Marshal.Copy(pb, b, 0, pixels);

            var buffer = new byte[pixels * 3];
            for (int i = 0; i < pixels; i++)
            {
                int offset = i * 3;
                buffer[offset] = b[i];
                buffer[offset + 1] = g[i];
                buffer[offset + 2] = r[i];
            }

            return Create(w, h, PixelFormats.Bgr24, buffer, w * 3);
        }

        /// <summary>int4 图线性拉伸到 8 位灰度（本图自身的最小最大值作为量程）。</summary>
        private static BitmapSource? StretchToGray8(int width, int height, int[] raw)
        {
            int min = int.MaxValue;
            int max = int.MinValue;
            for (int i = 0; i < raw.Length; i++)
            {
                int value = raw[i];
                if (value < min) min = value;
                if (value > max) max = value;
            }

            var buffer = new byte[raw.Length];
            double span = (double)max - min;
            for (int i = 0; i < raw.Length; i++)
            {
                buffer[i] = span > 0
                    ? (byte)Math.Round((raw[i] - min) * 255.0 / span)
                    : (byte)0;
            }

            return Create(width, height, PixelFormats.Gray8, buffer, width);
        }

        /// <summary>
        /// real 图线性拉伸到 8 位灰度。浮点图里常有 NaN / ±Inf（无效点），
        /// 统计量程时跳过，显示时置 0——否则一个 NaN 就能把整幅图的对比度拉平。
        /// </summary>
        private static BitmapSource? StretchToGray8(int width, int height, float[] raw)
        {
            float min = float.MaxValue;
            float max = float.MinValue;
            for (int i = 0; i < raw.Length; i++)
            {
                float value = raw[i];
                if (!float.IsFinite(value)) continue;
                if (value < min) min = value;
                if (value > max) max = value;
            }

            if (min > max)
                return null;

            var buffer = new byte[raw.Length];
            float span = max - min;
            for (int i = 0; i < raw.Length; i++)
            {
                float value = raw[i];
                if (!float.IsFinite(value))
                {
                    buffer[i] = 0;
                    continue;
                }

                buffer[i] = span > 0
                    ? (byte)Math.Round((value - min) * 255.0 / span)
                    : (byte)0;
            }

            return Create(width, height, PixelFormats.Gray8, buffer, width);
        }

        /// <summary>
        /// 统一出口：造位图并冻结。冻结是必须的——转换在流程线程发生，
        /// 不冻结的位图交给 UI 线程读会直接抛跨线程访问异常。
        /// </summary>
        private static BitmapSource Create(int width, int height, PixelFormat format, byte[] buffer, int stride)
        {
            var source = BitmapSource.Create(width, height, 96, 96, format, null, buffer, stride);
            source.Freeze();
            return source;
        }
    }
}
