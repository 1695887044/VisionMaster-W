using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using VisionMaster.Scada;
using VisionMaster.Scada.Controls;

namespace ScadaChecks
{
    internal partial class Program
    {
        /// <summary>
        /// [B2] 批次 B 的可视化扩展：ProgressBar 刻度/上下限、Gauge Ring/Plus、对角线/比例移动动画。
        /// </summary>
        private static class BatchBVisualChecks
        {
            public static void Run() => RunOnSta(RunChecks);

            private static void RunChecks()
            {
                Section("批次 B：棒图增强 / 环形仪表 / 对角线与比例移动");

                var bar = (ProgressBarElement)ElementRegistry.CreateControl(
                    ElementRegistry.CreateElement("Hmi.ProgressBar")!)!;
                bar.Style = BarStyle();
                bar.Value = 10;
                bar.Minimum = 0;
                bar.Maximum = 100;
                bar.ShowLimits = true;
                bar.LowLimit = 20;
                bar.HighLimit = 80;
                bar.ShowTicks = true;
                bar.TickCount = 5;
                Layout(bar, 300, 40);

                Check("[B2-PB1] 棒图低于下限：填充色切到 LowColor",
                    ReferenceEquals(bar.DisplayFill, bar.LowColor),
                    $"fill={bar.DisplayFill} / low={bar.LowColor}");

                bar.Value = 90;
                Check("[B2-PB2] 棒图高于上限：填充色切到 HighColor",
                    ReferenceEquals(bar.DisplayFill, bar.HighColor),
                    $"fill={bar.DisplayFill} / high={bar.HighColor}");

                bar.Value = 50;
                Check("[B2-PB3] 棒图在上下限内：回到 Fill",
                    ReferenceEquals(bar.DisplayFill, bar.Fill),
                    $"fill={bar.DisplayFill} / fillProp={bar.Fill}");

                var tickLayer = bar.Template?.FindName(ProgressBarElement.PartTickLayer, bar) as Canvas;
                var limitLayer = bar.Template?.FindName(ProgressBarElement.PartLimitLayer, bar) as Canvas;
                Check("[B2-PB4] 5 段刻度 = 6 根线；两条限制线都画出来",
                    tickLayer != null && tickLayer.Children.Count == 6
                    && limitLayer != null && limitLayer.Children.Count == 2,
                    $"ticks={tickLayer?.Children.Count} / limits={limitLayer?.Children.Count}");

                var gaugeElement = ElementRegistry.CreateElement("Hmi.Gauge")!;
                gaugeElement.SetProperty("Style", "Ring");
                gaugeElement.SetProperty("Value", "50");
                var gauge = (GaugeElement)ElementRegistry.CreateControl(gaugeElement)!;
                gauge.Style = GaugeStyle();
                gauge.Refresh();
                Layout(gauge, 220, 220);

                var needle = gauge.Template?.FindName(GaugeElement.PartNeedle, gauge) as Path;
                var progress = gauge.Template?.FindName(GaugeElement.PartProgress, gauge) as Path;
                Check("[B2-GA1] Ring：环形进度弧有几何，指针收起、进度弧显示",
                    gauge.ProgressGeometry != null
                    && needle != null && needle.Visibility == Visibility.Collapsed
                    && progress != null && progress.Visibility == Visibility.Visible,
                    $"progress={(gauge.ProgressGeometry == null ? "null" : "ok")} / needle={needle?.Visibility} / arc={progress?.Visibility}");

                gaugeElement.SetProperty("Style", "Classic");
                gauge.Refresh();
                Check("[B2-GA2] Classic：进度弧收起、指针恢复",
                    gauge.ProgressGeometry == null
                    && needle != null && needle.Visibility == Visibility.Visible
                    && progress != null && progress.Visibility == Visibility.Collapsed,
                    $"progress={(gauge.ProgressGeometry == null ? "null" : "ok")} / needle={needle?.Visibility}");

                var moving = new ScadaAnimation
                {
                    Type = ScadaAnimationType.DiagonalMove,
                    RangeLow = "0",
                    RangeHigh = "100",
                    EndX = 200,
                    EndY = 100,
                };
                var textElement = ElementRegistry.CreateElement("Hmi.Text")!;
                textElement.X = 10;
                textElement.Y = 20;
                textElement.Width = 100;
                textElement.Height = 50;
                var text = (TextElement)ElementRegistry.CreateControl(textElement)!;
                text.Style = TextStyle();
                text.Refresh();
                Layout(text, 100, 50);

                bool diagonalOk = text.TryApplyAnimationValue(moving, 50d, out var diagonalError);
                Check("[B2-AN1] 对角线移动：值 50 / 范围 0~100 → X=105、Y=60",
                    diagonalOk
                    && Math.Abs(Canvas.GetLeft(text) - 105d) < 1e-9
                    && Math.Abs(Canvas.GetTop(text) - 60d) < 1e-9,
                    $"ok={diagonalOk} / x={Canvas.GetLeft(text)} / y={Canvas.GetTop(text)} / err={diagonalError}");

                var scaling = new ScadaAnimation
                {
                    Type = ScadaAnimationType.ProportionalMove,
                    RangeLow = "0",
                    RangeHigh = "100",
                    EndX = 200,
                    EndY = 100,
                };
                bool scaleOk = text.TryApplyAnimationValue(scaling, 50d, out var scaleError);
                Check("[B2-AN2] 比例移动：100×50 → 150×75，中心保持（左上角左移 25、上移 12.5）",
                    scaleOk
                    && Math.Abs(text.Width - 150d) < 1e-9
                    && Math.Abs(text.Height - 75d) < 1e-9
                    && Math.Abs(Canvas.GetLeft(text) - (-15d)) < 1e-9
                    && Math.Abs(Canvas.GetTop(text) - 7.5d) < 1e-9,
                    $"ok={scaleOk} / w={text.Width} / h={text.Height} / x={Canvas.GetLeft(text)} / y={Canvas.GetTop(text)} / err={scaleError}");
            }

            private static Style BarStyle()
            {
                var theme = new ResourceDictionary
                {
                    Source = new Uri("/VM.Scada.Controls;component/Themes/Generic.xaml", UriKind.Relative),
                };
                return (Style)theme[typeof(ProgressBarElement)];
            }

            private static Style GaugeStyle()
            {
                var theme = new ResourceDictionary
                {
                    Source = new Uri("/VM.Scada.Controls;component/Themes/Generic.xaml", UriKind.Relative),
                };
                return (Style)theme[typeof(GaugeElement)];
            }

            private static Style TextStyle()
            {
                var theme = new ResourceDictionary
                {
                    Source = new Uri("/VM.Scada.Controls;component/Themes/Generic.xaml", UriKind.Relative),
                };
                return (Style)theme[typeof(TextElement)];
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
                    Check("批次 B UI 段执行完成（没有未捕获异常）", false, error.ToString());
            }
        }
    }
}
