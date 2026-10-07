using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Core.Interfaces;
using Prism.Dialogs;
using VisionMaster.Services;
using VisionMaster.Services.Help;
using VisionMaster.ViewModels.DialogViewModels;
using VisionMaster.Views.DialogViews;

namespace HelpProbe
{
    /// <summary>
    /// 帮助系统离屏探针：走**真实**链路（PluginService 扫 Modules\ → HelpCatalogService 合并 →
    /// 真 HelpView + HelpViewModel），断言合并结果与渲染通不通，并把帮助窗渲染成 PNG 供人工看效果。
    ///
    /// 用法：HelpProbe.exe [输出目录]     默认输出到 &lt;仓库根&gt;\_help_preview\
    /// </summary>
    internal static class Program
    {
        private static int _failures;

        [STAThread]
        private static int Main(string[] args)
        {
            var repoRoot = FindRepoRoot();
            var outputDir = args.Length > 0 ? args[0] : Path.Combine(repoRoot, "_help_preview");
            Directory.CreateDirectory(outputDir);

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            MergeAppResources(app, repoRoot);
            // 无 Prism 容器：视图上的 AutoWireViewModel 走"返回 null"，我们手工塞 DataContext
            Prism.Mvvm.ViewModelLocationProvider.SetDefaultViewModelFactory(_ => null);

            var log = new ConsoleLog();
            var notifier = new ConsoleNotifier();

            // ---- 1) 插件扫描（与启动自检链同一条路；DEBUG 下 Modules\ 在仓库根）----
            var provider = new PluginProvider(notifier);
            new PluginService(provider, notifier).InitPlugins();
            Console.WriteLine($"[probe] 插件表：模块 {provider.ModulePlugins.Count} / 相机 {provider.CameraPlugins.Count} / 运动 {provider.MotionPlugins.Count}");

            // ---- 2) 目录合并 ----
            var catalog = new HelpCatalogService(provider, log);
            catalog.Build();
            Console.WriteLine("[probe] " + catalog.Summary);
            Dump(catalog.Groups, 0);

            Check("宿主自带手册 4 页", catalog.HostPageCount == 4, $"实际 {catalog.HostPageCount}");
            Check("插件总数 > 20", catalog.PluginTotal > 20, $"实际 {catalog.PluginTotal}");
            Check("内置节点单独计数（If/While/For…）", catalog.BuiltInCount >= 5, $"实际 {catalog.BuiltInCount}");
            Check("至少两个插件自带手册", catalog.PluginManualCount >= 2, $"实际 {catalog.PluginManualCount}");

            var bead = FindTopic(catalog.Groups, "胶路检测");
            var matching = FindTopic(catalog.Groups, "模板匹配");
            var autoNode = FindTopic(catalog.Groups, "图像采集");
            Check("插件手册：胶路检测 2 页", bead?.Children.Count == 2, $"实际 {bead?.Children.Count}");
            Check("插件手册：模板匹配 1 页", matching?.Children.Count == 1, $"实际 {matching?.Children.Count}");
            Check("插件手册页不再带「自动生成」角标", bead?.Badge == null && matching?.Badge == null, $"{bead?.Badge}/{matching?.Badge}");
            Check("未写手册的插件带「自动生成」角标", autoNode?.Badge == "自动生成", $"实际 {autoNode?.Badge}");
            Check("分组齐全（缺陷检测 / 定位 都在）",
                FindGroup(catalog.Groups, "缺陷检测") != null && FindGroup(catalog.Groups, "定位") != null, "");

            // ---- 3) 全部页面都要能渲染（渲染器回归闸）----
            var pages = AllPages(catalog.Groups).ToList();
            var empty = new List<string>();
            foreach (var page in pages)
            {
                try
                {
                    var doc = MarkdownToFlowDocument.Render(page.LoadMarkdown(), page.LoadImage);
                    if (doc.Blocks.Count == 0)
                        empty.Add(page.Title);
                }
                catch (Exception ex)
                {
                    _failures++;
                    Console.WriteLine($"   [渲染异常] {page.Title}: {ex.Message}");
                }
            }
            Console.WriteLine($"[probe] 共渲染 {pages.Count} 页");
            Check($"全部 {pages.Count} 页都渲染出内容", empty.Count == 0,
                empty.Count == 0 ? "" : "空页：" + string.Join("、", empty.Take(5)));

            // ---- 4) 装配视图模型（真弹窗走的就是这个构造函数 + OnDialogOpened）----
            var vm = new HelpViewModel(catalog, log);
            vm.OnDialogOpened(new DialogParameters());

            // ---- 5) 回退到"已看过的页"：必须命中缓存并回到该页正文 ----
            // （这条路径曾经有缺陷：正文缓存在本地字段里、只在首次渲染时赋值，
            //   于是"看 A → 看 B → 回退到 A → 打印"会打出 B 的正文。修复后正文与文档同源同缓存。）
            if (bead != null && bead.Children.Count >= 2)
            {
                var pageA = bead.Children[0];
                var pageB = bead.Children[1];
                vm.SelectedNode = pageA;
                var documentA = vm.Document;
                vm.SelectedNode = pageB;
                var documentB = vm.Document;
                vm.SelectedNode = pageA;   // 第二次进 A：走缓存分支
                Check("回退到已看过的页：正文回到该页（缓存命中）",
                    ReferenceEquals(vm.Document, documentA) && !ReferenceEquals(documentA, documentB),
                    $"docA={documentA?.Blocks.Count} 块 / docB={documentB?.Blocks.Count} 块");
            }

            // ---- 6) 真视图渲染成 PNG ----
            var view = new HelpView { DataContext = vm };

            Expand(catalog.Groups, "软件手册");
            if (bead != null)
            {
                Expand(catalog.Groups, "缺陷检测");
                vm.SelectedNode = bead.Children[0];
            }
            Render(view, Path.Combine(outputDir, "01-插件手册页.png"));

            if (autoNode != null)
            {
                Expand(catalog.Groups, "常用工具");
                vm.SelectedNode = autoNode.Children[0];
            }
            Render(view, Path.Combine(outputDir, "02-自动生成页.png"));

            var hostGroup = FindGroup(catalog.Groups, "软件手册");
            if (hostGroup is { Children.Count: > 0 })
                vm.SelectedNode = hostGroup.Children[0];
            vm.ToggleTocCommand.Execute();
            Render(view, Path.Combine(outputDir, "03-隐藏目录.png"));

            Console.WriteLine(_failures == 0
                ? "=== HelpProbe: 全部通过 ==="
                : $"### HelpProbe: {_failures} 项失败");
            return _failures == 0 ? 0 : 1;
        }

        // ==================================================================
        //  断言与输出
        // ==================================================================

        private static void Check(string name, bool ok, string detail)
        {
            Console.WriteLine((ok ? "  [PASS] " : "  [FAIL] ") + name + (detail.Length > 0 ? $"（{detail}）" : ""));
            if (!ok)
                _failures++;
        }

        private static void Dump(IEnumerable<HelpNode> nodes, int depth)
        {
            foreach (var node in nodes)
            {
                Console.WriteLine(new string(' ', depth * 2)
                    + "- " + node.Title
                    + (node.Badge != null ? $" [{node.Badge}]" : "")
                    + (node.Page != null ? "  ·页" : ""));
                Dump(node.Children, depth + 1);
            }
        }

        private static IEnumerable<HelpPage> AllPages(IEnumerable<HelpNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.Page != null)
                    yield return node.Page;
                foreach (var page in AllPages(node.Children))
                    yield return page;
            }
        }

        private static HelpNode FindGroup(IEnumerable<HelpNode> groups, string title) =>
            groups.FirstOrDefault(g => g.Title == title);

        /// <summary>按标题找"插件主题节点"（分组下的第一层）</summary>
        private static HelpNode FindTopic(IEnumerable<HelpNode> groups, string title)
        {
            foreach (var group in groups)
            {
                foreach (var topic in group.Children)
                {
                    if (topic.Title == title)
                        return topic;
                }
            }
            return null;
        }

        private static void Expand(IEnumerable<HelpNode> groups, string title)
        {
            var node = FindGroup(groups, title);
            if (node != null)
                node.IsExpanded = true;
        }

        // ==================================================================
        //  渲染
        // ==================================================================

        private static void Render(FrameworkElement view, string path)
        {
            const int width = 1120;
            const int height = 740;

            var host = new Border
            {
                Width = width,
                Height = height,
                Background = Brushes.White,
                Child = view,
            };
            host.Measure(new Size(width, height));
            host.Arrange(new Rect(0, 0, width, height));
            host.UpdateLayout();
            host.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            host.UpdateLayout();

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(host);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path))
                encoder.Save(stream);
            Console.WriteLine("[probe] 截图：" + path);

            // 把字号下拉的选中态打出来：ComboBox 的初始化时序是这类"看着有值其实没生效"问题的高发区
            var combo = FindFirst<ComboBox>(host);
            if (combo != null)
                Console.WriteLine($"[probe] 字号下拉：项数 {combo.Items.Count}，SelectedItem={combo.SelectedItem ?? "(null)"}，SelectedIndex={combo.SelectedIndex}");

            // 摘下来：同一个视图要连着渲染多张（换页/换状态），不放回自由身下次 Add 会抛"已是逻辑子元素"
            host.Child = null;
        }

        private static T FindFirst<T>(DependencyObject root) where T : DependencyObject
        {
            if (root is T hit)
                return hit;
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var found = FindFirst<T>(VisualTreeHelper.GetChild(root, i));
                if (found != null)
                    return found;
            }
            return null;
        }

        // ==================================================================
        //  环境
        // ==================================================================

        private static string FindRepoRoot()
        {
            // <仓库根>\HelpProbe\bin\<Cfg>\net9.0-windows\ → 上四级
            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\"));
        }

        /// <summary>按 App.xaml 的方式合并全局资源（照抄 UIThemeSmokeTest 的做法）</summary>
        private static void MergeAppResources(Application app, string repoRoot)
        {
            var merged = app.Resources.MergedDictionaries;
            merged.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Core.Halcon;component/Generic.xaml") });
            merged.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/UI;component/Themes/Generic.xaml") });
            merged.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml") });

            var appXaml = File.ReadAllText(Path.Combine(repoRoot, @"VisionMaster\App.xaml"));
            var match = Regex.Match(appXaml, @"<ResourceDictionary>(?s:.)*</ResourceDictionary>\s*</prism:PrismApplication\.Resources>");
            if (match.Success)
            {
                var inline = match.Value.Substring(0, match.Value.LastIndexOf("</prism:PrismApplication.Resources>"));
                inline = inline.Replace(
                    "<ResourceDictionary>",
                    "<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                    "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">");
                var dict = (ResourceDictionary)XamlReader.Parse(inline);
                dict.MergedDictionaries.Clear();   // 正则把 App.xaml 的合并块也吃进来了，剥掉（否则 Fluent 会合并两遍）
                merged.Add(dict);
            }

            // CreateShell 里做的那一步：图标字体换名成 Icon（目录树/工具栏都引用它）
            if (app.Resources.Contains("FA.Light"))
                app.Resources["Icon"] = app.Resources["FA.Light"];
        }

        private sealed class ConsoleLog : ILogService
        {
            public void Success(params string[] messages) => Write("OK ", messages);
            public void Error(params Exception[] messages) => Write("ERR", messages.Select(m => m.Message));
            public void Error(params string[] messages) => Write("ERR", messages);
            public void Info(params string[] messages) => Write("INF", messages);
            public void Warn(params string[] messages) => Write("WRN", messages);

            private static void Write(string level, IEnumerable<string> messages)
            {
                foreach (var message in messages)
                    Console.WriteLine($"[{level}] {message}");
            }
        }

        private sealed class ConsoleNotifier : IUserNotifier
        {
            public void ShowInfo(string message) => Console.WriteLine("[通知] " + message);
            public void ShowWarn(string message) => Console.WriteLine("[警告] " + message);
            public void ShowError(string message) => Console.WriteLine("[错误] " + message);
        }
    }
}
