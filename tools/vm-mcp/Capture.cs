using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VmMcp;

internal sealed record CapturedImage(int Width, int Height, byte[] Bgra);

/// <summary>
/// Window / screen pixel capture without System.Drawing.
/// The PNG is produced through WPF's imaging stack (PresentationCore), which ships
/// with the WindowsDesktop shared framework, so this project needs no NuGet package.
/// </summary>
internal static class Capture
{
    /// <summary>
    /// Grab a window. PrintWindow with PW_RENDERFULLCONTENT (flag 2) is tried first
    /// because it also works when the window is occluded or the session has no active
    /// monitor; BitBlt from the screen DC is the fallback for windows that answer
    /// PrintWindow with a blank surface.
    /// </summary>
    public static CapturedImage GrabWindow(IntPtr hwnd, Native.RECT rect, out string method)
    {
        int w = rect.Width;
        int h = rect.Height;
        if (w <= 0 || h <= 0) throw new ToolException($"window rect has no area: {rect}");

        var print = TryGrab(hwnd, w, h, printWindow: true, screenOffset: null);
        if (print is not null) { method = "PrintWindow(PW_RENDERFULLCONTENT)"; return print; }

        var blt = TryGrab(hwnd, w, h, printWindow: false, screenOffset: (rect.Left, rect.Top));
        if (blt is not null) { method = "BitBlt(screen)"; return blt; }

        throw new ToolException(
            "capture failed: PrintWindow returned no pixels and BitBlt produced nothing. " +
            "If the session is locked, PrintWindow should still work; if the window is minimized, restore it first.");
    }

    public static CapturedImage GrabScreenRect(int left, int top, int w, int h)
    {
        if (w <= 0 || h <= 0) throw new ToolException($"screen rect has no area: {w}x{h}");
        var img = TryGrab(IntPtr.Zero, w, h, printWindow: false, screenOffset: (left, top));
        if (img is null) throw new ToolException("BitBlt(screen) produced no pixels (session locked?)");
        return img;
    }

    private static CapturedImage? TryGrab(IntPtr hwnd, int w, int h, bool printWindow, (int x, int y)? screenOffset)
    {
        IntPtr screenDc = Native.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return null;

        IntPtr memDc = IntPtr.Zero;
        IntPtr bmp = IntPtr.Zero;
        IntPtr old = IntPtr.Zero;
        try
        {
            memDc = Native.CreateCompatibleDC(screenDc);
            if (memDc == IntPtr.Zero) return null;
            bmp = Native.CreateCompatibleBitmap(screenDc, w, h);
            if (bmp == IntPtr.Zero) return null;
            old = Native.SelectObject(memDc, bmp);

            bool ok;
            if (printWindow)
            {
                ok = Native.PrintWindow(hwnd, memDc, 2);
                if (!ok) ok = Native.PrintWindow(hwnd, memDc, 0);
            }
            else
            {
                var off = screenOffset ?? (0, 0);
                ok = Native.BitBlt(memDc, 0, 0, w, h, screenDc, off.x, off.y, Native.SRCCOPY | Native.CAPTUREBLT);
            }
            if (!ok) return null;

            var bi = new Native.BITMAPINFOHEADER
            {
                biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h,          // negative = top-down rows
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,      // BI_RGB
            };
            var buffer = new byte[w * h * 4];
            int lines = Native.GetDIBits(memDc, bmp, 0, (uint)h, buffer, ref bi, 0);
            if (lines == 0) return null;

            // PrintWindow/BitBlt leave the alpha channel at 0; a PNG written like that
            // renders fully transparent. Force opaque.
            for (int i = 3; i < buffer.Length; i += 4) buffer[i] = 255;

            return new CapturedImage(w, h, buffer);
        }
        finally
        {
            if (old != IntPtr.Zero) Native.SelectObject(memDc, old);
            if (bmp != IntPtr.Zero) Native.DeleteObject(bmp);
            if (memDc != IntPtr.Zero) Native.DeleteDC(memDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    public static CapturedImage Crop(CapturedImage src, int x, int y, int w, int h)
    {
        x = Math.Clamp(x, 0, src.Width - 1);
        y = Math.Clamp(y, 0, src.Height - 1);
        w = Math.Clamp(w, 1, src.Width - x);
        h = Math.Clamp(h, 1, src.Height - y);

        var dst = new byte[w * h * 4];
        for (int row = 0; row < h; row++)
        {
            int srcOffset = ((y + row) * src.Width + x) * 4;
            Buffer.BlockCopy(src.Bgra, srcOffset, dst, row * w * 4, w * 4);
        }
        return new CapturedImage(w, h, dst);
    }

    /// <summary>Nearest-neighbour upscale — keeps pixel edges sharp so button borders can be measured.</summary>
    public static CapturedImage Zoom(CapturedImage src, int factor)
    {
        if (factor <= 1) return src;
        int w = src.Width * factor;
        int h = src.Height * factor;
        var dst = new byte[w * h * 4];
        for (int row = 0; row < h; row++)
        {
            int srcRow = row / factor;
            for (int col = 0; col < w; col++)
            {
                int srcIdx = (srcRow * src.Width + col / factor) * 4;
                int dstIdx = (row * w + col) * 4;
                dst[dstIdx] = src.Bgra[srcIdx];
                dst[dstIdx + 1] = src.Bgra[srcIdx + 1];
                dst[dstIdx + 2] = src.Bgra[srcIdx + 2];
                dst[dstIdx + 3] = 255;
            }
        }
        return new CapturedImage(w, h, dst);
    }

    public static void SavePng(CapturedImage img, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var source = BitmapSource.Create(
            img.Width, img.Height, 96, 96,
            PixelFormats.Bgra32, null, img.Bgra, img.Width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        encoder.Save(fs);
    }
}
