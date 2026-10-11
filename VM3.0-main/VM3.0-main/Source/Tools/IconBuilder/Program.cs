using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace IconBuilder;

class Program
{
    static void Main(string[] args)
    {
        var root = @"d:\GVision\Document";
        var pngPath = Path.Combine(root, "logo.png");
        var icoPath = Path.Combine(root, "logo.ico");
        if (!File.Exists(pngPath))
        {
            Console.Error.WriteLine("logo.png not found: " + pngPath);
            Environment.Exit(1);
        }
        var sizes = new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var images = new MemoryStream[sizes.Length];
        try
        {
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(pngPath, UriKind.Absolute);
                bi.DecodePixelWidth = s;
                bi.DecodePixelHeight = s;
                bi.EndInit();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bi));
                var ms = new MemoryStream();
                encoder.Save(ms);
                ms.Position = 0;
                images[i] = ms;
            }
            using var fs = new FileStream(icoPath, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs);
            bw.Write((short)0);
            bw.Write((short)1);
            bw.Write((short)images.Length);
            int offset = 6 + images.Length * 16;
            for (int i = 0; i < images.Length; i++)
            {
                var ms = images[i];
                int s = sizes[i];
                bw.Write((byte)(s == 256 ? 0 : s));
                bw.Write((byte)(s == 256 ? 0 : s));
                bw.Write((byte)0);
                bw.Write((byte)0);
                bw.Write((short)1);
                bw.Write((short)32);
                bw.Write((int)ms.Length);
                bw.Write((int)offset);
                offset += (int)ms.Length;
            }
            for (int i = 0; i < images.Length; i++)
            {
                images[i].CopyTo(fs);
            }
        }
        finally
        {
            foreach (var ms in images)
                ms?.Dispose();
        }
        Console.WriteLine("Wrote multi-size icon: " + icoPath);
    }
}
