using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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

    public class NestedConfig
    {
        [SuperDisplay(Name = "心跳间隔", Group = new[] { "链路参数" }, Order = 1)]
        [RangeValidation(100, 60000)]
        public int HeartbeatMs { get; set; } = 5000;

        [SuperDisplay(Name = "帧间隔", Group = new[] { "链路参数" }, Order = 2)]
        [RangeValidation(0, 1000)]
        public int FrameGapMs { get; set; } = 20;

        [SuperDisplay(Name = "原始帧记录", Group = new[] { "链路参数" }, Order = 3)]
        public bool RecordRaw { get; set; }
    }

    /// <summary>没有可见属性的对象：空态用</summary>
    public class EmptyVm
    {
        public string NotShown { get; set; } = "没有 [SuperDisplay]，不应出现";

        [SuperDisplay(Name = "隐藏字段", Visible = false)]
        public string Hidden { get; set; } = "Visible=false，不应出现";
    }

    public class SampleVm
    {
        // —— 链路配置（默认页签）——
        [SuperDisplay(Name = "连接名称", Group = new[] { "链路配置" }, Order = 1)]
        public string ConnectionName { get; set; } = "Modbus TCP - 1号线";

        [SuperDisplay(Name = "协议类型", Group = new[] { "链路配置" }, Order = 2)]
        public WorkMode Protocol { get; set; } = WorkMode.Continuous;

        [SuperDisplay(Name = "启用该连接", Group = new[] { "链路配置" }, Order = 3)]
        public bool Enabled { get; set; } = true;

        [SuperDisplay(Name = "IP 地址", Group = new[] { "链路配置" }, Order = 4, Description = "控制器的 IPv4 地址")]
        // 用**字符串 + 正则**而不是数值 + RangeValidation：后者会被 NumericBox 的
        // CoerceValueCallback 提前夹回合法区间（300 → 255），于是"校验器触发"这件事永远测不到。
        // 字符串没有钳制，非法值能真正落到校验器手里。
        [RegexValidation(@"^((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\.){3}(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)$",
            "IP 格式不正确，应为 0–255 的四段点分十进制")]
        public string IpAddress { get; set; } = "192.168.1.10";

        [SuperDisplay(Name = "端口", Group = new[] { "链路配置" }, Order = 5)]
        [RangeValidation(1, 65535)]
        public int Port { get; set; } = 502;

        [SuperDisplay(Name = "从站地址", Group = new[] { "链路配置" }, Order = 6)]
        [RangeValidation(0, 247)]
        public int SlaveId { get; set; } = 1;

        [SuperDisplay(Name = "连接超时", Group = new[] { "链路配置" }, Order = 7)]
        [RangeValidation(100, 60000)]
        public int TimeoutMs { get; set; } = 3000;

        [SuperDisplay(Name = "链路参数", Group = new[] { "链路配置" }, Order = 8)]
        [PropertyItem(Type = typeof(FrameworkElement))]
        public NestedConfig Link { get; set; } = new();

        [SuperDisplay(Name = "执行自检", Group = new[] { "链路配置" }, Order = 9)]
        [Command(Command = nameof(CheckCommand))]
        public bool RunCheck { get; set; }

        [SuperDisplay(Name = "设备型号", Group = new[] { "链路配置" }, Order = 10, IsReadOnly = true)]
        public string DeviceModel { get; set; } = "CP-2434-1";

        // —— 运行调度 ——
        [SuperDisplay(Name = "读周期", Group = new[] { "运行调度" }, GroupOrder = "2", Order = 1)]
        [RangeValidation(10, 60000)]
        public int ReadCycleMs { get; set; } = 500;

        [SuperDisplay(Name = "自动重连", Group = new[] { "运行调度" }, GroupOrder = "2", Order = 2)]
        public bool AutoReconnect { get; set; } = true;

        [SuperDisplay(Name = "开机自启", Group = new[] { "运行调度" }, GroupOrder = "2", Order = 3)]
        public bool AutoStart { get; set; } = true;

        // —— 高级（权限受限）——
        [SuperDisplay(Name = "管理员令牌", Group = new[] { "高级" }, GroupOrder = "10", Order = 1)]
        [Permission(RequiredRole = "Admin")]
        public string AdminToken { get; set; } = "tok-123";

        public System.Windows.Input.ICommand CheckCommand { get; } = new DelegateCommand(() => { });
    }

    public class DelegateCommand : System.Windows.Input.ICommand
    {
        private readonly Action _exec;
        public DelegateCommand(Action exec) => _exec = exec;
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _exec();
    }

    internal static class Program
    {
        private static int _pass;
        private static int _fail;

        [STAThread]
        private static void Main()
        {
            Console.WriteLine("ClientAreaAnimation = " + SystemParameters.ClientAreaAnimation);
            if (Application.Current == null)
                new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/UI;component/Themes/Generic.xaml")
            });

            var outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..");
            outDir = Path.GetFullPath(outDir);

            // ---------- 1. 扁平式：默认态 ----------
            var flat = new FlatPropertyGrid { BindingObject = new SampleVm() };
            var flatPage = BuildPage(("FlatPropertyGrid · 默认态", flat), 900, 760);
            ShowAndCapture(flatPage, Path.Combine(outDir, "pg-flat-ok.png"), () =>
            {
                CheckMeasurements(flat, "扁平式默认态");
                DumpTabs(flat, "flat");
            });

            // ---------- 2. 卡片式：默认态 ----------
            var card = new CardPropertyGrid { BindingObject = new SampleVm() };
            var cardPage = BuildPage(("CardPropertyGrid · 默认态", card), 900, 760);
            ShowAndCapture(cardPage, Path.Combine(outDir, "pg-card-ok.png"), () =>
            {
                CheckCardMeasurements(card);
                DumpTabs(card, "card");
            });

            // ---------- 3. 校验失败态 ----------
            var badVm = new SampleVm { IpAddress = "192.168.1.300" };
            var flatErr = new FlatPropertyGrid { BindingObject = badVm };
            var errPage = BuildPage(("FlatPropertyGrid · 校验失败", flatErr), 900, 760);
            ShowAndCapture(errPage, Path.Combine(outDir, "pg-flat-error.png"), () =>
            {
                CheckValidationError(flatErr);
            });

            // ---------- 4. 空态 ----------
            // 空态不给固定高度：探针要验证"空态按内容收高"，
            // 写死 Height 就把要测的东西盖掉了。
            var flatEmpty = new FlatPropertyGrid { BindingObject = new EmptyVm() };
            var emptyPage = BuildPage(("FlatPropertyGrid · 空态（按内容收高）", flatEmpty), 900, 500, fixedHeight: false);
            ShowAndCapture(emptyPage, Path.Combine(outDir, "pg-flat-empty.png"), () =>
            {
                CheckEmptyState(flatEmpty, "扁平式");
                Check("空态：控件按内容收高（不撑满容器）",
                    flatEmpty.ActualHeight > 0 && flatEmpty.ActualHeight < 300,
                    "实际高 " + Math.Round(flatEmpty.ActualHeight));
            });

            var cardEmpty = new CardPropertyGrid { BindingObject = new EmptyVm() };
            var cardEmptyPage = BuildPage(("CardPropertyGrid · 空态（按内容收高）", cardEmpty), 900, 500, fixedHeight: false);
            ShowAndCapture(cardEmptyPage, Path.Combine(outDir, "pg-card-empty.png"), () =>
            {
                CheckEmptyState(cardEmpty, "卡片式");
                Check("卡片式空态：控件按内容收高",
                    cardEmpty.ActualHeight > 0 && cardEmpty.ActualHeight < 300,
                    "实际高 " + Math.Round(cardEmpty.ActualHeight));
            });

            // ---------- 5. 权限受限 ----------
            PermissionProcessor.CurrentMockRole = "Operator";
            var denied = new FlatPropertyGrid { BindingObject = new SampleVm() };
            var deniedPage = BuildPage(("FlatPropertyGrid · 权限受限（Operator）", denied), 900, 760);
            ShowAndCapture(deniedPage, Path.Combine(outDir, "pg-flat-denied.png"), () =>
            {
                CheckDenied(denied);
            });
            PermissionProcessor.CurrentMockRole = "Admin";

            // ---------- 汇总 ----------
            Console.WriteLine();
            Console.WriteLine("========== 结果汇总 =========");
            Console.WriteLine($"通过 {_pass} / 失败 {_fail}");
            Environment.ExitCode = _fail == 0 ? 0 : 1;
        }

        // ---------------- 断言 ----------------

        /// <summary>扁平式：行高 48、控件高 36、标签列 150、最后一行无底边线</summary>
        private static void CheckMeasurements(Control grid, string tag)
        {
            grid.UpdateLayout();
            var rows = FindAll<Border>(grid)
                .Where(b => b.Child is Grid g && g.ColumnDefinitions.Count == 2 && g.ColumnDefinitions[0].SharedSizeGroup != null)
                .ToList();

            Check($"{tag}：找到属性行", rows.Count >= 8, $"实际 {rows.Count} 行");

            var heights = rows.Select(r => Math.Round(r.ActualHeight)).Distinct().OrderBy(h => h).ToList();
            // 嵌套行（内含子网格）天然更高，不算"行高不统一"——它承载的是一个块，不是一行字段。
            var plainHeights = rows
                .Where(r => !FindAll<PropertyGridBase>((DependencyObject)r).Any())
                .Select(r => Math.Round(r.ActualHeight))
                .Distinct().OrderBy(h => h).ToList();
            Check($"{tag}：【普通行高统一 48】", plainHeights.All(h => Math.Abs(h - 48) < 0.5),
                "普通行=" + string.Join("/", plainHeights) + "；全部行=" + string.Join("/", heights));

            var inputs = FindAll<Control>(grid)
                .Where(c => c is TextBox or ComboBox)
                .Where(c => c.IsVisible && c.ActualHeight > 0)
                .ToList();
            var controlHeights = inputs.Select(c => Math.Round(c.ActualHeight)).Distinct().OrderBy(h => h).ToList();
            Check($"{tag}：【控件高统一 36】", controlHeights.Count > 0 && controlHeights.All(h => Math.Abs(h - 36) < 0.5),
                "控件高度=" + string.Join("/", controlHeights));

            var labelBorders = rows.Select(r => FindAll<Border>((DependencyObject)r)
                    .FirstOrDefault(b => b.Width > 0 && Math.Abs(b.Width - 150) < 0.5))
                .Where(b => b != null).ToList();
            Check($"{tag}：【标签列 150px】", labelBorders.Count == rows.Count,
                $"命中 {labelBorders.Count} / 应有 {rows.Count}");

            var lastRow = rows.LastOrDefault();
            Check($"{tag}：最后一行不带底边线", lastRow != null && lastRow.BorderThickness.Bottom == 0,
                lastRow == null ? "没有行" : "BorderThickness=" + lastRow.BorderThickness);
        }

        /// <summary>卡片式：左栏 176、导航项 34 高、有计数</summary>
        private static void CheckCardMeasurements(Control grid)
        {
            grid.UpdateLayout();
            var rail = FindAll<Border>(grid).FirstOrDefault(b => b.BorderThickness.Right == 1 && b.ActualWidth > 100);
            Check("卡片式：【左栏 176px】", rail != null && Math.Abs(rail.ActualWidth - 176) < 1,
                rail == null ? "找不到左栏" : "实际宽 " + Math.Round(rail.ActualWidth));

            var tabControl = FindAll<TabControl>(grid).FirstOrDefault();
            var tabs = tabControl?.Items.OfType<TabItem>().ToList() ?? new List<TabItem>();
            Check("卡片式：页签数量 = 3 个分组", tabs.Count == 3, "实际 " + tabs.Count + "：" + string.Join("/", tabs.Select(t => t.Header)));

            var counts = tabs.Select(t => PropertyGridAttach.GetGroupCount(t)).ToList();
            Check("卡片式：每个页签带分组计数（>0）", counts.All(c => c > 0), "计数=" + string.Join("/", counts));
        }

        /// <summary>校验失败：行底色浅红 + 错误文案可见 + 控件红描边</summary>
        private static void CheckValidationError(Control grid)
        {
            // 页签是懒加载的：不选中就不构建内容，断言必须先把含 IP 的页签点开
            SelectTabByHeader(grid, "链路配置");
            grid.UpdateLayout();
            var errorTexts = FindAll<TextBlock>(grid)
                .Where(t => t.Text.Contains("IP 格式不正确") && t.IsVisible)
                .ToList();
            Check("校验失败：错误文案已显示", errorTexts.Count == 1,
                errorTexts.Count == 0 ? "没找到错误文案" : "找到 " + errorTexts.Count + " 条");

            if (errorTexts.Count == 1)
            {
                var danger = (Application.Current.TryFindResource("DialogDangerTextBrush") as Brush);
                var actual = errorTexts[0].Foreground as SolidColorBrush;
                Check("校验失败：错误文案用危险色令牌", danger is SolidColorBrush d && actual != null && actual.Color == d.Color,
                    actual == null ? "非 SolidColorBrush" : "#" + actual.Color.ToString());
            }

            // 行底色应为浅红（DialogDangerSubtleBrush = #FEF2F2）
            var errorRow = FindAll<Border>(grid)
                .FirstOrDefault(b => b.Background is SolidColorBrush sb && sb.Color.R == 0xFE && sb.Color.G == 0xF2 && sb.Color.B == 0xF2);
            Check("校验失败：所在行染浅红底", errorRow != null,
                errorRow == null ? "没有行染红" : "行高 " + Math.Round(errorRow.ActualHeight));

            // 非法值不得被改写（校验只标状态）
            var vm = (SampleVm)((PropertyGridBase)grid).BindingObject;
            Check("校验失败：非法值原样保留（校验不改值）", vm.IpAddress == "192.168.1.300", vm.IpAddress);
        }

        /// <summary>空态：说明可见、无属性行、卡片式左栏收起</summary>
        private static void CheckEmptyState(PropertyGridBase grid, string tag)
        {
            grid.UpdateLayout();
            Check($"{tag}：IsEmptyState = true", grid.IsEmptyState, "实际 " + grid.IsEmptyState);

            var title = FindAll<TextBlock>(grid).FirstOrDefault(t => t.Text.Contains("没有可显示的属性"));
            Check($"{tag}：空态说明可见", title != null && title.IsVisible, title == null ? "没找到说明" : "已显示");

            var rows = FindAll<Border>(grid)
                .Where(b => b.Child is Grid g && g.ColumnDefinitions.Count == 2 && g.ColumnDefinitions[0].SharedSizeGroup != null)
                .ToList();
            Check($"{tag}：没有任何属性行", rows.Count == 0, "实际 " + rows.Count + " 行");

            // 隐藏字段与无特性字段都不得出现
            var leaked = FindAll<TextBlock>(grid)
                .Where(t => t.Text.Contains("隐藏字段") || t.Text.Contains("没有 [SuperDisplay]"))
                .ToList();
            Check($"{tag}：隐藏字段未泄漏", leaked.Count == 0, "泄漏 " + leaked.Count + " 条");

            if (grid is CardPropertyGrid)
            {
                var rail = FindAll<Border>(grid).FirstOrDefault(b => b.BorderThickness.Right == 1 && b.ActualWidth > 100);
                Check("卡片式：空态下左栏已收起", rail == null || !rail.IsVisible || rail.ActualWidth < 1,
                    rail == null ? "左栏不存在" : "左栏宽 " + Math.Round(rail.ActualWidth));
            }
        }

        /// <summary>权限受限：控件禁用 + 锁图标 + 说明文字</summary>
        private static void CheckDenied(Control grid)
        {
            // 令牌在「高级」页签，懒加载，先切过去
            SelectTabByHeader(grid, "高级");
            grid.UpdateLayout();
            var tokenBox = FindAll<TextBox>(grid).FirstOrDefault(t => !t.IsEnabled && t.Text.Contains("tok-"));
            Check("权限受限：令牌框已禁用", tokenBox != null, tokenBox == null ? "没找到被禁用的令牌框" : "已禁用");

            var help = FindAll<TextBlock>(grid).FirstOrDefault(t => t.Text.Contains("无此权限"));
            Check("权限受限：给出原因说明", help != null && help.IsVisible, help == null ? "没找到说明文字" : help.Text);

            // 锁图标是作为 Run 追加进标签的 Inlines，TextBlock.Text 不包含它，必须查 Inlines
            var lockIcon = FindAll<TextBlock>(grid)
                .FirstOrDefault(t => t.Inlines.OfType<System.Windows.Documents.Run>().Any(r => r.Text.Contains("\U0001F512")));
            Check("权限受限：标签带锁图标", lockIcon != null, lockIcon == null ? "没找到锁" : "已显示");
        }

        // ---------------- 辅助 ----------------

        private static void Check(string name, bool ok, string detail)
        {
            _pass += ok ? 1 : 0;
            _fail += ok ? 0 : 1;
            Console.WriteLine($"  [{(ok ? "通过" : "失败")}] {name}" + (string.IsNullOrEmpty(detail) ? "" : "  -> " + detail));
        }

        private static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T hit) yield return hit;
                foreach (var deep in FindAll<T>(child)) yield return deep;
            }
        }

        private static void DumpTabs(Control grid, string tag)
        {
            var tab = FindAll<TabControl>(grid).FirstOrDefault();
            if (tab == null) { Console.WriteLine($"  {tag} tab: (none)"); return; }
            foreach (var item in tab.Items.OfType<TabItem>())
                Console.WriteLine($"  {tag} tab: [{item.Header}] count={PropertyGridAttach.GetGroupCount(item)}");
        }

        /// <summary>按标题选中页签（页签是懒加载的，断言前必须先选中才会构建内容）</summary>
        private static void SelectTabByHeader(Control grid, string header)
        {
            var tab = FindAll<TabControl>(grid).FirstOrDefault();
            if (tab == null) return;
            var item = tab.Items.OfType<TabItem>()
                .FirstOrDefault(t => string.Equals(t.Header as string, header, StringComparison.Ordinal));
            if (item != null)
            {
                tab.SelectedItem = item;
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                grid.UpdateLayout();
            }
        }

        private static FrameworkElement BuildPage((string title, FrameworkElement content) section, double width, double height, bool fixedHeight = true)
        {
            var panel = new StackPanel { Margin = new Thickness(10), Background = Brushes.White };
            panel.Children.Add(new TextBlock
            {
                Text = section.title,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(2, 2, 0, 8),
                Foreground = Brushes.Black,
            });
            section.content.Width = width - 40;
            if (fixedHeight) section.content.Height = height - 60;
            panel.Children.Add(new Border
            {
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(1),
                Child = section.content,
            });

            return new Grid { Background = Brushes.White, Width = width, Height = height, Children = { panel } };
        }

        private static void ShowAndCapture(FrameworkElement root, string path, Action? onShown = null)
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

            for (var i = 0; i < 100; i++)
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                Thread.Sleep(10);
            }
            root.UpdateLayout();
            onShown?.Invoke();
            root.UpdateLayout();

            const double scale = 1.25;
            var rtb = new RenderTargetBitmap(
                (int)(root.Width * scale), (int)(root.Height * scale),
                96 * scale, 96 * scale, PixelFormats.Pbgra32);
            rtb.Render(root);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(path)) enc.Save(fs);
            Console.WriteLine("  saved " + Path.GetFileName(path));
            win.Close();
        }
    }
}
