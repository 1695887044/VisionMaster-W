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
using Prism.Dialogs;
using Prism.Mvvm;
using Core.Interfaces;
using Core.Interfaces.Assets;
using VisionMaster.Models;
using VisionMaster.Models.Assets;
using VisionMaster.Scada;
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
                RunLifecycleChecks(ref failures);
                RunFilterSortChecks(ref failures);
                RunLifecycleFilterChecks(ref failures);
                RunReferenceNavigationChecks(ref failures);
                RunExportChecks(ref failures);
                RunArchiveDeleteChecks(ref failures);
                RunArchivedRuntimeChecks(ref failures);
                RunRecipeLifecycleChecks(ref failures);
                RunTextRecipeChecks(ref failures);
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

        /// <summary>筛选与排序必须真的改变 VisibleAssets，而不是只换按钮文案。</summary>
        private static void RunFilterSortChecks(ref int failures)
        {
            try
            {
                var workspace = new WorkspaceContext();
                var solutionService = new SolutionService();
                var solutionPath = @"D:\C#\VM\解决方案\智能装配检测线\智能装配检测线.vms";
                var load = solutionService.LoadAsync(solutionPath).GetAwaiter().GetResult();
                if (!load.Success || load.Data == null)
                {
                    Check(false, "筛选断言：加载方案失败：" + load.Message, ref failures);
                    return;
                }
                load.Data.SolutionFilePath = solutionPath;
                workspace.SwitchSolution(load.Data);

                var vm = new ProjectAssetsViewModel(workspace);

                vm.SelectTypeCommand.Execute("image");
                var imageTotal = vm.VisibleAssets.Count;
                Check(imageTotal > 0, $"图像页有 {imageTotal} 条可筛选", ref failures);
                Check(vm.CameraFilterOptions.Count == 1,
                    $"图像资产没有相机值时相机下拉只有「全部」（实际 {vm.CameraFilterOptions.Count}）", ref failures);
                Check(vm.CameraFilterOptions.First().Label == "全部相机",
                    "下拉首项文案是「全部相机」（不是空白）", ref failures);
                Check(vm.SelectedCameraOption?.Label == "全部相机",
                    "默认选中「全部相机」而不是空", ref failures);

                vm.SelectTypeCommand.Execute("calibration");
                Check(vm.VisibleAssets.Count > 0, "标定页有数据", ref failures);
                var cameraOptions = vm.CameraFilterOptions.Select(o => o.Value).ToArray();
                Check(cameraOptions.Contains("NET-STATION-02"),
                    "相机下拉出现真实相机 NET-STATION-02（不是硬编码）", ref failures);

                vm.CameraFilter = "NET-STATION-02";
                Check(vm.VisibleAssets.Count > 0 && vm.VisibleAssets.All(a => a.Camera == "NET-STATION-02"),
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
                File.Copy(@"D:\C#\VM\解决方案\智能装配检测线\Image\board_01.png", a);
                File.Copy(@"D:\C#\VM\解决方案\智能装配检测线\Image\board_02.png", b);
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
                var workspace = new WorkspaceContext();
                var solutionService = new SolutionService();
                var solutionPath = @"D:\C#\VM\解决方案\智能装配检测线\智能装配检测线.vms";
                var load = solutionService.LoadAsync(solutionPath).GetAwaiter().GetResult();
                if (!load.Success || load.Data == null)
                {
                    Check(false, "引用跳转：加载方案失败：" + load.Message, ref failures);
                    return;
                }
                load.Data.SolutionFilePath = solutionPath;
                workspace.SwitchSolution(load.Data);

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
                File.Copy(@"D:\C#\VM\解决方案\智能装配检测线\Image\board_01.png", imageSource);
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
                File.Copy(@"D:\C#\VM\解决方案\智能装配检测线\Image\board_01.png", imageSource);
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
                      && kindError.Contains("不是文本配方"),
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
                File.Copy(@"D:\C#\VM\解决方案\智能装配检测线\Image\board_01.png", imageSource);
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
                File.Copy(@"D:\C#\VM\解决方案\智能装配检测线\Image\board_01.png", imageSource);
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
                File.Copy(@"D:\C#\VM\解决方案\智能装配检测线\Image\board_01.png", imageSource);
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

                // 回滚：文件资产必须明确拒绝，不能假装成功。
                Check(!service.TryRollback(imageId, history[0].RecordId, out var rollbackError)
                      && rollbackError.Contains("标定"),
                    "文件资产回滚被明确拒绝", ref failures);

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
