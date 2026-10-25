using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;
using Path = System.Windows.Shapes.Path;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [B3] 批次 C 的图形与动图扩展：多边形 / 贝塞尔 / 动态画线 / GIF。
        ///
        /// 几何类断言都从"控件上的只读几何属性"取，而不是从模板里的 Path 取：
        /// 前者是唯一真相（模板只是消费者），后者在没套模板时会给出假红。
        /// 另有一条专门盯 <c>TemplateBinding</c>——只读 DP 绑不到模板是静默的（画面一片空白）。
        /// </summary>
        private static class BatchCVisualChecks
        {
            public static void Run() => RunOnSta(RunChecks);

            private static void RunChecks()
            {
                Section("批次 C：多边形 / 贝塞尔 / 动态画线 / GIF 动图");

                // ---------------- 多边形 ----------------

                var polygonModel = ElementRegistry.CreateElement("Hmi.Polygon")!;
                polygonModel.Width = 100;
                polygonModel.Height = 100;
                polygonModel.SetProperty("Points", "0.5,0;1,1;0,1");
                var polygon = (PolygonElement)ElementRegistry.CreateControl(polygonModel)!;
                polygon.Style = ThemeStyle(typeof(PolygonElement));
                Layout(polygon, 100, 100);

                var polygonFigure = FirstFigure(polygon.PolygonGeometry);
                Check("[B3-SH1] 多边形：三个顶点 → 闭合几何，起点 (50,0)",
                    polygonFigure != null
                    && polygonFigure.IsClosed
                    && Math.Abs(polygonFigure.StartPoint.X - 50d) < 1e-9
                    && Math.Abs(polygonFigure.StartPoint.Y) < 1e-9,
                    polygonFigure == null
                        ? "geometry=null"
                        : $"closed={polygonFigure.IsClosed} / start={polygonFigure.StartPoint}");

                var polygonPath = polygon.Template?.FindName("PolygonPath", polygon) as Path;
                Check("[B3-SH2] 多边形模板绑到了只读几何（TemplateBinding 生效，不是空白 Path）",
                    polygonPath?.Data != null,
                    polygonPath == null ? "模板里找不到 PolygonPath" : "Data=" + (polygonPath.Data == null ? "null" : "ok"));

                polygonModel.SetProperty("Points", "0,0;1,1");
                polygon.Refresh();
                Check("[B3-SH3] 多边形：只有两个点 → 不画（几何为 null）",
                    polygon.PolygonGeometry == null,
                    polygon.PolygonGeometry == null ? "" : "geometry=" + polygon.PolygonGeometry);

                polygonModel.SetProperty("Points", "abc;def");
                polygon.Refresh();
                Check("[B3-SH4] 多边形：坏点串 → 不画、不抛",
                    polygon.PolygonGeometry == null,
                    polygon.PolygonGeometry == null ? "" : "geometry=" + polygon.PolygonGeometry);

                // ---------------- 贝塞尔 ----------------

                var bezierModel = ElementRegistry.CreateElement("Hmi.Bezier")!;
                bezierModel.Width = 100;
                bezierModel.Height = 100;
                var bezier = (BezierElement)ElementRegistry.CreateControl(bezierModel)!;
                bezier.Style = ThemeStyle(typeof(BezierElement));
                Layout(bezier, 100, 100);

                var bezierFigure = FirstFigure(bezier.BezierGeometry);
                var bezierSegment = bezierFigure?.Segments.Count == 1
                    ? bezierFigure.Segments[0] as BezierSegment
                    : null;
                Check("[B3-BZ1] 贝塞尔：默认四个控制点 → 一段三次贝塞尔，起点 (0,100)、终点 (100,100)",
                    bezierSegment != null
                    && Math.Abs(bezierFigure!.StartPoint.X) < 1e-9
                    && Math.Abs(bezierFigure.StartPoint.Y - 100d) < 1e-9
                    && Math.Abs(bezierSegment.Point3.X - 100d) < 1e-9
                    && Math.Abs(bezierSegment.Point3.Y - 100d) < 1e-9,
                    bezierSegment == null
                        ? "segment=null"
                        : $"start={bezierFigure!.StartPoint} / end={bezierSegment.Point3}");

                bezierModel.SetProperty("Points", "0,0;0.5,0;1,1");
                bezier.Refresh();
                Check("[B3-BZ2] 贝塞尔：三个点 → 不画（必须四个控制点）",
                    bezier.BezierGeometry == null,
                    bezier.BezierGeometry == null ? "" : "geometry=" + bezier.BezierGeometry);

                // ---------------- 动态画线 ----------------

                var lineModel = ElementRegistry.CreateElement("Hmi.DynamicLine")!;
                lineModel.Width = 100;
                lineModel.Height = 100;
                lineModel.SetProperty("Length", "1");
                lineModel.SetProperty("Angle", "0");
                var dynamicLine = (DynamicLineElement)ElementRegistry.CreateControl(lineModel)!;
                dynamicLine.Style = ThemeStyle(typeof(DynamicLineElement));
                Layout(dynamicLine, 100, 100);

                var lineFigure = FirstFigure(dynamicLine.DynamicGeometry);
                Check("[B3-DL1] 动态画线：角度 0 / 长度 1 → 水平贯穿 (0,50)~(100,50)",
                    lineFigure != null
                    && Math.Abs(lineFigure.StartPoint.X) < 1e-9
                    && Math.Abs(lineFigure.StartPoint.Y - 50d) < 1e-9
                    && EndPoint(lineFigure) is { } end0
                    && Math.Abs(end0.X - 100d) < 1e-9
                    && Math.Abs(end0.Y - 50d) < 1e-9,
                    lineFigure == null ? "geometry=null" : $"start={lineFigure.StartPoint} / end={EndPoint(lineFigure)}");

                lineModel.SetProperty("Angle", "90");
                dynamicLine.Refresh();
                lineFigure = FirstFigure(dynamicLine.DynamicGeometry);
                Check("[B3-DL2] 动态画线：角度 90 → 竖直线 (50,0)~(50,100)（顺时针，Y 向下）",
                    lineFigure != null
                    && Math.Abs(lineFigure.StartPoint.X - 50d) < 1e-9
                    && Math.Abs(lineFigure.StartPoint.Y) < 1e-9
                    && EndPoint(lineFigure) is { } end90
                    && Math.Abs(end90.X - 50d) < 1e-9
                    && Math.Abs(end90.Y - 100d) < 1e-9,
                    lineFigure == null ? "geometry=null" : $"start={lineFigure.StartPoint} / end={EndPoint(lineFigure)}");

                lineModel.SetProperty("Segments", "4");
                dynamicLine.Refresh();
                lineFigure = FirstFigure(dynamicLine.DynamicGeometry);
                var zigzag = lineFigure?.Segments.Count == 1
                    ? lineFigure.Segments[0] as PolyLineSegment
                    : null;
                Check("[B3-DL3] 动态画线：段数 4 → 折线 4 段（锯齿）",
                    zigzag?.Points.Count == 4,
                    zigzag == null ? "segment=null" : $"points={zigzag.Points.Count}");

                // ---------------- GIF 动图 ----------------

                var gifBytes = BuildGif(3);
                var gifModel = ElementRegistry.CreateElement("Hmi.Gif")!;
                gifModel.SetProperty("SourcePath", "Image\\anim.gif");
                var gif = (GifElement)ElementRegistry.CreateControl(gifModel)!;
                gif.Style = ThemeStyle(typeof(GifElement));
                gif.ImageBytesResolver = _ => gifBytes;
                Layout(gif, 100, 100);

                Check("[B3-GF1] GIF：3 帧全部解出，首帧已落到 Image 上",
                    gif.FrameCount == 3 && gif.FrameIndex == 0 && gif.Template?.FindName("ImageHost", gif) is Image { Source: not null },
                    $"frames={gif.FrameCount} / index={gif.FrameIndex} / host={(gif.Template?.FindName("ImageHost", gif) as Image)?.Source}");

                bool step1 = gif.AdvanceFrame();
                bool step2 = gif.AdvanceFrame();
                bool step3 = gif.AdvanceFrame();
                Check("[B3-GF2] GIF：循环模式下逐帧推进 0→1→2→0",
                    step1 && step2 && step3 && gif.FrameIndex == 0,
                    $"step={step1}/{step2}/{step3} / index={gif.FrameIndex}");

                gif.Loop = false;
                gif.AdvanceFrame();
                gif.AdvanceFrame();
                bool atLast = gif.FrameIndex == 2;
                bool stopAtLast = !gif.AdvanceFrame() && gif.FrameIndex == 2;
                Check("[B3-GF3] GIF：关掉循环 → 停在最后一帧，再推进返回 false",
                    atLast && stopAtLast,
                    $"atLast={atLast} / index={gif.FrameIndex}");

                var emptyGif = (GifElement)ElementRegistry.CreateControl(
                    ElementRegistry.CreateElement("Hmi.Gif")!)!;
                emptyGif.Style = ThemeStyle(typeof(GifElement));
                Layout(emptyGif, 100, 100);
                var placeholder = emptyGif.Template?.FindName("Placeholder", emptyGif) as FrameworkElement;
                var emptyHost = emptyGif.Template?.FindName("ImageHost", emptyGif) as Image;
                Check("[B3-GF4] GIF：没配图 → 帧数 0，显示占位、图像本体收起",
                    emptyGif.FrameCount == 0
                    && placeholder?.Visibility == Visibility.Visible
                    && emptyHost?.Visibility == Visibility.Collapsed,
                    $"frames={emptyGif.FrameCount} / placeholder={placeholder?.Visibility} / host={emptyHost?.Visibility}");

                var brokenGif = (GifElement)ElementRegistry.CreateControl(
                    ElementRegistry.CreateElement("Hmi.Gif")!)!;
                brokenGif.Style = ThemeStyle(typeof(GifElement));
                brokenGif.ImageBytesResolver = _ => new byte[] { 1, 2, 3, 4 };
                Layout(brokenGif, 100, 100);
                Check("[B3-GF5] GIF：字节不是合法 GIF → 帧数 0、不抛异常",
                    brokenGif.FrameCount == 0,
                    $"frames={brokenGif.FrameCount}");

                // 画布在拖动/改属性时会反复调 Refresh()；若每次都重新解码，动画会跳回第一帧，
                // 用户看到的是"一动就闪一下"。判据取"帧序不倒退"——这是用户可见的那一面。
                gif.Loop = true;
                gif.AdvanceFrame();
                if (gif.FrameIndex == 0)
                    gif.AdvanceFrame(); // 先离开首帧，否则"被重置回首帧"会假绿

                int beforeRefresh = gif.FrameIndex;
                gif.Refresh();
                Check("[B3-GF6] GIF：Refresh 不重启动画（帧序保持，拖动图元不会闪回首帧）",
                    beforeRefresh == 1 && gif.FrameIndex == beforeRefresh,
                    $"before={beforeRefresh} / after={gif.FrameIndex}");
            }

            private static PathFigure? FirstFigure(Geometry? geometry)
                => geometry is PathGeometry path && path.Figures.Count == 1 ? path.Figures[0] : null;

            private static Point? EndPoint(PathFigure figure)
            {
                if (figure.Segments.Count != 1)
                    return null;

                return figure.Segments[0] switch
                {
                    LineSegment line => line.Point,
                    PolyLineSegment poly when poly.Points.Count > 0 => poly.Points[^1],
                    _ => null,
                };
            }

            /// <summary>造一段 N 帧、每帧纯色的 GIF（不依赖任何外部资产文件）</summary>
            private static byte[] BuildGif(int frameCount)
            {
                var encoder = new GifBitmapEncoder();
                for (int i = 0; i < frameCount; i++)
                {
                    byte b = (byte)(40 + i * 60);
                    var pixels = new byte[4 * 4 * 4];
                    for (int p = 0; p < pixels.Length; p += 4)
                    {
                        pixels[p] = b;
                        pixels[p + 1] = b;
                        pixels[p + 2] = b;
                        pixels[p + 3] = 255;
                    }

                    var frame = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, pixels, 4 * 4);
                    frame.Freeze();
                    encoder.Frames.Add(BitmapFrame.Create(frame));
                }

                using var stream = new MemoryStream();
                encoder.Save(stream);
                return stream.ToArray();
            }

            private static Style ThemeStyle(Type controlType)
            {
                var theme = new ResourceDictionary
                {
                    Source = new Uri("/VM.Scada.Controls;component/Themes/Generic.xaml", UriKind.Relative),
                };
                return (Style)theme[controlType];
            }

            private static void Layout(Control control, double width, double height)
            {
                control.ApplyTemplate();
                control.Measure(new Size(width, height));
                control.Arrange(new Rect(0, 0, width, height));
                control.UpdateLayout();
            }

            private static void RunOnSta(Action action)
            {
                Exception? error = null;
                var thread = new Thread(() =>
                {
                    try { action(); }
                    catch (Exception ex) { error = ex; }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();

                if (error != null)
                    Check("批次 C UI 段执行完成（没有未捕获异常）", false, error.ToString());
            }
        }
    }
}
