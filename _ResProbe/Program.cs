using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace ResProbe
{
    /// <summary>
    /// 全局资源字典「断链」运行期探针。
    ///
    /// 为什么需要它：`{StaticResource}` 写在全局 ResourceDictionary 里、或写在字典内的模板里时，
    /// 作用域是「声明它的那个分册 + 它自己 merge 的子树」——**兄弟分册互相看不见**
    /// （2026-09-23 两次实机崩溃定案，见 docs/code-changes）。断链的三种落脚点：
    ///   · BasedOn              → 解析字典条目就抛 XamlParseException（本次崩溃）
    ///   · 模板内容 / Freezable → 套模板 / 构造时抛
    ///   · 普通 Setter.Value    → 不抛，值静默不生效（视觉 bug）
    /// 这些编译期全绿、静态扫键也看不出（键在全仓确实存在），只有运行期真正实例化才会现形。
    ///
    /// 做法：按 App.xaml 的顺序合并真实字典 + 复刻 App.xaml 的内联资源（Icon / 模板 / 链接按钮样式）
    /// → 逐键解析 → 落地全部 Dialog* 样式与模板 → 实例化 + 布局真弹窗视图（走 BAML + ApplyTemplate 全路径）。
    /// 修复前必红，修复后应 ALL_OK。
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main()
        {
            if (Application.Current == null)
                new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

            // 探针里没有 Prism 容器：不加这一句，new 视图会在 AutoWireViewModel 处炸
            // （与 UIThemeSmokeTest / LogViewProbe 同一做法）。
            Prism.Mvvm.ViewModelLocationProvider.SetDefaultViewModelFactory(_ => null);

            int fail = 0;

            fail += AddAppInlineResources();
            fail += TryMerge("Core.Halcon Generic", "pack://application:,,,/Core.Halcon;component/Generic.xaml");
            fail += TryMerge("UI Generic", "pack://application:,,,/UI;component/Themes/Generic.xaml");
            fail += TryMerge(".NET9 Fluent", "pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml");

            Console.WriteLine();
            fail += ProbeKeys();
            Console.WriteLine();
            fail += SweepTemplates();
            Console.WriteLine();
            fail += ApplyDialogStyles();
            Console.WriteLine();
            fail += InstantiateViews();
            Console.WriteLine();
            fail += ProbeGroupedLogConsole();

            Console.WriteLine();
            Console.WriteLine(fail == 0 ? "RESULT: ALL_OK" : $"RESULT: FAIL count={fail}");
            return fail == 0 ? 0 : 1;
        }

        // ---------------------------------------------------------------- 引导

        private static int AddAppInlineResources()
        {
            try
            {
                var appXaml = Path.Combine(RepoRoot(), "VisionMaster", "App.xaml");
                var text = File.ReadAllText(appXaml);
                const string openTag = "<prism:PrismApplication.Resources>";
                const string closeTag = "</prism:PrismApplication.Resources>";
                int s = text.IndexOf(openTag, StringComparison.Ordinal);
                int e = text.IndexOf(closeTag, StringComparison.Ordinal);
                if (s < 0 || e < 0) throw new InvalidOperationException("App.xaml 里找不到 PrismApplication.Resources");

                var inner = text.Substring(s + openTag.Length, e - s - openTag.Length);
                // 探针自己按 App.xaml 顺序合并字典，这里只取内联资源（去掉 MergedDictionaries 段）
                inner = Regex.Replace(inner, "<ResourceDictionary.MergedDictionaries>.*?</ResourceDictionary.MergedDictionaries>", "", RegexOptions.Singleline);
                int bodyStart = inner.IndexOf('>', inner.IndexOf("<ResourceDictionary", StringComparison.Ordinal)) + 1;
                int bodyEnd = inner.LastIndexOf("</ResourceDictionary>", StringComparison.Ordinal);
                var body = inner.Substring(bodyStart, bodyEnd - bodyStart);

                var wrapper =
                    "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"" +
                    " xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"" +
                    " xmlns:prism=\"http://prismlibrary.com/\">" + body + "</ResourceDictionary>";
                var dict = (ResourceDictionary)XamlReader.Parse(wrapper);
                // App.xaml 里内联资源与 MergedDictionaries 同处一个字典；这里等价地放在合并链最前
                Application.Current.Resources.MergedDictionaries.Insert(0, dict);
                Console.WriteLine($"APP-INLINE              -> OK ({dict.Keys.Count} keys: {string.Join(", ", dict.Keys.Cast<object>())})");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"APP-INLINE              -> THROW {Describe(ex)}");
                return 1;
            }
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("定位不到仓库根（AGENTS.md）");
        }

        private static int TryMerge(string tag, string uri)
        {
            try
            {
                Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(uri) });
                Console.WriteLine($"MERGE {tag,-20} -> OK");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"MERGE {tag,-20} -> THROW {Describe(ex)}");
                return 1;
            }
        }

        // ---------------------------------------------------------------- 逐键解析

        private static int ProbeKeys()
        {
            int fail = 0;
            // DialogComboInGroup 是本次崩溃的现场（跨分册 BasedOn 的消费侧）
            string[] keys =
            {
                "DialogCombo", "DialogComboItem", "DialogComboInGroup",
                "DialogInput", "DialogInputInGroup", "DialogIconFont",
                "DialogDisplayNameTemplate", "DialogSourceNodeTemplate",
                "DialogConnectionOptionTemplate", "DialogDataGrid",
                "Icon", "ExpandToggleButtonTemplate", "LinkButtonStyle",
            };
            foreach (var k in keys) fail += ProbeKey(k, () => Application.Current.TryFindResource(k));
            fail += ProbeKey(typeof(Button), () => Application.Current.TryFindResource(typeof(Button)));
            fail += ProbeKey(typeof(ComboBox), () => Application.Current.TryFindResource(typeof(ComboBox)));
            return fail;
        }

        private static int ProbeKey(object key, Func<object> lookup)
        {
            try
            {
                var v = lookup();
                Console.WriteLine($"KEY {KeyText(key),-30} -> {(v == null ? "NOT FOUND (null)" : v.GetType().Name)}");
                return v == null ? 1 : 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"KEY {KeyText(key),-30} -> THROW {Describe(ex)}");
                return 1;
            }
        }

        // ------------------------------------------------------- 模板 / 样式落地

        /// <summary>
        /// 把合并链里每个分册的 DataTemplate / ControlTemplate 全部 LoadContent 一次 ——
        /// 等价实机 ApplyTemplate 那一刻，是断链主要的抛点。
        /// </summary>
        private static int SweepTemplates()
        {
            int fail = 0, total = 0;
            foreach (var (dict, source) in EnumerateDictionaries())
            {
                var keys = GetKeys(dict);
                foreach (var key in keys)
                {
                    object value;
                    try { value = dict[key]; }
                    catch (Exception ex)
                    {
                        Report(source, key, "DICT-ENTRY", ex);
                        fail++;
                        continue;
                    }

                    if (value is DataTemplate || value is ControlTemplate)
                    {
                        total++;
                        try
                        {
                            ((FrameworkTemplate)value).LoadContent();
                        }
                        catch (Exception ex)
                        {
                            Report(source, key, value is DataTemplate ? "DATA-TEMPLATE" : "CTRL-TEMPLATE", ex);
                            fail++;
                        }
                    }
                }
            }
            Console.WriteLine($"TEMPLATE sweep          -> {total} templates loaded, {fail} failed");
            return fail;
        }

        /// <summary>
        /// Dialog* 样式（外加两支历史遗留的图标按钮样式）逐支套到真元素上并 Measure 一次：
        /// 把「Setter.Value 静默失效」与「样式内模板断链」两路都逼出来，
        /// 同时汇报字体令牌是否真的解析到了图标字体。
        /// </summary>
        private static int ApplyDialogStyles()
        {
            // 图标字体令牌的三条消费链：Dialog*（分册） + 两支 IconButon/IconToggleButon（BtnExtension）
            string[] extraKeys = { "IconButon", "IconToggleButon" };
            bool Interesting(string key) => key.StartsWith("Dialog", StringComparison.Ordinal) || extraKeys.Contains(key);

            int fail = 0, total = 0;
            foreach (var (dict, source) in EnumerateDictionaries())
            {
                foreach (var key in GetKeys(dict).OfType<string>().Where(Interesting))
                {
                    object value;
                    try { value = dict[key]; }
                    catch (Exception ex)
                    {
                        Report(source, key, "DICT-ENTRY", ex);
                        fail++;
                        continue;
                    }
                    if (!(value is Style style)) continue;
                    var target = style.TargetType;
                    if (target == null || target.IsAbstract || !typeof(FrameworkElement).IsAssignableFrom(target)) continue;
                    if (typeof(Window).IsAssignableFrom(target)) continue;

                    total++;
                    try
                    {
                        var element = (FrameworkElement)Activator.CreateInstance(target);
                        element.Style = style;
                        element.Measure(new Size(320, 200));
                        element.Arrange(new Rect(0, 0, 320, 200));

                        // 只报「自己声明了 FontFamily」的样式：图标字体令牌断链的症状正是这里拿到系统默认字体，
                        // 其余样式没声明字体属正常，一律不打印（否则一屏噪音，真信号看不见）。
                        bool declaresFont = style.Setters.OfType<Setter>().Any(s =>
                            s.Property == Control.FontFamilyProperty || s.Property == TextBlock.FontFamilyProperty);
                        if (!declaresFont) continue;
                        var fam = element switch
                        {
                            TextBlock tb => tb.FontFamily?.Source,
                            Control c => c.FontFamily?.Source,
                            _ => null,
                        };
                        if (fam == null) continue;
                        var ok = fam.Contains("Font Awesome", StringComparison.OrdinalIgnoreCase);
                        Console.WriteLine($"STYLE {key,-28} -> {target.Name,-14} FontFamily={fam}{(ok ? "  [icon-font OK]" : "  [NOT the icon font]")}");
                    }
                    catch (Exception ex)
                    {
                        Report(source, key, "STYLE", ex);
                        fail++;
                    }
                }
            }
            Console.WriteLine($"STYLE sweep             -> {total} Dialog* styles applied, {fail} failed");
            return fail;
        }

        // ---------------------------------------------------------------- 真视图

        private static int InstantiateViews()
        {
            int fail = 0;
            fail += TryView("变量管理 GlobalVariableView", () => new VisionMaster.Views.DialogViews.GlobalVariableView());
            fail += TryView("连接管理 CommunicationSettingsView", () => new VisionMaster.Views.DialogViews.CommunicationSettingsView());
            fail += TryView("相机设置 CameraSettingsView", () => new VisionMaster.Views.DialogViews.CameraSettingsView());
            return fail;
        }

        /// <summary>
        /// 日志控制台「分组」路径：分组表头模板是 LogConsole.xaml 里 <c>Style.Resources</c> 下的
        /// 嵌套模板，前面那次模板散扫（只遍历字典顶层条目）覆盖不到 —— 这里真开 GroupBy + 布局，
        /// 把它逼到 ApplyTemplate（该模板里引用 AccentBrush，2026-10-10 由静态断言补上就地 merge）。
        /// </summary>
        private static int ProbeGroupedLogConsole()
        {
            try
            {
                var items = new System.Collections.ObjectModel.ObservableCollection<UI.Models.LogItem>
                {
                    new(UI.Models.LogLevel.Info, "分组路径样例 1"),
                    new(UI.Models.LogLevel.Info, "分组路径样例 2"),
                };
                var console = new UI.CustomControl.LogConsole { GroupBy = "Source" };
                console.ItemsSource = items;
                console.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                console.Measure(new Size(600, 320));
                console.Arrange(new Rect(0, 0, 600, 320));
                console.UpdateLayout();
                var groups = System.Windows.Data.CollectionViewSource.GetDefaultView(items).GroupDescriptions.Count;
                Console.WriteLine($"LOGCONSOLE grouped      -> OK (GroupDescriptions={groups})");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"LOGCONSOLE grouped      -> THROW {Describe(ex)}");
                return 1;
            }
        }

        private static int TryView(string tag, Func<FrameworkElement> factory)
        {
            try
            {
                var view = factory();
                // 实例化只走 BAML 加载；模板与样式在 Measure/Arrange 时才落地 —— 与实机一致
                view.Measure(new Size(1400, 900));
                view.Arrange(new Rect(0, 0, 1400, 900));
                view.UpdateLayout();
                Console.WriteLine($"VIEW {tag,-34} -> CREATED+LAID OUT");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"VIEW {tag,-34} -> THROW {Describe(ex)}");
                return 1;
            }
        }

        // ---------------------------------------------------------------- 工具

        private static IEnumerable<(ResourceDictionary Dict, string Source)> EnumerateDictionaries()
        {
            var seen = new HashSet<ResourceDictionary>();
            var queue = new Queue<(ResourceDictionary, string)>();
            queue.Enqueue((Application.Current.Resources, "App.xaml"));
            while (queue.Count > 0)
            {
                var (dict, source) = queue.Dequeue();
                if (!seen.Add(dict)) continue;
                yield return (dict, source);
                foreach (var child in dict.MergedDictionaries)
                    queue.Enqueue((child, child.Source?.OriginalString ?? source));
            }
        }

        /// <summary>取键列表（迭代 Keys 会把分册的延迟内容整体物化 —— 本身也是一次断链体检）。</summary>
        private static List<object> GetKeys(ResourceDictionary dict)
        {
            var keys = new List<object>();
            foreach (var k in dict.Keys) keys.Add(k);
            return keys;
        }

        private static void Report(string source, object key, string stage, Exception ex)
        {
            var src = source ?? "(inline)";
            int slash = src.LastIndexOf('/');
            if (slash >= 0) src = src.Substring(slash + 1);
            Console.WriteLine($"{stage} {src} :: {KeyText(key)} -> THROW {Describe(ex)}");
        }

        private static string KeyText(object key) => key is Type t ? "{x:Type " + t.Name + "}" : key.ToString();

        private static string Describe(Exception ex)
        {
            var sb = new StringBuilder();
            var e = ex;
            for (int depth = 0; e != null && depth < 4; depth++)
            {
                sb.Append(depth == 0 ? "" : "  <-- inner: ");
                sb.Append(e.GetType().Name).Append(": ").Append(e.Message);
                if (e is XamlParseException xpe)
                    sb.Append($" [line={xpe.LineNumber} pos={xpe.LinePosition} uri={xpe.BaseUri}]");
                e = e.InnerException;
            }
            return sb.ToString();
        }
    }
}
