using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Core.Halcon.Controls;
using Core.Halcon.Models;
using HalconDotNet;

namespace ZoomProbe
{
    /// <summary>
    /// 临时诊断探针（用完即删）：复现"流程跑完后滚轮缩放 → 图像消失、只剩标注文字"。
    ///
    /// 只做观测，不下结论：每一步都打印 HALCON 窗口的实际 part + 窗口内容的像素统计，
    /// 并在缩放后再强制 RenderAll 一次作为对照——用来区分
    ///   ① part 被算飞（图在画面外）   ② 显示栈重放丢图（重画就能回来）   ③ 图像对象已失效（重画也黑）
    /// </summary>
    internal static class Program
    {
        private const string ImagePath =
            @"D:\C#\VM\解决方案\视觉样例对拍\Image\barcode_code128\code12801.png";

        [STAThread]
        private static void Main()
        {
            var sta = new Thread(Run);
            sta.SetApartmentState(ApartmentState.STA);
            sta.Start();
            sta.Join();
            Console.WriteLine("DONE");
        }

        private static void Run()
        {
            var win = new Window
            {
                Width = 900,
                Height = 700,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -4000,
                Top = 0,
            };
            win.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/Core.Halcon;component/Generic.xaml", UriKind.Relative),
            });

            var display = new ImageDisplay { Width = 880, Height = 660 };
            win.Content = display;
            win.Show();
            Pump(500);

            var hswc = FindChild<HSmartWindowControlWPF>(display);
            if (hswc?.HalconWindow == null)
            {
                Console.WriteLine("FATAL: HALCON window not ready");
                return;
            }
            var hw = hswc.HalconWindow;
            Console.WriteLine($"window ok  control={hswc.ActualWidth:F0}x{hswc.ActualHeight:F0}");

            var img = new HImage();
            img.ReadImage(ImagePath);
            img.GetImageSize(out int w, out int h);
            Console.WriteLine($"image {w}x{h} ch={img.CountChannels().I}");

            display.HImage = img;
            Pump(400);
            Report("after set image (fit)", hw);

            // 复刻 ResultDisplay 发布的覆盖层：一条绿色码框 + 一行绿色文本
            display.Annotations = new List<MeasureAnnotation>
            {
                new MeasureAnnotation
                {
                    Type = MeasureType.Polyline,
                    Points = new[] { 120.0, 200.0, 120.0, 1200.0, 700.0, 1200.0, 700.0, 200.0 },
                    Color = "green",
                    LineWidth = 2,
                },
                new MeasureAnnotation
                {
                    Type = MeasureType.Text,
                    Points = new[] { 60.0, 120.0 },
                    Text = "A: Text, B: 000, C: 347862",
                    Color = "green",
                },
            };
            Pump(400);
            Report("after annotations", hw);

            // ── 滚轮放大：每次 +120，与 HSmartWindowControlWPF 的 HWPFWindow_MouseWheel 同一条路径 ──
            double cx = hswc.ActualWidth / 2, cy = hswc.ActualHeight / 2;
            for (int i = 1; i <= 6; i++)
            {
                hswc.HZoomWindowContents(cx, cy, 120);
                Pump(150);
                Report($"wheel in x{i}", hw);
            }

            Console.WriteLine("--- 对照：强制 RenderAll（重画一次图像 + 标注）---");
            ForceRender(display);
            Pump(300);
            Report("after forced RenderAll", hw);

            for (int i = 1; i <= 12; i++)
            {
                hswc.HZoomWindowContents(cx, cy, -120);
                Pump(120);
                Report($"wheel out x{i}", hw);
            }

            Console.WriteLine("--- 对照：再次强制 RenderAll ---");
            ForceRender(display);
            Pump(300);
            Report("after forced RenderAll (zoomed out)", hw);

            // 换一张同尺寸新图（复刻循环运行时每轮换帧）
            var img2 = new HImage();
            img2.ReadImage(ImagePath);
            display.HImage = img2;
            Pump(400);
            Report("after same-size new image", hw);
            hswc.HZoomWindowContents(cx, cy, 120);
            Pump(200);
            Report("new image + wheel in x1", hw);
            ForceRender(display);
            Pump(300);
            Report("new image + forced RenderAll", hw);

            // ── 关键对照：把"仍在显示中"的图 Dispose 掉（复刻 ImageCollectionService 覆盖旧帧）──
            Console.WriteLine("=== 场景 B：显示中的图像被宿主 Dispose（旧帧被新帧覆盖） ===");
            display.HImage = null;
            Pump(200);
            img2.Dispose();
            var img3 = new HImage();
            img3.ReadImage(ImagePath);
            display.HImage = img3;
            Pump(300);
            Report("B0 重新绑定活图", hw);
            // 模拟：Annotations 先更新（绑定顺序在 XAML 里排在 HImage 前面）→ 此时 HImage 还是旧的
            img3.Dispose();                 // 旧帧被 Dispose，但 DP 还指着它
            display.Annotations = new List<MeasureAnnotation>
            {
                new MeasureAnnotation
                {
                    Type = MeasureType.Text,
                    Points = new[] { 60.0, 120.0 },
                    Text = "A: Text, B: 000, C: 347862 (2)",
                    Color = "green",
                },
            };
            Pump(300);
            Report("B1 旧图已释放 + Annotations 变更后重画", hw);
            Console.WriteLine("flush_graphic=" + FlushState());
            Console.WriteLine(
                "state: HImage==null? " + (display.HImage == null)
                + "  IsInitialized=" + (display.HImage != null && display.HImage.IsInitialized())
                + "  annotations=" + display.Annotations.Count
                + "  hw==null? " + (hw == null));
            try
            {
                hw.SetColor("green");
                hw.SetTposition(80, 140);
                hw.WriteString("HELLO-TEXT");
                hw.SetColor("black");
            }
            catch (Exception ex) { Console.WriteLine("manual text threw: " + ex.Message); }
            Report("B1c 手写一行文字后", hw);
            // 手画一条红线：能出线 = 绘制通路正常（说明 RenderAll 中途抛了）；出不来 = 刷新被卡住
            try
            {
                hw.SetColor("red");
                hw.DispLine(10.0, 10.0, 400.0, 700.0);
                hw.SetColor("black");
            }
            catch (Exception ex) { Console.WriteLine("manual draw threw: " + ex.Message); }
            Report("B1b 手画一条红线后", hw);
            Console.WriteLine("flush_graphic=" + FlushState());
            hswc.HZoomWindowContents(cx, cy, 120);
            Pump(200);
            Report("B2 旧图已释放 + 滚轮放大", hw);
            ForceRender(display);
            Pump(300);
            Report("B3 旧图已释放 + 强制重画", hw);
            var img4 = new HImage();
            img4.ReadImage(ImagePath);
            display.HImage = img4;
            Pump(300);
            Report("B4 绑定新活图", hw);
            hswc.HZoomWindowContents(cx, cy, 120);
            Pump(200);
            Report("B5 新活图 + 滚轮放大", hw);

            // ── 场景 C（关键）：图还在显示中就被宿主 Dispose，但 DP 没换、也没有重画 ──
            // 复刻 ImageCollectionService.InsertOrReplace：_frames[i]=新帧 → existing.Dispose()，
            // 而 ImageFrame.Image 是普通 CLR 属性（无 INotify），绑定不会跟着变。
            Console.WriteLine("=== 场景 C：显示中的图被 Dispose（DP 不变、不重画）→ 再滚轮 ===");
            var img5 = new HImage();
            img5.ReadImage(ImagePath);
            display.HImage = img5;
            Pump(300);
            Report("C0 绑定活图", hw);
            img5.Dispose();
            Report("C1 直接 Dispose（未重画，缓冲里应仍是图）", hw);
            hswc.HZoomWindowContents(cx, cy, 120);
            Pump(200);
            Report("C2 Dispose 后滚轮放大", hw);
            hswc.HZoomWindowContents(cx, cy, 120);
            Pump(200);
            Report("C3 再放大一次", hw);
            ForceRender(display);
            Pump(300);
            Report("C4 再强制重画", hw);

            // ── 场景 D：滚轮缩放下限（2026-10-27 修复验证）──
            Console.WriteLine("=== 场景 D：缩放下限（铺满即止 / 缩过头弹回 / 放大仍放行）===");
            var img6 = new HImage();
            img6.ReadImage(ImagePath);
            display.HImage = img6;
            Pump(300);
            hswc.SetFullImagePart();
            Pump(200);
            Report("D0 铺满基线", hw);
            double fitSpan = PartSpan(hw);

            bool h1 = RaiseWheel(hswc, -120);
            Pump(150);
            Report("D1 铺满时滚轮外缩（应被拦，part 不变）", hw);
            Console.WriteLine($"    Handled={h1}  span={PartSpan(hw):F0}（基线 {fitSpan:F0}）");

            bool h2 = RaiseWheel(hswc, 120);
            Pump(150);
            Report("D2 滚轮内缩一格（应放行）", hw);
            Console.WriteLine($"    Handled={h2}  span={PartSpan(hw):F0}");

            // 强行造出"缩过头"的状态（复刻现场：旧缓存/外部 SetPart 留下的超大 part）
            hw.SetPart(-300000.0, -300000.0, 600000.0, 600000.0);
            Pump(150);
            Report("D3 外部强设超大 part（复刻现场黑屏）", hw);
            bool h3 = RaiseWheel(hswc, -120);
            Pump(200);
            Report("D4 此状态下滚轮外缩（应弹回铺满）", hw);
            Console.WriteLine($"    Handled={h3}  span={PartSpan(hw):F0}（铺满基线 {fitSpan:F0}）");

            // 放大极限：连续内缩到跨度 < 8 像素应被拦
            for (int i = 0; i < 40; i++) RaiseWheel(hswc, 120);
            Pump(200);
            Report("D5 连续内缩 40 格（跨度应停在 >= 8）", hw);
            Console.WriteLine($"    span={PartSpan(hw):F0}");

            // RaiseEvent(Preview) 不会驱动控件自身的 MouseWheel，所以用控件公开方法真放大到极限，
            // 再验证「到极限后滚轮内缩被拦」
            for (int i = 0; i < 40 && PartSpan(hw) > 8; i++) hswc.HZoomWindowContents(cx, cy, 120);
            Pump(200);
            double spanAtLimit = PartSpan(hw);
            bool h4 = RaiseWheel(hswc, 120);
            Pump(150);
            Console.WriteLine($"    D6 放大到极限 span={spanAtLimit:F1}，再内缩 Handled={h4}，span 仍={PartSpan(hw):F1}");

            win.Close();
            Pump(200);
        }

        private static void ForceRender(HalconBase display)
        {
            var m = typeof(HalconBase).GetMethod("RenderAll", BindingFlags.NonPublic | BindingFlags.Instance);
            m?.Invoke(display, null);
        }

        private static double PartSpan(HWindow hw)
        {
            try
            {
                HOperatorSet.GetPart(hw, out HTuple r1, out _, out HTuple r2, out _);
                return r2.D - r1.D;
            }
            catch { return -1; }
        }

        /// <summary>按 WPF 的隧道阶段投递一次滚轮事件（Handled 之后控件自己的 MouseWheel 不再执行）</summary>
        private static bool RaiseWheel(HSmartWindowControlWPF hswc, int delta)
        {
            var args = new System.Windows.Input.MouseWheelEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, delta)
            {
                RoutedEvent = UIElement.PreviewMouseWheelEvent,
            };
            hswc.RaiseEvent(args);
            return args.Handled;
        }

        private static string FlushState()        {
            try
            {
                HOperatorSet.GetSystem(new HTuple("flush_graphic"), out HTuple v);
                return v.ToString();
            }
            catch (Exception ex) { return "? " + ex.Message; }
        }

        private static void Report(string step, HWindow hw)
        {
            try
            {
                HOperatorSet.GetPart(hw, out HTuple r1, out HTuple c1, out HTuple r2, out HTuple c2);
                using var shot = hw.DumpWindowImage();
                HOperatorSet.CountChannels(shot, out HTuple ch);
                double maxBright = 0, mean1 = 0;
                for (int c = 1; c <= ch.I; c++)
                {
                    using var chImg = ch.I == 1 ? new HImage(shot) : shot.AccessChannel(c);
                    HOperatorSet.GetDomain(chImg, out HObject domain);
                    HOperatorSet.Threshold(chImg, out HObject bright, 1, 255);
                    HOperatorSet.AreaCenter(bright, out HTuple area, out _, out _);
                    HOperatorSet.AreaCenter(domain, out HTuple total, out _, out _);
                    double frac = area.D / Math.Max(1.0, total.D) * 100.0;
                    if (frac > maxBright) maxBright = frac;
                    if (c == 1)
                    {
                        HOperatorSet.Intensity(domain, chImg, out HTuple mean, out _);
                        mean1 = mean.D;
                    }
                    bright.Dispose();
                    domain.Dispose();
                }
                Console.WriteLine(
                    $"{step,-34} part=({r1.D:F1},{c1.D:F1})-({r2.D:F1},{c2.D:F1}) "
                    + $"span={r2.D - r1.D:F0}x{c2.D - c1.D:F0} ch={ch.I} "
                    + $"meanR={mean1:F1} nonblack={maxBright:F1}%");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{step,-34} DUMP FAILED: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static T FindChild<T>(DependencyObject root) where T : DependencyObject
        {
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T typed) return typed;
                var found = FindChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        private static void Pump(int ms)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(ms),
            };
            timer.Tick += (_, __) =>
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }
    }
}
