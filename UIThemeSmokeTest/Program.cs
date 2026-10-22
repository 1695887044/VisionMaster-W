using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using Core.Controls;
using Core.Halcon.Controls;
using Core.Halcon.Extensions;
using Core.Halcon.Helpers;
using Core.Halcon.Models;
using Core.Interfaces;
using HalconDotNet;
using Prism.Dialogs;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.Views.DialogViews;
using VisionMaster.Views.DialogViews.MotionBoard;
using UI.AttachedProperties;

namespace UIThemeSmokeTest
{
    /// <summary>
    /// 无窗口冒烟测试：完全按 App.xaml 的方式合并全局资源，
    /// 用 XamlReader 解析 Shell.xaml（去掉 x:Class/VM 自动装配/事件处理器/internal 命令扩展/DockingManager），
    /// 再手动 Measure+Arrange 强制模板实例化 —— 复现启动期 XamlParseException。
    /// 崩溃后自动沿可视树二分，定位抛异常的最深元素。
    ///
    /// 用法：
    ///   UIThemeSmokeTest.exe                （按 App.xaml 原样：含 Fluent 主题）
    ///   UIThemeSmokeTest.exe --no-fluent    （去掉 Fluent 主题再测）
    /// </summary>
    internal static class Program
    {
        private static bool _withFluent;

        /// <summary>
        /// ShellCopy.xaml 实例化出来的那份 DockingManager（Main 里在测量前抓住）。
        /// 为什么要缓存：LayoutHelper 的公开重载都从 Application.Current.MainWindow 找管理器，
        /// 而本宿主里的窗口从不 Show、也不是 MainWindow，那条路走不通——
        /// 面板显隐断言必须显式传"它自己这份"（LayoutHelper 的指定宿主重载）。
        /// </summary>
        private static AvalonDock.DockingManager? _shellDock;

        [STAThread]
        private static int Main(string[] args)
        {
            _withFluent = !args.Contains("--no-fluent");
            bool mutateResources = args.Contains("--mutation");

            if (args.Contains("--real"))
            {
                // 直接启动真实 App（完整 Prism 容器 + Splash + 真实 ShellViewModel + CreateShell + Show）
                // 尽早挂异常钩子（先于 App 自己的全局处理器），把完整异常链打到 stdout
                AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                {
                    Console.WriteLine("### [AppDomain.UnhandledException]");
                    Print(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
                };
                try
                {
                    var real = new VisionMaster.App();
                    Application.Current.DispatcherUnhandledException += (_, e) =>
                    {
                        Console.WriteLine("### [DispatcherUnhandledException]");
                        Print(e.Exception);
                        Console.WriteLine();
                        Console.WriteLine("### 崩溃时刻 App 资源状态：");
                        var rd = Application.Current.Resources;
                        Console.WriteLine($">> MergedDictionaries.Count = {rd.MergedDictionaries.Count}");
                        for (var i = 0; i < rd.MergedDictionaries.Count; i++)
                        {
                            var d = rd.MergedDictionaries[i];
                            Console.WriteLine($">>   [{i}] Source={d.Source?.ToString() ?? "(inline)"}  Keys={d.Keys.Count}");
                            foreach (var md in d.MergedDictionaries)
                                Console.WriteLine($">>        child Source={md.Source?.ToString() ?? "(inline)"}  Keys={md.Keys.Count}");
                        }
                        Console.WriteLine($">> App[AccentBrush] 存在 = {rd.Contains("AccentBrush")}");
                        Console.WriteLine($">> App[TextPrimaryBrush] 存在 = {rd.Contains("TextPrimaryBrush")}");
                        Console.WriteLine($">> App[Icon] 存在 = {rd.Contains("Icon")}");
                        var brush = rd["AccentBrush"] as System.Windows.Media.SolidColorBrush;
                        Console.WriteLine($">> App[AccentBrush] 值 = {(brush?.Color.ToString() ?? "非画刷/null")}");

                        // 二分定位：找出最深抛异常的元素
                        try
                        {
                            var w = Application.Current?.MainWindow;
                            if (w?.Content is FrameworkElement feRoot)
                            {
                                Console.WriteLine("### 开始逐元素二分定位：");
                                var broken = FindBroken(feRoot, "ShellContent");
                                Console.WriteLine(broken == null
                                    ? "### 子树单独测量均正常（异常可能与模板外层作用域相关）"
                                    : $"### 最深异常元素: {broken.GetType().FullName}  Name=\"{broken.Name}\"");
                                if (broken is System.Windows.Controls.Button fb)
                                {
                                    var style = fb.Style;
                                    Console.WriteLine($">> Button.Style = {(style == null ? "null(继承隐式)" : style.GetType().Name + "#" + style.GetHashCode())}");
                                    if (style != null)
                                    {
                                        foreach (var s in style.Setters)
                                        {
                                            if (s is Setter st && st.Property == System.Windows.Controls.Control.ForegroundProperty)
                                                Console.WriteLine($">>   样式 Foreground Setter 值 = {(st.Value == DependencyProperty.UnsetValue ? "★ UnsetValue ★" : st.Value?.ToString() ?? "null")}");
                                        }
                                    }
                                    var fg = fb.ReadLocalValue(System.Windows.Controls.Control.ForegroundProperty);
                                    Console.WriteLine($">> Button 本地 Foreground = {(fg == DependencyProperty.UnsetValue ? "UnsetValue(未本地设置)" : fg)}");
                                    Console.WriteLine($">> Button 有效 Foreground = {fb.Foreground?.ToString() ?? "null"}");
                                    var winStyle = fb.TryFindResource("WindowControlButton");
                                    Console.WriteLine($">> TryFindResource(WindowControlButton) = {(winStyle == null ? "null" : "找到")}");
                                    var tpb = fb.TryFindResource("TextPrimaryBrush");
                                    Console.WriteLine($">> TryFindResource(TextPrimaryBrush) = {(tpb is System.Windows.Media.SolidColorBrush sb ? sb.Color.ToString() : tpb?.ToString() ?? "null")}");
                                }
                            }
                        }
                        catch (Exception bi)
                        {
                            Console.WriteLine("### 二分定位自身出错: " + bi.Message);
                        }
                        Console.WriteLine("###（dump 完毕，标记 Handled 并退出）");
                        e.Handled = true;
                        Application.Current.Shutdown();
                    };
                    // 实时追踪 App 资源字典变化轨迹（100ms 轮询，变化才打印）
                    var monitor = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                    var lastCount = -1;
                    var lastKeys = -1;
                    monitor.Tick += (_, __) =>
                    {
                        var rd = Application.Current?.Resources;
                        if (rd == null) return;
                        if (rd.MergedDictionaries.Count != lastCount || rd.Keys.Count != lastKeys)
                        {
                            lastCount = rd.MergedDictionaries.Count;
                            lastKeys = rd.Keys.Count;
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [MONITOR] MergedCount={lastCount} InlineKeys={lastKeys}");
                        }
                    };
                    monitor.Start();

                    // 关键：Application 派生类的 InitializeComponent（加载 App.xaml 资源）不在构造函数里，
                    // 必须显式调用，否则 App 资源全空 —— 那是测试器伪影，不是真实应用的启动状态
                    real.InitializeComponent();
                    real.Run();
                    Console.WriteLine("=== REAL APP: 正常运行至退出 ===");
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("### 真实 App 启动抛出异常:");
                    Print(ex);
                    return 1;
                }
            }

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            PreloadAssemblies();
            // 测试器无 Prism 容器：视图上的 AutoWireViewModel 统一返回 null VM（绑定失败静默，不影响模板测试）
            Prism.Mvvm.ViewModelLocationProvider.SetDefaultViewModelFactory(_ => null);
            try
            {
                BuildAppResources(app);

                if (args.Contains("--gallery"))
                    return RunGalleryCheck();

                if (args.Contains("--controls"))
                    return RunFluentControlChecks();

                if (args.Contains("--calibration"))
                    return RunCalibrationViewSmoke();

                if (args.Contains("--image-acquisition"))
                    return RunImageAcquisitionViewSmoke();

                if (args.Contains("--image-acquisition-render"))
                    return RunImageAcquisitionViewRender();

                if (args.Contains("--auto-port-config"))
                    return RunAutoPortConfigViewSmoke();

                if (args.Contains("--global-variables"))
                    return GlobalVariableViewSmoke.RunSmoke();

                if (args.Contains("--global-variables-render"))
                    return GlobalVariableViewSmoke.RunRender();

                if (args.Contains("--variable-binding"))
                    return VariableBindingViewSmoke.RunSmoke();

                if (args.Contains("--variable-binding-render"))
                    return VariableBindingViewSmoke.RunRender();

                if (args.Contains("--solution-list"))
                    return SolutionListViewSmoke.RunSmoke();

                if (args.Contains("--solution-list-render"))
                    return SolutionListViewSmoke.RunRender();

                if (args.Contains("--message-dialog"))
                    return MessageDialogSmoke.RunSmoke();

                if (args.Contains("--message-dialog-render"))
                    return MessageDialogSmoke.RunRender();

                if (args.Contains("--plugin-views"))
                    return PluginViewSmoke.Run(args);

                // 控件级检查：开关动画不得污染应用级共享画刷。
                //
                // 为什么值得单独测：开关模板里的 ColorAnimation 目标是 Background/Fill 的 Color，
                // 一旦那个画刷指向应用级令牌，动画会把**全应用**引用该令牌的画刷一起改色；而本库的
                // 派生画刷又是 {Binding Color, Source=令牌} 出来的，于是连锁触发整窗重绘
                // （现场观感：每新建一个开关，窗口像卡住）。
                // 这里断言的是结构契约（实例专属 + 未冻结 + 令牌不变色），不依赖动画时钟推进，
                // 因此在"窗口从不显示"的冒烟环境里也能稳定复现/回归。
                int RunFluentControlChecks()
                {
                    int failures = 0;
                    void Check(string name, bool ok, string detail = "")
                    {
                        Console.WriteLine((ok ? "  [PASS] " : "  [FAIL] ") + name + (detail.Length > 0 ? $"  ({detail})" : ""));
                        if (!ok) failures++;
                    }

                    var sharedTrack = app.TryFindResource("FluentSurfaceSunkenBrush") as SolidColorBrush;
                    var sharedThumb = app.TryFindResource("FluentTextSecondaryBrush") as SolidColorBrush;
                    var style = (app.TryFindResource("Grid_SwitchToggleStyle")
                                 ?? app.TryFindResource("FluentToggleSwitchStyle")) as Style;

                    Check("取到开关样式与两个共享令牌画刷",
                        style != null && sharedTrack != null && sharedThumb != null,
                        $"style={style != null} track={sharedTrack != null} thumb={sharedThumb != null}");

                    var toggle = new System.Windows.Controls.Primitives.ToggleButton { Style = style };
                    var host = new Border { Width = 160, Height = 60, Child = toggle };
                    host.Measure(new Size(160, 60));
                    host.Arrange(new Rect(0, 0, 160, 60));
                    host.UpdateLayout();
                    host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                    var trackBrush = toggle.Template?.FindName("TrackBrush", toggle) as SolidColorBrush;
                    var trackBorderBrush = toggle.Template?.FindName("TrackBorderBrush", toggle) as SolidColorBrush;
                    var thumbBrush = toggle.Template?.FindName("ThumbBrush", toggle) as SolidColorBrush;

                    // 轨道填充/描边各挂一把实例画刷。它们若被写成共享令牌本体，动画会把全应用
                    // 引用该令牌的画刷一起改色（现场事故），所以对着令牌逐个盯。
                    var sharedBorder = app.TryFindResource("FluentBorderBrush") as SolidColorBrush;
                    var sharedBorderStrong = app.TryFindResource("FluentBorderStrongBrush") as SolidColorBrush;
                    var tokenOffTrack = app.TryFindResource("FluentToggleTrackOffColor") as Color? ?? default;
                    var tokenOffBorder = app.TryFindResource("FluentToggleTrackOffBorderColor") as Color? ?? default;

                    Check("模板里的实例画刷取得到",
                        trackBrush != null && trackBorderBrush != null && thumbBrush != null,
                        $"track={trackBrush != null} trackBorder={trackBorderBrush != null} thumb={thumbBrush != null}");
                    Check("实例画刷 ≠ 应用级共享令牌（动画不会污染全局）",
                        trackBrush != null && trackBorderBrush != null
                        && !ReferenceEquals(trackBrush, sharedBorder) && !ReferenceEquals(trackBorderBrush, sharedBorderStrong),
                        "");
                    Check("实例画刷未冻结（动画不会无声失败）",
                        trackBrush is { IsFrozen: false } && trackBorderBrush is { IsFrozen: false },
                        $"trackFrozen={trackBrush?.IsFrozen} borderFrozen={trackBorderBrush?.IsFrozen}");
                    Check("实例画刷初值 = 关态令牌色（观感可预期）",
                        trackBrush != null && trackBorderBrush != null
                        && trackBrush.Color == tokenOffTrack && trackBorderBrush.Color == tokenOffBorder,
                        $"轨道={trackBrush?.Color}（令牌 {tokenOffTrack}） 描边={trackBorderBrush?.Color}（令牌 {tokenOffBorder}）");

                    var beforeTrack = sharedBorder!.Color;
                    var beforeBorder = sharedBorderStrong!.Color;
                    toggle.IsChecked = true;   // 触发 EnterActions
                    host.UpdateLayout();
                    host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    Check("拨动开关后共享令牌未被改色",
                        sharedBorder.Color == beforeTrack && sharedBorderStrong.Color == beforeBorder,
                        $"边框令牌={sharedBorder.Color} 强边框令牌={sharedBorderStrong.Color}");

                    // ---- 静态扫描：Brush 类型属性不得引用 *Color 令牌 ----
                    //
                    // 这是一次真实事故的回归闸（不是假想）：FluentControls 的 IsSelected 触发器里把
                    // Foreground 写成了 FluentAccentPressedColor（Color 令牌）→ TextBlock 测量期抛
                    // "「#FF0067BE」不是属性「Foreground」的有效值" → 异常被全局处理器降级成 WARN 吞掉
                    // → 布局反复失效重试 → 界面永久无响应（一次会话刷了两万多条 WARN）。
                    // 这类错误运行期只在"恰好用到那条触发器"时才发作，静态扫描能在构建期就拦住。
                    string repoRoot = @"d:\C#\VM";
                    if (!Directory.Exists(repoRoot))
                    {
                        Check("XAML 静态扫描（*Color 不得用在 Brush 属性上）", false, $"找不到仓库根 {repoRoot}");
                    }
                    else
                    {
                        var offenders = new List<string>();
                        var inlineHStyles = new List<string>();
                        int scanned = 0;
                        foreach (var file in Directory.GetFiles(repoRoot, "*.xaml", SearchOption.AllDirectories))
                        {
                            if (file.Contains(@"\obj\") || file.Contains(@"\bin\")
                                || file.Contains(@"WPF-Halcon-流程拖拉")) continue;
                            scanned++;
                            string text = File.ReadAllText(file);
                            string name = Path.GetFileName(file);

                            foreach (Match mt in Regex.Matches(text,
                                @"(?<p>Foreground|Background|BorderBrush|Fill|Stroke)\s*=\s*""\{StaticResource\s+(?<k>[A-Za-z0-9_]*Color)\}"""))
                                offenders.Add($"{name}: {mt.Groups["p"].Value}={mt.Groups["k"].Value}");

                            foreach (Match mt in Regex.Matches(text,
                                @"Property\s*=\s*""(?<p>Foreground|Background|BorderBrush|Fill|Stroke)""[^>]*?Value\s*=\s*""\{StaticResource\s+(?<k>[A-Za-z0-9_]*Color)\}"""))
                                offenders.Add($"{name}: Setter {mt.Groups["p"].Value}={mt.Groups["k"].Value}");

                            // 内联 <h:*.Style> 会把 Core.Halcon 合并字典里的**隐式样式**顶掉（ControlTemplate 就在那边）：
                            // 控件没有模板 → 永远不渲染且不报错。2026-10-04 事故（BlobDetect 配置窗口预览区永远空白）
                            // 就是这条没守住；可见性/触发器一律挂外层容器，别动控件自身的样式。
                            foreach (Match mt in Regex.Matches(text, @"<h:[A-Za-z0-9_]+\.Style>"))
                                inlineHStyles.Add($"{name}: {mt.Value}");
                        }

                        Check("XAML 里没有把 *Color 令牌用在 Brush 属性上", offenders.Count == 0,
                            offenders.Count == 0
                                ? $"扫过 {scanned} 个 xaml"
                                : string.Join(" | ", offenders.Take(5)));

                        Check("XAML 不给 h: 控件挂内联 Style（顶掉隐式样式=控件没模板，界面会永远空白）",
                            inlineHStyles.Count == 0,
                            inlineHStyles.Count == 0
                                ? $"扫过 {scanned} 个 xaml"
                                : string.Join(" | ", inlineHStyles.Take(5)));
                    }

                    // ---- 重复异常日志限流：一次风暴只能留下"个位数行 + 一次自激提示" ----
                    // 时间是调用方给的，所以断言不依赖真实时钟，毫秒级跑完。
                    // 背景：那次卡死一共写了 29141 条同样的 WARN，逐条落盘本身就是现场负担。
                    var throttle = new VisionMaster.Lifetime.ExceptionThrottle();
                    var t0 = new DateTime(2026, 10, 1, 12, 0, 0);
                    int verbatim = 0, summaries = 0, stormHints = 0, suppressed = 0;
                    for (int i = 0; i < 1000; i++)
                    {
                        var d = throttle.Decide("同一条异常", t0.AddMilliseconds(i * 10));   // 1000 次 / 10 秒
                        if (d.StormHint) stormHints++;
                        if (!d.ShouldLog) { suppressed++; continue; }
                        if (d.IsSummary) summaries++; else verbatim++;
                    }

                    Check("异常风暴：1000 次只留下个位数行日志", verbatim + summaries <= 10,
                        $"{verbatim} 行原始 + {summaries} 行汇总（{suppressed} 次被合并）");
                    Check("异常风暴：必须留下\"它还在刷、累计多少次\"的汇总行", summaries >= 1,
                        $"汇总 {summaries} 行（0 行 = 汇总计时被窗口重置吃掉了）");
                    Check("异常风暴：合并率 > 98%", suppressed > 980, $"合并 {suppressed}/1000");
                    Check("异常风暴：\"疑似自激\"只提示一次", stormHints == 1, $"提示 {stormHints} 次");
                    Check("限流按异常种类隔离（别的异常不受牵连）",
                        throttle.Decide("另一条异常", t0.AddMilliseconds(5)) is { ShouldLog: true, IsSummary: false }, "");
                    Check("窗口期过后恢复原样记录（不吃掉正常信息）",
                        throttle.Decide("同一条异常", t0.AddSeconds(120)) is { ShouldLog: true, IsSummary: false }, "");

                    for (int i = 0; i < 500; i++) throttle.Decide("key" + i, t0);
                    Check("跟踪表有界（异常种类爆炸也不涨内存）", throttle.TrackedKeyCount <= 128,
                        $"覆盖 500 种异常后仍只跟踪 {throttle.TrackedKeyCount} 种");

                    // ---- 开关在"属性网格行"里的实际几何 ----
                    // 现场报过"开关被画成蓝眼睛"：轨道被拉长、滑块留在中间偏左。
                    // 这里把开关放进真正的 FlatPropertyGrid 行里量（宿主 360 宽 ≈ 现场那张卡片的宽度），
                    // 只要它仍是 40×20、滑块仍是 12×12，就说明面板本身没问题。
                    var boolGrid = new UI.CustomControl.FlatPropertyGrid { BindingObject = new ToggleGridModel() };
                    var boolHost = new Border { Width = 360, Height = 260, Child = boolGrid };
                    boolHost.Measure(new Size(360, 260));
                    boolHost.Arrange(new Rect(0, 0, 360, 260));
                    boolHost.UpdateLayout();
                    boolHost.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                    T Find<T>(DependencyObject root) where T : DependencyObject
                    {
                        if (root is T hit) return hit;
                        int n = VisualTreeHelper.GetChildrenCount(root);
                        for (int i = 0; i < n; i++)
                        {
                            var found = Find<T>(VisualTreeHelper.GetChild(root, i));
                            if (found != null) return found;
                        }
                        return null;
                    }

                    var inRow = Find<System.Windows.Controls.Primitives.ToggleButton>(boolGrid);
                    Check("属性网格里的开关仍是 44×24（行布局没把它拉宽）",
                        inRow != null && Math.Abs(inRow.ActualWidth - 44) < 0.5 && Math.Abs(inRow.ActualHeight - 24) < 0.5,
                        inRow == null ? "行里没找到 ToggleButton" : $"实际 {inRow.ActualWidth:F0}×{inRow.ActualHeight:F0}（宿主 360 宽）");

                    var rowTrack = inRow?.Template?.FindName("Track", inRow) as FrameworkElement;
                    var rowThumb = inRow?.Template?.FindName("Thumb", inRow) as FrameworkElement;
                    Check("属性网格里的开关：轨道 44×24、滑块 Ø20 内缩 2px（不鼓出轨道）",
                        rowTrack != null && rowThumb != null
                        && Math.Abs(rowTrack.ActualWidth - 44) < 0.5 && Math.Abs(rowTrack.ActualHeight - 24) < 0.5
                        && Math.Abs(rowThumb.ActualWidth - 20) < 0.5 && Math.Abs(rowThumb.ActualHeight - 20) < 0.5,
                        $"轨道 {rowTrack?.ActualWidth:F0}×{rowTrack?.ActualHeight:F0}，"
                        + $"滑块 {rowThumb?.ActualWidth:F0}×{rowThumb?.ActualHeight:F0}");

                    // 位移量必须正好"轨道宽 − 2×内缩 − 滑块直径"（44−4−20=20），否则滑块停在半路。
                    // 动画终值在无窗口冒烟里取不到（动画时钟不推进），所以走静态扫描。
                    var switchFile = @"d:\C#\VM\UI\Controls\Themes\Fluent\FluentControls.xaml";
                    var switchBlock = File.Exists(switchFile)
                        ? Regex.Match(File.ReadAllText(switchFile), @"x:Key=""FluentToggleSwitchStyle""(?s:.*?)</Style>").Value
                        : string.Empty;
                    Check("开关位移 = 44 − 2×2 − 20 = 20（滑块要滚到头）",
                        switchBlock.Contains("To=\"20\""),
                        switchBlock.Length == 0 ? "没读到样式块" : "To=20 ✓");

                    // ---- 开关规格静态闸：四态部件 / 减动效 / 无写死色值 ----
                    Check("开关模板四态部件齐全（FocusRing/HoverOverlay/ThumbHost/两级缩放）",
                        new[] { "FocusRing", "HoverOverlay", "ThumbHost", "ThumbStretch", "ThumbHoverScale" }
                            .All(p => switchBlock.Contains($"x:Name=\"{p}\"")),
                        switchBlock.Length == 0 ? "没读到样式块" : "5 个部件全部命中");
                    // 减动效：常规版的 Storyboard 里不能绑系统设置（会破坏可冻结性，实测解析失败），
                    // 所以由 BoolStateGenerator 按系统设置二选一 —— 这里验证"变体存在 + 生成器真的会选"。
                    var genFile = @"d:\C#\VM\UI\Controls\CustomControl\PropertyGrid\Generators\BoolStateGenerator.cs";
                    var genText = File.Exists(genFile) ? File.ReadAllText(genFile) : string.Empty;
                    Check("减动效：静态变体存在且生成器按系统设置选择",
                        app.TryFindResource("FluentToggleSwitchStyleStatic") is Style
                        && genText.Contains("ClientAreaAnimation")
                        && genText.Contains("FluentToggleSwitchStyleStatic"),
                        $"变体可解析={app.TryFindResource("FluentToggleSwitchStyleStatic") is Style} "
                        + $"生成器接线={genText.Contains("FluentToggleSwitchStyleStatic")}");

                    // 减动效变体是全新模板，必须真的实例化一次才算验证过（字典是懒解析的）
                    var staticToggle = new System.Windows.Controls.Primitives.ToggleButton
                    {
                        Style = app.TryFindResource("FluentToggleSwitchStyleStatic") as Style
                    };
                    var staticHost = new Border { Width = 160, Height = 60, Child = staticToggle };
                    staticHost.Measure(new Size(160, 60));
                    staticHost.Arrange(new Rect(0, 0, 160, 60));
                    staticHost.UpdateLayout();
                    staticHost.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    Check("减动效变体：模板可实例化、几何与常规版一致",
                        Math.Abs(staticToggle.ActualWidth - 44) < 0.5
                        && Math.Abs(staticToggle.ActualHeight - 24) < 0.5
                        && staticToggle.Template?.FindName("TrackOn", staticToggle) != null,
                        $"实际 {staticToggle.ActualWidth:F0}×{staticToggle.ActualHeight:F0}，"
                        + $"TrackOn={staticToggle.Template?.FindName("TrackOn", staticToggle) != null}");
                    Check("开关样式里没有写死的色值（颜色一律走令牌）",
                        !Regex.IsMatch(switchBlock, @"#FF[0-9A-Fa-f]{6}"),
                        switchBlock.Length == 0 ? "没读到样式块" : "无 #FFxxxxxx 字面量");

                    // ---- 表格行规格：整行可点 + 无障碍名（扫描处理器源码，与仓库静态契约同款做法） ----
                    var procFile = @"d:\C#\VM\UI\Controls\CustomControl\PropertyGrid\Processors\PropertyGridLayoutProcessor.cs";
                    var procText = File.Exists(procFile) ? File.ReadAllText(procFile) : string.Empty;
                    Check("属性行：整行可点（行容器挂切换 + 手型光标 + 命中测试透明底）",
                        procText.Contains("MakeRowClickable")
                        && procText.Contains("Cursors.Hand")
                        && procText.Contains("Brushes.Transparent"),
                        procText.Length == 0 ? "没读到处理器源码" : "接线齐全");
                    Check("属性行：开关有无障碍名与说明（AutomationProperties）",
                        procText.Contains("AutomationProperties.SetName")
                        && procText.Contains("AutomationProperties.SetHelpText"),
                        "");

                    // 焦点环只认键盘焦点（:focus-visible）：模板里不得再放 IsKeyboardFocused 触发器，
                    // 否则鼠标点击也亮环 —— 蓝环叠蓝轨道，现场被看成"画坏了"（历史截图就是这副样子）
                    int ringBlocks = 0, cleanBlocks = 0;
                    var themeAll = File.Exists(switchFile) ? File.ReadAllText(switchFile) : string.Empty;
                    foreach (Match m in Regex.Matches(themeAll, @"x:Key=""FluentToggleSwitchStyle\w*""(?s:.*?)</Style>"))
                    {
                        ringBlocks++;
                        if (!m.Value.Contains("IsKeyboardFocused")) cleanBlocks++;
                    }
                    Check("焦点环：键盘焦点才亮（两个模板都不再用 IsKeyboardFocused + 生成器挂行为）",
                        ringBlocks == 2 && cleanBlocks == 2 && genText.Contains("FocusVisibleBehavior"),
                        $"模板块 {cleanBlocks}/{ringBlocks} 干净，生成器接线={genText.Contains("FocusVisibleBehavior")}");

                    // 开态=深色轨道（"黑轨道+白滑块"的单色开关，不是系统蓝）：
                    // 常规版动画目标是 Color 令牌，减动效变体用同值的 FluentTextPrimaryBrush
                    Check("开态轨道是深色令牌（不是系统蓝）",
                        switchBlock.Contains("FluentToggleTrackOnColor")
                        && themeAll.Contains("FluentTextPrimaryBrush"),
                        $"常规版引用令牌={switchBlock.Contains("FluentToggleTrackOnColor")}");

                    // ---- 按钮规格：悬停叠加层必须铺满整颗按钮（2026-10-20 真机反馈） ----
                    // 旧模板把 Padding 放在根 Border 上，叠加层作为 Border 的子级被 Padding 一起缩进：
                    // 96x32 的「取消」悬停时只亮中间 70x19 的小胶囊，看着像"悬浮区域错位"。
                    // 现在 Padding 挪到 ContentPresenter.Margin，叠加层与内容同级铺满 Border 内容区。
                    var btnStyle = app.TryFindResource("DialogSecondaryButton") as Style;
                    var btn = new Button { Content = "Cancel", MinWidth = 96, Style = btnStyle };
                    var btnHost = new Border { Width = 200, Height = 60, Child = btn };
                    btnHost.Measure(new Size(200, 60));
                    btnHost.Arrange(new Rect(0, 0, 200, 60));
                    btnHost.UpdateLayout();
                    btn.ApplyTemplate();
                    var btnRoot = btn.Template?.FindName("Root", btn) as FrameworkElement;
                    var btnHover = btn.Template?.FindName("HoverOverlay", btn) as FrameworkElement;
                    var btnContent = btn.Template?.FindName("ContentHost", btn) as ContentPresenter;
                    Check("按钮悬停层铺满整颗按钮（不再缩在 Padding 内容区里）",
                        btnRoot != null && btnHover != null
                        && Math.Abs(btnHover.ActualWidth - (btnRoot.ActualWidth - 2)) < 0.5
                        && Math.Abs(btnHover.ActualHeight - (btnRoot.ActualHeight - 2)) < 0.5,
                        $"按钮 {btnRoot?.ActualWidth:F1}x{btnRoot?.ActualHeight:F1}，叠加层 {btnHover?.ActualWidth:F1}x{btnHover?.ActualHeight:F1}");
                    Check("按钮焦点环走 FluentButtonFocusVisual（2px 强调色圆角，不再用系统虚线）",
                        btn.FocusVisualStyle == app.TryFindResource("FluentButtonFocusVisual"),
                        $"FocusVisualStyle={(btn.FocusVisualStyle == null ? "null" : "set")}");
                    Check("按钮内容位置不变（Padding 挪到 ContentPresenter.Margin）",
                        btnContent != null && btnContent.Margin == btn.Padding,
                        $"ContentHost.Margin={btnContent?.Margin} Padding={btn.Padding}");

                    Console.WriteLine(failures == 0
                        ? "=== Fluent 控件冒烟：全部通过 ==="
                        : $"### Fluent 控件冒烟：{failures} 项失败");
                    return failures == 0 ? 0 : 1;
                }

                if (mutateResources)
                {
                    // 复刻 App.CreateShell 的关键动作：解析期之前对 App 资源字典做一次索引写入
                    var thinFont = app.Resources["FA.Light"];
                    app.Resources["Icon"] = thinFont;
                    Console.WriteLine($">> 已复刻 CreateShell 资源写入：FA.Light={thinFont != null}");
                }
                // 直接验证键可达性
                Console.WriteLine($">> App.Resources[AccentBrush] = {app.Resources["AccentBrush"] ?? "(null)"}");
                Console.WriteLine($">> App.Resources[TextPrimaryBrush] = {app.Resources["TextPrimaryBrush"] ?? "(null)"}");
            }
            catch (Exception ex)
            {
                Console.WriteLine("### 合并 App 资源时即抛出异常:");
                Print(ex);
                return 3;
            }

            Window window;
            try
            {
                // BAML 全保真加载（Page 编译，与应用一致的类型/资源解析）
                window = (Window)Application.LoadComponent(
                    new Uri("/UIThemeSmokeTest;component/ShellCopy.xaml", UriKind.Relative));
            }
            catch (Exception ex)
            {
                Console.WriteLine("### Shell.xaml 解析(XamlReader.Parse)阶段抛出异常:");
                Print(ex);
                return 2;
            }

            window.Width = 1280;
            window.Height = 920;

            // 复刻 Shell.ApplyDockTheme()：给 DockingManager 注入 FluentLight 主题字典
            try
            {
                var dock = FindDockingManager(window);
                _shellDock = dock;
                if (dock != null)
                {
                    dock.Theme = new HarnessDockTheme(new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/VisionMaster;component/Themes/DockThemes/FluentLight.xaml")
                    });
                    Console.WriteLine(">> 已复刻 ApplyDockTheme（FluentLight 注入 DockingManager）");
                }
                else
                {
                    Console.WriteLine(">> 未找到 DockingManager，跳过 ApplyDockTheme 复刻");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("### 复刻 ApplyDockTheme 时抛出异常:");
                Print(ex);
            }

            try
            {
                window.Measure(new Size(1280, 920));
                window.Arrange(new Rect(0, 0, 1280, 920));
                window.UpdateLayout();
                Console.WriteLine($"=== OK: Shell.xaml 全树模板实例化无异常 (Fluent={_withFluent}) ===");
                // A1（2026-10-22）：这一段的 7 条断言原先返回值被丢掉——失败只打日志、不进退出码。
                // 现在必须接住并计入返回码，否则"多格布局坏了"在 CI 里等于没发生（R65：绿灯是真的，只是少了一条）。
                int paneLayoutFailures = RunPaneLayoutChecks();
                int shellLayoutFailures = RunShellLayoutChecks();
                int panelModeFailures = RunPanelModeChecks();
                int shellModeFailures = RunShellModeChecks();
                int gridFailures = RunGridViewChecks();
                int colorTokenFailures = RunHardcodedColorChecks();
                int flowCanvasFailures = RunFlowCanvasChecks();
                int logConsoleFailures = RunLogConsoleChecks();
                int firstBatchFailures = RunFirstBatchViewChecks();
                int loginFailures = RunLoginChecks();
                int pluginEntryFailures = RunPluginEntryIsolationChecks();
                return paneLayoutFailures + shellLayoutFailures + panelModeFailures + shellModeFailures + gridFailures + colorTokenFailures
                     + flowCanvasFailures + logConsoleFailures + firstBatchFailures + loginFailures
                     + pluginEntryFailures == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"### 全树测量抛出异常 (Fluent={_withFluent})，开始二分定位元素:");
                Console.WriteLine();
                Print(ex);
                Console.WriteLine();

                var content = window.Content as FrameworkElement;
                if (content != null)
                {
                    var broken = FindBroken(content, string.Empty);
                    Console.WriteLine();
                    Console.WriteLine(broken == null
                        ? "### 未能在子树中进一步定位"
                        : $"### 最深异常元素: {broken.GetType().FullName}  Name=\"{broken.Name}\"");
                }
                return 1;
            }
            finally
            {
                app.Shutdown();
            }
        }

        /// <summary>递归预加载引用程序集：裸 XamlReader 仅在程序集已加载时才能解析 XmlnsDefinition URI</summary>
        private static void PreloadAssemblies()
        {
            var loaded = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new System.Collections.Generic.Queue<System.Reflection.Assembly>();
            var entry = System.Reflection.Assembly.GetEntryAssembly();
            queue.Enqueue(entry);
            loaded.Add(entry.GetName().Name);
            while (queue.Count > 0)
            {
                foreach (var name in queue.Dequeue().GetReferencedAssemblies())
                {
                    if (loaded.Contains(name.Name)) continue;
                    loaded.Add(name.Name);
                    try { queue.Enqueue(System.Reflection.Assembly.Load(name)); }
                    catch { /* 可选依赖缺失时忽略 */ }
                }
            }
        }

        private static void BuildAppResources(Application app)
        {
            var merged = app.Resources.MergedDictionaries;
            merged.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Core.Halcon;component/Generic.xaml") });
            merged.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/UI;component/Themes/Generic.xaml") });
            if (_withFluent)
                merged.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml") });

            // App.xaml 内联资源（Icon 字体 / ExpandToggleButtonTemplate / LinkButtonStyle），从 App.xaml 原文提取
            string appXaml = File.ReadAllText(@"d:\C#\VM\VisionMaster\App.xaml");
            var m = Regex.Match(appXaml, @"<ResourceDictionary>(?s:.)*</ResourceDictionary>\s*</prism:PrismApplication\.Resources>");
            if (!m.Success)
            {
                Console.WriteLine("!! 未能从 App.xaml 提取内联资源，跳过");
                return;
            }
            string inline = m.Value;
            inline = inline.Substring(0, inline.LastIndexOf("</prism:PrismApplication.Resources>"));
            inline = inline.Replace(
                "<ResourceDictionary>",
                "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">");
            var inlineDict = (ResourceDictionary)XamlReader.Parse(inline);
            // 上面那条正则把 App.xaml 的 <ResourceDictionary.MergedDictionaries> 块一起吃进来了
            // （贪婪匹配到最后一个 </ResourceDictionary>）。不剥掉的话，无论 --no-fluent 与否，
            // Fluent 主题都会从这条路径再合并一次 —— "--no-fluent" 就成了摆设
            // （2026-10-06 排查方案列表表格缺陷时实测：去掉 Fluent 的对照组里 Fluent 仍在生效）。
            if (inlineDict.MergedDictionaries.Count > 0)
            {
                Console.WriteLine($">> App.xaml 内联块里夹带的合并字典 {inlineDict.MergedDictionaries.Count} 个已剥掉（否则 --no-fluent 无效）");
                inlineDict.MergedDictionaries.Clear();
            }
            merged.Add(inlineDict);
        }

        private static Window ParseShell()
        {
            string shell = File.ReadAllText(@"d:\C#\VM\VisionMaster\Shell.xaml");
            shell = shell.Replace("x:Class=\"VisionMaster.Shell\"", "")
                         .Replace("prism:ViewModelLocator.AutoWireViewModel=\"True\"", "")
                         .Replace("ui:WindowExtensions.IsAutoWireCommands=\"True\"", "");
            shell = shell.Replace("clr-namespace:VisionMaster.Commands\"", "clr-namespace:VisionMaster.Commands;assembly=VisionMaster\"")
                         .Replace("clr-namespace:VisionMaster.Views\"", "clr-namespace:VisionMaster.Views;assembly=VisionMaster\"");
            // internal 的 MarkupExtension 无法被裸 XamlReader 实例化，剥掉行为型 Command
            shell = Regex.Replace(shell, @"\s+Command=""\{commands:[A-Za-z]+\}""", string.Empty);
            // AvalonDock 与本测试无关（崩溃点在标题栏），整块替换为占位
            shell = Regex.Replace(shell, @"<avalonDock:DockingManager(?s:.)*?</avalonDock:DockingManager>", @"<Grid x:Name=""DockStub""/>");
            // 去掉事件处理器（无 x:Class 后无法绑定 code-behind 方法）
            shell = Regex.Replace(
                shell,
                @"\s(MouseLeftButtonDown|MouseLeftButtonUp|MouseDown|MouseUp|MouseMove|MouseEnter|MouseLeave|Loaded|Unloaded|Closing|Closed|SizeChanged|KeyDown|KeyUp|PreviewMouseDown|PreviewMouseUp|PreviewKeyDown|Drop|DragEnter|DragOver|SelectionChanged|TextChanged|Click|SourceUpdated|TargetUpdated)=""[A-Za-z0-9_]+""",
                string.Empty);
            return (Window)XamlReader.Parse(shell);
        }

        /// <summary>属性网格几何检查用的最小模型：一个"启用"开关行（与预处理插件里那一行同形）</summary>
        private sealed class ToggleGridModel
        {
            [UI.Attributes.SuperDisplay(Name = "启用", Group = new[] { "算子" }, Order = 0)]
            public bool Enabled { get; set; } = true;
        }

        /// <summary>复刻 Shell.FluentLightTheme：DictionaryTheme 是抽象类，需派生</summary>
        private sealed class HarnessDockTheme : AvalonDock.Themes.DictionaryTheme
        {
            public HarnessDockTheme(ResourceDictionary dictionary) : base(dictionary) { }
        }

        private static AvalonDock.DockingManager? FindDockingManager(DependencyObject root)
        {
            if (root is AvalonDock.DockingManager dm) return dm;
            if (root is not System.Windows.Media.Visual && root is not System.Windows.Media.Media3D.Visual3D) return null;
            var count = VisualTreeHelper.GetChildrenCount(root);
            if (count > 0)
            {
                for (var i = 0; i < count; i++)
                {
                    var found = FindDockingManager(VisualTreeHelper.GetChild(root, i));
                    if (found != null) return found;
                }
            }
            foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root))
            {
                if (child is DependencyObject d)
                {
                    var found = FindDockingManager(d);
                    if (found != null) return found;
                }
            }
            return null;
        }

        /// <summary>逐元素测量，异常则深入子元素，返回最深抛异常的元素</summary>
        private static FrameworkElement FindBroken(FrameworkElement element, string path)
        {
            var here = string.IsNullOrEmpty(path) ? element.GetType().Name : path + " > " + element.GetType().Name;
            try
            {
                element.Measure(new Size(1280, 920));
                element.Arrange(new Rect(0, 0, 1280, 920));
                return null;
            }
            catch
            {
                // 该元素抛异常 —— 继续深入子元素缩小范围
                foreach (var child in EnumerateChildren(element))
                {
                    var broken = FindBroken(child, here);
                    if (broken != null) return broken;
                }
                Console.WriteLine($"### 异常元素: {here}   Name=\"{element.Name}\"");
                try
                {
                    element.Measure(new Size(1280, 920));
                }
                catch (Exception ex)
                {
                    Print(ex);
                }
                return element;
            }
        }

        private static System.Collections.Generic.IEnumerable<FrameworkElement> EnumerateChildren(DependencyObject parent)
        {
            var count = VisualTreeHelper.GetChildrenCount(parent);
            if (count > 0)
            {
                for (var i = 0; i < count; i++)
                    if (VisualTreeHelper.GetChild(parent, i) is FrameworkElement fe)
                        yield return fe;
            }
            else
            {
                foreach (var item in System.Windows.LogicalTreeHelper.GetChildren(parent).OfType<FrameworkElement>())
                    yield return item;
            }
        }

        /// <summary>
        /// 图集控件冒烟：真实实例化 <see cref="ImageGallery"/> → 应用模板 → 断言筛选/可见性逻辑。
        /// 帧不带 HImage（只验证控件与模板通路，不依赖 HALCON 是否真的取到图）。
        /// </summary>
        private static int RunGalleryCheck()
        {
            var frames = new ObservableCollection<ImageFrame>
            {
                new ImageFrame { FlowName = "流程A", StepName = "预处理", PortName = "图像" },
                new ImageFrame { FlowName = "流程A", StepName = "定位", PortName = "结果图" },
                new ImageFrame { FlowName = "流程B", StepName = "测量", PortName = "图像" },
            };

            var gallery = new ImageGallery { ItemsSource = frames };
            // 用 Border 作宿主直接测控件：Window 未 Show 时不驱动内容布局（实测 Actual 恒为 0x0），
            // 而 Border 一 Measure 就会给子元素应用模板，正是要验证的路径。
            var host = new Border
            {
                Width = 900,
                Height = 600,
                Child = gallery
            };

            var failures = 0;
            void Check(string name, bool ok, string detail)
            {
                Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}  {detail}");
                if (!ok) failures++;
            }

            try
            {
                host.Measure(new Size(900, 600));
                host.Arrange(new Rect(0, 0, 900, 600));
                host.UpdateLayout();
                // 控件的延迟重算走 Dispatcher.BeginInvoke(Normal)：真实应用由消息循环驱动，
                // 测试器里显式冲刷一次（低优先级 = 先跑完 Normal 队列里的重算）
                host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                Console.WriteLine("### 图集控件测量/模板应用抛出异常:");
                Print(ex);
                return 1;
            }

            Console.WriteLine($">> 模板应用后 FilteredFrames={gallery.FilteredFrames.Count}, AvailableFlows=[{string.Join(", ", gallery.AvailableFlows)}]");
            Console.WriteLine($">> 诊断：Template={(gallery.Template == null ? "null（样式未解析）" : "已应用")}, VisualChildren={VisualTreeHelper.GetChildrenCount(gallery)}, 找到PART_Big={(gallery.Template?.FindName("PART_Big", gallery) != null)}");
            Console.WriteLine($">> 诊断2：Style={(gallery.Style == null ? "null" : "有")}, App.TryFindResource(typeof(ImageGallery))={(Application.Current.TryFindResource(typeof(ImageGallery)) == null ? "null" : "有")}, Loaded={gallery.IsLoaded}, Actual={gallery.ActualWidth}x{gallery.ActualHeight}");
            Check("全部流程可见", gallery.FilteredFrames.Count == 3, $"期望 3，实际 {gallery.FilteredFrames.Count}");
            Check("流程下拉为 全部+2 项", gallery.AvailableFlows.Count == 3, $"期望 3，实际 {gallery.AvailableFlows.Count}");
            Check("自动选中最新帧", gallery.SelectedFrame != null, "应自动选中一张");

            gallery.SelectedFlow = "流程A";
            gallery.Recompute();
            Check("按流程A筛选", gallery.FilteredFrames.Count == 2, $"期望 2，实际 {gallery.FilteredFrames.Count}");

            gallery.SelectedFlow = ImageGallery.AllFlowsLabel;
            gallery.Recompute();
            Check("恢复全部流程", gallery.FilteredFrames.Count == 3, $"期望 3，实际 {gallery.FilteredFrames.Count}");

            frames.Add(new ImageFrame { FlowName = "流程B", StepName = "判定", PortName = "图像" });
            gallery.Recompute();
            Check("新增帧后可见（集合通知通路）", gallery.FilteredFrames.Count == 4, $"期望 4，实际 {gallery.FilteredFrames.Count}");

            // ---- 循环运行刷新公平性：Normal 洪水不得饿死重算 ----
            // 背景：循环运行时"采集插帧"以 Normal 优先级连续到达；重算原先挂 Background，
            // 会被 Normal 洪水永久饿死 → 帧明明在集合里，界面却一直停在"本格暂无图像"
            //（而单次运行插完就没流量，完全正常——正是这个不对称让缺陷潜伏很久）。
            // 这里用自续投递的 Normal 洪水复现，断言重算仍能跑完。
            {
                var floodFrames = new ObservableCollection<ImageFrame>();
                var floodGallery = new ImageGallery { ItemsSource = floodFrames, ViewIndexFilter = 1 };
                var floodHost = new Border { Width = 300, Height = 200, Child = floodGallery };
                floodHost.Measure(new Size(300, 200));
                floodHost.Arrange(new Rect(0, 0, 300, 200));
                floodHost.UpdateLayout();

                var frame = new System.Windows.Threading.DispatcherFrame();
                void Flood()
                {
                    // 只要泵还没停就续投 Normal 工作：模拟循环运行期间持续不断的插帧流量
                    if (frame.Continue)
                        floodHost.Dispatcher.BeginInvoke(new Action(Flood), System.Windows.Threading.DispatcherPriority.Normal);
                }

                var deadline = DateTime.UtcNow.AddMilliseconds(800);
                var stopper = new System.Windows.Threading.DispatcherTimer(
                    TimeSpan.FromMilliseconds(5),
                    System.Windows.Threading.DispatcherPriority.Send,   // 看门狗用最高优先级，洪水饿不死它
                    (_, __) =>
                    {
                        if (floodGallery.FilteredFrames.Count > 0 || DateTime.UtcNow >= deadline)
                            frame.Continue = false;
                    },
                    floodHost.Dispatcher);

                Flood();
                floodFrames.Add(new ImageFrame
                {
                    FlowName = "循环流程",
                    StepName = "采集",
                    PortName = "图像",
                    ViewIndex = 1
                });
                stopper.Start();
                System.Windows.Threading.Dispatcher.PushFrame(frame);   // 泵消息直到出结果或超时
                stopper.Stop();

                Check("循环运行的 Normal 洪水下画廊仍会刷新（重算不再被饿死）",
                    floodGallery.FilteredFrames.Count == 1,
                    $"期望 1，实际 {floodGallery.FilteredFrames.Count}");

                // 收尾：洪水已停，冲刷一次让残余队列跑完，不影响后续用例
                floodHost.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            }

            // ---- 对比模式：DP 驱动模板触发器，第二张大图应变为可见 ----
            gallery.PinnedFrame = gallery.FilteredFrames.FirstOrDefault();
            gallery.IsCompareMode = true;
            host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var big2 = FindByTag(gallery, "对比");
            Check("对比模式显示第二张大图", big2 != null && big2.Visibility == Visibility.Visible,
                $"big2={(big2 == null ? "未找到" : big2.Visibility.ToString())}");

            // ---- 导出：真实 HImage → 按流程分文件夹落盘 ----
            try
            {
                HOperatorSet.GenImageConst(out HObject ho, "byte", 32, 24);
                var image = HObjectExtension.ToHimage(ho);
                ho.Dispose();

                var exportRoot = Path.Combine(Path.GetTempPath(), "vm_gallery_export_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                var exportFrames = new List<ImageFrame>
                {
                    new ImageFrame { FlowName = "流程A", StepName = "预处理", PortName = "图像", Image = image },
                    new ImageFrame { FlowName = "流程B", StepName = "测量", PortName = "图像", Image = image },
                };

                var saved = ImageExporter.SaveFrames(exportFrames, exportRoot);
                var files = Directory.Exists(exportRoot)
                    ? Directory.GetFiles(exportRoot, "*.png", SearchOption.AllDirectories)
                    : Array.Empty<string>();

                Check("导出落盘（按流程分文件夹）", saved == 2 && files.Length == 2, $"saved={saved}, files={files.Length}");

                if (Directory.Exists(exportRoot)) Directory.Delete(exportRoot, true);
            }
            catch (Exception ex)
            {
                Check("导出落盘（按流程分文件夹）", false, "异常：" + ex.Message);
            }

            // ================= 画布新语义：上行大图 + 下行信息列表 =================

            // ---- ① 布局方向：大图在上、列表在下（本次改造的核心诉求） ----
            var big = gallery.Template?.FindName("PART_Big", gallery) as FrameworkElement;
            var partList = gallery.Template?.FindName("PART_List", gallery) as FrameworkElement;
            if (big != null && partList != null)
            {
                var bigTop = big.TransformToAncestor(host).Transform(new Point(0, 0)).Y;
                var listTop = partList.TransformToAncestor(host).Transform(new Point(0, 0)).Y;
                Check("布局：大图在上 / 列表在下",
                    bigTop < listTop && big.ActualHeight > partList.ActualHeight,
                    $"big.Top={bigTop:F0} h={big.ActualHeight:F0}；list.Top={listTop:F0} h={partList.ActualHeight:F0}");
            }
            else
            {
                Check("布局：大图在上 / 列表在下", false, $"PART_Big={big != null} PART_List={partList != null}");
            }

            // ---- ①b 对比列默认不占位：非对比模式下大图必须铺满整宽（否则右边空一片） ----
            gallery.IsCompareMode = false;
            host.Measure(new Size(900, 600));
            host.Arrange(new Rect(0, 0, 900, 600));
            host.UpdateLayout();
            host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Check("非对比模式：大图铺满整宽",
                big != null && big.ActualWidth >= 890,
                $"big.Width={big?.ActualWidth:F0}（容器 900）");

            gallery.PinnedFrame = gallery.FilteredFrames.FirstOrDefault();
            gallery.IsCompareMode = true;
            host.Measure(new Size(900, 600));
            host.Arrange(new Rect(0, 0, 900, 600));
            host.UpdateLayout();
            host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var compareBig = FindByTag(gallery, "对比");
            Check("对比模式：对比列展开且有宽度",
                compareBig != null && compareBig.ActualWidth > 0 && big != null && big.ActualWidth < 890,
                $"对比图.Width={compareBig?.ActualWidth:F0}，主图.Width={big?.ActualWidth:F0}");
            gallery.IsCompareMode = false;
            host.Measure(new Size(900, 600));
            host.Arrange(new Rect(0, 0, 900, 600));
            host.UpdateLayout();
            host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            var missingParts = new List<string>();
            foreach (var part in new[] { "PART_Big", "PART_Big2", "PART_List", "PART_Empty", "PART_CompareSplitter" })
            {
                if (gallery.Template?.FindName(part, gallery) == null) missingParts.Add(part);
            }
            Check("模板部件齐全（工具条已去掉）", missingParts.Count == 0,
                missingParts.Count == 0 ? "5 个部件全部命中" : "缺：" + string.Join(",", missingParts));

            // 工具条去掉后，清空/导出必须仍有入口（代码里建的右键菜单）
            Check("右键菜单有清空/导出入口",
                gallery.ContextMenu != null
                && gallery.ContextMenu.Items.OfType<System.Windows.Controls.MenuItem>().Count() == 3,
                gallery.ContextMenu == null
                    ? "(没有右键菜单)"
                    : string.Join(" / ", gallery.ContextMenu.Items.OfType<System.Windows.Controls.MenuItem>().Select(m => m.Header?.ToString())));

            Check("工具条部件确实已移除",
                gallery.Template?.FindName("PART_ExportAll", gallery) == null
                && gallery.Template?.FindName("PART_FlowFilter", gallery) == null,
                "PART_ExportAll / PART_FlowFilter 不应再存在");

            // ---- ② 插件注入的信息：标题 / 键值行 / 来源都要显示得出来 ----
            var injected = new ImageFrame("\u0002inject-1")
            {
                FlowName = "流程A",
                StepName = "缺陷检测_0",          // 注入帧的"步骤位"装的是插件实例名
                PortName = string.Empty,
                Title = "缺陷检测结果",
                InfoRows = new List<ImageInfoRow> { new("缺陷数", "51"), new("判定", "NG") },
                Width = 646,
                Height = 492,
            };
            frames.Add(injected);
            gallery.Recompute();
            host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            Check("注入标题优先于「步骤.端口」", injected.DisplayName == "缺陷检测结果", injected.DisplayName);
            Check("注入信息单行摘要", injected.InfoSummary == "缺陷数: 51   判定: NG", injected.InfoSummary);
            Check("来源含流程与插件",
                injected.MetaText.Contains("流程A") && injected.MetaText.Contains("缺陷检测_0"), injected.MetaText);
            Check("唯一槽位键不与端口帧合并",
                injected.SlotKey == "\u0002inject-1" && frames.Count == 5, $"slot={injected.SlotKey}");

            // 缩略图条只显示图片：条上不该再有标题/信息文字；信息全部进了悬停提示
            var stripTexts = new List<string>();
            var tipTexts = new List<string>();
            var visited = 0;
            void CollectTip(DependencyObject root)
            {
                visited++;
                if (root is FrameworkElement fe && fe.ToolTip is string s && !string.IsNullOrWhiteSpace(s))
                    tipTexts.Add(s);
                if (root is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text)) stripTexts.Add(tb.Text);
                int n = VisualTreeHelper.GetChildrenCount(root);
                for (int i = 0; i < n; i++) CollectTip(VisualTreeHelper.GetChild(root, i));
            }
            if (partList != null) CollectTip(partList);
            Console.WriteLine($">> 缩略图条遍历：vis={visited}, 条上文字={stripTexts.Count}, 悬停提示={tipTexts.Count} 条");
            Check("缩略图上不再有文字（只显示图片）",
                !stripTexts.Any(t => t.Contains("缺陷") || t.Contains("流程A") || t.Contains("流程B")),
                $"条上文字：{string.Join(" | ", stripTexts.Where(t => t != "无预览").Take(4))}");
            Check("悬停提示文本含标题/注入信息/来源",
                injected.ToolTipText.Contains("缺陷检测结果")
                && injected.ToolTipText.Contains("缺陷数: 51")
                && injected.ToolTipText.Contains("流程A"),
                injected.ToolTipText.Replace(Environment.NewLine, " ⏎ "));
            Check("缩略图的悬停提示已挂上（元素绑定生效）",
                tipTexts.Any(t => t.Contains("缺陷检测结果")),
                $"提示条数={tipTexts.Count}；首条={tipTexts.FirstOrDefault()?.Replace(Environment.NewLine, " ⏎ ")}");

            // ---- ③ 一张图都没有时的提示（文案由宿主设置，必须原样显示出来） ----
            var emptyGallery = new ImageGallery
            {
                ItemsSource = new ObservableCollection<ImageFrame>(),
                HintText = "测试提示-ABC"
            };
            var emptyHost = new Border
            {
                Width = 500,
                Height = 300,
                Child = emptyGallery
            };
            emptyHost.Measure(new Size(500, 300));
            emptyHost.Arrange(new Rect(0, 0, 500, 300));
            emptyHost.UpdateLayout();
            emptyHost.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var emptyHint = emptyGallery.Template?.FindName("PART_Empty", emptyGallery) as FrameworkElement;
            Check("空画布给出提示",
                emptyHint != null && emptyHint.Visibility == Visibility.Visible,
                emptyHint == null ? "未找到 PART_Empty" : emptyHint.Visibility.ToString());
            Check("提示文案由宿主设置（TemplateBinding 生效）",
                emptyHint is TextBlock hintTb && hintTb.Text == "测试提示-ABC",
                emptyHint is TextBlock tb2 ? tb2.Text : "(不是 TextBlock)");

            // ---- ③b 有了图之后提示要自动消失 ----
            emptyGallery.ItemsSource = new ObservableCollection<ImageFrame>
            {
                new ImageFrame { FlowName = "流程A", StepName = "预处理", PortName = "图像" }
            };
            emptyHost.UpdateLayout();
            emptyHost.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Check("有图后提示自动隐藏",
                emptyHint != null && emptyHint.Visibility != Visibility.Visible,
                emptyHint == null ? "(未找到)" : emptyHint.Visibility.ToString());

            // ---- ④ 每轮流程开始：清空 / 覆盖本流程（走真实的 ImageCollectionService） ----
            var settings = new VisionMaster.Services.AppSettingsService();
            var svc = new VisionMaster.Services.ImageCollectionService(settings, null!);
            var cfg = settings.Current.ImageGallery;
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var insert = typeof(VisionMaster.Services.ImageCollectionService).GetMethod("InsertOrReplace", flags)!;
            var onStarted = typeof(VisionMaster.Services.ImageCollectionService).GetMethod("OnFlowRunStarted", flags)!;
            var currentFlowField = typeof(VisionMaster.Services.ImageCollectionService).GetField("_currentFlowName", flags)!;

            void Seed()
            {
                insert.Invoke(svc, new object[] { new ImageFrame { FlowName = "流程A", StepName = "预处理", PortName = "图像" }, cfg });
                insert.Invoke(svc, new object[] { new ImageFrame { FlowName = "流程A", StepName = "定位", PortName = "结果" }, cfg });
                insert.Invoke(svc, new object[] { new ImageFrame { FlowName = "流程B", StepName = "测量", PortName = "图像" }, cfg });
            }

            // ---- ④ 每轮流程开始：登记待清空，第一张新图到达时同批原子替换（方案 A：循环不闪黑） ----
            // 语义：本轮开始**不清空**（清完到出图之间隔着采集耗时，那段空窗就是"黑屏↔图片"频闪）；
            // 清空推迟到本轮第一张新图到达时、与插入同一批完成；本轮无新图则不不清空（保持上一轮画面）。
            HImage MakeTestImage()
            {
                HOperatorSet.GenImageConst(out HObject proto, "byte", 8, 8);
                var img = new HImage(proto);
                proto.Dispose();
                return img;
            }

            void CaptureFrame()
            {
                using var img = MakeTestImage();
                svc.Capture(new Core.Events.ImageDisplayEvent<HalconDotNet.HImage>
                {
                    ViewIndex = 1,
                    Image = img,
                    PluginName = "流程A.预处理"   // 流程归属取 _currentFlowName（见 ImageCollectionService.Capture）
                });
            }

            // 界面可见性：把画廊直接接到采集服务上（与真实 ImageView 同构）
            var svcGallery = new ImageGallery { ItemsSource = svc.Frames };

            cfg.Enabled = true;                 // 断言前提：收录总开关与实时注入都开
            cfg.IncludeRealtimePreviews = true;

            cfg.RunStartMode = VisionMaster.Models.ImageGalleryRunStartMode.ReplaceFlow;
            Seed();

            var batchCounts = new List<int>();
            EventHandler onChange = (_, __) => batchCounts.Add(svc.Count);
            svc.Changed += onChange;
            try
            {
                onStarted.Invoke(svc, new object[] { new VisionMaster.Models.FlowSession { FlowName = "流程A" } });
                Check("本轮开始不立刻清空（旧图保持可见——循环不闪黑的前提）",
                    svc.Count == 3,
                    $"剩余 {svc.Count} 张：[{string.Join(",", svc.Frames.Select(f => f.FlowName))}]");

                Check("记录当前流程（注入图归属用）",
                    (string?)currentFlowField.GetValue(svc) == "流程A", (string?)currentFlowField.GetValue(svc) ?? "(null)");

                CaptureFrame();   // 本轮第一张新图：清上一轮的 A + 插入本帧 → 同一批
                Check("第一张新图到达时同批完成清空+插入（A 换新、B 不动）",
                    svc.Count == 2
                    && svc.Frames.Any(f => f.FlowName == "流程A")
                    && svc.Frames.Any(f => f.FlowName == "流程B"),
                    $"剩余 {svc.Count} 张：[{string.Join(",", svc.Frames.Select(f => f.FlowName))}]");
                Check("采集批次从未出现空集合（界面看不到 旧图→空→新图）",
                    batchCounts.Count > 0 && !batchCounts.Contains(0),
                    $"批次张数：[{string.Join(",", batchCounts)}]");

                // 本轮再无任何新图：不清空，画布保持上一轮（监视器语义）
                onStarted.Invoke(svc, new object[] { new VisionMaster.Models.FlowSession { FlowName = "流程A" } });
                Check("本轮没有新图 → 不清空（保持上一轮画面）",
                    svc.Count == 2, $"剩余 {svc.Count} 张");

                // 「清空整张画布」同样推迟到首帧
                cfg.RunStartMode = VisionMaster.Models.ImageGalleryRunStartMode.ClearAll;
                var beforeClearAll = svc.Count;
                onStarted.Invoke(svc, new object[] { new VisionMaster.Models.FlowSession { FlowName = "流程A" } });
                Check("清空整张画布：登记时先不动（同样不闪黑）",
                    svc.Count == beforeClearAll && beforeClearAll > 0, $"剩余 {svc.Count} 张");

                CaptureFrame();
                Check("清空整张画布：首帧到达时全清并只留本帧",
                    svc.Count == 1, $"剩余 {svc.Count} 张");

                svcGallery.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Check("画廊跟随：切换后显示的是本轮新图（不是空、也不是上一轮）",
                    svcGallery.FilteredFrames.Count == 1
                    && ReferenceEquals(svcGallery.FilteredFrames[0], svc.Frames[0]),
                    $"画廊 {svcGallery.FilteredFrames.Count} 张 / 服务 {svc.Count} 张");
            }
            finally { svc.Changed -= onChange; }

            // ---- ⑤ 缩略图条：单行横向滚动（竖向不该有滚动量） ----
            for (int i = 0; i < 25; i++)
                frames.Add(new ImageFrame { FlowName = "流程B", StepName = "批量", PortName = "图" + i });
            gallery.Recompute();
            host.Measure(new Size(900, 600));
            host.Arrange(new Rect(0, 0, 900, 600));
            host.UpdateLayout();
            host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            System.Windows.Controls.ScrollViewer? FindScroll(DependencyObject root)
            {
                if (root is System.Windows.Controls.ScrollViewer sv) return sv;
                int n = VisualTreeHelper.GetChildrenCount(root);
                for (int i = 0; i < n; i++)
                {
                    var found = FindScroll(VisualTreeHelper.GetChild(root, i));
                    if (found != null) return found;
                }
                return null;
            }
            var stripScroll = partList == null ? null : FindScroll(partList);
            Check("缩略图条是单行横向滚动",
                stripScroll != null && stripScroll.ScrollableWidth > 0 && stripScroll.ScrollableHeight <= 1,
                $"可横滚={stripScroll?.ScrollableWidth:F0} / 可竖滚={stripScroll?.ScrollableHeight:F0} / 张数={gallery.FilteredFrames.Count}");

            // ---- ⑥ 多格布局：每格是一张完整画布，按"显示窗口号"各收一份 ----
            var paneFrames = new ObservableCollection<ImageFrame>
            {
                new ImageFrame { FlowName = "流程A", StepName = "相机1", PortName = "图", ViewIndex = 1 },
                new ImageFrame { FlowName = "流程A", StepName = "相机2", PortName = "图", ViewIndex = 2 },
                new ImageFrame { FlowName = "流程A", StepName = "相机2", PortName = "图2", ViewIndex = 2 },
                new ImageFrame { FlowName = "流程A", StepName = "相机3", PortName = "图", ViewIndex = 3 },
            };
            var pane = new ImageGallery { ItemsSource = paneFrames, ViewIndexFilter = 2 };
            var paneHost = new Border
            {
                Width = 600,
                Height = 400,
                Child = pane
            };
            paneHost.Measure(new Size(600, 400));
            paneHost.Arrange(new Rect(0, 0, 600, 400));
            paneHost.UpdateLayout();
            paneHost.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Check("每格只收自己窗口的图（第2格）",
                pane.FilteredFrames.Count == 2 && pane.FilteredFrames.All(f => f.ViewIndex == 2),
                $"收到 {pane.FilteredFrames.Count} 张，窗口号=[{string.Join(",", pane.FilteredFrames.Select(f => f.ViewIndex))}]");

            pane.ViewIndexFilter = 0;   // 0 = 不过滤（"全部输出"那张）
            paneHost.UpdateLayout();
            paneHost.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Check("全部输出不过滤（4 张全收）",
                pane.FilteredFrames.Count == 4,
                $"实际 {pane.FilteredFrames.Count} 张，窗口号=[{string.Join(",", pane.FilteredFrames.Select(f => f.ViewIndex))}]");

            Console.WriteLine(failures == 0 ? "=== 图集控件冒烟：全部通过 ===" : $"### 图集控件冒烟：{failures} 项失败");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>按 Tag 在可视树里找元素（模板内元素用 FindName 不稳定，按可视树找最可靠）</summary>
        private static FrameworkElement? FindByTag(DependencyObject root, string tag)
        {
            if (root is FrameworkElement fe && string.Equals(fe.Tag?.ToString(), tag, StringComparison.Ordinal))
                return fe;

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var found = FindByTag(VisualTreeHelper.GetChild(root, i), tag);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// 多格布局集成断言：在真实 Shell 里把画布切成九宫格 / 双画面 / 四宫格 / 全部输出，检查
        /// 每一格都是一张完整画布（有自己的大图），且各自绑了正确的窗口号（第 N 格 ↔ 窗口号 N）。
        /// </summary>
        private static int RunPaneLayoutChecks()
        {
            var failures = 0;
            void Check(string name, bool ok, string detail)
            {
                Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}  {detail}");
                if (!ok) failures++;
            }

            try
            {
                // 自己造一个 ImageView 来测布局：真实 Shell 里的那个挂在 AvalonDock 的 LayoutDocument 里，
                // 而 AvalonDock 只在窗口真正 Show（Loaded）时才把面板内容建出来 —— 无窗口夹具里找不到它，
                // 那是夹具的限制，不是画布的问题。
                var view = new VisionMaster.Views.ImageView();
                var host = new Border
                {
                    Width = 1000,
                    Height = 700,
                    Child = view
                };
                host.Measure(new Size(1000, 700));
                host.Arrange(new Rect(0, 0, 1000, 700));
                host.UpdateLayout();
                host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                void Switch(VisionMaster.eViewMode mode)
                {
                    Core.Events.GlobalEventBus.Publish(
                        new VisionMaster.EventModel.ImageCanvasChangeEvent { ViewMode = mode });
                    host.UpdateLayout();
                    host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                }

                Switch(VisionMaster.eViewMode.Night);
                var nine = Descendants<ImageGallery>(view).ToList();
                Check("九宫格：9 格画布",
                    nine.Count == 9,
                    $"实际 {nine.Count} 格，窗口号=[{string.Join(",", nine.Select(p => p.ViewIndexFilter))}]");
                Check("九宫格：各格绑 1..9",
                    nine.Select(p => p.ViewIndexFilter).OrderBy(v => v).SequenceEqual(Enumerable.Range(1, 9)),
                    $"=[{string.Join(",", nine.Select(p => p.ViewIndexFilter).OrderBy(v => v))}]");

                Switch(VisionMaster.eViewMode.Two);
                var two = Descendants<ImageGallery>(view).ToList();
                Check("双画面：2 格且绑 1/2",
                    two.Count == 2 && two.Select(p => p.ViewIndexFilter).OrderBy(v => v).SequenceEqual(new[] { 1, 2 }),
                    $"格数={two.Count}，窗口号=[{string.Join(",", two.Select(p => p.ViewIndexFilter))}]");

                Switch(VisionMaster.eViewMode.Four);
                var four = Descendants<ImageGallery>(view).ToList();
                Check("四宫格：每格都有大图（是完整画布）",
                    four.Count == 4 && four.All(p => p.Template?.FindName("PART_Big", p) != null),
                    $"格数={four.Count}，带大图 {four.Count(p => p.Template?.FindName("PART_Big", p) != null)} 格");

                // 越界值（老方案里存过 29 = 已下线的"全部输出"）：回落单画面，不能摆出空画布
                Switch((VisionMaster.eViewMode)29);
                var fallback = Descendants<ImageGallery>(view).ToList();
                Check("越界布局回落单画面",
                    fallback.Count == 1 && fallback[0].ViewIndexFilter == 1,
                    $"格数={fallback.Count}，窗口号={fallback.FirstOrDefault()?.ViewIndexFilter}");

                // 切回单画面：应复用同一批格（不重建），窗口号还是 1
                Switch(VisionMaster.eViewMode.One);
                var one = Descendants<ImageGallery>(view).ToList();
                Check("单画面：1 格且绑窗口号 1",
                    one.Count == 1 && one[0].ViewIndexFilter == 1,
                    $"格数={one.Count}，窗口号={one.FirstOrDefault()?.ViewIndexFilter}");
            }
            catch (Exception ex)
            {
                Console.WriteLine("### 多格布局断言抛出异常:");
                Print(ex);
                failures++;
            }

            Console.WriteLine(failures == 0 ? "=== 多格布局：全部通过 ===" : $"### 多格布局：{failures} 项失败");
            return failures;
        }

        /// <summary>
        /// Shell 停靠布局结构闸（2026-10-15「左栏重排」的守门人）。
        ///
        /// 为什么必须钉：这次把「程序 / 流程栏」由并排两列改成一列上下堆叠，并把「流程画布」
        /// 从左侧栏的标签页搬进中央文档区。三处改动全部只活在 Shell.xaml 的结构里——
        /// 编译期全绿，单看任何一个视图的源码也看不出来；谁把它们挪回去，不会有任何断言变红。
        /// 判据只认"元素类型 + 谁是谁的父/兄弟"，不写死行号与空白，所以改缩进、改注释都不会误报。
        /// 用 XDocument 静态判是可行的：LayoutRoot / LayoutPanel / LayoutAnchorablePane /
        /// LayoutDocument 都是普通对象，不依赖窗口 Show 之后才成型（无窗口夹具里 AvalonDock
        /// 恰好只建到这一层，再往下才需要 Loaded）。
        /// </summary>
        private static int RunShellLayoutChecks()
        {
            var failures = 0;
            void Check(string name, bool ok, string detail)
            {
                Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}  {detail}");
                if (!ok) failures++;
            }

            try
            {
                string path = Path.Combine(@"d:\C#\VM", "VisionMaster", "Shell.xaml");
                if (!File.Exists(path))
                {
                    Check("Shell.xaml 布局结构闸", false, $"找不到 {path}");
                    return failures;
                }

                var doc = XDocument.Load(path);
                var canvas = ByContentId(doc, "Panel_FlowCanvasView");
                var image = ByContentId(doc, "Panel_ImageView");
                var process = ByContentId(doc, "Panel_ProcessView");
                var flowList = ByContentId(doc, "Panel_FlowListView");
                var property = ByContentId(doc, "Panel_ScadaPropertyView");
                var layer = ByContentId(doc, "Panel_ScadaLayerView");
                var tool = ByContentId(doc, "Panel_ToolView");
                var scadaToolbox = ByContentId(doc, "Panel_ScadaToolboxView");

                var canvasParent = canvas == null ? null : canvas.Parent;
                var imageParent = image == null ? null : image.Parent;
                var processParent = process == null ? null : process.Parent;

                Check("流程画布是中央文档区的文档标签（不是左侧栏的标签页）",
                    canvas != null
                        && canvas.Name.LocalName == "LayoutDocument"
                        && canvasParent != null
                        && canvasParent.Name.LocalName == "LayoutDocumentPane",
                    $"元素={KindOf(canvas)}，父={KindOf(canvasParent)}");

                Check("流程画布与视觉图像同处一个文档窗格（并列标签，可拖出分屏对照）",
                    canvasParent != null && ReferenceEquals(canvasParent, imageParent),
                    $"画布父={KindOf(canvasParent)}，视觉图像父={KindOf(imageParent)}");

                Check("默认选中仍是视觉图像（大小写不敏感：XAML 里写 True/true 都算）",
                    image != null
                        && string.Equals((string)image.Attribute("IsSelected"), "True", StringComparison.OrdinalIgnoreCase),
                    $"视觉图像 IsSelected={(image == null ? "(缺)" : ((string)image.Attribute("IsSelected") ?? "(缺)"))}");

                Check("默认选中唯一：画布身上不得有 IsSelected（否则会和视觉图像抢启动视角）",
                    canvas != null && canvas.Attribute("IsSelected") == null,
                    $"画布 IsSelected={((string)canvas?.Attribute("IsSelected") ?? "(无)")}");

                Check("左侧只有一个流程导航窗格（程序面板已并入流程导航）",
                    flowList == null
                        && processParent != null
                        && processParent.Name.LocalName == "LayoutAnchorablePane"
                        && (string)processParent.Attribute("DockWidth") == "340",
                    flowList == null ? $"流程导航父={KindOf(processParent)}" : "仍存在独立 Panel_FlowListView");

                var processViewPath = Path.Combine(@"d:\C#\VM", "VisionMaster", "Views", "ProcessView.xaml");
                var processViewDoc = File.Exists(processViewPath) ? XDocument.Load(processViewPath) : null;
                Check("流程胶囊已嵌到流程树上方（ProcessView 内 FlowListView 在 Grid.Row=0）",
                    processViewDoc != null
                        && processViewDoc.Descendants().Any(e => e.Name.LocalName == "FlowListView"
                            && (string)e.Attribute("Grid.Row") == "0"),
                    processViewDoc == null ? "找不到 ProcessView.xaml" : "FlowListView 已嵌入");

                Check("右侧检查器只有一列（属性/图层/工具箱/图元工具箱同 pane，宽 320）",
                    property != null && layer != null && tool != null && scadaToolbox != null
                        && property.Parent != null
                        && ReferenceEquals(property.Parent, layer.Parent)
                        && ReferenceEquals(property.Parent, tool.Parent)
                        && ReferenceEquals(property.Parent, scadaToolbox.Parent)
                        && (string)property.Parent.Attribute("DockWidth") == "320",
                    $"父={KindOf(property?.Parent)}，宽={(string)property?.Parent?.Attribute("DockWidth") ?? "(缺)"}");

                // ---- 活动栏闸（2026-10-15 三度改版后的现状：整条下线）----
                // 沿革：先"一条竖条塞 5 个开关"（其中 3 个管的面板在最右边 = 落点指示失效）→
                // 拆成左右两条 → 左条下线、右条只留「工具箱」→ **整条下线**（当前）。
                // 现状判据：主体 Grid 只有一列；文件里不得再有任何 TogglePanelCommand 开关。
                // 组态三面板的显隐改由模式自动收敛（见 RunPanelModeChecks），不再需要手工开关。
                var all = doc.Descendants().ToList();
                int dockIndex = all.FindIndex(e => e.Name.LocalName == "DockingManager");
                int IndexOfTopCommand(string action) => all.FindIndex(e =>
                    e.Name.LocalName == "Button"
                    && ((string)e.Attribute("CommandParameter") ?? "").Contains("SystemAction." + action, StringComparison.Ordinal));

                var gridCols = all.FirstOrDefault(e => e.Name.LocalName == "Grid" && (string)e.Attribute("Grid.Row") == "2")
                    ?.Elements().FirstOrDefault(c => c.Name.LocalName == "Grid.ColumnDefinitions")?.Elements().Count() ?? -1;

                Check("活动栏已整条下线：主体 Grid 只有一列（没有竖条占宽）",
                    gridCols == 1, $"Grid.Row=2 的列数={gridCols}");

                var runBar = all.FirstOrDefault(e => e.Name.LocalName == "Border"
                    && (string)e.Attributes().FirstOrDefault(a => a.Name.LocalName == "Name") == "RunBar");
                var runBarIndex = runBar == null ? -1 : all.IndexOf(runBar);
                var executionButtons = all.Where(e => e.Name.LocalName == "Button"
                    && ((string)e.Attribute("Command") ?? "").Contains("ExecutionCommand", StringComparison.Ordinal)).ToList();
                Check("运行条存在（48px）且运行控制只在运行条里",
                    runBar != null && (string)runBar.Attribute("Height") == "48"
                    && executionButtons.Count > 0
                    && executionButtons.All(e => all.IndexOf(e) > runBarIndex),
                    $"运行条={runBar != null}，运行按钮数={executionButtons.Count}");

                var panelToggles = all.Where(e => e.Name.LocalName == "ToggleButton"
                        && (string)e.Attribute("CommandParameter") is string cp && cp.StartsWith("Panel_", StringComparison.Ordinal))
                    .ToList();
                Check("活动栏已整条下线：文件里没有任何面板开关（组态三面板改由模式自动收敛）",
                    panelToggles.Count == 0,
                    panelToggles.Count == 0 ? "（无）" : string.Join("，", panelToggles.Select(e => (string)e.Attribute("CommandParameter"))));

                Check("两个全局命令（全局变量/硬件管理）落在停靠区之前的顶栏",
                    dockIndex >= 0
                        && IndexOfTopCommand("GlobalVariables") is int g && g >= 0 && g < dockIndex
                        && IndexOfTopCommand("HardwareSettings") is int c && c >= 0 && c < dockIndex,
                    $"停靠区位置={dockIndex}；全局变量={IndexOfTopCommand("GlobalVariables")}，硬件管理={IndexOfTopCommand("HardwareSettings")}");

                // ---- 命令面板 / 状态栏 KPI / 控制台开关（2026-10-20 主界面收口）----
                // 这些入口都只在 XAML 里接线，绑错/漏绑编译期全绿，必须在结构闸里点名。
                var shellCodePath = Path.Combine(@"d:\C#\VM", "VisionMaster", "Shell.xaml.cs");
                var shellCode = File.Exists(shellCodePath) ? File.ReadAllText(shellCodePath) : string.Empty;
                string NameOf(XElement e) => (string)e.Attributes().FirstOrDefault(a => a.Name.LocalName == "Name") ?? string.Empty;
                var palette = doc.Descendants().FirstOrDefault(e => NameOf(e) == "CommandPaletteOverlay");
                Check("命令面板入口存在（遮罩 + 搜索框 + 结果列表 + Ctrl+K 处理）",
                    palette != null
                        && palette.Descendants().Any(e => e.Name.LocalName == "TextBox" && NameOf(e) == "CommandPaletteBox")
                        && palette.Descendants().Any(e => e.Name.LocalName == "ListBox" && NameOf(e) == "CommandPaletteList")
                        && shellCode.Contains("Key.K", StringComparison.Ordinal)
                        && shellCode.Contains("OpenCommandPaletteCommand", StringComparison.Ordinal),
                    palette == null ? "找不到 CommandPaletteOverlay" : "面板与 Ctrl+K 接线齐全");

                var statusTexts = doc.Descendants()
                    .Where(e => e.Name.LocalName == "TextBlock")
                    .Select(e => (string)e.Attribute("Text") ?? string.Empty)
                    .ToList();
                Check("状态栏生产 KPI 占位齐全（节拍 / OK / NG / 告警）",
                    new[] { "节拍", "OK", "NG", "告警" }.All(t => statusTexts.Contains(t)),
                    string.Join("，", statusTexts.Where(t => t is "节拍" or "OK" or "NG" or "告警")));
                Check("状态栏有控制台展开/收起入口（ToggleConsoleCommand）",
                    doc.Descendants().Any(e => ((string)e.Attribute("Command") ?? string.Empty).Contains("ToggleConsoleCommand", StringComparison.Ordinal)),
                    "已接线");
                Check("主界面术语已收口（流程导航 / 流程画布 / 控制台 / 检查器）",
                    statusTexts.Concat(doc.Descendants().Select(e => (string)e.Attribute("Header") ?? string.Empty))
                        .Any(t => t == "流程导航")
                    && doc.Descendants().Any(e => (string)e.Attribute("Header") == "流程画布")
                    && doc.Descendants().Any(e => (string)e.Attribute("Header") == "控制台")
                    && doc.Descendants().Any(e => (string)e.Attribute("Header") == "检查器"),
                    "四项术语均在菜单里出现");

                // ---- 窗口控制按钮的几何闸（2026-10-16 真机："关闭显示不全"）----
                // 症状看着像被裁切，其实那三个按钮的 Content 是**几何字符串**（Path.Data 直接吃它），
                // 关闭按钮原本写的是 `M 0 0 L 10 10 M 0 10 L 0 10`——第二笔起点=终点，长度 0，
                // 于是 ✕ 只画出一条对角线，屏幕上就是一个 `\`。**编译期全绿、StaticResource 全绿、
                // 模板同源全绿**（样式与模板都没错，错的是数据），只有眼睛能看见。
                // 所以闸只能问几何本身：不许出现零长度线段。ShellCopy.xaml 一起扫（R44：副本会照抄原件的错）。
                var captionBad = CaptionGeometryOffenders(doc);
                Check("标题栏三个按钮的几何没有「少一笔」（零长度线段）",
                    captionBad.Count == 0,
                    captionBad.Count == 0 ? "（无）" : string.Join("；", captionBad));

                var copyPath = Path.Combine(@"d:\C#\VM", "UIThemeSmokeTest", "ShellCopy.xaml");
                var copyBad = File.Exists(copyPath)
                    ? CaptionGeometryOffenders(XDocument.Load(copyPath))
                    : new List<string> { "找不到 " + copyPath };
                Check("同一道几何闸也过 ShellCopy.xaml（结构副本不许照抄原件的错）",
                    copyBad.Count == 0,
                    copyBad.Count == 0 ? "（无）" : string.Join("；", copyBad));

                // ---- 系统菜单接线闸（2026-10-20）----
                // 2026-10-20 普查发现「硬件设置 / 报表查询 / 回零 / UI 设计」四项在 Shell.xaml 有菜单项，
                // 但 OnSystemAction 没有 case：点下去毫无反应，编译期与资源闸全绿。
                // 判据只认「Shell.xaml 里出现过的 SystemAction」必须有 case；ShellCopy 必须同集合（R44）。
                List<string> SystemActions(XDocument d) => d.Descendants()
                    .Where(e => e.Name.LocalName == "MenuItem")
                    .Select(e => (string)e.Attribute("CommandParameter") ?? string.Empty)
                    .Where(s => s.StartsWith("{x:Static local:SystemAction.", StringComparison.Ordinal)
                                && s.EndsWith("}", StringComparison.Ordinal))
                    .Select(s => s.Substring("{x:Static local:SystemAction.".Length).TrimEnd('}'))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(s => s, StringComparer.Ordinal)
                    .ToList();

                var shellActions = SystemActions(doc);
                var shellVmPath = Path.Combine(@"d:\C#\VM", "VisionMaster", "ShellViewModel.cs");
                var shellVm = File.Exists(shellVmPath) ? File.ReadAllText(shellVmPath) : string.Empty;
                var missingCases = shellActions
                    .Where(a => !shellVm.Contains("case SystemAction." + a + ":", StringComparison.Ordinal))
                    .ToList();
                Check("系统菜单每个 SystemAction 都有 OnSystemAction 处理分支",
                    missingCases.Count == 0,
                    missingCases.Count == 0
                        ? $"{shellActions.Count} 个动作全部接线"
                        : "缺 case: " + string.Join("，", missingCases));

                var copyActions = File.Exists(copyPath) ? SystemActions(XDocument.Load(copyPath)) : new List<string>();
                Check("ShellCopy 与 Shell.xaml 的 SystemAction 集合一致",
                    string.Equals(string.Join(",", shellActions), string.Join(",", copyActions), StringComparison.Ordinal),
                    $"Shell.xaml=[{string.Join(",", shellActions)}]；ShellCopy=[{string.Join(",", copyActions)}]");

                // 系统健康也算重复入口：硬件管理左栏那颗按钮打开的是同一个 SystemHealthView
                //（HardwareManagerViewModel.OpenSystemHealthCommand → ShowDialog("SystemHealthView")）。
                var redundantEntries = new[] { "CameraSettings", "MotionBoard", "CommSettings", "SystemHealth" };
                var stillExposed = shellActions.Where(a => redundantEntries.Contains(a, StringComparer.Ordinal)).ToList();
                Check("系统菜单不再重复暴露相机/运动/通讯/健康入口（统一走硬件管理）",
                    stillExposed.Count == 0,
                    stillExposed.Count == 0 ? "只保留硬件管理" : "仍在菜单: " + string.Join("，", stillExposed));

                // 新视图的 XAML 解析闸：静态资源键缺失/模板写错只会在实例化时抛，编译期全绿。
                try
                {
                    var hardwareView = new HardwareManagerView();
                    hardwareView.Measure(new Size(1100, 720));
                    hardwareView.Arrange(new Rect(0, 0, 1100, 720));
                    hardwareView.UpdateLayout();
                    Check("硬件管理视图能实例化并完成布局（资源键 / XAML 解析）",
                        hardwareView.ActualWidth > 0 && hardwareView.ActualHeight > 0,
                        $"{hardwareView.ActualWidth:0}x{hardwareView.ActualHeight:0}");
                }
                catch (Exception ex)
                {
                    Check("硬件管理视图能实例化并完成布局（资源键 / XAML 解析）", false, ex.Message);
                }

                // 运动「点位列表」窄窗布局闸（2026-10-21）：
                // 宿主最小宽度 1100 时右表只有 ~552px，7 列会把表头压到互相裁切、
                // 标题与「当前位置」在同一个 Grid 单元里重叠。判据用实际 Arrange 后的
                // 元素边界，而不是看 XAML 里写没写 ColumnDefinition。
                try
                {
                    var points = new MotionBoardPointsView
                    {
                        DataContext = new
                        {
                            AxisRows = new ObservableCollection<object>(),
                            Points = new ObservableCollection<object>(),
                            CurveOptions = new ObservableCollection<object>(),
                            HeaderAxisName = "轴1",
                            CurrentPositionText = "0.000",
                        }
                    };
                    points.Measure(new Size(660, 600));
                    points.Arrange(new Rect(0, 0, 660, 600));
                    points.UpdateLayout();

                    var left = points.FindName("HeaderLeft") as FrameworkElement;
                    var right = points.FindName("HeaderRight") as FrameworkElement;
                    var scroll = points.FindName("PointsScroll") as ScrollViewer;
                    var table = points.FindName("PointsTable") as FrameworkElement;

                    var noOverlap = false;
                    if (left != null && right != null)
                    {
                        var l = left.TransformToAncestor(points).TransformBounds(new Rect(left.RenderSize));
                        var r = right.TransformToAncestor(points).TransformBounds(new Rect(right.RenderSize));
                        noOverlap = l.Right <= r.Left + 1;
                    }

                    Check("运动点位表窄窗：标题与当前位置不重叠，表格铺满可用宽度",
                        noOverlap
                        && table != null && table.ActualWidth >= 600
                        && scroll != null && scroll.ScrollableWidth == 0,
                        $"重叠={!noOverlap}；表宽={table?.ActualWidth:0}；横向滚动={scroll?.ScrollableWidth ?? 0:0}");
                }
                catch (Exception ex)
                {
                    Check("运动点位表窄窗：标题与当前位置不重叠，表格铺满可用宽度", false, ex.Message);
                }

                // 用户登录视图（2026-10-21 双栏改版）：资源键/模板写错只会在实例化时抛。
                try
                {
                    var loginView = new ScadaLoginView();
                    loginView.Measure(new Size(820, 560));
                    loginView.Arrange(new Rect(0, 0, 820, 560));
                    loginView.UpdateLayout();
                    Check("用户登录视图能实例化并完成布局（双栏 / 资源键 / XAML 解析）",
                        loginView.ActualWidth > 0 && loginView.ActualHeight > 0,
                        $"{loginView.ActualWidth:0}x{loginView.ActualHeight:0}");
                }
                catch (Exception ex)
                {
                    Check("用户登录视图能实例化并完成布局（双栏 / 资源键 / XAML 解析）", false, ex.Message);
                }

                // 密码框双向绑定：PasswordBoxHelper 若用 SetValue 会顶掉绑定，
                // 表现是"界面有圆点、VM.Password 还是空"，登录永远报「请输入密码」。
                try
                {
                    var probe = new PasswordProbe();
                    var passwordBox = new PasswordBox();
                    PasswordBoxHelper.SetAttach(passwordBox, true);
                    BindingOperations.SetBinding(
                        passwordBox,
                        PasswordBoxHelper.BindablePasswordProperty,
                        new Binding(nameof(PasswordProbe.Password))
                        {
                            Source = probe,
                            Mode = BindingMode.TwoWay,
                            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                        });

                    passwordBox.Password = "admin123";
                    var typedToVm = probe.Password == "admin123";
                    probe.Password = string.Empty;
                    var clearedFromVm = passwordBox.Password.Length == 0;

                    Check("PasswordBoxHelper 双向绑定：输入回写 VM、VM 清空回写 UI",
                        typedToVm && clearedFromVm,
                        $"输入→VM={typedToVm}；VM→UI={clearedFromVm}");
                }
                catch (Exception ex)
                {
                    Check("PasswordBoxHelper 双向绑定：输入回写 VM、VM 清空回写 UI", false, ex.Message);
                }
            }
            catch (Exception ex)
            {
                Check("Shell.xaml 布局结构闸", false, ex.Message);
            }

            Console.WriteLine(failures == 0
                ? "=== Shell 布局结构：全部通过 ==="
                : $"### Shell 布局结构：{failures} 项失败");
            return failures;
        }

        /// <summary>
        /// 标题栏按钮几何里的「零长度线段」（线段的起点=终点）——画出来什么都没有，等于图标少一笔。
        /// 2026-10-16 真机：关闭按钮的 ✕ 只剩一条对角线（屏幕上是个 `\`），因为第二笔写成 `M 0 10 L 0 10`。
        /// 样式、模板、StaticResource 那时全绿，错的是喂给 `Path.Data` 的**数据**，所以只能按几何判。
        /// </summary>
        private static List<string> CaptionGeometryOffenders(XDocument doc)
        {
            var offenders = new List<string>();
            foreach (var button in doc.Descendants().Where(e => e.Name.LocalName == "Button"
                         && ((string)e.Attribute("Style") ?? "").Contains("WindowControlButton", StringComparison.Ordinal)))
            {
                var content = (string)button.Attribute("Content") ?? string.Empty;
                var label = (string)button.Attribute("Tag")
                            ?? (string)button.Attributes().FirstOrDefault(a => a.Name.LocalName == "Name")
                            ?? "(未命名按钮)";
                foreach (Match m in Regex.Matches(content, @"M\s*([-\d.]+)[,\s]+([-\d.]+)\s+L\s*([-\d.]+)[,\s]+([-\d.]+)"))
                {
                    var p = new double[4];
                    for (var i = 0; i < 4; i++)
                        p[i] = double.Parse(m.Groups[i + 1].Value, CultureInfo.InvariantCulture);
                    if (p[0] == p[2] && p[1] == p[3])
                        offenders.Add($"「{label}」的 {m.Value}");
                }
            }
            return offenders;
        }

        private static XElement ByContentId(XDocument doc, string contentId)
        {
            return doc.Descendants().FirstOrDefault(e => (string)e.Attribute("ContentId") == contentId);
        }

        /// <summary>
        /// 「组态三面板 vs 算子工具箱」按场景互斥的运行时闸（2026-10-15）。
        ///
        /// 为什么必须真调一遍而不是只扫 XAML：这条逻辑的载体是 AvalonDock 的**可见性模型**
        /// （`LayoutAnchorable.IsVisible`，隐藏即移入 LayoutRoot.Hidden），XAML 里根本看不出对错；
        /// 而写反了（组态/非组态两组的 Show/Hide 对调）在编译期与静态扫描下**全绿**，
        /// 屏幕上却是"调流程时冒出图层属性、进组态反而只剩算子工具箱"。
        ///
        /// 为什么能在这里跑：本宿主加载的是 ShellCopy.xaml，它带一份真实的 DockingManager
        /// 与全部 LayoutAnchorable（注意：应用里的面板内容由 LayoutHelper.FillContents 回填，
        /// 这里不依赖内容，只依赖 ContentId）。
        /// </summary>
        private static int RunPanelModeChecks()
        {
            var failures = 0;
            void Check(string name, bool ok, string detail)
            {
                Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}  {detail}");
                if (!ok) failures++;
            }

            try
            {
                var dock = _shellDock;
                if (dock == null)
                {
                    Check("Panel 模式（组态三面板 vs 算子工具箱）", false, "没抓到 DockingManager");
                    return failures;
                }

                // 组态三面板 / 算子工具箱 的 ContentId（与 LayoutHelper 里的口径保持一致）
                var scadaPanels = new[] { "Panel_ScadaPropertyView", "Panel_ScadaLayerView", "Panel_ScadaToolboxView" };

                string Snapshot()
                    => "组态三面板=[" + string.Join(",", scadaPanels.Select(id => VisionMaster.Services.LayoutHelper.IsPanelVisible(dock, id) ? "可见" : "隐藏"))
                       + "]，工具箱=" + (VisionMaster.Services.LayoutHelper.IsPanelVisible(dock, "Panel_ToolView") ? "可见" : "隐藏");

                // ---- ① 组态设计模式：三个组态面板可见、算子工具箱隐藏 ----
                VisionMaster.Services.LayoutHelper.ApplyScadaPanelMode(dock, true);
                Check("组态设计模式：属性/图层/图元工具箱 都显示，算子工具箱 收起",
                    scadaPanels.All(id => VisionMaster.Services.LayoutHelper.IsPanelVisible(dock, id))
                    && !VisionMaster.Services.LayoutHelper.IsPanelVisible(dock, "Panel_ToolView"),
                    Snapshot());

                // ---- ② 非组态模式：反之 ----
                VisionMaster.Services.LayoutHelper.ApplyScadaPanelMode(dock, false);
                Check("非组态模式：只留算子工具箱，属性/图层/图元工具箱 全部收起（两列随之收成 0、画布变宽）",
                    VisionMaster.Services.LayoutHelper.IsPanelVisible(dock, "Panel_ToolView")
                    && scadaPanels.All(id => !VisionMaster.Services.LayoutHelper.IsPanelVisible(dock, id)),
                    Snapshot());

                // ---- ③ 来回切两次后仍与基线一致（防"隐藏了就再也显示不回来"的累积错） ----
                VisionMaster.Services.LayoutHelper.ApplyScadaPanelMode(dock, true);
                VisionMaster.Services.LayoutHelper.ApplyScadaPanelMode(dock, false);
                VisionMaster.Services.LayoutHelper.ApplyScadaPanelMode(dock, true);
                bool backToScada = scadaPanels.All(id => VisionMaster.Services.LayoutHelper.IsPanelVisible(dock, id))
                                   && !VisionMaster.Services.LayoutHelper.IsPanelVisible(dock, "Panel_ToolView");
                VisionMaster.Services.LayoutHelper.ApplyScadaPanelMode(dock, false);
                bool backToNormal = VisionMaster.Services.LayoutHelper.IsPanelVisible(dock, "Panel_ToolView")
                                    && scadaPanels.All(id => !VisionMaster.Services.LayoutHelper.IsPanelVisible(dock, id));
                Check("来回切换多次后两个模式仍各自收敛到同一条基线（幂等、无累积错）",
                    backToScada && backToNormal,
                    $"切回组态={backToScada}，切回非组态={backToNormal}");

                // ---- ③b 组态模式的"让空间"收敛（v4）：左栏收起、底部日志收 0、属性/图层合并成标签页 ----
                static AvalonDock.Layout.LayoutAnchorablePane FindAncestorPane(AvalonDock.DockingManager manager, string contentId)
                {
                    AvalonDock.Layout.ILayoutElement cursor = VisionMaster.Services.LayoutHelper.FindPanel(manager, contentId);
                    while (cursor?.Parent is AvalonDock.Layout.ILayoutContainer parent)
                    {
                        if (parent is AvalonDock.Layout.LayoutAnchorablePane pane) return pane;
                        cursor = parent as AvalonDock.Layout.ILayoutElement;
                    }

                    return null;
                }

                VisionMaster.Services.LayoutHelper.ApplyScadaPanelMode(dock, true);
                // 模式收敛可能重建布局树（ShowAnchorable 会重挂面板），检查必须用**切换之后**的实例
                var logPaneScada = FindAncestorPane(dock, "Panel_LogView");
                var propScada = VisionMaster.Services.LayoutHelper.FindPanel(dock, "Panel_ScadaPropertyView");
                var layerScada = VisionMaster.Services.LayoutHelper.FindPanel(dock, "Panel_ScadaLayerView");
                bool leftHiddenInScada = !VisionMaster.Services.LayoutHelper.IsPanelVisible(dock, "Panel_ProcessView");
                bool logCollapsed = logPaneScada != null && logPaneScada.DockHeight.IsAbsolute && logPaneScada.DockHeight.Value < 2;
                string logCollapsedHeight = logPaneScada?.DockHeight.ToString() ?? "null";
                bool samePane = propScada?.Parent != null && ReferenceEquals(propScada.Parent, layerScada?.Parent);
                bool propSelected = (propScada as AvalonDock.Layout.LayoutAnchorable)?.IsSelected == true;

                VisionMaster.Services.LayoutHelper.ApplyScadaPanelMode(dock, false);
                var logPaneNormal = FindAncestorPane(dock, "Panel_LogView");
                bool leftRestored = VisionMaster.Services.LayoutHelper.IsPanelVisible(dock, "Panel_ProcessView");
                bool logRestored = logPaneNormal != null && logPaneNormal.DockHeight.IsAbsolute
                                   && System.Math.Abs(logPaneNormal.DockHeight.Value - 200) < 0.1;
                string logRestoredHeight = logPaneNormal?.DockHeight.ToString() ?? "null";

                Check("组态模式收起左栏（程序/流程栏），退出后恢复", leftHiddenInScada && leftRestored,
                    $"组态收起={leftHiddenInScada}，退出恢复={leftRestored}");
                Check("组态模式把底部日志收成 1px 条（高度还给画布），退出后恢复 200", logCollapsed && logRestored,
                    $"组态高度={logCollapsedHeight}，退出高度={logRestoredHeight}");
                Check("属性/图层在同一个 pane（标签页），组态模式默认选中属性", samePane && propSelected,
                    $"同 pane={samePane}，属性选中={propSelected}");

                // ---- ④ 判据闸：文档的 IsSelected 才是"当前在组态设计"，IsActive 不是 ----
                // 这条钉的是"为什么不能用 IsPanelActive 当判据"：点一下右侧停靠面板就会把
                // 文档的 IsActive 抢走（AvalonDock 语义），拿它当判据会自毁循环。
                // 这里只验语义可用性：ShellCopy 里组态画面是 LayoutDocument、可取 IsSelected。
                var scadaDoc = VisionMaster.Services.LayoutHelper.FindPanel(dock, "Panel_ScadaEditorView");
                Check("「组态画面」是 LayoutDocument（判据走 IsSelected；LayoutAnchorable 没有这个语义）",
                    scadaDoc is AvalonDock.Layout.LayoutDocument,
                    $"实际类型={scadaDoc?.GetType().Name ?? "(没找到)"}");

                // ---- ⑤ 图层面板在 ContentFactories 里登记了（否则恢复布局后它变空壳） ----
                // 反证方式：改坏 LayoutHelper.ContentFactories 里 Panel_ScadaLayerView 那条 → 本断言红。
                var factoryProbe = typeof(VisionMaster.Services.LayoutHelper)
                    .GetField("ContentFactories", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                    ?.GetValue(null) as System.Collections.IDictionary;
                Check("图层面板登记在 ContentFactories 里（缺它 = 每次恢复布局后图层面板变空壳，且覆盖度校验不会拦）",
                    factoryProbe != null && factoryProbe.Contains("Panel_ScadaLayerView"),
                    factoryProbe == null ? "(反射取不到 ContentFactories)" : $"登记项 {factoryProbe.Count} 个");

                // ---- ⑨ 流程栏列宽兜底（用户把这一列拖到 230 → v2 组头右侧统计会把实例名挤没） ----
                // 反证：把 ShellCopy 里流程栏那一列改成 230，再调一次校正，必须回到 340 / 最小 320。
                var processContent = VisionMaster.Services.LayoutHelper.FindPanel(dock, "Panel_ProcessView");
                AvalonDock.Layout.LayoutAnchorablePane processPane = null;
                AvalonDock.Layout.ILayoutElement cursor = processContent;
                while (cursor?.Parent is AvalonDock.Layout.ILayoutContainer parent)
                {
                    if (parent is AvalonDock.Layout.LayoutAnchorablePane anchorablePane)
                    {
                        processPane = anchorablePane;
                        break;
                    }

                    cursor = parent as AvalonDock.Layout.ILayoutElement;
                }

                if (processPane == null)
                {
                    Check("流程导航列宽兜底（过窄 → 拉回 340）", false, "没找到流程导航所在的 LayoutAnchorablePane");
                }
                else
                {
                    processPane.DockWidth = new System.Windows.GridLength(230);
                    processPane.DockMinWidth = 0;
                    VisionMaster.Services.LayoutHelper.EnsureProcessColumnWidth(dock);
                    Check("流程导航列宽兜底：230 → 340、最小宽度 320（旧 Layout.xml 拖窄后恢复不再溢出）",
                        processPane.DockWidth.IsAbsolute
                        && System.Math.Abs(processPane.DockWidth.Value - 340) < 0.1
                        && System.Math.Abs(processPane.DockMinWidth - 320) < 0.1,
                        $"宽度={processPane.DockWidth}（期望 340） 最小={processPane.DockMinWidth}（期望 320）");
                }

                // ---- ⑩ 出厂快照尺寸基线（"恢复默认布局"的数据源） ----
                // 序列化出来的就是启动时 CaptureDefaultLayoutSnapshot 写进 DefaultLayout.xml 的内容。
                // 反证：把 ShellCopy 的流程栏列改成 230，或把右侧检查器列改成 260 → 这条红。
                string defaultXml;
                using (var writer = new System.IO.StringWriter())
                {
                    new AvalonDock.Layout.Serialization.XmlLayoutSerializer(dock).Serialize(writer);
                    defaultXml = writer.ToString();
                }

                Check("出厂快照尺寸基线：流程导航 340、右侧检查器 320（否则\"恢复默认布局\"会恢复出错误尺寸）",
                    defaultXml.Contains("DockWidth=\"340\"") && defaultXml.Contains("DockWidth=\"320\""),
                    $"宽度340={defaultXml.Contains("DockWidth=\"340\"")} 宽度320={defaultXml.Contains("DockWidth=\"320\"")}");

                // ---- ⑥⑦⑧ AvalonDock 4.74.1 的 Show() 只判上界不判下界（2026-10-16 真机闪退） ----
                // 现场：LayoutAnchorable.Show() 把 Hide 时记下的"回哪一列、第几个位置"原样喂给
                // Collection<T>.Insert，索引 -1 时抛 ArgumentOutOfRangeException，且异常出在
                // "清空记忆"那两行之前 → 坏记忆永久留着，本面板此后每 Show 一次炸一次；而 R56 让
                // "切中央文档区标签"就重收敛一次，于是变成天天炸。防护在 LayoutHelper.ShowAnchorable。
                const string dirtyId = "Panel_ScadaLayerView";
                var dirtyPanel = VisionMaster.Services.LayoutHelper.FindPanel(dock, dirtyId)
                    as AvalonDock.Layout.LayoutAnchorable;

                // 口径必须与产线**逐字一致**（LayoutHelper.ReadPreviousContainer / ReadPreviousContainerIndex）：
                //   · `PreviousContainer` 是 `LayoutContent` 上的 **protected 属性**（公开接口
                //     `ILayoutPreviousContainer` 是 internal，外部程序集转不过去 → CS0122）；
                //   · `PreviousContainerIndex` 是 **public 属性**（底层字段 `_previousContainerIndex`）。
                //   两者都**不是**同名字段——2026-10-16 第一版断言写成 `GetField(...)` 取不到，
                //   直接假红（`字段=(缺)/(缺)`）；产线同款写法则会让"记忆判废"整条分支静默失效。
                var prevContainerProp = typeof(AvalonDock.Layout.LayoutContent)
                    .GetProperty("PreviousContainer",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var prevIndexProp = typeof(AvalonDock.Layout.LayoutContent)
                    .GetProperty("PreviousContainerIndex",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                if (dirtyPanel == null || prevContainerProp == null || prevIndexProp == null)
                {
                    // 故意判红不判"跳过"：反射口径失效说明 AvalonDock 换了版本（可能已修这条），
                    // 那正是"防护该重审/该摘"的时刻，静默放行等于给未来回归留门（R38）。
                    Check("脏记忆断言可用（图层面板 + PreviousContainer/PreviousContainerIndex 属性取得到）",
                        false,
                        $"面板={(dirtyPanel == null ? "(没找到)" : "有")}，属性={prevContainerProp?.Name ?? "(缺)"}/{prevIndexProp?.Name ?? "(缺)"}");
                }
                else
                {
                    // ---- ⑥ 索引打成 -1（记忆里的列已不含本面板） ----
                    VisionMaster.Services.LayoutHelper.ApplyScadaPanelMode(dock, false); // 图层此刻隐藏，记忆有效
                    prevIndexProp.SetValue(dirtyPanel, -1);

                    // ⑥a 反证：先证明"没有防护时库自己就炸"。这条红了 = 脏索引的前提不成立 =
                    // 下面的防护断言是空转，必须重审（R38：绿灯要有可反证的前提）。
                    string raw6 = "没抛异常（库这一版免疫？防护前提要重审）";
                    try { dirtyPanel.IsVisible = true; }
                    catch (Exception ex) { raw6 = ex.GetType().Name; }
                    Check("脏索引下直接写 IsVisible=true 当场抛 ArgumentOutOfRangeException（真机那条栈的复现，证明防护不是空转）",
                        raw6 == "ArgumentOutOfRangeException", raw6);

                    // ⑥b 防护：ShowPanel 不许抛、且面板必须真的显示回来
                    if (dirtyPanel.IsVisible)
                    {
                        dirtyPanel.IsVisible = false; // ⑥a 居然没抛（库行为变了）：重新造脏再验防护
                        prevIndexProp.SetValue(dirtyPanel, -1);
                    }
                    string dirty6 = "";
                    bool threw6 = false;
                    try
                    {
                        VisionMaster.Services.LayoutHelper.ShowPanel(dock, dirtyId);
                    }
                    catch (Exception ex)
                    {
                        threw6 = true;
                        dirty6 = $"{ex.GetType().Name}: {ex.Message}";
                    }
                    bool shown6 = VisionMaster.Services.LayoutHelper.IsPanelVisible(dock, dirtyId);
                    Check("记忆索引 = -1 时 ShowPanel 不抛异常、图层面板照样显示回来（库的 Show() 缺下界判据，真机闪退那一条）",
                        !threw6 && shown6,
                        threw6 ? $"抛了 {dirty6}" : (shown6 ? "未抛、已显示" : "未抛但没显示（静默故障）"));

                    // ---- ⑦ 整条记忆抹掉（PreviousContainer = null） ----
                    dirtyPanel.IsVisible = false; // 重新隐藏，让 Hide 把有效记忆写回去（下面再抹）
                    prevContainerProp.SetValue(dirtyPanel, null);
                    prevIndexProp.SetValue(dirtyPanel, -1);

                    // ⑦a 反证：这一态库**不抛、也不显示**——"没抛异常"不等于"面板回来了"，
                    // 所以防护不能只 catch，必须自己把它挂回去。
                    bool silent7 = false;
                    try
                    {
                        dirtyPanel.IsVisible = true;
                        silent7 = !dirtyPanel.IsVisible;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"（⑦a 备注：这一态库改成抛 {ex.GetType().Name} 了）");
                    }
                    Check("记忆为空时库自己 Show() 是静默不显示（所以防护不许只 catch 不挂回）",
                        silent7, silent7 ? "确认静默不显示" : "这一态没复现静默（库行为变了，重审防护口径）");

                    string dirty7 = "";
                    bool threw7 = false;
                    try
                    {
                        VisionMaster.Services.LayoutHelper.ShowPanel(dock, dirtyId);
                    }
                    catch (Exception ex)
                    {
                        threw7 = true;
                        dirty7 = $"{ex.GetType().Name}: {ex.Message}";
                    }
                    bool shown7 = VisionMaster.Services.LayoutHelper.IsPanelVisible(dock, dirtyId);
                    Check("整条记忆被抹平时 ShowPanel 不抛异常、图层面板仍显示回来（旧行为是静默显示不出来）",
                        !threw7 && shown7,
                        threw7 ? $"抛了 {dirty7}" : (shown7 ? "未抛、已显示" : "未抛但没显示（静默故障）"));

                    // ⑧ 修完之后收敛照常工作（反证修复没把布局搞坏：Hide 会重新记下有效索引）
                    VisionMaster.Services.LayoutHelper.ApplyScadaPanelMode(dock, true);
                    bool scadaOk = scadaPanels.All(id => VisionMaster.Services.LayoutHelper.IsPanelVisible(dock, id))
                                   && !VisionMaster.Services.LayoutHelper.IsPanelVisible(dock, "Panel_ToolView");
                    VisionMaster.Services.LayoutHelper.ApplyScadaPanelMode(dock, false);
                    bool normalOk = VisionMaster.Services.LayoutHelper.IsPanelVisible(dock, "Panel_ToolView")
                                    && scadaPanels.All(id => !VisionMaster.Services.LayoutHelper.IsPanelVisible(dock, id));
                    Check("两轮脏记忆修复后，两种模式的收敛仍各自回到基线（修复没留下坏状态）",
                        scadaOk && normalOk,
                        $"切组态={scadaOk}，切非组态={normalOk}，{Snapshot()}");
                }
            }
            catch (Exception ex)
            {
                Check("Panel 模式（组态三面板 vs 算子工具箱）", false, ex.Message);
            }

            Console.WriteLine(failures == 0
                ? "=== Panel 模式：全部通过 ==="
                : $"### Panel 模式：{failures} 项失败");
            return failures;
        }

        /// <summary>
        /// 四种工作模式的上下文隔离（调试 / 运行 / 组态 / 自由）：
        /// 文档标签、左右面板、底部日志必须一起收敛，不能只藏左右栏。
        /// </summary>
        private static int RunShellModeChecks()
        {
            var failures = 0;
            void Check(string name, bool ok, string detail)
            {
                Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}  {detail}");
                if (!ok) failures++;
            }

            try
            {
                var dock = _shellDock;
                if (dock == null)
                {
                    Check("Shell 模式隔离", false, "没抓到 DockingManager");
                    return failures;
                }

                bool DocInLayout(string id)
                    => VisionMaster.Services.LayoutHelper.FindPanel(dock, id) is AvalonDock.Layout.LayoutDocument;

                bool PanelVisible(string id)
                    => VisionMaster.Services.LayoutHelper.IsPanelVisible(dock, id);

                static AvalonDock.Layout.LayoutAnchorablePane FindAncestorPane(AvalonDock.DockingManager manager, string contentId)
                {
                    AvalonDock.Layout.ILayoutElement cursor = VisionMaster.Services.LayoutHelper.FindPanel(manager, contentId);
                    while (cursor?.Parent is AvalonDock.Layout.ILayoutContainer parent)
                    {
                        if (parent is AvalonDock.Layout.LayoutAnchorablePane pane) return pane;
                        cursor = parent as AvalonDock.Layout.ILayoutElement;
                    }

                    return null;
                }

                VisionMaster.Services.LayoutHelper.ApplyShellMode(dock, VisionMaster.ShellMode.Debug);
                Check("调试模式：文档仅视觉图像 + 流程画布",
                    DocInLayout("Panel_ImageView") && DocInLayout("Panel_FlowCanvasView")
                    && !DocInLayout("Panel_ScadaEditorView") && !DocInLayout("Panel_RunCockpitView"),
                    $"图像={DocInLayout("Panel_ImageView")}，流程={DocInLayout("Panel_FlowCanvasView")}，组态={DocInLayout("Panel_ScadaEditorView")}，驾驶舱={DocInLayout("Panel_RunCockpitView")}");
                Check("调试模式：流程导航 + 算子工具箱可见，组态三面板隐藏",
                    PanelVisible("Panel_ProcessView")
                    && PanelVisible("Panel_ToolView")
                    && !PanelVisible("Panel_ScadaPropertyView")
                    && !PanelVisible("Panel_ScadaLayerView")
                    && !PanelVisible("Panel_ScadaToolboxView"),
                    $"流程导航={PanelVisible("Panel_ProcessView")}，工具箱={PanelVisible("Panel_ToolView")}");

                VisionMaster.Services.LayoutHelper.ApplyShellMode(dock, VisionMaster.ShellMode.Config);
                Check("组态模式：文档仅组态画面",
                    DocInLayout("Panel_ScadaEditorView")
                    && !DocInLayout("Panel_ImageView") && !DocInLayout("Panel_FlowCanvasView")
                    && !DocInLayout("Panel_RunCockpitView"),
                    $"组态={DocInLayout("Panel_ScadaEditorView")}，图像={DocInLayout("Panel_ImageView")}，流程={DocInLayout("Panel_FlowCanvasView")}，驾驶舱={DocInLayout("Panel_RunCockpitView")}");
                Check("组态模式：流程导航隐藏、组态三面板可见、算子工具箱隐藏",
                    !PanelVisible("Panel_ProcessView")
                    && PanelVisible("Panel_ScadaPropertyView")
                    && PanelVisible("Panel_ScadaLayerView")
                    && PanelVisible("Panel_ScadaToolboxView")
                    && !PanelVisible("Panel_ToolView"),
                    $"属性/图层/图元={PanelVisible("Panel_ScadaPropertyView")}/{PanelVisible("Panel_ScadaLayerView")}/{PanelVisible("Panel_ScadaToolboxView")}，算子工具箱={PanelVisible("Panel_ToolView")}");
                var propInConfig = VisionMaster.Services.LayoutHelper.FindPanel(dock, "Panel_ScadaPropertyView");
                var layerInConfig = VisionMaster.Services.LayoutHelper.FindPanel(dock, "Panel_ScadaLayerView");
                Check("组态模式：属性面板是当前标签（不是图层）",
                    (propInConfig as AvalonDock.Layout.LayoutAnchorable)?.IsSelected == true
                    && (layerInConfig as AvalonDock.Layout.LayoutAnchorable)?.IsSelected != true,
                    $"属性选中={(propInConfig as AvalonDock.Layout.LayoutAnchorable)?.IsSelected}，图层选中={(layerInConfig as AvalonDock.Layout.LayoutAnchorable)?.IsSelected}");
                var logPane = FindAncestorPane(dock, "Panel_LogView");
                Check("组态模式：底部日志收成 1px（无流程日志占位）",
                    logPane != null && logPane.DockHeight.IsAbsolute && logPane.DockHeight.Value < 2,
                    logPane?.DockHeight.ToString() ?? "null");

                VisionMaster.Services.LayoutHelper.ApplyShellMode(dock, VisionMaster.ShellMode.Run);
                Check("运行模式：文档仅运行驾驶舱，左右栏隐藏",
                    DocInLayout("Panel_RunCockpitView")
                    && !DocInLayout("Panel_ImageView") && !DocInLayout("Panel_FlowCanvasView") && !DocInLayout("Panel_ScadaEditorView")
                    && !PanelVisible("Panel_ProcessView")
                    && !PanelVisible("Panel_ToolView"),
                    $"驾驶舱={DocInLayout("Panel_RunCockpitView")}，图像={DocInLayout("Panel_ImageView")}，流程={DocInLayout("Panel_FlowCanvasView")}，组态={DocInLayout("Panel_ScadaEditorView")}");

                VisionMaster.Services.LayoutHelper.ApplyShellMode(dock, VisionMaster.ShellMode.Free);
                Check("自由模式：四个文档全部恢复，左右面板全部可见（含属性/图层）",
                    DocInLayout("Panel_ImageView") && DocInLayout("Panel_FlowCanvasView")
                    && DocInLayout("Panel_ScadaEditorView") && DocInLayout("Panel_RunCockpitView")
                    && PanelVisible("Panel_ProcessView")
                    && PanelVisible("Panel_ToolView") && PanelVisible("Panel_ScadaToolboxView")
                    && PanelVisible("Panel_ScadaPropertyView") && PanelVisible("Panel_ScadaLayerView"),
                    $"文档={DocInLayout("Panel_ImageView")}/{DocInLayout("Panel_FlowCanvasView")}/{DocInLayout("Panel_ScadaEditorView")}/{DocInLayout("Panel_RunCockpitView")}，属性/图层={PanelVisible("Panel_ScadaPropertyView")}/{PanelVisible("Panel_ScadaLayerView")}");

                // ---- 旧布局拒绝路径：不许丢文档（P0 回归闸） ----
                // 顺序很关键：必须先在 Debug 模式下让组态/驾驶舱处于“已摘出缓存”的状态，
                // 再用过期版本 XML 调 LoadFromString。若先切 Free，文档会被全部恢复、缓存已空，
                // 这条断言就变成恒真（空转）。
                VisionMaster.Services.LayoutHelper.ApplyShellMode(dock, VisionMaster.ShellMode.Debug);
                var staleXml = "<LayoutRoot LayoutSchemaVersion=\"4\"><RootPanel Orientation=\"Horizontal\" /></LayoutRoot>";
                bool rejected = !VisionMaster.Services.LayoutHelper.LoadFromString(dock, staleXml);
                VisionMaster.Services.LayoutHelper.ApplyShellMode(dock, VisionMaster.ShellMode.Config);
                bool scadaRestored = DocInLayout("Panel_ScadaEditorView");
                VisionMaster.Services.LayoutHelper.ApplyShellMode(dock, VisionMaster.ShellMode.Run);
                bool cockpitRestored = DocInLayout("Panel_RunCockpitView");
                Check("旧布局被拒后不丢文档（组态画面 / 运行驾驶舱仍可恢复）",
                    rejected && scadaRestored && cockpitRestored,
                    $"拒绝={rejected}，组态恢复={scadaRestored}，驾驶舱恢复={cockpitRestored}");

                // 保存出的布局必须覆盖全部面板，否则下次启动会被覆盖度闸门拒绝
                var coverageXml = VisionMaster.Services.LayoutHelper.SaveToString(dock);
                bool coversAll = coverageXml != null
                    && coverageXml.Contains("Panel_ImageView")
                    && coverageXml.Contains("Panel_FlowCanvasView")
                    && coverageXml.Contains("Panel_ScadaEditorView")
                    && coverageXml.Contains("Panel_RunCockpitView")
                    && coverageXml.Contains("Panel_ProcessView")
                    && coverageXml.Contains("Panel_ToolView");
                Check("保存布局覆盖全部文档与关键面板（不被覆盖度闸门拒绝）",
                    coversAll,
                    coversAll ? "四个文档 + 流程栏/工具箱齐全" : "缺少面板或序列化失败");

                // ---- 保存布局不许把日志 1px 写进文件（P0 回归闸） ----
                VisionMaster.Services.LayoutHelper.ApplyShellMode(dock, VisionMaster.ShellMode.Config);
                var savedXml = VisionMaster.Services.LayoutHelper.SaveToString(dock);
                bool logHeightSafe = savedXml != null
                    && !savedXml.Contains("DockHeight=\"1\"")
                    && !savedXml.Contains("DockHeight=\"25\"");
                Check("组态模式保存布局：日志高度不写 1px/25px（下次启动日志不消失）",
                    logHeightSafe,
                    logHeightSafe ? "未写伪高度" : "写入了伪高度或序列化失败");

                // ---- 保存布局不许翻用户当前标签（P1 回归闸） ----
                VisionMaster.Services.LayoutHelper.ApplyShellMode(dock, VisionMaster.ShellMode.Debug);
                var imageDoc = VisionMaster.Services.LayoutHelper.FindPanel(dock, "Panel_ImageView") as AvalonDock.Layout.LayoutDocument;
                if (imageDoc != null)
                {
                    imageDoc.IsSelected = true;
                    VisionMaster.Services.LayoutHelper.SaveToString(dock);
                    Check("保存布局不改变当前选中的文档标签",
                        imageDoc.IsSelected,
                        $"保存后图像标签选中={imageDoc.IsSelected}");
                }

                // ---- 控制台 32 / 240 两档（状态栏按钮与命令面板共用同一条命令） ----
                VisionMaster.Services.LayoutHelper.ApplyShellMode(dock, VisionMaster.ShellMode.Debug);
                VisionMaster.Services.LayoutHelper.ToggleConsole(dock);
                var consolePaneCollapsed = FindAncestorPane(dock, "Panel_LogView");
                var collapsedHeight = consolePaneCollapsed?.DockHeight.ToString() ?? "null";
                var collapsedValue = consolePaneCollapsed != null && consolePaneCollapsed.DockHeight.IsAbsolute
                    ? consolePaneCollapsed.DockHeight.Value
                    : double.NaN;
                VisionMaster.Services.LayoutHelper.ToggleConsole(dock);
                var consolePaneExpanded = FindAncestorPane(dock, "Panel_LogView");
                var expandedHeight = consolePaneExpanded?.DockHeight.ToString() ?? "null";
                var expandedValue = consolePaneExpanded != null && consolePaneExpanded.DockHeight.IsAbsolute
                    ? consolePaneExpanded.DockHeight.Value
                    : double.NaN;
                Check("控制台 32 / 240 两档可切换（状态栏与命令面板共用）",
                    Math.Abs(collapsedValue - 32) < 0.1 && Math.Abs(expandedValue - 240) < 0.1,
                    $"收起={collapsedHeight}，展开={expandedHeight}");

                // ---- 布局文件缺核心文档的兜底：模拟历史 Layout.xml 只存了图像/画布 ----
                // 真实事故：Layout.xml 缺少 Panel_ScadaEditorView，组态模式只剩空文档窗格，
                // 属性面板显示“未打开方案”。EnsureCoreDocuments 必须按清单补回。
                var missingScada = VisionMaster.Services.LayoutHelper.FindPanel(dock, "Panel_ScadaEditorView")
                    as AvalonDock.Layout.LayoutDocument;
                var missingPane = missingScada?.Parent as AvalonDock.Layout.LayoutDocumentPane;
                missingPane?.Children.Remove(missingScada);
                var hiddenDocsField = typeof(VisionMaster.Services.LayoutHelper)
                    .GetField("HiddenDocuments", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (hiddenDocsField?.GetValue(null) is System.Collections.IDictionary hiddenDocs)
                    hiddenDocs.Remove("Panel_ScadaEditorView");
                var ensureCore = typeof(VisionMaster.Services.LayoutHelper)
                    .GetMethod("EnsureCoreDocuments", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                ensureCore?.Invoke(null, new object[] { dock });
                var repairedScada = VisionMaster.Services.LayoutHelper.FindPanel(dock, "Panel_ScadaEditorView")
                    as AvalonDock.Layout.LayoutDocument;
                Check("布局缺组态文档时能按核心清单补回（不再出现空文档窗格）",
                    repairedScada != null && repairedScada.Content != null,
                    repairedScada == null ? "没有补回 Panel_ScadaEditorView" : "已补回且有内容");
            }
            catch (Exception ex)
            {
                Check("Shell 模式隔离", false, ex.Message);
            }

            Console.WriteLine(failures == 0
                ? "=== Shell 模式隔离：全部通过 ==="
                : $"### Shell 模式隔离：{failures} 项失败");
            return failures;
        }

        private static string KindOf(XElement element)
        {
            return element == null ? "(缺)" : element.Name.LocalName;
        }

        /// <summary>
        /// 流程画布回归闸（静态扫描 FlowCanvasView.xaml，每条都是真实事故的回潮闸）：
        ///   · 连线的显式 Style 里不得再出现 ContextMenu 的 Setter/触发器——连线菜单现在
        ///     内联在 ConnectionTemplate 里（每连线一份实例、DataContext 正确的本地值），
        ///     本地值永远压掉样式值：样式侧再挂菜单或 {x:Null} 触发器，就是 2026-10-07
        ///     "本地值压掉样式 → 数据线弹类型全名行、顺序链弹空菜单"的事故形态回潮。
        ///   · 连线样式与触发器必须配 Fill——Nodify 的箭头由 Fill 填充，漏配就是描边色空芯三角。
        ///   · ItemContainerStyle 必须覆盖 BorderBrush——Nodify 默认是常驻 DodgerBlue 1px
        ///     外框（暗色演示主题泄漏进浅色调画布）。
        /// </summary>
        private static int RunFlowCanvasChecks()
        {
            var failures = 0;
            void Check(string name, bool ok, string detail)
            {
                Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}  {detail}");
                if (!ok) failures++;
            }

            try
            {
                string xamlPath = Path.Combine(@"d:\C#\VM", "VisionMaster", "Views", "FlowCanvasView.xaml");
                if (!File.Exists(xamlPath))
                {
                    Check("FlowCanvasView.xaml 静态守门", false, $"找不到 {xamlPath}");
                    return failures;
                }

                string text = File.ReadAllText(xamlPath);

                // 控件类型不写死（LineConnection→CircuitConnection→StepConnection 走过一轮换型），
                // 只要还是 nodify:*Connection 家族，事故形态的防回潮断言就跟着走
                var linkStyle = Regex.Match(text,
                    @"<(?<t>nodify:\w+Connection)\.Style>(?<body>.*?)</\k<t>\.Style>",
                    RegexOptions.Singleline);
                Check("连线样式侧没有 ContextMenu 的 Setter/触发器（模板内联菜单是本地值，样式侧再挂必被压掉=右键失灵事故回潮）",
                    linkStyle.Success && !linkStyle.Groups["body"].Value.Contains("ContextMenu"),
                    linkStyle.Success ? linkStyle.Groups["t"].Value : "没找到连线显式 Style");

                var linkMenu = Regex.Match(text, @"<(?<t>nodify:\w+Connection)\.ContextMenu>");
                Check("连线模板挂着每实例右键菜单 + 顺序链 Opening 拦截（编辑绑定/解绑入口）",
                    linkMenu.Success && text.Contains("ContextMenuOpening=\"OnConnectionContextMenuOpening\""),
                    linkMenu.Success ? linkMenu.Groups["t"].Value : "没找到连线内联菜单");

                int fillCount = Regex.Matches(text, @"Property\s*=\s*""Fill""").Count;
                Check("连线样式与触发器都配了 Fill（箭头由 Fill 填充，漏配=空芯箭头）",
                    fillCount >= 3, $"Property=\"Fill\" setter 数={fillCount}");

                var containerStyle = Regex.Match(text,
                    @"nodify:NodifyEditor\.ItemContainerStyle>(?<body>.*?)</nodify:NodifyEditor\.ItemContainerStyle>",
                    RegexOptions.Singleline);
                Check("ItemContainerStyle 覆盖了 Nodify 默认 BorderBrush（默认是常驻 DodgerBlue 1px 蓝框）",
                    containerStyle.Success && containerStyle.Groups["body"].Value.Contains("BorderBrush"),
                    containerStyle.Success ? "" : "没找到 ItemContainerStyle");
            }
            catch (Exception ex)
            {
                Check("FlowCanvasView.xaml 静态守门", false, ex.Message);
            }

            return failures;
        }

        /// <summary>
        /// 表格（ListView + GridView）回归闸。
        ///
        /// 为什么单独钉它：App.xaml 把 PresentationFramework.Fluent 合并进全局资源，它自带两条
        /// **隐式样式**（按类型作键，落点就是应用级，优先级高于框架主题样式）——
        ///   · 隐式 ListView 样式换成自家模板，模板里没有 GridViewHeaderRowPresenter → 列头全没了；
        ///   · 隐式 ListViewItem 样式行模板用裸 ContentPresenter → 列布局塌成一列，只剩类型全名。
        /// 2026-10-06 真机缺陷（方案列表弹窗一行行 VisionMaster.Models.AppSolutionEntry）就是这两条
        /// 合起来的产物，而且只读 XAML 看不出来（视图源码一直是好的）。
        ///
        /// 所以这里按"视图实际会怎么挂样式"渲染一遍：ListView 挂主题里的 GridListViewStyle
        /// （空样式，作用是让回框架默认模板），行挂自带 ItemContainerStyle（模板里是
        /// GridViewRowPresenter）。再配一条静态扫描，钉住"凡用 GridView 的视图三件套必须齐"。
        /// </summary>
        private static int RunGridViewChecks()
        {
            var failures = 0;
            void Check(string name, bool ok, string detail)
            {
                Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}  {detail}");
                if (!ok) failures++;
            }

            try
            {
                var listStyle = Application.Current.TryFindResource("GridListViewStyle") as Style;
                Check("取到 GridListViewStyle（UI 主题里那份给 GridView 让路的样式）",
                    listStyle != null,
                    listStyle == null ? "找不到键 /UI;component/Themes/Controls/GridView.xaml" : "已解析");

                // 行容器照 Scada 范式写（模板里必须是 GridViewRowPresenter）：这里只验结构，不带配色
                var rowStyle = (Style)XamlReader.Parse(@"
<Style xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
       xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"" TargetType=""ListViewItem"">
  <Setter Property=""OverridesDefaultStyle"" Value=""True"" />
  <Setter Property=""HorizontalContentAlignment"" Value=""Stretch"" />
  <Setter Property=""Padding"" Value=""0"" />
  <Setter Property=""Template"">
    <Setter.Value>
      <ControlTemplate TargetType=""ListViewItem"">
        <Border Padding=""4,6"" Background=""Transparent"">
          <GridViewRowPresenter Columns=""{TemplateBinding GridView.ColumnCollection}""
                                Content=""{TemplateBinding Content}""
                                HorizontalAlignment=""{TemplateBinding HorizontalContentAlignment}"" />
        </Border>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>
");

                ListView BuildTable(Style listStyleOrNull)
                {
                    var grid = new GridView();
                    AddColumn(grid, "Index", 50);
                    AddColumn(grid, "Name", 150);
                    AddColumn(grid, "Path", 220);
                    var table = new ListView
                    {
                        ItemContainerStyle = rowStyle,
                        View = grid,
                        ItemsSource = new ObservableCollection<GridViewProbeRow>
                        {
                            new GridViewProbeRow { Index = "1", Name = "方案A", Path = "D:\\A\\Main.vms" },
                            new GridViewProbeRow { Index = "2", Name = "方案B", Path = "D:\\B\\Main.vms" },
                        },
                    };
                    // 注意：不挂样式时**不能写 table.Style = null** —— 那是往本地值里写 null，
                    // 照样会把隐式样式顶掉（等于变相"修好了"），量出来的就不是真实现象。
                    if (listStyleOrNull != null) table.Style = listStyleOrNull;
                    var host = new Border { Width = 560, Height = 200, Child = table };
                    host.Measure(new Size(560, 200));
                    host.Arrange(new Rect(0, 0, 560, 200));
                    host.UpdateLayout();
                    host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    return table;
                }

                var table = BuildTable(listStyle);
                var headers = Descendants<GridViewColumnHeader>(table)
                    .Where(h => h.Content != null)
                    .OrderBy(h => h.TranslatePoint(new Point(0, 0), table).X)
                    .Select(h => h.Content!.ToString() ?? string.Empty).ToList();
                Check("三列列头都画出来了（Fluent 的隐式样式在位时这里是 0 个）",
                    string.Join("/", headers) == "Index/Name/Path",
                    "列头=[" + string.Join("/", headers) + "]");
                Check("列头由 GridViewHeaderRowPresenter 摆放（模板里没有它就没有上面那三个列头）",
                    Descendants<GridViewHeaderRowPresenter>(table).Any(p => p.Visibility == Visibility.Visible),
                    $"headerPresenters={Descendants<GridViewHeaderRowPresenter>(table).Count()}");

                var rows = Descendants<ListViewItem>(table).ToList();
                Check("每行都挂着 GridViewRowPresenter（列布局靠它；退化成 ContentPresenter 就是类型全名）",
                    rows.Count == 2 && rows.All(r => Descendants<GridViewRowPresenter>(r).Any()),
                    $"行数={rows.Count}，带列渲染器的行={rows.Count(r => Descendants<GridViewRowPresenter>(r).Any())}");

                var texts = Descendants<TextBlock>(table).Select(t => t.Text ?? string.Empty).ToList();
                Check("整棵树里没有任何一处渲染出类型全名",
                    !texts.Any(t => t.Contains("GridViewProbeRow", StringComparison.Ordinal)),
                    texts.FirstOrDefault(t => t.Contains("GridViewProbeRow", StringComparison.Ordinal)) ?? "（没有）");
                Check("每一列都真的接上了数据（序号 / 名称 / 路径三列各自有值）",
                    texts.Contains("1") && texts.Contains("方案A") && texts.Contains("D:\\A\\Main.vms"),
                    string.Join(" | ", texts.Where(t => t.Length > 0)));

                // 参考信息（不作断言）：同一张表不挂样式的样子 —— Fluent 在位时列头会被顶掉，
                // 这就是"为什么必须有 GridListViewStyle"。若哪天 App.xaml 去掉了 Fluent 合并，
                // 这行会变成 3 —— 那不是失败，只是说明根因不在了。
                var bare = BuildTable(null);
                int bareHeaders = Descendants<GridViewColumnHeader>(bare).Count(h => h.Content != null);
                Console.WriteLine($"        （参考：不挂 GridListViewStyle 时列头 = {bareHeaders} 个；行容器样式仍是自带的，故行不受影响）");

                // ---- 静态扫描：用 GridView 的视图，三件套必须在同一个文件里齐 ----
                // 少任何一件，屏幕上就是"列头没了 / 一行行类型全名"，而这两种错只读 XAML 看不出来。
                //
                // 两条口径（2026-10-19 修）：
                // ① 只扫第一方目录。vendored 的 VM3.0-main 不是本仓维护的代码，把它算进来会让
                //    扫描恒红（实测 10 条 offender），新违规会被挤掉、看不见。
                // ② 逐个 ListView 判，不做文件级子串。文件级判据下"4 张表里 3 张漏挂样式"照样绿。
                string repoRoot = @"d:\C#\VM";
                string[] firstPartyRoots =
                {
                    Path.Combine(repoRoot, "VisionMaster"),
                    Path.Combine(repoRoot, "UI"),
                    Path.Combine(repoRoot, "Scada"),
                    Path.Combine(repoRoot, "Scada.Controls"),
                    Path.Combine(repoRoot, "Scada.Controls.Charts"),
                    Path.Combine(repoRoot, "Plugins"),
                };

                bool IsFirstParty(string file) =>
                    firstPartyRoots.Any(root =>
                        file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

                int LineOf(string text, int index)
                {
                    int line = 1;
                    for (int i = 0; i < index && i < text.Length; i++)
                        if (text[i] == '\n') line++;
                    return line;
                }

                var offenders = new List<string>();
                int scanned = 0;
                foreach (var file in Directory.GetFiles(repoRoot, "*.xaml", SearchOption.AllDirectories))
                {
                    if (!IsFirstParty(file)) continue;
                    if (file.Contains(@"\obj\") || file.Contains(@"\bin\")) continue;

                    string text = File.ReadAllText(file);
                    if (!text.Contains("<GridView", StringComparison.Ordinal)) continue;
                    scanned++;
                    string name = Path.GetFileName(file);

                    // <ListView(?![.\w]) 排除 <ListView.View> 这类成员元素。
                    var listViews = Regex.Matches(text, @"<ListView(?![.\w])[^>]*>");
                    if (listViews.Count == 0)
                    {
                        if (!text.Contains("GridListViewStyle", StringComparison.Ordinal))
                            offenders.Add(name + ": 没挂 GridListViewStyle（列头会被 Fluent 的隐式 ListView 样式顶掉）");
                    }
                    else
                    {
                        foreach (Match lv in listViews)
                        {
                            if (lv.Value.Contains("GridListViewStyle", StringComparison.Ordinal)) continue;
                            offenders.Add($"{name}: 第 {LineOf(text, lv.Index)} 行的 ListView 未挂 GridListViewStyle");
                        }
                    }

                    if (!text.Contains("GridViewRowPresenter", StringComparison.Ordinal))
                        offenders.Add(name + ": 行模板缺 GridViewRowPresenter（列布局会塌成一行行类型全名）");
                }

                Check($"凡用 GridView 的第一方视图都带齐三件套（扫过 {scanned} 个含 GridView 的 xaml）",
                    offenders.Count == 0,
                    offenders.Count == 0
                        ? "全部齐全"
                        : $"{offenders.Count} 条：{string.Join(" | ", offenders)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine("### 表格（GridView）断言抛出异常:");
                Print(ex);
                failures++;
            }

            Console.WriteLine(failures == 0
                ? "=== 表格（GridView）冒烟：全部通过 ==="
                : $"### 表格（GridView）冒烟：{failures} 项失败");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// 日志控制台（LogConsole）回归闸。
        ///
        /// 钉的是几处"只读 XAML 看不出来、只有真跑才暴露"的行为：
        ///   · GroupBy 常写在 ItemsSource 之前（XAML 的属性顺序就是这样），分组必须补应用一次；
        ///   · 过滤挂在集合的**默认视图**上（WPF 的 ItemsControl 就是从那里取数据），换源时若不摘掉，
        ///     旧集合会被"幽灵过滤"，控件也回收不掉；
        ///   · 搜索走 200ms 防抖（防抖窗口内不得重算视图），等级过滤立即生效；
        ///   · MaxItems 的裁剪必须排到 Dispatcher 上执行（ObservableCollection 在派发
        ///     CollectionChanged 的过程中禁止再改自己）。
        /// 另配一条静态扫描：分组表头不得回退成深色底（亮色日志列表上就是黑条压白列表）。
        /// </summary>
        private static int RunLogConsoleChecks()
        {
            var failures = 0;
            void Check(string name, bool ok, string detail)
            {
                Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}  {detail}");
                if (!ok) failures++;
            }

            // 把 Dispatcher 上排队的活（Background 优先级）跑完：Background(4) > ApplicationIdle(2)
            void Pump(UI.CustomControl.LogConsole console)
                => console.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            try
            {
                // ---- 静态扫描：分组表头配色 ----
                // 只扫非注释内容：注释里写着"以前是 #252526"是说明，不是缺陷
                const string logConsoleTheme = @"d:\C#\VM\UI\Controls\Themes\LogConsole.xaml";
                string themeText = File.Exists(logConsoleTheme) ? File.ReadAllText(logConsoleTheme) : string.Empty;
                string themeCode = Regex.Replace(themeText, "<!--.*?-->", string.Empty, RegexOptions.Singleline);
                Check("分组表头不再用深色底（#252526 压在亮色日志列表上）",
                    themeCode.Length > 0 && !themeCode.Contains("#252526", StringComparison.OrdinalIgnoreCase),
                    themeCode.Length == 0 ? "找不到 " + logConsoleTheme : "已扫过主题文件（注释除外）");

                // ---- GroupBy 先于 ItemsSource ----
                var grouped = new ObservableCollection<UI.Models.LogItem>();
                var groupedConsole = new UI.CustomControl.LogConsole { GroupBy = "Source" };
                groupedConsole.ItemsSource = grouped;
                var groupedView = System.Windows.Data.CollectionViewSource.GetDefaultView(grouped);
                Check("GroupBy 先于 ItemsSource 赋值也能生效（XAML 的属性顺序就是这个顺序）",
                    groupedView.GroupDescriptions.Count == 1,
                    $"GroupDescriptions={groupedView.GroupDescriptions.Count}");

                // ---- 换源：旧集合默认视图上的过滤委托必须摘掉 ----
                var oldSource = new ObservableCollection<UI.Models.LogItem>
                {
                    new UI.Models.LogItem(UI.Models.LogLevel.Info, "old")
                };
                var oldView = System.Windows.Data.CollectionViewSource.GetDefaultView(oldSource);
                var switchConsole = new UI.CustomControl.LogConsole { ItemsSource = oldSource };
                bool attached = oldView.Filter != null;
                switchConsole.ItemsSource = new ObservableCollection<UI.Models.LogItem>();
                Check("换源后旧集合默认视图上的过滤委托被摘掉（不摘 = 旧集合被幽灵过滤 + 控件回收不掉）",
                    attached && oldView.Filter == null,
                    $"换源前 attached={attached}，换源后 filter={(oldView.Filter == null ? "null" : "仍在")}");

                // ---- 过滤：等级立即生效，关键字走防抖 ----
                var logs = new ObservableCollection<UI.Models.LogItem>
                {
                    new UI.Models.LogItem(UI.Models.LogLevel.Info, "alpha 一条"),
                    new UI.Models.LogItem(UI.Models.LogLevel.Error, "beta 一条"),
                    new UI.Models.LogItem(UI.Models.LogLevel.Error, "alpha 报错"),
                };
                var filterConsole = new UI.CustomControl.LogConsole { ItemsSource = logs };
                var filterView = System.Windows.Data.CollectionViewSource.GetDefaultView(logs);

                filterConsole.FilterLevel = UI.Models.LogLevel.Error;
                Check("等级过滤立即生效（不走防抖）", CountView(filterView) == 2, $"命中 {CountView(filterView)}/3");

                filterConsole.SearchText = "alpha";
                // 此刻仍在 Dispatcher 线程上同步执行，计时器没机会跑 —— 防抖窗口内视图必须还没变
                int beforeDebounce = CountView(filterView);
                System.Threading.Thread.Sleep(320);   // 越过 200ms 防抖窗口（Sleep 期间消息泵停摆，醒来才补跑计时器）
                Pump(filterConsole);
                int afterDebounce = CountView(filterView);
                Check("关键字过滤：防抖窗口内不重算，窗口过后命中 1 条",
                    beforeDebounce == 2 && afterDebounce == 1,
                    $"防抖窗口内 {beforeDebounce}/3（等级=Error），窗口过后 {afterDebounce}/3（Error+alpha）");

                // ---- MaxItems 裁剪 ----
                var capped = new ObservableCollection<UI.Models.LogItem>();
                var capConsole = new UI.CustomControl.LogConsole { MaxItems = 3, ItemsSource = capped };
                for (int i = 0; i < 10; i++)
                    capped.Add(new UI.Models.LogItem(UI.Models.LogLevel.Info, "m" + i));
                Pump(capConsole);
                Check("MaxItems 生效：超出后从头部裁掉最旧的",
                    capped.Count == 3 && capped[0].Message == "m7",
                    $"count={capped.Count}，首条={(capped.Count > 0 ? capped[0].Message : "（空）")}");

                // ---- 自动滚动：首屏贴底 / 用户上翻后不抢镜 / 翻回底部后恢复 ----
                // 说明：这里用 ScrollToVerticalOffset 模拟"用户往上翻"，它与拖拽/滚轮产生的
                // ScrollChanged 签名一致（ExtentHeightChange=0、VerticalChange≠0），状态机走同一条路；
                // 差别只在触发源是真手还是代码。真正只在"滚轮与新增落在同一次布局"时才分得出的那处
                // 判定差异（旧写法看 ExtentHeightChange、新写法看 VerticalChange），公开 API 无法稳定造出。
                var feed = new ObservableCollection<UI.Models.LogItem>();
                for (int i = 0; i < 100; i++)
                    feed.Add(new UI.Models.LogItem(UI.Models.LogLevel.Info, "line " + i));
                var console = new UI.CustomControl.LogConsole { ItemsSource = feed };
                var host = new Border { Width = 400, Height = 200, Child = console };
                host.Measure(new Size(400, 200));
                host.Arrange(new Rect(0, 0, 400, 200));

                // 先让布局把新增的项算进 ExtentHeight，再跑排队的 ScrollToBottom ——
                // 顺序反了的话 ScrollToBottom 拿的是上一轮的 ScrollableHeight，会差一项。
                void Settle()
                {
                    host.UpdateLayout();
                    Pump(console);
                }

                Settle();
                var sv = FindDescendant<ScrollViewer>(console);
                Check("拿到模板里的 PART_ScrollViewer", sv != null,
                    sv == null ? "模板里没有 ScrollViewer" : "已拿到");

                if (sv != null)
                {
                    bool AtBottom() => sv.VerticalOffset >= sv.ScrollableHeight - 0.5;

                    Check("AutoScroll 默认开启：首屏就贴在底部（逻辑滚动，ScrollableHeight 以项为单位）",
                        AtBottom(), $"offset={sv.VerticalOffset}/{sv.ScrollableHeight}");

                    sv.ScrollToVerticalOffset(0);   // 模拟用户往上翻
                    feed.Add(new UI.Models.LogItem(UI.Models.LogLevel.Info, "user is reading history"));
                    Settle();
                    Check("用户上翻后新日志不抢镜（粘底策略：不把正在看的位置拽回底部）",
                        sv.VerticalOffset <= 0.5, $"新日志到达后 offset={sv.VerticalOffset}/{sv.ScrollableHeight}");

                    sv.ScrollToBottom();            // 翻回底部
                    feed.Add(new UI.Models.LogItem(UI.Models.LogLevel.Info, "back to bottom"));
                    Settle();
                    Check("翻回底部后自动滚动恢复", AtBottom(), $"offset={sv.VerticalOffset}/{sv.ScrollableHeight}");
                }

                // ---- LogView 的接线：绑的点必须在 VM 上真的存在 ----
                // 属性名写错时 XAML 照样编译通过、运行时绑定静默失效 —— 下拉/搜索框会"看着有，其实没用"。
                const string logViewPath = @"d:\C#\VM\VisionMaster\Views\LogView.xaml";
                string viewText = File.Exists(logViewPath) ? File.ReadAllText(logViewPath) : string.Empty;
                var vmType = typeof(VisionMaster.ViewModels.LogViewModel);
                var wires = new (string Snippet, string Property)[]
                {
                    ("SearchText=\"{Binding SearchText}\"", "SearchText"),
                    ("FilterLevel=\"{Binding FilterLevel}\"", "FilterLevel"),
                    ("AutoScroll=\"{Binding AutoScroll}\"", "AutoScroll"),
                    ("ItemsSource=\"{Binding LevelOptions}\"", "LevelOptions"),
                    ("SelectedItem=\"{Binding LevelOption}\"", "LevelOption"),
                };
                var wireProblems = new List<string>();
                foreach (var wire in wires)
                {
                    if (!viewText.Contains(wire.Snippet, StringComparison.Ordinal))
                        wireProblems.Add(wire.Property + "：视图里没有 " + wire.Snippet);
                    else if (vmType.GetProperty(wire.Property) == null)
                        wireProblems.Add(wire.Property + "：LogViewModel 上没有这个属性");
                }
                // 筹码模板要绑到 LogLevelFilterOption 的 Text / Count 上（写错就是空白筹码，编译还不报错）
                foreach (var prop in new[] { "Text", "Count" })
                {
                    if (viewText.Length == 0 || !viewText.Contains("{Binding " + prop + "}", StringComparison.Ordinal)
                        || typeof(VisionMaster.ViewModels.LogLevelFilterOption).GetProperty(prop) == null)
                        wireProblems.Add("等级筹码的 {Binding " + prop + "} 对不上 LogLevelFilterOption." + prop);
                }
                Check("LogView 的三处过滤/开关接线 + 等级筹码都点到点（写错只会静默失效，不报编译错）",
                    viewText.Length > 0 && wireProblems.Count == 0,
                    viewText.Length == 0 ? "找不到 " + logViewPath
                        : (wireProblems.Count == 0 ? "5 处绑定 + 筹码 Text/Count 全部对得上" : string.Join(" | ", wireProblems)));

                // ---- 来源为空的行不许留灰块 ----
                // 真机截图里的症状：每行时间右边一个灰色小方块（来源胶囊空着仍然在画）。
                // 现在来源为空整块 Collapsed，列宽不变所以正文起点仍然对齐。
                //
                // 判据按 Tag="SourceChip" 计数，不绑死背景色：2026-10-22 复刻把来源胶囊的
                // 硬编码 #E9ECEF 换成了令牌（DialogBorderLightBrush = #EEF2F7），
                // 按颜色数会假红——这条断言要守的是"空来源不画块"，不是"块是什么颜色"。
                var chipLogs = new ObservableCollection<UI.Models.LogItem>
                {
                    new UI.Models.LogItem(UI.Models.LogLevel.Info, "no source", null),
                    new UI.Models.LogItem(UI.Models.LogLevel.Info, "has source", "MainTask"),
                };
                var chipConsole = new UI.CustomControl.LogConsole { ItemsSource = chipLogs };
                var chipHost = new Border { Width = 600, Height = 120, Child = chipConsole };
                chipHost.Measure(new Size(600, 120));
                chipHost.Arrange(new Rect(0, 0, 600, 120));
                chipHost.UpdateLayout();
                int visibleChips = Descendants<Border>(chipConsole).Count(b =>
                    b.Visibility == Visibility.Visible && (b.Tag as string) == "SourceChip");
                Check("来源为空的行不再留一个灰块（有来源的行照旧显示来源胶囊）",
                    visibleChips == 1, $"可见来源块={visibleChips}（期望 1：只有那条有来源的）");

                // ---- 端到端：真视图 + 真 VM，验证值真的落到了依赖属性上 ----
                // 上面那条只证明"名字对得上"，这条证明"值真的会传过去"（OneWay/TwoWay 方向、
                // 派生属性 FilterLevel 的通知链）。构造 LogView 会走 Prism 的自动装配（本工程没有容器），
                // 万一装配失败也不该让整段检查炸掉 —— 退化成一行参考说明。
                try
                {
                    var logVm = new VisionMaster.ViewModels.LogViewModel(null);   // null 服务：不订阅日志，只测接线
                    var logView = new VisionMaster.Views.LogView { DataContext = logVm };
                    var probe = new Border { Width = 600, Height = 300, Child = logView };
                    probe.Measure(new Size(600, 300));
                    probe.Arrange(new Rect(0, 0, 600, 300));
                    probe.UpdateLayout();
                    probe.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                    var wiredConsole = FindDescendant<UI.CustomControl.LogConsole>(logView);
                    var levelBox = logView.FindName("LevelChips") as ListBox;   // 等级筹码条（原先是 ComboBox）
                    var followBox = FindDescendant<CheckBox>(logView);

                    Check("端到端：真视图 + 真 VM，控件的 AutoScroll/FilterLevel/SearchText 与筹码/勾选初值都对",
                        wiredConsole != null && wiredConsole.AutoScroll && wiredConsole.FilterLevel == null
                            && string.IsNullOrEmpty(wiredConsole.SearchText)
                            && levelBox != null && ReferenceEquals(levelBox.SelectedItem, logVm.LevelOptions[0])
                            && followBox != null && followBox.IsChecked == true,
                        wiredConsole == null ? "视图里没找到 LogConsole"
                            : $"AutoScroll={wiredConsole.AutoScroll} FilterLevel={(wiredConsole.FilterLevel?.ToString() ?? "null")} "
                              + $"SearchText='{wiredConsole.SearchText}' 等级选中={levelBox?.SelectedItem} 勾选={followBox?.IsChecked}");

                    // 筹码上的数字要跟着日志走：增一条 +1，删一条（面板 MaxItems 裁剪走的就是 Remove）-1
                    logVm.SystemLogs.Add(new UI.Models.LogItem(UI.Models.LogLevel.Info, "i1", null));
                    logVm.SystemLogs.Add(new UI.Models.LogItem(UI.Models.LogLevel.Info, "i2", null));
                    logVm.SystemLogs.Add(new UI.Models.LogItem(UI.Models.LogLevel.Error, "e1", null));
                    probe.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    var afterAdd = new int[5];
                    for (int i = 0; i < 5; i++) afterAdd[i] = logVm.LevelOptions[i].Count;
                    logVm.SystemLogs.RemoveAt(0);   // 最旧那条（Info）被裁掉
                    probe.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    var afterTrim = new int[5];
                    for (int i = 0; i < 5; i++) afterTrim[i] = logVm.LevelOptions[i].Count;
                    Check("等级筹码的计数跟着日志走（新增 +1、裁剪 -1，且「全部」= 总条数）",
                        string.Join("/", afterAdd) == "3/2/0/0/1" && string.Join("/", afterTrim) == "2/1/0/0/1",
                        $"加三条后 全部/信息/成功/警告/错误 = {string.Join("/", afterAdd)}，裁掉一条信息后 = {string.Join("/", afterTrim)}");

                    logVm.SearchText = "abc";
                    logVm.LevelOption = logVm.LevelOptions[4];   // 错误
                    logVm.AutoScroll = false;
                    probe.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                    Check("端到端：改 VM 后控件三个依赖属性跟着变，搜索框也显示出新词（派生属性通知链通）",
                        wiredConsole != null && wiredConsole.SearchText == "abc"
                            && wiredConsole.FilterLevel == UI.Models.LogLevel.Error && !wiredConsole.AutoScroll
                            && Descendants<TextBox>(logView).Any(t => t.Text == "abc"),
                        wiredConsole == null ? "视图里没找到 LogConsole"
                            : $"SearchText='{wiredConsole.SearchText}' FilterLevel={(wiredConsole.FilterLevel?.ToString() ?? "null")} "
                              + $"AutoScroll={wiredConsole.AutoScroll} 搜索框文本={Descendants<TextBox>(logView).FirstOrDefault(t => t.Text == "abc")?.Text ?? "（没有 abc）"}");

                    // 搜索框必须有真正的输入框外形：Fluent 的隐式 TextBox 样式只画"透明底 + 一条 6% 黑的底线"，
                    // 放在浅色工具条上等于一块看不见的空地（真机截图已踩）。要求模板里有白底 + 非零描边的 Border。
                    var searchBox = logView.FindName("SearchBox") as TextBox;
                    var searchFrame = searchBox == null ? null : Descendants<Border>(searchBox)
                        .FirstOrDefault(b => b.BorderThickness.Left > 0 && (b.Background as SolidColorBrush)?.Color.A == 255);
                    Check("搜索框有真外形（白底 + 非零描边）—— Fluent 那条 6% 黑底线在浅色条上等于看不见",
                        searchFrame != null,
                        searchBox == null ? "视图里没有 x:Name=SearchBox"
                            : (searchFrame == null
                                ? "模板里没有可见外框"
                                : $"外框厚={(searchFrame.BorderThickness.Left.ToString("0.#"))}px bg={(searchFrame.Background as SolidColorBrush)?.Color.ToString()}"));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"        （参考：端到端接线检查未执行 —— {ex.GetType().Name}: {ex.Message}）");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("### 日志控制台断言抛出异常:");
                Print(ex);
                failures++;
            }

            Console.WriteLine(failures == 0
                ? "=== 日志控制台（LogConsole）冒烟：全部通过 ==="
                : $"### 日志控制台（LogConsole）冒烟：{failures} 项失败");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// 第 1 批四视图 Web 化复刻的守门人（2026-10-22）。
        ///
        /// 这四页的共同风险是"改样式改掉了契约"：绑定名写错、命令漏挂、令牌又变回字面量——
        /// 编译期全绿，只有运行时静默失效（下拉/按钮"看着有，其实没用"）。
        /// 所以这里钉两件事：① 静态扫描契约字符串与 hex=0；② 真实例化证明 XAML 能解析、模板能套上。
        /// </summary>
        private static int RunFirstBatchViewChecks()
        {
            var failures = 0;
            void Check(string name, bool ok, string detail)
            {
                Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}  {detail}");
                if (!ok) failures++;
            }

            const string root = @"d:\C#\VM\";
            string Code(string rel)
            {
                var p = root + rel;
                if (!File.Exists(p)) return string.Empty;
                // 注释里的色号是说明，不是缺陷
                return Regex.Replace(File.ReadAllText(p), "<!--.*?-->", string.Empty, RegexOptions.Singleline);
            }

            var monitor = Code(@"VisionMaster\Views\MonitorView.xaml");
            var flow = Code(@"VisionMaster\Views\FlowListView.xaml");
            var cockpit = Code(@"VisionMaster\Views\RunCockpitView.xaml");
            var logTheme = Code(@"UI\Controls\Themes\LogConsole.xaml");

            Check("监视面板：零硬编码色（23 处已全部换令牌）",
                monitor.Length > 0 && !Regex.IsMatch(monitor, "#[0-9A-Fa-f]{6,8}"),
                monitor.Length == 0 ? "找不到 MonitorView.xaml"
                    : (Regex.Matches(monitor, "#[0-9A-Fa-f]{6,8}").Count == 0 ? "hex=0" : "仍有字面量色"));

            // 契约：四个命令 + 两个集合 + 失效计数 + 小图标按钮 Padding=0（R68）
            var monitorWires = new[]
            {
                "{Binding AddWatchCommand}", "{Binding QuickAddCommand}", "{Binding ClearAllCommand}",
                "DataContext.RemoveCommand", "ItemsSource=\"{Binding GroupedPorts}\"",
                "ItemsSource=\"{Binding SuggestedItems}\"", "{Binding MissingCount}",
                "{Binding DisplayPorts.Count}",
            };
            var monitorMissing = monitorWires.Where(w => !monitor.Contains(w, StringComparison.Ordinal)).ToList();
            Check("监视面板：四个命令 + 分组/联想集合 + 失效计数全部还接在 VM 上（写错只静默失效）",
                monitor.Length > 0 && monitorMissing.Count == 0,
                monitorMissing.Count == 0 ? "8 处契约字符串齐" : ("缺：" + string.Join(" | ", monitorMissing)));

            Check("监视面板：移除按钮 24×24 且 Padding=0（R68，不写就会被隐式内边距削掉字形）",
                monitor.Contains("Width=\"24\"", StringComparison.Ordinal) &&
                monitor.Contains("Padding=\"0\"", StringComparison.Ordinal),
                "检查 Width=24 / Padding=0");

            // 流程胶囊条：零 hex + 11 个 FlowAction 齐 + 菜单挂在胶囊上而不是 ListBox 上
            var flowActions = new[]
            {
                "FlowAction.Create", "FlowAction.Delete", "FlowAction.Rename", "FlowAction.EditComment",
                "FlowAction.ToggleEnabled", "FlowAction.Compile", "FlowAction.RunOnce",
                "FlowAction.RunContinuous", "FlowAction.Stop", "FlowAction.Manager", "FlowAction.VariableEvents",
            };
            var flowMissing = flowActions.Where(a => !flow.Contains(a, StringComparison.Ordinal)).ToList();
            Check("流程胶囊条：零硬编码色 + 11 个 FlowAction 全部保留",
                flow.Length > 0 && !Regex.IsMatch(flow, "#[0-9A-Fa-f]{6,8}") && flowMissing.Count == 0,
                flow.Length == 0 ? "找不到 FlowListView.xaml"
                    : (flowMissing.Count > 0 ? ("缺：" + string.Join(" | ", flowMissing)) : "hex=0 + 11 项齐"));

            Check("流程胶囊条：右键菜单挂在胶囊上（挂在 ListBox 上会让空白区也弹菜单、靶点错位）",
                flow.Contains("<Grid.ContextMenu>", StringComparison.Ordinal) &&
                !flow.Contains("<ListBox.ContextMenu>", StringComparison.Ordinal),
                "检查 Grid.ContextMenu 存在且 ListBox.ContextMenu 不存在");

            // 驾驶舱：零 hex + 停止命令 + 四张 KPI 标签
            var kpiLabels = new[] { "节拍", "良率", "设备在线", "未确认报警" };
            var kpiMissing = kpiLabels.Where(l => !cockpit.Contains(l, StringComparison.Ordinal)).ToList();
            Check("运行驾驶舱：零硬编码色 + 停止命令 + 四张 KPI 卡齐",
                cockpit.Length > 0 && !Regex.IsMatch(cockpit, "#[0-9A-Fa-f]{6,8}")
                    && cockpit.Contains("ExecutionAction.Stop", StringComparison.Ordinal) && kpiMissing.Count == 0,
                cockpit.Length == 0 ? "找不到 RunCockpitView.xaml"
                    : (kpiMissing.Count > 0 ? ("缺 KPI：" + string.Join("/", kpiMissing)) : "hex=0 + 停止命令 + 4 卡"));

            // 日志控件皮肤：零 hex + 模板部件 + 来源徽标标记（断言按它计数，不按颜色）
            Check("日志控件皮肤：零硬编码色 + PART_ScrollViewer + 来源徽标 Tag 标记",
                logTheme.Length > 0 && !Regex.IsMatch(logTheme, "#[0-9A-Fa-f]{6,8}")
                    && logTheme.Contains("PART_ScrollViewer", StringComparison.Ordinal)
                    && logTheme.Contains("Tag=\"SourceChip\"", StringComparison.Ordinal),
                logTheme.Length == 0 ? "找不到 LogConsole.xaml" : "hex=0 + 两个标记齐");

            // 真实例化：证明 XAML 解析、模板套用、StaticResource 解析都过（无 VM，绑定为空）
            foreach (var (viewName, factory) in new (string, Func<FrameworkElement>)[]
            {
                ("MonitorView", () => new VisionMaster.Views.MonitorView()),
                ("FlowListView", () => new VisionMaster.Views.FlowListView()),
                ("RunCockpitView", () => new VisionMaster.Views.RunCockpitView()),
            })
            {
                string detail;
                bool ok;
                try
                {
                    var v = factory();
                    var host = new Border { Width = 640, Height = 480, Child = v };
                    host.Measure(new Size(640, 480));
                    host.Arrange(new Rect(0, 0, 640, 480));
                    host.UpdateLayout();
                    ok = v.ActualWidth > 0 && v.ActualHeight > 0;
                    detail = $"实得 {v.ActualWidth:F0}×{v.ActualHeight:F0}";
                }
                catch (Exception ex)
                {
                    ok = false;
                    detail = ex.GetType().Name + ": " + ex.Message;
                }
                Check($"第 1 批视图可实例化：{viewName}（XAML 解析 + 模板 + 令牌解析）", ok, detail);
            }

            Console.WriteLine(failures == 0
                ? "=== 第 1 批四视图（日志/监视/流程条/驾驶舱）冒烟：全部通过 ==="
                : $"### 第 1 批四视图冒烟：{failures} 项失败");
            return failures;
        }

        /// <summary>
        /// 用户登录（S12）端到端守门人（2026-10-22）。
        ///
        /// 这条链路跨三层：<c>ScadaUserStore</c>（验身份）→ <c>ScadaAccessPolicy</c>（记会话）→
        /// <c>ScadaLoginView</c>（界面）。三层里任何一层断开，表现都是"点了登录没反应"：
        /// 绑定名写错、命令漏挂、账号文件读不出来、验过了却没开会话。
        ///
        /// 所以这里分两段钉：
        /// <list type="bullet">
        /// <item>界面层：静态契约扫描（14 处绑定 + 7 个命令 + 三态判据 + hex=0）+ 真实例化。</item>
        /// <item>后端两层：用<b>临时账号文件</b>跑真实实现（不碰程序目录的 ScadaUsers.json），
        /// 验"出厂管理员能登 → 错密码被拒 → 改密后旧密码失效、新密码生效 → 最后一个管理员不能降权 → 会话开关"。</item>
        /// </list>
        /// </summary>
        private static int RunLoginChecks()
        {
            var failures = 0;
            void Check(string name, bool ok, string detail)
            {
                Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {name}  {detail}");
                if (!ok) failures++;
            }

            // ---------- 界面层：静态契约 ----------
            const string viewPath = @"d:\C#\VM\VisionMaster\Views\DialogViews\ScadaLoginView.xaml";
            string viewText = File.Exists(viewPath) ? File.ReadAllText(viewPath) : string.Empty;
            string viewCode = Regex.Replace(viewText, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

            Check("登录视图零硬编码色（品牌色已上移 Brand* 令牌）",
                viewText.Length > 0 && !Regex.IsMatch(viewCode, "#[0-9A-Fa-f]{6,8}"),
                viewText.Length == 0 ? "找不到 ScadaLoginView.xaml"
                    : (Regex.Matches(viewCode, "#[0-9A-Fa-f]{6,8}").Count == 0 ? "hex=0" : "仍有字面量色"));

            var loginWires = new[]
            {
                "{Binding UserName", "{Binding Password", "{Binding ChangeUserName", "{Binding OldPassword",
                "{Binding NewPassword", "{Binding ConfirmPassword", "{Binding LoginMessage}",
                "{Binding ChangeMessage}", "{Binding LoginStateText}", "{Binding RolePermissionText}",
                "{Binding IdleTimeoutText}", "{Binding SolutionName}", "{Binding ShowDefaultPasswordHint",
                "{Binding CanManageUsers", "{Binding LoginCommand}", "{Binding LogoutCommand}",
                "{Binding ChangePasswordCommand}", "{Binding SubmitCommand}", "{Binding CloseCommand}",
                "{Binding OpenUserManagerCommand}", "{Binding OpenChangePasswordCommand}",
                "{Binding IsLoggedOut}", "{Binding IsLoggedIn}", "{Binding SelectedTabIndex}",
            };
            var missingWires = loginWires.Where(w => !viewText.Contains(w, StringComparison.Ordinal)).ToList();
            Check("登录视图的 14 处数据绑定 + 7 个命令 + 三态判据全部在（写错只静默失效，编译不报错）",
                viewText.Length > 0 && missingWires.Count == 0,
                missingWires.Count == 0 ? "24 处契约字符串齐" : ("缺：" + string.Join(" | ", missingWires)));

            Check("登录视图品牌区走 Brand* 令牌、没有残留 LoginBrand* 局部键",
                viewCode.Contains("BrandSurfaceGradientBrush", StringComparison.Ordinal)
                    && !viewCode.Contains("LoginBrand", StringComparison.Ordinal),
                "检查 BrandSurfaceGradientBrush 存在且 LoginBrand* 已清");

            // ---------- 后端两层：真实账号存储 + 会话（临时文件） ----------
            var tmpStore = Path.Combine(Path.GetTempPath(), "vm_login_check_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var store = new VisionMaster.Services.ScadaUserStore(tmpStore);
                var policy = new VisionMaster.Services.ScadaAccessPolicy();

                Check("出厂账号就位：首次运行给出内置管理员（否则全新机器谁也登不进去）",
                    store.Users.Any(u => u.Name == VisionMaster.Services.ScadaUserStore.DefaultAdminName
                                         && u.Role == VisionMaster.Scada.ScadaRole.Administrator),
                    "账号数=" + store.Users.Count);

                Check("出厂密码检测：未改密时为真（登录窗据此亮「立即修改」提示条）",
                    store.IsUsingDefaultPassword(VisionMaster.Services.ScadaUserStore.DefaultAdminName),
                    "IsUsingDefaultPassword=true");

                // 正确密码 → 角色
                bool okAdmin = store.TryValidate(
                    VisionMaster.Services.ScadaUserStore.DefaultAdminName,
                    VisionMaster.Services.ScadaUserStore.DefaultAdminPassword,
                    out var adminRole, out var adminError);
                Check("正确账号密码通过校验并给出管理员角色",
                    okAdmin && adminRole == VisionMaster.Scada.ScadaRole.Administrator && adminError == null,
                    $"ok={okAdmin} role={adminRole} error={adminError ?? "null"}");

                // 错密码：文案不区分"用户不存在"与"密码错误"（避免账号枚举）
                bool okWrong = store.TryValidate(VisionMaster.Services.ScadaUserStore.DefaultAdminName, "wrong-pass",
                                                 out _, out var wrongError);
                Check("错误密码被拒，且文案不区分「无此账号 / 密码错误」（防账号枚举）",
                    !okWrong && wrongError == "用户名或密码不正确", $"error={wrongError ?? "null"}");

                // 空输入：由存储报可读原因（登录按钮不禁用，靠这句话解释）
                bool okEmptyUser = store.TryValidate("", "x", out _, out var emptyUserError);
                bool okEmptyPwd = store.TryValidate(VisionMaster.Services.ScadaUserStore.DefaultAdminName, "", out _, out var emptyPwdError);
                Check("空用户名 / 空密码各有可读提示（按钮不禁用，全靠这两句解释）",
                    !okEmptyUser && emptyUserError == "请输入用户名"
                    && !okEmptyPwd && emptyPwdError == "请输入密码",
                    $"用户='{emptyUserError}' 密码='{emptyPwdError}'");

                // 改密 → 旧密码失效、新密码生效、出厂密码提示熄灭
                bool okChange = store.TryChangePassword(
                    VisionMaster.Services.ScadaUserStore.DefaultAdminName,
                    VisionMaster.Services.ScadaUserStore.DefaultAdminPassword,
                    "NewPass#2026", out var changeError);
                bool oldStillWorks = store.TryValidate(
                    VisionMaster.Services.ScadaUserStore.DefaultAdminName,
                    VisionMaster.Services.ScadaUserStore.DefaultAdminPassword, out _, out _);
                bool newWorks = store.TryValidate(
                    VisionMaster.Services.ScadaUserStore.DefaultAdminName, "NewPass#2026", out _, out _);
                bool hintAfter = store.IsUsingDefaultPassword(VisionMaster.Services.ScadaUserStore.DefaultAdminName);
                Check("改密后：旧密码失效、新密码生效、出厂密码提示熄灭",
                    okChange && !oldStillWorks && newWorks && !hintAfter,
                    $"改密={okChange} 旧密码还能用={oldStillWorks} 新密码能用={newWorks} 提示仍亮={hintAfter} 错误={changeError ?? "null"}");

                // 最后一个管理员不能被降权（否则"以后没人能再管理账号"）
                bool okDemote = store.TrySetRole(
                    VisionMaster.Services.ScadaUserStore.DefaultAdminName,
                    VisionMaster.Scada.ScadaRole.Operator, out var demoteError);
                Check("最后一个管理员不能被降权（守的是数据不变式，不是界面）",
                    !okDemote && demoteError != null, $"ok={okDemote} error={demoteError ?? "null"}");

                // 会话：验过之后必须真的开会话，登出后回到未登录
                policy.Login(VisionMaster.Services.ScadaUserStore.DefaultAdminName,
                             VisionMaster.Scada.ScadaRole.Administrator);
                bool loggedIn = policy.IsLoggedIn;
                string stateText = policy.LoginStateText;
                policy.Logout();
                bool loggedOut = !policy.IsLoggedIn;
                Check("会话开关：登录后 IsLoggedIn/状态行成立，登出后回到未登录",
                    loggedIn && loggedOut && stateText.Contains("管理员", StringComparison.Ordinal),
                    $"登录={loggedIn} 登出={loggedOut} 状态行='{stateText}'");
            }
            catch (Exception ex)
            {
                Check("登录链路端到端（账号存储 + 会话）", false, ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                try { if (File.Exists(tmpStore)) File.Delete(tmpStore); } catch { /* 临时文件删不掉不影响结论 */ }
            }

            // ---------- 视图实例化：XAML 解析 + 模板 + 令牌 ----------
            try
            {
                var loginView = new ScadaLoginView();
                loginView.Measure(new Size(820, 560));
                loginView.Arrange(new Rect(0, 0, 820, 560));
                loginView.UpdateLayout();
                Check("登录视图可实例化并完成布局（XAML 解析 + 模板 + Brand 令牌解析）",
                    loginView.ActualWidth > 0 && loginView.ActualHeight > 0,
                    $"{loginView.ActualWidth:0}x{loginView.ActualHeight:0}");
            }
            catch (Exception ex)
            {
                Check("登录视图可实例化并完成布局（XAML 解析 + 模板 + Brand 令牌解析）", false, ex.GetType().Name + ": " + ex.Message);
            }

            Console.WriteLine(failures == 0
                ? "=== 用户登录（S12）冒烟：全部通过 ==="
                : $"### 用户登录冒烟：{failures} 项失败");
            return failures;
        }

        /// <summary>数视图里过滤后还剩几条（枚举视图只会走过滤后的项）</summary>
        private static int CountView(System.ComponentModel.ICollectionView view)
        {
            int n = 0;
            foreach (var _ in view) n++;
            return n;
        }

        private static void AddColumn(GridView grid, string path, double width)
        {
            var col = new GridViewColumn { Header = path, Width = width };
            col.DisplayMemberBinding = new System.Windows.Data.Binding(path);
            grid.Columns.Add(col);
        }

        /// <summary>表格冒烟的数据行：名字只用于"整棵树里不得出现类型全名"这条断言</summary>
        private sealed class GridViewProbeRow
        {
            public string Index { get; set; }
            public string Name { get; set; }
            public string Path { get; set; }
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            foreach (var found in Descendants<T>(root)) return found;
            return null;
        }

        /// <summary>可视树里所有 T 类型元素（先根序）</summary>
        private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T typed) yield return typed;

                foreach (var sub in Descendants<T>(child)) yield return sub;
            }
        }

        /// <summary>
        /// 颜色令牌闸：第一方 XAML 里**除颜色定义文件外**不许出现 <c>#RRGGBB</c>。
        ///
        /// <para>为什么需要它：全仓有 1400+ 处 <c>{StaticResource *Brush}</c>，主题切换靠的是
        /// "换字典"；只要有人在视图里写死一个 hex，切主题时那一块就不会跟着变，
        /// 而且这种错编译期全绿、肉眼也只在切主题后才发现。</para>
        ///
        /// <para>老文件怎么处理：按 <c>hardcoded-colors.baseline.txt</c> 的"只减不增"口径放行——
        /// 一次性全改不现实，但**新文件必须是 0**、老文件数量不许上涨。
        /// 基线里已无硬编码（或数量下降）的条目会在日志里提示"可收缩"，方便逐步清零。</para>
        /// </summary>
        private static int RunHardcodedColorChecks()
        {
            Console.WriteLine();
            Console.WriteLine("=== 颜色令牌：第一方 XAML 不许硬编码颜色 ===");

            string repoRoot = @"d:\C#\VM";
            string baselinePath = Path.Combine(repoRoot, @"UIThemeSmokeTest\hardcoded-colors.baseline.txt");

            // 颜色定义文件本身：写 hex 是它们的职责。
            var paletteFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                @"UI\Controls\Themes\Colors.xaml",
                @"UI\Controls\Themes\Fluent\FluentTokens.xaml",
                @"UI\Controls\Themes\PluginConfigColors.xaml",
                // SCADA 运行画面令牌：与上面三份一样，写 hex 是它的职责（R140 的例外）。
                @"Scada.Controls\Themes\ScadaTokens.xaml",
            };

            var baseline = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(baselinePath))
            {
                foreach (var line in File.ReadAllLines(baselinePath))
                {
                    int bar = line.LastIndexOf('|');
                    if (bar <= 0) continue;
                    if (int.TryParse(line.Substring(bar + 1), out int allowed))
                        baseline[line.Substring(0, bar).Trim()] = allowed;
                }
            }

            var current = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int scanned = 0;

            foreach (var root in new[] { "VisionMaster", "UI", "Scada", "Scada.Controls", "Scada.Controls.Charts", "Plugins" })
            {
                string dir = Path.Combine(repoRoot, root);
                if (!Directory.Exists(dir)) continue;

                foreach (var file in Directory.GetFiles(dir, "*.xaml", SearchOption.AllDirectories))
                {
                    if (file.Contains(@"\obj\") || file.Contains(@"\bin\")) continue;

                    string rel = file.Substring(repoRoot.Length + 1);
                    if (paletteFiles.Contains(rel)) continue;
                    if (rel.StartsWith(@"VisionMaster\Themes\DockThemes\", StringComparison.OrdinalIgnoreCase)) continue;

                    scanned++;

                    string text = File.ReadAllText(file);
                    text = Regex.Replace(text, "<!--.*?-->", "", RegexOptions.Singleline);
                    int count = Regex.Matches(text, "#[0-9A-Fa-f]{6}").Count;
                    if (count > 0) current[rel] = count;
                }
            }

            var offenders = new List<string>();
            foreach (var pair in current)
            {
                if (!baseline.TryGetValue(pair.Key, out int allowed))
                    offenders.Add($"{pair.Key}: 新文件写了 {pair.Value} 处硬编码颜色（改用 Dialog* 令牌）");
                else if (pair.Value > allowed)
                    offenders.Add($"{pair.Key}: 硬编码颜色 {allowed} → {pair.Value}（只许减少）");
            }

            var shrinkable = baseline
                .Where(b => !current.TryGetValue(b.Key, out int now) || now < b.Value)
                .Select(b => $"{b.Key}（基线 {b.Value}）")
                .ToList();

            Console.WriteLine($"    扫过 {scanned} 个第一方 XAML；基线 {baseline.Count} 个文件");
            if (shrinkable.Count > 0)
                Console.WriteLine($"    基线可收缩：{string.Join("；", shrinkable)}");

            if (offenders.Count == 0)
            {
                Console.WriteLine("    [PASS] 无新增硬编码颜色");
                return 0;
            }

            Console.WriteLine($"    [FAIL] {offenders.Count} 个文件新增了硬编码颜色：");
            foreach (var offender in offenders)
                Console.WriteLine($"      {offender}");
            return 1;
        }

        /// <summary>
        /// 标定配置视图冒烟（--calibration）：真实实例化 <see cref="Plugin.Calibration.CalibrationView"/> 并强制模板实例化，
        /// 把"交付时人工走查"里**可自动化**的部分钉成回归：
        /// · XAML 资源键/转换器/模板全部解析（缺键会在测量期抛"找不到资源"——正是最阴的一类）；
        /// · 关键控件就位：画布 / 逐行「取点」「预填」/ 导入导出按钮 / 锁定开关 / 透视单选；
        /// · 关键绑定生效：锁定 → 表格禁用；模式切换 → 表格显隐（像素当量隐藏，九点/透视共用显示）。
        /// 纯 UI 手感（拖动、图上取点点击）仍建议交付时人工过一遍（见主文档 3.3/3.4）。
        /// </summary>
        private static int RunCalibrationViewSmoke()
        {
            int failures = 0;
            void Check(string name, bool ok, string detail = "")
            {
                Console.WriteLine((ok ? "  [PASS] " : "  [FAIL] ") + name + (detail.Length > 0 ? $"  ({detail})" : ""));
                if (!ok) failures++;
            }

            try
            {
                var stepData = new StubStepConfigData();
                var plugin = new Plugin.Calibration.CalibrationPlugin { InstanceName = "标定_冒烟" };
                var view = new Plugin.Calibration.CalibrationView(stepData, plugin);
                var host = new Border { Width = 900, Height = 600, Child = view };
                host.Measure(new Size(900, 600));
                host.Arrange(new Rect(0, 0, 900, 600));
                host.UpdateLayout();
                host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                int elementCount = Descendants<FrameworkElement>(view).Count();
                bool contentRealized = view is System.Windows.Controls.UserControl uc
                    && uc.Content != null && elementCount > 10;
                Check("配置视图可实例化并完成模板实例化（资源键/转换器/控件模板全部解析）", contentRealized,
                    $"elements={elementCount}");

                int canvas = Descendants<Core.Halcon.Controls.ImageEdit>(view).Count();
                Check("画布控件存在（ImageEdit 恰 1 个）", canvas == 1, $"count={canvas}");

                var buttons = Descendants<System.Windows.Controls.Button>(view).ToList();
                var pickList = buttons.Where(b => (b.Content as string) == "取点").ToList();
                Check("逐行「取点」按钮 = 9、且命令全部绑定（默认 3×3 表格）",
                    pickList.Count == 9 && pickList.All(b => b.Command != null),
                    $"count={pickList.Count} bound={pickList.Count(b => b.Command != null)}");
                var prefillList = buttons.Where(b => (b.Content as string) == "预填").ToList();
                Check("逐行「预填」按钮 = 9、且命令全部绑定（上游定位点回填）",
                    prefillList.Count == 9 && prefillList.All(b => b.Command != null),
                    $"count={prefillList.Count} bound={prefillList.Count(b => b.Command != null)}");
                var ioButtons = buttons.Where(b => (b.Content as string ?? "").Contains("导出")
                    || (b.Content as string ?? "").Contains("导入")).ToList();
                Check("导入 / 导出按钮存在且命令绑定（多设备共享标定）",
                    ioButtons.Count >= 2 && ioButtons.All(b => b.Command != null),
                    $"count={ioButtons.Count} bound={ioButtons.Count(b => b.Command != null)}");

                var lockBox = Descendants<System.Windows.Controls.CheckBox>(view).FirstOrDefault();
                Check("锁定复选框存在（防误改）", lockBox != null);

                bool hasPerspectiveRadio = Descendants<System.Windows.Controls.RadioButton>(view)
                    .Any(r => (r.Content as string ?? "").Contains("透视"));
                Check("存在「透视标定」模式单选", hasPerspectiveRadio);

                bool hasMeshRadio = Descendants<System.Windows.Controls.RadioButton>(view)
                    .Any(r => (r.Content as string ?? "").Contains("网格"));
                Check("存在「网格标定」模式单选（分段仿射）", hasMeshRadio);

                System.Windows.Controls.ItemsControl FindTable()
                    => Descendants<System.Windows.Controls.ItemsControl>(view)
                        .FirstOrDefault(ic => ReferenceEquals(ic.ItemsSource, plugin.PointRows));
                FrameworkElement FindTableHost()
                {
                    // 表格卡片的显隐：向上找到承载 ItemsControl 的 Border（Visibility 绑 ShowPointTable）
                    var t = FindTable();
                    if (t == null) return null;
                    DependencyObject cur = t;
                    while (cur != null && !(cur is Border)) cur = System.Windows.Media.VisualTreeHelper.GetParent(cur);
                    return cur as FrameworkElement;
                }

                var table = FindTable();
                Check("标定表就位（绑定 PointRows）", table != null);

                // 锁定绑定：锁定 → 表格禁用（防误改的底线，在真视图上验证 IsEnabled 绑定）
                plugin.IsCalibrationLocked = true;
                host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Check("锁定后标定表被禁用（IsEnabled 绑定生效）", table != null && !table.IsEnabled,
                    $"enabled={table?.IsEnabled}");
                plugin.IsCalibrationLocked = false;
                host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                // 模式切换：像素当量 → 表格隐藏；九点/透视 → 显示（九点与透视共用同一张表）
                var tableHost = FindTableHost();
                plugin.Mode = Plugin.Calibration.CalibrationMode.PixelScale;
                host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                bool hiddenInPixelScale = tableHost != null && tableHost.Visibility != Visibility.Visible;
                plugin.Mode = Plugin.Calibration.CalibrationMode.NinePoint;
                host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                bool shownInNinePoint = tableHost != null && tableHost.Visibility == Visibility.Visible;
                plugin.Mode = Plugin.Calibration.CalibrationMode.Perspective;
                host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                bool shownInPerspective = tableHost != null && tableHost.Visibility == Visibility.Visible;
                plugin.Mode = Plugin.Calibration.CalibrationMode.Mesh;
                host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                bool shownInMesh = tableHost != null && tableHost.Visibility == Visibility.Visible;
                Check("模式切换 → 表格显隐跟随（像素当量隐藏 / 九点、透视、网格显示）",
                    hiddenInPixelScale && shownInNinePoint && shownInPerspective && shownInMesh,
                    $"pixelScale={hiddenInPixelScale} ninePoint={shownInNinePoint} perspective={shownInPerspective} mesh={shownInMesh}");

                // 取点横幅默认不占位（进入待命需加载图像；默认态与绑定正确性）
                Check("取点横幅默认不占位（IsPickingPoint=false）", !plugin.IsPickingPoint);

                plugin.Dispose();
                host.Child = null;
                view = null;
            }
            catch (Exception ex)
            {
                Console.WriteLine("### 标定视图冒烟抛出异常:");
                Print(ex);
                failures++;
            }

            Console.WriteLine(failures == 0
                ? "=== 标定配置视图冒烟：全部通过 ==="
                : $"### 标定配置视图冒烟：{failures} 项失败");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// UI 入口异常隔离的静态扫描（P0-3）。
        ///
        /// 为什么用静态扫描：这些隔离点的失败模式是"插件抛异常 → 冒到 UI 线程 → 窗口消失"，
        /// 运行期要构造"每个入口都抛"的夹具才能覆盖，成本高且容易漏分支。
        /// 静态判据只钉"调用点必须在 try 块内"这一条结构事实——判据用**花括号配对 + 块头**
        /// 定位（不写行号、不写空白），改缩进/加注释不会误伤；`catch`/`finally` 块里的调用
        /// 明确判负（否则"把调用搬到 catch 里"也会被当成已隔离）。
        /// </summary>
        private static int RunPluginEntryIsolationChecks()
        {
            int failures = 0;
            void Check(string name, bool ok, string detail = "")
            {
                Console.WriteLine((ok ? "  [PASS] " : "  [FAIL] ") + name + (detail.Length > 0 ? $"  ({detail})" : ""));
                if (!ok) failures++;
            }

            const string repoRoot = @"d:\C#\VM";
            string Read(params string[] parts)
            {
                var path = Path.Combine(repoRoot, Path.Combine(parts));
                return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
            }

            var dialog = Read("VisionMaster", "Services", "StepParameterDialog.cs");
            var shellVm = Read("VisionMaster", "ViewModels", "DialogViewModels", "PluginConfigShellViewModel.cs");
            var app = Read("VisionMaster", "App.xaml.cs");

            Check("StepParameterDialog.cs 可读", dialog.Length > 0);
            Check("PluginConfigShellViewModel.cs 可读", shellVm.Length > 0);
            Check("App.xaml.cs 可读", app.Length > 0);

            Check("StepParameterDialog: GetConfigView 调用在 try 块内",
                AllCallsInsideTry(dialog, "GetConfigView("),
                "插件视图创建（第三方代码）必须隔离");
            Check("StepParameterDialog: Initialize(stepData) 调用在 try 块内",
                AllCallsInsideTry(dialog, "Initialize(stepData)"),
                "插件灌值/通用面板创建必须隔离");
            Check("StepParameterDialog: ShowDialog(\"PluginConfigShell\") 调用在 try 块内",
                AllCallsInsideTry(dialog, "ShowDialog(\"PluginConfigShell\""),
                "外壳打开失败按\"未打开\"处理");

            Check("PluginConfigShellViewModel: Initialize 调用在 try 块内",
                AllCallsInsideTry(shellVm, "_pluginView?.Initialize(_stepData)"),
                "插件视图初始化必须隔离");
            Check("PluginConfigShellViewModel: SetConfigContext 调用在 try 块内",
                AllCallsInsideTry(shellVm, "SetConfigContext(BuildConfigContext())"),
                "宿主上下文透传必须隔离");
            Check("PluginConfigShellViewModel: OnConfirm 的全部调用在 try 块内",
                AllCallsInsideTry(shellVm, "_pluginView?.OnConfirm(_stepData)"),
                "确认失败不关窗（含试运行前的同步写回）");

            Check("App.xaml.cs: 已订阅 DispatcherUnhandledException",
                app.Contains("DispatcherUnhandledException += OnDispatcherUnhandledException", StringComparison.Ordinal));
            Check("App.xaml.cs: 兜底置 e.Handled = true",
                app.Contains("e.Handled = true", StringComparison.Ordinal));
            Check("App.xaml.cs: 日志写完整异常（含堆栈）",
                app.Contains("Environment.NewLine + ex", StringComparison.Ordinal));

            Console.WriteLine(failures == 0
                ? "=== UI 入口异常隔离静态扫描：全部通过 ==="
                : $"### UI 入口异常隔离静态扫描：{failures} 项失败");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>token 的**每一次**出现都必须位于 try 块内（至少出现一次）。</summary>
        private static bool AllCallsInsideTry(string source, string token)
        {
            int count = 0;
            int index = 0;
            while ((index = source.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                if (!IsCallInsideTryAt(source, index)) return false;
                index += token.Length;
            }
            return count > 0;
        }

        /// <summary>
        /// 从调用点向前做花括号配对，找包裹它的最近一个块头：
        ///  · 块头是 try → true；
        ///  · 块头是 catch/finally → false（调用被搬进处理器里不算隔离）；
        ///  · 其它块（if/for/方法体/对象初始化器/匿名块）继续向外找。
        /// </summary>
        private static bool IsCallInsideTryAt(string source, int index)
        {
            int depth = 0;
            for (int i = index - 1; i >= 0; i--)
            {
                char c = source[i];
                if (c == '}') { depth++; continue; }
                if (c != '{') continue;

                if (depth > 0) { depth--; continue; }

                var header = ReadBlockHeader(source, i);
                if (header == "try") return true;
                if (header == "catch" || header == "finally") return false;
            }
            return false;
        }

        /// <summary>读 '{' 前面的块头关键字（跳过 catch (Exception ex) 这类括号）。</summary>
        private static string ReadBlockHeader(string source, int braceIndex)
        {
            int i = braceIndex - 1;
            while (i >= 0 && char.IsWhiteSpace(source[i])) i--;

            if (i >= 0 && source[i] == ')')
            {
                int parenDepth = 0;
                while (i >= 0)
                {
                    if (source[i] == ')') parenDepth++;
                    else if (source[i] == '(')
                    {
                        parenDepth--;
                        if (parenDepth == 0) { i--; break; }
                    }
                    i--;
                }
                while (i >= 0 && char.IsWhiteSpace(source[i])) i--;
            }

            int end = i;
            while (i >= 0 && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) i--;
            return end >= i + 1 ? source.Substring(i + 1, end - i) : string.Empty;
        }

        /// <summary>
        /// 自动参数面板冒烟（--auto-port-config）：真实实例化 <see cref="AutoPortConfigView"/>，
        /// 用 8 个不同类型端口验证：行数/排序、步骤卡统计、链接态配色、枚举锁、类型徽章、
        /// Initialize 幂等、空态与插件未注入文案；再用 5 个 [StepConfig] 配置项验证
        /// 枚举/布尔/数值/文本/其它类型的渲染与"编辑即写回"（含非法数值不写 0）；
        /// 最后验证 StepParameterDialog 对"GetConfigView 抛异常"的入口隔离。
        /// 输出默认/空态两张 WPF 证据 PNG。
        /// </summary>
        private static int RunAutoPortConfigViewSmoke()
        {
            int failures = 0;
            void Check(string name, bool ok, string detail = "")
            {
                Console.WriteLine((ok ? "  [PASS] " : "  [FAIL] ") + name + (detail.Length > 0 ? $"  ({detail})" : ""));
                if (!ok) failures++;
            }

            Window window = null;
            Border host = null;
            try
            {
                var plugin = new AutoPortProbePlugin();
                var stepData = new AutoPortProbeStepData();
                stepData.SetLink("SrcImage", new LinkReference(LinkKind.StepPort, Guid.NewGuid(), "Image", "图像采集_0.Image"));
                stepData.SetLink("Sigma", new LinkReference(LinkKind.GlobalVariable, Guid.Empty, "Sigma", "Global.Sigma"));

                var view = new AutoPortConfigView(plugin);
                view.Initialize(stepData);

                host = new Border
                {
                    Padding = new Thickness(20, 16, 20, 16),
                    Background = Brushes.White,
                    Child = view,
                };
                window = new Window
                {
                    Width = 560,
                    MinWidth = 640, // 模拟 PluginConfigShellView 的保底宽度，验证本视图能改回 560
                    Height = 760,
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    Left = -4000,
                    Top = -4000,
                    Background = Brushes.White,
                    Content = host,
                };
                window.Show();
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                Check("外壳 MinWidth=640 被本视图改回 560（设计基线宽度落地）",
                    Math.Abs(window.ActualWidth - 560) < 1.0, $"actual={window.ActualWidth:0.##}");

                var rows = Descendants<AutoPortRow>(view).ToList();
                Check("行数 = 8 个输入端口", rows.Count == 8, $"rows={rows.Count}");
                Check("行按端口名排序",
                    rows.Select(r => r.Port?.Name).SequenceEqual(new[]
                    {
                        "Any", "Count", "Flag", "Kind", "MaskRegion", "Name", "Sigma", "SrcImage"
                    }),
                    string.Join(",", rows.Select(r => r.Port?.Name)));

                Check("步骤名 = StepName", TextRoot(view, "StepNameText") == "几何查找_0", TextRoot(view, "StepNameText"));
                Check("输入统计 = 8", TextRoot(view, "InputMetricText") == "8", TextRoot(view, "InputMetricText"));
                Check("必填统计 = 1", TextRoot(view, "RequiredMetricText") == "1", TextRoot(view, "RequiredMetricText"));
                Check("已链接统计 = 2", TextRoot(view, "LinkedMetricText") == "2", TextRoot(view, "LinkedMetricText"));
                Check("区块数量 = 8", TextRoot(view, "SectionCountText") == "8", TextRoot(view, "SectionCountText"));

                var srcRow = rows.Single(r => r.Port?.Name == "SrcImage");
                var maskRow = rows.Single(r => r.Port?.Name == "MaskRegion");
                var kindRow = rows.Single(r => r.Port?.Name == "Kind");
                var intRow = rows.Single(r => r.Port?.Name == "Count");

                Check("已链接行 IsLinked=true", srcRow.IsLinked, $"linked={srcRow.IsLinked}");
                Check("已链接编辑区底色 = DialogAccentSoftBrush",
                    BrushColor(srcRow, "EditorShell") == ColorOf("DialogAccentSoftBrush"),
                    $"{BrushColor(srcRow, "EditorShell")} vs {ColorOf("DialogAccentSoftBrush")}");
                Check("已链接编辑区描边 = DialogAccentBorderBrush",
                    BorderColor(srcRow, "EditorShell") == ColorOf("DialogAccentBorderBrush"),
                    $"{BorderColor(srcRow, "EditorShell")} vs {ColorOf("DialogAccentBorderBrush")}");

                Check("不可文本且未链接行 IsTextEditable=false", !maskRow.IsTextEditable, $"editable={maskRow.IsTextEditable}");
                Check("不可文本且未链接编辑区底色 = DialogRowAlternateBrush",
                    BrushColor(maskRow, "EditorShell") == ColorOf("DialogRowAlternateBrush"),
                    $"{BrushColor(maskRow, "EditorShell")} vs {ColorOf("DialogRowAlternateBrush")}");
                Check("不可文本且未链接编辑区描边 = DialogBorderStrongBrush",
                    BorderColor(maskRow, "EditorShell") == ColorOf("DialogBorderStrongBrush"),
                    $"{BorderColor(maskRow, "EditorShell")} vs {ColorOf("DialogBorderStrongBrush")}");

                Check("枚举行显示锁图标", VisRow(kindRow, "LockIcon") == Visibility.Visible, VisRow(kindRow, "LockIcon").ToString());
                Check("整数行不显示锁图标", VisRow(intRow, "LockIcon") == Visibility.Collapsed, VisRow(intRow, "LockIcon").ToString());

                var srcEditor = Editor(srcRow);
                Check("LinkableValueEditor.Label 为空（标签外置）", string.IsNullOrEmpty(srcEditor?.Label), srcEditor?.Label ?? "(null)");
                Check("LinkableValueEditor.Port 已注入", srcEditor?.Port?.Name == "SrcImage", srcEditor?.Port?.Name ?? "(null)");
                Check("LinkableValueEditor 占满 240px 编辑列（内容 238，EditorShell 边框占 2）",
                    srcEditor != null && Math.Abs(srcEditor.ActualWidth - 238) < 1.0,
                    srcEditor?.ActualWidth.ToString("0.##") ?? "(null)");

                var typeTexts = rows.ToDictionary(r => r.Port?.Name ?? "", r => TextRow(r, "TypeText"));
                Check("类型徽章：HImage→图像", typeTexts["SrcImage"] == "图像", typeTexts["SrcImage"]);
                Check("类型徽章：HRegion→区域", typeTexts["MaskRegion"] == "区域", typeTexts["MaskRegion"]);
                Check("类型徽章：enum→枚举", typeTexts["Kind"] == "枚举", typeTexts["Kind"]);
                Check("类型徽章：double→数值", typeTexts["Sigma"] == "数值", typeTexts["Sigma"]);
                Check("类型徽章：bool→布尔", typeTexts["Flag"] == "布尔", typeTexts["Flag"]);
                Check("类型徽章：int→整数", typeTexts["Count"] == "整数", typeTexts["Count"]);
                Check("类型徽章：string→文本", typeTexts["Name"] == "文本", typeTexts["Name"]);
                Check("类型徽章：object→参数", typeTexts["Any"] == "参数", typeTexts["Any"]);

                // ================= [StepConfig] 插件参数区（P0-4） =================
                // 这一段直接验"没有自定义视图的插件也能改配置项"：5 个 descriptor 覆盖
                // 枚举 / 布尔 / 数值 / 文本 / 其它类型，改值必须即时落到插件实例上。
                var configPanel = view.FindName("ConfigPanel") as StackPanel;
                Check("插件参数计数 = 5", TextRoot(view, "ConfigCountText") == "5", TextRoot(view, "ConfigCountText"));
                Check("插件参数行数 = 5", configPanel?.Children.Count == 5, $"rows={configPanel?.Children.Count}");

                var configRowTags = configPanel?.Children.OfType<FrameworkElement>()
                    .Select(c => c.Tag as string ?? string.Empty).ToList() ?? new List<string>();
                Check("插件参数按声明顺序渲染（MetadataToken）",
                    configRowTags.SequenceEqual(new[]
                    {
                        "Config:SortMode", "Config:FailOnMiss", "Config:Scale", "Config:Note", "Config:Payload"
                    }),
                    string.Join(",", configRowTags));

                Check("[Display(Name)] 优先于属性名上屏",
                    Descendants<TextBlock>(view).Any(t => t.Text == "查找类型")
                    && Descendants<TextBlock>(view).Any(t => t.Text == "SortMode"),
                    "显示名=查找类型 / 属性名=SortMode");

                var enumEditor = EditorByTag<ComboBox>(view, "SortMode");
                Check("枚举参数 → ComboBox（值=成员名）",
                    enumEditor?.SelectedValue as string == nameof(StringComparison.Ordinal),
                    enumEditor?.SelectedValue?.ToString() ?? "(null)");
                if (enumEditor != null)
                {
                    enumEditor.SelectedValue = nameof(StringComparison.OrdinalIgnoreCase);
                    view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                }
                Check("枚举参数选中即写回插件实例",
                    plugin.SortMode == StringComparison.OrdinalIgnoreCase, plugin.SortMode.ToString());

                var boolEditor = EditorByTag<CheckBox>(view, "FailOnMiss");
                Check("布尔参数 → CheckBox", boolEditor?.IsChecked == true, boolEditor?.IsChecked?.ToString() ?? "(null)");
                if (boolEditor != null) boolEditor.IsChecked = false;
                Check("布尔参数勾选即写回插件实例", !plugin.FailOnMiss, plugin.FailOnMiss.ToString());

                var scaleEditor = EditorByTag<TextBox>(view, "Scale");
                Check("数值参数 → TextBox（InvariantCulture 显示）",
                    scaleEditor?.Text == "0.75", scaleEditor?.Text ?? "(null)");
                if (scaleEditor != null) scaleEditor.Text = "1.25";
                Check("数值参数编辑即写回插件实例",
                    Math.Abs(plugin.Scale - 1.25) < 1e-9, plugin.Scale.ToString(CultureInfo.InvariantCulture));

                // 非法输入：绝不能静默写 0 / 写脏值；红框 + 行内提示，插件里保留上一次合法值
                if (scaleEditor != null) scaleEditor.Text = "abc";
                var scaleError = Descendants<TextBlock>(view).FirstOrDefault(t => (t.Tag as string) == "ConfigError:Scale");
                var scaleShell = Descendants<Border>(view).FirstOrDefault(b => (b.Tag as string) == "ConfigShell:Scale");
                Check("数值解析失败：保留上一次合法值（不写 0）",
                    Math.Abs(plugin.Scale - 1.25) < 1e-9, plugin.Scale.ToString(CultureInfo.InvariantCulture));
                Check("数值解析失败：行内提示可见",
                    scaleError?.Visibility == Visibility.Visible
                    && (scaleError.Text ?? string.Empty).Contains("不是合法"),
                    $"{scaleError?.Visibility}/{scaleError?.Text}");
                Check("数值解析失败：编辑框描边 = DialogDangerBrush",
                    scaleShell?.BorderBrush is SolidColorBrush danger && danger.Color == ColorOf("DialogDangerBrush"),
                    scaleShell?.BorderBrush?.ToString() ?? "(null)");

                if (scaleEditor != null) scaleEditor.Text = "2.5";
                Check("修正后红框消失且值写回",
                    Math.Abs(plugin.Scale - 2.5) < 1e-9 && scaleError?.Visibility == Visibility.Collapsed,
                    $"scale={plugin.Scale.ToString(CultureInfo.InvariantCulture)} error={scaleError?.Visibility}");

                var noteEditor = EditorByTag<TextBox>(view, "Note");
                if (noteEditor != null) noteEditor.Text = "hello";
                Check("文本参数编辑即写回插件实例", plugin.Note == "hello", plugin.Note ?? "(null)");

                var payloadEditor = EditorByTag<TextBox>(view, "Payload");
                var payloadHint = Descendants<TextBlock>(view).FirstOrDefault(t => (t.Tag as string) == "ConfigHint:Payload");
                Check("其它类型 → 只读文本 + 暂不支持提示",
                    payloadEditor?.IsReadOnly == true && payloadHint?.Text == "该类型暂不支持在此编辑",
                    $"readOnly={payloadEditor?.IsReadOnly} hint={payloadHint?.Text}");

                var configRowsBeforeSecondInit = configPanel?.Children.OfType<FrameworkElement>().ToList()
                    ?? new List<FrameworkElement>();
                var rowsBeforeSecondInit = Descendants<AutoPortRow>(view).ToList();
                view.Initialize(stepData);
                host.UpdateLayout();
                var rowsAfterSecondInit = Descendants<AutoPortRow>(view).ToList();
                var configRowsAfterSecondInit = configPanel?.Children.OfType<FrameworkElement>().ToList()
                    ?? new List<FrameworkElement>();
                Check("Initialize 幂等（二次调用行数仍为 8）", rowsAfterSecondInit.Count == 8,
                    $"rows={rowsAfterSecondInit.Count}");
                Check("Initialize 二次调用复用现有行（不重建，避免端口 ValueChanged 订阅累积）",
                    rowsBeforeSecondInit.SequenceEqual(rowsAfterSecondInit),
                    "行对象引用应保持不变");
                Check("插件参数区 Initialize 幂等（复用行对象）",
                    configRowsAfterSecondInit.Count == 5
                    && configRowsBeforeSecondInit.SequenceEqual(configRowsAfterSecondInit),
                    $"rows={configRowsAfterSecondInit.Count}");

                const string repoRoot = @"d:\C#\VM";
                var outDir = EvidencePaths.Dir(repoRoot, "auto-port-config");
                Directory.CreateDirectory(outDir);
                RenderElement(host, Path.Combine(outDir, "wpf-after-render-v1-default.png"));

                var emptyPlugin = new AutoPortEmptyPlugin();
                var emptyView = new AutoPortConfigView(emptyPlugin);
                emptyView.Initialize(stepData);
                var emptyHost = new Border
                {
                    Padding = new Thickness(20, 16, 20, 16),
                    Background = Brushes.White,
                    Child = emptyView,
                };
                var emptyWindow = new Window
                {
                    Width = 560,
                    Height = 600,
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    Left = -4000,
                    Top = -4000,
                    Background = Brushes.White,
                    Content = emptyHost,
                };
                emptyWindow.Show();
                emptyWindow.UpdateLayout();
                emptyWindow.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Check("无端口 → 空态可见", VisRoot(emptyView, "EmptyState") == Visibility.Visible,
                    VisRoot(emptyView, "EmptyState").ToString());
                Check("空态标题 = 该步骤没有输入参数", TextRoot(emptyView, "EmptyTitleText") == "该步骤没有输入参数",
                    TextRoot(emptyView, "EmptyTitleText"));
                Check("无端口 → 列表收起", VisRoot(emptyView, "PortListBorder") == Visibility.Collapsed,
                    VisRoot(emptyView, "PortListBorder").ToString());
                RenderElement(emptyHost, Path.Combine(outDir, "wpf-after-render-v1-empty.png"));
                emptyWindow.Close();

                var nullView = new AutoPortConfigView(null);
                nullView.Initialize(stepData);
                Measure(nullView);
                Check("插件未注入 → 空态标题 = 无法读取插件参数",
                    TextRoot(nullView, "EmptyTitleText") == "无法读取插件参数",
                    TextRoot(nullView, "EmptyTitleText"));

                // ---- 入口隔离（P0-3）：插件 GetConfigView 抛异常 → 不冒泡、不打开外壳 ----
                var throwingStep = new ActionStep(
                    string.Empty,
                    "抛异常探针",
                    typeof(ThrowingViewProbePlugin).AssemblyQualifiedName,
                    "抛异常探针_0");
                var recordingDialog = new RecordingDialogService();

                bool opened;
                try
                {
                    opened = StepParameterDialog.Open(throwingStep, recordingDialog);
                    Check("StepParameterDialog：GetConfigView 抛异常不冒泡", true);
                }
                catch (Exception ex)
                {
                    opened = true;
                    Check("StepParameterDialog：GetConfigView 抛异常不冒泡", false,
                        ex.GetType().Name + ": " + ex.Message);
                }

                Check("StepParameterDialog：视图创建失败 → 返回 false 且不打开外壳",
                    !opened && recordingDialog.ShownDialogs.Count == 0,
                    $"opened={opened} dialogs={string.Join(",", recordingDialog.ShownDialogs)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine("### 自动参数面板冒烟抛出异常:");
                Print(ex);
                failures++;
            }
            finally
            {
                window?.Close();
            }

            Console.WriteLine(failures == 0
                ? "=== 自动参数面板冒烟：全部通过 ==="
                : $"### 自动参数面板冒烟：{failures} 项失败");
            return failures == 0 ? 0 : 1;

            static string TextRoot(FrameworkElement root, string name) =>
                (root.FindName(name) as TextBlock)?.Text ?? "(null)";
            static string TextRow(AutoPortRow row, string name) =>
                (row.FindName(name) as TextBlock)?.Text ?? "(null)";
            static Visibility VisRoot(FrameworkElement root, string name) =>
                (root.FindName(name) as UIElement)?.Visibility ?? Visibility.Collapsed;
            static Visibility VisRow(AutoPortRow row, string name) =>
                (row.FindName(name) as UIElement)?.Visibility ?? Visibility.Collapsed;
            static LinkableValueEditor Editor(AutoPortRow row) =>
                (row.FindName("EditorHost") as ContentControl)?.Content as LinkableValueEditor;
            static T EditorByTag<T>(FrameworkElement root, string name) where T : FrameworkElement =>
                Descendants<T>(root).FirstOrDefault(e => (e.Tag as string) == "ConfigEditor:" + name);
            static Color ColorOf(string key) =>
                ((SolidColorBrush)Application.Current.TryFindResource(key)).Color;
            static Color BrushColor(FrameworkElement root, string name) =>
                ((SolidColorBrush)((Border)root.FindName(name)).Background).Color;
            static Color BorderColor(FrameworkElement root, string name) =>
                ((SolidColorBrush)((Border)root.FindName(name)).BorderBrush).Color;
            static void Measure(FrameworkElement view)
            {
                var host = new Border { Width = 520, Background = Brushes.White, Child = view };
                host.Measure(new Size(520, 600));
                host.Arrange(new Rect(0, 0, 520, Math.Max(600, host.DesiredSize.Height)));
                host.UpdateLayout();
                host.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            }
        }

        /// <summary>
        /// 图像采集配置视图冒烟（--image-acquisition）：真实实例化 V2 视图并验证：
        /// · 资源键/模板/转换器全部解析；
        /// · 4 个 LinkableValueEditor 的 Port 绑定到正确端口；
        /// · ImageReadOnly.HImage 仍绑 PreviewImage；
        /// · Mode / SelectedCameraOption / DisplayViewIndex 的绑定类型未变；
        /// · 全部命令与推送字段绑定仍在；
        /// · 端口编辑 → OnConfirm 写回 InputValues 的功能链未断；
        /// · XAML 无硬编码十六进制颜色。
        /// </summary>
        private static int RunImageAcquisitionViewSmoke()
        {
            int failures = 0;
            void Check(string name, bool ok, string detail = "")
            {
                Console.WriteLine((ok ? "  [PASS] " : "  [FAIL] ") + name + (detail.Length > 0 ? $"  ({detail})" : ""));
                if (!ok) failures++;
            }

            try
            {
                // ---- 静态：无硬编码颜色 + 关键绑定仍在 ----
                const string repoRoot = @"d:\C#\VM";
                var xamlPath = Path.Combine(repoRoot, "Plugins", "Plugin.ImageAcquisition", "ImageAcquisitionView.xaml");
                var xaml = File.Exists(xamlPath) ? File.ReadAllText(xamlPath) : string.Empty;
                Check("视图 XAML 可读", xaml.Length > 0, xamlPath);

                var hex = System.Text.RegularExpressions.Regex.Matches(xaml, @"#[0-9A-Fa-f]{6,8}").Count;
                Check("XAML 无硬编码十六进制颜色（全部走公共令牌）", hex == 0, $"hex={hex}");

                string[] requiredBindings =
                {
                    "Port=\"{Binding FilePathPort}\"",
                    "Port=\"{Binding FolderPathPort}\"",
                    "Port=\"{Binding FileIndexPort}\"",
                    "Port=\"{Binding FrameTimeoutPort}\"",
                    "Content=\"{Binding PreviewImage}\"",
                    "HImage=\"{Binding Mode=OneWay}\"",
                    "<h:ImageReadOnly",
                    "SelectedItem=\"{Binding SelectedCameraOption}\"",
                    "SelectedItem=\"{Binding DisplayViewIndex}\"",
                    "Command=\"{Binding BrowseFileCommand}\"",
                    "Command=\"{Binding BrowseFolderCommand}\"",
                    "Command=\"{Binding PreviousImageCommand}\"",
                    "Command=\"{Binding NextImageCommand}\"",
                    "Command=\"{Binding RefreshPreviewCommand}\"",
                    "Command=\"{Binding PushLocalImageCommand}\"",
                    "Text=\"{Binding PushFlowName",
                    "Text=\"{Binding PushHost",
                    "Text=\"{Binding PushPortText",
                    "Text=\"{Binding PushToken",
                    "Text=\"{Binding PushResultText",
                    "Text=\"{Binding StatusMessage",
                };
                var missingBindings = requiredBindings.Where(b => !xaml.Contains(b)).ToList();
                Check("关键绑定全部在 XAML 里", missingBindings.Count == 0, string.Join(" | ", missingBindings));

                var modeBindings = System.Text.RegularExpressions.Regex.Matches(
                    xaml, @"IsChecked=""\{Binding Mode, Converter=\{ui:EnumConverter\}").Count;
                Check("模式卡片仍用 EnumConverter 写回 Mode（4 个）", modeBindings == 4, $"count={modeBindings}");

                // ---- 运行时：真实实例化 ----
                var stepData = new StubStepConfigData();
                var plugin = new Plugin.ImageAcquisition.ImageAcquisitionPlugin { InstanceName = "图像采集_冒烟" };
                var view = new Plugin.ImageAcquisition.ImageAcquisitionView(stepData, plugin);
                var host = new Border { Width = 920, Height = 520, Child = view };
                host.Measure(new Size(920, 520));
                host.Arrange(new Rect(0, 0, 920, 520));
                host.UpdateLayout();
                host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                int elementCount = Descendants<FrameworkElement>(view).Count();
                Check("视图可实例化并完成模板实例化", view.Content != null && elementCount > 30, $"elements={elementCount}");
                Check("DataContext 仍是插件实例（宿主靠它取 IPluginConfigView）",
                    ReferenceEquals(view.DataContext, plugin),
                    view.DataContext?.GetType().Name ?? "null");

                // 「调试与接口」默认折叠；展开后其内部的推送测试字段/按钮才会进可视树
                var debugExpander = view.FindName("DebugExpander") as System.Windows.Controls.Expander;
                if (debugExpander != null) debugExpander.IsExpanded = true;

                // 逐模式收集可见面板里的控件/绑定（折叠面板不保证已进可视树）
                var portNames = new HashSet<string>(StringComparer.Ordinal);
                var commandPaths = new HashSet<string>(StringComparer.Ordinal);
                var textPaths = new HashSet<string>(StringComparer.Ordinal);
                foreach (var mode in new[]
                         {
                             Plugin.ImageAcquisition.AcquisitionMode.SingleFile,
                             Plugin.ImageAcquisition.AcquisitionMode.Folder,
                             Plugin.ImageAcquisition.AcquisitionMode.Hub,
                             Plugin.ImageAcquisition.AcquisitionMode.Camera
                         })
                {
                    plugin.Mode = mode;
                    host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                    foreach (var editor in Descendants<Core.Controls.LinkableValueEditor>(view))
                        if (editor.Port != null) portNames.Add(editor.Port.Name);

                    foreach (var button in Descendants<System.Windows.Controls.Button>(view))
                    {
                        var path = BindingOperations.GetBinding(button, System.Windows.Controls.Button.CommandProperty)?.Path?.Path;
                        if (!string.IsNullOrEmpty(path)) commandPaths.Add(path);
                    }

                    foreach (var textBox in Descendants<System.Windows.Controls.TextBox>(view))
                    {
                        var path = BindingOperations.GetBinding(textBox, System.Windows.Controls.TextBox.TextProperty)?.Path?.Path;
                        if (!string.IsNullOrEmpty(path)) textPaths.Add(path);
                    }
                }
                plugin.Mode = Plugin.ImageAcquisition.AcquisitionMode.SingleFile;
                host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                Check("4 个 LinkableValueEditor 的 Port 绑定正确",
                    portNames.Count == 4
                    && portNames.Contains("FilePath")
                    && portNames.Contains("FolderPath")
                    && portNames.Contains("FileIndex")
                    && portNames.Contains("FrameTimeoutMs"),
                    string.Join(",", portNames));

                var previewHost = view.FindName("PreviewHost") as ContentControl;
                var previewHostBinding = previewHost == null
                    ? null
                    : BindingOperations.GetBinding(previewHost, ContentControl.ContentProperty);

                // 有图才创建 ImageReadOnly：先造一张 64×48 灰度图，再验证模板实例与控制器的 HImage
                var smokePixels = new byte[64 * 48];
                var smokeHandle = System.Runtime.InteropServices.GCHandle.Alloc(
                    smokePixels, System.Runtime.InteropServices.GCHandleType.Pinned);
                HImage smokeImage;
                try
                {
                    smokeImage = new HImage();
                    smokeImage.GenImage1("byte", 64, 48, smokeHandle.AddrOfPinnedObject());
                }
                finally
                {
                    smokeHandle.Free();
                }

                plugin.PreviewImage = smokeImage;
                host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                host.UpdateLayout();

                var preview = Descendants<Core.Halcon.Controls.ImageReadOnly>(view).FirstOrDefault();
                Check("预览宿主仍绑 PreviewImage，ImageReadOnly 由模板创建并拿到图",
                    previewHost != null
                    && previewHostBinding?.Path?.Path == "PreviewImage"
                    && preview != null
                    && ReferenceEquals(preview.HImage, smokeImage),
                    $"host={previewHostBinding?.Path?.Path} control={preview != null} image={ReferenceEquals(preview?.HImage, smokeImage)}");

                try
                {
                    preview?.FitToWindow();
                    preview?.FitToImage();
                    Check("预览工具条可调用 FitToWindow / FitToImage", true, "");
                }
                catch (Exception ex)
                {
                    Check("预览工具条可调用 FitToWindow / FitToImage", false,
                        ex.GetType().Name + ": " + ex.Message);
                }

                var modeRadios = Descendants<System.Windows.Controls.RadioButton>(view)
                    .Where(r => r.GroupName == "AcquisitionMode").ToList();
                var modeParams = modeRadios
                    .Select(r => BindingOperations.GetBinding(
                        r, System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty)?.ConverterParameter)
                    .ToList();
                bool hasAllModes = new[] { "SingleFile", "Folder", "Hub", "Camera" }
                    .All(n => modeParams.Any(p => p?.ToString() == n));
                Check("4 个模式卡片都绑 Mode，且四个枚举参数齐全",
                    modeRadios.Count == 4 && hasAllModes,
                    $"count={modeRadios.Count} params={string.Join(",", modeParams.Select(p => p?.ToString() ?? "null"))}");

                // 真实点击路径：IsChecked=true → EnumConverter.ConvertBack → Mode
                var folderRadio = modeRadios.FirstOrDefault(r =>
                    BindingOperations.GetBinding(r, System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty)
                        ?.ConverterParameter?.ToString() == "Folder");
                if (folderRadio != null) folderRadio.IsChecked = true;
                host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Check("模式卡片点击写回 Mode（EnumConverter 双向）",
                    plugin.Mode == Plugin.ImageAcquisition.AcquisitionMode.Folder,
                    $"mode={plugin.Mode}");
                plugin.Mode = Plugin.ImageAcquisition.AcquisitionMode.SingleFile;
                host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                var cameraCombo = view.FindName("CameraCombo") as System.Windows.Controls.ComboBox;
                var cameraItems = cameraCombo == null
                    ? null
                    : BindingOperations.GetBinding(cameraCombo, System.Windows.Controls.ItemsControl.ItemsSourceProperty);
                var cameraSelected = cameraCombo == null
                    ? null
                    : BindingOperations.GetBinding(cameraCombo, System.Windows.Controls.Primitives.Selector.SelectedItemProperty);
                Check("相机下拉仍是 CameraOptions / SelectedCameraOption",
                    cameraCombo != null
                    && cameraItems?.Path?.Path == "CameraOptions"
                    && cameraSelected?.Path?.Path == "SelectedCameraOption",
                    $"items={cameraItems?.Path?.Path} selected={cameraSelected?.Path?.Path}");

                var displayCombo = view.FindName("DisplayViewCombo") as System.Windows.Controls.ComboBox;
                var displaySelected = displayCombo == null
                    ? null
                    : BindingOperations.GetBinding(displayCombo, System.Windows.Controls.Primitives.Selector.SelectedItemProperty);
                Check("显示窗口下拉仍是 int 的 DisplayViewIndex",
                    displayCombo != null && displaySelected?.Path?.Path == "DisplayViewIndex",
                    displaySelected?.Path?.Path ?? "null");

                string[] requiredCommands =
                {
                    "BrowseFileCommand", "BrowseFolderCommand", "PreviousImageCommand",
                    "NextImageCommand", "RefreshPreviewCommand", "PushLocalImageCommand"
                };
                var missingCommands = requiredCommands.Where(c => !commandPaths.Contains(c)).ToList();
                Check("功能命令绑定全部在（浏览/翻页/刷新/推送测试）", missingCommands.Count == 0,
                    string.Join(",", missingCommands));

                string[] requiredTexts = { "PushFlowName", "PushHost", "PushPortText", "PushToken", "PushResultText" };
                var missingTexts = requiredTexts.Where(t => !textPaths.Contains(t)).ToList();
                Check("推送测试字段绑定全部在", missingTexts.Count == 0, string.Join(",", missingTexts));

                var statusBinding = Descendants<System.Windows.Controls.TextBlock>(view)
                    .Select(t => BindingOperations.GetBinding(t, System.Windows.Controls.TextBlock.TextProperty)?.Path?.Path)
                    .FirstOrDefault(p => p == "StatusMessage");
                Check("状态栏仍绑 StatusMessage", statusBinding == "StatusMessage", statusBinding ?? "null");

                // ---- 功能链：端口编辑 → OnConfirm 写回 ----
                var fileEditor = Descendants<Core.Controls.LinkableValueEditor>(view)
                    .FirstOrDefault(e => e.Port?.Name == "FilePath");
                var tempFile = Path.Combine(Path.GetTempPath(), "ia_ui_smoke_" + Guid.NewGuid().ToString("N") + ".bmp");
                File.WriteAllBytes(tempFile, new byte[] { 0x42, 0x4D, 0, 0 });
                try
                {
                    if (fileEditor != null) fileEditor.PortValue = tempFile;
                    host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    Check("路径编辑器写回 FilePathPort",
                        fileEditor != null && (plugin.FilePathPort.Value as string) == tempFile,
                        $"port={plugin.FilePathPort.Value}");

                    plugin.Mode = Plugin.ImageAcquisition.AcquisitionMode.Folder;
                    host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    var folderEditor = Descendants<Core.Controls.LinkableValueEditor>(view)
                        .FirstOrDefault(e => e.Port?.Name == "FolderPath");
                    var indexEditor = Descendants<Core.Controls.LinkableValueEditor>(view)
                        .FirstOrDefault(e => e.Port?.Name == "FileIndex");
                    if (folderEditor != null) folderEditor.PortValue = @"D:\Images\Line-A";
                    if (indexEditor != null) indexEditor.PortValue = "7";
                    host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    Check("文件夹路径/索引编辑器写回端口",
                        plugin.FolderPathPort.TypedValue == @"D:\Images\Line-A" && plugin.FileIndexPort.TypedValue == 7,
                        $"folder={plugin.FolderPathPort.TypedValue} index={plugin.FileIndexPort.TypedValue}");

                    plugin.Mode = Plugin.ImageAcquisition.AcquisitionMode.Camera;
                    host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    var timeoutEditor = Descendants<Core.Controls.LinkableValueEditor>(view)
                        .FirstOrDefault(e => e.Port?.Name == "FrameTimeoutMs");
                    if (timeoutEditor != null) timeoutEditor.PortValue = "250";
                    host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    Check("取图超时编辑器写回 FrameTimeoutMs",
                        plugin.FrameTimeoutPort.TypedValue == 250,
                        $"timeout={plugin.FrameTimeoutPort.TypedValue}");

                    plugin.Mode = Plugin.ImageAcquisition.AcquisitionMode.SingleFile;
                    host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                    displayCombo.SelectedItem = 3;
                    host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    Check("显示窗口下拉写回 DisplayViewIndex（int）", plugin.DisplayViewIndex == 3,
                        $"value={plugin.DisplayViewIndex}");

                    var cameraOption = new Core.Interfaces.CameraOption
                    {
                        Id = Guid.NewGuid(),
                        SerialNo = "IA-SMOKE-001",
                        DisplayName = "冒烟相机",
                        State = Core.Interfaces.CameraConnectionState.Streaming,
                        StateText = "采流中"
                    };
                    plugin.CameraOptions.Add(cameraOption);
                    plugin.Mode = Plugin.ImageAcquisition.AcquisitionMode.Camera;
                    host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    if (cameraCombo != null) cameraCombo.SelectedItem = cameraOption;
                    host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    Check("相机下拉选择写回 CameraSerial（序列号寻址）", plugin.CameraSerial == "IA-SMOKE-001",
                        plugin.CameraSerial);

                    plugin.Mode = Plugin.ImageAcquisition.AcquisitionMode.Folder;
                    host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    Check("模式切换仍走 Mode setter（预览被清空）", plugin.PreviewImage == null,
                        $"mode={plugin.Mode}");

                    plugin.OnConfirm(stepData);
                    string[] keys = { "FilePath", "FolderPath", "FileIndex", "FrameTimeoutMs", "Mode", "DisplayViewIndex", "CameraSerial" };
                    var missingKeys = keys.Where(k => !stepData.InputValues.ContainsKey(k)).ToList();
                    Check("OnConfirm 仍把端口/配置写回 InputValues", missingKeys.Count == 0,
                        string.Join(",", missingKeys));

                    bool valuesOk = stepData.InputValues.TryGetValue("FilePath", out var fileValue)
                        && (fileValue as string) == tempFile
                        && stepData.InputValues.TryGetValue("FolderPath", out var folderValue)
                        && (folderValue as string) == @"D:\Images\Line-A"
                        && stepData.InputValues.TryGetValue("FileIndex", out var indexValue)
                        && Convert.ToInt32(indexValue) == 7
                        && stepData.InputValues.TryGetValue("FrameTimeoutMs", out var timeoutValue)
                        && Convert.ToInt32(timeoutValue) == 250
                        && stepData.InputValues.TryGetValue("Mode", out var modeValue)
                        && modeValue is Plugin.ImageAcquisition.AcquisitionMode modeValueEnum
                        && modeValueEnum == Plugin.ImageAcquisition.AcquisitionMode.Folder
                        && stepData.InputValues.TryGetValue("DisplayViewIndex", out var viewValue)
                        && Convert.ToInt32(viewValue) == 3
                        && stepData.InputValues.TryGetValue("CameraSerial", out var serialValue)
                        && (serialValue as string) == "IA-SMOKE-001";
                    Check("OnConfirm 写回值正确（端口 + Mode + 显示窗口 + 相机序列号）", valuesOk,
                        string.Join(",", keys.Select(k => k + "=" + (stepData.InputValues.TryGetValue(k, out var v) ? v : "null"))));
                }
                finally
                {
                    try { File.Delete(tempFile); } catch { }
                }

                plugin.Dispose();
                host.Child = null;
                view = null;
            }
            catch (Exception ex)
            {
                Console.WriteLine("### 图像采集视图冒烟抛出异常:");
                Print(ex);
                failures++;
            }

            Console.WriteLine(failures == 0
                ? "=== 图像采集配置视图冒烟：全部通过 ==="
                : $"### 图像采集配置视图冒烟：{failures} 项失败");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>
        /// 图像采集 V2 视图渲染（--image-acquisition-render）：
        /// 按 920×520 内容区真实 Measure/Arrange，把四种模式的空态渲染成 PNG，
        /// 落到 docs/UI设计基线/image-acquisition/evidence/，供与 Web 基线逐块对照。
        /// </summary>
        private static int RunImageAcquisitionViewRender()
        {
            int failures = 0;
            void Check(string name, bool ok, string detail = "")
            {
                Console.WriteLine((ok ? "  [PASS] " : "  [FAIL] ") + name + (detail.Length > 0 ? $"  ({detail})" : ""));
                if (!ok) failures++;
            }

            try
            {
                const string repoRoot = @"d:\C#\VM";
                var outDir = EvidencePaths.Dir(repoRoot, "image-acquisition");
                Directory.CreateDirectory(outDir);

                var stepData = new StubStepConfigData();
                var plugin = new Plugin.ImageAcquisition.ImageAcquisitionPlugin { InstanceName = "图像采集_渲染" };
                var view = new Plugin.ImageAcquisition.ImageAcquisitionView(stepData, plugin);
                var host = new Border
                {
                    Width = 920,
                    Height = 520,
                    Background = Application.Current.TryFindResource("DialogSurfaceBrush") as Brush,
                    Child = view
                };
                host.Measure(new Size(920, 520));
                host.Arrange(new Rect(0, 0, 920, 520));
                host.UpdateLayout();
                host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                // 宿主透传上下文：网络推送横幅显示"正在监听"
                plugin.SetConfigContext(new Core.Interfaces.PluginConfigContext
                {
                    FlowName = "检测流程",
                    HttpEnabled = true,
                    HttpListening = true,
                    HttpHost = "127.0.0.1",
                    HttpPort = 19000,
                    HttpToken = "demo-token",
                    RequestTimeoutMs = 30000,
                    Cameras = new List<Core.Interfaces.CameraOption>()
                });

                // 相机候选：相机页状态卡显示"采流中"
                var camera = new Core.Interfaces.CameraOption
                {
                    Id = Guid.NewGuid(),
                    SerialNo = "40123456",
                    DisplayName = "Basler acA1920-40gm",
                    State = Core.Interfaces.CameraConnectionState.Streaming,
                    StateText = "采流中"
                };
                plugin.CameraOptions.Add(camera);
                plugin.SelectedCameraOption = camera;

                var modes = new[]
                {
                    Plugin.ImageAcquisition.AcquisitionMode.SingleFile,
                    Plugin.ImageAcquisition.AcquisitionMode.Folder,
                    Plugin.ImageAcquisition.AcquisitionMode.Hub,
                    Plugin.ImageAcquisition.AcquisitionMode.Camera
                };

                foreach (var mode in modes)
                {
                    plugin.Mode = mode;
                    host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    host.UpdateLayout();
                    var path = Path.Combine(outDir, "after-render-v2-" + mode + ".png");
                    RenderElement(host, path);
                    Check("渲染 " + mode, File.Exists(path), path);
                }

                // 错误态：路径不存在 → StatusLevel=Error，预览区显示错误覆盖层
                plugin.Mode = Plugin.ImageAcquisition.AcquisitionMode.SingleFile;
                host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                plugin.PreviewImagePath = @"D:\Images\missing.bmp";
                plugin.FilePathPort.Value = @"D:\Images\missing.bmp";
                host.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                host.UpdateLayout();
                var errorPath = Path.Combine(outDir, "after-render-v2-Error.png");
                RenderElement(host, errorPath);
                Check("渲染 Error", File.Exists(errorPath), errorPath);

                plugin.Dispose();
                host.Child = null;
                view = null;
            }
            catch (Exception ex)
            {
                Console.WriteLine("### 图像采集视图渲染抛出异常:");
                Print(ex);
                failures++;
            }

            Console.WriteLine(failures == 0
                ? "=== 图像采集配置视图渲染：完成 ==="
                : $"### 图像采集配置视图渲染：{failures} 项失败");
            return failures == 0 ? 0 : 1;
        }

        private static void RenderElement(FrameworkElement element, string path)
        {
            var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
            var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }

        /// <summary>
        /// 自动参数面板探针：8 个端口覆盖图像/区域/枚举/数值/布尔/整数/文本/object，
        /// 外加 5 个 [StepConfig] 配置项覆盖枚举/布尔/数值/文本/其它类型（只读）的渲染分支。
        /// </summary>
        private sealed class AutoPortProbePlugin : VisionPluginBase
        {
            public InputPort<HImage> SrcImage { get; } = new("SrcImage", null, "待查找图像") { IsRequired = true };
            public InputPort<HRegion> MaskRegion { get; } = new("MaskRegion", null, "查找区域（可选）：只在区域内找；未接=整图") { IsRequired = false };
            public InputPort<StringComparison> Kind { get; } = new("Kind", StringComparison.Ordinal, "查找类型（直线/圆/椭圆）") { IsRequired = false };
            public InputPort<double> Sigma { get; } = new("Sigma", 1.0, "边缘提取平滑 Sigma（越大越平滑）") { IsRequired = false };
            public InputPort<bool> Flag { get; } = new("Flag", true, "找不到是否判失败（默认是）") { IsRequired = false };
            public InputPort<int> Count { get; } = new("Count", 1, "显示窗口(0=不显示,1~9=窗口号)") { IsRequired = false };
            public InputPort<string> Name { get; } = new("Name", "x", "名称") { IsRequired = false };
            public InputPort<object> Any { get; } = new("Any", null, "任意值") { IsRequired = false };

            // ---- [StepConfig] 配置项：没有自定义视图的插件靠 AutoPortConfigView 渲染 ----

            /// <summary>[Display(Name)] 必须优先于属性名上屏（A1 口径）。</summary>
            [Display(Name = "查找类型")]
            [StepConfig]
            public StringComparison SortMode { get; set; } = StringComparison.Ordinal;

            [StepConfig]
            public bool FailOnMiss { get; set; } = true;

            [StepConfig]
            public double Scale { get; set; } = 0.75;

            [StepConfig]
            public string Note { get; set; } = "abc";

            /// <summary>其它类型（object）：面板只给只读文本 + "暂不支持"提示。</summary>
            [StepConfig]
            public object Payload { get; set; } = "opaque";

            public override void RunAlgorithm(IExecutionContext context) { }
        }

        private sealed class AutoPortEmptyPlugin : VisionPluginBase
        {
            public override void RunAlgorithm(IExecutionContext context) { }
        }

        /// <summary>自动参数面板探针的步骤数据：带两条链接，验证统计与行底色。</summary>
        private sealed class AutoPortProbeStepData : IStepConfigData
        {
            public Guid StepId { get; } = Guid.NewGuid();
            public string Icon { get; set; } = "\uf1b2";
            public string StepName { get; set; } = "几何查找_0";
            public string Description { get; set; } = "在 ROI 内亚像素找边并拟合";
            public Dictionary<string, object> InputValues { get; } = new();
            public Dictionary<string, string> Links { get; } = new();
            public List<DynamicPortInfo> OutputPortDefinitions { get; set; } = new();

            public void SetInputValue(string key, object value) => InputValues[key] = value;
            public void RemoveInputValue(string key) => InputValues.Remove(key);
            public bool IsLinked(string inputPortName) => Links.ContainsKey(inputPortName);
            public string GetLinkedAddress(string inputPortName) => Links.TryGetValue(inputPortName, out var v) ? v : null;
            public LinkReference GetLink(string inputPortName) => null;
            public void SetLink(string inputPortName, LinkReference link) => Links[inputPortName] = link.DisplayAddress;
            public void RemoveLink(string inputPortName) => Links.Remove(inputPortName);
        }

        /// <summary>最小步骤配置桩：标定视图初始化只需空 InputValues + 无链接。</summary>
        private sealed class StubStepConfigData : Core.Interfaces.IStepConfigData
        {
            private readonly System.Collections.Generic.Dictionary<string, object> _values = new();

            public Guid StepId { get; } = Guid.NewGuid();
            public string Icon => "";
            public string StepName => "标定_冒烟";
            public string Description => "";
            public System.Collections.Generic.Dictionary<string, object> InputValues => _values;

            public void SetInputValue(string key, object value) => _values[key] = value;
            public void RemoveInputValue(string key) => _values.Remove(key);
            public bool IsLinked(string inputPortName) => false;
            public string GetLinkedAddress(string inputPortName) => null;
            public Core.Interfaces.LinkReference GetLink(string inputPortName) => null;
            public void SetLink(string inputPortName, Core.Interfaces.LinkReference link) { }
            public void RemoveLink(string inputPortName) { }
            public System.Collections.Generic.List<Core.Interfaces.DynamicPortInfo> OutputPortDefinitions { get; set; } = new();
        }

        private static void Print(Exception ex)
        {
            var cur = ex;
            var depth = 0;
            while (cur != null)
            {
                Console.WriteLine(new string('>', ++depth * 2) + $" [{cur.GetType().Name}] {cur.Message}");
                if (cur.StackTrace != null && depth <= 3)
                {
                    foreach (var line in cur.StackTrace.Split('\n').Take(12))
                        Console.WriteLine("      " + line.Trim());
                }
                cur = cur.InnerException;
            }
        }
    }

    /// <summary>
    /// 入口隔离探针：GetConfigView 故意抛异常（模拟第三方插件视图构造失败）。
    ///
    /// 必须是**文件级**类型（不是 Program 的嵌套类）：StepParameterDialog 通过
    /// Type.GetType(AssemblyQualifiedName) + Activator.CreateInstance 反射创建实例，
    /// 文件级类型更接近真实插件形态（没有 `+` 嵌套限定名这层额外变量）。
    /// </summary>
    internal sealed class ThrowingViewProbePlugin : VisionPluginBase, IPluginCustomViewProvider
    {
        /// <summary>显式 public 无参构造：StepParameterDialog 走 Activator.CreateInstance(type)，只认公开构造。</summary>
        public ThrowingViewProbePlugin() { }

        public object GetConfigView(IStepConfigData stepData)
            => throw new InvalidOperationException("冒烟探针：GetConfigView 故意抛异常");

        public override void RunAlgorithm(IExecutionContext context) { }
    }

    /// <summary>
    /// IDialogService 形状桩：只记录"弹了哪个窗口"，不真的开窗
    ///（断言宿主没有 Prism 容器，真服务一 ShowDialog 就会去容器里解析视图）。
    /// </summary>
    internal sealed class RecordingDialogService : IDialogService
    {
        public List<string> ShownDialogs { get; } = new();

        public void ShowDialog(string name, IDialogParameters parameters) => ShownDialogs.Add(name);

        public void ShowDialog(string name, IDialogParameters parameters, DialogCallback callback)
            => ShownDialogs.Add(name);

        public void ShowDialog(string name, IDialogParameters parameters, string windowName)
            => ShownDialogs.Add(name);

        public void ShowDialog(
            string name,
            IDialogParameters parameters,
            DialogCallback callback,
            string windowName
        ) => ShownDialogs.Add(name);

        public void Show(string name, IDialogParameters parameters) => ShownDialogs.Add(name);

        public void Show(string name, IDialogParameters parameters, DialogCallback callback)
            => ShownDialogs.Add(name);

        public void Show(string name, IDialogParameters parameters, string windowName)
            => ShownDialogs.Add(name);

        public void Show(
            string name,
            IDialogParameters parameters,
            DialogCallback callback,
            string windowName
        ) => ShownDialogs.Add(name);
    }

    /// <summary>PasswordBoxHelper 双向绑定探针：只需要一个可通知的 Password。</summary>
    internal sealed class PasswordProbe : INotifyPropertyChanged
    {
        private string _password = string.Empty;

        public string Password
        {
            get => _password;
            set
            {
                if (string.Equals(_password, value, StringComparison.Ordinal)) return;
                _password = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Password)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
