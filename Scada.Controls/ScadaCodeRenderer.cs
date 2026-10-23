using System;
using System.Globalization;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace VisionMaster.Scada.Controls
{
    /// <summary>
    /// 二维码 / 条码编码（ZXing.Net）。WPF 与 Web 宿主共用同一份实现，避免两端算法不一致
    /// 导致“桌面能扫、网页不能扫”。
    ///
    /// 失败一律返回 false + 可直接上屏的中文原因（空内容 / 超长 / 码制不支持 / 编码器异常），
    /// 不抛异常——一个坏内容不该让整幅画面打不开。
    /// </summary>
    public static class ScadaCodeRenderer
    {
        /// <summary>QR 内容上限（字节，UTF-8；ZXing 的实际容量还受纠错等级影响，这里只是先挡明显超长的）</summary>
        public const int MaxQrBytes = 2953;

        public const string FormatQr = "QR";
        public const string FormatCode128 = "Code128";
        public const string FormatCode39 = "Code39";
        public const string FormatEan13 = "EAN13";

        public static bool TryRender(
            string? content,
            string? format,
            int width,
            int height,
            int margin,
            string? errorCorrection,
            out BitmapSource? bitmap,
            out string? error)
        {
            bitmap = null;
            error = null;

            if (string.IsNullOrWhiteSpace(content))
            {
                error = "内容为空";
                return false;
            }

            var fmt = (format ?? FormatQr).Trim();
            var isQr = fmt.Equals(FormatQr, StringComparison.OrdinalIgnoreCase)
                       || fmt.Equals("QRCode", StringComparison.OrdinalIgnoreCase);

            if (isQr && Encoding.UTF8.GetByteCount(content) > MaxQrBytes)
            {
                error = $"内容过长：QR 上限 {MaxQrBytes} 字节（当前 {Encoding.UTF8.GetByteCount(content)} 字节）";
                return false;
            }

            if (!isQr && !ValidateBarcodeContent(content, fmt, out error))
                return false;

            try
            {
                var size = Math.Max(64, Math.Min(width, height));
                var options = isQr
                    ? new QrCodeEncodingOptions
                    {
                        Width = size,
                        Height = size,
                        Margin = Math.Max(0, margin),
                        CharacterSet = "UTF-8",
                        ErrorCorrection = MapErrorCorrection(errorCorrection),
                        PureBarcode = false,
                    }
                    : new EncodingOptions
                    {
                        Width = Math.Max(64, width),
                        Height = Math.Max(48, height),
                        Margin = Math.Max(0, margin),
                        PureBarcode = false,
                    };

                var writer = new BarcodeWriterPixelData
                {
                    Format = MapFormat(fmt),
                    Options = options,
                };

                var data = writer.Write(content);

                var source = BitmapSource.Create(
                    data.Width,
                    data.Height,
                    96,
                    96,
                    PixelFormats.Bgra32,
                    null,
                    data.Pixels,
                    data.Width * 4);

                source.Freeze();
                bitmap = source;
                return true;
            }
            catch (Exception ex)
            {
                error = "编码失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>把渲染出的位图再解一遍（断言/探针用：证明“真的能扫”，不是只画了个像码的图案）。</summary>
        public static bool TryDecode(BitmapSource? bitmap, out string? text)
        {
            text = null;
            if (bitmap == null)
                return false;

            try
            {
                var stride = bitmap.PixelWidth * 4;
                var pixels = new byte[stride * bitmap.PixelHeight];
                bitmap.CopyPixels(pixels, stride, 0);

                var source = new RGBLuminanceSource(
                    pixels,
                    bitmap.PixelWidth,
                    bitmap.PixelHeight,
                    RGBLuminanceSource.BitmapFormat.BGRA32);

                var reader = new BarcodeReaderGeneric();
                var result = reader.Decode(source);
                if (result == null)
                    return false;

                text = result.Text;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static ErrorCorrectionLevel MapErrorCorrection(string? value)
            => (value ?? "M").ToUpperInvariant() switch
            {
                "L" => ErrorCorrectionLevel.L,
                "Q" => ErrorCorrectionLevel.Q,
                "H" => ErrorCorrectionLevel.H,
                _ => ErrorCorrectionLevel.M,
            };

        private static BarcodeFormat MapFormat(string format) => format switch
        {
            _ when format.Equals(FormatCode128, StringComparison.OrdinalIgnoreCase) => BarcodeFormat.CODE_128,
            _ when format.Equals(FormatCode39, StringComparison.OrdinalIgnoreCase) => BarcodeFormat.CODE_39,
            _ when format.Equals(FormatEan13, StringComparison.OrdinalIgnoreCase) => BarcodeFormat.EAN_13,
            _ => BarcodeFormat.QR_CODE,
        };

        private static bool ValidateBarcodeContent(string content, string format, out string? error)
        {
            error = null;

            if (format.Equals(FormatEan13, StringComparison.OrdinalIgnoreCase))
            {
                var digits = content.Length;
                if ((digits != 12 && digits != 13) || !long.TryParse(content, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                {
                    error = "EAN13 只接受 12 或 13 位数字";
                    return false;
                }

                return true;
            }

            if (format.Equals(FormatCode39, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var ch in content)
                {
                    if (char.IsLower(ch))
                    {
                        error = "Code39 不支持小写字母（请用大写或改 Code128）";
                        return false;
                    }
                }
            }

            foreach (var ch in content)
            {
                if (ch > 127)
                {
                    error = $"{format} 只支持 ASCII 字符（遇到「{ch}」）";
                    return false;
                }
            }

            return true;
        }
    }
}
