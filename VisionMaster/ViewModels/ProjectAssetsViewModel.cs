using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Models;
using VisionMaster.Models.Assets;
using VisionMaster.Scada;
using VisionMaster.Services;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 项目资产管理：读取当前方案的真实标定/模板/模型/配方/图像；
    /// 模板/模型/图像支持导入到方案目录，保存方案后随 .vms 落盘。
    /// 默认/冻结/版本/权限/审计走 ProjectAssetService 单一写入口。
    /// </summary>
    public sealed class ProjectAssetsViewModel : BindableBase, IDialogAware
    {
        private readonly IReadOnlyWorkspaceContext _workspace;
        private readonly SolutionProjectAssetCatalog _catalog;
        private readonly ProjectAssetImporter _importer;
        private readonly ProjectAssetExporter _exporter;
        private readonly ScadaAuditReader _auditReader;
        private readonly ProjectAssetService? _assetService;
        private readonly List<ProjectAssetItem> _allAssets = new();

        private AssetTypeTab? _selectedTypeTab;
        private ProjectAssetItem? _selectedAsset;
        private string _selectedType = "calibration";
        private string _searchText = string.Empty;
        private int _selectedDetailTabIndex;
        private string _actionMessage = string.Empty;

        public ProjectAssetsViewModel(
            IReadOnlyWorkspaceContext workspace,
            ProjectAssetService? assetService = null,
            string? auditDirectory = null)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _catalog = new SolutionProjectAssetCatalog(workspace);
            _importer = new ProjectAssetImporter(workspace);
            _exporter = new ProjectAssetExporter(workspace);
            // 审计目录可注入：宿主用默认（程序目录 Audit），探针指到临时目录才能验证"页面能看到刚才那条".
            _auditReader = new ScadaAuditReader(auditDirectory);
            _assetService = assetService;

            TypeTabs = new ObservableCollection<AssetTypeTab>
            {
                new("all", "资产总览"),
                new("calibration", "标定资产"),
                new("template", "模板资产"),
                new("model", "模型资产"),
                new("recipe", "配方资产"),
                new("image", "图像资产")
            };
            TypeFilters = new ObservableCollection<AssetTypeFilter>
            {
                new("calibration", "标定资产"),
                new("template", "模板资产"),
                new("model", "模型资产"),
                new("recipe", "配方资产"),
                new("image", "图像资产")
            };

            SelectTypeCommand = new DelegateCommand<string>(kind => SelectType(string.IsNullOrWhiteSpace(kind) ? "all" : kind));
            ImportAssetCommand = new DelegateCommand(ImportAsset);
            RefreshCommand = new DelegateCommand(ReloadAssets);
            ToggleSortCommand = new DelegateCommand(ToggleSort);
            ClearFiltersCommand = new DelegateCommand(ClearFilters);
            SetDefaultCommand = new DelegateCommand(SetDefault);
            ToggleFreezeCommand = new DelegateCommand(ToggleFreeze);
            RollbackCommand = new DelegateCommand(RollbackVersion);
            ToggleArchiveCommand = new DelegateCommand(ToggleArchive);
            DeleteAssetCommand = new DelegateCommand(DeleteAsset);
            RefreshAuditCommand = new DelegateCommand(RefreshAudit);
            SelectLifecycleCommand = new DelegateCommand<string>(kind => SelectLifecycle(string.IsNullOrWhiteSpace(kind) ? "全部" : kind));
            GoToReferenceCommand = new DelegateCommand<AssetReference>(GoToReference);
            ImportNewVersionCommand = new DelegateCommand(ImportNewVersion);
            SaveRecipeContentCommand = new DelegateCommand(SaveRecipeContent);
            ExportCommand = new DelegateCommand(ExportAsset);
            CopyIdCommand = new DelegateCommand(CopyId);
            CloseCommand = new DelegateCommand(() => RequestClose.Invoke(ButtonResult.Cancel));

            ReloadAssets();
        }

        public string Title => "项目资产管理";
        public DialogCloseListener RequestClose { get; set; }

        public ObservableCollection<AssetTypeTab> TypeTabs { get; }
        public ObservableCollection<AssetTypeFilter> TypeFilters { get; }
        public ObservableCollection<ProjectAssetItem> VisibleAssets { get; } = new();

        public AssetTypeTab? SelectedTypeTab
        {
            get => _selectedTypeTab;
            set
            {
                if (!SetProperty(ref _selectedTypeTab, value) || value == null) return;
                SelectType(value.Kind);
            }
        }

        public ProjectAssetItem? SelectedAsset
        {
            get => _selectedAsset;
            set
            {
                if (!SetProperty(ref _selectedAsset, value)) return;
                SelectedDetailTabIndex = 0;
                RaisePropertyChanged(nameof(HasSelectedAsset));
                RaisePropertyChanged(nameof(IsTextRecipeSelected));
                RecipeContentDraft = value?.RecipeContent ?? string.Empty;
            }
        }

        public bool HasSelectedAsset => SelectedAsset != null;

        /// <summary>当前选中的是"内容可编辑的配方"（text / bead）。</summary>
        public bool IsTextRecipeSelected => SelectedAsset?.IsTextRecipe == true;

        private string _recipeContentDraft = string.Empty;
        /// <summary>配方内容编辑草稿：改完点「保存配方内容」才落方案（与配置弹窗同一套草稿语义）。</summary>
        public string RecipeContentDraft
        {
            get => _recipeContentDraft;
            set => SetProperty(ref _recipeContentDraft, value ?? string.Empty);
        }

        public int SelectedDetailTabIndex
        {
            get => _selectedDetailTabIndex;
            set => SetProperty(ref _selectedDetailTabIndex, value);
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (!SetProperty(ref _searchText, value ?? string.Empty)) return;
                ApplyFilter();
            }
        }

        public string ListTitle { get; private set; } = "标定资产";
        public string ListCountText { get; private set; } = "0 项";
        public string FooterText { get; private set; } = "显示 0 / 0 项（方案资产 0 项）";
        public string HealthPercent { get; private set; } = "0%";
        public string HealthText { get; private set; } = "0 有效 / 0 · 0 待处理\n0 归档 · 0 未绑定 · 0 冻结";
        public int ValidCount { get; private set; }
        public int PendingCount { get; private set; }
        public int ArchivedCount { get; private set; }
        public int LockedCount { get; private set; }

        public string ActionMessage
        {
            get => _actionMessage;
            private set => SetProperty(ref _actionMessage, value);
        }

        /// <summary>当前资产的审计流水（按对象名匹配，见 RefreshAudit）。</summary>
        public ObservableCollection<AssetAuditRow> AuditRows { get; } = new();

        private string _auditHint = "选中资产后点「刷新审计」";
        public string AuditHint
        {
            get => _auditHint;
            private set => SetProperty(ref _auditHint, value);
        }

        /// <summary>版本页当前选中记录的字段级差异（切换记录时重算）。</summary>
        public ObservableCollection<AssetFieldDiff> VersionDiff { get; } = new();

        private AssetVersion? _selectedVersion;
        public AssetVersion? SelectedVersion
        {
            get => _selectedVersion;
            set
            {
                if (!SetProperty(ref _selectedVersion, value)) return;
                RefreshVersionDiff();
            }
        }

        private string _versionDiffHint = "选中一条版本记录查看逐字段差异";
        public string VersionDiffHint
        {
            get => _versionDiffHint;
            private set => SetProperty(ref _versionDiffHint, value);
        }

        private void RefreshVersionDiff()
        {
            VersionDiff.Clear();

            if (SelectedAsset == null || SelectedVersion == null)
            {
                VersionDiffHint = "选中一条版本记录查看逐字段差异";
                return;
            }

            var solution = _workspace.CurrentSolution;
            if (solution == null || !Guid.TryParse(SelectedAsset.Id, out var assetId))
            {
                VersionDiffHint = "当前没有方案，无法读版本快照";
                return;
            }

            var records = AssetVersionLog.Of(solution, assetId);
            var current = records.FirstOrDefault(r => r.RecordId.ToString() == SelectedVersion.RecordId);
            if (current == null)
            {
                VersionDiffHint = "这条记录没有快照（早期记录只存动作）";
                return;
            }

            // 与"上一条更早的记录"比：history 已按时间倒序，所以往后找第一条。
            var index = records.ToList().FindIndex(r => r.RecordId == current.RecordId);
            var previous = index >= 0 && index + 1 < records.Count ? records[index + 1] : null;

            var diffs = ProjectAssetService.DiffSnapshots(previous?.SnapshotJson, current.SnapshotJson);
            var changed = 0;
            foreach (var diff in diffs)
            {
                VersionDiff.Add(diff);
                if (diff.Changed) changed++;
            }

            VersionDiffHint = diffs.Count == 0
                ? "两条记录都没有可比较的快照"
                : previous == null
                    ? $"与空快照比较：{changed} 个字段有值"
                    : $"与 v{Math.Max(1, previous.Version)}（{previous.Action}）比较：{changed} 个字段变化";
        }

        public DelegateCommand<string> SelectTypeCommand { get; }
        public DelegateCommand ImportAssetCommand { get; }
        public DelegateCommand RefreshCommand { get; }
        public DelegateCommand ToggleSortCommand { get; }
        public DelegateCommand ClearFiltersCommand { get; }
        public DelegateCommand SetDefaultCommand { get; }
        public DelegateCommand ToggleFreezeCommand { get; }
        public DelegateCommand RollbackCommand { get; }
        public DelegateCommand ToggleArchiveCommand { get; }
        public DelegateCommand DeleteAssetCommand { get; }
        public DelegateCommand RefreshAuditCommand { get; }
        public DelegateCommand<string> SelectLifecycleCommand { get; }
        public DelegateCommand<AssetReference> GoToReferenceCommand { get; }
        public DelegateCommand ImportNewVersionCommand { get; }
        public DelegateCommand SaveRecipeContentCommand { get; }
        public DelegateCommand ExportCommand { get; }
        public DelegateCommand CopyIdCommand { get; }
        public DelegateCommand CloseCommand { get; }

        public bool CanCloseDialog() => true;
        public void OnDialogClosed() { }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            ReloadAssets();
        }

        private void ReloadAssets()
        {
            _allAssets.Clear();
            _allAssets.AddRange(_catalog.Build());
            RefreshCounts();
            RefreshFilterOptions();

            if (_selectedTypeTab == null)
                _selectedTypeTab = TypeTabs.FirstOrDefault(t => t.Kind == "calibration") ?? TypeTabs[0];

            foreach (var filter in TypeFilters)
                filter.IsSelected = filter.Kind == _selectedType;

            ApplyFilter();

            var solution = _workspace.CurrentSolution;
            ActionMessage = solution == null
                ? "当前没有打开方案"
                : $"当前方案：{solution.SolutionName} · 共 {_allAssets.Count} 个真实资产";
        }

        private void SelectType(string kind)
        {
            _selectedType = kind;
            var tab = TypeTabs.FirstOrDefault(t => t.Kind == kind) ?? TypeTabs[0];
            _selectedTypeTab = tab;
            RaisePropertyChanged(nameof(SelectedTypeTab));
            foreach (var filter in TypeFilters)
                filter.IsSelected = filter.Kind == kind;
            RefreshFilterOptions();
            ApplyFilter();
        }

        private string _cameraFilter = string.Empty;
        /// <summary>相机筛选（空 = 全部）。选项来自当前页签里真实出现的相机值。</summary>
        public string CameraFilter
        {
            get => _cameraFilter;
            set
            {
                if (!SetProperty(ref _cameraFilter, value ?? string.Empty)) return;
                ApplyFilter();
            }
        }

        /// <summary>
        /// 下拉绑定用 SelectedItem（不是 SelectedValue）：WPF 里 `SelectedValue=""` 常被当成
        /// "未选择"，空项就显示成空白框 —— 用户看不到"全部相机"这几个字。
        /// </summary>
        public FilterOption? SelectedCameraOption
        {
            get => CameraFilterOptions.FirstOrDefault(o => o.Value == _cameraFilter)
                   ?? CameraFilterOptions.FirstOrDefault();
            set => CameraFilter = value?.Value ?? string.Empty;
        }

        public FilterOption? SelectedTagOption
        {
            get => TagFilterOptions.FirstOrDefault(o => o.Value == _tagFilter)
                   ?? TagFilterOptions.FirstOrDefault();
            set => TagFilter = value?.Value ?? string.Empty;
        }

        /// <summary>下拉项（空值 = 全部）。</summary>
        public ObservableCollection<FilterOption> CameraFilterOptions { get; } = new();
        public ObservableCollection<FilterOption> TagFilterOptions { get; } = new();

        private string _tagFilter = string.Empty;
        /// <summary>标签筛选（空 = 全部）。标签取自资产作用域首段（ST1/ST2/… 或整线）。</summary>
        public string TagFilter
        {
            get => _tagFilter;
            set
            {
                if (!SetProperty(ref _tagFilter, value ?? string.Empty)) return;
                ApplyFilter();
            }
        }

        private void RefreshFilterOptions()
        {
            // 选项必须来自**当前页签**的资产：全量取会让"图像页出现相机下拉、选中后 0 条"，
            // 用户只看到空列表却不知道为什么。
            var scoped = _allAssets.Where(a => _selectedType == "all" || a.Kind == _selectedType).ToList();

            var cameras = scoped
                .Select(a => a.Camera)
                .Where(c => !string.IsNullOrWhiteSpace(c) && c != "—")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var tags = scoped
                .Select(TagOf)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();

            CameraFilterOptions.Clear();
            CameraFilterOptions.Add(new FilterOption(string.Empty, "全部相机"));
            foreach (var camera in cameras)
                CameraFilterOptions.Add(new FilterOption(camera, camera));

            TagFilterOptions.Clear();
            TagFilterOptions.Add(new FilterOption(string.Empty, "全部标签"));
            foreach (var tag in tags)
                TagFilterOptions.Add(new FilterOption(tag, tag));

            // 选项消失时回落到"全部"，避免筛出空列表却看不出原因。
            if (!CameraFilterOptions.Any(o => o.Value == _cameraFilter)) _cameraFilter = string.Empty;
            if (!TagFilterOptions.Any(o => o.Value == _tagFilter)) _tagFilter = string.Empty;
            RaisePropertyChanged(nameof(CameraFilter));
            RaisePropertyChanged(nameof(TagFilter));
            RaisePropertyChanged(nameof(SelectedCameraOption));
            RaisePropertyChanged(nameof(SelectedTagOption));
        }

        /// <summary>排序键：updated = 最近更新优先；name = 名称升序。</summary>
        private string _sortMode = "updated";
        public string SortButtonText => _sortMode == "updated" ? "最近更新" : "名称排序";

        private void ToggleSort()
        {
            _sortMode = _sortMode == "updated" ? "name" : "updated";
            RaisePropertyChanged(nameof(SortButtonText));
            ApplyFilter();
        }

        private void ClearFilters()
        {
            _searchText = string.Empty;
            _cameraFilter = string.Empty;
            _tagFilter = string.Empty;
            RaisePropertyChanged(nameof(SearchText));
            RaisePropertyChanged(nameof(CameraFilter));
            RaisePropertyChanged(nameof(TagFilter));
            LifecycleFilter = "全部";
            ApplyFilter();
            ActionMessage = "已清除搜索 / 相机 / 标签 / 生命周期筛选";
        }

        /// <summary>作用域首段当标签：`ST3 · 精密测量` → `ST3`，`整线` → `整线`。</summary>
        private static string TagOf(ProjectAssetItem item)
        {
            var scope = (item.Scope ?? string.Empty).Trim();
            if (scope.Length == 0) return string.Empty;
            var cut = scope.IndexOfAny(new[] { '·', ' ', '/', '\\' });
            return cut > 0 ? scope[..cut].Trim() : scope;
        }

        /// <summary>生命周期筛选：全部 / 有效 / 已归档。左侧三行计数点一下切这里。</summary>
        private string _lifecycleFilter = "全部";
        public string LifecycleFilter
        {
            get => _lifecycleFilter;
            private set
            {
                if (!SetProperty(ref _lifecycleFilter, value ?? "全部")) return;
                RaisePropertyChanged(nameof(IsAllLifecycle));
                RaisePropertyChanged(nameof(IsValidLifecycle));
                RaisePropertyChanged(nameof(IsArchivedLifecycle));
                ApplyFilter();
            }
        }

        public bool IsAllLifecycle => _lifecycleFilter == "全部";
        public bool IsValidLifecycle => _lifecycleFilter == "有效";
        public bool IsArchivedLifecycle => _lifecycleFilter == "已归档";

        private void SelectLifecycle(string filter)
        {
            // 再点一次当前项 = 取消筛选（比"必须点全部"少一步）。
            LifecycleFilter = _lifecycleFilter == filter ? "全部" : filter;
        }

        private void GoToReference(AssetReference? reference)
        {
            if (reference == null) return;
            if (!reference.CanNavigate)
            {
                ActionMessage = "这条引用没有可跳转的步骤";
                return;
            }

            var solution = _workspace.CurrentSolution;
            if (solution == null)
            {
                ActionMessage = "当前没有打开方案";
                return;
            }

            var flow = solution.Flows.FirstOrDefault(f =>
                string.Equals(f.FlowName, reference.FlowName, StringComparison.Ordinal));
            if (flow == null)
            {
                ActionMessage = $"找不到流程「{reference.FlowName}」";
                return;
            }

            var step = FindStepDeep(flow.Steps, reference.StepName!);
            if (step == null)
            {
                ActionMessage = $"流程「{reference.FlowName}」里找不到步骤「{reference.StepName}」";
                return;
            }

            // VM 只持有只读工作区上下文（断言宿主也用它）；真正切流程/步骤要走可写的那份。
            if (_workspace is not IWorkspaceManager manager)
            {
                ActionMessage = "当前环境不支持跳转到步骤（没有可写工作区）";
                return;
            }

            manager.SwitchFlow(flow);
            manager.SwitchStep(step);
            // 资产页是弹窗：不关掉的话用户看不到刚选中的步骤。
            RequestClose.Invoke(ButtonResult.OK);
        }

        /// <summary>容器步骤（If/For/While/Parallel）的子步骤也要能找到——引用可能来自分支内部。</summary>
        private static StepModel? FindStepDeep(IEnumerable<StepModel> steps, string name)
        {
            foreach (var step in steps)
            {
                if (string.Equals(step.StepName, name, StringComparison.Ordinal)) return step;
                if (step is not IContainerStep container) continue;

                foreach (var branch in container.Children)
                {
                    var found = FindStepDeep(branch.Steps, name);
                    if (found != null) return found;
                }
            }

            return null;
        }

        private void ApplyFilter()
        {
            var source = _allAssets.Where(a => _selectedType == "all" || a.Kind == _selectedType).ToList();

            if (!string.IsNullOrWhiteSpace(LifecycleFilter) && LifecycleFilter != "全部")
                source = source.Where(a => LifecycleGroupOf(a) == LifecycleFilter).ToList();

            if (!string.IsNullOrWhiteSpace(CameraFilter))
                source = source.Where(a => string.Equals(a.Camera, CameraFilter, StringComparison.OrdinalIgnoreCase)).ToList();

            if (!string.IsNullOrWhiteSpace(TagFilter))
                source = source.Where(a => string.Equals(TagOf(a), TagFilter, StringComparison.OrdinalIgnoreCase)).ToList();

            var filtered = string.IsNullOrWhiteSpace(SearchText)
                ? source
                : source.Where(a => a.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || a.TypeLabel.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || a.Scope.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || a.Camera.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();

            VisibleAssets.Clear();

            // 排序只作用于展示集合，不改目录顺序（目录来自方案顺序，重排会打乱稳定 Id 观感）。
            var ordered = _sortMode == "name"
                ? filtered.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList()
                : filtered.OrderByDescending(a => a.Updated, StringComparer.Ordinal)
                          .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                          .ToList();

            foreach (var item in ordered) VisibleAssets.Add(item);

            ListTitle = _selectedType switch
            {
                "all" => "全部资产",
                "calibration" => "标定资产",
                "template" => "模板资产",
                "model" => "模型资产",
                "recipe" => "配方资产",
                _ => "图像资产"
            };
            ListCountText = $"{VisibleAssets.Count} 项";
            FooterText = $"显示 {VisibleAssets.Count} / {source.Count} 项（方案资产 {_allAssets.Count} 项）";
            RaisePropertyChanged(nameof(ListTitle));
            RaisePropertyChanged(nameof(ListCountText));
            RaisePropertyChanged(nameof(FooterText));

            if (SelectedAsset == null || !VisibleAssets.Contains(SelectedAsset))
                SelectedAsset = VisibleAssets.FirstOrDefault();

            RefreshHealth(source);
        }

        /// <summary>
        /// 把资产自身的 Status 归到左侧三个桶里。**筛选与计数必须共用这一份判据**，
        /// 否则会出现"点『待处理』筛出 0 条、而计数写着 3"这种自相矛盾。
        /// </summary>
        private static string LifecycleGroupOf(ProjectAssetItem item) => item.Status switch
        {
            "有效" or "派生" or "内嵌" => "有效",
            "待复核" or "待学习" => "待处理",
            "已归档" => "已归档",
            _ => "其他",
        };

        private void RefreshHealth(IReadOnlyCollection<ProjectAssetItem> source)
        {
            var total = source.Count;
            var valid = source.Count(a => LifecycleGroupOf(a) == "有效");
            var pending = source.Count(a => LifecycleGroupOf(a) == "待处理");
            var archived = source.Count(a => LifecycleGroupOf(a) == "已归档");
            var unbound = source.Count(a => a.Status == "未配置" || a.Status == "缺失");
            var locked = source.Count(a => a.IsFrozen);
            var pct = total == 0 ? 0 : (int)Math.Round(valid * 100.0 / total);
            HealthPercent = pct + "%";
            HealthText = $"{valid} 有效 / {total} · {pending} 待处理\n{archived} 归档 · {unbound} 未绑定 · {locked} 冻结";
            ValidCount = valid;
            PendingCount = pending;
            ArchivedCount = archived;
            LockedCount = locked;
            RaisePropertyChanged(nameof(HealthPercent));
            RaisePropertyChanged(nameof(HealthText));
            RaisePropertyChanged(nameof(ValidCount));
            RaisePropertyChanged(nameof(PendingCount));
            RaisePropertyChanged(nameof(ArchivedCount));
            RaisePropertyChanged(nameof(LockedCount));
        }

        private void RefreshCounts()
        {
            foreach (var tab in TypeTabs)
                tab.Count = tab.Kind == "all" ? _allAssets.Count : _allAssets.Count(a => a.Kind == tab.Kind);
            foreach (var filter in TypeFilters)
                filter.Count = _allAssets.Count(a => a.Kind == filter.Kind);
        }

        private void ImportAsset()
        {
            if (_workspace.CurrentSolution == null)
            {
                ActionMessage = "当前没有打开方案";
                return;
            }

            var kind = _selectedType;
            if (kind != "template" && kind != "model" && kind != "image" && kind != "recipe")
            {
                ActionMessage = "请切换到模板 / 模型 / 图像 / 配方页签再导入";
                return;
            }

            // 导入也是资产写操作：先过同一道权限闸门，避免"能导不能冻结"的分叉。
            if (_assetService == null)
            {
                ActionMessage = "当前没有资产写服务；请先打开方案";
                return;
            }

            if (!_assetService.CheckWritePermission("导入资产", out var denyReason))
            {
                ActionMessage = denyReason;
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = "导入" + (kind == "template" ? "模板" : kind == "model" ? "模型" : kind == "recipe" ? "文本配方" : "图像") + "资产",
                Filter = FilterFor(kind),
                CheckFileExists = true,
                Multiselect = false,
            };
            if (dialog.ShowDialog() != true) return;

            if (!_importer.TryImport(dialog.FileName, kind, out var assetId, out var error))
            {
                ActionMessage = error;
                return;
            }

            // 导入即内容变化：写一条版本 + 审计；失败不回滚已复制的文件，但会明确提示。
            // 导入器已经把版本置成 1，这里**不能再递增**——否则版本历史第一条就写 v2，而 v2 文件不存在。
            if (!_assetService.TryRecordVersion(assetId, "导入", "文件：" + Path.GetFileName(dialog.FileName),
                    bumpVersion: false, out var versionError))
            {
                ActionMessage = "已导入文件，但版本记录失败：" + versionError;
            }

            ReloadAssets();
            SelectType(kind);
            SelectedAsset = _allAssets.FirstOrDefault(a => a.Id == assetId.ToString());
            ActionMessage = $"已导入：{SelectedAsset?.Name ?? dialog.FileName}；保存方案后随 .vms 落盘";
        }

        private static string FilterFor(string kind) => kind switch
        {
            "image" => "图片 (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件 (*.*)|*.*",
            "template" => "模板文件 (*.json;*.tpl;*.tmpl;*.png;*.jpg;*.bmp;*.xml;*.yaml)|*.json;*.tpl;*.tmpl;*.png;*.jpg;*.bmp;*.xml;*.yaml|所有文件 (*.*)|*.*",
            "model" => "模型文件 (*.onnx;*.pt;*.pth;*.bin;*.model;*.json;*.engine;*.trt)|*.onnx;*.pt;*.pth;*.bin;*.model;*.json;*.engine;*.trt|所有文件 (*.*)|*.*",
            "recipe" => "文本配方 (*.txt;*.csv;*.tbl)|*.txt;*.csv;*.tbl|所有文件 (*.*)|*.*",
            _ => "所有文件 (*.*)|*.*",
        };

        private void ToggleArchive()
        {
            if (SelectedAsset == null) return;
            if (_assetService == null || !Guid.TryParse(SelectedAsset.Id, out var assetId))
            {
                ActionMessage = "当前页面没有资产写服务，无法归档/恢复";
                return;
            }

            var archived = SelectedAsset.Status == "已归档";
            if (!_assetService.TrySetArchived(assetId, !archived, out var error))
            {
                ActionMessage = error;
                return;
            }

            var kind = SelectedAsset.Kind;
            ReloadAssets();
            SelectType(kind);
            ActionMessage = archived
                ? $"已恢复：{SelectedAsset?.Name}；保存方案后随 .vms 落盘"
                : $"已归档：{SelectedAsset?.Name}（文件保留）；保存方案后随 .vms 落盘";
        }

        private void DeleteAsset()
        {
            if (SelectedAsset == null) return;
            if (_assetService == null || !Guid.TryParse(SelectedAsset.Id, out var assetId))
            {
                ActionMessage = "当前页面没有资产写服务，无法删除";
                return;
            }

            // 删除是不可逆的方案内操作（文件仍在磁盘上），必须二次确认。
            var confirm = MessageBox.Show(
                $"从方案中移除资产「{SelectedAsset.Name}」？\n\n" +
                "只移除方案记录，磁盘文件保留。默认指针与生命周期记录会一起清掉，历史版本记录保留。",
                "删除资产",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.OK) return;

            if (!_assetService.TryDelete(assetId, out var filePath, out var error))
            {
                ActionMessage = error;
                return;
            }

            var kind = SelectedAsset.Kind;
            ReloadAssets();
            SelectType(kind);
            ActionMessage = string.IsNullOrWhiteSpace(filePath)
                ? "已从方案移除；保存方案后随 .vms 落盘"
                : $"已从方案移除，文件保留：{filePath}；保存方案后随 .vms 落盘";
        }

        private void RefreshAudit()
        {
            AuditRows.Clear();

            if (SelectedAsset == null)
            {
                AuditHint = "先选中一个资产";
                return;
            }

            try
            {
                var result = _auditReader.Read();
                var name = SelectedAsset.Name;
                var kindLabel = SelectedAsset.Kind;

                // 审计行只记"对象名"，没有资产 Id：按名字 + 类型标签匹配，
                // 同名不同类可能串（已知边界，界面上标明匹配口径）。
                var rows = result.Rows
                    .Where(r => string.Equals(r.Subject, name, StringComparison.Ordinal))
                    .OrderByDescending(r => r.Timestamp)
                    .Take(200)
                    .ToList();

                foreach (var row in rows)
                {
                    AuditRows.Add(new AssetAuditRow(
                        row.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                        row.Actor,
                        row.ActionText,
                        row.OutcomeText,
                        row.DetailRemainder,
                        row.OutcomeText));
                }

                AuditHint = rows.Count == 0
                    ? $"没有查到「{name}」的审计记录（目录：{_auditReader.Directory}）"
                    : $"共 {rows.Count} 条（按对象名匹配，目录：{_auditReader.Directory}）";
            }
            catch (Exception ex)
            {
                AuditHint = "读审计失败：" + ex.Message;
            }
        }

        private void SetDefault()
        {
            if (SelectedAsset == null) return;

            if (_assetService == null || !Guid.TryParse(SelectedAsset.Id, out var assetId))
            {
                ActionMessage = "当前页面没有资产写服务，无法设为默认";
                return;
            }

            if (!_assetService.TrySetDefault(assetId, out var error))
            {
                ActionMessage = error;
                return;
            }

            ReloadAssets();
            SelectType(SelectedAsset?.Kind ?? _selectedType);
            ActionMessage = $"已设为默认并记录版本：{SelectedAsset?.Name}；保存方案后随 .vms 落盘";
        }

        private void ToggleFreeze()
        {
            if (SelectedAsset == null) return;

            if (_assetService == null || !Guid.TryParse(SelectedAsset.Id, out var assetId))
            {
                ActionMessage = "当前页面没有资产写服务，无法冻结/解冻";
                return;
            }

            if (!_assetService.TryToggleFreeze(assetId, out var frozen, out var error))
            {
                ActionMessage = error;
                return;
            }

            var kind = SelectedAsset.Kind;
            ReloadAssets();
            SelectType(kind);
            ActionMessage = frozen
                ? $"已冻结：{SelectedAsset?.Name}；保存方案后随 .vms 落盘"
                : $"已解冻：{SelectedAsset?.Name}；保存方案后随 .vms 落盘";
        }

        /// <summary>给当前文件资产导入一个新版本：旧文件进版本库，活动文件换新，版本号 +1。</summary>
        private void ImportNewVersion()
        {
            if (SelectedAsset == null) return;

            var kind = SelectedAsset.Kind;
            if (kind != "template" && kind != "model" && kind != "image")
            {
                ActionMessage = "只有模板 / 模型 / 图像资产支持导入新版本";
                return;
            }

            if (_assetService == null || !Guid.TryParse(SelectedAsset.Id, out var assetId))
            {
                ActionMessage = "当前页面没有资产写服务，无法导入新版本";
                return;
            }

            if (!_assetService.CheckWritePermission("导入新版本", out var denyReason))
            {
                ActionMessage = denyReason;
                return;
            }

            if (_assetService.IsFrozen(assetId))
            {
                ActionMessage = $"资产「{SelectedAsset.Name}」已冻结，先解冻再导入新版本";
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = "导入新版本：" + SelectedAsset.Name,
                Filter = FilterFor(kind),
                CheckFileExists = true,
                Multiselect = false,
            };
            if (dialog.ShowDialog() != true) return;

            if (!_importer.TryImportNewVersion(assetId, dialog.FileName, out var newVersion, out var error))
            {
                ActionMessage = error;
                return;
            }

            _assetService.TryRecordVersion(assetId, "新版本",
                $"v{newVersion} ← {Path.GetFileName(dialog.FileName)}", bumpVersion: false, out _);

            ReloadAssets();
            SelectType(kind);
            SelectedAsset = _allAssets.FirstOrDefault(a => a.Id == assetId.ToString());
            ActionMessage = $"已导入新版本 v{newVersion}（旧文件保留在 Assets\\Versions）；保存方案后随 .vms 落盘";
        }

        /// <summary>保存配方内联内容：写回 TextContent，递增内容版本并记一条版本。</summary>
        private void SaveRecipeContent()
        {
            if (SelectedAsset == null || !SelectedAsset.IsTextRecipe) return;

            var solution = _workspace.CurrentSolution;
            if (solution == null) { ActionMessage = "当前没有打开方案"; return; }
            if (_assetService == null || !Guid.TryParse(SelectedAsset.Id, out var assetId))
            {
                ActionMessage = "当前页面没有资产写服务，无法保存配方";
                return;
            }

            if (_assetService.IsFrozen(assetId))
            {
                ActionMessage = $"配方「{SelectedAsset.Name}」已冻结，先解冻再改内容";
                return;
            }

            var recipe = solution.RecipesOrEmpty().FirstOrDefault(r => r.RecipeId == assetId);
            if (recipe == null) { ActionMessage = "配方资产已不存在"; return; }

            var text = RecipeContentDraft ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                ActionMessage = "配方内容不能为空";
                return;
            }

            if (string.Equals(text, recipe.TextContent, StringComparison.Ordinal))
            {
                ActionMessage = "内容没有变化，未生成新版本";
                return;
            }

            // 胶路库必须先能解析：存一份解析不了的 JSON 等于把产线停在一个必然失败的配方上。
            if (string.Equals(recipe.ContentFormat, "bead", StringComparison.OrdinalIgnoreCase)
                && !IsValidJsonArray(text, out var jsonError))
            {
                ActionMessage = "胶路配方库不是合法 JSON 数组：" + jsonError;
                return;
            }

            recipe.TextContent = text;
            recipe.UpdatedAtUtc = DateTime.UtcNow;
            recipe.UpdatedBy = Environment.UserName;

            if (!_assetService.TryRecordVersion(assetId, "改内容", "配方内容编辑", out var versionError))
            {
                ActionMessage = "已写入内容，但版本记录失败：" + versionError;
            }

            ReloadAssets();
            SelectType("recipe");
            SelectedAsset = _allAssets.FirstOrDefault(a => a.Id == assetId.ToString());
            ActionMessage = $"配方内容已保存（v{recipe.ContentVersion}）；保存方案后随 .vms 落盘";
        }

        private static bool IsValidJsonArray(string text, out string error)
        {
            error = string.Empty;
            try
            {
                return Newtonsoft.Json.Linq.JToken.Parse(text) is Newtonsoft.Json.Linq.JArray;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private void RollbackVersion()
        {
            if (SelectedAsset == null) return;

            // 优先回滚到用户选中的那条记录；没选就退回落"最新一条历史记录"。
            var record = SelectedVersion is { CanRollback: true } picked
                ? picked
                : SelectedAsset.Versions.FirstOrDefault(v => v.CanRollback);
            if (record == null)
            {
                ActionMessage = "当前资产没有可回滚的历史版本";
                return;
            }

            if (_assetService == null || !Guid.TryParse(SelectedAsset.Id, out var assetId))
            {
                ActionMessage = "当前页面没有资产写服务，无法回滚";
                return;
            }

            if (!Guid.TryParse(record.RecordId, out var recordId))
            {
                ActionMessage = "版本记录 Id 无效";
                return;
            }

            if (!_assetService.TryRollback(assetId, recordId, out var error))
            {
                ActionMessage = error;
                return;
            }

            var kind = SelectedAsset.Kind;
            ReloadAssets();
            SelectType(kind);
            ActionMessage = $"已回滚到 {record.Version}；保存方案后随 .vms 落盘";
        }

        private void ExportAsset()
        {
            if (SelectedAsset == null) return;

            var dialog = new SaveFileDialog
            {
                Title = "导出资产包",
                Filter = "资产包 (*.zip)|*.zip",
                FileName = Sanitize(SelectedAsset.Name) + "-v" + Math.Max(1, ParseVersion(SelectedAsset.Version)) + ".zip",
                AddExtension = true,
                DefaultExt = ".zip",
            };
            if (dialog.ShowDialog() != true) return;

            if (!_exporter.TryExport(
                    new[] { ParseGuid(SelectedAsset.Id) },
                    dialog.FileName,
                    out var fileCount,
                    out _,
                    out var error))
            {
                ActionMessage = error;
                try
                {
                    // 导出失败也把清单放剪贴板：至少用户能拿到 Id/路径去手查。
                    Clipboard.SetText(SelectedAsset.Id + " " + SelectedAsset.FilePath);
                }
                catch { }
                return;
            }

            ActionMessage = fileCount > 0
                ? $"已导出 {SelectedAsset.Name}（{fileCount} 个文件）→ {dialog.FileName}"
                : $"已导出清单，但文件不存在或没有文件：{dialog.FileName}";
        }

        private static string Sanitize(string name)
        {
            var cleaned = (name ?? "asset").Trim();
            foreach (var ch in Path.GetInvalidFileNameChars())
                cleaned = cleaned.Replace(ch, '_');
            return string.IsNullOrWhiteSpace(cleaned) ? "asset" : cleaned;
        }

        private static int ParseVersion(string text)
            => int.TryParse((text ?? string.Empty).TrimStart('v', 'V'), out var v) ? v : 1;

        private static Guid ParseGuid(string text)
            => Guid.TryParse(text, out var id) ? id : Guid.Empty;

        private void CopyId()
        {
            if (SelectedAsset == null) return;
            try
            {
                Clipboard.SetText(SelectedAsset.Id);
                ActionMessage = "资产 Id 已复制";
            }
            catch
            {
                ActionMessage = "复制失败";
            }
        }
    }

    /// <summary>页内审计流水的一行（只读展示）。</summary>
    public sealed class AssetAuditRow
    {
        public AssetAuditRow(string time, string actor, string action, string outcome, string detail, string outcomeKind)
        {
            Time = time ?? string.Empty;
            Actor = actor ?? string.Empty;
            Action = action ?? string.Empty;
            Outcome = outcome ?? string.Empty;
            Detail = detail ?? string.Empty;
            OutcomeKind = outcomeKind ?? string.Empty;
        }

        public string Time { get; }
        public string Actor { get; }
        public string Action { get; }
        public string Outcome { get; }
        public string Detail { get; }
        public string OutcomeKind { get; }
    }

    /// <summary>筛选下拉项：Value 是筛选键，Label 是界面文案（空 Value = 全部）。</summary>
    public sealed class FilterOption
    {
        public FilterOption(string value, string label)
        {
            Value = value ?? string.Empty;
            Label = label ?? string.Empty;
        }

        public string Value { get; }
        public string Label { get; }

        /// <summary>
        /// 必须重写：ComboBox 的选中项（SelectionBoxItem）与下拉项都靠它出文案。
        /// 只写 DisplayMemberPath 在本仓的 DialogCombo 自定义模板下不生效 ——
        /// 界面上会出现 `VisionMaster.ViewModels.FilterOption` 这种类型全名。
        /// </summary>
        public override string ToString() => Label;
    }

    public sealed class AssetTypeTab : BindableBase    {
        private int _count;
        public AssetTypeTab(string kind, string label) { Kind = kind; Label = label; }
        public string Kind { get; }
        public string Label { get; }
        public int Count { get => _count; set => SetProperty(ref _count, value); }
    }

    public sealed class AssetTypeFilter : BindableBase
    {
        private int _count;
        private bool _isSelected;
        public AssetTypeFilter(string kind, string label) { Kind = kind; Label = label; }
        public string Kind { get; }
        public string Label { get; }
        public int Count { get => _count; set => SetProperty(ref _count, value); }
        public bool IsSelected
        {
            get => _isSelected;
            set { if (SetProperty(ref _isSelected, value)) RaisePropertyChanged(nameof(CheckGlyph)); }
        }
        public string CheckGlyph => IsSelected ? "✓" : string.Empty;
    }

    public sealed class ProjectAssetItem : BindableBase
    {
        private string _status = "有效";
        private string _lockText = "未冻结";
        private bool _isFrozen;
        private bool _isDefault;

        public string Name { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public string ShortId { get; set; } = string.Empty;
        public string Kind { get; set; } = "calibration";
        public string Ownership { get; set; } = "方案级";
        public string TypeLabel { get; set; } = string.Empty;
        public string Scope { get; set; } = string.Empty;
        public string Camera { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string ContentHash { get; set; } = string.Empty;

        public string Status
        {
            get => _status;
            set { if (SetProperty(ref _status, value)) RaisePropertyChanged(nameof(StatusKind)); }
        }

        public string LockText
        {
            get => _lockText;
            set => SetProperty(ref _lockText, value);
        }

        public string RefsSummary { get; set; } = string.Empty;
        public string Updated { get; set; } = string.Empty;
        public string UpdatedSub { get; set; } = string.Empty;
        public string IconGlyph { get; set; } = "\uf0ad";
        public string PreviewTitle { get; set; } = string.Empty;
        public string PreviewSub { get; set; } = string.Empty;
        public bool IsCalibration { get; set; }
        public bool IsNotCalibration => !IsCalibration;

        /// <summary>真缩略图（图像/图片模板有；其它为 null，界面回落图标）。</summary>
        public System.Windows.Media.ImageSource? Thumbnail { get; set; }
        public bool HasThumbnail => Thumbnail != null;
        public bool HasNoThumbnail => Thumbnail == null;

        /// <summary>配方内联内容（文本/胶路配方原文）；非配方或参数配方为空。</summary>
        public string RecipeContent { get; set; } = string.Empty;
        public string RecipeContentFormat { get; set; } = string.Empty;

        /// <summary>可编辑内联内容的配方（text / bead）。参数配方不走这里。</summary>
        public bool IsTextRecipe => Kind == "recipe"
            && !string.IsNullOrWhiteSpace(RecipeContent)
            && (RecipeContentFormat == "text" || RecipeContentFormat == "bead");

        public bool IsFrozen
        {
            get => _isFrozen;
            set
            {
                if (!SetProperty(ref _isFrozen, value)) return;
                LockText = value ? "已冻结" : "未冻结";
            }
        }

        public bool IsDefault
        {
            get => _isDefault;
            set => SetProperty(ref _isDefault, value);
        }

        public string StatusKind => Status switch
        {
            "有效" => "ok",
            "待复核" or "待学习" => "check",
            "未配置" or "缺失" => "unbound",
            _ => "arch",
        };

        public ObservableCollection<AssetFact> Overview { get; } = new();
        public ObservableCollection<AssetAssociation> Associations { get; } = new();
        public ObservableCollection<AssetTechGroup> TechGroups { get; } = new();
        public ObservableCollection<AssetReference> References { get; } = new();
        public ObservableCollection<AssetVersion> Versions { get; } = new();
        public bool HasVersions => Versions.Count > 0;
        public bool HasNoVersions => !HasVersions;
    }

    public sealed class AssetFact
    {
        public AssetFact(string key, string value) { Key = key; Value = value; }
        public string Key { get; }
        public string Value { get; }
    }

    public sealed class AssetAssociation
    {
        public AssetAssociation(string label, string value, string sub) { Label = label; Value = value; Sub = sub; }
        public string Label { get; }
        public string Value { get; }
        public string Sub { get; }
    }

    public sealed class AssetTechGroup
    {
        public AssetTechGroup(string title, string right, IEnumerable<AssetFact> fields)
        {
            Title = title; Right = right; Fields = new ObservableCollection<AssetFact>(fields);
        }
        public string Title { get; }
        public string Right { get; }
        public ObservableCollection<AssetFact> Fields { get; }
    }

    public sealed class AssetReference
    {
        public AssetReference(string kind, string title, string sub)
            : this(kind, title, sub, null, null)
        {
        }

        public AssetReference(string kind, string title, string sub, string? flowName, string? stepName)
        {
            Kind = kind;
            Title = title;
            Sub = sub;
            FlowName = flowName;
            StepName = stepName;
        }

        public string Kind { get; }
        public string Title { get; }
        public string Sub { get; }

        /// <summary>可跳转时给的原始流程名（不是显示用的友好名）与步骤名；不可跳转时为 null。</summary>
        public string? FlowName { get; }
        public string? StepName { get; }

        public bool CanNavigate => !string.IsNullOrWhiteSpace(FlowName) && !string.IsNullOrWhiteSpace(StepName);
    }

    public sealed class AssetVersion
    {
        public AssetVersion(string version, string tag, string time, string meta, string diff)
        {
            Version = version; Tag = tag; Time = time; Meta = meta; Diff = diff;
        }

        /// <summary>可回滚记录的 Id（当前只有版本历史记录有；派生版本为空）。</summary>
        public string RecordId { get; init; } = string.Empty;
        public bool CanRollback => !string.IsNullOrWhiteSpace(RecordId);
        public string Version { get; }
        public string Tag { get; }
        public string Time { get; }
        public string Meta { get; }
        public string Diff { get; }
    }
}
