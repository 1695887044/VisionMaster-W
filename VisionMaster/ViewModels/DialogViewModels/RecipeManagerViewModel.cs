using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>配方列表的一行（把“是否生效”这个 UI 态从模型里隔离出来）。</summary>
    public sealed class RecipeListItem
    {
        public RecipeListItem(RecipeDefinition recipe, bool isActive)
        {
            Recipe = recipe;
            IsActive = isActive;
        }

        public RecipeDefinition Recipe { get; }
        public string Name => Recipe.DisplayName;
        public string Version => Recipe.DisplayVersion;
        public int EntryCount => Recipe.EntryCount;
        public bool IsActive { get; }
        public string ActiveText => IsActive ? "生效中" : string.Empty;
        public string SubText => $"{Recipe.EntryCount} 条参数 · {Recipe.Code}".TrimEnd(' ', '·');
    }

    /// <summary>参数表的一行：配方值 / 当前值 / 差异状态。</summary>
    public sealed class RecipeParameterRow
    {
        public RecipeTargetKind Kind { get; init; }
        public Guid TargetId { get; init; }
        public string Target { get; init; } = string.Empty;
        public string Key { get; init; } = string.Empty;
        public string DeclaredType { get; init; } = string.Empty;
        public string RecipeValue { get; init; } = string.Empty;
        public string CurrentValue { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public bool IsDifferent { get; init; }
    }

    /// <summary>应用确认表的一行（与参数表分开：确认表要显示中文状态）。</summary>
    public sealed class RecipePreviewRow
    {
        public string Target { get; init; } = string.Empty;
        public string Key { get; init; } = string.Empty;
        public string Current { get; init; } = string.Empty;
        public string Recipe { get; init; } = string.Empty;
        public string StatusText { get; init; } = string.Empty;
        public bool IsDifferent { get; init; }
    }

    /// <summary>
    /// 配方管理器（v1：列表 + 参数对照 + 捕获/应用/应用并保存/删除/重命名/清除生效）。
    /// 所有写操作都走 <see cref="RecipeService"/>；本 VM 不直接改步骤参数。
    /// </summary>
    public sealed class RecipeManagerViewModel : BindableBase, IDialogAware
    {
        private readonly RecipeService _service;
        private readonly RecipeSaveCoordinator _saveCoordinator;
        private readonly RecipeFileService _fileService;

        private RecipeListItem? _selectedItem;
        private RecipeParameterRow? _selectedParameter;
        private RecipeCandidate? _selectedCandidate;
        private string _newRecipeName = string.Empty;
        private string _newRecipeCode = string.Empty;
        private string _newRecipeNotes = string.Empty;
        private string _renameName = string.Empty;
        private string _statusText = string.Empty;
        private string _previewSummary = string.Empty;
        private bool _statusIsError;
        private bool _isBusy;
        private bool _isConfirmingApply;
        private bool _previewCanApply;
        private bool _confirmSaveAfter;

        public RecipeManagerViewModel(
            RecipeService service,
            RecipeSaveCoordinator saveCoordinator,
            RecipeFileService fileService)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _saveCoordinator = saveCoordinator ?? throw new ArgumentNullException(nameof(saveCoordinator));
            _fileService = fileService ?? throw new ArgumentNullException(nameof(fileService));

            RecipeItems = new ObservableCollection<RecipeListItem>();
            Parameters = new ObservableCollection<RecipeParameterRow>();
            Candidates = new ObservableCollection<RecipeCandidate>();
            PreviewItems = new ObservableCollection<RecipePreviewRow>();

            CaptureCommand = new DelegateCommand(Capture, () => !IsBusy && !string.IsNullOrWhiteSpace(NewRecipeName))
                .ObservesProperty(() => IsBusy)
                .ObservesProperty(() => NewRecipeName);
            ApplyCommand = new DelegateCommand(BeginPreviewApply, CanApply)
                .ObservesProperty(() => SelectedItem)
                .ObservesProperty(() => IsBusy)
                .ObservesProperty(() => IsConfirmingApply);
            ApplyAndSaveCommand = new DelegateCommand(BeginPreviewApplyAndSave, CanApply)
                .ObservesProperty(() => SelectedItem)
                .ObservesProperty(() => IsBusy)
                .ObservesProperty(() => IsConfirmingApply);
            ConfirmApplyCommand = new DelegateCommand(ConfirmApply, () => IsConfirmingApply && PreviewCanApply)
                .ObservesProperty(() => IsConfirmingApply)
                .ObservesProperty(() => PreviewCanApply)
                .ObservesProperty(() => IsBusy);
            CancelPreviewCommand = new DelegateCommand(CancelPreview, () => IsConfirmingApply)
                .ObservesProperty(() => IsConfirmingApply);
            AddCandidateCommand = new DelegateCommand(AddCandidate, () => !IsBusy && SelectedCandidate != null)
                .ObservesProperty(() => SelectedCandidate)
                .ObservesProperty(() => IsBusy);
            RemoveParameterCommand = new DelegateCommand(RemoveParameter, () => !IsBusy && SelectedParameter != null)
                .ObservesProperty(() => SelectedParameter)
                .ObservesProperty(() => IsBusy);
            DeleteCommand = new DelegateCommand(Delete, CanApply)
                .ObservesProperty(() => SelectedItem)
                .ObservesProperty(() => IsBusy);
            RenameCommand = new DelegateCommand(Rename, CanRename)
                .ObservesProperty(() => SelectedItem)
                .ObservesProperty(() => RenameName)
                .ObservesProperty(() => IsBusy);
            ClearActiveCommand = new DelegateCommand(ClearActive, () => !IsBusy)
                .ObservesProperty(() => IsBusy);
            ExportCommand = new DelegateCommand(Export, CanApply)
                .ObservesProperty(() => SelectedItem)
                .ObservesProperty(() => IsBusy);
            ImportCommand = new DelegateCommand(Import, () => !IsBusy)
                .ObservesProperty(() => IsBusy);
            RefreshCommand = new DelegateCommand(Refresh, () => !IsBusy).ObservesProperty(() => IsBusy);
            CloseCommand = new DelegateCommand(() => RequestClose.Invoke(ButtonResult.OK));

            Refresh();
        }

        public string Title => "配方管理";

        public DialogCloseListener RequestClose { get; set; }

        public ObservableCollection<RecipeListItem> RecipeItems { get; }
        public ObservableCollection<RecipeParameterRow> Parameters { get; }
        public ObservableCollection<RecipeCandidate> Candidates { get; }
        public ObservableCollection<RecipePreviewRow> PreviewItems { get; }

        public DelegateCommand CaptureCommand { get; }
        public DelegateCommand ApplyCommand { get; }
        public DelegateCommand ApplyAndSaveCommand { get; }
        public DelegateCommand ConfirmApplyCommand { get; }
        public DelegateCommand CancelPreviewCommand { get; }
        public DelegateCommand AddCandidateCommand { get; }
        public DelegateCommand RemoveParameterCommand { get; }
        public DelegateCommand DeleteCommand { get; }
        public DelegateCommand RenameCommand { get; }
        public DelegateCommand ClearActiveCommand { get; }
        public DelegateCommand ExportCommand { get; }
        public DelegateCommand ImportCommand { get; }
        public DelegateCommand RefreshCommand { get; }
        public DelegateCommand CloseCommand { get; }

        public RecipeListItem? SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (!SetProperty(ref _selectedItem, value)) return;
                RenameName = value?.Recipe.Name ?? string.Empty;
                CancelPreview();
                RaisePropertyChanged(nameof(SelectedRecipe));
                RaisePropertyChanged(nameof(SelectedTitle));
                RaisePropertyChanged(nameof(SelectedSubTitle));
                RefreshParameters();
            }
        }

        public RecipeParameterRow? SelectedParameter
        {
            get => _selectedParameter;
            set => SetProperty(ref _selectedParameter, value);
        }

        public RecipeCandidate? SelectedCandidate
        {
            get => _selectedCandidate;
            set => SetProperty(ref _selectedCandidate, value);
        }

        public bool IsConfirmingApply
        {
            get => _isConfirmingApply;
            private set => SetProperty(ref _isConfirmingApply, value);
        }

        public bool PreviewCanApply
        {
            get => _previewCanApply;
            private set => SetProperty(ref _previewCanApply, value);
        }

        public string PreviewSummary
        {
            get => _previewSummary;
            private set => SetProperty(ref _previewSummary, value);
        }

        public RecipeDefinition? SelectedRecipe => SelectedItem?.Recipe;

        public string SelectedTitle => SelectedRecipe?.DisplayName ?? "未选择配方";

        public string SelectedSubTitle
        {
            get
            {
                var recipe = SelectedRecipe;
                if (recipe == null) return "从左侧选择一份配方，或从当前值捕获一份新配方";
                var applied = recipe.LastAppliedAtUtc.HasValue
                    ? $"最后应用 {recipe.LastAppliedAtUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm}"
                    : "尚未应用";
                return $"{recipe.DisplayVersion} · {recipe.EntryCount} 条参数 · {applied}";
            }
        }

        public string NewRecipeName
        {
            get => _newRecipeName;
            set => SetProperty(ref _newRecipeName, value);
        }

        public string NewRecipeCode
        {
            get => _newRecipeCode;
            set => SetProperty(ref _newRecipeCode, value);
        }

        public string NewRecipeNotes
        {
            get => _newRecipeNotes;
            set => SetProperty(ref _newRecipeNotes, value);
        }

        public string RenameName
        {
            get => _renameName;
            set => SetProperty(ref _renameName, value);
        }

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        public bool StatusIsError
        {
            get => _statusIsError;
            private set
            {
                if (!SetProperty(ref _statusIsError, value)) return;
                RaisePropertyChanged(nameof(StatusBrush));
            }
        }

        public Brush StatusBrush => StatusIsError ? Brushes.OrangeRed : Brushes.ForestGreen;

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (!SetProperty(ref _isBusy, value)) return;
                RaisePropertyChanged(nameof(IsRunLocked));
                RaisePropertyChanged(nameof(RunLockText));
            }
        }

        public bool IsRunLocked => _service.IsAnyFlowRunning();

        public string RunLockText => IsRunLocked
            ? "流程运行中（含暂停）：应用被禁用，请先停止"
            : "停止态：可应用配方";

        public void Refresh()
        {
            var selectedId = SelectedItem?.Recipe.RecipeId;
            var activeId = _service.Solution?.ActiveRecipeId;

            RecipeItems.Clear();
            foreach (var recipe in _service.Recipes)
            {
                if (recipe == null) continue;
                RecipeItems.Add(new RecipeListItem(recipe, activeId == recipe.RecipeId));
            }

            SelectedItem = RecipeItems.FirstOrDefault(i => i.Recipe.RecipeId == selectedId)
                           ?? RecipeItems.FirstOrDefault();
            RaisePropertyChanged(nameof(IsRunLocked));
            RaisePropertyChanged(nameof(RunLockText));
            RefreshParameters();
        }

        private void RefreshParameters()
        {
            Parameters.Clear();
            SelectedParameter = null;
            var recipe = SelectedRecipe;
            if (recipe == null)
            {
                Candidates.Clear();
                SelectedCandidate = null;
                return;
            }

            var report = _service.Compare(recipe.RecipeId);
            var byKey = report.Items.ToDictionary(
                i => i.TargetId + "\n" + i.Key,
                i => i,
                StringComparer.Ordinal);

            foreach (var entry in recipe.Entries ?? new List<RecipeEntry>())
            {
                if (entry == null) continue;
                byKey.TryGetValue(entry.TargetId + "\n" + entry.Key, out var item);
                Parameters.Add(new RecipeParameterRow
                {
                    Kind = entry.Kind,
                    TargetId = entry.TargetId,
                    Target = entry.TargetName,
                    Key = entry.Key,
                    DeclaredType = entry.DeclaredType,
                    RecipeValue = entry.DisplayValue,
                    CurrentValue = string.IsNullOrEmpty(item?.Current) ? "—" : item!.Current,
                    Status = DisplayStatus(item?.Status ?? RecipeDeviationStatus.ValueDiff),
                    IsDifferent = item == null || item.Status != RecipeDeviationStatus.Equal,
                });
            }

            RefreshCandidates();
        }

        private void RefreshCandidates()
        {
            Candidates.Clear();
            SelectedCandidate = null;
            var recipe = SelectedRecipe;
            if (recipe == null) return;

            foreach (var candidate in _service.GetCandidates(recipe.RecipeId))
                Candidates.Add(candidate);
        }

        private static string DisplayStatus(RecipeDeviationStatus status) => status switch
        {
            RecipeDeviationStatus.Equal => "一致",
            RecipeDeviationStatus.ValueDiff => "不同",
            RecipeDeviationStatus.TargetMissing => "目标缺失",
            RecipeDeviationStatus.TypeChanged => "类型变化",
            RecipeDeviationStatus.LinkedNow => "已链接",
            RecipeDeviationStatus.TriggerSource => "触发源",
            _ => "未应用",
        };

        private bool CanApply() => !IsBusy && !IsConfirmingApply && SelectedRecipe != null;

        private bool CanRename() => !IsBusy && SelectedRecipe != null && !string.IsNullOrWhiteSpace(RenameName);

        private void Capture()
        {
            var result = _service.Capture(NewRecipeName, NewRecipeCode, NewRecipeNotes);
            SetStatus(result.Message, !result.Success);
            if (!result.Success) return;

            NewRecipeName = string.Empty;
            NewRecipeCode = string.Empty;
            NewRecipeNotes = string.Empty;
            Refresh();
            if (result.Recipe != null)
                SelectedItem = RecipeItems.FirstOrDefault(i => i.Recipe.RecipeId == result.Recipe.RecipeId);
        }

        private void BeginPreviewApply() => BeginPreview(saveAfter: false);

        private void BeginPreviewApplyAndSave() => BeginPreview(saveAfter: true);

        private void BeginPreview(bool saveAfter)
        {
            var recipe = SelectedRecipe;
            if (recipe == null) return;

            var preview = _service.Preview(recipe.RecipeId);
            _confirmSaveAfter = saveAfter;

            PreviewItems.Clear();
            foreach (var item in preview.Items)
            {
                PreviewItems.Add(new RecipePreviewRow
                {
                    Target = item.Target,
                    Key = item.Key,
                    Current = string.IsNullOrEmpty(item.Current) ? "—" : item.Current,
                    Recipe = string.IsNullOrEmpty(item.Recipe) ? "—" : item.Recipe,
                    StatusText = DisplayStatus(item.Status),
                    IsDifferent = item.Status != RecipeDeviationStatus.Equal,
                });
            }

            PreviewSummary = $"共 {preview.Total} 条 · 修改 {preview.Modified} · 不变 {preview.Unchanged}"
                             + $" · 错误 {preview.Errors} · 警告 {preview.Warnings}";
            PreviewCanApply = preview.CanApply;
            IsConfirmingApply = true;

            if (!preview.CanApply)
                SetStatus("预检有错误，整单拒绝；请先修正配方后再应用", true);
            else
                SetStatus(saveAfter ? "确认后将应用并保存方案" : "确认后将应用（只写内存，不保存）", false);
        }

        private async void ConfirmApply()
        {
            var recipe = SelectedRecipe;
            if (recipe == null) return;

            IsBusy = true;
            try
            {
                var apply = _service.Apply(recipe.RecipeId);
                if (!apply.Success)
                {
                    SetStatus(apply.Message, true);
                    return;
                }

                if (_confirmSaveAfter)
                {
                    var save = await _saveCoordinator.SaveToCurrentPathAsync();
                    SetStatus(
                        apply.Message + "；" + (save.Success ? "方案已保存" : "保存失败：" + save.Message),
                        !save.Success);
                }
                else
                {
                    SetStatus(apply.Message, false);
                }
            }
            catch (Exception ex)
            {
                SetStatus("应用失败：" + ex.Message, true);
            }
            finally
            {
                IsBusy = false;
                IsConfirmingApply = false;
                Refresh();
            }
        }

        private void CancelPreview()
        {
            IsConfirmingApply = false;
            PreviewCanApply = false;
            PreviewItems.Clear();
            PreviewSummary = string.Empty;
        }

        private void AddCandidate()
        {
            var recipe = SelectedRecipe;
            var candidate = SelectedCandidate;
            if (recipe == null || candidate == null) return;

            var ok = _service.AddEntry(recipe.RecipeId, candidate.Kind, candidate.TargetId, candidate.Key, out var error);
            SetStatus(ok ? $"已添加参数「{candidate.DisplayName}」" : error, !ok);
            Refresh();
        }

        private void RemoveParameter()
        {
            var recipe = SelectedRecipe;
            var parameter = SelectedParameter;
            if (recipe == null || parameter == null) return;

            var ok = _service.RemoveEntry(recipe.RecipeId, parameter.Kind, parameter.TargetId, parameter.Key, out var error);
            SetStatus(ok ? $"已删除参数「{parameter.Target}.{parameter.Key}」" : error, !ok);
            Refresh();
        }

        private void Delete()
        {
            var recipe = SelectedRecipe;
            if (recipe == null) return;

            var answer = MessageBox.Show(
                $"删除配方「{recipe.Name}」？\n\n只删除方案内的配方定义，不删除 .vms 文件；当前参数值保持现状。",
                "删除配方",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;

            var ok = _service.Delete(recipe.RecipeId, out var error);
            SetStatus(ok ? $"已删除配方「{recipe.Name}」" : error, !ok);
            Refresh();
        }

        private void Rename()
        {
            var recipe = SelectedRecipe;
            if (recipe == null) return;

            var ok = _service.Rename(recipe.RecipeId, RenameName, out var error);
            SetStatus(ok ? $"已重命名为「{RenameName}」" : error, !ok);
            Refresh();
        }

        private void ClearActive()
        {
            var ok = _service.ClearActive(out var error);
            SetStatus(ok ? "已清除生效指针；参数值保持现状" : error, !ok);
            Refresh();
        }

        private void Export()
        {
            var recipe = SelectedRecipe;
            if (recipe == null) return;

            var dialog = new SaveFileDialog
            {
                Filter = "VisionMaster配方 (*.vmrecipe)|*.vmrecipe|所有文件 (*.*)|*.*",
                Title = "导出配方",
                DefaultExt = ".vmrecipe",
                FileName = recipe.Name + ".vmrecipe",
            };
            if (dialog.ShowDialog() != true) return;

            var ok = _fileService.Export(recipe.RecipeId, dialog.FileName, out var error);
            SetStatus(ok ? $"已导出到 {dialog.FileName}" : error, !ok);
        }

        private void Import()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "VisionMaster配方 (*.vmrecipe)|*.vmrecipe|所有文件 (*.*)|*.*",
                Title = "导入配方",
                CheckFileExists = true,
            };
            if (dialog.ShowDialog() != true) return;

            var ok = _fileService.Import(dialog.FileName, out var imported, out var error);
            SetStatus(ok
                ? $"已导入配方「{imported?.Name}」（未应用；请核对目标后手动应用）"
                : error, !ok);
            Refresh();
            if (ok && imported != null)
                SelectedItem = RecipeItems.FirstOrDefault(i => i.Recipe.RecipeId == imported.RecipeId);
        }

        private void SetStatus(string text, bool isError)
        {
            StatusText = text ?? string.Empty;
            StatusIsError = isError;
        }

        public bool CanCloseDialog() => !IsBusy;

        public void OnDialogClosed()
        {
        }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            Refresh();
        }
    }
}
