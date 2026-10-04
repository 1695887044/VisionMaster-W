using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Core.Interfaces;
using UI.Attributes;
using UI.CustomControl;
using UI.CustomControl.PropertyGrid;

namespace StyleProbe
{
    public enum WorkMode
    {
        [Description("连续模式")] Continuous,
        [Description("单次模式")] Single,
        [Description("步进模式")] Step
    }

    public class DelegateCommand : ICommand
    {
        private readonly Action _exec;
        public DelegateCommand(Action exec) => _exec = exec;
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _exec();
    }

    public class NestedConfig
    {
        [SuperDisplay(Name = "子项开关", Group = new[] { "子参数" }, Order = 1)]
        public bool SubA { get; set; } = true;

        [SuperDisplay(Name = "子项名称", Group = new[] { "子参数" }, Order = 2)]
        public string SubB { get; set; } = "嵌套值";
    }

    public class SampleVm
    {
        // —— 连接（GroupOrder 默认 "0"，应排第一）——
        [SuperDisplay(Name = "启用日志", Group = new[] { "连接" }, Order = 1)]
        public bool EnableLogging { get; set; } = true;

        [SuperDisplay(Name = "IP 地址", Group = new[] { "连接" }, Order = 2, Description = "控制器的 IPv4 地址")]
        [RegexValidation(@"^\d{1,3}(\.\d{1,3}){3}$", "IP 格式不正确")]
        public string IpAddress { get; set; } = "192.168.1.10";

        [SuperDisplay(Name = "端口", Group = new[] { "连接" }, Order = 3)]
        [RangeValidation(1, 65535)]
        public int Port { get; set; } = 502;

        [SuperDisplay(Name = "工作模式", Group = new[] { "连接" }, Order = 4)]
        public WorkMode Mode { get; set; }

        [SuperDisplay(Name = "序列号", Group = new[] { "连接" }, Order = 5, IsReadOnly = true)]
        public string SerialNo { get; set; } = "SN-2026-001";

        [SuperDisplay(Name = "管理员令牌", Group = new[] { "连接" }, Order = 6)]
        [Permission(RequiredRole = "Admin")]
        public string AdminToken { get; set; } = "tok-123";

        [SuperDisplay(Name = "隐藏字段", Group = new[] { "连接" }, Visible = false)]
        public string Hidden { get; set; } = "不应显示";

        // —— 高级（GroupOrder "10"，数值上应排在"报警"之后；字符串排序会把它插到中间）——
        [SuperDisplay(Name = "轴名", Group = new[] { "高级" }, GroupOrder = "10", Order = 1)]
        [StepConfigOptions(StepConfigOptionKind.MotionAxisName)]
        public string AxisName { get; set; } = "";

        // —— 报警（GroupOrder "2"）——
        [SuperDisplay(Name = "报警使能", Group = new[] { "报警" }, GroupOrder = "2", Order = 1, ColSpan = 6)]
        public bool AlarmEnabled { get; set; } = true;

        [SuperDisplay(Name = "联动使能", Group = new[] { "报警" }, GroupOrder = "2", Order = 2, ColSpan = 6)]
        public bool LinkageEnabled { get; set; }

        [SuperDisplay(Name = "执行自检", Group = new[] { "报警" }, GroupOrder = "2", Order = 3)]
        [Command(Command = nameof(CheckCommand))]
        public bool RunCheck { get; set; }

        [SuperDisplay(Name = "子参数表", Group = new[] { "报警" }, GroupOrder = "2", Order = 4)]
        [PropertyItem(Type = typeof(FrameworkElement))]
        public NestedConfig Child { get; set; } = new();

        public ICommand CheckCommand { get; } = new DelegateCommand(() => { });
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Console.WriteLine("ClientAreaAnimation = " + SystemParameters.ClientAreaAnimation);
            // OnLastWindowClose 会让第一张图截完关窗后整个 Application 关停，
            // 第二次截图就只剩黑屏 —— 改为显式关停。
            if (Application.Current == null)
                new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

            // 复现 App.xaml 的做法：合并 UI 的 Generic.xaml（含两个属性网格的隐式样式）。
            // UI 程序集没有 ThemeInfo 声明 → 默认样式查找不生效，宿主不合并就静默空白。
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/UI;component/Themes/Generic.xaml")
            });

            var vm = new SampleVm();
            var card = new CardPropertyGrid { BindingObject = vm };
            var flat = new FlatPropertyGrid { BindingObject = vm };

            var root = BuildPage(new (string, FrameworkElement)[]
            {
                ("CardPropertyGrid（卡片式）", card),
                ("FlatPropertyGrid（扁平式）", flat),
            }, 1180, 900);

            ShowAndCapture(root, "pg_render.png", () =>
            {
                foreach (var tab in DumpTabs(card)) Console.WriteLine("card tab: " + tab);
                foreach (var tab in DumpTabs(flat)) Console.WriteLine("flat tab: " + tab);
                DumpWidths(card, "card");
            });

            // 权限降级场景：非 Admin 角色
            PermissionProcessor.CurrentMockRole = "Operator";
            var flat2 = new FlatPropertyGrid { BindingObject = new SampleVm() };
            var root2 = BuildPage(new (string, FrameworkElement)[]
            {
                ("FlatPropertyGrid — CurrentMockRole=Operator（权限不足行应禁用 0.4）", flat2),
            }, 600, 900);

            ShowAndCapture(root2, "pg_render_denied.png", () =>
            {
                foreach (var tab in DumpTabs(flat2)) Console.WriteLine("denied tab: " + tab);
            });
            Console.WriteLine("done");
        }

        private static void DumpWidths(DependencyObject root, string tag)
        {
            var sb = new System.Text.StringBuilder();
            Walk(root, 0, sb);
            Console.WriteLine(sb.ToString());
        }

        private static void Walk(DependencyObject node, int depth, System.Text.StringBuilder sb)
        {
            if (depth > 30 || sb.Length > 30000) return;
            var fe = node as FrameworkElement;
            var indent = new string(' ', depth * 2);
            if (fe != null)
            {
                var info = $"{fe.GetType().Name} w={fe.ActualWidth:F0} h={fe.ActualHeight:F0} ha={fe.HorizontalAlignment}";
                if (fe is TextBox tb) info += $" text=\"{tb.Text}\"";
                if (fe is TabItem ti) info += $" header={ti.Header}";
                if (fe is ContentPresenter cp)
                    info += $" halign={cp.HorizontalAlignment} margin={cp.Margin}";
                if (fe is Border b)
                    info += $" margin={b.Margin} padding={b.Padding} halign={b.HorizontalAlignment}";
                if (fe is Expander ex)
                    info += $" hca={ex.HorizontalContentAlignment} padding={ex.Padding}";
                if (fe.ActualWidth > 0 || fe is TabItem)
                    sb.AppendLine(indent + info);
            }
            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++)
                Walk(VisualTreeHelper.GetChild(node, i), depth + 1, sb);
        }

        private static System.Collections.Generic.IEnumerable<string> DumpTabs(Control grid)        {
            var tab = FindDescendant<TabControl>(grid);
            if (tab == null) yield break;
            foreach (var item in tab.Items.OfType<TabItem>())
                yield return "" + item.Header;
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : class
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T hit) return hit;
                var deep = FindDescendant<T>(child);
                if (deep != null) return deep;
            }
            return null;
        }

        private static FrameworkElement BuildPage((string title, FrameworkElement content)[] sections, double width, double height)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10), Background = Brushes.White };
            foreach (var (title, content) in sections)
            {
                var sp = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
                sp.Children.Add(new TextBlock
                {
                    Text = title,
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(2, 2, 0, 8),
                    Foreground = Brushes.Black,
                });
                content.Width = width / sections.Length - 40;
                content.Height = height - 50;
                sp.Children.Add(new Border
                {
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    Child = content,
                });
                panel.Children.Add(sp);
            }

            var root = new Grid { Background = Brushes.White, Width = width, Height = height };
            root.Children.Add(panel);
            return root;
        }

        private static void ShowAndCapture(FrameworkElement root, string fileName, Action? onShown = null)
        {
            var win = new Window
            {
                Content = root,
                Width = root.Width + 40,
                Height = root.Height + 60,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 10,
                Top = 10,
                ShowInTaskbar = false,
                ShowActivated = false,
                Title = "propertygrid probe"
            };
            win.Show();

            // 泵 Dispatcher：等模板应用、Tab 懒加载构建、绑定完成
            for (var i = 0; i < 150; i++)
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                Thread.Sleep(10);
            }
            root.UpdateLayout();
            onShown?.Invoke();

            const double scale = 1.25;
            var rtb = new RenderTargetBitmap(
                (int)(root.Width * scale), (int)(root.Height * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            rtb.Render(root);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(@"D:\C#\VM\_StyleProbe\" + fileName);
            enc.Save(fs);
            Console.WriteLine("saved " + fileName);
            win.Close();
        }
    }
}
