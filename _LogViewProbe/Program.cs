using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UI.Models;

namespace LogViewProbe
{
    /// <summary>
    /// 日志面板（LogView + LogConsole）离屏渲染探针：把真视图 + 真 VM 渲染成 PNG，
    /// 用来肉眼验收工具条与日志行的观感（与 _StyleProbe 同一套做法）。
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            if (Application.Current == null)
                new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

            // 探针里没有 Prism 容器：不加这一句，new LogView() 会在 XamlParseException 里炸
            // （AutoWireViewModel=True → 默认工厂 Activator.CreateInstance(LogViewModel) → 没有无参构造）。
            // 与 UIThemeSmokeTest:148 同一做法。
            Prism.Mvvm.ViewModelLocationProvider.SetDefaultViewModelFactory(_ => null);

            // 复现 App.xaml：UI 主题（含 LogConsole 样式）+ .NET 9 Fluent（工具条上的
            // ComboBox / CheckBox / TextBox 吃的是它，不合并就看不出真机观感）
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/UI;component/Themes/Generic.xaml")
            });
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml")
            });

            var vm = new VisionMaster.ViewModels.LogViewModel(null);
            // 真机上的日志：Source 是 null（LogService.PublishLog 从没传过 source）。
            // 这里刻意两种都造：null 的（现状）和有来源的（万一将来填上）。
            vm.SystemLogs.Add(new LogItem(LogLevel.Info, "已加载图像: D:\\C#\\VM\\Image\\bead\\adhesive_bead_01.png (1280x1024)", null));
            vm.SystemLogs.Add(new LogItem(LogLevel.Info, "已加载图像: D:\\C#\\VM\\Image\\bead\\adhesive_bead_01.png (1280x1024)", null));
            vm.SystemLogs.Add(new LogItem(LogLevel.Warning, "标定当量缺失，按默认值 0.05mm/px 继续", null));
            vm.SystemLogs.Add(new LogItem(LogLevel.Error, "图像采集_0 取图超时（3000ms），本周期跳过", null));
            vm.SystemLogs.Add(new LogItem(LogLevel.Success, "流程 MainTask 执行完成，耗时 128ms", "MainTask"));
            vm.SystemLogs.Add(new LogItem(LogLevel.Info, "软件加载成功", "System"));

            var view = new VisionMaster.Views.LogView { DataContext = vm };
            // 探针里没有 Prism 容器，关掉自动装配，免得它把 DataContext 顶掉
            Prism.Mvvm.ViewModelLocator.SetAutoWireViewModel(view, false);

            var root = new Border { Background = Brushes.White, Width = 1000, Height = 300, Child = view };
            ShowAndCapture(root, "logview.png");
            Console.WriteLine("---- 工具条可视树（宽高/边距）----");
            DumpToolbar(view);
            Console.WriteLine("done");
        }

        /// <summary>只 dump 工具条那一层（LogConsole 之上），看数字定布局</summary>
        private static void DumpToolbar(FrameworkElement view)
        {
            var sv = new StringBuilder();
            Walk(view, 0, sv, stopAtConsole: true);
            Console.WriteLine(sv.ToString());
        }

        private static void Walk(DependencyObject node, int depth, StringBuilder sb, bool stopAtConsole)
        {
            if (depth > 12 || sb.Length > 8000) return;
            if (stopAtConsole && node is UI.CustomControl.LogConsole) return;

            if (node is FrameworkElement fe)
            {
                var info = fe.GetType().Name
                           + $" w={fe.ActualWidth:F0} h={fe.ActualHeight:F0}"
                           + $" m={fe.Margin} ha={fe.HorizontalAlignment} va={fe.VerticalAlignment}";
                if (fe is TextBlock tb) info += $" text=\"{tb.Text}\"";
                if (fe is TextBox box) info += $" text=\"{box.Text}\"";
                if (fe is ComboBox cb) info += $" sel={(cb.SelectedItem as VisionMaster.ViewModels.LogLevelFilterOption)?.Text} items={cb.Items.Count}";
                if (fe is CheckBox chk) info += $" checked={chk.IsChecked}";
                if (fe is Border bd) info += $" bg={bd.Background} bb={bd.BorderBrush} bt={bd.BorderThickness} p={bd.Padding}";
                sb.AppendLine(new string(' ', depth * 2) + info);
            }

            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++)
                Walk(VisualTreeHelper.GetChild(node, i), depth + 1, sb, stopAtConsole);
        }

        private static void ShowAndCapture(FrameworkElement root, string fileName)
        {
            var win = new Window
            {
                Content = root,
                Width = root.Width + 40,
                Height = root.Height + 60,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 20,
                Top = 20,
                ShowInTaskbar = false,
                ShowActivated = false,
                Title = "logview probe"
            };
            win.Show();

            for (var i = 0; i < 60; i++)
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                Thread.Sleep(10);
            }

            root.UpdateLayout();

            const double scale = 1.25;
            var rtb = new RenderTargetBitmap(
                (int)(root.Width * scale), (int)(root.Height * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            rtb.Render(root);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(@"D:\C#\VM\_LogViewProbe\" + fileName);
            enc.Save(fs);
            Console.WriteLine("saved " + fileName);
            win.Close();
        }
    }
}
