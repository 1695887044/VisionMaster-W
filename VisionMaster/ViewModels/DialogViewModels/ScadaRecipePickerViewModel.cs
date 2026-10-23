using System;
using System.Collections.ObjectModel;
using System.Linq;
using Prism.Commands;
using Prism.Dialogs;
using Prism.Mvvm;
using VisionMaster.Models;
using VisionMaster.Services;

namespace VisionMaster.ViewModels.DialogViewModels
{
    /// <summary>配方选择器的一行。</summary>
    public sealed class ScadaRecipeOption
    {
        public ScadaRecipeOption(RecipeDefinition recipe, bool isActive)
        {
            Recipe = recipe;
            IsActive = isActive;
        }

        public RecipeDefinition Recipe { get; }
        public Guid Id => Recipe.RecipeId;
        public string Name => Recipe.DisplayName;
        public string Version => Recipe.DisplayVersion;
        public bool IsActive { get; }
        public string ActiveText => IsActive ? "生效中" : string.Empty;
        public string Summary => $"{Recipe.EntryCount} 条参数 · {Recipe.Code}".TrimEnd(' ', '·');
    }

    /// <summary>
    /// SCADA「选择配方」弹窗：只列当前方案里的配方，确认后回传 Id + 名字。
    /// 不在这里应用配方；应用仍由组态动作分发器走 RecipeService。
    /// </summary>
    public sealed class ScadaRecipePickerViewModel : BindableBase, IDialogAware
    {
        private readonly RecipeService _service;
        private ScadaRecipeOption? _selectedRecipe;

        public ScadaRecipePickerViewModel(RecipeService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            Recipes = new ObservableCollection<ScadaRecipeOption>();
            PickCommand = new DelegateCommand(Pick, () => SelectedRecipe != null)
                .ObservesProperty(() => SelectedRecipe);
            CancelCommand = new DelegateCommand(() => RequestClose.Invoke(ButtonResult.Cancel));
        }

        public string Title => "选择配方";

        public DialogCloseListener RequestClose { get; set; }

        public ObservableCollection<ScadaRecipeOption> Recipes { get; }

        public ScadaRecipeOption? SelectedRecipe
        {
            get => _selectedRecipe;
            set => SetProperty(ref _selectedRecipe, value);
        }

        public DelegateCommand PickCommand { get; }
        public DelegateCommand CancelCommand { get; }

        public bool CanCloseDialog() => true;

        public void OnDialogClosed()
        {
        }

        public void OnDialogOpened(IDialogParameters parameters)
        {
            var currentId = Guid.Empty;
            if (parameters != null && parameters.TryGetValue<Guid>(ScadaRecipePicker.CurrentIdKey, out var id))
                currentId = id;

            string? currentName = null;
            if (parameters != null && parameters.TryGetValue<string>(ScadaRecipePicker.CurrentNameKey, out var name))
                currentName = name;

            Recipes.Clear();
            var activeId = _service.Solution?.ActiveRecipeId;
            foreach (var recipe in _service.Recipes)
            {
                if (recipe == null) continue;
                Recipes.Add(new ScadaRecipeOption(recipe, activeId == recipe.RecipeId));
            }

            SelectedRecipe = currentId != Guid.Empty
                ? Recipes.FirstOrDefault(r => r.Id == currentId)
                : null;

            if (SelectedRecipe == null && !string.IsNullOrWhiteSpace(currentName))
            {
                SelectedRecipe = Recipes.FirstOrDefault(r =>
                    string.Equals(r.Name, currentName.Trim(), StringComparison.OrdinalIgnoreCase));
            }

            SelectedRecipe ??= Recipes.FirstOrDefault();
        }

        private void Pick()
        {
            var selected = SelectedRecipe;
            if (selected == null) return;

            var parameters = new DialogParameters
            {
                { ScadaRecipePicker.PickedIdKey, selected.Id },
                { ScadaRecipePicker.PickedNameKey, selected.Name },
            };

            RequestClose.Invoke(parameters, ButtonResult.OK);
        }
    }
}
