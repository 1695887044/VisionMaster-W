using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Newtonsoft.Json;
using Prism.Mvvm;
using VisionMaster.Models;
using VisionMaster.Services;
using VisionMaster.ViewModels;
using VisionMaster.Views;

namespace ProjectAssetsUiProbe
{
    /// <summary>
    /// 项目资产管理页离线出图与布局断言（R105/R134/R53）：
    /// 在屏幕外真窗口里装真实 ProjectAssetsView，不启动 VisionMaster 主窗口。
    /// 断言列表行的类型/状态徽标高度为 22、文字在框内水平/垂直居中、背景框不随行高拉伸。
    /// 输出 artifacts\render\&lt;yyyyMMdd&gt;\project-assets-ui\wpf-*.png。
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            MergeAppResources(app);
            ViewModelLocationProvider.SetDefaultViewModelFactory(_ => null);

            var iconFont = (FontFamily)app.Resources["Icon"];
            var iconTypeface = new Typeface(iconFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            Console.WriteLine($"  [font] source={iconFont.Source} glyph={(iconTypeface.TryGetGlyphTypeface(out var glyph) ? glyph.FontUri.ToString() : "NOT_FOUND")}");

            string dir = OutputDirectory(args);
            Directory.CreateDirectory(dir);

            int failures = 0;
            try
            {
                var workspace = new WorkspaceContext();
                var solutionService = new SolutionService();
                var solutionPath = @"D:\C#\VM\解决方案\智能装配检测线\智能装配检测线.vms";
                var load = solutionService.LoadAsync(solutionPath).GetAwaiter().GetResult();
                if (!load.Success || load.Data == null)
                    throw new InvalidOperationException("加载真实方案失败：" + load.Message);
                load.Data.SolutionFilePath = solutionPath;
                workspace.SwitchSolution(load.Data);

                var vm = new ProjectAssetsViewModel(workspace);
                Check(vm.TypeTabs.First(t => t.Kind == "calibration").Count == 1,
                    "真实标定资产数量 = 1", ref failures);
                Check(vm.TypeTabs.First(t => t.Kind == "template").Count == 0,
                    "真实模板资产数量 = 0", ref failures);
                Check(vm.TypeTabs.First(t => t.Kind == "model").Count == 0,
                    "真实模型资产数量 = 0", ref failures);
                Check(vm.TypeTabs.First(t => t.Kind == "recipe").Count == 1,
                    "真实配方资产数量 = 1（BeadA 内嵌）", ref failures);
                Check(vm.TypeTabs.First(t => t.Kind == "image").Count == 14,
                    "真实图像资产数量 = 14（流程引用）", ref failures);
                RunImportChecks(ref failures);
                var view = new ProjectAssetsView
                {
                    DataContext = vm,
                    Width = 1280,
                    Height = 850,
                };

                var window = new Window
                {
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    SizeToContent = SizeToContent.Manual,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -4000,
                    Top = -4000,
                    Width = 1280,
                    Height = 850,
                    ShowInTaskbar = false,
                    Content = view,
                };

                window.Show();
                Pump(800);
                view.UpdateLayout();

                CheckSize(view, 1280, 850, ref failures);

                var list = FindDescendants<ListBox>(view)
                    .FirstOrDefault(l => ReferenceEquals(l.ItemsSource, vm.VisibleAssets));
                Check(list != null, "资产列表存在（ItemsSource=VisibleAssets）", ref failures);
                if (list == null)
                    return Finish(dir, failures);

                var asset = vm.VisibleAssets.FirstOrDefault(a => a.Name == "LineCalib")
                            ?? vm.VisibleAssets.FirstOrDefault();
                Check(asset != null, "列表有可测资产", ref failures);
                if (asset == null)
                    return Finish(dir, failures);

                list.SelectedItem = asset;
                list.ScrollIntoView(asset);
                list.UpdateLayout();
                view.UpdateLayout();
                Pump(250);

                var row = list.ItemContainerGenerator.ContainerFromItem(asset) as ListBoxItem;
                Check(row != null, "第一行容器已生成", ref failures);
                if (row == null)
                    return Finish(dir, failures);

                var typeChip = FindChip(row, asset.TypeLabel);
                var statusChip = FindChip(row, asset.Status);
                CheckChip("类型", typeChip, asset.TypeLabel, row.ActualHeight, ref failures);
                CheckChip("状态", statusChip, asset.Status, row.ActualHeight, ref failures);

                var full = Render(view, (int)Math.Round(view.ActualWidth), (int)Math.Round(view.ActualHeight), 2.0);
                Save(full, Path.Combine(dir, "wpf-project-assets.png"));
                Console.WriteLine($"  [render] {Path.Combine(dir, "wpf-project-assets.png")}");

                var rowBitmap = Render(row, (int)Math.Round(row.ActualWidth), (int)Math.Round(row.ActualHeight), 2.0);
                Save(rowBitmap, Path.Combine(dir, "wpf-project-assets-row.png"));
                Console.WriteLine($"  [render] {Path.Combine(dir, "wpf-project-assets-row.png")}");

                foreach (var kind in new[] { "calibration", "template", "model", "recipe", "image" })
                {
                    vm.SelectTypeCommand.Execute(kind);
                    view.UpdateLayout();
                    Pump(180);
                    var tabBitmap = Render(view, (int)Math.Round(view.ActualWidth), (int)Math.Round(view.ActualHeight), 2.0);
                    var tabPath = Path.Combine(dir, $"wpf-project-assets-{kind}.png");
                    Save(tabBitmap, tabPath);
                    Console.WriteLine($"  [render] {tabPath}");
                }

                window.Close();
                Pump(100);
                return Finish(dir, failures);
            }
            catch (Exception ex)
            {
                Console.WriteLine("RESULT: FAIL");
                Console.WriteLine(ex);
                return 1;
            }
        }

        private static void RunImportChecks(ref int failures)
        {
            var temp = Path.Combine(Path.GetTempPath(), "vm-asset-probe-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temp);
                var sourceDir = Path.Combine(temp, "src");
                Directory.CreateDirectory(sourceDir);

                var solution = new SolutionModel
                {
                    SolutionName = "AssetProbe",
                    SolutionFilePath = Path.Combine(temp, "AssetProbe.vms"),
                };
                var workspace = new WorkspaceContext();
                workspace.SwitchSolution(solution);
                var importer = new ProjectAssetImporter(workspace);

                var imageSource = Path.Combine(sourceDir, "probe.png");
                File.Copy(@"D:\C#\VM\解决方案\智能装配检测线\Image\board_01.png", imageSource);

                var templateSource = Path.Combine(sourceDir, "probe-template.json");
                File.WriteAllText(templateSource, "{\"Name\":\"probe\"}");
                var modelSource = Path.Combine(sourceDir, "probe-model.onnx");
                File.WriteAllText(modelSource, "probe-model");

                Check(importer.TryImport(imageSource, "image", out var imageId, out var imageError),
                    "图像导入成功" + (string.IsNullOrEmpty(imageError) ? string.Empty : "：" + imageError), ref failures);
                Check(solution.ImageAssets.Count == 1, "图像资产写入方案集合", ref failures);
                if (solution.ImageAssets.Count == 1)
                {
                    var image = solution.ImageAssets[0];
                    Check(image.Width == 1280 && image.Height == 960, "图像尺寸读取 1280×960", ref failures);
                    Check(!string.IsNullOrWhiteSpace(image.ContentHash), "图像 SHA-256 已写入", ref failures);
                    Check(File.Exists(Path.Combine(temp, image.RelativePath)), "图像文件已复制到方案目录", ref failures);
                }

                Check(!importer.TryImport(imageSource, "image", out _, out _), "重复内容被拒绝", ref failures);
                Check(importer.TryImport(templateSource, "template", out _, out var templateError),
                    "模板导入成功" + (string.IsNullOrEmpty(templateError) ? string.Empty : "：" + templateError), ref failures);
                Check(importer.TryImport(modelSource, "model", out _, out var modelError),
                    "模型导入成功" + (string.IsNullOrEmpty(modelError) ? string.Empty : "：" + modelError), ref failures);
                Check(solution.TemplateAssets.Count == 1, "模板资产写入方案集合", ref failures);
                Check(solution.ModelAssets.Count == 1, "模型资产写入方案集合", ref failures);

                var resolver = new SolutionProjectAssetResolver(solution);
                Check(resolver.TryResolve(solution.ModelAssets[0].AssetId, out var modelRef, out var modelResolveError)
                      && modelRef != null && File.Exists(modelRef.AbsolutePath),
                    "模型 AssetId 解析到方案文件" + (string.IsNullOrEmpty(modelResolveError) ? string.Empty : "：" + modelResolveError), ref failures);
                Check(resolver.TryResolve(solution.TemplateAssets[0].AssetId, out var templateRef, out var templateResolveError)
                      && templateRef != null && File.Exists(templateRef.AbsolutePath),
                    "模板 AssetId 解析到方案文件" + (string.IsNullOrEmpty(templateResolveError) ? string.Empty : "：" + templateResolveError), ref failures);
                Check(resolver.TryResolve(solution.ImageAssets[0].AssetId, out var imageRef, out var imageResolveError)
                      && imageRef != null && File.Exists(imageRef.AbsolutePath),
                    "图像 AssetId 解析到方案文件" + (string.IsNullOrEmpty(imageResolveError) ? string.Empty : "：" + imageResolveError), ref failures);

                var json = SolutionService.Serialize(solution);
                Check(json.Contains("\"TemplateAssets\"", StringComparison.Ordinal)
                      && json.Contains("\"ModelAssets\"", StringComparison.Ordinal)
                      && json.Contains("\"ImageAssets\"", StringComparison.Ordinal),
                    "新资产集合进入 .vms 序列化", ref failures);

                var round = JsonConvert.DeserializeObject<SolutionModel>(json, SolutionService.SerializationSettings);
                Check(round != null
                      && round.TemplateAssets.Count == 1
                      && round.ModelAssets.Count == 1
                      && round.ImageAssets.Count == 1,
                    "新资产集合可从 .vms 反序列化", ref failures);
            }
            catch (Exception ex)
            {
                Check(false, "导入断言异常：" + ex.Message, ref failures);
            }
            finally
            {
                try { Directory.Delete(temp, recursive: true); } catch { }
            }
        }

        private static int Finish(string dir, int failures)
        {
            if (failures == 0)
            {
                Console.WriteLine($"RESULT: OK（列表徽标 22px、文字水平/垂直居中；出图 {dir}）");
                return 0;
            }

            Console.WriteLine($"RESULT: FAIL（{failures} 项断言未通过）");
            return 1;
        }

        private static void CheckSize(FrameworkElement view, int width, int height, ref int failures)
        {
            bool ok = Math.Abs(view.ActualWidth - width) <= 1 && Math.Abs(view.ActualHeight - height) <= 1;
            Check(ok, $"视图尺寸 {width}x{height}，实际 {view.ActualWidth:0.#}x{view.ActualHeight:0.#}", ref failures);
        }

        private static void CheckChip(
            string label, Border chip, string expectedText, double rowHeight, ref int failures)
        {
            if (chip == null)
            {
                Check(false, $"{label}徽标存在（文本「{expectedText}」）", ref failures);
                return;
            }

            var text = FindDescendants<TextBlock>(chip).FirstOrDefault();
            if (text == null)
            {
                Check(false, $"{label}徽标内有文本", ref failures);
                return;
            }

            var origin = text.TransformToAncestor(chip).Transform(new Point(0, 0));
            double topGap = origin.Y - chip.BorderThickness.Top;
            double bottomGap = chip.ActualHeight - chip.BorderThickness.Bottom - (origin.Y + text.ActualHeight);
            double leftGap = origin.X - chip.BorderThickness.Left;
            double rightGap = chip.ActualWidth - chip.BorderThickness.Right - (origin.X + text.ActualWidth);

            bool heightOk = Math.Abs(chip.ActualHeight - 22) <= 0.5;
            bool notRowTall = chip.ActualHeight <= 24 && chip.ActualHeight < rowHeight - 10;
            bool verticalOk = Math.Abs(topGap - bottomGap) <= 1.0;
            bool horizontalOk = Math.Abs(leftGap - rightGap) <= 1.0;
            bool textFits = text.ActualWidth > 0 && text.ActualHeight > 0
                            && chip.ActualWidth >= text.ActualWidth
                            && chip.ActualHeight >= text.ActualHeight;
            bool widthTight = chip.ActualWidth <= text.ActualWidth + 2 * 8 + 2 + 1;

            Console.WriteLine(
                $"  [{label}] chip={chip.ActualWidth:0.#}x{chip.ActualHeight:0.#} row={rowHeight:0.#} " +
                $"text={text.ActualWidth:0.#}x{text.ActualHeight:0.#} " +
                $"gaps(v)={topGap:0.##}/{bottomGap:0.##} gaps(h)={leftGap:0.##}/{rightGap:0.##}");

            Check(heightOk, $"{label}徽标高度 22（实际 {chip.ActualHeight:0.#}）", ref failures);
            Check(notRowTall, $"{label}徽标未随行高拉伸（行 {rowHeight:0.#}）", ref failures);
            Check(verticalOk, $"{label}文字垂直居中（上/下 {topGap:0.##}/{bottomGap:0.##}）", ref failures);
            Check(horizontalOk, $"{label}文字水平居中（左/右 {leftGap:0.##}/{rightGap:0.##}）", ref failures);
            Check(textFits, $"{label}文字完整落在框内", ref failures);
            Check(widthTight, $"{label}背景框未额外放大（框 {chip.ActualWidth:0.#} / 文本 {text.ActualWidth:0.#}）", ref failures);
        }

        private static Border FindChip(DependencyObject root, string text)
        {
            var block = FindDescendants<TextBlock>(root).FirstOrDefault(t => t.Text == text);
            if (block == null) return null;

            DependencyObject current = block;
            while (current != null && current is not Border)
                current = VisualTreeHelper.GetParent(current);
            return current as Border;
        }

        private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) yield break;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match) yield return match;
                foreach (var descendant in FindDescendants<T>(child))
                    yield return descendant;
            }
        }

        private static void Check(bool ok, string message, ref int failures)
        {
            Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {message}");
            if (!ok) failures++;
        }

        private static string OutputDirectory(string[] args)
        {
            var @out = args.FirstOrDefault(a => a.StartsWith("--out=", StringComparison.Ordinal));
            if (@out != null) return @out.Substring("--out=".Length);

            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "VisionMaster.sln")))
                dir = dir.Parent;

            string root = dir?.FullName ?? AppContext.BaseDirectory;
            return Path.Combine(root, "artifacts", "render", DateTime.Now.ToString("yyyyMMdd"), "project-assets-ui");
        }

        /// <summary>与 VisionMaster/App.xaml 的 MergedDictionaries 同序同份数（含 Icon 字体键）。</summary>
        private static void MergeAppResources(Application app)
        {
            app.Resources["Icon"] = new FontFamily(
                new Uri("pack://application:,,,/UI;component/Asserts/FontFamilys/"),
                "./#Font Awesome 6 Pro Solid");

            string[] sources =
            {
                "/Core.Halcon;component/Generic.xaml",
                "pack://application:,,,/UI;component/Themes/Generic.xaml",
                "/VM.Scada.Controls;component/Themes/ScadaIcons.xaml",
                "pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.xaml",
                "/UI;component/Themes/Controls/Input.xaml",
            };

            foreach (var source in sources)
            {
                try
                {
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri(source, UriKind.RelativeOrAbsolute),
                    });
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"资源字典「{source}」合并失败，出图不可信：{ex.Message}", ex);
                }
            }

            if (app.Resources.MergedDictionaries.Count != sources.Length)
                throw new InvalidOperationException(
                    $"资源字典应为 {sources.Length} 份，实际 {app.Resources.MergedDictionaries.Count} 份");
        }

        private static RenderTargetBitmap Render(Visual visual, int width, int height, double scale)
        {
            var bitmap = new RenderTargetBitmap(
                Math.Max(1, (int)Math.Round(width * scale)),
                Math.Max(1, (int)Math.Round(height * scale)),
                96 * scale,
                96 * scale,
                PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        private static void Save(BitmapSource bitmap, string path)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
        }

        private static void Pump(int milliseconds)
        {
            var frame = new DispatcherFrame();
            var stopper = new DispatcherTimer(DispatcherPriority.Send)
            {
                Interval = TimeSpan.FromMilliseconds(milliseconds),
            };
            stopper.Tick += (_, _) =>
            {
                stopper.Stop();
                frame.Continue = false;
            };
            stopper.Start();
            Dispatcher.PushFrame(frame);
        }
    }
}
