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
using Core.Interfaces;
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
                RunExportChecks(ref failures);
                RunArchiveDeleteChecks(ref failures);
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
