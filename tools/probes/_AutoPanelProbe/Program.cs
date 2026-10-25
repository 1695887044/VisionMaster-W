// 自动参数面板探针：把「不提供自定义配置界面」的算子逐个渲染成 PNG，供目视检查。
//
// 判据（与 VisionMaster/Services/StepParameterDialog.cs:63-99 同口径）：
//   1. 插件类型不实现 IPluginCustomViewProvider（等价于没有自定义配置界面）；
//   2. 步骤数据是 IStepConfigData（ActionStep 派生类 ActionProbeStep）。
// 这两种情况下真机会走 new AutoPortConfigView(plugin)。本探针复刻这条路径。
//
// 用法：
//   AutoPanelProbe.exe --out <目录>   渲染全部并输出 PNG + 一份清单 txt
//   AutoPanelProbe.exe                默认输出 artifacts/render/<yyyyMMdd>/auto-panel/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Core.Interfaces;
using VisionMaster.Services;
using VisionMaster.Views.DialogViews;

namespace AutoPanelProbe
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            var outDir = ResolveOutDir(args);
            Directory.CreateDirectory(outDir);

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var resourcesLoaded = LoadTheme(app);

            var log = new StringBuilder();
            log.AppendLine($"# 自动参数面板探针  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            log.AppendLine($"# 主题资源: {(resourcesLoaded ? "已加载" : "加载失败（颜色将回落默认）")}");
            log.AppendLine();

            int failures = 0;
            var cases = DiscoverCases(log);
            log.AppendLine($"# 待渲染算子: {cases.Count}");
            log.AppendLine();

            foreach (var c in cases)
            {
                try
                {
                    var (ok, detail) = RenderOne(c, outDir, log);
                    if (!ok) failures++;
                    log.AppendLine($"[{(ok ? "OK  " : "FAIL")}] {c.Name}  ({detail})");
                }
                catch (Exception ex)
                {
                    failures++;
                    log.AppendLine($"[FAIL] {c.Name}  渲染抛异常: {ex.GetType().Name}: {ex.Message}");
                    var inner = ex.InnerException;
                    while (inner != null)
                    {
                        log.AppendLine($"         -> {inner.GetType().Name}: {inner.Message}");
                        inner = inner.InnerException;
                    }
                }
            }

            log.AppendLine();
            log.AppendLine($"=== 合计 {cases.Count} 个算子，失败 {failures} 个 ===");

            var logPath = Path.Combine(outDir, "auto-panel-manifest.txt");
            File.WriteAllText(logPath, log.ToString(), new UTF8Encoding(false));
            Console.WriteLine(log.ToString());
            Console.WriteLine($"[清单] {logPath}");

            app.Shutdown();
            return failures == 0 ? 0 : 1;
        }

        private static string ResolveOutDir(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "--out", StringComparison.OrdinalIgnoreCase))
                    return Path.GetFullPath(args[i + 1]);
            }

            var root = AppContext.BaseDirectory;
            for (int i = 0; i < 6 && root != null; i++)
            {
                if (File.Exists(Path.Combine(root, "VisionMaster.sln")))
                    return Path.Combine(root, "artifacts", "render", DateTime.Now.ToString("yyyyMMdd"), "auto-panel");
                root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
            }

            return Path.Combine(AppContext.BaseDirectory, "auto-panel");
        }

        /// <summary>
        /// 按 App.xaml 的口径合并全局资源：Core.Halcon / UI 的 Generic.xaml + Fluent 主题。
        ///
        /// 为什么不像 App.xaml 那样直接读 App.xaml：本探针只渲染一个 UserControl，
        /// 不需要 PrismApplication 的整条启动链；资源面只需与 UIThemeSmokeTest 等价
        /// （见 UIThemeSmokeTest/Program.cs:644-647 BuildAppResources）。
        /// 少了 Fluent 会让隐式样式缺失 —— 截图与真机不符，就不再是"渲染真实的算子面板"。
        /// </summary>
        private static bool LoadTheme(Application app)
        {
            var merged = app.Resources.MergedDictionaries;
            int loaded = 0;

            void Try(string uri)
            {
                try
                {
                    merged.Add(new ResourceDictionary { Source = new Uri(uri, UriKind.Absolute) });
                    loaded++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"!! 主题字典加载失败 {uri}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            Try("pack://application:,,,/Core.Halcon;component/Generic.xaml");
            Try("pack://application:,,,/UI;component/Themes/Generic.xaml");
            Try("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml");

            // App.xaml 的内联资源（x:Key="Icon" 的字体 / ScadaIconFont 等）不在任何字典文件里，
            // 是 PrismApplication.Resources 的行内 ResourceDictionary。AutoPortRow/AutoPortConfigView
            // 用 {StaticResource Icon} 取字体族，缺了就是 XamlParseException（整段渲染失败）。
            // 这里按同样的键补最小必要项 —— 只补视图真正用到的那个。
            app.Resources["Icon"] = new FontFamily(
                "pack://application:,,,/UI;component/Controls/Asserts/FontFamilys/Font Awesome 6 Pro-Solid-900.otf#Font Awesome 6 Pro Solid");

            // UI;component/Themes/Generic.xaml 已经合并了 IconDictionary.xaml，那里定义了 FA.Solid 等键。
            // 真机用 key="Icon"，是 Shell 启动时从 FA.Light 复刻过来的（见 UIThemeSmokeTest 的同款做法）。
            if (app.Resources.Contains("FA.Solid"))
                app.Resources["Icon"] = app.Resources["FA.Solid"];

            // 缺字形时 WPF 会静默回落到 Segoe UI 画豆腐块，看不出是 URI 错还是家族名错。
            // 这里做一次显式自检：能取到 GlyphTypeface 才认为图标字体可用。
            Console.WriteLine(DiagnoseIconFont());

            return loaded > 0;
        }

        /// <summary>
        /// 图标字体自检：把候选 FontFamily 逐个试一遍，打印实际解析到的真实家族名。
        /// 只打印，不参与断言 —— 目的是让"豆腐块"这种静默失败变成看得见的一行。
        /// </summary>
        private static string DiagnoseIconFont()
        {
            var candidates = new[]
            {
                "pack://application:,,,/UI;component/Controls/Asserts/FontFamilys/Font Awesome 6 Pro-Solid-900.otf#Font Awesome 6 Pro Solid",
                "pack://application:,,,/UI;component/Asserts/FontFamilys/Font Awesome 6 Pro-Solid-900.otf#Font Awesome 6 Pro Solid",
                // App.xaml / IconDictionary.xaml 的原始写法（无 Controls 段）——真机跑得通，探针里未必
                "pack://application:,,,/UI;component/Controls/Asserts/FontFamilys/#Font Awesome 6 Pro Solid"
            };

            var lines = new List<string> { "[图标字体自检]" };
            foreach (var uri in candidates)
            {
                try
                {
                    var family = new FontFamily(uri);
                    var faces = family.GetTypefaces();
                    var names = new List<string>();
                    foreach (var face in faces)
                    {
                        if (face.TryGetGlyphTypeface(out var glyph))
                            names.Add($"{glyph.FamilyNames.Values.FirstOrDefault() ?? "?"} / {glyph.FaceNames.Values.FirstOrDefault() ?? "?"}");
                    }
                    lines.Add(names.Count > 0
                        ? $"  OK  {uri}\n      -> {string.Join(", ", names)}"
                        : $"  EMPTY  {uri}（取不到任何 GlyphTypeface）");
                }
                catch (Exception ex)
                {
                    lines.Add($"  FAIL {uri} -> {ex.GetType().Name}: {ex.Message}");
                }
            }
            return string.Join(Environment.NewLine, lines);
        }

        private sealed class Case
        {
            public string Name;
            public string Category;
            public string TypeName;
            public Type Type;
        }

        /// <summary>
        /// 扫描 Modules\ 下全部 Plugin*.dll，找出「实现 IVisionPlugin 且不实现 IPluginCustomViewProvider」的具体类。
        ///
        /// 为什么不走 PluginService：那个类是宿主启动链的一部分（依赖容器、日志、通知），
        /// 在探针里装配它会把启动链的副作用一起拉进来。这里只做"哪些类型该有自动面板"这一件事。
        /// </summary>
        private static List<Case> DiscoverCases(StringBuilder log)
        {
            var result = new List<Case>();
            var modulesDir = FindModulesDir();
            if (modulesDir == null)
            {
                log.AppendLine("!! 找不到 Modules 目录（应位于仓库根的 Modules\\）");
                return result;
            }

            log.AppendLine($"# 插件目录: {modulesDir}");

            var dlls = Directory.GetFiles(modulesDir, "Plugin*.dll").OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
            foreach (var dll in dlls)
            {
                Assembly asm;
                try
                {
                    asm = Assembly.LoadFrom(dll);
                }
                catch (Exception ex)
                {
                    log.AppendLine($"!! 跳过 {Path.GetFileName(dll)}：加载失败 {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray();
                    log.AppendLine($"!! {Path.GetFileName(dll)} 部分类型加载失败，已取可用部分");
                }
                catch (Exception ex)
                {
                    log.AppendLine($"!! 跳过 {Path.GetFileName(dll)}：GetTypes 失败 {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                foreach (var type in types)
                {
                    if (type == null || !type.IsClass || type.IsAbstract || !type.IsPublic)
                        continue;
                    if (!typeof(IVisionPlugin).IsAssignableFrom(type))
                        continue;
                    if (typeof(IPluginCustomViewProvider).IsAssignableFrom(type))
                        continue; // 有自定义配置界面，不在本次范围
                    if (type.GetConstructor(Type.EmptyTypes) == null)
                    {
                        log.AppendLine($"!! 跳过 {type.FullName}：没有无参构造器，真机同样起不来");
                        continue;
                    }

                    var display = type.GetCustomAttribute<System.ComponentModel.DataAnnotations.DisplayAttribute>();
                    result.Add(new Case
                    {
                        Name = display?.Name ?? type.Name,
                        Category = display?.GroupName ?? "",
                        TypeName = type.FullName,
                        Type = type,
                    });
                }
            }

            return result.OrderBy(c => c.Category, StringComparer.Ordinal)
                         .ThenBy(c => c.Name, StringComparer.Ordinal)
                         .ToList();
        }

        private static string FindModulesDir()
        {
            // 探针的 bin 落在 tools\probes\_AutoPanelProbe\bin\<cfg>\<tfm>\，往上要爬 6 层才到仓库根。
            // 判据用 VisionMaster.sln 而不是"有没有 Modules 子目录"：先撞到 Modules\PluginCache 之类的
            // 同名目录会误判成插件目录，扫描结果直接变成 0 个算子且不报错。
            var root = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && root != null; i++)
            {
                if (File.Exists(Path.Combine(root, "VisionMaster.sln")))
                {
                    var modules = Path.Combine(root, "Modules");
                    return Directory.Exists(modules) ? modules : null;
                }
                root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
            }

            return null;
        }

        private static (bool Ok, string Detail) RenderOne(Case c, string outDir, StringBuilder log)
        {
            var plugin = (IVisionPlugin)Activator.CreateInstance(c.Type);
            var step = new ActionProbeStep(c.Name, c.TypeName);

            if (plugin is VisionPluginBase basePlugin)
                basePlugin.Initialize(step);

            var view = new AutoPortConfigView(plugin);
            view.Initialize(step);

            var host = new Border { Padding = new Thickness(20, 16, 20, 16), Background = Brushes.White, Child = view };
            // 窗口宽度 = 面板内容宽 + 左右 Padding(20+20)。原先 Width=560 且没有扣掉 Padding，
            // 内容实际只有 520 宽，把控件列挤窄、文字被截断 —— 那是探针的错，不是面板的错。
            const double contentWidth = 560;
            const double hostPaddingX = 40;
            var window = new Window
            {
                Width = contentWidth + hostPaddingX,
                MinWidth = contentWidth + hostPaddingX,
                Height = 760,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                Left = -4000,
                Top = -4000,
                Background = Brushes.White,
                Content = host,
            };

            try
            {
                window.Show();
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                // 内容比窗口高时，ScrollViewer 会滚动截断；这里按内容实际高度撑开窗口再截，
                // 保证"一次看到全部行"，而不是靠人脑拼两张图。
                var scroll = FindDescendant<ScrollViewer>(view);
                double contentHeight = window.ActualHeight;
                if (scroll != null && scroll.ExtentHeight > 0)
                    contentHeight = Math.Ceiling(scroll.ExtentHeight) + 96; // 96 = 上下 Padding + 边框余量
                contentHeight = Math.Max(contentHeight, 240);
                contentHeight = Math.Min(contentHeight, 4000);

                window.Height = contentHeight;
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                var bitmaps = RenderToBitmap(window, (int)Math.Ceiling(host.ActualWidth), (int)Math.Ceiling(host.ActualHeight));
                var file = Path.Combine(outDir, SafeFileName($"{c.Category}_{c.Name}_{c.Type.Name}") + ".png");
                using (var fs = File.Create(file))
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmaps));
                    encoder.Save(fs);
                }

                var rows = CountDescendants<AutoPortRow>(view);
                var optionEmpty = CountDescendantsByName(view, "PART_OptionEmpty", "Border", true);
                var emptyHeight = FirstDescendantByName<Border>(view, "PART_OptionEmpty") is { Visibility: Visibility.Visible } eb
                    ? $"{eb.ActualWidth:0}x{eb.ActualHeight:0}"
                    : "-";
                var emptyText = FirstDescendantByName<TextBlock>(view, "PART_OptionEmptyText")?.Text ?? "-";
                var emptyTextSize = FirstDescendantByName<TextBlock>(view, "PART_OptionEmptyText") is { } et
                    ? $"{et.ActualWidth:0}x{et.ActualHeight:0}"
                    : "-";
                var emptyShell = FirstDescendantByName<Border>(view, "EditorShell") is { } es
                    ? $"{es.ActualWidth:0}x{es.ActualHeight:0}"
                    : "-";
                var enumBoxText = FirstDescendantByName<ComboBox>(view, "PART_EnumBox") is { } cmb
                    ? $"{cmb.Visibility}/{cmb.ActualWidth:0}x{cmb.ActualHeight:0}"
                    : "-";
                // 空态被裁时逐层量高度，直接指出是哪一层卡住（2026-10-24 排障用）。
                var chain = optionEmpty > 0
                    ? " 链=" + MeasureChain(FirstDescendantByName<Border>(view, "PART_OptionEmpty"))
                    : string.Empty;
                var configRows = CountDescendantsTagPrefix(view, "Config:");
                var metrics = $"输入={TextOf(view, "InputMetricText")} 必填={TextOf(view, "RequiredMetricText")} 已链接={TextOf(view, "LinkedMetricText")} 参数={TextOf(view, "ConfigCountText")}";
                var detail = $"{Path.GetFileName(file)}  {bitmaps.PixelWidth}x{bitmaps.PixelHeight}  行={rows} 空候选行={optionEmpty} {metrics}";
                if (optionEmpty > 0)
                {
                    detail += $" 空态块={emptyHeight} 文案尺寸={emptyTextSize} 外壳={emptyShell} 下拉={enumBoxText} 空态文案={emptyText}{chain}";
                }
                return (true, detail);
            }
            finally
            {
                window.Close();
                window.Content = null;
            }
        }

        private static BitmapSource RenderToBitmap(Visual visual, int width, int height)
        {
            width = Math.Max(width, 1);
            height = Math.Max(height, 1);
            var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
                dc.DrawRectangle(new VisualBrush(visual), null, new Rect(0, 0, width, height));
            }
            bmp.Render(dv);
            bmp.Freeze();
            return bmp;
        }

        private static string SafeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (var ch in name)
                sb.Append(invalid.Contains(ch) ? '_' : ch);
            return sb.ToString();
        }

        private static T FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T hit) return hit;
                var deeper = FindDescendant<T>(child);
                if (deeper != null) return deeper;
            }
            return null;
        }

        private static int CountDescendants<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) return 0;
            int total = root is T ? 1 : 0;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
                total += CountDescendants<T>(VisualTreeHelper.GetChild(root, i));
            return total;
        }

        private static int CountDescendantsByName(DependencyObject root, string name, string typeName, bool onlyVisible)
        {
            if (root == null) return 0;
            int total = 0;
            if (root is FrameworkElement fe && fe.Name == name && fe.GetType().Name == typeName)
            {
                if (!onlyVisible || fe.Visibility == Visibility.Visible) total++;
            }
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
                total += CountDescendantsByName(VisualTreeHelper.GetChild(root, i), name, typeName, onlyVisible);
            return total;
        }

        private static T? FirstDescendantByName<T>(DependencyObject root, string name) where T : FrameworkElement
        {
            if (root == null) return null;
            if (root is T hit && hit.Name == name) return hit;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var found = FirstDescendantByName<T>(VisualTreeHelper.GetChild(root, i), name);
                if (found != null) return found;
            }
            return null;
        }

        private static int CountDescendantsTagPrefix(DependencyObject root, string prefix)
        {
            if (root == null) return 0;
            int total = 0;
            if (root is FrameworkElement fe && fe.Tag is string tag && tag.StartsWith(prefix, StringComparison.Ordinal))
                total++;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
                total += CountDescendantsTagPrefix(VisualTreeHelper.GetChild(root, i), prefix);
            return total;
        }

        /// <summary>
        /// 从某个元素往上逐层打印 ActualHeight 与影响高度的约束（母版排障用）：
        /// 空态块被裁时一眼看出是哪一层的固定高度卡住的。
        /// </summary>
        private static string MeasureChain(FrameworkElement? start)
        {
            if (start == null) return "(未找到)";
            var parts = new List<string>();
            DependencyObject? node = start;
            int depth = 0;
            while (node != null && depth < 12)
            {
                if (node is FrameworkElement fe)
                {
                    var label = string.IsNullOrEmpty(fe.Name) ? fe.GetType().Name : fe.Name;
                    var h = fe.Height.Equals(double.NaN) ? "-" : fe.Height.ToString("0");
                    var mh = fe.MinHeight.Equals(double.NaN) ? "-" : fe.MinHeight.ToString("0");
                    var mx = fe.MaxHeight.Equals(double.NaN) ? "-" : fe.MaxHeight.ToString("0");
                    parts.Add($"{label}[{fe.ActualHeight:0} h={h} min={mh} max={mx} va={fe.VerticalAlignment}]");
                }
                node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
                depth++;
            }
            return string.Join(" <- ", parts);
        }

        private static string TextOf(DependencyObject root, string name)
        {
            var tb = FindByName(root, name) as TextBlock;
            return tb?.Text ?? "-";
        }

        private static DependencyObject FindByName(DependencyObject root, string name)
        {
            if (root == null) return null;
            if (root is FrameworkElement fe && fe.Name == name) return root;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var hit = FindByName(VisualTreeHelper.GetChild(root, i), name);
                if (hit != null) return hit;
            }
            return null;
        }
    }

    /// <summary>
    /// 步骤数据桩：真机上插件参数面板拿到的就是 ActionStep（实现 IStepConfigData）。
    /// 这里不引入 ActionStep 的流程语义（FlowModel 等），只给面板需要的那几个成员。
    /// </summary>
    internal sealed class ActionProbeStep : IStepConfigData
    {
        private readonly Dictionary<string, object> _values = new();
        private readonly Dictionary<string, string> _links = new();

        public ActionProbeStep(string stepName, string pluginTypeName)
        {
            StepName = stepName + "_0";
            PluginTypeName = pluginTypeName;
        }

        public string PluginTypeName { get; }

        public Guid StepId { get; } = Guid.NewGuid();
        public string Icon => "\uf1b2";
        public string StepName { get; }
        public string Description => "";
        public Dictionary<string, object> InputValues => _values;

        public void SetInputValue(string key, object value) => _values[key] = value;
        public void RemoveInputValue(string key) => _values.Remove(key);
        public bool IsLinked(string inputPortName) => _links.ContainsKey(inputPortName);
        public string GetLinkedAddress(string inputPortName) => _links.TryGetValue(inputPortName, out var v) ? v : null;
        public LinkReference GetLink(string inputPortName) => null;
        public void SetLink(string inputPortName, LinkReference link) => _links[inputPortName] = link.DisplayAddress;
        public void RemoveLink(string inputPortName) => _links.Remove(inputPortName);
        public List<DynamicPortInfo> OutputPortDefinitions { get; set; } = new();
    }
}
