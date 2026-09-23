using System;
using Prism.Dialogs;

namespace VisionMaster.Services
{
    /// <summary>
    /// <see cref="IScadaPagePicker"/> 的默认实现：把"入参打包 / 出参拆包"这一段接在 Prism 弹窗上。
    ///
    /// 与 <see cref="ScadaVariablePicker"/> 逐字同构（本类不含任何界面逻辑，只做翻译）：
    /// 调用方给一对 (Id, 名字)，本类转成弹窗参数；弹窗回一对 (Id, 名字)，本类转成一次回调。
    /// 弹窗自己长什么样、有什么列，全在 <c>ScadaPagePickerView</c> / <c>ScadaPagePickerViewModel</c> 里。
    /// </summary>
    public sealed class ScadaPagePicker : IScadaPagePicker
    {
        /// <summary>弹窗注册名（<c>App.RegisterTypes</c> 里 RegisterDialog 用的键，两处必须一致）</summary>
        public const string DialogName = "ScadaPagePicker";

        /// <summary>入参键：打开时要预选的目标画面</summary>
        public const string CurrentIdKey = "CurrentPageId";
        public const string CurrentNameKey = "CurrentPageName";

        /// <summary>出参键：用户确认后选中的画面</summary>
        public const string PickedIdKey = "PickedPageId";
        public const string PickedNameKey = "PickedPageName";

        private readonly IDialogService _dialogs;

        public ScadaPagePicker(IDialogService dialogs)
            => _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));

        /// <inheritdoc />
        public void Pick(Guid currentId, string? currentName, Action<Guid, string?> onPicked)
        {
            if (onPicked == null) throw new ArgumentNullException(nameof(onPicked));

            var parameters = new DialogParameters
            {
                { CurrentIdKey, currentId },
                { CurrentNameKey, currentName },
            };

            _dialogs.ShowDialog(DialogName, parameters, result =>
            {
                // 取消 = 什么都不做。这里刻意<b>不</b>回写一个空值：取消的语义是"当我没点过"，
                // 而不是"清掉原来选的那个"——后者是破坏性动作，不能藏在取消键里。
                if (result.Result != ButtonResult.OK) return;

                // 只有 Id 是权威的：拿不到就整条放弃，绝不退化成"按名字猜一个"。
                if (!result.Parameters.TryGetValue<Guid>(PickedIdKey, out var id)) return;

                result.Parameters.TryGetValue<string>(PickedNameKey, out var name);
                onPicked(id, name);
            });
        }
    }
}
