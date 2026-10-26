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
        private readonly ProjectAssetService? _assetService;
        private readonly List<ProjectAssetItem> _allAssets = new();

        private AssetTypeTab? _selectedTypeTab;
        private ProjectAssetItem? _selectedAsset;
        private string _selectedType = "calibration";
        private string _searchText = string.Empty;
        private int _selectedDetailTabIndex;
        private string _actionMessage = string.Empty;

        public ProjectAssetsViewModel(IReadOnlyWorkspaceContext workspace, ProjectAssetService? assetService = null)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _catalog = new SolutionProjectAssetCatalog(workspace);
            _importer = new ProjectAssetImporter(workspace);
            _exporter = new ProjectAssetExporter(workspace);
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
            }
        }

        public bool HasSelectedAsset => SelectedAsset != null;

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

        public DelegateCommand<string> SelectTypeCommand { get; }
        public DelegateCommand ImportAssetCommand { get; }
        public DelegateCommand RefreshCommand { get; }
        public DelegateCommand ToggleSortCommand { get; }
        public DelegateCommand ClearFiltersCommand { get; }
        public DelegateCommand SetDefaultCommand { get; }
        public DelegateCommand ToggleFreezeCommand { get; }
        public DelegateCommand RollbackCommand { get; }
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
            ApplyFilter();
            ActionMessage = "已清除搜索 / 相机 / 标签筛选";
        }

        /// <summary>作用域首段当标签：`ST3 · 精密测量` → `ST3`，`整线` → `整线`。</summary>
        private static string TagOf(ProjectAssetItem item)
        {
            var scope = (item.Scope ?? string.Empty).Trim();
            if (scope.Length == 0) return string.Empty;
            var cut = scope.IndexOfAny(new[] { '·', ' ', '/', '\\' });
            return cut > 0 ? scope[..cut].Trim() : scope;
        }

        private void ApplyFilter()
        {
            var source = _allAssets.Where(a => _selectedType == "all" || a.Kind == _selectedType).ToList();

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

        private void RefreshHealth(IReadOnlyCollection<ProjectAssetItem> source)
        {
            var total = source.Count;
            var valid = source.Count(a => a.Status == "有效" || a.Status == "派生" || a.Status == "内嵌");
            var pending = source.Count(a => a.Status == "待复核" || a.Status == "待学习");
            var archived = source.Count(a => a.Status == "已归档");
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
            if (kind != "template" && kind != "model" && kind != "image")
            {
                ActionMessage = "请切换到模板 / 模型 / 图像页签再导入";
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
                Title = "导入" + (kind == "template" ? "模板" : kind == "model" ? "模型" : "图像") + "资产",
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
            if (!_assetService.TryRecordVersion(assetId, "导入", "文件：" + Path.GetFileName(dialog.FileName), out var versionError))
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
            _ => "所有文件 (*.*)|*.*",
        };

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

        private void RollbackVersion()
        {
            if (SelectedAsset == null) return;

            var record = SelectedAsset.Versions.FirstOrDefault(v => v.CanRollback);
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
        public AssetReference(string kind, string title, string sub) { Kind = kind; Title = title; Sub = sub; }
        public string Kind { get; }
        public string Title { get; }
        public string Sub { get; }
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
