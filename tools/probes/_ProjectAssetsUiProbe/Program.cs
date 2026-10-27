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
using Plugin.Motion.Virtual;
using Prism.Dialogs;
using Prism.Mvvm;
using Core.Interfaces;
using Core.Interfaces.Assets;
using HalconDotNet;
using VisionMaster.Models;
using VisionMaster.Models.Assets;
using VisionMaster.Scada;
using VisionMaster.Services;
using VisionMaster.Services.Motion;
using VisionMaster.ViewModels;
using VisionMaster.ViewModels.DialogViewModels;
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
                // 方案目录是活的（会被人改名/换样例），不能硬编码某一个文件名：
                // 取仓库里最新的一个 .vms 当"真实方案"，找不到才判红。
                var solutionPath = FindRealSolutionPath();
                if (solutionPath == null)
                    throw new InvalidOperationException("找不到真实方案：解决方案\\ 下没有任何 .vms");
                var load = solutionService.LoadAsync(solutionPath).GetAwaiter().GetResult();
                if (!load.Success || load.Data == null)
                    throw new InvalidOperationException("加载真实方案失败：" + load.Message);
                load.Data.SolutionFilePath = solutionPath;
                workspace.SwitchSolution(load.Data);
                Console.WriteLine("  [solution] " + solutionPath);

                var vm = new ProjectAssetsViewModel(workspace);
                var total = vm.TypeTabs.Where(t => t.Kind != "all").Sum(t => t.Count);
                Check(total > 0, $"真实方案里能读到资产（{total} 个）", ref failures);
                Check(vm.TypeTabs.First(t => t.Kind == "all").Count == total,
                    "资产总览数 = 各页签之和", ref failures);
                Check(vm.VisibleAssets.Count >= 0 && vm.TypeTabs.Count >= 6,
                    "页签齐全（总览 + 五类）", ref failures);
                RunImportChecks(ref failures);
                RunLifecycleChecks(ref failures);
                RunFilterSortChecks(ref failures);
                RunLifecycleFilterChecks(ref failures);
                RunReferenceNavigationChecks(ref failures);
                RunExportChecks(ref failures);
                RunArchiveDeleteChecks(ref failures);
                RunArchivedRuntimeChecks(ref failures);
                RunRecipeLifecycleChecks(ref failures);
                RunTextRecipeChecks(ref failures);
                RunFileVersionChecks(ref failures);
                RunVersionPruneChecks(ref failures);
                RunRecipeConflictChecks(ref failures);
                RunReverseReferenceChecks(ref failures);
                RunPluginAssetContractChecks(ref failures);
                RunPluginAssetRuntimeChecks(ref failures);
                RunSoftwareOperationLogChecks(ref failures);
                RunSolutionAssetHistoryChecks(ref failures);
                RunDiffChecks(ref failures);
                RunAuditRowChecks(ref failures);
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

                // 默认页签可能正好是空的（样例方案换来换去），先切到第一个有资产的页签再取列表。
                if (vm.VisibleAssets.Count == 0)
                {
                    var firstWithData = vm.TypeTabs
                        .Where(t => t.Kind != "all" && t.Count > 0)
                        .Select(t => t.Kind)
                        .FirstOrDefault();
                    if (firstWithData != null) vm.SelectTypeCommand.Execute(firstWithData);
                    view.UpdateLayout();
                    Pump(200);
                }

                var list = FindDescendants<ListBox>(view)
                    .FirstOrDefault(l => ReferenceEquals(l.ItemsSource, vm.VisibleAssets));
                Check(list != null, "资产列表存在（ItemsSource=VisibleAssets）", ref failures);
                if (list == null)
                    return Finish(dir, failures);

                var asset = vm.VisibleAssets.FirstOrDefault();
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

                // 引用页签：标定资产在真实方案里有 2 条引用，用来确认"引用卡可点"
                vm.SelectTypeCommand.Execute("calibration");
                vm.SelectedDetailTabIndex = 2;
                view.UpdateLayout();
                Pump(200);
                var refBitmap = Render(view, (int)Math.Round(view.ActualWidth), (int)Math.Round(view.ActualHeight), 2.0);
                var refPath = Path.Combine(dir, "wpf-project-assets-references.png");
                Save(refBitmap, refPath);
                Console.WriteLine($"  [render] {refPath}");

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

        /// <summary>仓库里最新的一个真实方案（`解决方案\**\*.vms`）；找不到返回 null。</summary>
        private static string? FindRealSolutionPath()
        {
            var root = Path.Combine(@"D:\C#\VM", "解决方案");
            if (!Directory.Exists(root)) return null;

            return Directory.GetFiles(root, "*.vms", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        /// <summary>筛选与排序必须真的改变 VisibleAssets，而不是只换按钮文案。</summary>
        private static void RunFilterSortChecks(ref int failures)
        {
            var temp = Path.Combine(Path.GetTempPath(), "vm-asset-filter-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temp);
                var sourceDir = Path.Combine(temp, "src");
                Directory.CreateDirectory(sourceDir);

                // 自带夹具：真实方案的内容会变（样例方案换来换去），
                // 筛选断言不该依赖"当前那个方案刚好有标定/相机"。
                var solution = new SolutionModel
                {
                    SolutionName = "FilterSort",
                    SolutionFilePath = Path.Combine(temp, "FilterSort.vms"),
                };
                solution.Calibrations.Add(new CalibrationAsset
                {
                    Name = "相机A标定",
                    Transform = new CalibrationTransform { MmPerPixel = 0.05, SourceImageWidth = 640, SourceImageHeight = 480, CameraSerial = "CAM-A" },
                });
                solution.Calibrations.Add(new CalibrationAsset
                {
                    Name = "相机B标定",
                    Transform = new CalibrationTransform { MmPerPixel = 0.1, SourceImageWidth = 640, SourceImageHeight = 480, CameraSerial = "CAM-B" },
                });

                var workspace = new WorkspaceContext();
                workspace.SwitchSolution(solution);

                var importer = new ProjectAssetImporter(workspace);
                var png = Path.Combine(sourceDir, "f1.png");
                File.Copy(@"D:\C#\VM\Image\board\board_01.png", png);
                importer.TryImport(png, "image", out _, out _);

                var vm = new ProjectAssetsViewModel(workspace);

                vm.SelectTypeCommand.Execute("image");
                Check(vm.VisibleAssets.Count > 0, $"图像页有 {vm.VisibleAssets.Count} 条可筛选", ref failures);
                Check(vm.CameraFilterOptions.Count == 1,
                    $"图像资产没有相机值时相机下拉只有「全部」（实际 {vm.CameraFilterOptions.Count}）", ref failures);
                Check(vm.CameraFilterOptions.First().Label == "全部相机",
                    "下拉首项文案是「全部相机」（不是空白）", ref failures);
                Check(vm.SelectedCameraOption?.Label == "全部相机",
                    "默认选中「全部相机」而不是空", ref failures);

                vm.SelectTypeCommand.Execute("calibration");
                Check(vm.VisibleAssets.Count == 2, $"标定页有 2 条（实际 {vm.VisibleAssets.Count}）", ref failures);
                var cameraOptions = vm.CameraFilterOptions.Select(o => o.Value).ToArray();
                Check(cameraOptions.Contains("CAM-A") && cameraOptions.Contains("CAM-B"),
                    "相机下拉来自资产真实相机值（不是硬编码）", ref failures);

                vm.CameraFilter = "CAM-A";
                Check(vm.VisibleAssets.Count == 1 && vm.VisibleAssets.All(a => a.Camera == "CAM-A"),
                    "相机筛选真的过滤了列表", ref failures);

                vm.CameraFilter = "";
                var baseline = vm.VisibleAssets.Count;

                var tagOptions = vm.TagFilterOptions.Select(o => o.Value).Where(v => v.Length > 0).ToArray();
                Check(tagOptions.Length > 0, "标签下拉来自作用域", ref failures);
                Check(vm.TagFilterOptions.First().Label == "全部标签",
                    "标签下拉首项文案是「全部标签」", ref failures);
                if (tagOptions.Length > 0)
                {
                    vm.TagFilter = tagOptions[0];
                    Check(vm.VisibleAssets.Count > 0, $"标签筛选 {tagOptions[0]} 后仍有数据", ref failures);
                }

                vm.ClearFiltersCommand.Execute();
                Check(vm.CameraFilter.Length == 0 && vm.TagFilter.Length == 0 && vm.SearchText.Length == 0,
                    "清除筛选命令清空三处条件", ref failures);
                Check(vm.VisibleAssets.Count == baseline, "清除筛选后恢复全量", ref failures);

                var beforeSort = vm.VisibleAssets.Select(a => a.Name).ToArray();
                vm.ToggleSortCommand.Execute();
                Check(vm.SortButtonText == "名称排序", "排序按钮文案切到「名称排序」", ref failures);
                var afterSort = vm.VisibleAssets.Select(a => a.Name).ToArray();
                Check(afterSort.SequenceEqual(afterSort.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)),
                    "名称排序结果确实是升序", ref failures);
                Check(beforeSort.Length == afterSort.Length, "排序不丢条目", ref failures);
            }
            catch (Exception ex)
            {
                Check(false, "筛选/排序断言异常：" + ex.Message, ref failures);
            }
            finally
            {
                try { Directory.Delete(temp, recursive: true); } catch { }
            }
        }

        /// <summary>
        /// 左侧「有效 / 待处理 / 已归档」必须是真的筛选器，且**筛选与计数共用同一份归桶判据**。
        /// 判据：点「已归档」后列表只剩归档项、计数与条数对得上、再点一次回到全部。
        /// </summary>
        private static void RunLifecycleFilterChecks(ref int failures)
        {
            var temp = Path.Combine(Path.GetTempPath(), "vm-asset-lifefilter-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temp);
                var sourceDir = Path.Combine(temp, "src");
                Directory.CreateDirectory(sourceDir);

                var solution = new SolutionModel
                {
                    SolutionName = "AssetLifeFilter",
                    SolutionFilePath = Path.Combine(temp, "AssetLifeFilter.vms"),
                };
                var workspace = new WorkspaceContext();
                workspace.SwitchSolution(solution);

                var importer = new ProjectAssetImporter(workspace);
                var a = Path.Combine(sourceDir, "a.png");
                var b = Path.Combine(sourceDir, "b.png");
                File.Copy(@"D:\C#\VM\Image\board\board_01.png", a);
                File.Copy(@"D:\C#\VM\Image\board\board_02.png", b);
                importer.TryImport(a, "image", out var idA, out _);
                importer.TryImport(b, "image", out var idB, out _);

                var policy = new ScadaAccessPolicy();
                var service = new ProjectAssetService(
                    new SolutionAssetWriter(solution), policy,
                    new ScadaAuditWriter(Path.Combine(temp, "Audit")));
                policy.Login(new ScadaUserInfo(
                    "admin", ScadaGroupDefaults.AdminTemplateId,
                    ScadaPermissionKeys.BuiltInKeys.ToArray(), "模板·管理员"));
                service.TrySetArchived(idB, true, out _);

                var vm = new ProjectAssetsViewModel(workspace);
                vm.SelectTypeCommand.Execute("image");
                var all = vm.VisibleAssets.Count;
                Check(all == 2, $"全部图像 {all} 条", ref failures);
                Check(vm.LifecycleFilter == "全部", "默认筛选是「全部」", ref failures);

                vm.SelectLifecycleCommand.Execute("已归档");
                Check(vm.LifecycleFilter == "已归档", "点「已归档」后筛选生效", ref failures);
                Check(vm.VisibleAssets.Count == 1 && vm.VisibleAssets.All(x => x.Status == "已归档"),
                    $"只剩归档项（实际 {vm.VisibleAssets.Count}）", ref failures);
                Check(vm.ArchivedCount == vm.VisibleAssets.Count,
                    $"归档计数与列表条数一致（{vm.ArchivedCount} vs {vm.VisibleAssets.Count}）", ref failures);

                vm.SelectLifecycleCommand.Execute("有效");
                Check(vm.VisibleAssets.Count == 1 && vm.VisibleAssets.All(x => x.Status == "有效"),
                    "点「有效」只剩有效项", ref failures);
                Check(vm.ValidCount == 1, $"有效计数 1（实际 {vm.ValidCount}）", ref failures);

                // 再点同一个桶 = 取消筛选。
                vm.SelectLifecycleCommand.Execute("有效");
                Check(vm.LifecycleFilter == "全部" && vm.VisibleAssets.Count == all,
                    "再点同一个桶回到全部", ref failures);

                // 待处理桶：没有待处理资产时必须筛出 0 条，而不是把全部当待处理。
                vm.SelectLifecycleCommand.Execute("待处理");
                Check(vm.VisibleAssets.Count == 0 && vm.PendingCount == 0,
                    $"没有待处理资产时筛出 0 条（实际 {vm.VisibleAssets.Count}）", ref failures);

                // 清除筛选必须把生命周期一起清掉。
                vm.SelectLifecycleCommand.Execute("已归档");
                vm.ClearFiltersCommand.Execute();
                Check(vm.LifecycleFilter == "全部" && vm.VisibleAssets.Count == all,
                    "清除筛选同时复位生命周期", ref failures);
            }
            catch (Exception ex)
            {
                Check(false, "生命周期筛选断言异常：" + ex.Message, ref failures);
            }
            finally
            {
                try { Directory.Delete(temp, recursive: true); } catch { }
            }
        }

        /// <summary>
        /// 引用卡必须能跳到真实步骤：流程/步骤都对得上、容器分支里的步骤也能找到。
        /// 找不到时必须说清原因，不许静默什么都不做。
        /// </summary>
        private static void RunReferenceNavigationChecks(ref int failures)
        {
            try
            {
                // 自带夹具：造一个"标定步骤按 CatalogAssetId 引用标定资产"的方案。
                // 依赖真实方案的话，换一个样例方案这条断言就跟着失效（2026-10-27 就发生了）。
                var solution = new SolutionModel { SolutionName = "RefNav" };
                var calibration = new CalibrationAsset
                {
                    Name = "跳转标定",
                    Transform = new CalibrationTransform { MmPerPixel = 0.05, SourceImageWidth = 640, SourceImageHeight = 480 },
                };
                solution.Calibrations.Add(calibration);

                var step = new WaitStep("", "标定", "Plugin.Calibration.CalibrationPlugin, Plugin.Calibration", "标定计算");
                step.InputValues["CatalogAssetId"] = calibration.CalibrationId.ToString();
                step.InputValues["UseSavedCalibration"] = true;
                var flow = new FlowModel { FlowName = "F_RefNav" };
                flow.Steps.Add(step);
                solution.Flows.Add(flow);

                var workspace = new WorkspaceContext();
                workspace.SwitchSolution(solution);

                var vm = new ProjectAssetsViewModel(workspace);
                vm.SelectTypeCommand.Execute("calibration");

                var asset = vm.VisibleAssets.FirstOrDefault();
                Check(asset != null && asset.References.Count > 0,
                    $"标定资产有 {asset?.References.Count ?? 0} 条引用", ref failures);

                var reference = asset?.References.FirstOrDefault(r => r.CanNavigate);
                Check(reference != null, "引用带可跳转的流程/步骤名", ref failures);
                if (reference == null) return;

                IDialogResult? closeResult = null;
                AttachCloseListener(vm, r => closeResult = r);
                vm.GoToReferenceCommand.Execute(reference);

                Check(closeResult != null, "跳转后弹窗请求关闭（用户才看得到选中步骤）", ref failures);
                Check(workspace.CurrentFlow != null
                      && string.Equals(workspace.CurrentFlow.FlowName, reference.FlowName, StringComparison.Ordinal),
                    $"当前流程切到 {reference.FlowName}（实际 {workspace.CurrentFlow?.FlowName}）", ref failures);
                Check(workspace.CurrentStep != null
                      && string.Equals(workspace.CurrentStep.StepName, reference.StepName, StringComparison.Ordinal),
                    $"当前步骤选中 {reference.StepName}（实际 {workspace.CurrentStep?.StepName}）", ref failures);

                // 找不到时必须给出原因，不能静默。
                var bogus = new AssetReference("flow", "不存在 / 也不存在", "读取", "NoSuchFlow", "NoSuchStep");
                vm.GoToReferenceCommand.Execute(bogus);
                Check(vm.ActionMessage.Contains("找不到流程"),
                    $"流程不存在时说明原因（实际：{vm.ActionMessage}）", ref failures);
            }
            catch (Exception ex)
            {
                Check(false, "引用跳转断言异常：" + ex.Message, ref failures);
            }
        }

        /// <summary>
        /// DialogCloseListener 的构造函数是 internal（Prism 的设计），断言里只能反射造一个。
        /// 与 RecipeChecks.MakeDialogCloseListener 同一手法。
        /// </summary>
        private static void AttachCloseListener(
            ProjectAssetsViewModel vm, Action<IDialogResult> onClosed)
        {
            var property = typeof(ProjectAssetsViewModel).GetProperty(nameof(ProjectAssetsViewModel.RequestClose))!;
            var ctor = property.PropertyType.GetConstructor(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(Action<IDialogResult>) },
                modifiers: null);

            if (ctor == null)
                throw new MissingMethodException(property.PropertyType.FullName, ".ctor(Action<IDialogResult>)");

            property.SetValue(vm, ctor.Invoke(new object[] { onClosed }));
        }

        /// <summary>导出必须真的产出 zip，且里面含 manifest 与文件本体。</summary>
        private static void RunExportChecks(ref int failures)
        {
            var temp = Path.Combine(Path.GetTempPath(), "vm-asset-export-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temp);
                var sourceDir = Path.Combine(temp, "src");
                Directory.CreateDirectory(sourceDir);

                var solution = new SolutionModel
                {
                    SolutionName = "AssetExport",
                    SolutionFilePath = Path.Combine(temp, "AssetExport.vms"),
                };
                var workspace = new WorkspaceContext();
                workspace.SwitchSolution(solution);

                var importer = new ProjectAssetImporter(workspace);
                var imageSource = Path.Combine(sourceDir, "board.png");
                File.Copy(@"D:\C#\VM\Image\board\board_01.png", imageSource);
                Check(importer.TryImport(imageSource, "image", out var imageId, out _),
                    "导出前置：图像导入", ref failures);

                var exporter = new ProjectAssetExporter(workspace);
                var zipPath = Path.Combine(temp, "package.zip");
                Check(exporter.TryExport(new[] { imageId }, zipPath, out var fileCount, out var manifest, out var error),
                    "导出资产包成功" + (string.IsNullOrEmpty(error) ? string.Empty : "：" + error), ref failures);
                Check(File.Exists(zipPath), "zip 文件已生成", ref failures);
                Check(fileCount == 1, $"zip 含 1 个文件（实际 {fileCount}）", ref failures);
                Check(manifest.Contains("manifest") == false && manifest.Contains("SHA-256"),
                    "manifest 含 SHA-256 列", ref failures);

                using (var archive = System.IO.Compression.ZipFile.OpenRead(zipPath))
                {
                    Check(archive.GetEntry("manifest.tsv") != null, "zip 含 manifest.tsv", ref failures);
                    Check(archive.Entries.Any(e => e.FullName.StartsWith("files/image/", StringComparison.OrdinalIgnoreCase)),
                        "zip 含 files/image 文件本体", ref failures);
                }

                // 标定/配方没有独立文件，导出应明确拒绝而不是产出空包。
                Check(!exporter.TryExport(new[] { Guid.NewGuid() }, Path.Combine(temp, "none.zip"), out _, out _, out var emptyError),
                    "无资产时导出被拒绝", ref failures);
                Check(emptyError.Contains("没有可导出"), "拒绝原因说明没有可导出资产", ref failures);

                var thumb = ProjectAssetExporter.TryLoadThumbnail(Path.Combine(temp, "Image", "board.png"), 120);
                Check(thumb != null && thumb.PixelWidth <= 120, "缩略图可解码且已缩放", ref failures);
                Check(ProjectAssetExporter.TryLoadThumbnail(Path.Combine(temp, "nope.png")) == null,
                    "文件不存在时缩略图为 null", ref failures);
            }
            catch (Exception ex)
            {
                Check(false, "导出断言异常：" + ex.Message, ref failures);
            }
            finally
            {
                try { Directory.Delete(temp, recursive: true); } catch { }
            }
        }

        /// <summary>归档/恢复/删除：状态落方案、默认指针清理、文件不删。</summary>
        private static void RunArchiveDeleteChecks(ref int failures)
        {
            var temp = Path.Combine(Path.GetTempPath(), "vm-asset-archive-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temp);
                var sourceDir = Path.Combine(temp, "src");
                Directory.CreateDirectory(sourceDir);

                var solution = new SolutionModel
                {
                    SolutionName = "AssetArchive",
                    SolutionFilePath = Path.Combine(temp, "AssetArchive.vms"),
                };
                var workspace = new WorkspaceContext();
                workspace.SwitchSolution(solution);

                var importer = new ProjectAssetImporter(workspace);
                var imageSource = Path.Combine(sourceDir, "probe.png");
                File.Copy(@"D:\C#\VM\Image\board\board_01.png", imageSource);
                Check(importer.TryImport(imageSource, "image", out var imageId, out _),
                    "归档前置：图像导入", ref failures);

                var policy = new ScadaAccessPolicy();
                var auditDir = Path.Combine(temp, "Audit");
                var service = new ProjectAssetService(
                    new SolutionAssetWriter(solution), policy, new ScadaAuditWriter(auditDir));

                // 未登录：归档/删除都必须被拒。
                Check(!service.TrySetArchived(imageId, true, out var deniedArchive) && deniedArchive.Contains("权限"),
                    "未登录时归档被拒", ref failures);
                Check(!service.TryDelete(imageId, out _, out var deniedDelete) && deniedDelete.Contains("权限"),
                    "未登录时删除被拒", ref failures);

                policy.Login(new ScadaUserInfo(
                    "admin", ScadaGroupDefaults.AdminTemplateId,
                    ScadaPermissionKeys.BuiltInKeys.ToArray(), "模板·管理员"));

                Check(service.TrySetArchived(imageId, true, out var archiveError),
                    "归档成功" + (string.IsNullOrEmpty(archiveError) ? string.Empty : "：" + archiveError), ref failures);
                Check(solution.AssetLifecycle?.Any(s => s.AssetId == imageId && s.State == "已归档") == true,
                    "归档状态写入方案", ref failures);
                Check(solution.ImageAssets.First(a => a.AssetId == imageId).Version == 1,
                    "归档不改版本号", ref failures);

                // 目录读取必须把归档状态显示出来（生命周期优先）。
                var catalogItem = new SolutionProjectAssetCatalog(workspace)
                    .Build().FirstOrDefault(a => a.Id == imageId.ToString());
                Check(catalogItem?.Status == "已归档",
                    $"目录读取显示已归档（实际 {catalogItem?.Status}）", ref failures);

                Check(service.TrySetArchived(imageId, false, out var restoreError),
                    "恢复成功" + (string.IsNullOrEmpty(restoreError) ? string.Empty : "：" + restoreError), ref failures);
                Check(solution.AssetLifecycle.All(s => s.AssetId != imageId || s.State == "有效"),
                    "恢复后状态回到有效", ref failures);

                // 冻结时不许归档/删除。
                service.TryToggleFreeze(imageId, out _, out _);
                Check(!service.TrySetArchived(imageId, true, out var frozenArchive) && frozenArchive.Contains("冻结"),
                    "冻结时归档被拒", ref failures);
                Check(!service.TryDelete(imageId, out _, out var frozenDelete) && frozenDelete.Contains("冻结"),
                    "冻结时删除被拒", ref failures);
                service.TryToggleFreeze(imageId, out _, out _);

                // 设默认后再删除：默认指针必须一起清掉。
                service.TrySetDefault(imageId, out _);
                Check(solution.AssetDefaults?.Any(p => p.AssetId == imageId) == true,
                    "删除前置：默认指针存在", ref failures);

                var relativePath = solution.ImageAssets[0].RelativePath;
                var absolutePath = Path.Combine(temp, relativePath);
                Check(File.Exists(absolutePath), "删除前置：文件存在", ref failures);

                Check(service.TryDelete(imageId, out var deletedFile, out var deleteError),
                    "删除成功" + (string.IsNullOrEmpty(deleteError) ? string.Empty : "：" + deleteError), ref failures);
                Check(solution.ImageAssets.All(a => a.AssetId != imageId), "资产已从方案移除", ref failures);
                Check(solution.AssetDefaults?.All(p => p.AssetId != imageId) == true, "默认指针已清理", ref failures);
                Check(solution.AssetLifecycle?.All(s => s.AssetId != imageId) == true, "生命周期记录已清理", ref failures);
                Check(File.Exists(absolutePath), "磁盘文件仍保留（删除只移除方案记录）", ref failures);
                Check(!string.IsNullOrWhiteSpace(deletedFile) && deletedFile.Contains("Image"),
                    "返回值带上被保留的文件路径", ref failures);

                var json = SolutionService.Serialize(solution);
                Check(json.Contains("\"AssetLifecycle\""), "生命周期状态进入 .vms 序列化", ref failures);
            }
            catch (Exception ex)
            {
                Check(false, "归档/删除断言异常：" + ex.Message, ref failures);
            }
            finally
            {
                try { Directory.Delete(temp, recursive: true); } catch { }
            }
        }

        /// <summary>
        /// 配方也必须能冻结/解冻、能写版本快照。
        /// 旧实现的 FindFreezable 只覆盖标定与文件资产，配方点冻结会得到"找不到资产"。
        /// </summary>
        private static void RunRecipeLifecycleChecks(ref int failures)
        {
            var temp = Path.Combine(Path.GetTempPath(), "vm-asset-recipe-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temp);

                var solution = new SolutionModel
                {
                    SolutionName = "AssetRecipe",
                    SolutionFilePath = Path.Combine(temp, "AssetRecipe.vms"),
                };
                var recipe = new RecipeDefinition { Name = "R1", Code = "R1" };
                recipe.TouchEntries("probe");
                solution.EnsureRecipes().Add(recipe);

                var workspace = new WorkspaceContext();
                workspace.SwitchSolution(solution);

                var policy = new ScadaAccessPolicy();
                var service = new ProjectAssetService(
                    new SolutionAssetWriter(solution), policy,
                    new ScadaAuditWriter(Path.Combine(temp, "Audit")));
                policy.Login(new ScadaUserInfo(
                    "admin", ScadaGroupDefaults.AdminTemplateId,
                    ScadaPermissionKeys.BuiltInKeys.ToArray(), "模板·管理员"));

                Check(service.TryToggleFreeze(recipe.RecipeId, out var frozen, out var freezeError),
                    "配方可以冻结" + (string.IsNullOrEmpty(freezeError) ? string.Empty : "：" + freezeError), ref failures);
                Check(frozen && recipe.IsFrozen, "冻结状态写到配方本身", ref failures);
                Check(service.IsFrozen(recipe.RecipeId), "服务口径读到配方已冻结", ref failures);

                Check(service.TryToggleFreeze(recipe.RecipeId, out var unfrozen, out var unfreezeError),
                    "配方可以解冻" + (string.IsNullOrEmpty(unfreezeError) ? string.Empty : "：" + unfreezeError), ref failures);
                Check(!unfrozen && !recipe.IsFrozen, "解冻状态写回", ref failures);

                // 冻结后归档必须被拒（与其它四类同一判据）。
                service.TryToggleFreeze(recipe.RecipeId, out _, out _);
                Check(!service.TrySetArchived(recipe.RecipeId, true, out var frozenArchive)
                      && frozenArchive.Contains("冻结"),
                    "配方冻结时归档被拒", ref failures);
                service.TryToggleFreeze(recipe.RecipeId, out _, out _);

                // 版本记录：冻结/解冻/归档都应留下记录，且快照含配方条目而不是空串。
                var records = AssetVersionLog.Of(solution, recipe.RecipeId);
                Check(records.Count >= 2, $"配方版本记录有 {records.Count} 条", ref failures);
                Check(records.Any(r => r.Action == "冻结"), "版本记录含「冻结」", ref failures);
                Check(records.All(r => !string.IsNullOrWhiteSpace(r.SnapshotJson)),
                    "配方版本记录都带快照", ref failures);
                Check(records[0].SnapshotJson.Contains("ContentVersion"),
                    "配方快照含 ContentVersion 字段", ref failures);

                // 快照必须能被逐字段差异读出来（配方走的是同一套 diff）。
                var diffs = ProjectAssetService.DiffSnapshots(records[0].SnapshotJson, records[0].SnapshotJson);
                Check(diffs.Count > 0 && diffs.All(d => !d.Changed),
                    "配方快照自比无差异", ref failures);

                var catalogItem = new SolutionProjectAssetCatalog(workspace)
                    .Build().FirstOrDefault(a => a.Id == recipe.RecipeId.ToString());
                Check(catalogItem != null, "配方出现在资产目录", ref failures);
                Check(catalogItem?.IsFrozen == false, "目录如实反映「当前未冻结」", ref failures);

                // 冻结后目录必须跟着变（否则界面上的冻结徽标永远不亮）。
                service.TryToggleFreeze(recipe.RecipeId, out _, out _);
                var frozenItem = new SolutionProjectAssetCatalog(workspace)
                    .Build().FirstOrDefault(a => a.Id == recipe.RecipeId.ToString());
                Check(frozenItem?.IsFrozen == true, "目录读到配方冻结标记", ref failures);
                service.TryToggleFreeze(recipe.RecipeId, out _, out _);
            }
            catch (Exception ex)
            {
                Check(false, "配方生命周期断言异常：" + ex.Message, ref failures);
            }
            finally
            {
                try { Directory.Delete(temp, recursive: true); } catch { }
            }
        }

        /// <summary>
        /// 文本配方：导入 → 内容进方案 → 解析器给 text 内容 → 参数配方被明确拒绝。
        /// 这条是 ColorCheck 接 AssetId 的地基，所以要钉在宿主侧。
        /// </summary>
        private static void RunTextRecipeChecks(ref int failures)
        {
            var temp = Path.Combine(Path.GetTempPath(), "vm-asset-textrecipe-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temp);
                var source = Path.Combine(temp, "color.txt");
                File.WriteAllText(source, "红白线 = 红,白\r\n红黑线 = 红,黑\r\n", new UTF8Encoding(false));

                var solution = new SolutionModel
                {
                    SolutionName = "AssetTextRecipe",
                    SolutionFilePath = Path.Combine(temp, "AssetTextRecipe.vms"),
                };
                var workspace = new WorkspaceContext();
                workspace.SwitchSolution(solution);

                var importer = new ProjectAssetImporter(workspace);
                Check(importer.TryImport(source, "recipe", out var recipeId, out var importError),
                    "文本配方导入成功" + (string.IsNullOrEmpty(importError) ? string.Empty : "：" + importError), ref failures);

                var recipe = solution.RecipesOrEmpty().FirstOrDefault(r => r.RecipeId == recipeId);
                Check(recipe != null && recipe.TextContent.Contains("红白线"), "配方内容进方案（不拷文件）", ref failures);
                Check(!Directory.Exists(Path.Combine(temp, "Assets", "Recipes")), "没有多出一份配方文件目录", ref failures);

                // 同内容再导一次必须被拒（不能出两份一模一样的配方）。
                Check(!importer.TryImport(source, "recipe", out _, out var dupError) && dupError.Contains("已存在"),
                    "相同内容的文本配方被拒", ref failures);

                var resolver = new SolutionProjectAssetResolver(solution);
                Check(resolver.TryResolve(recipeId, out var assetRef, out _) && assetRef != null,
                    "文本配方可解析", ref failures);
                Check(assetRef?.ContentKind == "text" && assetRef.Content.Contains("红白线"),
                    "解析结果给 text 内容", ref failures);

                var context = new VisionMaster.Services.ExecutionContext(null!) { Workspace = workspace };
                Check(ProjectAssetPathResolver.TryResolveContent(context, recipeId.ToString(), "recipe", "text", out var content, out _)
                      && content.Contains("红黑线"),
                    "插件口径能拿到文本配方原文", ref failures);

                // 参数配方（无 TextContent）必须是 json 形态，按文本要会被明确拒绝。
                var paramRecipe = new RecipeDefinition { Name = "参数配方" };
                paramRecipe.TouchEntries("probe");
                solution.EnsureRecipes().Add(paramRecipe);
                Check(!ProjectAssetPathResolver.TryResolveContent(
                        context, paramRecipe.RecipeId.ToString(), "recipe", "text", out _, out var kindError)
                      && kindError.Contains("内容形态"),
                    "参数配方按文本解析被明确拒绝（不是静默给空串）", ref failures);

                // 导出必须带上文本配方内容（之前配方整体不参与打包）。
                var exporter = new ProjectAssetExporter(workspace);
                var zipPath = Path.Combine(temp, "pkg.zip");
                Check(exporter.TryExport(new[] { recipeId }, zipPath, out var fileCount, out _, out var exportError),
                    "文本配方可导出" + (string.IsNullOrEmpty(exportError) ? string.Empty : "：" + exportError), ref failures);
                Check(fileCount == 1, $"导出含 1 个配方文件（实际 {fileCount}）", ref failures);
                using (var archive = System.IO.Compression.ZipFile.OpenRead(zipPath))
                {
                    var entry = archive.Entries.FirstOrDefault(e => e.FullName.StartsWith("files/recipe/"));
                    Check(entry != null, "zip 含 files/recipe 条目", ref failures);
                    if (entry != null)
                    {
                        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                        Check(reader.ReadToEnd().Contains("红白线"), "zip 里的配方内容完整", ref failures);
                    }
                }

                // 资产页目录要显示"文本配方"，不是"0 条目 · 0 点位"。
                var item = new SolutionProjectAssetCatalog(workspace).Build()
                    .FirstOrDefault(a => a.Id == recipeId.ToString());
                Check(item?.PreviewSub.Contains("文本配方") == true,
                    $"目录预览标明文本配方（实际 {item?.PreviewSub}）", ref failures);
            }
            catch (Exception ex)
            {
                Check(false, "文本配方断言异常：" + ex.Message, ref failures);
            }
            finally
            {
                try { Directory.Delete(temp, recursive: true); } catch { }
            }
        }

        /// <summary>
        /// 文件资产版本：导入新版本 → 旧文件进版本库、活动文件换新 → 回滚能把旧内容真的拿回来。
        /// 判据看**文件内容**，不看版本号自增（版本号对了但文件没换是最坏的一种"看起来成功"）。
        /// </summary>
        private static void RunFileVersionChecks(ref int failures)
        {
            var temp = Path.Combine(Path.GetTempPath(), "vm-asset-version-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temp);
                var sourceDir = Path.Combine(temp, "src");
                Directory.CreateDirectory(sourceDir);

                var solution = new SolutionModel
                {
                    SolutionName = "AssetVersion",
                    SolutionFilePath = Path.Combine(temp, "AssetVersion.vms"),
                };
                var workspace = new WorkspaceContext();
                workspace.SwitchSolution(solution);

                var v1Source = Path.Combine(sourceDir, "v1.json");
                var v2Source = Path.Combine(sourceDir, "v2.json");
                File.WriteAllText(v1Source, "{\"Templates\":[\"v1\"]}", new UTF8Encoding(false));
                File.WriteAllText(v2Source, "{\"Templates\":[\"v2\"]}", new UTF8Encoding(false));

                var importer = new ProjectAssetImporter(workspace);
                Check(importer.TryImport(v1Source, "template", out var templateId, out var importError),
                    "版本前置：导入 v1" + (string.IsNullOrEmpty(importError) ? string.Empty : "：" + importError),
                    ref failures);

                var asset = solution.TemplateAssets.First(a => a.AssetId == templateId);
                var root = temp;
                var activePath = Path.Combine(root, asset.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                var extension = Path.GetExtension(asset.RelativePath);
                var v1VersionFile = Path.Combine(root, ProjectAssetImporter
                    .VersionFileRelativePath("template", templateId, 1, extension)
                    .Replace('/', Path.DirectorySeparatorChar));

                Check(File.Exists(v1VersionFile), "导入 v1 时就把文件收进版本库", ref failures);
                Check(File.ReadAllText(activePath).Contains("v1"), "活动文件是 v1 内容", ref failures);

                // 版本记录必须**在每次内容变化时立刻写**：事后再补会拿到当时的版本号，
                // 两条记录都变成 v2，"回滚到 v1"就无从选起。
                var policy = new ScadaAccessPolicy();
                var service = new ProjectAssetService(
                    new SolutionAssetWriter(solution), policy,
                    new ScadaAuditWriter(Path.Combine(temp, "Audit")));
                policy.Login(new ScadaUserInfo(
                    "admin", ScadaGroupDefaults.AdminTemplateId,
                    ScadaPermissionKeys.BuiltInKeys.ToArray(), "模板·管理员"));
                service.TryRecordVersion(templateId, "导入", "v1", bumpVersion: false, out _);

                // 同内容再导新版本必须被拒（不白占版本号）。
                Check(!importer.TryImportNewVersion(templateId, v1Source, out _, out var sameError)
                      && sameError.Contains("内容相同"),
                    "同内容导入新版本被拒", ref failures);

                Check(importer.TryImportNewVersion(templateId, v2Source, out var newVersion, out var newError),
                    "导入 v2 成功" + (string.IsNullOrEmpty(newError) ? string.Empty : "：" + newError), ref failures);
                service.TryRecordVersion(templateId, "新版本", "v2", bumpVersion: false, out _);
                Check(newVersion == 2, $"版本号递增到 2（实际 {newVersion}）", ref failures);
                Check(File.ReadAllText(activePath).Contains("v2"), "活动文件换成 v2 内容", ref failures);
                Check(File.ReadAllText(v1VersionFile).Contains("v1"), "v1 文件仍在版本库里且内容未变", ref failures);

                var v2VersionFile = Path.Combine(root, ProjectAssetImporter
                    .VersionFileRelativePath("template", templateId, 2, extension)
                    .Replace('/', Path.DirectorySeparatorChar));
                Check(File.Exists(v2VersionFile), "v2 也进版本库（回滚到 v2 才可能）", ref failures);

                var v1Record = AssetVersionLog.Of(solution, templateId)
                    .FirstOrDefault(r => r.Version == 1);
                Check(v1Record != null, "找得到 v1 的版本记录", ref failures);
                if (v1Record == null) return;

                Check(service.TryRollback(templateId, v1Record.RecordId, out var rollbackError),
                    "文件资产可回滚" + (string.IsNullOrEmpty(rollbackError) ? string.Empty : "：" + rollbackError),
                    ref failures);
                Check(File.ReadAllText(activePath).Contains("v1"),
                    "回滚后活动文件真的是 v1 内容（不是只改了版本号）", ref failures);
                Check(solution.TemplateAssets.First(a => a.AssetId == templateId).Version == 3,
                    "回滚本身算一个新版本（历史只追加）", ref failures);

                var v3VersionFile = Path.Combine(root, ProjectAssetImporter
                    .VersionFileRelativePath("template", templateId, 3, extension)
                    .Replace('/', Path.DirectorySeparatorChar));
                Check(File.Exists(v3VersionFile), "回滚结果也进版本库", ref failures);

                service.TryToggleFreeze(templateId, out _, out _);
                Check(!service.TryRollback(templateId, v1Record.RecordId, out var frozenError)
                      && frozenError.Contains("冻结"),
                    "冻结时回滚被拒", ref failures);
                service.TryToggleFreeze(templateId, out _, out _);
            }
            catch (Exception ex)
            {
                Check(false, "文件版本断言异常：" + ex.Message, ref failures);
            }
            finally
            {
                try { Directory.Delete(temp, recursive: true); } catch { }
            }
        }

        /// <summary>
        /// 版本库清理：只保留版本记录引用到的版本 + 当前版本。
        /// 判据：留着的文件必须覆盖每一条记录的版本；记录没引用的旧文件要被删掉。
        /// </summary>
        private static void RunVersionPruneChecks(ref int failures)
        {
            var temp = Path.Combine(Path.GetTempPath(), "vm-asset-prune-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temp);
                var sourceDir = Path.Combine(temp, "src");
                Directory.CreateDirectory(sourceDir);

                var solution = new SolutionModel
                {
                    SolutionName = "AssetPrune",
                    SolutionFilePath = Path.Combine(temp, "AssetPrune.vms"),
                };
                var workspace = new WorkspaceContext();
                workspace.SwitchSolution(solution);

                var importer = new ProjectAssetImporter(workspace);
                var v1 = Path.Combine(sourceDir, "p1.json");
                File.WriteAllText(v1, "{\"Templates\":[\"p1\"]}", new UTF8Encoding(false));
                importer.TryImport(v1, "template", out var templateId, out _);

                var folder = Path.Combine(temp, "Assets", "Versions", "template", templateId.ToString("N"));
                Check(Directory.Exists(folder), "版本库目录已创建", ref failures);

                // 造 6 个版本文件，但只给 2 条版本记录：记录外的文件必须被清掉。
                for (var version = 2; version <= 6; version++)
                {
                    var next = Path.Combine(sourceDir, "p" + version + ".json");
                    File.WriteAllText(next, "{\"Templates\":[\"p" + version + "\"]}", new UTF8Encoding(false));
                    importer.TryImportNewVersion(templateId, next, out _, out _);
                }

                Check(Directory.GetFiles(folder, "v*.*").Length == 6, "清理前有 6 个版本文件", ref failures);

                var policy = new ScadaAccessPolicy();
                var service = new ProjectAssetService(
                    new SolutionAssetWriter(solution), policy,
                    new ScadaAuditWriter(Path.Combine(temp, "Audit")));
                policy.Login(new ScadaUserInfo(
                    "admin", ScadaGroupDefaults.AdminTemplateId,
                    ScadaPermissionKeys.BuiltInKeys.ToArray(), "模板·管理员"));

                // 只记 v1 与 v6 两条（模拟"老记录已被 50 条上限挤掉"）。
                var v1File = Path.Combine(folder, "v1.json");
                var v6File = Path.Combine(folder, "v6.json");
                Check(File.Exists(v1File) && File.Exists(v6File), "前置：v1/v6 文件在", ref failures);

                solution.AssetVersionLog ??= new List<AssetVersionRecord>();
                AssetVersionLog.Append(solution, templateId, "template", "p", 1, "h", "{}", "导入", "admin");
                AssetVersionLog.Append(solution, templateId, "template", "p", 6, "h", "{}", "新版本", "admin");

                var removed = ProjectAssetImporter.PruneVersionFiles(solution, "template", templateId, 6);
                Check(removed == 4, $"清理掉 4 个无记录版本（实际 {removed}）", ref failures);
                Check(File.Exists(v1File), "记录引用的 v1 必须保留（否则回滚是空话）", ref failures);
                Check(File.Exists(v6File), "当前版本 v6 必须保留", ref failures);
                Check(!File.Exists(Path.Combine(folder, "v3.json")), "无记录的 v3 已删除", ref failures);

                // 记版本这条路径必须自动清理，不能只靠手工调用。
                service.TryRecordVersion(templateId, "冻结前记录", "prune", bumpVersion: false, out _);
                Check(Directory.GetFiles(folder, "v*.*").Length == 2,
                    $"记版本后仍只剩 2 个文件（实际 {Directory.GetFiles(folder, "v*.*").Length}）", ref failures);
            }
            catch (Exception ex)
            {
                Check(false, "版本清理断言异常：" + ex.Message, ref failures);
            }
            finally
            {
                try { Directory.Delete(temp, recursive: true); } catch { }
            }
        }

        /// <summary>
        /// 配方两种形态同时有内容 = 配置冲突：必须能被校验出来、并在目录里标成"待处理"。
        /// </summary>
        private static void RunRecipeConflictChecks(ref int failures)
        {
            try
            {
                var solution = new SolutionModel { SolutionName = "RecipeConflict" };
                var workspace = new WorkspaceContext();
                workspace.SwitchSolution(solution);

                var clean = new RecipeDefinition { Name = "干净配方", TextContent = "A = 红,白" };
                Check(clean.ValidateContent() == null, "只有文本内容时校验通过", ref failures);

                var paramOnly = new RecipeDefinition { Name = "参数配方" };
                paramOnly.TouchEntries("probe");
                Check(paramOnly.ValidateContent() == null, "只有参数条目时校验通过", ref failures);

                var conflict = new RecipeDefinition { Name = "冲突配方", TextContent = "A = 红" };
                conflict.Entries.Add(new RecipeEntry { Key = "K", Value = "V" });
                Check(conflict.HasConflictingContent, "同时有文本与参数被识别为冲突", ref failures);
                Check(conflict.ValidateContent()?.Contains("只能留一种") == true,
                    "冲突原因写清只能留一种", ref failures);

                var emptyDeclared = new RecipeDefinition { Name = "空文本", ContentFormat = "text" };
                Check(emptyDeclared.ValidateContent()?.Contains("内容为空") == true,
                    "声明了形态却没有内容也要报", ref failures);

                // 目录必须把它标成"待处理"桶，而不是当成有效资产。
                solution.EnsureRecipes().Add(clean);
                solution.EnsureRecipes().Add(conflict);
                var items = new SolutionProjectAssetCatalog(workspace).Build();
                Check(items.First(i => i.Id == conflict.RecipeId.ToString()).Status == "配置冲突",
                    "目录把冲突配方标成「配置冲突」", ref failures);
                Check(items.First(i => i.Id == clean.RecipeId.ToString()).Status == "有效",
                    "干净配方仍是有效", ref failures);
            }
            catch (Exception ex)
            {
                Check(false, "配方冲突断言异常：" + ex.Message, ref failures);
            }
        }

        /// <summary>
        /// 反向引用索引：**别的插件**用路径/文件名引用资产时也必须算引用。
        /// 旧实现只认"已知插件 + 已知键名"，会把这些显示成"未引用"。
        /// </summary>
        private static void RunReverseReferenceChecks(ref int failures)
        {
            var temp = Path.Combine(Path.GetTempPath(), "vm-asset-refidx-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temp);
                var sourceDir = Path.Combine(temp, "src");
                Directory.CreateDirectory(sourceDir);

                var solution = new SolutionModel
                {
                    SolutionName = "RefIndex",
                    SolutionFilePath = Path.Combine(temp, "RefIndex.vms"),
                };
                var workspace = new WorkspaceContext();
                workspace.SwitchSolution(solution);

                var importer = new ProjectAssetImporter(workspace);
                var png = Path.Combine(sourceDir, "refimg.png");
                File.Copy(@"D:\C#\VM\Image\board\board_01.png", png);
                importer.TryImport(png, "image", out var imageId, out _);
                var asset = solution.ImageAssets.First(a => a.AssetId == imageId);

                // 造一条流程：步骤是"别的插件"，输入里只有文件路径（没有 ImageAssetId）。
                var step = new WaitStep("", "外部插件", "Plugin.SomeOther.OtherPlugin, Plugin.SomeOther", "外部引用步骤");
                step.InputValues["SomePath"] = asset.RelativePath;
                var flow = new FlowModel { FlowName = "F1" };
                flow.Steps.Add(step);
                solution.Flows.Add(flow);

                var item = new SolutionProjectAssetCatalog(workspace).Build()
                    .FirstOrDefault(a => a.Id == imageId.ToString());
                Check(item != null, "目录里有该图像资产", ref failures);
                Check(item?.References.Count > 0,
                    $"别的插件用路径引用也算引用（实际 {item?.References.Count ?? 0} 条）", ref failures);
                Check(item?.References.Any(r => r.Sub != null && r.Sub.Contains("路径")) == true,
                    "引用卡标出匹配依据（路径）", ref failures);

                // 点得动：带流程/步骤名。
                Check(item?.References.Any(r => r.CanNavigate) == true, "反向引用也能跳转", ref failures);

                // 名字不同、路径不同 → 不该凭空算引用。
                var unrelated = new WaitStep("", "外部插件", "Plugin.SomeOther.OtherPlugin, Plugin.SomeOther", "无关步骤");
                unrelated.InputValues["SomePath"] = "Image/other.png";
                flow.Steps.Add(unrelated);

                var after = new SolutionProjectAssetCatalog(workspace).Build()
                    .First(a => a.Id == imageId.ToString());
                Check(after.References.Count == item!.References.Count,
                    "无关路径不会凭空多出引用", ref failures);
            }
            catch (Exception ex)
            {
                Check(false, "反向引用断言异常：" + ex.Message, ref failures);
            }
            finally
            {
                try { Directory.Delete(temp, recursive: true); } catch { }
            }
        }

        /// <summary>
        /// 资产契约：每个消费插件必须声明正确的 AssetKind，AssetId 要能回写到插件自己的字段，
        /// 配置壳的下拉必须真的列出这类资产（选不到 = 等于没接）。
        /// </summary>
        private static void RunPluginAssetContractChecks(ref int failures)
        {
            try
            {
                var solution = new SolutionModel { SolutionName = "PluginContract" };
                var template = new TemplateAsset { Name = "tpl", RelativePath = "Assets/Templates/t.json", ContentHash = "h" };
                var model = new ModelAsset { Name = "mdl", RelativePath = "Assets/Models/m.onnx", ContentHash = "h" };
                var image = new ImageAsset { Name = "img", RelativePath = "Image/i.png", ContentHash = "h" };
                var textRecipe = new RecipeDefinition { Name = "文本配方", TextContent = "A = 红,白" };
                var beadRecipe = new RecipeDefinition { Name = "胶路配方", TextContent = "[{\"Name\":\"B\",\"RefPointsJson\":\"[]\"}]", ContentFormat = "bead" };
                var paramRecipe = new RecipeDefinition { Name = "参数配方" };
                paramRecipe.TouchEntries("probe");

                solution.TemplateAssets.Add(template);
                solution.ModelAssets.Add(model);
                solution.ImageAssets.Add(image);
                solution.EnsureRecipes().Add(textRecipe);
                solution.EnsureRecipes().Add(beadRecipe);
                solution.EnsureRecipes().Add(paramRecipe);

                CheckContract(new Plugin.Matching.MatchingPlugin(), "template", template.AssetId, solution, ref failures);
                CheckContract(new Plugin.Ocr.OcrPlugin(), "model", model.AssetId, solution, ref failures);
                CheckContract(new Plugin.Yolo.YoloPlugin(), "model", model.AssetId, solution, ref failures);
                CheckContract(new Plugin.DlAnomaly.DlAnomalyPlugin(), "model", model.AssetId, solution, ref failures);
                CheckContract(new Plugin.DlClassify.DlClassifyPlugin(), "model", model.AssetId, solution, ref failures);
                CheckContract(new Plugin.DlSegment.DlSegmentPlugin(), "model", model.AssetId, solution, ref failures);
                CheckContract(new Plugin.ImageAcquisition.ImageAcquisitionPlugin(), "image", image.AssetId, solution, ref failures);
                CheckContract(new Plugin.ColorCheck.ColorCheckPlugin(), "recipe", textRecipe.RecipeId, solution, ref failures);
                CheckContract(new Plugin.BeadInspect.BeadInspectPlugin(), "recipe", beadRecipe.RecipeId, solution, ref failures);

                // 配方下拉只列有内联内容的：参数配方没有可被插件解析的文本。
                var recipeOptions = PluginConfigShellViewModel.BuildAssetOptionsFor(solution, "recipe");
                Check(recipeOptions.Any(o => o.AssetId == textRecipe.RecipeId),
                    "配方下拉含文本配方", ref failures);
                Check(recipeOptions.Any(o => o.AssetId == beadRecipe.RecipeId),
                    "配方下拉含胶路配方", ref failures);
                Check(recipeOptions.All(o => o.AssetId != paramRecipe.RecipeId),
                    "配方下拉不含参数配方（选了也解析不出内容）", ref failures);

                // 未填 AssetId 时下拉仍要有"不使用资产"，否则用户没法退回旧路径。
                Check(PluginConfigShellViewModel.BuildAssetOptionsFor(solution, "image").Count == 1,
                    "图像类只有一条时下拉仍可用", ref failures);
            }
            catch (Exception ex)
            {
                Check(false, "插件资产契约断言异常：" + ex.Message, ref failures);
            }
        }

        private static void CheckContract<T>(
            T plugin, string expectedKind, Guid sampleAssetId, SolutionModel solution, ref int failures)
            where T : class, Core.Interfaces.Assets.IAssetIdConfig
        {
            var name = typeof(T).Name;
            Check(plugin.AssetKind == expectedKind,
                $"{name} 声明 AssetKind={expectedKind}（实际 {plugin.AssetKind}）", ref failures);

            plugin.AssetId = sampleAssetId.ToString();
            Check(string.Equals(plugin.AssetId, sampleAssetId.ToString(), StringComparison.OrdinalIgnoreCase),
                $"{name} 的 AssetId 能写回自身字段", ref failures);

            var options = PluginConfigShellViewModel.BuildAssetOptionsFor(solution, plugin.AssetKind);
            Check(options.Any(o => o.AssetId == sampleAssetId),
                $"{name} 的配置下拉能选到这类资产", ref failures);

            plugin.AssetId = string.Empty;
            Check(string.IsNullOrEmpty(plugin.AssetId), $"{name} 的 AssetId 能清空（退回旧路径）", ref failures);
        }

        /// <summary>
        /// 运行期：消费插件按 AssetId 取到资产、归档后被拒、格式不匹配被拒，且**都不抛异常**。
        /// 图像采集走真实全链路（读文件 → 出 HImage）。
        /// </summary>
        private static void RunPluginAssetRuntimeChecks(ref int failures)
        {
            var temp = Path.Combine(Path.GetTempPath(), "vm-asset-plugins-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temp);
                var sourceDir = Path.Combine(temp, "src");
                Directory.CreateDirectory(sourceDir);

                var solution = new SolutionModel
                {
                    SolutionName = "PluginRuntime",
                    SolutionFilePath = Path.Combine(temp, "PluginRuntime.vms"),
                };
                var workspace = new WorkspaceContext();
                workspace.SwitchSolution(solution);

                var importer = new ProjectAssetImporter(workspace);
                var png = Path.Combine(sourceDir, "frame.png");
                File.Copy(@"D:\C#\VM\Image\board\board_01.png", png);
                Check(importer.TryImport(png, "image", out var imageId, out var importError),
                    "插件验证前置：图像资产导入" + (string.IsNullOrEmpty(importError) ? string.Empty : "：" + importError),
                    ref failures);

                var context = new VisionMaster.Services.ExecutionContext(null!) { Workspace = workspace };

                // ── ① 图像采集：真实读文件 ──
                var acquisition = new Plugin.ImageAcquisition.ImageAcquisitionPlugin
                {
                    Mode = Plugin.ImageAcquisition.AcquisitionMode.SingleFile,
                    ImageAssetId = imageId.ToString(),
                    DisplayViewIndex = 0,
                };
                acquisition.FilePathPort.Value = string.Empty;

                Exception? thrown = null;
                try { acquisition.RunAlgorithm(context); }
                catch (Exception ex) { thrown = ex; }

                Check(thrown == null, "图像采集按资产运行不抛异常" + (thrown == null ? string.Empty : "：" + thrown.Message),
                    ref failures);
                Check(acquisition.Success.TypedValue, "图像采集成功（Success=true）原因=" + acquisition.ErrorMessage.Value,
                    ref failures);
                Check(acquisition.OutputImage.TypedValue != null, "图像采集产出图像", ref failures);
                Check(acquisition.CurrentFilePath.TypedValue.Contains("frame.png"),
                    $"采集到的路径来自资产解析（实际 {acquisition.CurrentFilePath.TypedValue}）", ref failures);
                Check(File.Exists(acquisition.CurrentFilePath.TypedValue), "采集路径是真实存在的绝对路径", ref failures);
                acquisition.OutputImage.TypedValue?.Dispose();

                // ── ② 归档后：同一插件必须被拒且说清原因 ──
                var policy = new ScadaAccessPolicy();
                var service = new ProjectAssetService(
                    new SolutionAssetWriter(solution), policy,
                    new ScadaAuditWriter(Path.Combine(temp, "Audit")));
                policy.Login(new ScadaUserInfo(
                    "admin", ScadaGroupDefaults.AdminTemplateId,
                    ScadaPermissionKeys.BuiltInKeys.ToArray(), "模板·管理员"));
                service.TrySetArchived(imageId, true, out _);

                var archived = new Plugin.ImageAcquisition.ImageAcquisitionPlugin
                {
                    Mode = Plugin.ImageAcquisition.AcquisitionMode.SingleFile,
                    ImageAssetId = imageId.ToString(),
                    DisplayViewIndex = 0,
                };
                archived.FilePathPort.Value = string.Empty;

                thrown = null;
                try { archived.RunAlgorithm(context); }
                catch (Exception ex) { thrown = ex; }

                Check(thrown == null, "归档资产运行不抛异常（应当是干净的失败）", ref failures);
                Check(!archived.Success.TypedValue, "归档资产运行失败（Success=false）", ref failures);
                Check(((string)archived.ErrorMessage.Value).Contains("已归档"),
                    $"归档资产的失败原因点名「已归档」（实际：{archived.ErrorMessage.Value}）", ref failures);
                service.TrySetArchived(imageId, false, out _);

                // ── ③ 配方格式不匹配：颜色检查拿胶路库必须被拒 ──
                var beadSource = Path.Combine(sourceDir, "bead.json");
                File.WriteAllText(beadSource,
                    "[{\"Name\":\"B1\",\"RefPointsJson\":\"[]\"}]", new UTF8Encoding(false));
                Check(importer.TryImport(beadSource, "recipe", out var beadId, out _), "胶路配方导入", ref failures);
                Check(solution.RecipesOrEmpty().First(r => r.RecipeId == beadId).ContentFormat == "bead",
                    "胶路配方被识别为 bead 格式", ref failures);

                var textSource = Path.Combine(sourceDir, "color.txt");
                File.WriteAllText(textSource, "A = 红,白", new UTF8Encoding(false));
                Check(importer.TryImport(textSource, "recipe", out var textId, out _), "文本配方导入", ref failures);

                var colorCheck = new Plugin.ColorCheck.ColorCheckPlugin
                {
                    RecipeAssetId = beadId.ToString(),
                    DisplayViewIndex = 0,
                };
                using (var rgb = new HImage(png))
                {
                    colorCheck.Image.Value = rgb;
                    thrown = null;
                    try { colorCheck.RunAlgorithm(context); }
                    catch (Exception ex) { thrown = ex; }
                }

                Check(thrown == null, "颜色检查拿错格式不抛异常", ref failures);
                Check(!(bool)colorCheck.Result.Value && ((string)colorCheck.ErrorMessage.Value).Contains("内容形态"),
                    $"颜色检查对胶路库给出格式错误（实际：{colorCheck.ErrorMessage.Value}）", ref failures);

                // ── ④ 胶路检查拿文本配方必须被拒 ──
                var beadInspect = new Plugin.BeadInspect.BeadInspectPlugin
                {
                    RecipeAssetId = textId.ToString(),
                };
                using (var gray = new HImage("byte", 8, 8))
                {
                    beadInspect.SrcImage.Value = gray;
                    thrown = null;
                    try { beadInspect.RunAlgorithm(context); }
                    catch (Exception ex) { thrown = ex; }
                }

                Check(thrown == null, "胶路检查拿错格式不抛异常", ref failures);
                Check(((string)beadInspect.ErrorMessage.Value).Contains("内容形态"),
                    $"胶路检查对文本配方给出格式错误（实际：{beadInspect.ErrorMessage.Value}）", ref failures);

                // ── ⑤ 模板/模型：插件口径能解析到真实文件，归档后被拒 ──
                var tplSource = Path.Combine(sourceDir, "tpl.json");
                File.WriteAllText(tplSource, "{\"Templates\":[]}", new UTF8Encoding(false));
                Check(importer.TryImport(tplSource, "template", out var templateId, out _), "模板资产导入", ref failures);

                Check(ProjectAssetPathResolver.TryResolve(context, templateId.ToString(), null, "template",
                        out var templatePath, out var templateError) && File.Exists(templatePath),
                    "模板资产能被插件口径解析" + (string.IsNullOrEmpty(templateError) ? string.Empty : "：" + templateError),
                    ref failures);

                service.TrySetArchived(templateId, true, out _);
                Check(!ProjectAssetPathResolver.TryResolve(context, templateId.ToString(), null, "template", out _, out var archivedTemplate)
                      && archivedTemplate.Contains("已归档"),
                    "归档模板被插件口径拒绝", ref failures);
                service.TrySetArchived(templateId, false, out _);

                var mdlSource = Path.Combine(sourceDir, "m.onnx");
                File.WriteAllBytes(mdlSource, new byte[] { 1, 2, 3, 4 });
                Check(importer.TryImport(mdlSource, "model", out var modelId, out _), "模型资产导入", ref failures);
                Check(ProjectAssetPathResolver.TryResolve(context, modelId.ToString(), null, "model", out var modelPath, out _)
                      && File.Exists(modelPath),
                    "模型资产能被插件口径解析", ref failures);
            }
            catch (Exception ex)
            {
                Check(false, "插件运行期断言异常：" + ex.Message, ref failures);
            }
            finally
            {
                try { Directory.Delete(temp, recursive: true); } catch { }
            }
        }

        /// <summary>
        /// 软件级操作记录（SQLite）：
        /// 写→查→按条件筛→保留期清理；建库失败必须 fail-open（不抛、不影响业务）；
        /// 真实调用点（登录/登出、系统参数保存）必须真的落到库里。
        /// </summary>
        private static void RunSoftwareOperationLogChecks(ref int failures)
        {
            var temp = Path.Combine(Path.GetTempPath(), "vm-swoff-log-" + Guid.NewGuid().ToString("N"));
            var previous = Core.Interfaces.Operations.SoftwareOperationLogHost.Current;
            try
            {
                Directory.CreateDirectory(temp);
                var dbPath = Path.Combine(temp, "operations.db");
                var warnings = new List<string>();
                using var log = new Core.Database.SqliteSoftwareOperationLog(
                    dbPath, retentionDays: () => 90, warn: w => warnings.Add(w));

                // ── ① 写 + 查：字段一个不少 ──
                var solutionId = Guid.NewGuid();
                log.Record(new Core.Interfaces.Operations.SoftwareOperationEntry
                {
                    Category = "Identity",
                    Action = "登录",
                    Outcome = "拒绝",
                    Actor = "operator1",
                    ActorRole = "操作员",
                    Subject = "密码错误",
                    SolutionId = solutionId,
                    SolutionName = "S1",
                    Detail = "连续第三次",
                });

                var rows = log.Query(category: "Identity");
                Check(rows.Count == 1, $"写进去能查回来（实际 {rows.Count} 条）", ref failures);
                var row = rows.FirstOrDefault();
                Check(row?.Actor == "operator1" && row.Outcome == "拒绝" && row.Action == "登录",
                    "关键字段原样保存（人/结果/动作）", ref failures);
                Check(row?.SolutionId == solutionId && row.SolutionName == "S1",
                    "方案归属也记下来了", ref failures);
                Check(row?.Detail == "连续第三次" && !string.IsNullOrWhiteSpace(row.Machine),
                    "明细与机器名都在", ref failures);

                // ── ② 条件筛选 ──
                log.Record(new Core.Interfaces.Operations.SoftwareOperationEntry
                {
                    Category = "Settings", Action = "保存系统参数", Outcome = "成功", Actor = "admin",
                });
                Check(log.Query(category: "Settings").Count == 1, "按分类筛选准确", ref failures);
                Check(log.Query(actor: "operator1").Count == 1, "按操作者筛选准确", ref failures);
                Check(log.Query(solutionId: solutionId).Count == 1, "按方案筛选准确", ref failures);
                Check(log.Query().Count == 2, "不带条件返回全部", ref failures);

                // ── ③ 保留期清理 ──
                using (var shortRetention = new Core.Database.SqliteSoftwareOperationLog(
                           Path.Combine(temp, "retention.db"), retentionDays: () => 7))
                {
                    shortRetention.Record(new Core.Interfaces.Operations.SoftwareOperationEntry
                    {
                        Category = "Identity",
                        Action = "登录",
                        TimestampUtc = DateTime.UtcNow.AddDays(-30),
                    });
                    shortRetention.Record(new Core.Interfaces.Operations.SoftwareOperationEntry
                    {
                        Category = "Identity", Action = "登录",
                    });

                    var removed = shortRetention.PruneExpired();
                    Check(removed == 1, $"过期行被清掉 1 条（实际 {removed}）", ref failures);
                    Check(shortRetention.Query().Count == 1, "未过期的行保留", ref failures);
                }

                // ── ④ fail-open：库建不起来时不许抛 ──
                var blocker = Path.Combine(temp, "blocker.txt");
                File.WriteAllText(blocker, "not a folder");
                var brokenPath = Path.Combine(blocker, "sub", "operations.db");

                var brokenWarnings = new List<string>();
                using var broken = new Core.Database.SqliteSoftwareOperationLog(
                    brokenPath, retentionDays: () => 90, warn: w => brokenWarnings.Add(w));

                Exception? thrown = null;
                try
                {
                    broken.Record(new Core.Interfaces.Operations.SoftwareOperationEntry
                    {
                        Category = "Identity", Action = "登录",
                    });
                    broken.Query();
                    broken.PruneExpired();
                }
                catch (Exception ex) { thrown = ex; }

                Check(thrown == null, "建库失败时记录/查询/清理都不抛（fail-open）" + (thrown == null ? string.Empty : "：" + thrown.Message),
                    ref failures);
                Check(brokenWarnings.Count > 0, "失败有 Warn 可查（不是静默吞掉）", ref failures);
                Check(string.IsNullOrEmpty(broken.DatabasePath), "库不可用时对外报告「没有库」", ref failures);

                // ── ⑤ 静态出口：异常被吞，且真实调用点真的落库 ──
                Core.Interfaces.Operations.SoftwareOperationLogHost.Current = log;

                Core.Interfaces.Operations.SoftwareOperationLogHost.Current = new ThrowingOperationLog();
                thrown = null;
                try
                {
                    Core.Interfaces.Operations.SoftwareOperationLogHost.SafeRecord(e =>
                    {
                        e.Category = "Identity";
                        e.Action = "登录";
                    });
                }
                catch (Exception ex) { thrown = ex; }
                Check(thrown == null, "实现抛异常时 SafeRecord 也吞掉（业务不受影响）", ref failures);

                Core.Interfaces.Operations.SoftwareOperationLogHost.Current = log;
                var policy = new ScadaAccessPolicy();
                policy.Login(new ScadaUserInfo(
                    "probeuser", ScadaGroupDefaults.AdminTemplateId,
                    ScadaPermissionKeys.BuiltInKeys.ToArray(), "模板·管理员"));
                policy.Logout();

                var identityRows = log.Query(category: "Identity");
                Check(identityRows.Any(r => r.Action == "登录" && r.Actor == "probeuser" && r.Outcome == "成功"),
                    "登录成功真的写进了软件库", ref failures);
                Check(identityRows.Any(r => r.Action == "登出" && r.Actor == "probeuser"),
                    "登出真的写进了软件库（超时/切方案同一条出口）", ref failures);

                // 系统参数保存（Core 层调用点）：容器里没有也应当写成功。
                var settings = new AppSettingsService();
                settings.Save();
                Check(log.Query(category: "Settings").Any(r => r.Action == "保存系统参数"),
                    "系统参数保存真的写进了软件库", ref failures);

                // ── ⑥ 权限变更（账号本 / 用户组）──
                var usersDir = Path.Combine(temp, "Users");
                Directory.CreateDirectory(usersDir);
                var store = new ScadaUserStore(
                    Path.Combine(usersDir, "Users.json"),
                    actorProvider: () => "permadmin");

                store.TryAddUser("u1", "Passw0rd!x", ScadaGroupDefaults.AdminTemplateId, out var addUserError);
                store.TryResetPassword("u1", "Passw0rd!y", out var resetError);
                store.TryAddGroup("自定义组", ScadaPermissionKeys.BuiltInKeys.Take(1), out var addGroupError);
                var custom = store.Groups.FirstOrDefault(g => g.Name == "自定义组");
                Check(custom != null, "前置：自定义组已创建（" + (addGroupError ?? "ok") + "）", ref failures);
                if (custom != null)
                {
                    store.TryUpdateGroup(custom.Id, "自定义组2", ScadaPermissionKeys.BuiltInKeys.Take(2), out _);
                    store.RevokeGroup(custom.Id, out _);
                }
                store.TryRemoveUser("u1", out var removeError);
                Check(string.IsNullOrEmpty(addUserError) && string.IsNullOrEmpty(resetError) && string.IsNullOrEmpty(removeError),
                    "前置：账号操作都成功（" + (addUserError ?? resetError ?? removeError ?? "ok") + "）", ref failures);

                var permRows = log.Query(category: "Permission");
                Check(permRows.Any(r => r.Action == "创建账号" && r.Subject == "u1"),
                    "创建账号写进了软件库", ref failures);
                Check(permRows.Any(r => r.Action == "重置密码" && r.Subject == "u1"),
                    "重置密码写进了软件库", ref failures);
                Check(permRows.Any(r => r.Action == "新建用户组" && r.Subject == "自定义组"),
                    "新建用户组写进了软件库", ref failures);
                Check(permRows.Any(r => r.Action == "修改用户组" && r.Subject == "自定义组2"),
                    "修改用户组写进了软件库", ref failures);
                Check(permRows.Any(r => r.Action == "撤销用户组"),
                    "撤销用户组写进了软件库", ref failures);
                Check(permRows.Any(r => r.Action == "删除账号" && r.Subject == "u1"),
                    "删除账号写进了软件库", ref failures);
                Check(permRows.All(r => r.Actor == "permadmin"),
                    "权限变更带操作者（不是空白）", ref failures);

                // ── ⑦ 运动安全动作（真实执行器 + 真实虚拟运动卡）──
                RunMotionSafetyLogChecks(log, ref failures);
            }
            catch (Exception ex)
            {
                Check(false, "软件级操作记录断言异常：" + ex.Message, ref failures);
            }
            finally
            {
                Core.Interfaces.Operations.SoftwareOperationLogHost.Current = previous;
                try { Directory.Delete(temp, recursive: true); } catch { }
            }
        }

        /// <summary>
        /// ⑦ 运动安全动作（使能 / 失能 / 回零 / 停止 / 清报警）必须落到软件级操作记录。
        ///
        /// 用**真实** <see cref="MotionActionExecutor"/> + 真实虚拟运动卡（Plugin.Motion.Virtual）跑：
        /// "被门禁拒绝也要落库""记录不影响动作结果"这两条只有真设备状态机才证明得了，
        /// new 一个假 <see cref="IMotionDevice"/> 只能断言常量（R38：跳过型/常量型断言必须能反证）。
        ///
        /// 三条口径：
        ///   · 成功与被门禁拒绝两条路径都留记录，且带轴名（Subject）与稳定 Id（SubjectId）；
        ///   · 定位这类**不**记 Safety —— 反向断言，防"以后把全部运动都记进去"；
        ///   · 记录不影响动作结果：同一条调用"接库 / 不接库"返回值与提示必须逐字一致。
        /// </summary>
        private static void RunMotionSafetyLogChecks(
            Core.Database.SqliteSoftwareOperationLog log, ref int failures)
        {
            VirtualMotionCard online = null;
            VirtualMotionCard offline = null;
            MotionActionExecutor executor = null;

            try
            {
                // 两张卡：一张连上（使能/失能走成功路径），一张始终不连（回零走"被门禁拒绝"路径）。
                var onlineDescriptor = NewProbeMotionDescriptor("断言用在线虚拟卡", "SX");
                var offlineDescriptor = NewProbeMotionDescriptor("断言用离线虚拟卡", "SY");

                online = new VirtualMotionCard(onlineDescriptor) { Log = new ProbeLog() };
                offline = new VirtualMotionCard(offlineDescriptor) { Log = new ProbeLog() };

                var provider = new ProbeMotionProvider(
                    (onlineDescriptor, online), (offlineDescriptor, offline));
                var registry = new MotionAxisRegistry(() => new[] { onlineDescriptor, offlineDescriptor });

                Check(online.Connect(), "前置：虚拟运动卡连上了", ref failures);
                Check(offline.State == MotionCardState.Closed,
                    "前置：第二张卡保持未连接（用来造「被门禁拒绝」）", ref failures);

                executor = new MotionActionExecutor(registry, provider, new ProbeLog());

                // 等第一拍轮询把轴快照填出来（定位门禁要读 Enabled / 位置）
                Check(WaitUntilMotion(() => online.GetAxisStatus(0) != null, 3000),
                    "前置：在线卡的轴快照已就绪", ref failures);

                var safetyBase = log.Query(category: "Safety").Count;

                // ① 使能（成功路径）
                var enableOk = executor.TryExecute(
                    NewAxisAction(ScadaActionType.MotionEnable, "SX"), out var enableMessage);
                Check(enableOk, "前置：使能被卡接受（" + enableMessage + "）", ref failures);
                Check(WaitUntilMotion(() => online.GetAxisStatus(0) is { Enabled: true }, 3000),
                    "前置：轴已使能（后续定位门禁要求它）", ref failures);

                // ② 定位：不进 Safety（反向断言）。先确认它**真的**被接受，否则这条断言等于空跑。
                var moveOk = executor.TryExecute(
                    NewAxisAction(ScadaActionType.MotionMoveAbsolute, "SX", targetMm: 10, velocityMmPerS: 100),
                    out var moveMessage);
                Check(moveOk, "前置：定位被卡接受（" + moveMessage + "）", ref failures);
                Check(log.Query(category: "Safety").Count == safetyBase + 1,
                    $"定位不产生 Safety 记录（此刻应只有使能那 1 条，实际 {log.Query(category: "Safety").Count - safetyBase} 条）",
                    ref failures);

                // ③ 失能（成功路径）
                var disableOk = executor.TryExecute(
                    NewAxisAction(ScadaActionType.MotionDisable, "SX"), out var disableMessage);
                Check(disableOk, "前置：失能被卡接受（" + disableMessage + "）", ref failures);

                var safetyRows = log.Query(category: "Safety");
                var enableRow = safetyRows.FirstOrDefault(r => r.Action == "轴使能");
                Check(enableRow != null, $"使能写进了软件库（Safety 共 {safetyRows.Count} 条）", ref failures);
                Check(enableRow?.Outcome == "成功",
                    "使能记录的结果是「成功」（实际 " + (enableRow?.Outcome ?? "无") + "）", ref failures);
                Check(enableRow?.Subject == "SX",
                    "使能记录带轴名（实际 " + (enableRow?.Subject ?? "无") + "）", ref failures);
                Check(!string.IsNullOrWhiteSpace(enableRow?.SubjectId),
                    "使能记录带轴的稳定 Id（不是空串）", ref failures);

                var disableRow = safetyRows.FirstOrDefault(r => r.Action == "轴失能");
                Check(disableRow?.Outcome == "成功" && disableRow?.Subject == "SX",
                    "失能记录：成功 + 轴名（实际 " + (disableRow?.Outcome ?? "无") + "/" + (disableRow?.Subject ?? "无") + "）",
                    ref failures);

                // ④ 被门禁拒绝（未连接的卡上回零）：要留「拒绝」且明细非空
                var homeOk = executor.TryExecute(
                    NewAxisAction(ScadaActionType.MotionHome, "SY"), out var homeMessage);
                Check(!homeOk, "前置：未连接卡上的回零被门禁拒绝（" + homeMessage + "）", ref failures);

                var homeRow = log.Query(category: "Safety").FirstOrDefault(r => r.Action == "轴回零");
                Check(homeRow?.Outcome == "拒绝",
                    "被门禁拒绝的动作也留记录（结果 " + (homeRow?.Outcome ?? "无") + "）", ref failures);
                Check(homeRow?.Subject == "SY",
                    "拒绝记录带轴名（实际 " + (homeRow?.Subject ?? "无") + "）", ref failures);
                Check(!string.IsNullOrWhiteSpace(homeRow?.Detail) && homeRow.Detail.Contains("未连接"),
                    "拒绝记录的明细带原因（实际 " + (homeRow?.Detail ?? "空") + "）", ref failures);

                // ⑤ 记录不影响动作结果：同一条调用"接库 / 不接库"返回值与提示必须逐字一致
                var previousHost = Core.Interfaces.Operations.SoftwareOperationLogHost.Current;

                Core.Interfaces.Operations.SoftwareOperationLogHost.Current = log;
                var withLog = executor.TryExecute(
                    NewAxisAction(ScadaActionType.MotionHome, "SY"), out var withLogMessage);

                Core.Interfaces.Operations.SoftwareOperationLogHost.Current =
                    Core.Interfaces.Operations.NullSoftwareOperationLog.Instance;
                var withoutLog = executor.TryExecute(
                    NewAxisAction(ScadaActionType.MotionHome, "SY"), out var withoutLogMessage);

                Core.Interfaces.Operations.SoftwareOperationLogHost.Current = previousHost;

                Check(withLog == withoutLog
                      && string.Equals(withLogMessage, withoutLogMessage, StringComparison.Ordinal),
                    $"接不接库，返回值与提示逐字一致（{withLog}/{withoutLog}）", ref failures);

                // ⑥ 条数收口：只有那四条安全动作各一条，没有多余记录
                var expected = safetyBase + 4;   // 使能 + 失能 + 回零(拒绝) + 回零(第⑤步接库那次)
                Check(log.Query(category: "Safety").Count == expected,
                    $"Safety 记录条数恰好 {expected}（实际 {log.Query(category: "Safety").Count}）", ref failures);
            }
            catch (Exception ex)
            {
                Check(false, "运动安全动作记录断言异常：" + ex.Message, ref failures);
            }
            finally
            {
                executor?.Dispose();
                offline?.Dispose();
                online?.Dispose();
            }
        }

        /// <summary>造一条"指定轴"的运动动作（只给名字，Id 由注册表回填 —— 顺便走一遍名字兜底路径）。</summary>
        private static ScadaAction NewAxisAction(
            ScadaActionType type, string axisName, double? targetMm = null, double velocityMmPerS = 0)
        {
            var action = new ScadaAction { Type = type };
            if (targetMm.HasValue) action.TargetMm = targetMm.Value;
            if (velocityMmPerS > 0) action.VelocityMmPerS = velocityMmPerS;
            action.BindAxis(Guid.Empty, axisName);
            return action;
        }

        /// <summary>等一个条件成立（运动卡状态由轮询线程异步刷新，不能假设"下发完就能读到"）。</summary>
        private static bool WaitUntilMotion(Func<bool> condition, int timeoutMs)
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                if (condition()) return true;
                System.Threading.Thread.Sleep(10);
            }

            return condition();
        }

        /// <summary>断言用运动卡配置：一张卡一根轴（软限位放宽到 ±100mm，够跑一次 10mm 定位）。</summary>
        private static MotionDescriptor NewProbeMotionDescriptor(string caption, string axisName) => new()
        {
            Id = Guid.NewGuid(),
            DriverTypeKey = typeof(VirtualMotionCard).AssemblyQualifiedName ?? nameof(VirtualMotionCard),
            DisplayName = caption,
            Address = "probe://" + Guid.NewGuid().ToString("N").Substring(0, 6),
            Params = new MotionParams
            {
                PollIntervalMs = 5,
                WatchdogTimeoutMs = 500,
                CommandTimeoutMs = 3000,
                DefaultVelocityMmPerS = 200,
                MaxQueueDepth = 16,
            },
            Axes = new List<AxisMapping>
            {
                new()
                {
                    LogicalName = axisName,
                    PhysicalIndex = 0,
                    UnitsPerMm = 1000,
                    SoftLimitMinMm = -100,
                    SoftLimitMaxMm = 100,
                },
            },
        };

        /// <summary>断言用的最小 IMotionProvider（两张卡 + 两个设备，按卡 Id 取）</summary>
        private sealed class ProbeMotionProvider : IMotionProvider
        {
            private readonly (MotionDescriptor Descriptor, IMotionDevice Device)[] _entries;

            public ProbeMotionProvider(params (MotionDescriptor, IMotionDevice)[] entries) => _entries = entries;

            public IReadOnlyList<MotionDescriptor> Cards => _entries.Select(e => e.Descriptor).ToList();

            public bool TryGetDevice(Guid cardId, out IMotionDevice device)
            {
                foreach (var entry in _entries)
                {
                    if (entry.Descriptor.Id == cardId)
                    {
                        device = entry.Device;
                        return true;
                    }
                }

                device = null;
                return false;
            }

            public bool TryGetByKey(string address, out IMotionDevice device)
            {
                foreach (var entry in _entries)
                {
                    if (string.Equals(entry.Descriptor.Address, address, StringComparison.Ordinal))
                    {
                        device = entry.Device;
                        return true;
                    }
                }

                device = null;
                return false;
            }

            public bool TryGetByName(string displayName, out IMotionDevice device)
            {
                foreach (var entry in _entries)
                {
                    if (string.Equals(entry.Descriptor.DisplayName, displayName, StringComparison.Ordinal))
                    {
                        device = entry.Device;
                        return true;
                    }
                }

                device = null;
                return false;
            }

            public void ReleaseAll() { }
        }

        /// <summary>断言用的最小日志实现（运动卡的看门狗/连接路径会写日志，丢掉即可）</summary>
        private sealed class ProbeLog : ILogService
        {
            public void Success(params string[] messages) { }
            public void Error(params Exception[] messages) { }
            public void Error(params string[] messages) { }
            public void Info(params string[] messages) { }
            public void Warn(params string[] messages) { }
        }

        private sealed class ThrowingOperationLog : Core.Interfaces.Operations.ISoftwareOperationLog
        {
            public string DatabasePath => string.Empty;

            public void Record(Core.Interfaces.Operations.SoftwareOperationEntry entry)
                => throw new InvalidOperationException("故意失败");
        }

        /// <summary>
        /// 方案级资产历史：`&lt;方案目录&gt;\Logs\operations.db`。
        /// 判据：写进库、能读回、**超过 `.vms` 的 50 条上限也不丢**、老方案（只在 .vms 里）仍看得到、没方案时静默。
        /// </summary>
        private static void RunSolutionAssetHistoryChecks(ref int failures)
        {
            var temp = Path.Combine(Path.GetTempPath(), "vm-asset-history-" + Guid.NewGuid().ToString("N"));
            var previous = Core.Interfaces.Operations.AssetVersionSinkHost.Current;
            try
            {
                Directory.CreateDirectory(temp);
                string? root = temp;
                var warnings = new List<string>();
                var store = new Core.Database.SqliteAssetHistoryStore(() => root, w => warnings.Add(w));
                Core.Interfaces.Operations.AssetVersionSinkHost.Current = store;

                var solution = new SolutionModel
                {
                    SolutionName = "HistoryScope",
                    SolutionFilePath = Path.Combine(temp, "HistoryScope.vms"),
                };
                var assetId = Guid.NewGuid();

                // ① 写 60 条：`.vms` 里只留 50（Trim），库里 60 条全在。
                for (var i = 1; i <= 60; i++)
                {
                    VisionMaster.Models.Assets.AssetVersionLog.Append(
                        solution, assetId, "template", "tpl", i, "h" + i, "{}", "动作" + i, "admin");
                }

                var dbPath = Path.Combine(temp, "Logs", "operations.db");
                Check(File.Exists(dbPath), "方案级历史库建在 <方案目录>\\Logs\\operations.db", ref failures);
                Check(solution.AssetVersionLog.Count == 0,
                    $"`.vms` 不再追加历史（实际 {solution.AssetVersionLog.Count} 条）", ref failures);

                var merged = VisionMaster.Models.Assets.AssetVersionLog.Of(solution, assetId);
                Check(merged.Count == 60, $"读历史合并库与 .vms 后是 60 条（实际 {merged.Count}）", ref failures);
                Check(merged[0].Version == 60 && merged[^1].Version == 1, "按时间倒序，最新在前", ref failures);

                // ①b 界面必须读合并源（不是只读 .vms 那 50 条），且列表分页。
                solution.TemplateAssets.Add(new TemplateAsset
                {
                    AssetId = assetId,
                    Name = "tpl",
                    RelativePath = "Assets/Templates/t.json",
                    ContentHash = "h",
                });
                var ws = new WorkspaceContext();
                ws.SwitchSolution(solution);
                var vm = new ProjectAssetsViewModel(ws);
                vm.SelectTypeCommand.Execute("template");
                var templateItem = vm.VisibleAssets.FirstOrDefault(a => a.Id == assetId.ToString());
                Check(templateItem?.Versions.Count == 61,
                    $"资产页读到的版本数是 61（60 条历史 + 1 条「当前」；实际 {templateItem?.Versions.Count ?? 0}）——界面已读库", ref failures);

                vm.SelectedAsset = templateItem;
                Check(vm.PagedVersions.Count == 50,
                    $"第一页 50 条（实际 {vm.PagedVersions.Count}）", ref failures);
                Check(vm.VersionPageCount == 2, $"共 2 页（实际 {vm.VersionPageCount}）", ref failures);
                Check(vm.HasVersionPaging, "超过一页时显示分页条", ref failures);
                vm.NextVersionPageCommand.Execute();
                Check(vm.PagedVersions.Count == 11 && vm.VersionPage == 2,
                    $"第二页 11 条（实际 {vm.PagedVersions.Count}）", ref failures);
                vm.PrevVersionPageCommand.Execute();
                Check(vm.PagedVersions.Count == 50 && vm.VersionPage == 1, "翻回第一页", ref failures);

                // ② 重开方案（`.vms` 里只剩 50 条）：库里那 10 条仍要读得到。
                var reopened = new SolutionModel
                {
                    SolutionName = "HistoryScope",
                    SolutionFilePath = Path.Combine(temp, "HistoryScope.vms"),
                };
                foreach (var record in solution.AssetVersionLog)
                    reopened.AssetVersionLog.Add(record);

                var afterReopen = VisionMaster.Models.Assets.AssetVersionLog.Of(reopened, assetId);
                Check(afterReopen.Count == 60, $"重开方案后历史不丢（实际 {afterReopen.Count}）", ref failures);

                // ③ 库不可用时回落到 `.vms`：老方案（历史只写在 .vms 里）不能因为切了库就看不到。
                var legacyAsset = Guid.NewGuid();
                VisionMaster.Models.Assets.AssetVersionLog.Append(
                    reopened, legacyAsset, "template", "old", 1, "h", "{}", "导入", "admin");
                Check(store.Read(legacyAsset, 100).Count == 1, "新记录写进了库", ref failures);
                Check(reopened.AssetVersionLog.All(r => r.AssetId != legacyAsset),
                    "新记录不再写进 `.vms`（停写已生效）", ref failures);

                // 手工塞一条"老方案遗留"的记录，模拟升级前就存在的历史。
                reopened.AssetVersionLog.Add(new AssetVersionRecord
                {
                    AssetId = legacyAsset,
                    Kind = "template",
                    Name = "old",
                    Version = 1,
                    Action = "老记录",
                    Actor = "admin",
                    CreatedAtUtc = DateTime.UtcNow.AddDays(-30),
                });

                store.Dispose();
                var readOnlyOld = new Core.Database.SqliteAssetHistoryStore(() => null);
                Core.Interfaces.Operations.AssetVersionSinkHost.Current = readOnlyOld;
                var fallback = VisionMaster.Models.Assets.AssetVersionLog.Of(reopened, legacyAsset);
                Check(fallback.Any(r => r.Action == "老记录"),
                    "库不可用时回落到 `.vms`（老方案的历史照样看得到）", ref failures);

                // ④ 没有方案（root 为空）时静默：不抛、不建文件。
                Exception? thrown = null;
                try
                {
                    VisionMaster.Models.Assets.AssetVersionLog.Append(
                        reopened, Guid.NewGuid(), "template", "x", 1, "h", "{}", "导入", "admin");
                }
                catch (Exception ex) { thrown = ex; }
                Check(thrown == null, "没有方案时写历史不抛（静默停用）", ref failures);
                Check(string.IsNullOrEmpty(readOnlyOld.DatabasePath), "无方案时对外报告「没有库」", ref failures);
            }
            catch (Exception ex)
            {
                Check(false, "方案级资产历史断言异常：" + ex.Message, ref failures);
            }
            finally
            {
                Core.Interfaces.Operations.AssetVersionSinkHost.Current = previous;
                try { Directory.Delete(temp, recursive: true); } catch { }
            }
        }

        /// <summary>逐字段 diff：标定矩阵变化要能逐个数值看出来。</summary>
        private static void RunDiffChecks(ref int failures)
        {
            const string before = "{\"Kind\":0,\"MmPerPixel\":0.05,\"Matrix\":[1.0,0.0,0.0,1.0,0.0,0.0],\"SourceImageWidth\":640}";
            const string after = "{\"Kind\":0,\"MmPerPixel\":0.2,\"Matrix\":[2.0,0.0,0.0,2.0,0.0,0.0],\"SourceImageWidth\":640,\"CameraSerial\":\"HK-03\"}";

            var diffs = ProjectAssetService.DiffSnapshots(before, after);
            Check(diffs.Count > 0, $"diff 有 {diffs.Count} 条字段", ref failures);

            var mm = diffs.FirstOrDefault(d => d.Field == "MmPerPixel");
            Check(mm != null && mm.Changed && mm.Before == "0.05" && mm.After == "0.2",
                "MmPerPixel 差异被识别（0.05 → 0.2）", ref failures);

            var matrix = diffs.FirstOrDefault(d => d.Field == "Matrix[0]");
            Check(matrix != null && matrix.Changed, "数组按下标展开（Matrix[0] 变化可见）", ref failures);

            var unchanged = diffs.FirstOrDefault(d => d.Field == "SourceImageWidth");
            Check(unchanged != null && !unchanged.Changed, "未变字段标为未变化", ref failures);

            var added = diffs.FirstOrDefault(d => d.Field == "CameraSerial");
            Check(added != null && added.Before == "<新增>" && added.After == "HK-03",
                "新增字段标 <新增> 而不是空串", ref failures);

            var removed = ProjectAssetService.DiffSnapshots(after, before)
                .FirstOrDefault(d => d.Field == "CameraSerial");
            Check(removed != null && removed.After == "<移除>",
                "删除字段标 <移除>", ref failures);

            // 空快照不能崩。
            Check(ProjectAssetService.DiffSnapshots(null, null).Count == 0, "空快照 diff 为空", ref failures);
            Check(ProjectAssetService.DiffSnapshots("not-json", "{\"a\":1}").Count > 0,
                "非法快照当整段比较，不抛", ref failures);
        }

        /// <summary>页内审计流水：能读到之前写入的记录。</summary>
        private static void RunAuditRowChecks(ref int failures)
        {
            var temp = Path.Combine(Path.GetTempPath(), "vm-asset-audit-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temp);
                var sourceDir = Path.Combine(temp, "src");
                Directory.CreateDirectory(sourceDir);

                var solution = new SolutionModel
                {
                    SolutionName = "AssetAudit",
                    SolutionFilePath = Path.Combine(temp, "AssetAudit.vms"),
                };
                var workspace = new WorkspaceContext();
                workspace.SwitchSolution(solution);

                var importer = new ProjectAssetImporter(workspace);
                var imageSource = Path.Combine(sourceDir, "probe.png");
                File.Copy(@"D:\C#\VM\Image\board\board_01.png", imageSource);
                importer.TryImport(imageSource, "image", out var imageId, out _);

                var policy = new ScadaAccessPolicy();
                var auditDir = Path.Combine(temp, "Audit");
                var service = new ProjectAssetService(
                    new SolutionAssetWriter(solution), policy, new ScadaAuditWriter(auditDir));

                var name = solution.ImageAssets[0].Name;
                policy.Login(new ScadaUserInfo(
                    "admin", ScadaGroupDefaults.AdminTemplateId,
                    ScadaPermissionKeys.BuiltInKeys.ToArray(), "模板·管理员"));
                service.TrySetArchived(imageId, true, out _);

                var reader = new ScadaAuditReader(auditDir);
                var result = reader.Read();
                Check(result.Rows.Count > 0, $"审计读到 {result.Rows.Count} 条", ref failures);

                var mine = result.Rows.Where(r => r.Subject == name).ToList();
                Check(mine.Count > 0, $"能按对象名查到「{name}」的记录", ref failures);
                Check(mine.Any(r => r.ActionText == "归档" && r.OutcomeText == "成功"),
                    "归档动作与结果可见", ref failures);
                Check(mine.Any(r => r.Actor == "admin"), "审计记录带操作者", ref failures);
            }
            catch (Exception ex)
            {
                Check(false, "审计流水断言异常：" + ex.Message, ref failures);
            }
            finally
            {
                try { Directory.Delete(temp, recursive: true); } catch { }
            }
        }

        /// <summary>
        /// 归档必须真的挡住运行期解析：这是"归档 = 退出生产"的唯一判据。
        /// 插件全部经 ProjectAssetPathResolver → context.Assets → SolutionProjectAssetResolver，
        /// 所以两层都要断言（解析器本身 + 插件口径的包装器）。
        /// </summary>
        private static void RunArchivedRuntimeChecks(ref int failures)
        {
            var temp = Path.Combine(Path.GetTempPath(), "vm-asset-runtime-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temp);
                var sourceDir = Path.Combine(temp, "src");
                Directory.CreateDirectory(sourceDir);

                var solution = new SolutionModel
                {
                    SolutionName = "AssetRuntime",
                    SolutionFilePath = Path.Combine(temp, "AssetRuntime.vms"),
                };
                var workspace = new WorkspaceContext();
                workspace.SwitchSolution(solution);

                var importer = new ProjectAssetImporter(workspace);
                var imageSource = Path.Combine(sourceDir, "runtime.png");
                File.Copy(@"D:\C#\VM\Image\board\board_01.png", imageSource);
                importer.TryImport(imageSource, "image", out var imageId, out _);
                var name = solution.ImageAssets[0].Name;

                var resolver = new SolutionProjectAssetResolver(solution);
                var context = new VisionMaster.Services.ExecutionContext(null!) { Workspace = workspace };

                Check(resolver.TryResolve(imageId, out var before, out _) && before != null,
                    "未归档时解析成功", ref failures);
                Check(ProjectAssetPathResolver.TryResolve(context, imageId.ToString(), null, "image", out var beforePath, out var beforeError)
                      && File.Exists(beforePath),
                    "未归档时插件口径能拿到真实文件" + (string.IsNullOrEmpty(beforeError) ? string.Empty : "：" + beforeError),
                    ref failures);

                solution.AssetLifecycle ??= new List<AssetLifecycleState>();
                solution.AssetLifecycle.Add(new AssetLifecycleState
                {
                    AssetId = imageId,
                    Kind = "image",
                    State = "已归档",
                    ChangedBy = "probe",
                });

                Check(!resolver.TryResolve(imageId, out var archivedRef, out var archivedError),
                    "已归档时解析被拒绝", ref failures);
                Check(archivedRef == null, "被拒绝时不返回半成品引用", ref failures);
                Check(archivedError != null && archivedError.Contains("已归档") && archivedError.Contains(name),
                    $"拒绝原因点名资产与状态（实际：{archivedError}）", ref failures);

                Check(!ProjectAssetPathResolver.TryResolve(context, imageId.ToString(), null, "image", out _, out var pluginError)
                      && pluginError != null && pluginError.Contains("已归档"),
                    "插件口径同样被拒绝（不是只拦了宿主自己）", ref failures);

                // 旧路径回退：归档资产带 AssetId 时不许悄悄退回旧路径继续跑。
                Check(!ProjectAssetPathResolver.TryResolve(context, imageId.ToString(), "Image/whatever.png", "image", out _, out _),
                    "归档资产带旧路径也不放行", ref failures);

                // 恢复后必须重新可用（归档是可逆动作，不能一次归档就永久废掉）。
                solution.AssetLifecycle.Clear();
                Check(ProjectAssetPathResolver.TryResolve(context, imageId.ToString(), null, "image", out var restoredPath, out _)
                      && File.Exists(restoredPath),
                    "恢复后解析重新可用", ref failures);
            }
            catch (Exception ex)
            {
                Check(false, "归档运行期断言异常：" + ex.Message, ref failures);
            }
            finally
            {
                try { Directory.Delete(temp, recursive: true); } catch { }
            }
        }

        private static void RunLifecycleChecks(ref int failures)
        {
            var temp = Path.Combine(Path.GetTempPath(), "vm-asset-life-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temp);
                var sourceDir = Path.Combine(temp, "src");
                Directory.CreateDirectory(sourceDir);

                var solution = new SolutionModel
                {
                    SolutionName = "AssetLifecycle",
                    SolutionFilePath = Path.Combine(temp, "AssetLifecycle.vms"),
                };
                var workspace = new WorkspaceContext();
                workspace.SwitchSolution(solution);

                var importer = new ProjectAssetImporter(workspace);
                var imageSource = Path.Combine(sourceDir, "probe.png");
                File.Copy(@"D:\C#\VM\Image\board\board_01.png", imageSource);
                Check(importer.TryImport(imageSource, "image", out var imageId, out _),
                    "生命周期前置：图像导入", ref failures);

                var policy = new ScadaAccessPolicy();
                var auditDir = Path.Combine(temp, "Audit");
                var audit = new ScadaAuditWriter(auditDir);
                var writer = new SolutionAssetWriter(solution);
                var service = new ProjectAssetService(writer, policy, audit);

                // 未登录：所有写操作必须被拒，且审计里要出现「被拒绝」。
                Check(!service.TrySetDefault(imageId, out var deniedDefault) && deniedDefault.Contains("权限"),
                    "未登录时设为默认被拒并说明权限", ref failures);
                Check(!service.TryToggleFreeze(imageId, out _, out var deniedFreeze) && deniedFreeze.Contains("权限"),
                    "未登录时冻结被拒并说明权限", ref failures);
                Check(!service.CheckWritePermission("导入资产", out var deniedImport) && deniedImport.Contains("权限"),
                    "未登录时导入被拒并说明权限", ref failures);

                // 登录必须带显式权限集：Login(name, role) 只映射一条 Legacy 权限，
                // 不含「改结构」，用它做断言会得到"登录了仍没权限"的假失败。
                policy.Login(new ScadaUserInfo(
                    "admin",
                    ScadaGroupDefaults.AdminTemplateId,
                    ScadaPermissionKeys.BuiltInKeys.ToArray(),
                    "模板·管理员"));

                Check(service.TrySetDefault(imageId, out var defaultError),
                    "登录后可设为默认" + (string.IsNullOrEmpty(defaultError) ? string.Empty : "：" + defaultError), ref failures);
                Check(solution.AssetDefaults.Any(p => p.AssetId == imageId),
                    "默认指针写入方案", ref failures);

                Check(service.TryToggleFreeze(imageId, out var frozen, out var freezeError) && frozen,
                    "可冻结" + (string.IsNullOrEmpty(freezeError) ? string.Empty : "：" + freezeError), ref failures);
                Check(solution.ImageAssets[0].IsFrozen, "冻结状态写入方案", ref failures);
                Check(!service.TryRecordVersion(imageId, "改内容", "冻结后不应允许", out var frozenWriteError)
                      && frozenWriteError.Contains("冻结"),
                    "冻结后拒绝内容写入", ref failures);

                Check(service.TryToggleFreeze(imageId, out var unfrozen, out _) && !unfrozen,
                    "可解冻", ref failures);
                Check(service.TryRecordVersion(imageId, "改内容", "解冻后允许", out var recordError),
                    "解冻后可写版本" + (string.IsNullOrEmpty(recordError) ? string.Empty : "：" + recordError), ref failures);

                var history = AssetVersionLog.Of(solution, imageId);
                Check(history.Count >= 4, $"版本历史累计 ≥4 条（实际 {history.Count}）", ref failures);
                Check(history.Any(r => r.Action == "设为默认")
                      && history.Any(r => r.Action == "冻结")
                      && history.Any(r => r.Action == "解冻"),
                    "版本历史记录了默认/冻结/解冻", ref failures);
                Check(history.All(r => !string.IsNullOrWhiteSpace(r.Actor)),
                    "版本历史每条都有操作者", ref failures);

                // 回滚：文件资产现在能真回滚（旧文件在 Assets\Versions），
                // 但"版本库里没有文件本体"的旧记录必须明确拒绝，不能假装成功。
                var rollbackError = string.Empty;
                var rolledBack = service.TryRollback(imageId, history[0].RecordId, out rollbackError);
                Check(rolledBack || rollbackError.Contains("没有保留文件本体"),
                    "文件资产回滚要么真还原、要么明确说明没有文件本体（实际：" +
                    (rolledBack ? "已回滚" : rollbackError) + "）", ref failures);

                // 标定资产可以真回滚。
                var calibration = new CalibrationAsset
                {
                    Name = "LifecycleCalib",
                    CalibrationId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
                    Transform = new CalibrationTransform { MmPerPixel = 0.05, SourceImageWidth = 640, SourceImageHeight = 480 },
                };
                solution.Calibrations.Add(calibration);
                Check(service.TryRecordVersion(calibration.CalibrationId, "标定", "首次", out _),
                    "标定资产写版本记录", ref failures);

                var calibHistory = AssetVersionLog.Of(solution, calibration.CalibrationId);
                Check(calibHistory.Count == 1, "标定历史 1 条", ref failures);

                calibration.Transform = new CalibrationTransform { MmPerPixel = 0.2, SourceImageWidth = 640, SourceImageHeight = 480 };
                Check(service.TryRollback(calibration.CalibrationId, calibHistory[0].RecordId, out var calibRollbackError),
                    "标定资产可回滚" + (string.IsNullOrEmpty(calibRollbackError) ? string.Empty : "：" + calibRollbackError), ref failures);
                Check(Math.Abs(solution.Calibrations[0].Transform.MmPerPixel - 0.05) < 1e-9,
                    "回滚后像素当量恢复 0.05", ref failures);

                // 审计：拒绝与允许都要落盘。
                var auditFile = Directory.Exists(auditDir)
                    ? Directory.GetFiles(auditDir, "Audit-*.csv").FirstOrDefault()
                    : null;
                Check(auditFile != null && File.Exists(auditFile), "审计文件已生成", ref failures);
                if (auditFile != null)
                {
                    var csv = File.ReadAllText(auditFile);
                    Check(csv.Contains("被拒绝") || csv.Contains("拒绝"), "审计记录包含被拒绝操作", ref failures);
                    Check(csv.Contains("设为默认") && csv.Contains("冻结"), "审计记录包含允许的操作", ref failures);
                    Check(csv.Contains("admin"), "审计记录包含操作者", ref failures);
                }

                var json = SolutionService.Serialize(solution);
                Check(json.Contains("\"AssetVersionLog\"") && json.Contains("\"AssetDefaults\""),
                    "版本历史与默认指针进入 .vms 序列化", ref failures);

                RenderLifecycleView(workspace, solution, auditDir, ref failures);
                RenderRecipeEditorView(workspace, ref failures);
            }
            catch (Exception ex)
            {
                Check(false, "生命周期断言异常：" + ex.Message, ref failures);
            }
            finally
            {
                try { Directory.Delete(temp, recursive: true); } catch { }
            }
        }

        /// <summary>
        /// 配方内容编辑区的截图：往方案里放一条文本配方，概览页必须出现编辑框与保存按钮。
        /// </summary>
        private static void RenderRecipeEditorView(WorkspaceContext workspace, ref int failures)
        {
            var solution = workspace.CurrentSolution;
            if (solution == null) return;

            var recipe = new RecipeDefinition
            {
                Name = "颜色配方A",
                Code = "COLOR-A",
                TextContent = "红白线 = 红,白\r\n红黑线 = 红,黑\r\n蓝白线 = 蓝,白",
                ContentFormat = "text",
            };
            solution.EnsureRecipes().Add(recipe);

            var vm = new ProjectAssetsViewModel(workspace);
            vm.SelectTypeCommand.Execute("recipe");
            var asset = vm.VisibleAssets.FirstOrDefault(a => a.Id == recipe.RecipeId.ToString());
            Check(asset != null && asset.IsTextRecipe, "文本配方出现在配方页且可编辑", ref failures);
            if (asset == null) return;

            vm.SelectedAsset = asset;
            Check(vm.RecipeContentDraft.Contains("红白线"), "编辑框草稿装载了配方原文", ref failures);

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

            try
            {
                window.Show();
                Pump(600);
                view.UpdateLayout();
                vm.SelectedDetailTabIndex = 0;
                view.UpdateLayout();
                Pump(250);

                var bitmap = Render(view, (int)Math.Round(view.ActualWidth), (int)Math.Round(view.ActualHeight), 2.0);
                var path = Path.Combine(OutputDirectory(Array.Empty<string>()), "wpf-project-assets-recipe-editor.png");
                Save(bitmap, path);
                Console.WriteLine($"  [render] {path}");
            }
            finally
            {
                window.Close();
                Pump(80);
            }
        }

        /// <summary>
        /// 用真实写操作产生的方案渲染一次「版本」页签，证明默认/冻结/历史在界面上真的出得来。
        /// </summary>
        private static void RenderLifecycleView(
            WorkspaceContext workspace, SolutionModel solution, string auditDir, ref int failures)
        {
            var vm = new ProjectAssetsViewModel(workspace, auditDirectory: auditDir);
            vm.SelectTypeCommand.Execute("image");
            var asset = vm.VisibleAssets.FirstOrDefault(a => a.Name.Contains("probe"));
            Check(asset != null, "生命周期方案里能找到图像资产", ref failures);
            if (asset == null) return;

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

            try
            {
                window.Show();
                Pump(600);
                view.UpdateLayout();

                vm.SelectedDetailTabIndex = 3;
                view.UpdateLayout();
                Pump(200);

                Check(asset.HasVersions, $"版本页签有 {asset.Versions.Count} 条记录", ref failures);
                Check(asset.Versions.Any(v => v.CanRollback), "版本卡带可回滚标记", ref failures);
                Check(asset.IsDefault, "默认标记来自 AssetDefaults", ref failures);

                // 选中一条历史记录 → 字段差异面板必须有内容。
                vm.SelectedVersion = asset.Versions.FirstOrDefault(v => v.CanRollback);
                view.UpdateLayout();
                Pump(150);
                Check(vm.VersionDiff.Count > 0, $"字段差异面板有 {vm.VersionDiff.Count} 行", ref failures);
                Check(!string.IsNullOrWhiteSpace(vm.VersionDiffHint), "差异说明非空", ref failures);

                vm.RefreshAuditCommand.Execute();
                view.UpdateLayout();
                Pump(150);
                Check(!string.IsNullOrWhiteSpace(vm.AuditHint), "审计提示非空", ref failures);

                // 审计页签截图（真实读取，不伪造审计目录）
                vm.SelectedDetailTabIndex = 4;
                view.UpdateLayout();
                Pump(200);
                Console.WriteLine($"  [audit] rows={vm.AuditRows.Count} hint=\"{vm.AuditHint}\"");

                var auditBitmap = Render(view, (int)Math.Round(view.ActualWidth), (int)Math.Round(view.ActualHeight), 2.0);
                var auditPath = Path.Combine(OutputDirectory(Array.Empty<string>()), "wpf-project-assets-audit.png");
                Save(auditBitmap, auditPath);
                Console.WriteLine($"  [render] {auditPath}");

                // 引用页签的可点性由主渲染（真实方案标定资产）出图，这里不重复截。

                // 回到版本页签（带选中记录 + 差异面板）再截一张
                vm.SelectedDetailTabIndex = 3;
                view.UpdateLayout();
                Pump(200);

                var bitmap = Render(view, (int)Math.Round(view.ActualWidth), (int)Math.Round(view.ActualHeight), 2.0);
                var path = Path.Combine(OutputDirectory(Array.Empty<string>()), "wpf-project-assets-versions.png");
                Save(bitmap, path);
                Console.WriteLine($"  [render] {path}");
            }
            finally
            {
                window.Close();
                Pump(80);
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
                File.Copy(@"D:\C#\VM\Image\board\board_01.png", imageSource);

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
