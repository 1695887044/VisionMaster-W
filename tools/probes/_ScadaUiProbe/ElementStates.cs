using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using VisionMaster.Scada.Controls;

namespace ScadaUiProbe
{
    /// <summary>
    /// 第三轮会审用的"图元状态出图"。
    ///
    /// 能力边界（写在最前面，避免把"空态截图"当成"运行态截图"用）
    /// =========
    /// 这里只渲染**不依赖运行态上下文**的状态：设计期 / 空态 / 未配置 / 禁用。
    /// 运行态（有报警数据、轴在动、表格有行）需要 <c>ScadaRuntimeContext</c>（报警引擎 + 数据泵 + 轴源），
    /// 那是另一个量级的装配，本轮不假装能出；会审时这类状态按模板代码与既有断言评审。
    /// </summary>
    internal static class ElementStates
    {
        private static readonly (string TypeKey, string Name, int W, int H, Dictionary<string, string>? Props, bool Disabled)[] Cases =
        {
            // 数据表格：设计期（配了列但没数据）/ 未配置 / 禁用
            ("Hmi.DataGrid", "datagrid-design", 720, 300, new()
            {
                ["Columns"] = "[{\"Header\":\"时间\",\"Variable\":\"Time\",\"Width\":150,\"Align\":\"Left\",\"Format\":\"HH:mm:ss\"},"
                              + "{\"Header\":\"产量\",\"Variable\":\"Qty\",\"Width\":0,\"Align\":\"Right\",\"Format\":\"F0\"},"
                              + "{\"Header\":\"合格率\",\"Variable\":\"Rate\",\"Width\":100,\"Align\":\"Right\",\"Format\":\"P1\"}]",
                ["RowHeight"] = "28",
            }, false),
            ("Hmi.DataGrid", "datagrid-empty", 720, 300, null, false),
            ("Hmi.DataGrid", "datagrid-disabled", 720, 300, null, true),

            // 报警条：空态（系统正常）/ 禁用
            ("Hmi.AlarmBanner", "alarmbanner-empty", 320, 140, null, false),
            ("Hmi.AlarmBanner", "alarmbanner-disabled", 320, 140, null, true),

            // 轴面板：未配置轴 / 禁用
            ("Hmi.AxisPanel", "axispanel-unconfigured", 280, 380, null, false),
            ("Hmi.AxisPanel", "axispanel-disabled", 280, 380, null, true),

            // 轴控台：未配置轴（有点位区）
            ("Hmi.AxisConsole", "axisconsole-unconfigured", 320, 500, null, false),
        };

        public static void RenderAll(string root)
        {
            var dir = Path.Combine(root, "third-round");
            Directory.CreateDirectory(dir);

            foreach (var item in Cases)
            {
                try
                {
                    Shot(dir, item);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [FAIL] {item.Name}: {ex.Message}");
                }
            }
        }

        private static void Shot(string dir, (string TypeKey, string Name, int W, int H,
            Dictionary<string, string>? Props, bool Disabled) item)
        {
            var element = ElementRegistry.CreateElement(item.TypeKey);
            element.Width = item.W;
            element.Height = item.H;

            if (item.Props != null)
            {
                foreach (var pair in item.Props)
                    element.SetProperty(pair.Key, pair.Value);
            }

            var control = ElementRegistry.CreateControl(element);
            control.Width = item.W;
            control.Height = item.H;
            control.IsEnabled = !item.Disabled;

            var host = new System.Windows.Controls.Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x15, 0x1C, 0x23)),
                Padding = new Thickness(12),
                Child = control,
            };

            var window = Show(host, item.W + 24, item.H + 24);
            Pump(120);

            var bitmap = Render(host, item.W + 24, item.H + 24);
            var path = Path.Combine(dir, item.Name + ".png");
            Save(bitmap, path);

            Console.WriteLine($"  {item.Name}.png  {item.W}×{item.H}");

            window.Close();
        }

        // 与 Program 里的同名工具保持一致（探针内部各自私有一份，避免跨类暴露）
        private static Window Show(FrameworkElement content, int width, int height)
        {
            var window = new Window
            {
                Width = width,
                Height = height,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -4000,
                Top = 40,
                ShowInTaskbar = false,
                Content = content,
            };

            window.Show();
            return window;
        }

        private static System.Windows.Media.Imaging.RenderTargetBitmap Render(Visual visual, int width, int height)
        {
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                Math.Max(1, width), Math.Max(1, height), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        private static void Save(System.Windows.Media.Imaging.BitmapSource bitmap, string path)
        {
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }

        private static void Pump(int milliseconds)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            var stopper = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Send)
            {
                Interval = TimeSpan.FromMilliseconds(milliseconds),
            };
            stopper.Tick += (_, _) =>
            {
                stopper.Stop();
                frame.Continue = false;
            };
            stopper.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
    }
}
