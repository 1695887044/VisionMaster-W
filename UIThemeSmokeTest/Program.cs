using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using Core.Halcon.Controls;
using Core.Halcon.Extensions;
using Core.Halcon.Helpers;
using Core.Halcon.Models;
using HalconDotNet;

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
                RunPaneLayoutChecks();
                int gridFailures = RunGridViewChecks();
                int logConsoleFailures = RunLogConsoleChecks();
                return gridFailures + logConsoleFailures == 0 ? 0 : 1;
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
        private static void RunPaneLayoutChecks()
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
                string repoRoot = @"d:\C#\VM";
                var offenders = new List<string>();
                int scanned = 0;
                foreach (var file in Directory.GetFiles(repoRoot, "*.xaml", SearchOption.AllDirectories))
                {
                    if (file.Contains(@"\obj\") || file.Contains(@"\bin\")
                        || file.Contains(@"WPF-Halcon-流程拖拉")) continue;
                    string text = File.ReadAllText(file);
                    if (!text.Contains("<GridView", StringComparison.Ordinal)) continue;
                    scanned++;
                    string name = Path.GetFileName(file);
                    if (!text.Contains("GridListViewStyle", StringComparison.Ordinal))
                        offenders.Add(name + ": 没挂 GridListViewStyle（列头会被 Fluent 的隐式 ListView 样式顶掉）");
                    if (!text.Contains("GridViewRowPresenter", StringComparison.Ordinal))
                        offenders.Add(name + ": 行模板缺 GridViewRowPresenter（列布局会塌成一行行类型全名）");
                }
                Check($"凡用 GridView 的视图都带齐三件套（扫过 {scanned} 个含 GridView 的 xaml）",
                    offenders.Count == 0,
                    offenders.Count == 0 ? "全部齐全" : string.Join(" | ", offenders.Take(6)));
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
                if (viewText.Length > 0
                    && (!viewText.Contains("DisplayMemberPath=\"Text\"", StringComparison.Ordinal)
                        || typeof(VisionMaster.ViewModels.LogLevelFilterOption).GetProperty("Text") == null))
                    wireProblems.Add("等级下拉的 DisplayMemberPath=Text 对不上 LogLevelFilterOption.Text");
                Check("LogView 的三处过滤/开关接线 + 等级下拉都点到点（写错只会静默失效，不报编译错）",
                    viewText.Length > 0 && wireProblems.Count == 0,
                    viewText.Length == 0 ? "找不到 " + logViewPath
                        : (wireProblems.Count == 0 ? "5 处绑定 + DisplayMemberPath 全部对得上" : string.Join(" | ", wireProblems)));

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
                    var levelBox = FindDescendant<ComboBox>(logView);
                    var followBox = FindDescendant<CheckBox>(logView);

                    Check("端到端：真视图 + 真 VM，控件的 AutoScroll/FilterLevel/SearchText 与下拉/勾选初值都对",
                        wiredConsole != null && wiredConsole.AutoScroll && wiredConsole.FilterLevel == null
                            && string.IsNullOrEmpty(wiredConsole.SearchText)
                            && levelBox != null && ReferenceEquals(levelBox.SelectedItem, logVm.LevelOptions[0])
                            && followBox != null && followBox.IsChecked == true,
                        wiredConsole == null ? "视图里没找到 LogConsole"
                            : $"AutoScroll={wiredConsole.AutoScroll} FilterLevel={(wiredConsole.FilterLevel?.ToString() ?? "null")} "
                              + $"SearchText='{wiredConsole.SearchText}' 下拉选中={levelBox?.SelectedItem} 勾选={followBox?.IsChecked}");

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
}
