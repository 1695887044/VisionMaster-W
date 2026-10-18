using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Core.Interfaces;
using Prism.Dialogs;
using VisionMaster.Models;
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

        /// <summary>探针专用"无手册插件"的显示名——目录里靠它找回自动生成页那一条路</summary>
        private const string NoManualName = "探针无手册模块";

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
            new PluginService(provider, notifier, log).InitPlugins();
            Console.WriteLine($"[probe] 插件表：模块 {provider.ModulePlugins.Count} / 相机 {provider.CameraPlugins.Count} / 运动 {provider.MotionPlugins.Count}");

            // 「自动生成页」这条路在真机目录里已经没有住户（42/42 插件都自带手册），
            // 所以塞一个"故意不写手册"的探针插件进来，让角标 / 来源档位 / 自动生成页截图三条断言有的可断。
            provider.RegisterModule(new ToolItemModel
            {
                Name = NoManualName,
                Category = "常用工具",
                Description = "帮助探针专用模块：不提供手册，用来走「自动生成页」这条路。",
                Icon = "\uf1b2",
                ModuleTypeName = typeof(NoManualProbe).AssemblyQualifiedName,
            });

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
            var autoNode = FindTopic(catalog.Groups, NoManualName);
            // 页数不写死：手册按 L/M/S 档位铺开后这两册都涨到 8 页，写死数字只会让下次扩册必红。
            // 这两条原本要证的其实是"收窄生效、各插件只看到自己的页"——用"成册 + 标题零交集"来证。
            Check("插件手册：胶路检测成册（≥2 页）", bead?.Children.Count >= 2, $"实际 {bead?.Children.Count}");
            Check("插件手册：模板匹配成册（≥2 页）", matching?.Children.Count >= 2, $"实际 {matching?.Children.Count}");
            var crossedTitles = bead == null || matching == null
                ? new List<string>()
                : bead.Children.Select(t => t.Title).Intersect(matching.Children.Select(t => t.Title)).ToList();
            Check("插件手册：两册标题零交集（NarrowToPlugin 没串页）",
                bead != null && matching != null && crossedTitles.Count == 0,
                bead == null || matching == null
                    ? "目录里找不到其中一册"
                    : "共有标题：" + string.Join("、", crossedTitles));
            Check("插件手册页不再带「自动生成」角标", bead?.Badge == null && matching?.Badge == null, $"{bead?.Badge}/{matching?.Badge}");
            Check("未写手册的插件带「自动生成」角标", autoNode?.Badge == "自动生成", $"实际 {autoNode?.Badge}");
            Check("分组齐全（缺陷检测 / 定位 都在）",
                FindGroup(catalog.Groups, "缺陷检测") != null && FindGroup(catalog.Groups, "定位") != null, "");

            // ---- 3) 全部页面都要能渲染（渲染器回归闸）----
            var pages = AllPages(catalog.Groups).ToList();
            var empty = new List<string>();
            var starLeak = new List<string>();
            var codeRunes = new HashSet<Rune>();
            FontFamily codeFont = null;
            foreach (var page in pages)
            {
                try
                {
                    var md = page.LoadMarkdown();
                    var doc = MarkdownToFlowDocument.Render(md, page.LoadImage);
                    if (doc.Blocks.Count == 0)
                        empty.Add(page.Title);
                    // 没闭合的加粗在屏幕上就是两个字面星号——渲染级对照，比数原文的奇偶可靠
                    // （原文里 `Help\**\*` 这类通配本来就该原样显示，所以额度只从代码上下文取）。
                    var renderedText = new TextRange(doc.ContentStart, doc.ContentEnd).Text;
                    var leaked = CountLiteral(renderedText, "**") - CountLiteralInsideCode(md);
                    if (leaked > 0)
                        starLeak.Add($"{page.Title}（多 {leaked} 处，上下文：{FirstStarContexts(renderedText, 2)}）");
                    // 代码块字体族从渲染结果里认，不写死字体名（生产换字体时这条闸跟着走）
                    foreach (var r in CodeContextRunes(md)) codeRunes.Add(r);
                    codeFont ??= FindCodeFont(doc);
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
            Check("渲染后的正文里没有未闭合的加粗（不许出现字面 **）", starLeak.Count == 0,
                string.Join("；", starLeak.Take(6)));
            // ---- 3b) 代码块里的每个字符都得有字形 ----
            //   代码块用的是等宽族（正文用中文字体族），①②③、箭头、制表符这类"中文能显示、
            //   等宽不一定能显示"的字符落在代码块里就是豆腐块；正文里同样的字符没事。
            var tofu = new List<string>();
            if (codeFont != null)
                foreach (var r in codeRunes.Where(r => RendersAsMissingGlyph(r, codeFont)))
                    tofu.Add($"「{r}」U+{r.Value:X4}");
            Check($"代码块字体族（{codeFont?.Source}）显示得了全部 {codeRunes.Count} 个代码上下文字符",
                codeFont != null && tofu.Count == 0,
                codeFont == null ? "没在渲染结果里找到代码块段落（判据失效，须查）" : "豆腐块：" + string.Join(" ", tofu));

            // ---- 3c) 帮助弹窗的窗口图标（Alt-Tab / 任务栏那一枚，不是窗口内标题栏的蓝方块）----
            //   用与 HelpView.xaml 那条 Setter **逐字相同**的 URI 解一遍：Setter 的字符串是
            //   "样式应用到窗口时"才转换的，写错了不会在编译期红，而是"弹窗打不开"，
            //   所以这里必须提前把它跑一遍（资源没打进包 / 程序集名写错 / 文件名少档，都在这关红）。
            //   ⚠ 这一关**拦不住大小写**：WPF 的资源名查找不区分大小写（实测把 help.ico 写成
            //   helpX.ico 报的是「找不到资源"resources/icons/helpx.ico"」——报错里已经全小写）。
            const string helpIconUri = "pack://application:,,,/VisionMaster;component/Resources/Icons/help.ico";
            List<string> iconSizes;
            try
            {
                iconSizes = BitmapDecoder.Create(new Uri(helpIconUri, UriKind.Absolute),
                        BitmapCreateOptions.DelayCreation, BitmapCacheOption.None)
                    .Frames.Select(f => $"{f.PixelWidth}x{f.PixelHeight}").ToList();
            }
            catch (Exception ex)
            {
                iconSizes = new List<string> { "解不开：" + ex.Message };
            }
            Check("帮助弹窗的窗口图标在包里且有 16px 那一档（Alt-Tab 用最小档，缩出来会糊）",
                iconSizes.Count >= 6 && iconSizes.Contains("16x16"),
                $"{iconSizes.Count} 档：{string.Join(" ", iconSizes)}");

            // ---- 4) 装配视图模型（真弹窗走的就是这个构造函数 + OnDialogOpened）----
            var vm = new HelpViewModel(catalog, log);
            vm.OnDialogOpened(new DialogParameters());

            // ---- 4b) 读者分层：加载永远全量，分家只发生在"看得见"这一层 ----
            //   这一段是显隐链路的运行期对照（静态那四条在 HelpManualCheck HM14）：
            //   断的是"关掉开关时用户页一个不许少、开发页一个不许多"——
            //   当年用 Help\{类名}\dev\ 子目录方案的失败模式正是"用户页被静默挤掉"，所以这条必须可反证。
            var devNodesAll = Flatten(catalog.Groups).Where(n => n.IsDevDoc).ToList();
            Check($"目录全量收集到开发页（收 {devNodesAll.Count} 页，均带「开发」角标）",
                devNodesAll.Count >= 3 && devNodesAll.All(n => n.Badge == HelpCatalogService.DeveloperPageBadge),
                $"{devNodesAll.Count} 页，角标：{string.Join("/", devNodesAll.Select(n => n.Badge).Distinct())}");
            Check("宿主台账 DeveloperPageCount 与目录数一致（台账不受开关影响）",
                catalog.DeveloperPageCount == devNodesAll.Count,
                $"台账 {catalog.DeveloperPageCount} / 实际 {devNodesAll.Count}");

            // 反证（R38）：绕开宿主的取名逻辑，自己从内嵌资源清单数一遍 dev- 页。
            //   MSBuild 把目录分隔符换成点，分册排版下资源名是 …Help.DelayPlugin.10-dev-architecture.md，
            //   谁要是拿 Path.GetFileNameWithoutExtension 当文件名，"DelayPlugin." 就粘在前面、
            //   dev- 标记认不出来 → 开发页被当成用户页，关着开关也上屏（2026-10-16 实测 14 页）。
            //   这条对照红了就说明"标记为开发页"的判据与资源清单脱钩，别改这条的期望数。
            var devFromManifest = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetManifestResourceNames(); }
                    catch { return Array.Empty<string>(); }
                })
                .Where(n => n.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                .Select(ManifestPageName)
                .Where(n => n.Length >= 6
                            && char.IsAsciiDigit(n[0]) && char.IsAsciiDigit(n[1]) && n[2] == '-'
                            && n.Substring(3).StartsWith(HelpCatalogService.DeveloperPageMark, StringComparison.OrdinalIgnoreCase))
                .ToList();
            Check($"开发页标记与资源清单对齐（清单数到 {devFromManifest.Count} 页）",
                devNodesAll.Count == devFromManifest.Count && devFromManifest.Count >= 3,
                $"目录标记 {devNodesAll.Count} / 资源清单 {devFromManifest.Count}（差 {Math.Abs(devNodesAll.Count - devFromManifest.Count)} 页，多插件程序集最容易掉）");

            var roiTopicName = "ROI";
            var roiInCatalog = FindTopic(catalog.Groups, roiTopicName);
            Check($"ROI 插件节点在目录里（{roiInCatalog?.Children.Count} 个子节点）",
                roiInCatalog != null && roiInCatalog.Children.Count >= 8,
                roiInCatalog == null
                    ? "没找到，分组内标题：" + string.Join("、", catalog.Groups.SelectMany(g => g.Children).Select(t => t.Title).Take(40))
                    : $"实际 {roiInCatalog.Children.Count} 页");

            // 默认（settings=null → 开关关）：可见树里不许有开发页，用户页一页不许少
            var visibleDev = Flatten(vm.Nodes).Where(n => n.IsDevDoc).ToList();
            var roiHidden = FindTopic(vm.Nodes, roiTopicName);
            var userPagesHidden = roiHidden?.Children.Count(n => !n.IsDevDoc) ?? -1;
            Check("开关关着：可见树里零开发页（裁树真的生效，不是只藏角标）",
                visibleDev.Count == 0, $"实际残留 {visibleDev.Count} 个：" + string.Join("、", visibleDev.Select(n => n.Title).Take(5)));
            Check("开关关着：ROI 用户页一页没被挤掉（窄化/裁剪的静默失败模式）",
                userPagesHidden >= 5, $"可见 {userPagesHidden} 页");

            // 打开开关：开发页出现，且原节点直接复用（不克隆 → 展开状态与引用都不变）
            vm.ShowDevDocs = true;
            var visibleDevOn = Flatten(vm.Nodes).Where(n => n.IsDevDoc).ToList();
            var roiShown = FindTopic(vm.Nodes, roiTopicName);
            Check("打开开关：开发页出现在目录里（数量与台账一致）",
                visibleDevOn.Count == catalog.DeveloperPageCount,
                $"可见 {visibleDevOn.Count} / 台账 {catalog.DeveloperPageCount}");
            Check("打开开关：ROI 用户页 + 开发页全都在（全量收集不受开关影响）",
                roiShown != null && roiShown.Children.Count == roiInCatalog?.Children.Count,
                roiShown == null ? "找不到 ROI" : $"{roiShown.Children.Count} 页 vs 原树 {roiInCatalog?.Children.Count} 页");
            Check("打开开关：可见树用原节点引用（不克隆，否则用户手工展开的分组会被重置）",
                vm.Nodes.Count == catalog.Groups.Count && ReferenceEquals(vm.Nodes[0], catalog.Groups[0]),
                $"节点数 {vm.Nodes.Count}/{catalog.Groups.Count}");

            // 反证（R38）：切回去要还原成关着的样子，且幂等
            vm.ShowDevDocs = false;
            Check("切回关闭：开发页再次消失（开关不是单向门）",
                Flatten(vm.Nodes).Any(n => n.IsDevDoc) == false &&
                (FindTopic(vm.Nodes, roiTopicName)?.Children.Count ?? -1) == userPagesHidden,
                $"用户页 {FindTopic(vm.Nodes, roiTopicName)?.Children.Count}");

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

            // ---- 6) 网页版式的正文度量（行距/留白/定宽都落在渲染产物上，不只看 XAML）----
            var probeDoc = MarkdownToFlowDocument.Render("# 标题\n\n正文一行。", null, 14);
            Check("正文行距按网页口径 1.8×（14 → 25）",
                Math.Abs(probeDoc.LineHeight - 25) < 0.01, $"实际 {probeDoc.LineHeight}");
            Check("正文左右留白 28",
                Math.Abs(probeDoc.PagePadding.Left - MarkdownToFlowDocument.HorizontalPadding) < 0.01
                && Math.Abs(probeDoc.PagePadding.Right - MarkdownToFlowDocument.HorizontalPadding) < 0.01,
                $"实际 {probeDoc.PagePadding.Left}/{probeDoc.PagePadding.Right}");
            Check("正文容器最大宽度 = 定宽 780 + 两侧留白 = 836",
                Math.Abs(MarkdownToFlowDocument.ViewerMaxWidth - 836) < 0.01,
                $"实际 {MarkdownToFlowDocument.ViewerMaxWidth}");

            // ---- 7) 来源档位：页眉徽章三档的判据是枚举，不是 Source 显示名 ----
            var hostNode = FindGroup(catalog.Groups, HelpCatalogService.HostGroupTitle);
            Check("宿主页档位 = Host",
                hostNode is { Children.Count: > 0 } && hostNode.Children[0].Page?.Kind == HelpSourceKind.Host,
                $"实际 {DescribeKind(hostNode)}");
            Check("插件自带页档位 = Plugin",
                bead is { Children.Count: > 0 } && bead.Children[0].Page?.Kind == HelpSourceKind.Plugin,
                $"实际 {DescribeKind(bead)}");
            Check("自动生成页档位 = Auto",
                autoNode is { Children.Count: > 0 } && autoNode.Children[0].Page?.Kind == HelpSourceKind.Auto,
                $"实际 {DescribeKind(autoNode)}");
            if (autoNode is { Children.Count: > 0 } && bead is { Children.Count: > 0 })
            {
                vm.SelectedNode = bead.Children[0];
                Check("页眉档位跟着正文走（插件页 → Plugin）", vm.PageSourceKind == "Plugin", $"实际 {vm.PageSourceKind}");
                vm.SelectedNode = autoNode.Children[0];
                Check("页眉档位跟着正文走（自动页 → Auto）", vm.PageSourceKind == "Auto", $"实际 {vm.PageSourceKind}");
            }

            // ---- 8) 目录搜索：过滤出来的是"带高亮的副本"，正文与原树都不许被它扰动 ----
            var allPages = AllPages(catalog.Groups).ToList();
            if (bead is { Children.Count: > 0 })
            {
                var needle = bead.Title.Substring(0, 2);       // 取插件名的头两字，保证至少命中该插件节点
                var documentBefore = vm.Document;
                var pageTitleBefore = vm.PageTitle;

                vm.Query = needle;
                var flat = Flatten(vm.Nodes).ToList();

                Check("搜索态：IsSearching 为真、空态为假", vm.IsSearching && !vm.HasNoMatch, "");
                Check($"搜索「{needle}」有命中", flat.Count > 0, $"实际 {flat.Count} 个节点");
                Check("每行标题三段能拼回原文（高亮切分不吃字）",
                    flat.All(n => n.TitleHead + n.TitleMatch + n.TitleTail == n.Title),
                    string.Join("、", flat.Where(n => n.TitleHead + n.TitleMatch + n.TitleTail != n.Title).Take(3).Select(n => n.Title)));
                Check("命中段逐字等于关键字且长度=关键字长度",
                    flat.Where(n => n.HasMatch).All(n => n.TitleMatch.Length == needle.Length
                        && string.Equals(n.TitleMatch, needle, StringComparison.OrdinalIgnoreCase)),
                    string.Join("、", flat.Where(n => n.HasMatch && n.TitleMatch.Length != needle.Length).Take(3).Select(n => n.Title)));
                Check("保留下来的分支一律展开（否则命中的页看不见）",
                    flat.All(n => n.Children.Count == 0 || n.IsExpanded), "");
                Check("每个可见页都能追溯到一次命中（自己或祖先）",
                    EveryLeafAccountable(vm.Nodes, false), "");
                Check("命中页数统计与目录树一致",
                    vm.MatchPageCount == flat.Count(n => n.Page != null), $"报告 {vm.MatchPageCount}");
                Check("搜索不打扰正文（文档与页标题都不变）",
                    ReferenceEquals(vm.Document, documentBefore) && vm.PageTitle == pageTitleBefore,
                    $"搜索后标题={vm.PageTitle}");
                Check("过滤树与原树共用同一批 HelpPage 引用（逐页缓存/历史仍按引用命中）",
                    flat.Where(n => n.Page != null).All(n => allPages.Contains(n.Page)), "");

                vm.Query = string.Empty;
                Check("清空搜索：整棵原树换回来（节点引用逐字相同）",
                    vm.Nodes.Count == catalog.Groups.Count && ReferenceEquals(vm.Nodes[0], catalog.Groups[0]),
                    $"节点数 {vm.Nodes.Count}/{catalog.Groups.Count}");
                Check("清空搜索：搜索态与空态归位、正文仍未受影响",
                    !vm.IsSearching && !vm.HasNoMatch && ReferenceEquals(vm.Document, documentBefore), "");
            }

            // ---- 9) 只命中分组名：整支保留（搜"软件手册"该看到这个分类里的全部页）----
            vm.Query = HelpCatalogService.HostGroupTitle;
            var hostHit = vm.Nodes.FirstOrDefault(g => g.Title == HelpCatalogService.HostGroupTitle);
            Check("只命中分组名时命中点落在分组自己", hostHit is { HasMatch: true }, "");
            Check("只命中分组名时整支保留（子项一个不丢）",
                hostHit != null && hostNode != null && hostHit.Children.Count == hostNode.Children.Count,
                $"过滤后 {hostHit?.Children.Count} / 原 {hostNode?.Children.Count}");
            Check("只命中分组名时子页不标底色（它们没命中，靠祖先撑起可见）",
                hostHit != null && hostHit.Children.All(c => !c.HasMatch), "");
            vm.Query = string.Empty;

            // ---- 10) 零命中给空态；Esc 在搜索态先清关键字、再按一次才关窗 ----
            var pageTitleSteady = vm.PageTitle;
            vm.Query = "绝不可能出现在目录里的串ZZZ";
            Check("零命中：目录清空并给空态", vm.HasNoMatch && vm.Nodes.Count == 0 && vm.MatchPageCount == 0,
                $"节点 {vm.Nodes.Count} / 页数 {vm.MatchPageCount}");
            Check("零命中同样不动正文", vm.PageTitle == pageTitleSteady, $"实际 {vm.PageTitle}");

            ButtonResult? closedResult = null;
            vm.RequestClose = MakeDialogCloseListener(result => closedResult = result.Result);
            vm.Query = "检测";
            vm.CloseCommand.Execute();
            Check("Esc 在搜索态只清关键字、不关窗", closedResult == null && !vm.IsSearching && !vm.HasNoMatch,
                $"closed={closedResult?.ToString() ?? "(未关)"}");
            vm.CloseCommand.Execute();
            Check("清完再按一次 Esc 才关窗（Cancel）", closedResult == ButtonResult.Cancel,
                $"实际 {closedResult?.ToString() ?? "(未关)"}");
            vm.Query = string.Empty;

            // ---- 11) 真视图渲染成 PNG ----
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

            // 搜索态：目录重新显示、命中分支展开、命中段带底色、页眉给档位徽章
            if (!vm.IsTocVisible)
                vm.ToggleTocCommand.Execute();
            vm.Query = "检测";
            var searchLeaf = FirstLeaf(vm.Nodes);
            if (searchLeaf != null)
                vm.SelectedNode = searchLeaf;
            Render(view, Path.Combine(outputDir, "04-目录搜索.png"));
            vm.Query = string.Empty;

            // 读者分层：打开开关后开发页带「开发」角标出现在树上（人工核对观感）
            var roiForShot = FindTopic(catalog.Groups, "ROI");
            if (roiForShot != null)
            {
                vm.ShowDevDocs = true;
                Expand(vm.Nodes, "常用工具");
                var roiVisible = FindTopic(vm.Nodes, "ROI");
                if (roiVisible != null)
                    roiVisible.IsExpanded = true;
                var devLeaf = roiVisible?.Children.FirstOrDefault(n => n.IsDevDoc);
                if (devLeaf != null)
                    vm.SelectedNode = devLeaf;
                Render(view, Path.Combine(outputDir, "05-开发章节.png"));
                vm.ShowDevDocs = false;
            }

            // ---- 12) 正文滚轮手感（真机反馈"卡顿、持续滚动太慢"）----
            if (bead != null)
                RunWheelChecks(vm, view, bead);

            // ---- 13) 图标按钮的紧格子（静态闸 GlyphButtonClipCheck 的算术前提）----
            RunGlyphClipChecks();

            Console.WriteLine(_failures == 0
                ? "=== HelpProbe: 全部通过 ==="
                : $"### HelpProbe: {_failures} 项失败");
            return _failures == 0 ? 0 : 1;
        }

        // ==================================================================
        //  滚轮手感断言（视图侧行为：接管 → 补间 → 落点）
        // ==================================================================

        /// <summary>
        /// 正文滚轮走的是"累加目标 + 每帧追近"的补间，所以断言必须能推进时间：
        /// <see cref="Dispatcher.PushFrame"/> 空转一段，让 DispatcherTimer 真的 tick 几下。
        /// </summary>
        private static void PumpFor(int milliseconds)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        /// <summary>
        /// 在 <paramref name="target"/> 上合成一格滚轮（delta：负数=往下）。返回事件本身好读 Handled。
        /// 必须 raise **Preview**MouseWheelEvent：手工 raise 冒泡的 MouseWheelEvent 时 WPF 只走冒泡段、
        /// 预览段整条不跑（对照实验试过），而真鼠标滚轮是从预览段起手的——raise 错事件的话
        /// HelpView 里那个 OnPreviewMouseWheel 根本不会被叫到，断言就成了"测一个没人接的电话"照样绿。
        /// </summary>
        private static MouseWheelEventArgs Wheel(UIElement target, int delta)
        {
            var e = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
            {
                RoutedEvent = UIElement.PreviewMouseWheelEvent,
            };
            target.RaiseEvent(e);
            return e;
        }

        /// <summary>
        /// 从视图读私有常量 <c>WheelLines</c>（一格几行正文）。const 在元数据里是 Literal 字段，反射照样取得。
        /// 取不到就返回 0 让闸门红，**不要**回退成一个抄来的默认值——那样"改了代码忘了改断言"又没人守。
        /// </summary>
        private static int ReadWheelLines()
        {
            try
            {
                var field = typeof(HelpView).GetField("WheelLines", BindingFlags.NonPublic | BindingFlags.Static);
                return field == null ? 0 : Convert.ToInt32(field.GetValue(null));
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static void RunWheelChecks(HelpViewModel vm, HelpView view, HelpNode bead)
        {
            // 挑一页足够长的正文（封面短了就无从判"滚得动"）
            vm.Query = string.Empty;
            if (bead is { Children.Count: > 0 })
                vm.SelectedNode = bead.Children[0];

            var host = new Border { Width = 1120, Height = 740, Background = Brushes.White, Child = view };
            host.Measure(new Size(1120, 740));
            host.Arrange(new Rect(0, 0, 1120, 740));
            host.UpdateLayout();

            var viewer = FindFirst<FlowDocumentScrollViewer>(host);
            var scroll = viewer != null ? FindFirst<ScrollViewer>(viewer) : null;
            var toc = FindFirst<TreeView>(host);

            var doc = viewer?.Document;
            var lineHeight = doc != null && doc.LineHeight > 0 ? doc.LineHeight : (doc?.FontSize ?? 14) * 1.8;

            // 一格几行从视图里读，不在断言里再抄一遍常量：抄了就成了"改代码不改断言也照样绿"。
            // 下限 4 行是"比 WPF 默认三行快"这条现场诉求本身（2026-10-16 反馈"持续滚动太慢"）。
            var wheelLines = ReadWheelLines();
            var step = lineHeight * wheelLines;
            Check($"从视图读到 WheelLines≥4（读到 {wheelLines}）", wheelLines >= 4,
                $"一格 {step:F0}px / 正文行高 {lineHeight:F1}");
            if (wheelLines < 4)
            {
                host.Child = null;
                return;
            }

            Check("正文容器与内层 ScrollViewer 都在（滚轮接管的前置）",
                viewer != null && scroll != null && scroll.ScrollableHeight > step * 3,
                $"可滚 {scroll?.ScrollableHeight:F0} / 一格 {step:F0}");
            if (viewer == null || scroll == null || scroll.ScrollableHeight <= step * 3)
            {
                host.Child = null;
                return;
            }

            var handled = Wheel(viewer, -120).Handled;
            Check("正文滚轮被视图接管（不走 WPF 默认的逐格硬跳）", handled, "");
            Check("这一格不当场硬跳到位（补间的证据：位移由后面的帧追过去）",
                scroll.VerticalOffset < 1,
                $"当场 {scroll.VerticalOffset:F1} / 目标 {step:F1}");

            PumpFor(100);
            Check("100ms 时正在半路上（既没原地不动、也没一步跨到位）",
                scroll.VerticalOffset > 1 && scroll.VerticalOffset < step - 1,
                $"100ms 时 {scroll.VerticalOffset:F1} / 目标 {step:F1}");

            PumpFor(700);
            Check($"补间落点 = {wheelLines} 行正文",
                Math.Abs(scroll.VerticalOffset - step) <= 2,
                $"实际 {scroll.VerticalOffset:F1} / 期望 {step:F1}");

            var first = scroll.VerticalOffset;
            Wheel(viewer, -120);
            PumpFor(700);
            Check("连续第二格：目标继续推远（每格重启动画就会只走一格）",
                Math.Abs(scroll.VerticalOffset - first - step) <= 2,
                $"第一格后 {first:F1} → 第二格后 {scroll.VerticalOffset:F1}");

            // 连滚四格、中间不泵：目标必须按格累加（只从"实际位置"累加就会越滚越追不上，
            // 现场说的"持续滚动太慢"是这一条）。落点 250→450，四格后应当是 450+4×125=950。
            var burstFrom = scroll.VerticalOffset;
            for (var i = 0; i < 4; i++)
                Wheel(viewer, -120);
            PumpFor(1200);
            Check("快速连滚四格：距离按格累加（不是每格都从当前位置重算）",
                Math.Abs(scroll.VerticalOffset - burstFrom - 4 * step) <= 3,
                $"起 {burstFrom:F1} → 四格后 {scroll.VerticalOffset:F1}（期望 {burstFrom + 4 * step:F1}）");

            // 目录上的滚轮不接管：正文位置必须一动不动
            var before = scroll.VerticalOffset;
            var tocHandled = Wheel(toc ?? (UIElement)viewer, -120).Handled;
            PumpFor(200);
            Check("目录上的滚轮不接管（正文不跟着滚）",
                !tocHandled && Math.Abs(scroll.VerticalOffset - before) < 1,
                $"Handled={tocHandled}，位移 {scroll.VerticalOffset - before:F1}");

            host.Child = null;
        }

        // ==================================================================
        //  图标按钮"格子减内边距"的前提闸（静态闸 GlyphButtonClipCheck 的判据来源）
        // ==================================================================

        /// <summary>
        /// 钉住 <c>FlowCanvasChecks.GlyphButtonClipCheck</c> 那条静态判据的**前提**：
        /// 不挂样式也不写 Padding 的按钮会吃到 Fluent 那支隐式 Button 样式的 <c>11,5,11,6</c> 内边距，
        /// 于是 30×30 只剩 6×17、24×24 只剩 <c>0×11</c>——「✕」被模板里那圈带圆角的 Border 切掉
        /// （2026-10-16 真机三连报"关闭按钮的 X 显示不全"，对照图上看就是 30×30 那颗只剩一个「&gt;」）。
        ///
        /// 判据取"内容区 vs 字形需求"，**不取内层 TextBlock 的 ActualWidth**：TextBlock 被父级挤了
        /// 也不会改自己的实得宽（本轮第一版就是这么误判成"全部完整"的），需求宽必须由一个不受约束的
        /// 参照 TextBlock 单独量。
        ///
        /// 这几条红了 = Fluent 换了版本、或主题不再并进应用级资源：静态闸的算术前提没了，
        /// 该重审判据而不是继续挂绿灯（R38）。
        /// </summary>
        private static void RunGlyphClipChecks()
        {
            Console.WriteLine("[glyph] —— 图标按钮的紧格子（Fluent 隐式内边距会切掉字形）——");

            var bare30 = GlyphFit(30, 30, 14, null, null);
            Check("前提① 30×30 裸按钮（不挂样式不写 Padding）放不下「✕」= 真机切字那一档",
                bare30.Clipped, $"内容区 {bare30.ContentW:F1}×{bare30.ContentH:F1} < 需求 {bare30.NeedW:F1}×{bare30.NeedH:F1}");

            var fixed30 = GlyphFit(30, 30, 14, new Thickness(0), null);
            Check("修法① 同一形状写 Padding=0 就完整（本轮三颗标题栏/收起按钮用的就是这一档）",
                !fixed30.Clipped, $"内容区 {fixed30.ContentW:F1}×{fixed30.ContentH:F1}");

            var bare24 = GlyphFit(24, 24, 16, null, null);
            Check("前提② 24×24 裸按钮的内容区宽度归零（报警历史那颗「×」整根不见，比切一半更狠）",
                bare24.ContentW <= 0, $"内容区 {bare24.ContentW:F1}×{bare24.ContentH:F1}");

            var subtle = GlyphFit(30, 30, 14, null, "DialogIconButton");
            Check("修法② 挂 DialogIconButton（内边距 6,4,6,4）同样完整——标题栏那五颗走的是这一档",
                !subtle.Clipped, $"内容区 {subtle.ContentW:F1}×{subtle.ContentH:F1}");
        }

        /// <summary>量一档形状：格子尺寸 / 字号 / 显式 Padding / 样式键，返回内容区与字形需求。</summary>
        private static (double ContentW, double ContentH, double NeedW, double NeedH, bool Clipped) GlyphFit(
            double width, double height, double fontSize, Thickness? padding, string styleKey)
        {
            var button = BuildGlyphButton(width, height, fontSize, padding, styleKey);
            var host = new Border { Width = 200, Height = 90, Background = Brushes.White, Child = button };
            host.Measure(new Size(200, 90));
            host.Arrange(new Rect(0, 0, 200, 90));
            host.UpdateLayout();

            // 参照：不受任何约束的同一个字，量出"本该多宽"
            var reference = new TextBlock
            {
                Text = "✕",
                FontSize = button.FontSize,
                FontFamily = button.FontFamily,
                FontWeight = button.FontWeight,
            };
            reference.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            var pad = button.Padding;
            var border = button.BorderThickness;
            var contentW = button.ActualWidth - pad.Left - pad.Right - border.Left - border.Right;
            var contentH = button.ActualHeight - pad.Top - pad.Bottom - border.Top - border.Bottom;
            var needW = reference.DesiredSize.Width;
            var needH = reference.DesiredSize.Height;

            Console.WriteLine($"[glyph] {width:F0}×{height:F0} Fs{fontSize} " +
                              $"{(styleKey != null ? "样式 " + styleKey : padding != null ? "Padding=0" : "裸按钮")} " +
                              $"生效内边距 {pad.Left},{pad.Top},{pad.Right},{pad.Bottom} " +
                              $"内容区 {contentW:F1}×{contentH:F1} / 需求 {needW:F1}×{needH:F1} " +
                              $"{(contentW < needW - 0.4 || contentH < needH - 0.4 ? "被切" : "完整")}");

            host.Child = null;
            return (contentW, contentH, needW, needH, contentW < needW - 0.4 || contentH < needH - 0.4);
        }

        private static Button BuildGlyphButton(double w, double h, double fontSize,
            Thickness? padding, string styleKey)
        {
            var button = new Button
            {
                Content = "✕",
                FontSize = fontSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            button.Width = w;
            button.Height = h;
            if (padding.HasValue)
                button.Padding = padding.Value;
            if (styleKey != null)
            {
                if (Application.Current.Resources[styleKey] is Style applied)
                    button.Style = applied;
                else
                    Check($"样式键 {styleKey} 在应用级资源里取得到", false, "取不到 = 这条修法没有对照物，判据该重审");
            }
            return button;
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

        /// <summary>
        /// 从内嵌资源名取出页自己的文件名：先砍掉 <c>{程序集}.Help.</c> 前缀，再去掉 <c>.md</c>，
        /// 最后取<b>最后一个点</b>之后的段——MSBuild 用点表达目录，多插件程序集的分册目录名
        /// （逐字等于插件类名）就粘在页名前面，不剥掉就认不出 <c>NN-dev-</c> 标记。
        /// </summary>
        private static string ManifestPageName(string resourceName)
        {
            const string marker = ".Help.";
            var i = resourceName.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            var rest = i < 0 ? resourceName : resourceName.Substring(i + marker.Length);
            var ext = rest.LastIndexOf('.');
            var stem = ext < 0 ? rest : rest.Substring(0, ext);
            var dir = stem.LastIndexOf('.');
            return dir < 0 ? stem : stem.Substring(dir + 1);
        }

        /// <summary>不重叠地数一个子串出现了几次。</summary>
        private static int CountLiteral(string text, string marker)
        {
            var count = 0;
            for (var at = 0; (at = text.IndexOf(marker, at, StringComparison.Ordinal)) >= 0; )
            {
                count++;
                at += marker.Length;
            }
            return count;
        }

        /// <summary>
        /// 原文里"代码上下文中"的 <c>**</c> 个数 = 渲染后<b>允许</b>原样出现的星号额度
        /// （<c>Help\**\*</c> 这种通配写在反引号或代码块里，本来就该字面显示）。
        /// 渲染结果超出这个额度，就说明正文里有一处加粗没闭合。
        /// </summary>
        private static int CountLiteralInsideCode(string markdown)
        {
            var total = 0;
            var inFence = false;
            foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.TrimStart();
                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    inFence = !inFence;
                    continue;
                }
                if (inFence)
                {
                    total += CountLiteral(line, "**");
                    continue;
                }
                for (var from = 0; ; )
                {
                    var open = line.IndexOf('`', from);
                    if (open < 0) break;
                    var close = line.IndexOf('`', open + 1);
                    if (close < 0) break;
                    total += CountLiteral(line.Substring(open + 1, close - open - 1), "**");
                    from = close + 1;
                }
            }
            return total;
        }

        /// <summary>把渲染出的文档里"代码块段落"用的字体族认出来：正文族挂在文档上，代码块段落是唯一改了这个属性的块。</summary>
        private static FontFamily FindCodeFont(FlowDocument doc)
        {
            foreach (var block in doc.Blocks)
                if (block is Paragraph p && p.FontFamily != null && !Equals(p.FontFamily, doc.FontFamily))
                    return p.FontFamily;
            return null;
        }

        /// <summary>
        /// 原文"代码上下文中"的非 ASCII 字符（fence 与行内反引号，遍历口径与 <see cref="CountLiteralInsideCode"/> 同一条）。
        /// 按 <see cref="Rune"/> 而不是 UTF-16 码元走——代理对拆开会测出"半个 emoji 没字形"的假阳性。
        /// ASCII 一律不测——等宽字体族缺基本拉丁字母不成立，测了只会拖慢。
        /// </summary>
        private static IEnumerable<Rune> CodeContextRunes(string markdown)
        {
            var inFence = false;
            foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.TrimStart();
                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    inFence = !inFence;
                    continue;
                }
                if (!inFence)
                {
                    for (var from = 0; ; )
                    {
                        var open = line.IndexOf('`', from);
                        if (open < 0) break;
                        var close = line.IndexOf('`', open + 1);
                        if (close < 0) break;
                        foreach (var r in LineCodeRunes(line.Substring(open + 1, close - open - 1))) yield return r;
                        from = close + 1;
                    }
                    continue;
                }
                foreach (var r in LineCodeRunes(line)) yield return r;
            }
        }

        private static IEnumerable<Rune> LineCodeRunes(string text) =>
            text.EnumerateRunes().Where(r => r.Value > 0x7F && !Rune.IsWhiteSpace(r));

        /// <summary>
        /// 这个字符在该字体族下是不是豆腐块：把它和<b>一定没有字形</b>的私有区字符 U+E000 各渲染一次比像素，
        /// 一模一样就是"走了缺字形兜底"。比对结果按字符缓存（全库去重后只有一千多个字符）。
        /// </summary>
        private static bool RendersAsMissingGlyph(Rune r, FontFamily family)
        {
            if (!_glyphProbe.TryGetValue(r, out var missing))
            {
                missing = GlyphPixels(r.ToString(), family).SequenceEqual(GlyphPixels("\uE000", family));
                _glyphProbe[r] = missing;
            }
            return missing;
        }

        private static readonly Dictionary<Rune, bool> _glyphProbe = new();

        private static byte[] GlyphPixels(string text, FontFamily family)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var ft = new FormattedText(text,
                    System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                    16, Brushes.Black, 1.0);
                dc.DrawText(ft, new Point(2, 2));
            }
            var rtb = new RenderTargetBitmap(28, 28, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            var px = new byte[28 * 28 * 4];
            rtb.CopyPixels(px, 28 * 4, 0);
            return px;
        }

        /// <summary>把渲染后仍留下的 <c>**</c> 各截一段上下文，便于回原文定位（诊断用，不参与判据）。</summary>
        private static string FirstStarContexts(string renderedText, int take)
        {
            var parts = new List<string>();
            for (var at = 0; parts.Count < take && (at = renderedText.IndexOf("**", at, StringComparison.Ordinal)) >= 0; )
            {
                var from = Math.Max(0, at - 140);
                parts.Add("…" + renderedText.Substring(from, Math.Min(360, renderedText.Length - from)).Replace("\n", " ") + "…");
                at += 2;
            }
            return string.Join(" ｜ ", parts);
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
        //  搜索态探针辅助
        // ==================================================================

        /// <summary>深度优先摊平（跳过自身、含全部后代）</summary>
        private static IEnumerable<HelpNode> Flatten(IEnumerable<HelpNode> nodes)
        {
            foreach (var node in nodes)
            {
                yield return node;
                foreach (var child in Flatten(node.Children))
                    yield return child;
            }
        }

        /// <summary>第一个带页的节点（搜索后要选中它当"命中的一页"）</summary>
        private static HelpNode FirstLeaf(IEnumerable<HelpNode> nodes) =>
            Flatten(nodes).FirstOrDefault(n => n.Page != null);

        /// <summary>
        /// 目录里可见的每一页都必须"自己命中"或"某个祖先命中"——
        /// 反着说：既没命中、祖先也没命中的页不该出现在过滤树里（那等于过滤失效）。
        /// </summary>
        private static bool EveryLeafAccountable(IEnumerable<HelpNode> nodes, bool ancestorMatched)
        {
            foreach (var node in nodes)
            {
                var matched = ancestorMatched || node.HasMatch;
                if (node.Page != null && !matched)
                    return false;
                if (!EveryLeafAccountable(node.Children, matched))
                    return false;
            }
            return true;
        }

        private static string DescribeKind(HelpNode node) =>
            node?.Children.Count > 0 ? node.Children[0].Page?.Kind.ToString() ?? "(无页)" : "(无子页)";

        /// <summary>
        /// 造一个能观察关窗结果的 <see cref="DialogCloseListener"/>（与 ScadaChecks 同源）。
        /// Prism 9 的这个类型是类而不是委托、回调构造器是 internal 的，真机上由 DialogService 注入；
        /// 探针里没有容器，所以反射走一次那个 internal 构造器拿它当替身，用来断言 Esc 的两段式去向。
        /// </summary>
        private static DialogCloseListener MakeDialogCloseListener(Action<IDialogResult> onClosed)
        {
            var ctor = typeof(DialogCloseListener).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(Action<IDialogResult>) },
                modifiers: null);

            if (ctor == null)
                throw new MissingMethodException(
                    "Prism.Dialogs.DialogCloseListener", ".ctor(Action<IDialogResult>)");

            return (DialogCloseListener)ctor.Invoke(new object[] { onClosed });
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
            {
                Console.WriteLine($"[probe] 字号下拉：项数 {combo.Items.Count}，SelectedItem={combo.SelectedItem ?? "(null)"}，SelectedIndex={combo.SelectedIndex}");

                // 判据取"模板与 DialogCombo 同源"而不是 Style 的名字：
                // 从字典里取到的 Style 没有 StyleName（ToString 只会给出 System.Windows.Style），
                // 而隐式样式同样会填 combo.Style——只有模板引用相等才证明没落回 Fluent 那套。
                var dialogCombo = Application.Current?.Resources["DialogCombo"] as Style;
                var dialogComboTemplate = dialogCombo?.Setters
                    .Cast<Setter>()
                    .FirstOrDefault(s => s.Property == Control.TemplateProperty)?
                    .Value as ControlTemplate;

                Check("字号下拉用的是 DialogCombo 模板（Fluent 隐式模板压到 28 高会把「标准」上下切一截）",
                    dialogComboTemplate != null
                    && ReferenceEquals(combo.Template, dialogComboTemplate)
                    && combo.ActualHeight >= 26 && combo.ActualWidth >= 60,
                    $"尺寸 {combo.ActualWidth}×{combo.ActualHeight}，模板同源={ReferenceEquals(combo.Template, dialogComboTemplate)}");

                // 关闭按钮与字号下拉同族：不挂样式就吃 Fluent 的隐式 Button 模板（自带 11,5,11,6 内边距），
                // 30×30 的格子会把「✕」切掉。判据同上——只认模板引用同源，不认 Style 的名字。
                var closeButton = FindAll<Button>(host)
                    .FirstOrDefault(b => b.ToolTip is string tip && tip.StartsWith("关闭", StringComparison.Ordinal));
                var toolButtonTemplate = (view.Resources["ToolButtonStyle"] as Style)?.Setters
                    .Cast<Setter>()
                    .FirstOrDefault(s => s.Property == Control.TemplateProperty)?
                    .Value as ControlTemplate;

                Check("关闭按钮走 ToolButtonStyle（Fluent 隐式模板会把「✕」切掉）",
                    closeButton != null && toolButtonTemplate != null
                    && ReferenceEquals(closeButton.Template, toolButtonTemplate)
                    && closeButton.ActualWidth >= 28 && closeButton.ActualHeight >= 28,
                    closeButton == null
                        ? "没找到关闭按钮"
                        : $"尺寸 {closeButton.ActualWidth}×{closeButton.ActualHeight}，模板同源={ReferenceEquals(closeButton.Template, toolButtonTemplate)}");
            }

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

        /// <summary>整棵子树里找全部 T（FindFirst 只回第一个，按钮族要逐个核对时不够用）</summary>
        private static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T hit)
                    yield return hit;
                foreach (var deeper in FindAll<T>(child))
                    yield return deeper;
            }
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

        /// <summary>
        /// 探针专用的"没有手册的插件"类型：不实现 <see cref="IPluginHelpProvider"/>、
        /// 所在程序集也没有 Help 内嵌资源，因此目录合并必定落到第三档（自动生成页）。
        /// 真机上 42/42 插件都自带手册，这条路已经没有住户，角标与来源档位两条断言只能靠它。
        /// </summary>
        private sealed class NoManualProbe
        {
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
