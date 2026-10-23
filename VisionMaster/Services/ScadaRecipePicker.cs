using System;
using Prism.Dialogs;

namespace VisionMaster.Services
{
    /// <summary>
    /// <see cref="IScadaRecipePicker"/> 的默认实现：只做「入参打包 / 出参拆包」，
    /// 界面与候选列表在 ScadaRecipePickerView / ScadaRecipePickerViewModel 里。
    /// </summary>
    public sealed class ScadaRecipePicker : IScadaRecipePicker
    {
        public const string DialogName = "ScadaRecipePicker";

        public const string CurrentIdKey = "CurrentRecipeId";
        public const string CurrentNameKey = "CurrentRecipeName";
        public const string PickedIdKey = "PickedRecipeId";
        public const string PickedNameKey = "PickedRecipeName";

        private readonly IDialogService _dialogs;

        public ScadaRecipePicker(IDialogService dialogs)
            => _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

        public void Pick(Guid currentRecipeId, string? currentName, Action<Guid, string?> onPicked)
        {
            if (onPicked == null) throw new ArgumentNullException(nameof(onPicked));

            var parameters = new DialogParameters
            {
                { CurrentIdKey, currentRecipeId },
                { CurrentNameKey, currentName },
            };

            _dialogs.ShowDialog(DialogName, parameters, result =>
            {
                if (result.Result != ButtonResult.OK) return;

                if (!result.Parameters.TryGetValue<Guid>(PickedIdKey, out var id) || id == Guid.Empty)
                    return;

                result.Parameters.TryGetValue<string>(PickedNameKey, out var name);
                onPicked(id, name);
            });
        }
    }
}
