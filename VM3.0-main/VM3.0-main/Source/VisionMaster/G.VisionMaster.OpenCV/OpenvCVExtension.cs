global using System.Windows.Media;
using G.Controls.Diagram.Presenter.NodeDatas.Base;
using G.VisionMaster.ResultPresenter.ResultPresenters;

namespace G.VisionMaster.OpenCV;

public static class MatExtension
{
    public static void DrawRectangle(this Mat image, Rect box, Color boxColor, int boxThickness = 1)
    {
        Cv2.Rectangle(image, box, boxColor.ToScalar(), boxThickness, LineTypes.Link8);
    }

    public static void DrawCircle(this Mat image, Point point, int radius, Color color, int thickness = 1)
    {
        Cv2.Circle(image, point, radius, color.ToScalar(), thickness, LineTypes.Link8);
    }

    public static int ToThickness(this Mat image)
    {
        if (image == null || image.IsDisposed || image.Empty())
            return 1;
        double s = Math.Sqrt(image.Height * image.Height + image.Width * image.Width);
        //int thickness = (image.Height * image.Width) / 100000;
        return (int)((s / 200) * VisionSettings.Instance.OutputThickness);
    }

    public static System.Windows.Rect ToWindowRect(this Rect2d rect)
    {
        return new System.Windows.Rect(rect.Left, rect.Top, rect.Width, rect.Height);
    }

    public static System.Windows.Rect ToWindowRect(this Rect rect)
    {
        return new System.Windows.Rect(rect.Left, rect.Top, rect.Width, rect.Height);
    }

    public static Rect ToCVRect(this System.Windows.Rect rect)
    {
        return new Rect((int)rect.Left, (int)rect.Top, (int)rect.Width, (int)rect.Height);
    }

    public static System.Windows.Rect ToWindowRect(this KeyPoint keyPoint)
    {
        return new System.Windows.Rect(keyPoint.Pt.X - keyPoint.Size / 2, keyPoint.Pt.Y - keyPoint.Size / 2, keyPoint.Size, keyPoint.Size);
    }

    public static System.Windows.Rect ToWindowRect(this IEnumerable<Point2f> points)
    {
        float minX = points.Min(x => x.X);
        float minY = points.Min(x => x.Y);
        float maxX = points.Max(x => x.X);
        float maxY = points.Max(x => x.Y);
        return new System.Windows.Rect(minX, minY, maxX - minX, maxY - minY);
    }

    public static System.Windows.Rect ToWindowRect(this IEnumerable<Point> points)
    {
        int minX = points.Min(x => x.X);
        int minY = points.Min(x => x.Y);
        int maxX = points.Max(x => x.X);
        int maxY = points.Max(x => x.Y);
        return new System.Windows.Rect(minX, minY, maxX - minX, maxY - minY);
    }

    public static string ToDetectSuccessMessage(this int count)
    {
        return $"识别目标数量:{count} 个";
    }

    //public static void DrawEllipse(this Mat image, Point point, int radius, Color color, int thickness = 1)
    //{
    //    Cv2.Ellipse(image, point, radius, color.ToScalar(), thickness, LineTypes.Link8);
    //}

    //public static ImageSource ToImageSource(this Mat mat)
    //{
    //    return System.Windows.Application.Current.Dispatcher.Invoke(() =>
    //      {
    //          return mat.Empty() ? null : mat?.ToWriteableBitmap();
    //      });
    //}
    public static IResultPresenter ToResultPresenter(this IEnumerable<KeyPoint> keyPoints)
    {
        return keyPoints.ToRectangleDataGridResultPresenter(x => x.ToWindowRect(), x => $"C[{x.ClassId.ToString()}],O[{x.Octave.ToString()}],R[{x.Response.ToString()}]");
    }
    public static IResultPresenter ToResultPresenter(this IEnumerable<Rect> rects, string name = "位置信息")
    {
        return rects.ToRectangleDataGridResultPresenter(x => x.ToWindowRect(), x => name);
    }

    public static IResultPresenter ToResultPresenter(this IEnumerable<Point2f> points, string name = "位置信息")
    {
        return points.ToWindowRect().ToEnumerable().ToRectangleDataGridResultPresenter(x => name);
    }
    public static IResultPresenter ToResultPresenter(this IEnumerable<Point> points, string name = "位置信息")
    {
        return points.ToWindowRect().ToEnumerable().ToRectangleDataGridResultPresenter(x => name);
    }
    public static IResultPresenter ToResultPresenter(this Rect rect, string name = "位置信息")
    {
        return rect.ToWindowRect().ToEnumerable().ToRectangleDataGridResultPresenter(x => name);
    }

    public static IResultPresenter ToResultPresenter(this IEnumerable<ConnectedComponents.Blob> blobs)
    {
        return blobs.ToRectangleDataGridResultPresenter(x => x.Rect.ToWindowRect(), x => "标记" + x.Label.ToString());
    }

    public static IResultPresenter ToResultPresenter(this IEnumerable<LineSegmentPoint> lineSegmentPoints, string name = "直线数据")
    {
        return lineSegmentPoints.ToLineDataGridResultPresenter(x => new VisionLine() { Start = x.P1.ToPoint(), End = x.P2.ToPoint() }, x => name);
    }

    public static ImageSource ToImageSource(this Mat mat)
    {
        if (!mat.IsValid())
            return null;
        return System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            return mat.ToWriteableBitmap();
        });
        //return mat.ToBitmapSource();
    }

    public static bool IsValid(this Mat mat)
    {
        return mat != null && !mat.IsDisposed && !mat.Empty() ;
    }

    public static Tuple<Scalar, Scalar> GetHSVRange(this Color color, int hRange = 30, int sRange = 20, int vRange = 20)
    {
        GetHSVRange(color, hRange, sRange, vRange, out HSVRange lower, out HSVRange upper);
        Scalar lowerScalar = new Scalar(lower.HueMin / 2, lower.SaturationMin * 2.55, lower.ValueMin * 2.55);
        Scalar upperScalar = new Scalar(upper.HueMax / 2, upper.SaturationMax * 2.55, upper.ValueMax * 2.55);
        return Tuple.Create(lowerScalar, upperScalar);
    }

    private static void GetHSVRange(Color rgbColor, int hRange, int sRange, int vRange,
                                  out HSVRange lower, out HSVRange upper)
    {
        // 将RGB转换为HSV
        double r = rgbColor.R / 255.0;
        double g = rgbColor.G / 255.0;
        double b = rgbColor.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));

        double h = 0;
        double s = 0;
        double v = max;

        double delta = max - min;

        if (max != 0)
        {
            s = delta / max;
        }

        if (delta != 0)
        {
            if (max == r)
            {
                h = (g - b) / delta;
            }
            else
            {
                h = max == g ? 2 + (b - r) / delta : 4 + (r - g) / delta;
            }

            h *= 60;
            if (h < 0) h += 360;
        }

        // 计算范围
        lower = new HSVRange
        {
            HueMin = Math.Max(0, h - hRange),
            HueMax = Math.Min(360, h + hRange),
            SaturationMin = Math.Max(0, s * 100 - sRange),
            SaturationMax = Math.Min(100, s * 100 + sRange),
            ValueMin = Math.Max(0, v * 100 - vRange),
            ValueMax = Math.Min(100, v * 100 + vRange)
        };

        upper = new HSVRange
        {
            HueMin = Math.Max(0, h - hRange / 2),
            HueMax = Math.Min(360, h + hRange / 2),
            SaturationMin = Math.Max(0, s * 100 - sRange / 2),
            SaturationMax = Math.Min(100, s * 100 + sRange / 2),
            ValueMin = Math.Max(0, v * 100 - vRange / 2),
            ValueMax = Math.Min(100, v * 100 + vRange / 2)
        };
    }
}
public struct HSVRange
{
    public double HueMin { get; set; }
    public double HueMax { get; set; }
    public double SaturationMin { get; set; }
    public double SaturationMax { get; set; }
    public double ValueMin { get; set; }
    public double ValueMax { get; set; }
}

public static class ColorProvider
{
    public static IEnumerable<Color> GetColors()
    {
        yield return Colors.Red;
        yield return Colors.Blue;
        yield return Colors.Gray;
        yield return Colors.Orange;
        yield return Colors.DeepPink;
        yield return Colors.Green;
        yield return Colors.Purple;
        yield return Colors.Yellow;
        yield return Colors.Brown;
        yield return Colors.SkyBlue;
    }

    public static Color GetRandomColor()
    {
        List<Color> colors = GetColors().ToList();
        Random random = new Random();
        int index = random.Next(colors.Count);
        return colors[index];
    }
}
public static class OpenvCVExtension
{
    public static Size ToCVSize(this System.Windows.Size size)
    {
        return new Size(size.Width, size.Height);
    }

    public static System.Windows.Size ToSize(this Size size)
    {
        return new System.Windows.Size(size.Width, size.Height);
    }
    public static Point ToCVPoint(this System.Windows.Point size)
    {
        return new Point((int)size.X, (int)size.Y);
    }

    public static System.Windows.Point ToPoint(this Point size)
    {
        return new System.Windows.Point(size.X, size.Y);
    }

    public static Rect ToCVRect(this System.Windows.Int32Rect size)
    {
        return new Rect(size.X, size.Y, size.Width, size.Height);
    }

    public static System.Windows.Int32Rect ToRect(this Rect size)
    {
        return new System.Windows.Int32Rect(size.Left, size.Top, size.Width, size.Height);
    }

    public static Rect ToCVRect(this Rect2f size)
    {
        return new Rect((int)size.Left, (int)size.Top, (int)size.Width, (int)size.Height);
    }

    public static Scalar ToScalar(this Color color)
    {
        return Scalar.FromRgb(color.R, color.G, color.B);
    }

    public static Color ToColor(this Scalar color)
    {
        return Color.FromRgb((byte)color.Val2, (byte)color.Val1, (byte)color.Val0);
    }

}

public static class AssertPathExtesion
{
    public static string ToDataPath(this string dataPath)
    {
        return string.IsNullOrEmpty(dataPath) ? null : dataPath;
    }

    public static string ToAssetsPath(this string dataPath)
    {
        return string.IsNullOrEmpty(dataPath) ? null : System.IO.Path.Combine("Assets", dataPath);
    }

    public static string ToOnnxPath(this string dataPath)
    {
        //return string.IsNullOrEmpty(dataPath)
        //    ? null
        //    : System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Onnx", dataPath);
        return string.IsNullOrEmpty(dataPath)
         ? null
         : System.IO.Path.Combine("Assets", "Onnx", dataPath);
    }
}

