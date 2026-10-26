using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Services;

namespace VisionMaster.ViewModels
{
    /// <summary>
    /// 项目资产管理：读取当前方案的真实标定/模板/模型/配方/图像；
    /// 模板/模型/图像支持导入到方案目录，保存方案后随 .vms 落盘。
    /// </summary>
    public sealed class ProjectAssetsViewModel : BindableBase, IDialogAware
    {
        private readonly IReadOnlyWorkspaceContext _workspace;
        private readonly SolutionProjectAssetCatalog _catalog;
        private readonly ProjectAssetImporter _importer;
        private readonly List<ProjectAssetItem> _allAssets = new();

        private AssetTypeTab? _selectedTypeTab;
        private ProjectAssetItem? _selectedAsset;
        private string _selectedType = "calibration";
        private string _searchText = string.Empty;
        private int _selectedDetailTabIndex;
        private string _actionMessage = string.Empty;

        public ProjectAssetsViewModel(IReadOnlyWorkspaceContext workspace)
        {
            _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
            _catalog = new SolutionProjectAssetCatalog(workspace);
            _importer = new ProjectAssetImporter(workspace);

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
            SetDefaultCommand = new DelegateCommand(SetDefault);
            ToggleFreezeCommand = new DelegateCommand(ToggleFreeze);
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
        public DelegateCommand SetDefaultCommand { get; }
        public DelegateCommand ToggleFreezeCommand { get; }
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
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var source = _allAssets.Where(a => _selectedType == "all" || a.Kind == _selectedType).ToList();
            var filtered = string.IsNullOrWhiteSpace(SearchText)
                ? source
                : source.Where(a => a.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || a.TypeLabel.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || a.Scope.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                    || a.Camera.Contains(SearchText, StringComparison.OrdinalIgnoreCase)).ToList();

            VisibleAssets.Clear();
            foreach (var item in filtered) VisibleAssets.Add(item);

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
            foreach (var item in _allAssets.Where(a => a.Kind == SelectedAsset.Kind && a.Scope == SelectedAsset.Scope))
                item.IsDefault = false;
            SelectedAsset.IsDefault = true;
            ActionMessage = $"已标记为当前会话默认：{SelectedAsset.Name}（默认资产持久化未接入）";
        }

        private void ToggleFreeze()
        {
            ActionMessage = "冻结/回滚将在资产生命周期阶段接入；当前只读展示真实数据";
        }

        private void ExportAsset()
        {
            if (SelectedAsset == null) return;
            var text = new StringBuilder()
                .AppendLine("项目资产")
                .AppendLine("Name: " + SelectedAsset.Name)
                .AppendLine("Id: " + SelectedAsset.Id)
                .AppendLine("Kind: " + SelectedAsset.Kind)
                .AppendLine("Version: " + SelectedAsset.Version)
                .AppendLine("Status: " + SelectedAsset.Status)
                .AppendLine("Source: " + SelectedAsset.SourcePath)
                .AppendLine("File: " + SelectedAsset.FilePath)
                .AppendLine("Hash: " + SelectedAsset.ContentHash)
                .ToString();
            try
            {
                Clipboard.SetText(text);
                ActionMessage = "资产元数据已复制到剪贴板";
            }
            catch
            {
                ActionMessage = "复制失败";
            }
        }

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

    public sealed class AssetTypeTab : BindableBase
    {
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
        public string Version { get; }
        public string Tag { get; }
        public string Time { get; }
        public string Meta { get; }
        public string Diff { get; }
    }
}
