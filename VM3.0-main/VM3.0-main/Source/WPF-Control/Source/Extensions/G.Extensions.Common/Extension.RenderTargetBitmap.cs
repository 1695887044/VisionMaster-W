using System.Windows.Media;
using System.Windows;
using System.Windows.Media.Imaging;

namespace G.Extensions.Common;

public static class RenderTargetBitmapExtensions
{
    public static BitmapSource GetImage(this UIElement element)
    {
        RenderTargetBitmap targetBitmap = new RenderTargetBitmap(
                                     (int)element.RenderSize.Width,
                                     (int)element.RenderSize.Height,
                                     96d,
                                     96d,
                                     PixelFormats.Default);
        targetBitmap.Render(element);
        return targetBitmap;
    }

    public static void SaveAsPng(this RenderTargetBitmap renderTargetBitmap, string filePath)
    {
        PngBitmapEncoder encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(renderTargetBitmap));
        using (var stream = new System.IO.FileStream(filePath, System.IO.FileMode.Create))
        {
            encoder.Save(stream);
        }
    }

    public static void SaveAsJpeg(this RenderTargetBitmap renderTargetBitmap, string filePath)
    {
        JpegBitmapEncoder encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(renderTargetBitmap));
        using (var stream = new System.IO.FileStream(filePath, System.IO.FileMode.Create))
        {
            encoder.Save(stream);
        }
    }

    public static void SaveAsBmp(this RenderTargetBitmap renderTargetBitmap, string filePath)
    {
        BmpBitmapEncoder encoder = new BmpBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(renderTargetBitmap));
        using (var stream = new System.IO.FileStream(filePath, System.IO.FileMode.Create))
        {
            encoder.Save(stream);
        }
    }
}
