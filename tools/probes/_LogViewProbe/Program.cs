using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
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
        /// <summary>
        /// 出图目录：直接写进 docs 的样式参考目录，探针本身就是这些图的生成器
        /// （放这里而不是探针自己的 bin：bin 会被清、也没人知道去那儿找）。
        /// </summary>
        private const string OutputDir = @"D:\C#\VM\docs\UI样式参考\";

        [STAThread]
        private static void Main(string[] args)
        {
            // 迁移模式不走 WPF：只做文件转换（受控迁移器，见 NumericBoxMigrator 的类注释）
            if (args.Length > 0 && (args[0] == "--scan-numeric" || args[0] == "--apply-numeric"))
            {
                Environment.ExitCode = NumericBoxMigrator.Run(args[0]);
                return;
            }

            Directory.CreateDirectory(OutputDir);
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

            var vm = BuildVm();
            Capture(vm, 1000, 300, "logview.png", dumpToolbar: true);
            // 窄面板：先吃掉弹簧，再压搜索框→等级→开关
            Capture(BuildVm(), 560, 260, "logview_narrow.png", dumpToolbar: false);
            // 输入后：占位提示必须让位，文字要看得见
            var typed = BuildVm();
            typed.SearchText = "图像采集";
            Capture(typed, 1000, 300, "logview_typed.png", dumpToolbar: false);
            // 等级筛选的三种替代形态（不用下拉框）
            CaptureVariants();
            // 属性网格两种布局在"参数列宽度"下的观感（插件配置壳切卡片式的依据）
            CaptureGridLayouts();
            // 新控件 NumericBox：行为断言 + 状态样式图
            CheckNumericBox();
            CaptureNumericBox();
            // 状态灯 StatusIndicator：断言 + 真模板渲染
            CheckStatusIndicator();
            CaptureConnectionStatus();
            // 插件表单里的数值框：BlobDetect 视图 19 个手写 TextBox 已换成 NumericBox
            CheckBlobDetectNumericBoxes();
            // 迁移批：Matching / Calibration / CaliperMeasure 三个视图（同一套守门口径）
            CheckMigratedPluginViews();
            // SCADA 属性面板的数值编辑器（EditNumber）
            CheckScadaNumberEditor();
            // 插件配置壳"卡片式"开关的端到端链路（真 PreProcessingView + 真插件 VM）
            CheckPreprocessLayoutSwitch();
            // 消息体系的两张样式参考：EasyDialog 弹窗、Notifier 通知卡片
            CaptureEasyDialog();
            CaptureNotifications();
            // Notifier 容量上限回归（2026-10-07 修复项：报警风暴不许糊满右下角）
            CheckNotifierCap();
            Console.WriteLine("done");
        }

        /// <summary>
        /// EasyDialog 弹窗的样式参考图：调真的异步弹窗，等它出现后渲染遮罩窗，再 Close 收口
        /// （EasyDialog 对"非按钮方式关闭"有专门处理：会以"取消"结束，锁也照常释放）。
        /// </summary>
        private static void CaptureEasyDialog()
        {
            var body = new StackPanel { Width = 360 };
            body.Children.Add(new TextBlock
            {
                Text = "删除后该算子及其参数不可恢复。",
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
            });
            body.Children.Add(new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 0),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x62, 0x66)),
                TextWrapping = TextWrapping.Wrap,
                Text = "整链快照已自动保存到「复制整链」的剪贴板，可粘贴还原。",
            });

            var task = UI.CustomControl.EasyDialog.ShowCustomAsync("删除步骤 3（阈值分割）？", body);

            Window? overlay = null;
            for (int i = 0; i < 150 && overlay == null; i++)
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Thread.Sleep(10);
                overlay = Application.Current.Windows.OfType<Window>()
                    .FirstOrDefault(w => w.WindowStyle == WindowStyle.None && w.IsVisible);
            }

            if (overlay == null)
            {
                Console.WriteLine("[SKIP] EasyDialog：没等到遮罩窗出现");
                return;
            }

            overlay.UpdateLayout();
            var rtb = new RenderTargetBitmap(
                Math.Max(1, (int)overlay.ActualWidth), Math.Max(1, (int)overlay.ActualHeight),
                96, 96, PixelFormats.Pbgra32);
            rtb.Render(overlay);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(OutputDir + "easydialog.png"))
                enc.Save(fs);
            Console.WriteLine($"saved easydialog.png ({overlay.ActualWidth:F0}x{overlay.ActualHeight:F0})");

            overlay.Close();   // 走"非按钮关闭"路径收口；锁由 EasyDialog 的 finally 释放
            _ = task;
        }

        /// <summary>Notifier 通知卡片的样式参考图：把 OverlayHost 摆进宿主，再塞三条不同等级的通知</summary>
        private static void CaptureNotifications()
        {
            var host = new UI.CustomControl.OverlayHost { Width = 460, Height = 300 };
            var root = new Border
            {
                Width = 460,
                Height = 300,
                Background = new SolidColorBrush(Color.FromRgb(0xE9, 0xEE, 0xF5)),
                Child = host,
            };

            root.Measure(new Size(460, 300));
            root.Arrange(new Rect(0, 0, 460, 300));
            root.UpdateLayout();

            UI.CustomControl.Notifier.Messages.Clear();
            UI.CustomControl.Notifier.ShowError("通信连接「PLC-1」已断开，正在重连（第 2 次）");
            UI.CustomControl.Notifier.ShowWarning("图像采集_0 取图超时（3000ms），本周期跳过");
            UI.CustomControl.Notifier.ShowSuccess("流程 MainTask 执行完成，耗时 128ms");
            root.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            ShowAndCapture(root, "notifications.png");
            UI.CustomControl.Notifier.Messages.Clear();   // 别把示例卡片留在后续图上
        }

        /// <summary>
        /// 真 PreProcessingView + 真插件 VM：验证"IPropertyGridLayoutSwitch → 网格可见性 + 参数列宽"这条链路，
        /// 并把表格 / 卡片两种状态各渲染一张图（核对三栏重排后预览列还站得住）。
        /// </summary>
        private static void CheckPreprocessLayoutSwitch()
        {
            var plugin = new Plugin.PreProcessing.PreProcessingPlugin();
            var view = new Plugin.PreProcessing.PreProcessingView { DataContext = plugin };
            var host = new Border { Width = 1120, Height = 620, Background = Brushes.White, Child = view };

            host.Measure(new Size(1120, 620));
            host.Arrange(new Rect(0, 0, 1120, 620));
            host.UpdateLayout();
            host.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            var flatGrid = FindChild<UI.CustomControl.FlatPropertyGrid>(view);
            var cardGrid = FindChild<UI.CustomControl.CardPropertyGrid>(view);
            bool flatState = flatGrid != null && flatGrid.Visibility == Visibility.Visible
                             && cardGrid != null && cardGrid.Visibility != Visibility.Visible;

            UI.CustomControl.IPropertyGridLayoutSwitch layoutSwitch = view;
            layoutSwitch.UseCardLayout = true;      // 等价于壳里勾上"卡片式"

            host.UpdateLayout();
            host.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            bool cardState = cardGrid != null && cardGrid.Visibility == Visibility.Visible
                             && flatGrid != null && flatGrid.Visibility != Visibility.Visible;
            double cardWidth = cardGrid?.ActualWidth ?? 0;

            Console.WriteLine($"[{(flatState && cardState && cardWidth > 500 ? "PASS" : "FAIL")}] "
                              + $"卡片式开关：切前表格可见={flatState}，切后卡片可见={cardState}，"
                              + $"卡片网格实际宽={cardWidth:F0}（期望 >500，即参数列已 360→540）");

            // 两种状态各出一张图（视图真渲染，方便肉眼核对三栏重排）
            layoutSwitch.UseCardLayout = false;
            host.UpdateLayout();
            ShowAndCapture(host, "preprocess_flat.png");

            layoutSwitch.UseCardLayout = true;
            host.UpdateLayout();
            ShowAndCapture(host, "preprocess_card.png");
        }

        private static T? FindChild<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T hit) return hit;
                var deep = FindChild<T>(child);
                if (deep != null) return deep;
            }
            return null;
        }

        /// <summary>模拟"用户敲完离开"：提交走的是 LostKeyboardFocus → Commit()</summary>
        private static void CommitByLosingFocus(UI.CustomControl.NumericBox box)
        {
            var args = new System.Windows.Input.KeyboardFocusChangedEventArgs(
                System.Windows.Input.Keyboard.PrimaryDevice, Environment.TickCount, box, box)
            {
                RoutedEvent = System.Windows.Input.Keyboard.LostKeyboardFocusEvent,
            };
            box.RaiseEvent(args);
        }

        /// <summary>
        /// NumericBox 的行为断言：钳制（外部设值 / 敲出来 / 改范围三条路）、解析、非法输入回退、
        /// 以及"属性网格里的数值属性确实换成了 NumericBox"（生成器接线）。
        /// </summary>
        private static void CheckNumericBox()
        {
            int failed = 0;
            void Check(string name, bool ok, string detail)
            {
                Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}  {detail}");
                if (!ok) failed++;
            }

            // ① 外部设值越界 → 由 CoerceValue 夹回来
            var clamped = new UI.CustomControl.NumericBox { Minimum = 0, Maximum = 100 };
            clamped.Value = 999;
            double high = clamped.Value;
            clamped.Value = -5;
            double low = clamped.Value;
            Check("钳制：设 999 → 100、设 -5 → 0（外部赋值也走同一道夹子）",
                Math.Abs(high - 100) < 1e-9 && Math.Abs(low) < 1e-9, $"high={high} low={low}");

            // ② 敲进去越界 → 提交时夹
            var typed = new UI.CustomControl.NumericBox { Minimum = 0, Maximum = 100, Value = 10 };
            typed.Text = "999";
            CommitByLosingFocus(typed);
            Check("钳制：敲 999 提交 → 100", Math.Abs(typed.Value - 100) < 1e-9, $"Value={typed.Value}");

            // ③ 正常解析 + 提交后归位（小数点分隔符不是 '.' 的机器上也认 "12.5"）
            var parsed = new UI.CustomControl.NumericBox();
            parsed.Text = "12.5";
            CommitByLosingFocus(parsed);
            Check("解析：\"12.5\" → 12.5", Math.Abs(parsed.Value - 12.5) < 1e-9, $"Value={parsed.Value}");

            // ④ 非法输入：值不动 + 标红 + 文本退回
            var invalid = new UI.CustomControl.NumericBox { Value = 7 };
            invalid.Text = "abc";
            CommitByLosingFocus(invalid);
            Check("非法输入：值不动（7）、IsInvalid=true、文本退回",
                Math.Abs(invalid.Value - 7) < 1e-9 && invalid.IsInvalid && invalid.Text == "7",
                $"Value={invalid.Value} IsInvalid={invalid.IsInvalid} Text='{invalid.Text}'");

            // ⑤ 小数位：1.5 固定 2 位 → "1.50"
            var decimals = new UI.CustomControl.NumericBox { DecimalPlaces = 2 };
            decimals.Value = 1.5;
            Check("小数位：Value=1.5 / DecimalPlaces=2 → 文本 \"1.50\"", decimals.Text == "1.50", $"Text='{decimals.Text}'");

            // ⑥ 生成器接线：属性网格里的 double/int 属性必须已经换成 NumericBox。
            //    注意 Tab 内容是懒加载的（选中才建），所以要先翻到第二个分组页再数，
            //    否则只看到当前页那几个。
            var grid = new UI.CustomControl.FlatPropertyGrid { BindingObject = new ParamSample() };
            var host = new Border { Width = 460, Height = 320, Child = grid };
            host.Measure(new Size(460, 320));
            host.Arrange(new Rect(0, 0, 460, 320));
            host.UpdateLayout();
            host.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            var tabs = Descendants<TabControl>(grid).FirstOrDefault();
            if (tabs != null && tabs.Items.Count > 1)
            {
                tabs.SelectedIndex = tabs.Items.Count - 1;   // 翻到"编辑框"页
                host.UpdateLayout();
                host.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            }

            var boxes = Descendants<UI.CustomControl.NumericBox>(grid).ToList();
            Check("生成器接线：FlatPropertyGrid 里的数值属性渲染为 NumericBox", boxes.Count >= 4,
                $"找出 {boxes.Count} 个 NumericBox（翻到第二个分组页后）");

            // ⑦ 范围来自既有的 [RangeValidation]：样例里的"框宽度"带 [200,5000]，框上必须拿到
            var ranged = boxes.FirstOrDefault(b => Math.Abs(b.Maximum - 5000) < 1e-9);
            Check("[RangeValidation] → 数值框自动带上上下限", ranged != null,
                ranged == null ? "没有框拿到 [200, 5000] 范围" : $"Minimum={ranged.Minimum} Maximum={ranged.Maximum}");

            if (failed > 0)
                Console.WriteLine($"### NumericBox 断言失败 {failed} 项");
        }

        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T hit) yield return hit;
                foreach (var deep in Descendants<T>(child)) yield return deep;
            }
        }

        /// <summary>NumericBox 的状态样式图（普通 / 单位 / 满量程 / 只读 / 非法 / 固定小数位）</summary>
        private static void CaptureNumericBox()
        {
            var panel = new StackPanel
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF4, 0xF8)),
                Width = 560,
            };

            void Row(string title, UI.CustomControl.NumericBox box, string? note = null)
            {
                var line = new Grid { Margin = new Thickness(16, 6, 16, 0) };
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var label = new TextBlock
                {
                    Text = title,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x64, 0x72)),
                };
                var tip = new TextBlock
                {
                    Text = note ?? string.Empty,
                    FontSize = 11,
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x94, 0xA6)),
                };
                Grid.SetColumn(label, 0);
                Grid.SetColumn(box, 1);
                Grid.SetColumn(tip, 2);
                line.Children.Add(label);
                line.Children.Add(box);
                line.Children.Add(tip);
                panel.Children.Add(line);
            }

            Row("普通（小数）", new UI.CustomControl.NumericBox { Value = 0.5 });
            Row("带单位", new UI.CustomControl.NumericBox { Value = 3000, Suffix = "ms" });
            Row("满量程（范围 0~100）", new UI.CustomControl.NumericBox { Minimum = 0, Maximum = 100, Value = 99 },
                "再往上敲会被夹回 100");
            Row("只读", new UI.CustomControl.NumericBox { Value = 12.5, IsReadOnly = true });
            Row("固定 2 位小数", new UI.CustomControl.NumericBox { Value = 1.5, DecimalPlaces = 2 });

            var bad = new UI.CustomControl.NumericBox { Value = 7 };
            bad.Text = "abc";
            CommitByLosingFocus(bad);   // 触发非法态（红边 + 值不动）
            Row("非法输入", bad, "值不动、文本退回 7、边框标红");

            var root = new Border { Background = Brushes.White, Width = 560, Height = 340, Child = panel };
            ShowAndCapture(root, "numericbox.png");
        }

        /// <summary>连接状态胶囊的假行：DialogStatusPillTemplate 绑定的是 State / HasError / LastError</summary>
        private class FakeConnectionRow
        {
            public VisionMaster.ConnectionState State { get; set; }
            public bool HasError { get; set; }
            public string LastError { get; set; } = string.Empty;
        }

        /// <summary>把控件塞进宿主测一遍，让模板应用（之后 FindName / 可视树才拿得到模板部件）</summary>
        private static T ApplyTemplateOf<T>(T control) where T : FrameworkElement
        {
            var host = new Border { Width = 260, Height = 40, Child = control };
            host.Measure(new Size(260, 40));
            host.Arrange(new Rect(0, 0, 260, 40));
            host.UpdateLayout();
            return control;
        }

        /// <summary>
        /// StatusIndicator（连接状态灯）断言：
        /// ① 四档颜色落到圆点上；② 空文案只留圆点；③ Detail 空白不挂 ToolTip；
        /// ④ 真模板 DialogStatusPillTemplate × 五种 ConnectionState → 档位与文案都对（这条覆盖整条映射链）。
        /// </summary>
        private static void CheckStatusIndicator()
        {
            int failed = 0;
            void Check(string name, bool ok, string detail)
            {
                Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}  {detail}");
                if (!ok) failed++;
            }

            // ① 四档 → 圆点颜色
            var cases = new (UI.CustomControl.StatusLevel Level, string Color)[]
            {
                (UI.CustomControl.StatusLevel.Off, "#FF98A2B3"),
                (UI.CustomControl.StatusLevel.Ok, "#FF16A34A"),
                (UI.CustomControl.StatusLevel.Busy, "#FFD97706"),
                (UI.CustomControl.StatusLevel.Error, "#FFDC2626"),
            };
            var got = new List<string>();
            foreach (var (level, want) in cases)
            {
                var lamp = ApplyTemplateOf(new UI.CustomControl.StatusIndicator { Level = level, StatusText = "x" });
                var dot = lamp.Template.FindName("Dot", lamp) as Ellipse;
                got.Add((dot?.Fill as SolidColorBrush)?.Color.ToString() ?? "(无圆点)");
            }
            Check("四档颜色：Off/Ok/Busy/Error → 灰/绿/橙/红",
                string.Join(",", got) == string.Join(",", cases.Select(c => c.Color)),
                string.Join(" ", got));

            // ② 空文案 → 只留圆点
            var bare = ApplyTemplateOf(new UI.CustomControl.StatusIndicator { Level = UI.CustomControl.StatusLevel.Ok });
            var label = bare.Template.FindName("Label", bare) as TextBlock;
            Check("空文案时文字整段收起（只留圆点）",
                label != null && label.Visibility == Visibility.Collapsed, $"Label.Visibility={label?.Visibility}");

            // ③ Detail → ToolTip
            var withDetail = new UI.CustomControl.StatusIndicator { Detail = "连接超时（3000ms）" };
            var blankDetail = new UI.CustomControl.StatusIndicator { Detail = "   " };
            Check("Detail：非空挂 ToolTip、全空白不挂（避免弹空框）",
                (withDetail.ToolTip as string) == "连接超时（3000ms）" && blankDetail.ToolTip == null,
                $"非空='{withDetail.ToolTip}' 空白={(blankDetail.ToolTip == null ? "null" : "有")}");

            // ④ 真模板 × 五种状态
            var template = Application.Current.TryFindResource("DialogStatusPillTemplate") as DataTemplate;
            if (template == null)
            {
                Check("取到 DialogStatusPillTemplate", false, "资源里找不到该模板");
            }
            else
            {
                var rows = new (VisionMaster.ConnectionState State, string Text, UI.CustomControl.StatusLevel Level)[]
                {
                    (VisionMaster.ConnectionState.Disconnected, "离线", UI.CustomControl.StatusLevel.Off),
                    (VisionMaster.ConnectionState.Connecting, "连接中", UI.CustomControl.StatusLevel.Busy),
                    (VisionMaster.ConnectionState.Connected, "在线", UI.CustomControl.StatusLevel.Ok),
                    (VisionMaster.ConnectionState.Reconnecting, "重连中", UI.CustomControl.StatusLevel.Busy),
                    (VisionMaster.ConnectionState.Error, "错误", UI.CustomControl.StatusLevel.Error),
                };

                var notes = new List<string>();
                bool allOk = true;
                foreach (var (state, wantText, wantLevel) in rows)
                {
                    var presenter = new ContentControl
                    {
                        Content = new FakeConnectionRow { State = state },
                        ContentTemplate = template,
                    };
                    ApplyTemplateOf(presenter);

                    var lamp = Descendants<UI.CustomControl.StatusIndicator>(presenter).FirstOrDefault();
                    var texts = Descendants<TextBlock>(presenter).Select(t => t.Text ?? string.Empty).ToList();
                    bool ok = lamp != null && lamp.Level == wantLevel && texts.Contains(wantText);
                    allOk &= ok;
                    notes.Add($"{state}→{(ok ? "OK" : "错")}(档位={(lamp == null ? "-" : lamp.Level.ToString())},文案={(texts.Contains(wantText) ? wantText : string.Join("/", texts.Where(s => s.Length > 0)))})");
                }

                Check("真模板 × 五种 ConnectionState：档位与文案都对", allOk, string.Join(" ", notes));

                // ⑤ 有错误文案时模板把 Detail 灌给灯（悬停才看得见，这里断言属性到位）
                var errPresenter = new ContentControl
                {
                    Content = new FakeConnectionRow { State = VisionMaster.ConnectionState.Error, HasError = true, LastError = "连接超时" },
                    ContentTemplate = template,
                };
                ApplyTemplateOf(errPresenter);
                var errLamp = Descendants<UI.CustomControl.StatusIndicator>(errPresenter).FirstOrDefault();
                Check("HasError 时模板把 LastError 映射到灯的 Detail（悬浮详情）",
                    errLamp != null && errLamp.Detail == "连接超时" && (errLamp.ToolTip as string) == "连接超时",
                    $"Detail='{errLamp?.Detail}' ToolTip='{errLamp?.ToolTip}'");
            }

            if (failed > 0) Console.WriteLine($"### StatusIndicator 断言失败 {failed} 项");
        }

        /// <summary>连接状态胶囊的样式参考图（五种状态一行一条；Busy 的脉动在静态图里关掉，免得停在半透明那一帧）</summary>
        private static void CaptureConnectionStatus()
        {
            var template = Application.Current.TryFindResource("DialogStatusPillTemplate") as DataTemplate;
            if (template == null)
            {
                Console.WriteLine("[SKIP] connection_status：找不到 DialogStatusPillTemplate");
                return;
            }

            var panel = new StackPanel { Background = Brushes.White, Width = 460 };

            void Row(string title, VisionMaster.ConnectionState state, bool hasError = false, string error = "")
            {
                var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 9, 0, 0) };
                line.Children.Add(new TextBlock
                {
                    Text = title,
                    Width = 190,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x64, 0x72)),
                });
                line.Children.Add(new ContentControl
                {
                    Content = new FakeConnectionRow { State = state, HasError = hasError, LastError = error },
                    ContentTemplate = template,
                });
                panel.Children.Add(line);
            }

            Row("Disconnected（离线）", VisionMaster.ConnectionState.Disconnected);
            Row("Connecting（连接中，运行时脉动）", VisionMaster.ConnectionState.Connecting);
            Row("Connected（在线）", VisionMaster.ConnectionState.Connected);
            Row("Reconnecting（重连中，运行时脉动）", VisionMaster.ConnectionState.Reconnecting);
            Row("Error + 错误详情（悬停看详情）", VisionMaster.ConnectionState.Error, true, "连接超时（3000ms），第 2 次重试失败");

            var root = new Border { Background = Brushes.White, Width = 460, Height = 250, Child = panel };
            root.Measure(new Size(460, 250));
            root.Arrange(new Rect(0, 0, 460, 250));
            root.UpdateLayout();

            // 静态图：关掉脉动（否则 Busy 那两行可能停在半透明帧上）
            foreach (var lamp in Descendants<UI.CustomControl.StatusIndicator>(root))
                lamp.IsPulsing = false;
            root.UpdateLayout();

            ShowAndCapture(root, "connection_status.png");
        }

        /// <summary>
        /// A 项落点：BlobDetect 视图的 19 个手写数值 TextBox 已换成 ui:NumericBox
        /// （样式仍用平台的 PluginNumericBox —— 它是 TextBox 样式、TargetType 是基类，套在 NumericBox 上一样成立）。
        /// 这里渲染真视图核对：数量、区间端点，并出一张图。
        /// </summary>
        private static void CheckBlobDetectNumericBoxes()
        {
            int failed = 0;
            void Check(string name, bool ok, string detail)
            {
                Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}  {detail}");
                if (!ok) failed++;
            }

            var plugin = new Plugin.BlobDetect.BlobDetectPlugin();
            var view = new Plugin.BlobDetect.BlobDetectView { DataContext = plugin };

            var host = new Border { Width = 980, Height = 720, Background = Brushes.White, Child = view };
            host.Measure(new Size(980, 720));
            host.Arrange(new Rect(0, 0, 980, 720));
            host.UpdateLayout();
            host.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            var boxes = Descendants<UI.CustomControl.NumericBox>(view).ToList();

            bool grayRange = boxes.Any(b => Math.Abs(b.Minimum) < 1e-9 && Math.Abs(b.Maximum - 65535) < 1e-9);
            bool unitRange = boxes.Any(b => Math.Abs(b.Minimum) < 1e-9 && Math.Abs(b.Maximum - 1) < 1e-9);
            // 整数参数（原 IntegerOnly=True 的那几个）应固定 0 位小数
            int integerBoxes = boxes.Count(b => b.DecimalPlaces == 0);

            Check("BlobDetect 数值框已换成 NumericBox 且区间/整数位对得上",
                boxes.Count >= 19 && grayRange && unitRange && integerBoxes >= 5,
                $"NumericBox={boxes.Count}（期望 ≥19），灰度区间 [0,65535] 命中={grayRange}，"
                + $"圆度区间 [0,1] 命中={unitRange}，固定整数位={integerBoxes}（期望 ≥5）");

            // ── 红框事故的守门人（2026-10-10 加）──
            // 绑定目标从 Text 换成 Value（double）之后，校验规则收到的不再是字符串。
            // 规则若只认字符串，就会对每个字段判"不能为空"：界面全红 + 用户输入被拒绝写回源。
            // 只数控件个数是抓不到它的 —— 必须验数据通路。
            bool noValidationError = boxes.All(b => !System.Windows.Controls.Validation.GetHasError(b));

            var minGrayBox = boxes.FirstOrDefault(b =>
                System.Windows.Data.BindingOperations
                    .GetBindingExpression(b, UI.CustomControl.NumericBox.ValueProperty)
                    ?.ParentBinding?.Path?.Path == "MinGray");

            bool wroteBack = false;
            bool stillClean = false;
            if (minGrayBox != null)
            {
                minGrayBox.Value = 5;   // 等价于"用户把它改成 5"
                host.UpdateLayout();
                host.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                wroteBack = Math.Abs(plugin.MinGray - 5) < 1e-9;
                stillClean = !System.Windows.Controls.Validation.GetHasError(minGrayBox);
            }

            Check("数值框写入能落到插件参数、且全程无校验错误（红框事故守门人）",
                noValidationError && minGrayBox != null && wroteBack && stillClean,
                $"初始无校验错误={noValidationError}；MinGray 框={minGrayBox != null}，"
                + $"写入后插件 MinGray={plugin.MinGray}，HasError={!stillClean}");

            if (failed > 0) Console.WriteLine($"### BlobDetect 数值框断言失败 {failed} 项");

            ShowAndCapture(host, "blobdetect_numeric.png");
        }

        /// <summary>
        /// 迁移批的三个插件视图（Matching 12 / Calibration 12 / CaliperMeasure 10）：
        /// 与 BlobDetect 同一套守门口径 —— 框数、**无校验错误**、**写入能落到插件参数**。
        /// 三种块形状都要过：带规则的（Matching 用 Core.Controls 的共享规则 / CaliperMeasure 用自带规则）、
        /// 不带规则的（Calibration 的 12 处）。
        /// 框数只做下限断言：部分块在"选中某个模板项/条目"后才可见（折叠子树不进可视树），
        /// 真正的闸是"写入落到插件参数"那一条。
        /// </summary>
        private static void CheckMigratedPluginViews()
        {
            int failed = 0;
            void Check(string name, bool ok, string detail)
            {
                Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}  {detail}");
                if (!ok) failed++;
            }

            var matching = new Plugin.Matching.MatchingPlugin();
            var calibration = new Plugin.Calibration.CalibrationPlugin();
            var caliper = new Plugin.CaliperMeasure.CaliperMeasurePlugin();

            var cases = new (string Name, FrameworkElement View, int MinBoxes, string Path, double Write, Func<double> Read)[]
            {
                // 探针路径要选"所在分区默认可见"的：Matching 的 BrushSize 在涂抹分区里（默认折叠 → 不进可视树）
                ("Matching", new Plugin.Matching.MatchingView { DataContext = matching },
                    5, "MinScore", 0.7, () => matching.MinScore),
                ("Calibration", new Plugin.Calibration.CalibrationView { DataContext = calibration },
                    4, "KnownLengthMm", 25, () => calibration.KnownLengthMm),
                ("CaliperMeasure", new Plugin.CaliperMeasure.CaliperMeasureView { DataContext = caliper },
                    4, "CaliperCount", 7, () => caliper.CaliperCount),
            };

            foreach (var c in cases)
            {
                var host = new Border { Width = 1000, Height = 700, Background = Brushes.White, Child = c.View };
                host.Measure(new Size(1000, 700));
                host.Arrange(new Rect(0, 0, 1000, 700));
                host.UpdateLayout();
                host.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                var boxes = Descendants<UI.CustomControl.NumericBox>(c.View).ToList();
                bool clean = boxes.All(b => !System.Windows.Controls.Validation.GetHasError(b));

                // 每个框的绑定路径（诊断用：哪几个框没渲染出来，一眼能看出是不是折叠分区）
                var paths = boxes
                    .Select(b => System.Windows.Data.BindingOperations
                        .GetBindingExpression(b, UI.CustomControl.NumericBox.ValueProperty)?.ParentBinding?.Path?.Path)
                    .Where(p => p != null)
                    .ToList();

                var target = boxes.FirstOrDefault(b => System.Windows.Data.BindingOperations
                    .GetBindingExpression(b, UI.CustomControl.NumericBox.ValueProperty)
                    ?.ParentBinding?.Path?.Path == c.Path);

                double before = c.Read();
                if (target != null)
                {
                    target.Value = c.Write;   // 等价于"用户改成这个值"
                    host.UpdateLayout();
                    host.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                }

                double after = c.Read();
                bool ok = boxes.Count >= c.MinBoxes && clean && target != null && Math.Abs(after - c.Write) < 1e-9;
                Check($"{c.Name}：数值框已迁移、无校验错误、写入 {c.Path}={c.Write} 能落到插件",
                    ok,
                    $"框数={boxes.Count}（≥{c.MinBoxes}），无校验错误={clean}，找到 {c.Path} 框={target != null}，"
                    + $"写前={before} 写后={after}；已渲染路径=[{string.Join(",", paths)}]");
            }

            if (failed > 0) Console.WriteLine($"### 迁移批断言失败 {failed} 项");
        }

        /// <summary>SCADA 属性面板"数值行"的假数据：模板绑定的是 Value（编辑缓冲）/ IsValid / ErrorText</summary>
        private class FakeScadaRow
        {
            public string Value { get; set; } = string.Empty;
            public bool IsValid { get; set; } = true;
            public string ErrorText { get; set; } = string.Empty;
        }

        /// <summary>
        /// SCADA 属性面板的数值编辑器（EditNumber）：从真视图的资源里取模板，用假行数据渲染，
        /// 核对"渲染成 NumericBox / 文本接编辑缓冲 / 非法文本不退回 / IsValid=False 标红"。
        /// </summary>
        private static void CheckScadaNumberEditor()
        {
            int failed = 0;
            void Check(string name, bool ok, string detail)
            {
                Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}  {detail}");
                if (!ok) failed++;
            }

            DataTemplate template = null;
            try
            {
                var view = new VisionMaster.Views.ScadaPropertyView();

                // 编辑器模板挂在**内层 Grid.Resources**（不是 UserControl.Resources）——
                // view.FindResource 取不到那种作用域，得把树量一遍、按"资源字典里有这个键"找宿主元素
                var host = new Border { Width = 300, Height = 200, Child = view };
                host.Measure(new Size(300, 200));
                host.Arrange(new Rect(0, 0, 300, 200));
                host.UpdateLayout();

                template = view.Resources.Contains("EditNumber") ? view.Resources["EditNumber"] as DataTemplate : null;
                if (template == null)
                {
                    template = Descendants<FrameworkElement>(view)
                        .Where(fe => fe != null && fe.Resources.Contains("EditNumber"))
                        .Select(fe => fe.Resources["EditNumber"] as DataTemplate)
                        .FirstOrDefault(t => t != null);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"        （SCADA 属性视图实例化失败：{ex.GetType().Name}: {ex.Message}）");
            }

            if (template == null)
            {
                Check("取到 SCADA 的 EditNumber 模板", false, "视图资源里找不到该键");
                return;
            }

            Border Render(object row)
            {
                var host = new ContentControl { Content = row, ContentTemplate = template };
                var border = new Border { Width = 260, Height = 40, Child = host };
                border.Measure(new Size(260, 40));
                border.Arrange(new Rect(0, 0, 260, 40));
                border.UpdateLayout();
                return border;
            }

            var good = Render(new FakeScadaRow { Value = "23.5" });
            var box = Descendants<UI.CustomControl.NumericBox>(good).FirstOrDefault();
            Check("EditNumber 渲染为 NumericBox，且文本接上行 VM 的 Value（编辑缓冲）",
                box != null && box.Text == "23.5", $"NumericBox={box != null}，Text='{box?.Text}'");
            var revertText = box == null ? "（没渲染出控件）" : box.RevertOnInvalid.ToString();
            Check("EditNumber 用 RevertOnInvalid=False（非法文本留给行 VM 报错，不退回）",
                box != null && !box.RevertOnInvalid, $"RevertOnInvalid={revertText}");

            var bad = Render(new FakeScadaRow { Value = "abc", IsValid = false, ErrorText = "必须是数字" });
            var badBox = Descendants<UI.CustomControl.NumericBox>(bad).FirstOrDefault();
            var danger = Color.FromRgb(0xE0, 0x5B, 0x5B);   // ScadaDangerBrush
            Check("行 VM 判非法（IsValid=False）时数值框标红",
                badBox != null && (badBox.BorderBrush as SolidColorBrush)?.Color == danger,
                $"BorderBrush={(badBox?.BorderBrush as SolidColorBrush)?.Color.ToString() ?? "null"}");

            if (failed > 0) Console.WriteLine($"### SCADA 数值编辑器断言失败 {failed} 项");
        }

        /// <summary>与 BoxOperatorBase 同形的样例：4 个参数同组、ColSpan=6（12 栅格里两个一行）</summary>
        private class ParamSample
        {
            [UI.Attributes.SuperDisplay(Name = "启用", Group = new[] { "算子" }, GroupOrder = "0", Order = 0)]
            public bool Enabled { get; set; } = true;

            [UI.Attributes.SuperDisplay(Name = "阈值", Group = new[] { "算子" }, GroupOrder = "0", Order = 1)]
            public double Threshold { get; set; } = 0.5;

            [UI.Attributes.SuperDisplay(Name = "中心X", Group = new[] { "编辑框" }, Order = 1, ColSpan = 6)]
            public double CenterX { get; set; } = 320.5;

            [UI.Attributes.SuperDisplay(Name = "中心Y", Group = new[] { "编辑框" }, Order = 2, ColSpan = 6)]
            public double CenterY { get; set; } = 256.25;

            [UI.Attributes.SuperDisplay(Name = "框宽度", Group = new[] { "编辑框" }, Order = 3, ColSpan = 6)]
            [UI.Attributes.RangeValidation(200, 5000)]
            public double BoxWidth { get; set; } = 120;

            [UI.Attributes.SuperDisplay(Name = "框高度", Group = new[] { "编辑框" }, Order = 4, ColSpan = 6)]
            public double BoxHeight { get; set; } = 80;
        }

        /// <summary>表格 vs 卡片，在参数列真实宽度（360）与放宽后（460）下各渲染一份</summary>
        private static void CaptureGridLayouts()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.White };

            foreach (var (title, card, width) in new (string, bool, double)[]
                     {
                         ("表格 Flat / 360 宽（现状）", false, 360),
                         ("卡片 Card / 540 宽（切卡片后参数列）", true, 540),
                         ("卡片 Card / 360 宽（不改列宽会怎样）", true, 360),
                     })
            {
                UI.CustomControl.PropertyGridBase grid = card
                    ? new UI.CustomControl.CardPropertyGrid { BindingObject = new ParamSample() }
                    : new UI.CustomControl.FlatPropertyGrid { BindingObject = new ParamSample() };

                var host = new Border
                {
                    Width = width,
                    Height = 300,
                    Margin = new Thickness(10),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0xD7, 0xDE)),
                    BorderThickness = new Thickness(1),
                    Child = grid,
                };

                var col = new StackPanel { Margin = new Thickness(4) };
                col.Children.Add(new TextBlock
                {
                    Text = title,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x64, 0x72)),
                    Margin = new Thickness(4, 4, 0, 4),
                });
                col.Children.Add(host);
                row.Children.Add(col);

                // 卡片 540 那张把可视树尺寸打出来：460 的渲染里左边有大片空白，要看清它到底哪里占着
                if (card && Math.Abs(width - 540) < 0.1)
                {
                    host.Measure(new Size(width, 300));
                    host.Arrange(new Rect(0, 0, width, 300));
                    host.UpdateLayout();
                    host.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    var sb = new StringBuilder();
                    Walk(grid, 0, sb, stopAtConsole: false);
                    Console.WriteLine("---- 卡片式 540 宽的可视树 ----");
                    Console.WriteLine(sb.ToString());
                }
            }

            var root = new Border { Background = Brushes.White, Width = 1320, Height = 340, Child = row };
            ShowAndCapture(root, "pg_layouts.png");
        }

        /// <summary>
        /// 塞 8 条通知，最多留 5 条、且丢的是最旧的。
        /// Show 内部是 InvokeAsync(async …)，但"Add + 裁剪"在第一个 await 之前，泵一次即可。
        /// </summary>
        private static void CheckNotifierCap()
        {
            UI.CustomControl.Notifier.Messages.Clear();
            for (int i = 0; i < 8; i++)
                UI.CustomControl.Notifier.ShowWarning("storm " + i);

            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            int count = UI.CustomControl.Notifier.Messages.Count;
            string last = count > 0 ? UI.CustomControl.Notifier.Messages[count - 1].Message : "(empty)";
            bool ok = count == 5 && last == "storm 7";
            Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] Notifier 上限：塞 8 条 → 可见 {count} 条（期望 5），最后一条={last}（期望 storm 7）");

            UI.CustomControl.Notifier.Messages.Clear();   // 别把测试卡片留在界面上
        }

        private static readonly Color InfoColor = Color.FromRgb(0x00, 0x78, 0xD4);
        private static readonly Color SuccessColor = Color.FromRgb(0x10, 0x7C, 0x10);
        private static readonly Color WarningColor = Color.FromRgb(0xD8, 0x3B, 0x01);
        private static readonly Color ErrorColor = Color.FromRgb(0xD1, 0x34, 0x38);

        private static void CaptureVariants()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF4, 0xF8)),
                Margin = new Thickness(0),
            };

            void Title(string text) => panel.Children.Add(new TextBlock
            {
                Text = text,
                Margin = new Thickness(14, 12, 0, 6),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x64, 0x72)),
            });

            void Row(params UIElement[] chips)
            {
                var wrap = new WrapPanel { Margin = new Thickness(14, 0, 14, 2) };
                foreach (var c in chips) wrap.Children.Add(c);
                panel.Children.Add(wrap);
            }

            Title("A 纯筹码（单选，无计数）");
            Row(
                Chip("全部", null, 0, selected: true),
                Chip("信息", null, 0, false),
                Chip("成功", null, 0, false),
                Chip("警告", null, 0, false),
                Chip("错误", null, 0, false));

            Title("B 等级色圆点 + 计数（单选）← 推荐：一眼看出有没有错误");
            Row(
                Chip("全部", null, 128, selected: false),
                Chip("信息", InfoColor, 90, false),
                Chip("成功", SuccessColor, 20, false),
                Chip("警告", WarningColor, 15, false),
                Chip("错误", ErrorColor, 3, selected: true));

            Title("C 多选（示例：信息 + 错误 同时看；需要控件先支持按等级集合过滤）");
            Row(
                Chip("信息", InfoColor, 90, selected: true),
                Chip("成功", SuccessColor, 20, false),
                Chip("警告", WarningColor, 15, false),
                Chip("错误", ErrorColor, 3, selected: true),
                Chip("全部", null, 128, selected: false));

            var root = new Border { Background = Brushes.White, Width = 900, Height = 252, Child = panel };
            ShowAndCapture(root, "levelfilter_variants.png");
        }

        /// <summary>筹码：dot 为等级色圆点（null = 不画），count &gt; 0 时右侧显示计数</summary>
        private static Border Chip(string text, Color? dot, int count, bool selected)
        {
            var accent = dot ?? Color.FromRgb(0x00, 0x5F, 0xB8);
            var inner = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            if (dot.HasValue)
            {
                inner.Children.Add(new Border
                {
                    Width = 7,
                    Height = 7,
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(selected ? Colors.White : accent),
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            inner.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 12,
                Foreground = selected ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x4A, 0x55, 0x68)),
                VerticalAlignment = VerticalAlignment.Center,
            });

            if (count > 0)
                inner.Children.Add(new TextBlock
                {
                    Text = " " + count,
                    FontSize = 11,
                    Foreground = selected ? new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF)) : new SolidColorBrush(Color.FromRgb(0x8A, 0x94, 0xA6)),
                    VerticalAlignment = VerticalAlignment.Center,
                });

            return new Border
            {
                Margin = new Thickness(0, 0, 6, 4),
                Padding = new Thickness(9, 4, 9, 4),
                CornerRadius = new CornerRadius(11),
                Background = selected ? new SolidColorBrush(accent) : Brushes.White,
                BorderBrush = selected ? new SolidColorBrush(accent) : new SolidColorBrush(Color.FromRgb(0xD0, 0xD7, 0xDE)),
                BorderThickness = new Thickness(1),
                Child = inner,
            };
        }

        private static VisionMaster.ViewModels.LogViewModel BuildVm()
        {
            var vm = new VisionMaster.ViewModels.LogViewModel(null);
            // 真机上的日志：Source 是 null（LogService.PublishLog 从没传过 source）。
            // 这里刻意两种都造：null 的（现状）和有来源的（万一将来填上）。
            vm.SystemLogs.Add(new LogItem(LogLevel.Info, "已加载图像: D:\\C#\\VM\\Image\\bead\\adhesive_bead_01.png (1280x1024)", null));
            vm.SystemLogs.Add(new LogItem(LogLevel.Info, "已加载图像: D:\\C#\\VM\\Image\\bead\\adhesive_bead_01.png (1280x1024)", null));
            vm.SystemLogs.Add(new LogItem(LogLevel.Warning, "标定当量缺失，按默认值 0.05mm/px 继续", null));
            vm.SystemLogs.Add(new LogItem(LogLevel.Error, "图像采集_0 取图超时（3000ms），本周期跳过", null));
            vm.SystemLogs.Add(new LogItem(LogLevel.Success, "流程 MainTask 执行完成，耗时 128ms", "MainTask"));
            vm.SystemLogs.Add(new LogItem(LogLevel.Info, "软件加载成功", "System"));
            return vm;
        }

        private static void Capture(VisionMaster.ViewModels.LogViewModel vm, double width, double height, string fileName, bool dumpToolbar)
        {
            var view = new VisionMaster.Views.LogView { DataContext = vm };
            // 探针里没有 Prism 容器，关掉自动装配，免得它把 DataContext 顶掉
            Prism.Mvvm.ViewModelLocator.SetAutoWireViewModel(view, false);

            var root = new Border { Background = Brushes.White, Width = width, Height = height, Child = view };
            ShowAndCapture(root, fileName);
            if (dumpToolbar)
            {
                Console.WriteLine("---- 工具条可视树（宽高/边距）----");
                var sv = new StringBuilder();
                Walk(view, 0, sv, stopAtConsole: true);
                Console.WriteLine(sv.ToString());
            }
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
            using var fs = File.Create(OutputDir + fileName);
            enc.Save(fs);
            Console.WriteLine("saved " + fileName);
            win.Close();
        }
    }
}
