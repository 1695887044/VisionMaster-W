using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VisionMaster.Services
{
    /// <summary>
    /// 图片编码字节 → 原始像素 的**唯一**解码实现。
    ///
    /// 为什么必须只有一处
    /// ---------
    /// 两条收图链路都要求完全相同的通道约定：
    ///   HttpImageServer    = 外部推一张图 → 立刻跑一次流程 → 同步回结果
    ///   NetworkCameraServer = 客户端按相机该有的样子持续推帧
    /// 两边各写一份解码，早晚会出现"其中一条链路的彩色图颜色是反的"——
    /// 而这种错在灰度图上完全看不出来、检测也照跑，只表现为彩色判别类的算子结果莫名不对，
    /// 是最难定位的一类缺陷。规则只写这一处：灰度保持单通道，彩色一律 BGR24 交错。
    ///
    /// 为什么在后台线程用 WPF 的 BitmapDecoder 是安全的
    /// ---------
    /// 解码全程（Create → 转格式 → CopyPixels）都在当前这一个线程内完成，产出的 byte[]
    /// 是不带线程亲和性的纯托管数组；BitmapSource 不出本方法，
    /// 因此不存在"跨线程使用 DispatcherObject"的问题。
    /// BitmapCacheOption.OnLoad 让帧在 Create 时就完全载入，之后 MemoryStream 可以安全释放。
    /// </summary>
    internal static class ImageBytesCodec
    {
        /// <summary>解码结果：原始像素（行优先、无行填充）+ 尺寸</summary>
        internal readonly struct DecodedImage
        {
            /// <summary>灰度 = 1 字节/像素；彩色 = BGR 交错 3 字节/像素</summary>
            public byte[] Pixels { get; init; }

            public int Width { get; init; }

            public int Height { get; init; }

            /// <summary>通道数：1 = 灰度，3 = 彩色（BGR 交错）</summary>
            public int Channels { get; init; }
        }

        /// <summary>
        /// 解码图片编码字节。失败抛异常（调用方负责转成 4xx 响应）。
        /// </summary>
        internal static DecodedImage Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                throw new InvalidOperationException("图片字节为空");

            using var stream = new MemoryStream(bytes, writable: false);

            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);

            if (decoder.Frames.Count == 0)
                throw new InvalidOperationException("图片中没有可用的帧");

            BitmapSource frame = decoder.Frames[0];

            // 灰度图保持单通道（HALCON 侧走 GenImage1），其余一律转 BGR24
            // （与 HALCON 的 "bgr" 交错格式逐字节对应，消费侧不需要再换通道序）
            var isGray = IsGrayFormat(frame.Format);
            var targetFormat = isGray ? PixelFormats.Gray8 : PixelFormats.Bgr24;

            if (frame.Format != targetFormat)
            {
                var converted = new FormatConvertedBitmap();
                converted.BeginInit();
                converted.Source = frame;
                converted.DestinationFormat = targetFormat;
                converted.EndInit();
                frame = converted;
            }

            var width = frame.PixelWidth;
            var height = frame.PixelHeight;
            var channels = isGray ? 1 : 3;
            var stride = width * channels;

            var pixels = new byte[stride * height];
            frame.CopyPixels(pixels, stride, 0);

            return new DecodedImage
            {
                Pixels = pixels,
                Width = width,
                Height = height,
                Channels = channels
            };
        }

        private static bool IsGrayFormat(PixelFormat format)
            => format == PixelFormats.Gray8
            || format == PixelFormats.Gray16
            || format == PixelFormats.Gray32Float
            || format == PixelFormats.BlackWhite;
    }
}
