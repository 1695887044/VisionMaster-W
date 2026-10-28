using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VmMcp;

/// <summary>
/// Decoded image with its ORIGINAL pixel format preserved. Bit depth matters here:
/// a byte fixture scaled by 257 silently clips at 255 and a uint2 histogram with the
/// wrong bin count reads as "all zeros" (ledger G-07 / G-08). Anything that normalises
/// first would hide exactly those bugs.
/// </summary>
internal sealed class LoadedImage
{
    public required string Path { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required string FormatName { get; init; }
    public required int BitsPerPixel { get; init; }
    public required int Channels { get; init; }
    public required double MaxValue { get; init; }
    public required byte[] Pixels { get; init; }
    public required int Stride { get; init; }
    public required int BytesPerSample { get; init; }
    public required bool IsGray { get; init; }
    public required string ChannelOrder { get; init; }

    public double Sample(int x, int y, int channel)
    {
        int offset = y * Stride + (x * Channels + channel) * BytesPerSample;
        return BytesPerSample == 1
            ? Pixels[offset]
            : Pixels[offset] | (Pixels[offset + 1] << 8);
    }

    /// <summary>Luminance normalised to 0..1 so images of different bit depths can be compared.</summary>
    public double Luma(int x, int y)
    {
        if (IsGray) return Sample(x, y, 0) / MaxValue;
        // channel order is stored as BGR(A) or RGB for 16-bit
        double c0, c1, c2;
        if (ChannelOrder == "BGR")
        {
            c0 = Sample(x, y, 2); // R
            c1 = Sample(x, y, 1); // G
            c2 = Sample(x, y, 0); // B
        }
        else
        {
            c0 = Sample(x, y, 0);
            c1 = Sample(x, y, 1);
            c2 = Sample(x, y, 2);
        }
        return (0.299 * c0 + 0.587 * c1 + 0.114 * c2) / MaxValue;
    }
}

internal sealed record ChannelStats(double Min, double Max, double Mean, double Std, long AtMin, long AtMax);

internal static class ImageTools
{
    public static LoadedImage Load(string path)
    {
        if (!File.Exists(path)) throw new ToolException($"image not found: {path}");

        BitmapFrame frame;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            frame = decoder.Frames[0];
        }
        catch (Exception ex)
        {
            throw new ToolException($"cannot decode '{Path.GetFileName(path)}': {ex.Message}");
        }

        var format = frame.Format;
        string name = FormatName(format);
        string originalName = name;
        bool supported = name is "Gray8" or "Gray16" or "Bgr24" or "Bgra32" or "Bgr32" or "Rgb48" or "Rgba64";

        if (!supported)
        {
            // Indexed / CMYK / exotic formats: normalise once and say so in the report.
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            converted.Freeze();
            frame = BitmapFrame.Create(converted);
            format = frame.Format;
            name = FormatName(format) + " (converted from " + originalName + ")";
        }

        int bpp = format.BitsPerPixel;
        int channels = format == PixelFormats.Gray8 || format == PixelFormats.Gray16 ? 1
            : format == PixelFormats.Bgr24 || format == PixelFormats.Rgb48 ? 3
            : 4;
        int bytesPerSample = bpp >= 48 || format == PixelFormats.Gray16 ? 2 : 1;
        double maxValue = bytesPerSample == 2 ? 65535 : 255;
        string order = format == PixelFormats.Rgb48 || format == PixelFormats.Rgba64 ? "RGB" : "BGR";
        bool gray = channels == 1;

        int stride = (bpp * frame.PixelWidth + 7) / 8;
        var pixels = new byte[stride * frame.PixelHeight];
        frame.CopyPixels(pixels, stride, 0);

        return new LoadedImage
        {
            Path = path,
            Width = frame.PixelWidth,
            Height = frame.PixelHeight,
            FormatName = name,
            BitsPerPixel = bpp,
            Channels = channels,
            MaxValue = maxValue,
            Pixels = pixels,
            Stride = stride,
            BytesPerSample = bytesPerSample,
            IsGray = gray,
            ChannelOrder = order,
        };
    }

    public static string FormatName(PixelFormat format)
    {
        if (format == PixelFormats.Gray8) return "Gray8";
        if (format == PixelFormats.Gray16) return "Gray16";
        if (format == PixelFormats.Bgr24) return "Bgr24";
        if (format == PixelFormats.Bgra32) return "Bgra32";
        if (format == PixelFormats.Bgr32) return "Bgr32";
        if (format == PixelFormats.Rgb48) return "Rgb48";
        if (format == PixelFormats.Rgba64) return "Rgba64";
        if (format == PixelFormats.Indexed8) return "Indexed8";
        if (format == PixelFormats.Indexed4) return "Indexed4";
        if (format == PixelFormats.Indexed1) return "Indexed1";
        if (format == PixelFormats.BlackWhite) return "BlackWhite";
        return format.ToString();
    }

    public static ChannelStats[] Stats(LoadedImage img, bool perChannel)
    {
        int channelCount = perChannel && !img.IsGray ? img.Channels : 1;
        var stats = new ChannelStats[channelCount];

        for (int c = 0; c < channelCount; c++)
        {
            double min = double.MaxValue, max = double.MinValue, sum = 0, sumSq = 0;
            long atMin = 0, atMax = 0;
            long n = 0;

            // Hot loop over every pixel of possibly-multi-megapixel images: inline the
            // channel arithmetic instead of calling Luma()/Sample() per pixel.
            int ch = img.Channels;
            int bps = img.BytesPerSample;
            var px = img.Pixels;
            int stride = img.Stride;

            for (int y = 0; y < img.Height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < img.Width; x++)
                {
                    int o = row + x * ch * bps;
                    double v;
                    if (img.IsGray)
                    {
                        v = bps == 1 ? px[o] : px[o] | (px[o + 1] << 8);
                    }
                    else if (perChannel)
                    {
                        int ci = c * bps;
                        v = bps == 1 ? px[o + ci] : px[o + ci] | (px[o + ci + 1] << 8);
                    }
                    else
                    {
                        // luminance from BGR(A) / RGB(A) at 8 or 16 bits per sample
                        double b, g, r;
                        if (img.ChannelOrder == "BGR")
                        {
                            b = bps == 1 ? px[o] : px[o] | (px[o + 1] << 8);
                            g = bps == 1 ? px[o + 1] : px[o + 2] | (px[o + 3] << 8);
                            r = bps == 1 ? px[o + 2] : px[o + 4] | (px[o + 5] << 8);
                        }
                        else
                        {
                            r = bps == 1 ? px[o] : px[o] | (px[o + 1] << 8);
                            g = bps == 1 ? px[o + 1] : px[o + 2] | (px[o + 3] << 8);
                            b = bps == 1 ? px[o + 2] : px[o + 4] | (px[o + 5] << 8);
                        }
                        v = 0.299 * r + 0.587 * g + 0.114 * b;
                    }

                    if (v < min) { min = v; atMin = 1; }
                    else if (v == min) atMin++;
                    if (v > max) { max = v; atMax = 1; }
                    else if (v == max) atMax++;
                    sum += v;
                    sumSq += v * v;
                    n++;
                }
            }

            double mean = sum / n;
            double variance = Math.Max(0, sumSq / n - mean * mean);
            stats[c] = new ChannelStats(min, max, mean, Math.Sqrt(variance), atMin, atMax);
        }
        return stats;
    }

    public static long[] Histogram(LoadedImage img, int bins, out double maxValue)
    {
        maxValue = img.MaxValue;
        var hist = new long[bins];
        double scale = bins / (img.MaxValue + 1.0);
        for (int y = 0; y < img.Height; y++)
        {
            for (int x = 0; x < img.Width; x++)
            {
                double v = img.IsGray ? img.Sample(x, y, 0) : img.Luma(x, y) * img.MaxValue;
                int b = (int)(v * scale);
                if (b < 0) b = 0;
                if (b >= bins) b = bins - 1;
                hist[b]++;
            }
        }
        return hist;
    }

    /// <summary>
    /// Variance of the 4-neighbour Laplacian on luminance. This is the number that
    /// actually tells a focus sweep apart: mean brightness barely moves while sharpness
    /// changes a lot, so a dataset "looks fine" by mean and is unusable for matching
    /// (see the pcb_focus investigation in the ledger).
    /// </summary>
    public static double Sharpness(LoadedImage img)
    {
        if (img.Width < 3 || img.Height < 3) return 0;

        double sum = 0, sumSq = 0;
        long n = 0;
        for (int y = 1; y < img.Height - 1; y++)
        {
            for (int x = 1; x < img.Width - 1; x++)
            {
                double c = img.Luma(x, y);
                double lap = 4 * c - img.Luma(x - 1, y) - img.Luma(x + 1, y) - img.Luma(x, y - 1) - img.Luma(x, y + 1);
                sum += lap;
                sumSq += lap * lap;
                n++;
            }
        }
        if (n == 0) return 0;
        double mean = sum / n;
        return Math.Max(0, sumSq / n - mean * mean) * 10000; // scaled to a readable range
    }

    public static string Describe(LoadedImage img, bool perChannel)
    {
        var stats = Stats(img, perChannel);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"size: {img.Width}x{img.Height}");
        sb.AppendLine($"format: {img.FormatName} ({img.BitsPerPixel} bpp, {img.Channels} channel(s), {(img.BytesPerSample == 2 ? "16-bit" : "8-bit")})");
        long total = (long)img.Width * img.Height;
        for (int c = 0; c < stats.Length; c++)
        {
            var s = stats[c];
            string label = stats.Length == 1 ? "stats" : $"stats[{c}]";
            sb.AppendLine($"{label}: min={s.Min:F0} max={s.Max:F0} mean={s.Mean:F2} std={s.Std:F2} " +
                          $"atMin={s.AtMin} ({s.AtMin * 100.0 / total:F1}%) atMax={s.AtMax} ({s.AtMax * 100.0 / total:F1}%)");
        }

        var s0 = stats[0];
        sb.AppendLine($"sharpness: {Sharpness(img):F1} (variance of Laplacian; higher = sharper)");
        if (s0.Min == s0.Max) sb.AppendLine("warning: constant image (no contrast) — not usable as a fixture");
        if (img.BytesPerSample == 2 && s0.Max < img.MaxValue / 4)
            sb.AppendLine($"warning: 16-bit image whose maximum is only {s0.Max:F0} of {img.MaxValue:F0} — " +
                          "usually a byte fixture that was converted instead of scaled (ledger G-08)");
        if (s0.AtMax * 100.0 / total > 20) sb.AppendLine($"note: {s0.AtMax * 100.0 / total:F0}% of pixels sit at the white end (normal for a bright background, suspicious for a mid-grey scene)");
        if (s0.AtMin * 100.0 / total > 20) sb.AppendLine($"note: {s0.AtMin * 100.0 / total:F0}% of pixels sit at the black end (normal for a dark background, suspicious if detail is expected there)");
        return sb.ToString().TrimEnd();
    }

    public static void Save(LoadedImage img, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        PixelFormat format = img.FormatName.StartsWith("Gray16") ? PixelFormats.Gray16
            : img.FormatName.StartsWith("Gray8") ? PixelFormats.Gray8
            : img.FormatName.StartsWith("Rgb48") ? PixelFormats.Rgb48
            : img.FormatName.StartsWith("Rgba64") ? PixelFormats.Rgba64
            : img.FormatName.StartsWith("Bgr24") ? PixelFormats.Bgr24
            : img.FormatName.StartsWith("Bgra32") ? PixelFormats.Bgra32
            : PixelFormats.Bgr32;

        var source = BitmapSource.Create(img.Width, img.Height, 96, 96, format, null, img.Pixels, img.Stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        encoder.Save(fs);
    }

    public static LoadedImage Crop(LoadedImage src, int x, int y, int w, int h)
    {
        x = Math.Clamp(x, 0, src.Width - 1);
        y = Math.Clamp(y, 0, src.Height - 1);
        w = Math.Clamp(w, 1, src.Width - x);
        h = Math.Clamp(h, 1, src.Height - y);

        int rowBytes = w * src.Channels * src.BytesPerSample;
        var dst = new byte[rowBytes * h];
        for (int row = 0; row < h; row++)
        {
            int srcOffset = (y + row) * src.Stride + x * src.Channels * src.BytesPerSample;
            Buffer.BlockCopy(src.Pixels, srcOffset, dst, row * rowBytes, rowBytes);
        }

        return new LoadedImage
        {
            Path = src.Path,
            Width = w,
            Height = h,
            FormatName = src.FormatName,
            BitsPerPixel = src.BitsPerPixel,
            Channels = src.Channels,
            MaxValue = src.MaxValue,
            Pixels = dst,
            Stride = rowBytes,
            BytesPerSample = src.BytesPerSample,
            IsGray = src.IsGray,
            ChannelOrder = src.ChannelOrder,
        };
    }

    public static string ResolvePath(string? path, string what)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ToolException($"{what} is required");
        var p = Paths.Abs(path);
        if (!File.Exists(p)) throw new ToolException($"{what} not found: {Paths.Rel(p)}");
        return p;
    }
}
