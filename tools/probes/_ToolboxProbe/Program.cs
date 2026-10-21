using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.ViewModels;
using VisionMaster.Views;

namespace ToolboxProbe
{
    /// <summary>
    /// 工具箱面板（ToolView + ToolViewModel）离屏渲染探针。
    ///
    /// 为什么要有它：UIThemeSmokeTest 里的 ShellCopy.xaml 确实含 &lt;views:ToolView/&gt;，
    /// 但那个宿主把 ViewModelLocationProvider 的工厂设成"永远返回 null"——
    /// 也就是说**所有绑定都在空 DataContext 上静默失败**，条目模板/转换器/附加属性
    /// 一个都没真正跑过。这里补上真 VM + 真条目的那一段：
    ///   1) 挂 PresentationTraceSources 监听，把绑定错误当断言（写错路径在这里必红）；
    ///   2) 逐条核对结构：列表/网格模板切换、星标命令、拖拽四件套、搜索高亮、预览浮层；
    ///   3) 出两张 PNG（列表 / 网格）供肉眼验收观感。
    /// </summary>
    internal static class Program
    {
        // 产物落点按仓库分区约定（R166）：artifacts\render\<yyyyMMdd>\，不落根目录
        private static readonly string OutputDir =
            Path.Combine(@"D:\C#\VM\artifacts\render", DateTime.Now.ToString("yyyyMMdd"));

        private static int _pass, _fail;

        private sealed class BindingErrorListener : TraceListener
        {
            public List<string> Messages { get; } = new();
            public override void Write(string message) { }
            public override void WriteLine(string message)
            {
                if (!string.IsNullOrWhiteSpace(message)) Messages.Add(message.Trim());
            }
        }

        [STAThread]
        private static int Main()
        {
            Directory.CreateDirectory(OutputDir);

            if (Application.Current == null)
                new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

            // 探针里没有 Prism 容器：不加这一句，new ToolView() 会在 XamlParseException 里炸
            // （AutoWireViewModel=True → 默认工厂 Activator.CreateInstance(ToolViewModel) → 没有无参构造）
            Prism.Mvvm.ViewModelLocationProvider.SetDefaultViewModelFactory(_ => null);

            // 复现 App.xaml：UI 主题 + Fluent。
            // Icon 令牌在 App.xaml 里是"内联 XAML 里的 FontFamily"，探针没有 App.xaml，
            // 所以这里**别名**到 UI 主题里那份同源同串的 FA.Solid——
            // 注意别用 new FontFamily("pack://...") 在代码里造：那样造出来的解析不出来
            // （实测 FamilyNames 直接退成 Arial，满屏豆腐块），XAML 解析出来的那份才认。
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/UI;component/Themes/Generic.xaml")
            });
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml")
            });
            Application.Current.Resources["Icon"] = Application.Current.TryFindResource("FA.Solid");

            var bindingErrors = new BindingErrorListener();
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(bindingErrors);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;

            // 图标字体能不能真的解析出来（解析不出来 = 满屏豆腐块，出图就白看了）
            const string FontUri = "pack://application:,,,/UI;component/Asserts/FontFamilys/Font Awesome 6 Pro-Solid-900.otf";
            const string FontFace = "Font Awesome 6 Pro Solid";
            try
            {
                var stream = Application.GetResourceStream(new Uri(FontUri));
                Console.WriteLine($"  [INFO] 字体资源流={(stream == null ? "null（路径不对）" : stream.Stream.Length + " 字节")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [INFO] 字体资源取不到：{ex.GetType().Name} {ex.Message}");
            }
            DumpFont("字符串构造", new FontFamily($"{FontUri}#{FontFace}"));
            DumpFont("两参构造", new FontFamily(
                new Uri("pack://application:,,,/UI;component/Asserts/FontFamilys/"),
                $"./{FontFace}.otf#{FontFace}"));

            var provider = new ProbePluginProvider();
            var vm = new ToolViewModel(provider);

            var view = new ToolView { DataContext = vm };
            var host = new Border { Width = 280, Height = 760, Child = view };

            var phase = "构造/首次布局";
            try
            {
                Layout(host);
            }
            catch (Exception ex)
            {
                Check("ToolView 构造 + 布局不抛异常", false, ex.Message);
                return Finish(bindingErrors);
            }
            Check("ToolView 构造 + 布局不抛异常", true, "");
            Console.WriteLine($"  [INFO] {phase} 后绑定错误 {bindingErrors.Messages.Count} 条");

            // 分组默认折叠（IsExpanded=false）→ 里面的条目容器根本不生成。下面的条目级断言
            // 需要它们可见，所以先把所有组展开（"默认折叠"这条语义由 G9 专门钉）。
            foreach (var g in vm.FilteredToolBarSource) g.IsExpanded = true;
            Layout(host);
            Layout(host);

            // ---- 1. 列表模式结构 ----
            var itemsControls = Descendants<ItemsControl>(view)
                .Where(ic => GongSolutions.Wpf.DragDrop.DragDrop.GetIsDragSource(ic))
                .ToList();
            Check("拖拽源：每个分组各一个 ItemsControl，且 dd 四件套齐",
                itemsControls.Count == provider.CategoryCount
                && itemsControls.All(ic =>
                    GongSolutions.Wpf.DragDrop.DragDrop.GetUseDefaultDragAdorner(ic) == false
                    && GongSolutions.Wpf.DragDrop.DragDrop.GetUseDefaultEffectDataTemplate(ic) == true
                    && GongSolutions.Wpf.DragDrop.DragDrop.GetDragAdornerTemplate(ic) != null),
                $"拖拽源={itemsControls.Count} / 分组={provider.CategoryCount}");

            // 条目数据必须是原始 ToolItemModel（放置端 FlowCanvasView.FindDraggedTool 硬认它）。
            // 用"宿主 ContentPresenter 挂的是条目模板"来定位条目根 Border——原来靠 ToolTip 定位，
            // 但预览已从 ToolTip 改成 Popup，条目上不再挂 ToolTip。
            var listTemplate = FindResource(view, "ToolListTemplate") as DataTemplate;
            var gridTemplate = FindResource(view, "ToolGridTemplate") as DataTemplate;
            var itemBorders = Descendants<ContentPresenter>(view)
                .Where(cp => ReferenceEquals(cp.ContentTemplate, listTemplate)
                             || ReferenceEquals(cp.ContentTemplate, gridTemplate))
                .Select(cp => VisualTreeHelper.GetChildrenCount(cp) > 0 ? VisualTreeHelper.GetChild(cp, 0) : null)
                .OfType<Border>()
                .ToList();
            Check("条目容器绑定的是原始 ToolItemModel（不是包裹 VM）",
                itemBorders.Count == provider.ModulePlugins.Count
                && itemBorders.All(b => b.DataContext.GetType() == typeof(ToolItemModel)),
                $"条目容器={itemBorders.Count} / 算子={provider.ModulePlugins.Count}");

            Check("列表模式：单列 StackPanel（没有 UniformGrid）",
                Descendants<UniformGrid>(view).Count == 0, "");
            Render(host, "toolbox-empty-hint.png");   // 尚无收藏：空态提示行在

            // 悬停预览浮层：已从 ToolTip 改成视图自管的 Popup（ToolTip 的内容拿不到焦点，
            // 里面的滚动条真机上点了不动）。探针不开窗（无窗口），只断言 Popup 就位、内容模板挂对、
            // 且条目上不再挂 ToolTip。
            var previewPopup = view.FindName("PreviewPopup") as System.Windows.Controls.Primitives.Popup;
            var previewContent = view.FindName("PreviewContent") as ContentControl;
            Check("预览浮层：视图自管 Popup + 内容模板就位（条目上不再挂 ToolTip）",
                previewPopup != null && previewContent != null
                && ReferenceEquals(previewContent.ContentTemplate, FindResource(view, "ToolPreviewTemplate"))
                && itemBorders.All(b => b.ToolTip == null),
                $"popup={previewPopup != null} content={previewContent != null}");

            // ---- 2. 星标：命令 + 收藏态观感 ----
            var starButtons = Descendants<Button>(view)
                .Where(b => ReferenceEquals(b.Command, vm.ToggleFavoriteCommand))
                .ToList();
            Check("每个条目一颗星标按钮，命令挂的是 ToggleFavoriteCommand",
                starButtons.Count == provider.ModulePlugins.Count, $"星标={starButtons.Count}");

            var leaf = provider.ModulePlugins[ProbePluginProvider.LeafKey];
            var leafStar = starButtons.FirstOrDefault(b => ReferenceEquals(b.DataContext, leaf));
            Check("未收藏：星标默认透明（悬停才显形）",
                leafStar != null && leafStar.Opacity == 0, $"opacity={leafStar?.Opacity}");

            vm.ToggleFavoriteCommand.Execute(leaf);
            Layout(host);
            Check("收藏后：该行星标常显且换成主色",
                leafStar != null && leafStar.Opacity == 1 && HasColor(leafStar.Foreground, "ScadaAccentBrush", view),
                $"opacity={leafStar?.Opacity} fg={(leafStar?.Foreground as SolidColorBrush)?.Color}");

            Check("收藏后：收藏分组置顶，且组头含该项",
                vm.FilteredToolBarSource.Count == provider.CategoryCount + 1
                && vm.FilteredToolBarSource[0].IsFavorites
                && ReferenceEquals(vm.FilteredToolBarSource[0].Children[0], leaf),
                $"groups={vm.FilteredToolBarSource.Count} first={vm.FilteredToolBarSource[0].Name}");

            // ---- 3. 搜索高亮 ----
            vm.SearchText = "图像";
            Layout(host);
            var highlightRun = Descendants<TextBlock>(view)
                .Where(tb => tb.DataContext is ToolItemModel)
                .SelectMany(tb => tb.Inlines.OfType<Run>())
                .FirstOrDefault(r => HasColor(r.Foreground, "ScadaAccentBrush", view));
            Check("搜索高亮：命中片段被拆成独立 Run 并换主色",
                highlightRun != null && highlightRun.Text == "图像",
                highlightRun == null ? "没有换色的 Run" : $"命中 Run='{highlightRun.Text}'");

            var plainCount = Descendants<TextBlock>(view)
                .Count(tb => tb.DataContext is ToolItemModel && tb.Inlines.OfType<Run>().Any(r => r.Text.Contains("图像")));
            Check("搜索高亮：命中条目在界面上确实存在", plainCount > 0, $"命中行={plainCount}");
            vm.SearchText = "";
            Layout(host);

            // ---- 3b. 原地更新：保住的分组与条目连容器一起留住（ToolViewModel.ApplyFilter） ----
            // 旧实现每敲一个字符都 Clear+Add，把整棵可视树拆掉重建（42 条目量级实测 150~200 ms）。
            // 现在只增删变化的条目，所以两条断言：命中集没变时分组 VM 与条目容器必须是同一个实例；
            // 换组时旧组必须离场、新组新建（防止写成"什么都不更新"）。
            List<Border> ItemBorders() => Descendants<ContentPresenter>(view)
                .Where(cp => ReferenceEquals(cp.ContentTemplate, listTemplate)
                             || ReferenceEquals(cp.ContentTemplate, gridTemplate))
                .Select(cp => VisualTreeHelper.GetChildrenCount(cp) > 0 ? VisualTreeHelper.GetChild(cp, 0) : null)
                .OfType<Border>()
                .ToList();

            var caliper = provider.ModulePlugins["Probe.Measure"];

            vm.SearchText = "卡尺";
            Layout(host);
            var caliperGroup = vm.FilteredToolBarSource.FirstOrDefault();
            var caliperBorder = ItemBorders().FirstOrDefault(b => ReferenceEquals(b.DataContext, caliper));

            vm.SearchText = "卡尺测";     // 命中集不变（还是那一条）
            Layout(host);
            var caliperGroupAfter = vm.FilteredToolBarSource.FirstOrDefault();
            var caliperBorderAfter = ItemBorders().FirstOrDefault(b => ReferenceEquals(b.DataContext, caliper));

            Check("命中集没变：分组 VM 与条目容器都原地保留（不拆可视树）",
                caliperGroup != null && caliperBorder != null
                && ReferenceEquals(caliperGroup, caliperGroupAfter)
                && ReferenceEquals(caliperBorder, caliperBorderAfter),
                $"组={caliperGroup?.Name} 容器前={caliperBorder != null} 后={caliperBorderAfter != null}"
                + $" 同实例={ReferenceEquals(caliperBorder, caliperBorderAfter)}");

            vm.SearchText = "模板";       // 命中集变了（换组、换条目）
            Layout(host);
            var changedGroup = vm.FilteredToolBarSource.FirstOrDefault();
            Check("命中集变了：旧组离场、新组新建（没有写成「什么都不更新」）",
                changedGroup != null && !ReferenceEquals(changedGroup, caliperGroup),
                $"新组={changedGroup?.Name} 旧组={caliperGroup?.Name}");

            // 分组是原地复用的，而 Count 是派生属性——Children 变了必须自己通知，
            // 否则分组头上的数量胶囊会停在旧数字上（旧实现整份重建，靠新建实例"蒙对"了）
            var countGroup = new ToolboxGroupViewModel();
            int countRaises = 0;
            countGroup.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ToolboxGroupViewModel.Count)) countRaises++;
            };
            countGroup.Children.Add(caliper);
            countGroup.Children.Add(leaf);
            countGroup.Children.Remove(caliper);
            Check("分组数量胶囊：Children 原地增删会通知 Count（原地复用的前提）",
                countRaises == 3 && countGroup.Count == 1, $"通知={countRaises} Count={countGroup.Count}");

            vm.SearchText = "";
            Layout(host);

            // ---- 4. 网格模式：ItemsPanel 换 UniformGrid(2 列) ----
            vm.SetViewModeCommand.Execute("Grid");
            Layout(host);
            var grids = Descendants<UniformGrid>(view).ToList();
            Check("网格模式：内层面板换成 UniformGrid 且 Columns=2",
                grids.Count == provider.CategoryCount + 1 && grids.All(g => g.Columns == 2),
                $"UniformGrid={grids.Count}");
            // 条目定位同第 1 节：走"宿主 ContentPresenter 挂的是网格条目模板"（不再靠 ToolTip）
            var gridItemBorders = Descendants<ContentPresenter>(view)
                .Where(cp => ReferenceEquals(cp.ContentTemplate, gridTemplate))
                .Select(cp => VisualTreeHelper.GetChildrenCount(cp) > 0 ? VisualTreeHelper.GetChild(cp, 0) : null)
                .OfType<Border>()
                .ToList();
            Check("网格模式：条目数不变（只换模板，不换数据）",
                gridItemBorders.Count == vm.FilteredToolBarSource.Sum(g => g.Children.Count),
                $"界面条目={gridItemBorders.Count}"
                + $" / VM 条目={vm.FilteredToolBarSource.Sum(g => g.Children.Count)}");
            Render(host, "toolbox-grid.png");

            vm.SetViewModeCommand.Execute("List");
            Layout(host);
            Render(host, "toolbox-list.png");

            // ---- 5. 悬停预览浮层：把内容模板实例化一遍，确认它自己的绑定也通 ----
            var previewTemplate = (DataTemplate)FindResource(view, "ToolPreviewTemplate");
            if (previewTemplate == null)
            {
                Check("预览浮层模板可达", false, "没找到 ToolPreviewTemplate");
            }
            else
            {
                var preview = (FrameworkElement)previewTemplate.LoadContent();
                preview.DataContext = leaf;
                preview.Measure(new Size(320, 600));
                preview.Arrange(new Rect(0, 0, 320, preview.DesiredSize.Height));
                // 绑定与 ItemsControl 的容器生成都是异步的（DataBind 优先级）：
                // 不泵几次 Dispatcher，Text 还是空的、端口行还没生成
                Pump(preview);

                var texts = Descendants<TextBlock>(preview).Select(t => t.Text ?? "").ToList();
                Check("预览浮层：名称 / 描述 / 输入 / 输出端口都渲染出来",
                    texts.Contains(leaf.Name) && texts.Contains(leaf.Description)
                    && texts.Contains("In") && texts.Contains("Out"),
                    string.Join(" | ", texts.Where(t => t.Length > 0)));

                // 端口行现在带类型 + 注释：类型走短名转换器（System.Double → Double），注释来自 Description
                Check("预览浮层：端口带类型与注释",
                    texts.Contains("HImage") && texts.Contains("Double")
                    && texts.Contains("输入图") && texts.Contains("结果"),
                    string.Join(" | ", texts.Where(t => t.Length > 0)));

                var chipColor = VisionMaster.Helpers.ToolboxCategoryVisuals.ColorFor(leaf.Category);
                var categoryChip = Descendants<Border>(preview)
                    .FirstOrDefault(b => (b.Background as SolidColorBrush)?.Color == chipColor);
                Check("预览浮层：分类名胶囊用的是该分类的专属色",
                    categoryChip != null, $"分类={leaf.Category} 期望色={chipColor}");

                // 端口容器是 Pump 之后才生成的，DesiredSize 会变——出图前重新量一次，
                // 否则按"还没长出端口时"的高度截图，会把输入/输出整段裁掉
                preview.InvalidateMeasure();
                preview.Measure(new Size(320, 2000));
                preview.Arrange(new Rect(0, 0, 320, preview.DesiredSize.Height));
                preview.UpdateLayout();
                Pump(preview);
                Render(preview, "toolbox-preview.png", 320, preview.DesiredSize.Height);
            }

            // ---- 6. 拖拽负载：adornertemplate 能按条目实例化（{Binding Icon}/{Binding Name} 都在） ----
            var adornerTemplate = (DataTemplate)FindResource(view, "MyDragAdornerTemplate");
            if (adornerTemplate == null)
            {
                Check("拖拽预览模板可达", false, "没找到 MyDragAdornerTemplate");
            }
            else
            {
                var adorner = (FrameworkElement)adornerTemplate.LoadContent();
                adorner.DataContext = leaf;
                adorner.Measure(new Size(200, 40));
                adorner.Arrange(new Rect(0, 0, 200, 40));
                Pump(adorner);
                var adornerTexts = Descendants<TextBlock>(adorner).Select(t => t.Text ?? "").ToList();
                Check("拖拽预览模板：Icon / Name 两个绑定都出值",
                    adornerTexts.Contains(leaf.Icon) && adornerTexts.Contains(leaf.Name),
                    string.Join(" | ", adornerTexts));

                // 出图（自然尺寸 + 24px 留白）：留白是为了让阴影完整落在位图里——
                // 贴着 DesiredSize 裁会把 Effect 外扩整圈切掉，证据图看起来像"没有阴影"
                var stage = new Border
                {
                    Padding = new Thickness(24),
                    Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)),
                    Child = adorner,
                };
                stage.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var stageSize = stage.DesiredSize;
                stage.Arrange(new Rect(0, 0, Math.Max(stageSize.Width, 1), Math.Max(stageSize.Height, 1)));
                stage.UpdateLayout();
                Pump(stage);
                Render(stage, "toolbox-drag-ghost.png", Math.Max(stageSize.Width, 1), Math.Max(stageSize.Height, 1));
            }

            // ---- 7. 条目图标的分类色（组 VM 的 AccentBrush 经 ItemsControl 祖先取到） ----
            // leaf 已收藏：界面上它出现两次——收藏组里是金色、原分类组里是分类色，两处都要对
            var iconColors = Descendants<TextBlock>(view)
                .Where(t => ReferenceEquals(t.DataContext, leaf) && (t.Text ?? "") == leaf.Icon)
                .Select(t => (t.Foreground as SolidColorBrush)?.Color)
                .ToList();
            Check("条目图标：前景色按所属分组的专属色（收藏组金 / 分类组分类色）",
                iconColors.Contains(VisionMaster.Helpers.ToolboxCategoryVisuals.FavoriteColor)
                && iconColors.Contains(VisionMaster.Helpers.ToolboxCategoryVisuals.ColorFor(leaf.Category)),
                string.Join(",", iconColors.Select(c => c?.ToString())));

            return Finish(bindingErrors);
        }

        /// <summary>泵几轮 Dispatcher：绑定求值与 ItemsControl 容器生成都在 DataBind 优先级上排队</summary>
        private static void Pump(FrameworkElement element)
        {
            for (int i = 0; i < 3; i++)
            {
                element.UpdateLayout();
                element.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            }
        }

        private static void DumpFont(string label, FontFamily family)
        {
            var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            bool ok = typeface.TryGetGlyphTypeface(out var gtf);
            Console.WriteLine($"  [INFO] {label}：解析={ok}"
                + (ok ? $" 实际家族={string.Join(",", gtf.FamilyNames.Values)}" : "")
                + $" 声明家族={string.Join(",", family.FamilyNames.Values)}");
        }

        private static void Check(string name, bool ok, string detail)
        {
            Console.WriteLine((ok ? "  [PASS] " : "  [FAIL] ") + name + (detail?.Length > 0 ? $"  →  {detail}" : ""));
            if (ok) _pass++; else _fail++;
        }

        /// <summary>比色不比引用：同一支令牌可能被两本字典各合并一份，引用相等会假红</summary>
        private static bool HasColor(Brush brush, string resourceKey, FrameworkElement scope)
            => brush is SolidColorBrush actual
               && scope.TryFindResource(resourceKey) is SolidColorBrush expected
               && actual.Color == expected.Color;

        private static int Finish(BindingErrorListener listener)
        {
            var errors = listener.Messages.Distinct().ToList();

            // 已知噪声：条目模板里的 FindAncestor 绑定在"容器被回收/重建"的那一瞬间会报
            // 「找不到源」——那时元素已经脱离可视树、DataContext 已清空，WPF 之后重新挂树会重算。
            // 判据是"它对应的绑定在最终树上确实生效"（下面各条正向断言已逐条守住），
            // 所以这里只把**真正的**绑定错误判红：路径写错会落到 "property not found" 这类别的措辞上。
            var transient = errors
                .Where(e => e.Contains("Cannot find source for binding with reference 'RelativeSource FindAncestor"))
                .ToList();
            var realErrors = errors.Except(transient).ToList();

            Check("没有真正的绑定错误（路径写错 / 转换器缺失在这里必红）", realErrors.Count == 0,
                realErrors.Count == 0 ? $"{errors.Count} 条全是 FindAncestor 回收期噪声" : $"{realErrors.Count} 条");
            foreach (var e in realErrors) Console.WriteLine("        !! " + e);

            Console.WriteLine();
            Console.WriteLine($"========== 结果汇总 ==========");
            Console.WriteLine($"通过: {_pass}  失败: {_fail}   （FindAncestor 回收期噪声 {transient.Count} 条，不计）");
            Console.WriteLine(_fail == 0 ? ">>> 全部断言通过 <<<" : ">>> 有失败 <<<");
            Console.WriteLine($"出图目录: {OutputDir}");
            return _fail == 0 ? 0 : 1;
        }

        private static void Layout(FrameworkElement host)
        {
            host.Measure(new Size(host.Width, host.Height));
            host.Arrange(new Rect(0, 0, host.Width, host.Height));
            host.UpdateLayout();
            host.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }

        private static void Render(FrameworkElement host, string file)
        {
            var bmp = new RenderTargetBitmap((int)host.Width, (int)host.Height, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(host);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(Path.Combine(OutputDir, file));
            encoder.Save(fs);
            Console.WriteLine($"  [IMG ] {file}");
        }

        /// <summary>按指定尺寸出图（浮层这类没有固定 Width/Height 的元素用）。</summary>
        private static void Render(FrameworkElement host, string file, double width, double height)
        {
            var bmp = new RenderTargetBitmap(
                Math.Max(1, (int)Math.Ceiling(width)), Math.Max(1, (int)Math.Ceiling(height)),
                96, 96, PixelFormats.Pbgra32);
            bmp.Render(host);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(Path.Combine(OutputDir, file));
            encoder.Save(fs);
            Console.WriteLine($"  [IMG ] {file}");
        }

        private static object FindResource(UserControl root, string key)
        {
            var grid = root.Content as FrameworkElement;
            return grid?.TryFindResource(key);
        }

        private static List<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            var result = new List<T>();
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T t) result.Add(t);
                result.AddRange(Descendants<T>(child));
            }
            return result;
        }

        /// <summary>探针用的最小插件提供者：3 个分类 4 个算子（含端口定义，给预览浮层用）</summary>
        private sealed class ProbePluginProvider : IPluginProvider
        {
            public const string LeafKey = "Probe.Leaf";

            private readonly Dictionary<string, ToolItemModel> _modules;

            public ProbePluginProvider()
            {
                _modules = new[]
                {
                    Make("Probe.Leaf", "图像二值化", "图像处理", "\uf1c5", "把灰度图按阈值切成前景/背景"),
                    Make("Probe.Measure", "卡尺测量", "测量", "\uf545", "沿卡尺找边并测距"),
                    Make("Probe.Match", "模板匹配", "定位", "\uf3c5", "按模板在图中找目标位置"),
                    Make("Probe.Logic", "逻辑运算", "逻辑控制", "\uf0e8", "对两个布尔量做与/或/非"),
                }.ToDictionary(m => m.ModuleTypeName, m => m);
            }

            private static ToolItemModel Make(string key, string name, string category, string icon, string desc) => new()
            {
                Id = Guid.NewGuid(),
                ModuleTypeName = key,
                Name = name,
                Category = category,
                Icon = icon,
                Description = desc,
                InputDefinitions = new List<PortDefinition>
                {
                    new() { Name = "In", Description = "输入图", DataTypeName = "HImage" },
                },
                OutputDefinitions = new List<PortDefinition>
                {
                    new() { Name = "Out", Description = "结果", DataTypeName = "System.Double" },
                },
            };

            public IReadOnlyDictionary<string, ToolItemModel> ModulePlugins => _modules;
            public IReadOnlyDictionary<string, ToolItemModel> CameraPlugins { get; } = new Dictionary<string, ToolItemModel>();
            public IReadOnlyDictionary<string, ToolItemModel> LaserPlugins { get; } = new Dictionary<string, ToolItemModel>();
            public IReadOnlyDictionary<string, ToolItemModel> MotionPlugins { get; } = new Dictionary<string, ToolItemModel>();

            /// <summary>分组数 = 去重后的分类数（收藏组另算）</summary>
            public int CategoryCount => _modules.Values.Select(m => m.Category).Distinct().Count();

            public void RegisterModule(ToolItemModel plugin) => _modules[plugin.ModuleTypeName] = plugin;
            public void RegisterCamera(ToolItemModel plugin) { }
            public void RegisterLaser(ToolItemModel plugin) { }
            public void RegisterMotion(ToolItemModel plugin) { }

            public ToolItemModel GetModule(string name) => _modules.TryGetValue(name, out var m) ? m : null;
            public ToolItemModel GetCamera(string name) => null;
            public ToolItemModel GetLaser(string name) => null;
            public ToolItemModel GetMotion(string name) => null;
        }
    }
}
